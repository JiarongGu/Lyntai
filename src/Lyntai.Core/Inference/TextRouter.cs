using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference;

/// <summary>Fallback router over the DI collection of <see cref="IModelProvider"/>s (design §6).
/// Behavior is driven by <see cref="RoutingPolicy"/> (<see cref="LyntaiOptions.Routing"/>): the
/// defaults reproduce §6 exactly, so an untouched policy behaves as documented.</summary>
/// <param name="providers">The registered backends. First registration wins on a duplicate id.</param>
/// <param name="deadHosts">Cooldown bookkeeping — the same tracker the generation router uses. A chat key is
/// the BARE provider/configuration identity (plus <c>::model</c> under
/// <see cref="CooldownScope.ProviderAndModel"/>); generation prefixes its own keys <c>generation::</c>. One
/// unprefixed namespace and one prefixed one cannot collide, so a chat outage never benches a media backend
/// sharing its id — and a host pre-benching a chat backend by hand must use the bare identity.</param>
/// <param name="options">Platform options; <see cref="LyntaiOptions.Routing"/> supplies the fallback policy,
/// retry budgets and cooldown granularity.</param>
/// <param name="logger">Null = no logging.</param>
/// <param name="modelRouting">Live per-consumer routes, read once per call and once per capability probe
/// (<see cref="GetCapabilitiesAsync"/>); a consumer's route replaces the candidates the call was given. Null =
/// the given candidates alone.</param>
/// <param name="configuration">Which CONFIGURATION a provider is running under, used to key dead-host
/// cooldown and admission. Null (or a null return) = key cooldown on
/// <see cref="Lyntai.Inference.IProviderIdentity.Id"/> and apply no admission — correct for a
/// single-configuration deployment. Supply one when several configurations of a backend id are live at once,
/// or one tenant's rate limit benches every other tenant sharing that backend.
/// <see cref="IProviderPool{TProvider}.TryGetKey"/> is the intended source; composes with
/// <see cref="CooldownScope.ProviderAndModel"/>, which still appends the model.
///
/// <para><b>Must return a STABLE key for a given instance.</b> One routing attempt invokes it more than once
/// — cooldown key, admission, and the record that follows — so a varying answer records cooldown under a key
/// different from the one checked, producing a bench that silently never takes effect. Look the key up (as a
/// pool does); never recompute it from live state.</para></param>
/// <param name="admission">Bounds concurrent completions per configuration — for a locally-run engine where
/// simultaneous calls contend for one CPU or GPU. Null = unbounded. Applied HERE rather than by wrapping a
/// provider, so no optional capability interface a caller type-tests for is erased by a decorator.
///
/// <para><b>Completions only — <see cref="StreamAsync"/> is deliberately NOT gated.</b> A stream holds its
/// permit for the whole response, and paired with the no-fallback-after-the-first-token rule that means a
/// consumer which simply stops enumerating would hold a permit until its enumerator is finally disposed.
/// Bounding a long-lived stream needs a lease the consumer cannot forget, which this is not.</para></param>
public sealed class TextRouter(
    IEnumerable<IModelProvider> providers,
    DeadHostTracker deadHosts,
    LyntaiOptions options,
    ILogger<TextRouter>? logger = null,
    IModelRoutingStore? modelRouting = null,
    Func<IModelProvider, ProviderKey?>? configuration = null,
    IProviderAdmission? admission = null) : ITextRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<TextRouter>.Instance;
    private RoutingPolicy Policy => options.Routing;

    // resolved once: the no-delegate case must cost nothing per candidate, and a null-returning delegate must
    // be indistinguishable from no delegate at all
    private readonly Func<IModelProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    // built once — O(1) per candidate/retry. Case-insensitive, as ProviderPoolGuard accepts a pool slot cased
    // differently from the provider's own Id: an ordinal table left such a backend poolable and never tried.
    private readonly Lazy<IReadOnlyDictionary<string, IModelProvider>> _byId = new(() => ProviderLookup.ById(providers));

    public async Task<TextResponse> CompleteAsync(IReadOnlyList<ProviderCandidate> candidates, TextRequest req, CancellationToken ct = default)
    {
        var routing = await LiveRouteAsync(candidates, req, ct).ConfigureAwait(false);
        TextResponse? last = null;           // the last SUBSTANTIVE failure — what the caller is told
        TextResponse? lastBlameless = null;  // …kept apart, so it can answer only when there was no real failure
        var skipped = new SkippedCandidates(ProviderOperation.Complete);

        foreach (var (provider, effectiveModel, key) in LiveCandidates(routing, req, ProviderOperation.Complete, skipped))
        {
            WarnIfToolsUnsupported(provider, req, streaming: false);
            // retry-then-advance: the same candidate may be retried on transient faults before advancing
            var retries = 0;
            while (true)
            {
                var reply = await TryCompleteAsync(provider, effectiveModel, req, ct).ConfigureAwait(false);
                _logger.LogInformation("router: {Provider} (model {Model}) → {Verdict}{Detail}",
                    provider.Id, effectiveModel ?? "(default)", reply.Verdict,
                    reply.Verdict == ProviderVerdict.Ok ? "" : $" — {reply.Detail}");

                if (reply.Verdict == ProviderVerdict.Ok)
                {
                    deadHosts.RecordSuccess(key);
                    return reply;
                }

                var action = Policy.ActionFor(reply.Verdict);
                if (action == FallbackAction.Surface)
                    return reply; // content policy follows the prompt, not the host — surface as-is

                // a blameless verdict must never MASK a real one — see IsBlameless
                if (reply.Verdict.IsBlameless()) lastBlameless = reply; else last = reply;
                if (action == FallbackAction.CooldownAndAdvance)
                {
                    deadHosts.MarkDead(key); // §6 amended: terminal for this host, advance to the next
                    break;
                }
                if (action == FallbackAction.PenalizeAndAdvance)
                {
                    if (Policy.ShouldRetrySameCandidate(reply.Verdict, ++retries))
                    {
                        _logger.LogDebug("router: retrying {Provider} ({Retry}/{Budget}) after {Verdict}",
                            provider.Id, retries, Policy.RetriesFor(reply.Verdict), reply.Verdict);
                        if (Policy.RetryBackoff > TimeSpan.Zero) await Task.Delay(Policy.RetryBackoff, ct).ConfigureAwait(false);
                        continue; // retries are part of ONE attempt at this candidate — no failure recorded yet
                    }
                    // retries exhausted: record exactly ONE failure for this request (not one per retry —
                    // that would cross the dead-host threshold within a single call)
                    deadHosts.RecordFailure(key);
                }
                // Advance (no host penalty) or exhausted retries → next candidate
                break;
            }
        }

        // a real failure outranks a blameless one; with no real failure the blameless verdict is still the
        // honest answer (a host turns "not configured" into a setup prompt), and only a candidate list that
        // produced nothing at all falls through to the synthetic reply
        if ((last ?? lastBlameless) is { } answer) return answer;
        var (verdict, detail) = skipped.Outcome();
        return new TextResponse("", verdict, Detail: detail);
    }

    public async IAsyncEnumerable<TextChunk> StreamAsync(IReadOnlyList<ProviderCandidate> candidates, TextRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var routing = await LiveRouteAsync(candidates, req, ct).ConfigureAwait(false);
        var failures = new StreamFailures();
        var skipped = new SkippedCandidates(ProviderOperation.Stream);

        foreach (var (provider, effectiveModel, key) in LiveCandidates(routing, req, ProviderOperation.Stream, skipped))
        {
            WarnIfToolsUnsupported(provider, req, streaming: true);
            var effective = req with { Model = effectiveModel };

            // pre-content retry-then-advance: streaming can only retry BEFORE the first token (after
            // it, no fallback at all). Each pass is a fresh stream attempt on the same candidate.
            var advance = false;
            var retries = 0;
            while (!advance)
            {
                var attempt = new StreamAttempt();
                await foreach (var chunk in StreamOnceAsync(provider, effective, key, failures, attempt, ct)
                                   .ConfigureAwait(false))
                    yield return chunk;
                if (attempt.Done) yield break;

                // pre-content failure: retry the same candidate if the policy allows, else advance
                if (Policy.ShouldRetrySameCandidate(attempt.RetryVerdict, ++retries))
                {
                    _logger.LogDebug("router: retrying stream {Provider} ({Retry}/{Budget}) after {Verdict}",
                        provider.Id, retries, Policy.RetriesFor(attempt.RetryVerdict), attempt.RetryVerdict);
                    if (Policy.RetryBackoff > TimeSpan.Zero) await Task.Delay(Policy.RetryBackoff, ct).ConfigureAwait(false);
                    continue; // retries are part of ONE attempt — no failure recorded yet
                }
                // retries exhausted: record exactly ONE failure for a penalize verdict (not one per retry)
                if (Policy.ActionFor(attempt.RetryVerdict) == FallbackAction.PenalizeAndAdvance)
                    deadHosts.RecordFailure(key);
                advance = true;
            }
        }

        yield return failures.Closing(skipped);
    }

    /// <summary>ONE stream attempt at one candidate, under its own span: read the provider's chunks, apply
    /// the two streaming invariants, and leave <paramref name="attempt"/> saying what the caller should do
    /// next — <see cref="StreamAttempt.Done"/> when the router's own stream is over, otherwise
    /// <see cref="StreamAttempt.RetryVerdict"/> for the retry-vs-advance decision.</summary>
    private async IAsyncEnumerable<TextChunk> StreamOnceAsync(
        IModelProvider provider, TextRequest effective, string key, StreamFailures failures, StreamAttempt attempt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var activity = LyntaiDiagnostics.StartChat(provider.Id, effective.Model);
        var start = Stopwatch.GetTimestamp();
        ProviderVerdict outcome = ProviderVerdict.Ok;
        TextUsage? usage = null;
        string? outcomeDetail = null;
        try
        {
            var enumerator = StreamOpening.Deferred(() => provider.StreamAsync(effective, ct), ct).GetAsyncEnumerator(ct);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    var chunk = await ReadNextAsync(enumerator, ct).ConfigureAwait(false);
                    if (chunk is null) break;

                    // Trust boundary: a Final with NO preceding content is the empty-reply trap in
                    // stream form (pitfalls: empty output must be fall-over-able, never a clean end) —
                    // convert it to an Error so the pre-content fallback path below handles it.
                    if (chunk.Kind == TextChunkKind.Final && !attempt.Committed)
                        chunk = TextChunk.Error(ProviderVerdict.Failed, $"{provider.Id}: empty stream (Final with no content)");

                    if (chunk.Kind == TextChunkKind.Error)
                    {
                        outcome = chunk.Verdict;
                        outcomeDetail = chunk.Detail;
                    }
                    if (chunk.Kind == TextChunkKind.Final) usage = chunk.Usage;

                    if (chunk.Kind == TextChunkKind.Error && !attempt.Committed)
                    {
                        if (!MayFallOver(provider, chunk, key, failures, attempt))
                        {
                            attempt.Done = true;
                            yield return chunk; // no fallback (same as non-streaming)
                            yield break;
                        }
                        break; // leave the enumerator; decide retry-vs-advance in the caller
                    }

                    // A TOOL CALL commits exactly as content does, and for a sharper reason (3.0). Once
                    // a call has been announced the consumer may already have EXECUTED it — ToolLoop
                    // invokes on the chunk — so falling over to another candidate would run somebody's
                    // side effect twice. Content only duplicates tokens; this duplicates actions.
                    // A malformed ToolCall chunk carrying no call is dropped rather than committing,
                    // the same trust-boundary rule the empty content chunk below follows.
                    if (chunk.Kind == TextChunkKind.ToolCall && chunk.ToolCall is null) continue;

                    if (chunk.Kind is TextChunkKind.Content or TextChunkKind.ToolCall)
                    {
                        // an empty/role-only content chunk is NOT real content: never yield it
                        // (no leak to the consumer) and it must not commit the stream / disable fallback
                        if (chunk.Kind == TextChunkKind.Content && chunk.Text.Length == 0) continue;
                        if (!attempt.Committed)
                        {
                            attempt.Committed = true;
                            deadHosts.RecordSuccess(key);
                            LyntaiDiagnostics.RecordFirstChunk(provider.Id, effective.Model,
                                Stopwatch.GetElapsedTime(start).TotalSeconds);
                            _logger.LogInformation("router: streaming from {Provider} (model {Model})",
                                provider.Id, effective.Model ?? "(default)");
                        }
                        yield return chunk;
                        continue;
                    }

                    // Final, or an Error AFTER content committed — the only kinds that reach here
                    // (Content continues above; a pre-content Error/Final breaks/converts above).
                    // Both are terminal: pass through unchanged and end the stream.
                    attempt.Done = true;
                    yield return chunk;
                    yield break;
                }
            }

            if (attempt.Committed) { attempt.Done = true; yield break; } // ended after content — done
            if (attempt.RetryVerdict == ProviderVerdict.Ok)
            {
                // the enumerator ended with NO chunks at all — a contract-violating empty stream
                // (providers must end with exactly one Final or Error). Same trust-boundary rule as
                // an empty reply: treat as Failed and retry/advance rather than ending the router's
                // own stream silently with no terminal chunk.
                attempt.RetryVerdict = ProviderVerdict.Failed;
                outcome = ProviderVerdict.Failed;
                outcomeDetail = "empty stream (no chunks)";
                failures.LastError = TextChunk.Error(ProviderVerdict.Failed, $"{provider.Id}: empty stream (no chunks)");
                _logger.LogWarning("router: {Provider} produced an empty stream (no chunks); treating as Failed", provider.Id);
            }
        }
        finally
        {
            LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, outcome, usage,
                Stopwatch.GetElapsedTime(start).TotalSeconds, outcomeDetail);
            activity?.Dispose();
        }
    }

    /// <summary>One guarded read off a provider's stream — the next chunk, or <c>null</c> at its end.
    /// <para>Only the CALLER's cancel aborts the router (the trust boundary); a provider's OWN
    /// <see cref="OperationCanceledException"/> (<paramref name="ct"/> not cancelled — e.g. its internal
    /// timeout) becomes a fall-over-able Error chunk rather than an abort of the whole stream. Any
    /// mid-iteration throw is classified through the shared taxonomy (thrown 429→RateLimited,
    /// provider-internal OCE→Timeout, …).</para></summary>
    private static async ValueTask<TextChunk?> ReadNextAsync(IAsyncEnumerator<TextChunk> enumerator, CancellationToken ct)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return TextChunk.Error(ProviderVerdictClassifier.FromThrown(ex), ex.Message);
        }
    }

    /// <summary>The pre-content fallback decision for an Error chunk: file it in the slot its verdict belongs
    /// in, apply the policy's host penalty, and say whether the router may fall over. <c>false</c> is
    /// <see cref="FallbackAction.Surface"/> — the caller yields the chunk as-is and ends, exactly as the
    /// non-streaming path surfaces a refusal.</summary>
    private bool MayFallOver(IModelProvider provider, TextChunk error, string key, StreamFailures failures,
        StreamAttempt attempt)
    {
        failures.Record(error);
        var action = Policy.ActionFor(error.Verdict);
        if (action == FallbackAction.Surface) return false;
        _logger.LogWarning("router: {Provider} failed pre-content ({Verdict} — {Detail}); trying next candidate",
            provider.Id, error.Verdict, error.Detail);
        if (action == FallbackAction.CooldownAndAdvance) deadHosts.MarkDead(key);
        // PenalizeAndAdvance records ONE failure on advance (in the caller), not per retry
        attempt.RetryVerdict = error.Verdict;
        return true;
    }

    /// <summary>What <see cref="StreamAsync"/>'s closing chunk is built from, folded across every candidate.
    /// The two slots are the streaming twin of <see cref="CompleteAsync"/>'s, for the same reason: a blameless
    /// verdict must never MASK a real one.</summary>
    private sealed class StreamFailures
    {
        /// <summary>The last SUBSTANTIVE failure — what the caller is told.</summary>
        public TextChunk? LastError { get; set; }

        /// <summary>…kept apart, so it answers only when there was no real failure.</summary>
        public TextChunk? LastBlameless { get; set; }

        /// <summary>File an error into the slot its verdict belongs in.</summary>
        public void Record(TextChunk error)
        {
            if (error.Verdict.IsBlameless()) LastBlameless = error; else LastError = error;
        }

        /// <summary>A real failure outranks a blameless one; only a candidate list that produced nothing at
        /// all falls through to the synthetic chunk, which <paramref name="skipped"/> words.</summary>
        public TextChunk Closing(SkippedCandidates skipped)
        {
            if ((LastError ?? LastBlameless) is { } error) return error;
            var (verdict, detail) = skipped.Outcome();
            return TextChunk.Error(verdict, detail);
        }
    }

    /// <summary>Why a candidate was skipped, as far as the synthesized verdict cares: a CAPABILITY gap (it serves
    /// no text, or not on this door) benches nothing and reports Unsupported; anything else is transient.</summary>
    private enum Gap { None, NoText, NoDoor }

    /// <summary>The candidates one call skipped, and why — what the router answers with when it tried none.</summary>
    private sealed class SkippedCandidates(ProviderOperation door)
    {
        private readonly List<(string Candidate, string Reason, Gap Gap)> _skipped = [];

        public void Add(ProviderCandidate candidate, string reason, Gap gap) =>
            _skipped.Add((ProviderCandidateSpec.Format(candidate), reason, gap));

        /// <summary><see cref="ProviderVerdict.Unsupported"/> when every candidate is a capability gap — which
        /// benches nothing — else <see cref="ProviderVerdict.Failed"/>; either way the detail names each
        /// candidate and why it was skipped.</summary>
        public (ProviderVerdict Verdict, string Detail) Outcome()
        {
            if (_skipped.Count == 0) return (ProviderVerdict.Failed, "no live candidate (none given)");
            var reasons = string.Join("; ", _skipped.Select(s => $"{s.Candidate}: {s.Reason}"));
            if (_skipped.TrueForAll(s => s.Gap == Gap.NoText))
                return (ProviderVerdict.Unsupported, $"no candidate serves text ({reasons})");
            return _skipped.TrueForAll(s => s.Gap != Gap.None)
                ? (ProviderVerdict.Unsupported, $"no candidate serves a text {door} call ({reasons})")
                : (ProviderVerdict.Failed, $"no live candidate (all skipped: {reasons})");
        }
    }

    /// <summary>One attempt's own state, carried out of <see cref="StreamOnceAsync"/> because an async
    /// iterator has no return value to put it in. Fresh per attempt, so a retry starts uncommitted.</summary>
    private sealed class StreamAttempt
    {
        /// <summary>Once real content is yielded, no fallback — everything after it passes through.</summary>
        public bool Committed { get; set; }

        /// <summary>The router's own stream is over: no retry, no next candidate, no closing chunk.</summary>
        public bool Done { get; set; }

        /// <summary>What retry-vs-advance is decided on; <see cref="ProviderVerdict.Ok"/> means nothing
        /// failed pre-content.</summary>
        public ProviderVerdict RetryVerdict { get; set; } = ProviderVerdict.Ok;
    }

    /// <inheritdoc/>
    /// <remarks>The live route is read through the same step the call takes, so the answer names the backend
    /// <see cref="CompleteAsync"/> would commit to first; a capable fallback that would never be reached does not
    /// flip it.</remarks>
    public async ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(IReadOnlyList<ProviderCandidate> candidates,
        TextRequest req, CancellationToken ct = default)
    {
        var routing = await LiveRouteAsync(candidates, req, ct).ConfigureAwait(false);
        foreach (var candidate in LiveCandidates(routing, req, ProviderOperation.Complete))
            return candidate.Provider.Capabilities; // the first live candidate decides
        return null;
    }

    /// <summary>The candidates one call routes over, and whether they are the consumer's live route.</summary>
    private readonly record struct Routing(IReadOnlyList<ProviderCandidate> Candidates, bool IsLiveRoute);

    /// <summary>The shared candidate-selection preamble every door runs: dedup the list, resolve each
    /// candidate's EFFECTIVE model, skip unknown/text-less/unavailable/cooling providers and those not declaring
    /// <paramref name="door"/> (with the sole-candidate exemption from cooldown), recording each skip in
    /// <paramref name="skipped"/>, and pair each survivor with its cooldown key. A given candidate's model is its
    /// own, else the request's, else the consumer default; a live route entry's never falls to the consumer
    /// default, which belongs to the candidates the route replaced — its own, else the request's, else the
    /// backend's.</summary>
    private IEnumerable<(IModelProvider Provider, string? Model, string Key)> LiveCandidates(
        Routing routing, TextRequest req, ProviderOperation door, SkippedCandidates? skipped = null)
    {
        var deduped = CandidateDedup.Dedup(routing.Candidates);
        var soleCandidate = deduped.Count == 1;
        foreach (var candidate in deduped)
        {
            var effectiveModel = routing.IsLiveRoute
                ? RouteEntryModel(candidate, req.Model)
                : options.ResolveModel(req.Consumer, candidate.Model ?? req.Model);
            var provider = SelectLive(candidate, effectiveModel, door, soleCandidate, out var skipReason, out var gap);
            if (provider is null)
            {
                // a text-less backend in a text list is the caller's defect, not transient state: warn, as D176
                // warns of the same entry in a live route
                if (gap == Gap.NoText)
                    _logger.LogWarning("router: skipping {Candidate} — {Reason}; a text candidate list should not name it",
                        ProviderCandidateSpec.Format(candidate), skipReason);
                else
                    _logger.LogDebug("router: skipping {Candidate} — {Reason}", candidate.ProviderId, skipReason);
                skipped?.Add(candidate, skipReason, gap);
                continue;
            }
            yield return (provider, effectiveModel, CooldownKey(provider, effectiveModel));
        }
    }

    private async Task<TextResponse> TryCompleteAsync(IModelProvider provider, string? effectiveModel, TextRequest req, CancellationToken ct)
    {
        // the permit is taken BEFORE the span opens, so a queued call's wait never inflates the backend's
        // reported latency. Scoped with `using`, so the verdict return, the rethrown caller cancel and the
        // classified provider throw below all release it — a permit that escapes pins its gate forever.
        using var permit = await EnterAdmissionAsync(provider, ct).ConfigureAwait(false);

        var effective = req with { Model = effectiveModel };
        using var activity = LyntaiDiagnostics.StartChat(provider.Id, effective.Model);
        var start = Stopwatch.GetTimestamp();
        TextResponse reply;
        try
        {
            reply = await provider.CompleteAsync(effective, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-initiated cancel is not a provider failure
        }
        catch (Exception ex)
        {
            // classify through the shared taxonomy (thrown 429→RateLimited, 401/403→AuthFailed,
            // OCE→Timeout, …) — a provider that THROWS must get the same fallback policy as one that
            // returns a verdict reply; hand-rolling Failed here would hammer a rate-limited host
            // instead of cooling it (see llm-and-router.md).
            reply = new TextResponse("", ProviderVerdictClassifier.FromThrown(ex), Detail: $"{provider.Id}: {ex.Message}");
        }
        LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, reply.Verdict, reply.Usage,
            Stopwatch.GetElapsedTime(start).TotalSeconds, reply.Detail);
        return reply;
    }

    /// <summary>The candidates a call routes over: the consumer's live route when one is set, else
    /// <paramref name="given"/>. Read once per call, and never silently wrong: a store that throws, and a route
    /// naming no registered TEXT provider, each leave <paramref name="given"/> in force with a warning; a route
    /// naming only SOME such entries is used without them, with a warning naming them — a typo in the primary
    /// otherwise moves all traffic to the backup unseen. A request model the route can never serve warns too.</summary>
    private async Task<Routing> LiveRouteAsync(
        IReadOnlyList<ProviderCandidate> given, TextRequest req, CancellationToken ct)
    {
        if (modelRouting is null) return new(given, false);
        var consumer = req.Consumer;
        IReadOnlyList<ProviderCandidate> route;
        try
        {
            route = await modelRouting.GetRouteAsync(consumer, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "router: the live route read failed for consumer {Consumer}; routing over the given candidates", consumer);
            return new(given, false);
        }
        if (route is not { Count: > 0 }) return new(given, false);

        // a registered backend that serves no text (an embedder, a reranker) is as unusable here as an unknown id
        bool ServesText(ProviderCandidate c) => _byId.Value.TryGetValue(c.ProviderId, out var p)
            && ClientCandidates.ServesText(p);
        var usable = route.Where(ServesText).ToList();
        var unusable = route.Where(c => !ServesText(c)).Select(ProviderCandidateSpec.Format).ToList();
        if (usable.Count == 0)
        {
            _logger.LogWarning("router: the live route for consumer {Consumer} ({Route}) names no registered text provider; routing over the given candidates",
                consumer, string.Join(", ", unusable));
            return new(given, false);
        }
        if (unusable.Count > 0)
            _logger.LogWarning("router: the live route for consumer {Consumer} names providers not registered here or serving no text ({Unknown}); they are skipped",
                consumer, string.Join(", ", unusable));

        // D119's composition-time check, at run time: every entry pins a model and none is the one asked for
        if (!string.IsNullOrEmpty(req.Model) && ClientCandidates.ModelPinIsInert(req.Model, usable))
            _logger.LogWarning("router: consumer {Consumer} asks for model {Model}, which its live route ({Route}) can never serve — every entry pins another; the route's models serve",
                consumer, req.Model, string.Join(", ", usable.Select(ProviderCandidateSpec.Format)));
        return new(usable, true);
    }

    /// <summary>A live route entry's model: its own, else the request's, else null — the backend's default. A
    /// blank entry model pins nothing, as <see cref="ClientCandidates.ModelPinIsInert"/> reads a pin, and an
    /// empty request model is absent, as <see cref="LyntaiOptions.ResolveModel"/> reads one.</summary>
    private static string? RouteEntryModel(ProviderCandidate entry, string? requestModel) =>
        !string.IsNullOrWhiteSpace(entry.Model) ? entry.Model
        : !string.IsNullOrEmpty(requestModel) ? requestModel
        : null;

    /// <summary>The backstop under the capability probe: tools sent to a backend that does not declare native
    /// tool calls (on a stream, that its STREAM carries them) go uncalled, and the reply reads as a final answer
    /// — so say so, once per candidate tried, fallback included. Not a verdict change.</summary>
    private void WarnIfToolsUnsupported(IModelProvider provider, TextRequest req, bool streaming)
    {
        if (req.Tools is not { Count: > 0 } tools) return;
        var capable = streaming
            ? provider.Capabilities.SupportsStreamingToolCalls
            : provider.Capabilities.SupportsToolCalls;
        if (!capable)
            _logger.LogWarning("router: {Provider} declares no native tool calls{Door}, but the request carries {Count} tool declaration(s); expect them to go uncalled",
                provider.Id, streaming ? " on its stream" : "", tools.Count);
    }

    /// <summary>Take a concurrency permit for this provider's CONFIGURATION, or nothing at all when no
    /// admission is wired or the configuration is unknown. Never returns a handle the caller may skip
    /// disposing: a permit that is not returned pins its gate for the life of the process, so the call site
    /// scopes the result with <c>using</c> and lets success, failure, a throw and cancellation all release it
    /// the same way.</summary>
    private async ValueTask<IDisposable?> EnterAdmissionAsync(IModelProvider provider, CancellationToken ct) =>
        admission is not null && _configuration(provider) is { } key
            ? await admission.EnterAsync(key, ct).ConfigureAwait(false)
            : null;

    /// <summary>The dead-host key for a candidate: its CONFIGURATION when one is known, else its provider id
    /// — so two configurations of one backend bench independently while two consumers of one downed host
    /// share a bench — at the configured cooldown granularity, which composes on top either way.</summary>
    private string CooldownKey(IModelProvider provider, string? effectiveModel)
    {
        var identity = _configuration(provider)?.ToString() ?? provider.Id;
        return Policy.CooldownScope == CooldownScope.ProviderAndModel
            ? $"{identity}::{effectiveModel ?? "(default)"}"
            : identity;
    }

    private IModelProvider? SelectLive(ProviderCandidate candidate, string? effectiveModel, ProviderOperation door,
        bool soleCandidate, out string skipReason, out Gap gap)
    {
        gap = Gap.None;
        if (!_byId.Value.TryGetValue(candidate.ProviderId, out var provider))
        { skipReason = "no provider with this id registered"; return null; }

        // composition refuses a CONFIGURED list naming one; a list passed at run time reaches here
        if (!ClientCandidates.ServesText(provider))
        {
            skipReason = $"produces {ClientCandidates.Produces(provider)}, not text";
            gap = Gap.NoText;
            return null;
        }
        // an undeclared door would answer Unsupported, which surfaces with no fallback — so it is never asked
        if (!ClientCandidates.Serves(provider, door))
        {
            skipReason = $"does not declare {door}";
            gap = Gap.NoDoor;
            return null;
        }
        if (!provider.IsAvailable) { skipReason = "provider reports unavailable"; return null; }

        // sole-candidate exemption: benching the only option just guarantees a synthetic failure —
        // try it and let it fail with a real error / maybe succeed if the cooldown was stale
        var exempt = soleCandidate && Policy.ExemptSoleCandidate;
        if (!exempt && deadHosts.IsDead(CooldownKey(provider, effectiveModel)))
        {
            skipReason = "dead-host cooldown";
            return null;
        }
        skipReason = "";
        return provider;
    }
}
