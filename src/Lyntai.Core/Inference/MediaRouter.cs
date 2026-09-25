using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;

namespace Lyntai.Inference;

/// <summary>The default <see cref="IMediaRouter"/>: capability pre-filter, then verdict-driven fallback
/// with dead-host cooldown, plus a span and metrics per attempt.
///
/// <para>Per-verdict fallback semantics are <see cref="MediaRoutingPolicy"/>'s, including where they
/// deliberately diverge from <see cref="TextRouter"/>'s on <see cref="ProviderVerdict.Unsupported"/>.</para>
///
/// <para><b>Reporting keeps TWO slots, and a blameless one never outranks a real failure.</b> The first
/// substantive failure is what the caller is told; the first BLAMELESS result that explained itself is kept
/// apart and reported only when nothing substantive failed (<c>docs/DECISIONS.md</c> D31).
/// <c>TextRouter.CompleteAsync</c> holds the same two slots, so change one and check the other.</para>
///
/// <para><b>Submission has one rule of its own:</b> a failed submission marked
/// <see cref="QueuedOperation.Inconclusive"/> SURFACES rather than advancing, because a backend that
/// never answered may already hold a billable render and the next candidate would buy it twice.</para>
///
/// <para><b>The policy governs submission too, at one remove.</b> A submission carries a
/// <see cref="QueuedOperationStatus"/>, so a rejection takes the backend's own
/// <see cref="QueuedOperation.Verdict"/> where it sets one, is otherwise classified from its
/// <see cref="QueuedOperation.Detail"/>, and is answered by the same table. Two things hold whatever the
/// text says: Inconclusive is decided BEFORE the verdict, and a submission the router does not accept
/// reports an EMPTY <see cref="MediaSubmission.ProviderId"/> — <see cref="IMediaRouter"/>'s "no
/// candidate accepted" — with the first rejection folded into the detail by the two-slot rule above.</para>
///
/// <para><b>The candidate list is deduped on the RESOLVED (backend, model) pair</b>, so two entries naming
/// one backend cannot inflate the count <see cref="MediaRoutingPolicy.ExemptSoleCandidate"/> reads.</para></summary>
/// <param name="providers">The registered backends.</param>
/// <param name="policy">Per-verdict fallback behaviour; null = <see cref="MediaRoutingPolicy"/>'s
/// defaults.</param>
/// <param name="deadHosts">Cooldown bookkeeping — the SAME <see cref="DeadHostTracker"/> <see cref="TextRouter"/> uses,
/// deliberately: "this host keeps failing, stop asking" is transport bookkeeping keyed by a string, not an LLM
/// concept, and a second copy would be a second set of bugs (and a second threshold to configure). A media key
/// is prefixed <c>generation::</c> while a chat key is the BARE provider/configuration identity (plus
/// <c>::model</c> under <see cref="CooldownScope.ProviderAndModel"/>) — one prefixed namespace and one
/// unprefixed one cannot collide, so a chat outage never benches a generation backend that happens to share its
/// id. Null disables cooldown entirely (a hand-built router in a test).</param>
/// <param name="configuration">Which CONFIGURATION a provider is running under, used to key dead-host
/// cooldown and admission. Null (or a null return) = key cooldown on
/// <see cref="Lyntai.Inference.IProviderIdentity.Id"/> and apply no admission — correct for a
/// single-configuration deployment. Supply one when several configurations of a backend id are live at once,
/// or one tenant's rate limit benches every other tenant sharing that backend.
/// <see cref="IProviderPool{TProvider}.TryGetKey"/> is the intended source. <b>Must return a STABLE key for a
/// given instance</b>, as <see cref="TextRouter"/>'s does.</param>
/// <param name="admission">Bounds concurrent attempts per configuration — for a locally-run engine where
/// simultaneous renders contend for one CPU or GPU. Null = unbounded. Applied HERE rather than by
/// wrapping a provider, because a wrapper implementing only <see cref="IModelProvider"/> erases the
/// optional capability interfaces (<see cref="IMediaJobProvider"/>) this router type-tests, which
/// would silently stop every queued render from routing. <see cref="GenerateAsync"/> and
/// <see cref="SubmitAsync"/> only — <see cref="StreamAsync"/> is deliberately NOT gated, for
/// <see cref="TextRouter"/>'s reason.</param>
public sealed class MediaRouter(
    IEnumerable<IModelProvider> providers,
    MediaRoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null,
    Func<IModelProvider, ProviderKey?>? configuration = null,
    IProviderAdmission? admission = null) : IMediaRouter
{
    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];
    private readonly MediaRoutingPolicy _policy = policy ?? new MediaRoutingPolicy();

    // the tracker is shared with TextRouter, so keys carry their domain: a chat provider and an image backend
    // both called "openai" must not bench each other
    private readonly RouterBookkeeping _bookkeeping = new(deadHosts, admission, configuration, "generation::");

    /// <inheritdoc/>
    public async Task<MediaResponse> GenerateAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, ProviderOperation.Complete);
        var failures = new FirstFailures<MediaResponse>();
        var tried = 0;
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            tried++;
            var result = await AttemptAsync(provider, resolved, ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                deadHosts?.RecordSuccess(_bookkeeping.Key(provider));
                return result;
            }

            // filed BEFORE the surface check, so advancing past one (a host that configured Refused -> Advance)
            // still reports it when nothing else succeeds
            failures.File(result, result.Verdict, result.Detail);
            var action = _policy.ActionFor(result.Verdict);
            if (action == FallbackAction.Surface) return result;
            _bookkeeping.Penalize(_bookkeeping.Key(provider), action);
        }

        if (failures.Reported is { } reported) return reported;
        var (verdict, detail) = NothingServed(candidates, request, ProviderOperation.Complete, tried, benched);
        return MediaResponse.Failure(verdict, detail);
    }

    /// <inheritdoc/>
    public async Task<MediaSubmission> SubmitAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, ProviderOperation.Queued);
        var benched = 0;

        // the two slots GenerateAsync keeps: the backend that explained why is the only thing in this run a
        // caller can act on, and the synthesized message is otherwise a list of ids
        var failures = new FirstFailures<Rejection>();
        var attempted = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (provider is not IMediaJobProvider job) continue;   // capability says Job; the type must agree
            if (IsBenched(provider, capable.Count)) { benched++; continue; }
            attempted++;

            // submitting is what commits the money, so it respects the same bound as an inline render. The
            // permit is scoped to THIS iteration: a submission that fails releases it before the next
            // candidate is tried.
            using (await _bookkeeping.EnterAsync(provider, ct).ConfigureAwait(false))
            {
                var started = Stopwatch.GetTimestamp();
                using var span = LyntaiDiagnostics.StartGeneration("submit", provider.Id, resolved.Kind, resolved.Model);
                QueuedOperation operation;
                try
                {
                    operation = await job.SubmitAsync(resolved, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // caller-initiated cancel is not a backend failure
                }
                catch (Exception ex) when (!ProviderVerdictClassifier.NeverReachedTheBackend(ex))
                {
                    // A throw during SUBMIT that MAY have been delivered is INCONCLUSIVE — the backend may
                    // already hold a billable render, so advancing to the next candidate would buy the same
                    // generation twice. One that provably never left this process is NOT caught (the filter):
                    // it committed nothing, so it propagates and the durable-job runner applies its ordinary
                    // retry — treating it as ambiguous would dead-letter a transient blip instead.
                    operation = QueuedOperation.FromThrownSubmit(ex, detail: $"{provider.Id}: {ex.Message}");
                }
                LyntaiDiagnostics.RecordSubmission(span, provider.Id, resolved.Kind, operation.Id, operation.Status,
                    Stopwatch.GetElapsedTime(started).TotalSeconds, operation.Inconclusive);

                if (operation.Status != QueuedOperationStatus.Failed)
                {
                    deadHosts?.RecordSuccess(_bookkeeping.Key(provider));
                    return new MediaSubmission(provider.Id, operation);
                }

                // An INCONCLUSIVE submission SURFACES, like a refusal does: the backend never answered, so it
                // may already hold a billable render, and trying the next candidate would buy the same
                // generation twice — the duplicate-payment case the job handler's checkpoint-first ordering
                // exists to prevent. The provider id rides along because "who might have it?" is the only
                // question worth asking next. No RecordFailure either: no answer is no evidence of ill health,
                // and benching a working backend on a slow network helps nobody.
                if (operation.Inconclusive) return new MediaSubmission(provider.Id, operation);

                // A rejection gets a VERDICT, so a blameless one takes no strike (an unconfigured FalProvider
                // says NotConfigured before it opens a socket; D31): the backend's own where it gives one, else
                // its words classified, and Failed when nothing recognises them.
                var verdict = operation.Verdict ?? ProviderVerdictClassifier.FromErrorText(operation.Detail);
                failures.File(new Rejection(provider.Id, operation.Detail, verdict), verdict, operation.Detail);

                var action = _policy.ActionFor(verdict);
                // a content refusal is the backend judging the PROMPT, and re-submitting it to the next vendor is
                // not a library's decision — the inline rule, overridable the same way (On(Refused, Advance)).
                // ProviderId stays empty: it means "no candidate accepted", and a refusal is not an acceptance.
                if (action == FallbackAction.Surface) return new MediaSubmission("", operation with { Verdict = verdict });
                _bookkeeping.Penalize(_bookkeeping.Key(provider), action);
            }
        }

        // the verdict is chosen as GenerateAsync chooses the one it reports, so "nobody could serve it" says why
        var reported = failures.Reported;
        var (synthesized, roster) = attempted > 0
            ? (ProviderVerdict.NotConfigured,
                $"no capable media backend accepted a '{request.Kind}' job among [{Ids(candidates)}]")
            : NothingServed(candidates, request, ProviderOperation.Queued, tried: 0, benched);
        return MediaSubmission.Failure(reported?.Verdict ?? synthesized, roster + Because(reported));
    }

    /// <summary>A rejected submission, as the reporting slots keep it.</summary>
    private sealed record Rejection(string ProviderId, string? Detail, ProviderVerdict Verdict);

    /// <inheritdoc/>
    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, ProviderOperation.Stream);
        var failures = new FirstFailures<MediaChunk>();
        var tried = 0;
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            // A backend that DECLARES Stream and does not serve it answers from IModelProvider's defaulted
            // member, so it is indistinguishable from a capable one until it answers; it lands in the ordinary
            // pre-commit failure path with its own NotServed detail. The declaration is checked where it can be:
            // GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements.
            tried++;
            var attempt = new StreamAttempt();
            await foreach (var chunk in StreamAttemptAsync(provider, resolved, attempt, ct).ConfigureAwait(false))
                yield return chunk;
            if (attempt.Done) yield break;

            var failure = attempt.Failure!;
            var verdict = failure.Error ?? ProviderVerdict.Failed;
            failures.File(failure, verdict, failure.Detail);
            var action = _policy.ActionFor(verdict);
            if (action == FallbackAction.Surface)
            {
                yield return failure;
                yield break;
            }
            _bookkeeping.Penalize(_bookkeeping.Key(provider), action);
        }

        // Nothing produced a stream — still exactly one terminal chunk, so a consumer's loop shape is the same
        // whether five backends were tried or none existed; a reason somebody gave outranks a synthesized one
        if (failures.Reported is { } reported)
        {
            yield return reported;
            yield break;
        }
        var (synthesized, detail) = NothingServed(candidates, request, ProviderOperation.Stream, tried, benched);
        yield return MediaChunk.Failure(synthesized, detail);
    }

    /// <summary>ONE backend's stream, pumped to a terminal chunk. Sets <see cref="StreamAttempt.Done"/> where
    /// the router's own stream is over — a terminal reached the caller, or data committed and invariant 1
    /// forbids falling over — and otherwise leaves the pre-commit <see cref="StreamAttempt.Failure"/> for the
    /// caller's fallback decision.</summary>
    private async IAsyncEnumerable<MediaChunk> StreamAttemptAsync(
        IModelProvider streamer, MediaRequest resolved, StreamAttempt attempt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var committed = false;             // invariant 2: set only by a chunk carrying real DATA
        var closed = false;                // did the backend send a terminal chunk of its own?
        MediaChunk? failure = null;

        await using var chunks = StreamOpening.Deferred(() => streamer.StreamAsync(resolved, ct), ct).GetAsyncEnumerator(ct);
        while (true)
        {
            bool moved;
            try
            {
                moved = await chunks.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // the CALLER cancelled — never a backend fault, and never something to fall over
            }
            catch (Exception ex)
            {
                // Unconditional on purpose — NeverReachedTheBackend is the SUBMIT door's billing rule.
                // Here nothing is charged by the act of asking, so a refused connection before the first
                // byte is the ordinary pre-commit failure the contract says advances.
                failure = MediaChunk.Failure(ProviderVerdictClassifier.FromThrown(ex), $"{streamer.Id}: {ex.Message}");
                break;
            }

            if (!moved) break;
            var chunk = chunks.Current;

            if (chunk.Error is not null)
            {
                closed = true;
                if (committed) { attempt.Done = true; yield return chunk; yield break; }   // invariant 1: no fallback after commit
                failure = chunk;
                break;
            }

            if (chunk.Final)
            {
                closed = true;
                deadHosts?.RecordSuccess(_bookkeeping.Key(streamer));
                attempt.Done = true;
                yield return chunk;
                yield break;
            }

            if (chunk.Data is { Length: > 0 }) committed = true;
            yield return chunk;
        }

        // A stream that just STOPPED. The backend told the caller nothing about why, so this router says
        // it instead — the terminal-chunk guarantee is the platform's, not the backend's.
        if (failure is null && !closed)
        {
            if (committed)
            {
                deadHosts?.RecordSuccess(_bookkeeping.Key(streamer));
                attempt.Done = true;
                yield return MediaChunk.Completed();
                yield break;
            }
            failure = MediaChunk.Failure(ProviderVerdict.Failed,
                $"{streamer.Id}: the stream ended without producing data or a terminal chunk");
        }

        // Committed and then failed: the caller already holds bytes, so this is the answer whatever the
        // fallback policy says about the verdict.
        if (committed) { attempt.Done = true; yield return failure!; yield break; }
        attempt.Failure = failure;
    }

    /// <summary>One streaming attempt's outcome, carried out of <see cref="StreamAttemptAsync"/> because an
    /// async iterator has no return value to put it in.</summary>
    private sealed class StreamAttempt
    {
        /// <summary>The router's own stream is finished: the caller stops, with no fallback decision left to
        /// make and no synthesized terminal to add.</summary>
        public bool Done { get; set; }

        /// <summary>The PRE-COMMIT failure the caller weighs against the fallback policy — non-null wherever
        /// <see cref="Done"/> is false.</summary>
        public MediaChunk? Failure { get; set; }
    }

    /// <summary>The verdict and sentence for a run in which no backend answered for itself — reached only when
    /// nobody said anything of their own, which is why every door consults its two slots first. Nothing tried
    /// describes the roster: all benched is <see cref="ProviderVerdict.RateLimited"/>, none capable
    /// <see cref="ProviderVerdict.Unsupported"/>; something tried and silent is
    /// <see cref="ProviderVerdict.NotConfigured"/>.</summary>
    private static (ProviderVerdict Verdict, string Detail) NothingServed(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, ProviderOperation door, int tried,
        int benched) =>
        tried > 0 ? (ProviderVerdict.NotConfigured, "every capable backend reported it is not configured")
        : benched > 0 ? (ProviderVerdict.RateLimited,
            $"every capable media backend for kind '{request.Kind}' is on dead-host cooldown ({benched} of [{Ids(candidates)}])")
        : (ProviderVerdict.Unsupported,
            $"no capable media backend for kind '{request.Kind}' via {door} among [{Ids(candidates)}]");

    private static string Ids(IReadOnlyList<ProviderCandidate> candidates) =>
        string.Join(", ", candidates.Select(c => c.ProviderId));

    /// <summary>The first rejecting backend's own words, folded onto the synthesized "nobody took it" message,
    /// because that message is what <c>GenerationRenderJobHandler</c> fails the job with and what
    /// <c>GenerationSubmitTool</c> hands a model. A rejection with no reason still names WHO; a blameless one with
    /// none is never kept, since naming a backend merely skipped blamelessly would read as an accusation.
    /// <see cref="MediaSubmission.ProviderId"/> stays EMPTY regardless — "no candidate accepted".</summary>
    private static string Because(Rejection? rejection) => rejection switch
    {
        null => "",
        { Detail: var detail } when string.IsNullOrWhiteSpace(detail) =>
            $" — '{rejection.ProviderId}' rejected it without giving a reason",
        _ => $" — '{rejection.ProviderId}' said: {rejection.Detail}",
    };

    /// <summary>The two reporting slots every door keeps: the first SUBSTANTIVE failure — what the caller is
    /// told — and, apart from it, the first BLAMELESS one that still gave a reason, which answers only when nothing
    /// really failed (<c>docs/DECISIONS.md</c> D31). A blameless failure with an empty detail is not kept: the
    /// synthesized sentence says strictly more than it does.</summary>
    private sealed class FirstFailures<T> where T : class
    {
        private T? _substantive;
        private T? _blameless;

        /// <summary>What to report: the substantive failure, else the blameless one, else null.</summary>
        public T? Reported => _substantive ?? _blameless;

        public void File(T failure, ProviderVerdict verdict, string? detail)
        {
            if (!verdict.IsBlameless()) _substantive ??= failure;
            else if (!string.IsNullOrWhiteSpace(detail)) _blameless ??= failure;
        }
    }

    /// <summary>One backend attempt, wrapped in a span + duration/cost metrics. Per ATTEMPT, not per request:
    /// a trace of a fallback run has to show the attempt that failed as well as the one that worked.</summary>
    private async Task<MediaResponse> AttemptAsync(
        IModelProvider provider, MediaRequest request, CancellationToken ct)
    {
        // the permit is taken BEFORE the clock starts, so a queued render's wait never inflates the
        // backend's reported latency
        using var permit = await _bookkeeping.EnterAsync(provider, ct).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        using var span = LyntaiDiagnostics.StartGeneration("generate", provider.Id, request.Kind, request.Model);
        MediaResponse result;
        try
        {
            result = await provider.GenerateAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-initiated cancel is not a backend failure
        }
        catch (Exception ex)
        {
            // THE TRUST BOUNDARY. IModelProvider documents "a value with a verdict, never a throw", and
            // AddProvider is a documented BYO seam — so a backend that breaks that contract is a
            // case this router HANDLES rather than a case that cannot happen. Without this, one throwing
            // backend killed the whole chain: the healthy candidate was never tried, RecordGeneration never
            // fired so the attempt was invisible in telemetry, and the caller got a raw exception.
            // Classified through the shared taxonomy for the same reason the LLM side gives — hand-rolling
            // Failed here would hammer a rate-limited host instead of cooling it.
            result = MediaResponse.Failure(ProviderVerdictClassifier.FromThrown(ex), $"{provider.Id}: {ex.Message}");
        }
        result = result with { ProviderId = provider.Id };   // the router's word, whatever the backend set
        LyntaiDiagnostics.RecordGeneration(span, provider.Id, request.Kind, result.Verdict,
            // an Ok result always has artifacts (MediaResponse.Success enforces it), so the count is
            // reported even by a backend that returns no usage of its own
            result.IsOk ? result.Usage ?? new MediaUsage(Count: result.Artifacts.Count) : result.Usage,
            Stopwatch.GetElapsedTime(started).TotalSeconds, result.Detail);
        return result;
    }

    /// <summary>Whether this backend is benched — honouring the sole-candidate exemption, so the only capable
    /// backend is always tried.</summary>
    private bool IsBenched(IModelProvider provider, int capableCount) =>
        _bookkeeping.IsBenched(_bookkeeping.Key(provider), capableCount == 1, _policy.ExemptSoleCandidate);

    /// <summary>Candidates that exist, are registered, report themselves available, are DISTINCT, and DECLARE
    /// they can serve this request/delivery — with the candidate's model applied to the request when it pins
    /// one. Materialized because the sole-candidate cooldown exemption needs to know how many there are before
    /// trying the first.
    ///
    /// <para><b>Dedup happens on the RESOLVED pair, and it happens HERE.</b> Resolved, because that is the
    /// pair that decides what is actually called: <c>"fal"</c> and <c>"FAL"</c> select one provider (ids match
    /// case-insensitively), and a candidate pinning the model the request already names resolves to the same
    /// call as one that pins nothing. Here, because <see cref="MediaRoutingPolicy.ExemptSoleCandidate"/>
    /// reads the COUNT of this list — so two entries naming one backend would both re-attempt a backend that
    /// just failed AND make the sole capable backend look like two, silently withdrawing the exemption. A
    /// dedup applied after the count is taken fixes neither.</para>
    ///
    /// <para>The dedup itself is <see cref="TextRouter"/>'s (<see cref="CandidateDedup"/>) — first wins, order preserved
    /// — rather than a second copy of it here.</para></summary>
    private List<(IModelProvider Provider, MediaRequest Request)> Capable(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, ProviderOperation delivery) =>
        Capable(_providers, candidates, request, delivery);

    /// <summary>The capability filter over an explicit backend set — the one copy of it, which a caller that
    /// must know the answer BEFORE routing (the pipeline job choosing a stage's door) reads rather than
    /// re-deriving.</summary>
    internal static List<(IModelProvider Provider, MediaRequest Request)> Capable(
        IReadOnlyList<IModelProvider> providers, IReadOnlyList<ProviderCandidate> candidates,
        MediaRequest request, ProviderOperation delivery)
    {
        var resolved = new List<(IModelProvider Provider, MediaRequest Request)>();
        foreach (var candidate in candidates)
        {
            var provider = ProviderLookup.Find(providers, candidate.ProviderId);
            if (provider is null) continue;   // an unknown id is a config typo, not a crash
            // before the count, so an unavailable backend never withdraws the sole-candidate exemption
            if (!provider.IsAvailable) continue;

            resolved.Add((provider,
                candidate.Model is { Length: > 0 } model ? request with { Model = model } : request));
        }

        // capability LAST: a duplicate is dropped before it is asked, and the surviving count is the number
        // of distinct backends this request could actually reach
        var capable = new List<(IModelProvider, MediaRequest)>();
        foreach (var entry in CandidateDedup.Dedup(resolved, e => (e.Provider.Id, e.Request.Model)))
        {
            // Where a domain REQUEST becomes a generic capability query. The mapping is the whole of what
            // the generation domain adds: its kind, its model, and whether it carries input artifacts.
            if (entry.Provider.Capabilities.Supports(
                    entry.Request.Kind, delivery,
                    accepts: ProviderKinds.Text,
                    model: entry.Request.Model,
                    hasInputs: entry.Request.Inputs.Count > 0))
                capable.Add(entry);
        }
        return capable;
    }
}
