using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Lyntai.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Routing;

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
/// <param name="modelRouting">Live per-consumer model overrides; null = the configured defaults alone.</param>
/// <param name="configuration">Which CONFIGURATION a provider is running under, used to key dead-host
/// cooldown and admission. Null (or a null return) = key cooldown on
/// <see cref="Lyntai.Lifecycle.IProviderIdentity.Id"/> and apply no admission — correct for a
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
public sealed class LlmRouter(
    IEnumerable<IModelProvider> providers,
    DeadHostTracker deadHosts,
    LyntaiOptions options,
    ILogger<LlmRouter>? logger = null,
    IModelRoutingStore? modelRouting = null,
    Func<IModelProvider, ProviderKey?>? configuration = null,
    IProviderAdmission? admission = null) : ILlmRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<LlmRouter>.Instance;
    private RoutingPolicy Policy => options.Routing;

    // resolved once: the no-delegate case must cost nothing per candidate, and a null-returning delegate must
    // be indistinguishable from no delegate at all
    private readonly Func<IModelProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    // provider lookup by id, built once — O(1) per candidate/retry instead of a linear scan. First
    // registration wins on a duplicate id (preserving the prior FirstOrDefault semantics).
    //
    // Keyed CASE-INSENSITIVELY, like every other id lookup in the tree (GenerationRouter, ProviderPoolGuard,
    // IToolRegistry, IJobHandlerRegistry, BoundedProviderPool). An ordinal table made a pool slot cased
    // differently from the provider's own Id — which ProviderPoolGuard deliberately ACCEPTS — reachable by
    // the guard, poolable, and then never selected here: the backend was simply never tried, with no error
    // and one debug line. Case-folding also merges two registrations whose ids differ only in case, which is
    // the same "first registration wins" rule one step earlier: the second was unreachable either way.
    private readonly Lazy<IReadOnlyDictionary<string, IModelProvider>> _byId = new(() =>
    {
        var map = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers) map.TryAdd(p.Id, p);
        return map;
    });

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<ProviderCandidate> candidates, LlmRequest req, CancellationToken ct = default)
    {
        var liveModel = await LiveModelAsync(req.Consumer, ct).ConfigureAwait(false);
        LlmReply? last = null;           // the last SUBSTANTIVE failure — what the caller is told
        LlmReply? lastBlameless = null;  // …kept apart, so it can answer only when there was no real failure

        foreach (var (provider, effectiveModel, key) in LiveCandidates(candidates, req, liveModel))
        {
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
        return last ?? lastBlameless
            ?? new LlmReply("", ProviderVerdict.Failed, Detail: "no live candidate (all skipped: unknown, unavailable, or dead)");
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(IReadOnlyList<ProviderCandidate> candidates, LlmRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var liveModel = await LiveModelAsync(req.Consumer, ct).ConfigureAwait(false);
        var failures = new StreamFailures();

        foreach (var (provider, effectiveModel, key) in LiveCandidates(candidates, req, liveModel))
        {
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

        yield return failures.Closing();
    }

    /// <summary>ONE stream attempt at one candidate, under its own span: read the provider's chunks, apply
    /// the two streaming invariants, and leave <paramref name="attempt"/> saying what the caller should do
    /// next — <see cref="StreamAttempt.Done"/> when the router's own stream is over, otherwise
    /// <see cref="StreamAttempt.RetryVerdict"/> for the retry-vs-advance decision.</summary>
    private async IAsyncEnumerable<LlmChunk> StreamOnceAsync(
        IModelProvider provider, LlmRequest effective, string key, StreamFailures failures, StreamAttempt attempt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var activity = LyntaiDiagnostics.StartChat(provider.Id, effective.Model);
        var start = Stopwatch.GetTimestamp();
        ProviderVerdict outcome = ProviderVerdict.Ok;
        LlmUsage? usage = null;
        string? outcomeDetail = null;
        try
        {
            var enumerator = provider.StreamAsync(effective, ct).GetAsyncEnumerator(ct);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    var chunk = await ReadNextAsync(enumerator, ct).ConfigureAwait(false);
                    if (chunk is null) break;

                    // Trust boundary: a Final with NO preceding content is the empty-reply trap in
                    // stream form (pitfalls: empty output must be fall-over-able, never a clean end) —
                    // convert it to an Error so the pre-content fallback path below handles it.
                    if (chunk.Kind == LlmChunkKind.Final && !attempt.Committed)
                        chunk = LlmChunk.Error(ProviderVerdict.Failed, $"{provider.Id}: empty stream (Final with no content)");

                    if (chunk.Kind == LlmChunkKind.Error)
                    {
                        outcome = chunk.Verdict;
                        outcomeDetail = chunk.Detail;
                    }
                    if (chunk.Kind == LlmChunkKind.Final) usage = chunk.Usage;

                    if (chunk.Kind == LlmChunkKind.Error && !attempt.Committed)
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
                    if (chunk.Kind == LlmChunkKind.ToolCall && chunk.ToolCall is null) continue;

                    if (chunk.Kind is LlmChunkKind.Content or LlmChunkKind.ToolCall)
                    {
                        // an empty/role-only content chunk is NOT real content: never yield it
                        // (no leak to the consumer) and it must not commit the stream / disable fallback
                        if (chunk.Kind == LlmChunkKind.Content && chunk.Text.Length == 0) continue;
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
                failures.LastError = LlmChunk.Error(ProviderVerdict.Failed, $"{provider.Id}: empty stream (no chunks)");
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
    private static async ValueTask<LlmChunk?> ReadNextAsync(IAsyncEnumerator<LlmChunk> enumerator, CancellationToken ct)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return LlmChunk.Error(ProviderVerdictClassifier.FromThrown(ex), ex.Message);
        }
    }

    /// <summary>The pre-content fallback decision for an Error chunk: file it in the slot its verdict belongs
    /// in, apply the policy's host penalty, and say whether the router may fall over. <c>false</c> is
    /// <see cref="FallbackAction.Surface"/> — the caller yields the chunk as-is and ends, exactly as the
    /// non-streaming path surfaces a refusal.</summary>
    private bool MayFallOver(IModelProvider provider, LlmChunk error, string key, StreamFailures failures,
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
        public LlmChunk? LastError { get; set; }

        /// <summary>…kept apart, so it answers only when there was no real failure.</summary>
        public LlmChunk? LastBlameless { get; set; }

        /// <summary>File an error into the slot its verdict belongs in.</summary>
        public void Record(LlmChunk error)
        {
            if (error.Verdict.IsBlameless()) LastBlameless = error; else LastError = error;
        }

        /// <summary>A real failure outranks a blameless one; only a candidate list that produced nothing at
        /// all falls through to the synthetic chunk.</summary>
        public LlmChunk Closing() => LastError ?? LastBlameless
            ?? LlmChunk.Error(ProviderVerdict.Failed, "no live candidate (all skipped: unknown, unavailable, or dead)");
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

    /// <summary>Native tool support for a candidate list: the first live candidate (registered,
    /// available, not on cooldown) decides — matching how the router commits to the first working one.
    /// A tool-capable fallback that would never be reached must not flip this true. Being a SYNC probe,
    /// it resolves against the CONFIGURED default model (no live <see cref="IModelRoutingStore"/> read) —
    /// under <see cref="CooldownScope.ProviderAndModel"/> plus a live override, the probe's cooldown key
    /// can differ from the completion's.</summary>
    public bool SupportsToolCalls(IReadOnlyList<ProviderCandidate> candidates, LlmRequest req)
    {
        foreach (var candidate in LiveCandidates(candidates, req, liveModel: null))
            return candidate.Provider.Capabilities.SupportsToolCalls; // first live candidate decides
        return false;
    }

    /// <inheritdoc/>
    public bool SupportsStreamingToolCalls(IReadOnlyList<ProviderCandidate> candidates, LlmRequest req)
    {
        foreach (var candidate in LiveCandidates(candidates, req, liveModel: null))
            return candidate.Provider.Capabilities.SupportsStreamingToolCalls; // first live candidate decides, as above
        return false;
    }

    /// <summary>The shared candidate-selection preamble every door runs (this used to be written out three
    /// times): dedup the list, resolve each candidate's EFFECTIVE model (candidate override → request →
    /// consumer default → live override when supplied), skip unknown/unavailable/cooling providers (with
    /// the sole-candidate exemption), and pair each survivor with its cooldown key.</summary>
    private IEnumerable<(IModelProvider Provider, string? Model, string Key)> LiveCandidates(
        IReadOnlyList<ProviderCandidate> candidates, LlmRequest req, string? liveModel)
    {
        var deduped = CandidateDedup.Dedup(candidates);
        var soleCandidate = deduped.Count == 1;
        foreach (var candidate in deduped)
        {
            var effectiveModel = options.ResolveModel(req.Consumer, candidate.Model ?? req.Model, liveModel);
            var provider = SelectLive(candidate, effectiveModel, soleCandidate, out var skipReason);
            if (provider is null)
            {
                _logger.LogDebug("router: skipping {Candidate} — {Reason}", candidate.ProviderId, skipReason);
                continue;
            }
            yield return (provider, effectiveModel, CooldownKey(provider, effectiveModel));
        }
    }

    private async Task<LlmReply> TryCompleteAsync(IModelProvider provider, string? effectiveModel, LlmRequest req, CancellationToken ct)
    {
        // the permit is taken BEFORE the span opens, so a queued call's wait never inflates the backend's
        // reported latency. Scoped with `using`, so the verdict return, the rethrown caller cancel and the
        // classified provider throw below all release it — a permit that escapes pins its gate forever.
        using var permit = await EnterAdmissionAsync(provider, ct).ConfigureAwait(false);

        var effective = req with { Model = effectiveModel };
        using var activity = LyntaiDiagnostics.StartChat(provider.Id, effective.Model);
        var start = Stopwatch.GetTimestamp();
        LlmReply reply;
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
            reply = new LlmReply("", ProviderVerdictClassifier.FromThrown(ex), Detail: $"{provider.Id}: {ex.Message}");
        }
        LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, reply.Verdict, reply.Usage,
            Stopwatch.GetElapsedTime(start).TotalSeconds, reply.Detail);
        return reply;
    }

    /// <summary>The live per-consumer model override (null when live routing isn't wired) — read once per
    /// call and passed into <see cref="LyntaiOptions.ResolveModel(string,string,string)"/> for each candidate.
    /// (The sync <see cref="SupportsToolCalls"/> capability probe stays on the configured default.)</summary>
    private async Task<string?> LiveModelAsync(string consumer, CancellationToken ct) =>
        modelRouting is null ? null : await modelRouting.GetModelOverrideAsync(consumer, ct).ConfigureAwait(false);

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

    private IModelProvider? SelectLive(ProviderCandidate candidate, string? effectiveModel, bool soleCandidate, out string skipReason)
    {
        if (!_byId.Value.TryGetValue(candidate.ProviderId, out var provider))
        { skipReason = "no provider with this id registered"; return null; }
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
