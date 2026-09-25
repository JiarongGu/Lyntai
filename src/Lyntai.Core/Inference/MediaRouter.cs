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
/// <see cref="Lyntai.Inference.IProviderIdentity.Id"/> and apply no admission, which is the historical
/// behaviour and correct for a single-configuration deployment. Supply one when several configurations of a
/// backend id are live at once — otherwise one tenant's rate limit benches every other tenant sharing that
/// backend, and two consumers of one downed self-hosted host fail to share a bench that would have spared
/// them both. <see cref="IProviderPool{TProvider}.TryGetKey"/> is the intended source.
///
/// <para><b>Must return a STABLE key for a given instance.</b> One routing attempt invokes it more than once
/// — for the bench check, for admission, and for the record that follows — so a delegate whose answer varies
/// between those calls would record cooldown under a key different from the one checked, producing a bench
/// that silently never takes effect. Look the key up (as a pool does); never recompute it from live
/// state.</para></param>
/// <param name="admission">Bounds concurrent attempts per configuration — for a locally-run engine where
/// simultaneous renders contend for one CPU or GPU. Null = unbounded. Applied HERE rather than by
/// wrapping a provider, because a wrapper implementing only <see cref="IModelProvider"/> erases the
/// optional capability interfaces (<see cref="IMediaJobProvider"/>) this router type-tests, which
/// would silently stop every queued render from routing.
///
/// <para><b><see cref="GenerateAsync"/> and <see cref="SubmitAsync"/> only —
/// <see cref="StreamAsync"/> is deliberately NOT gated</b>, the same carve-out
/// <c>TextRouter</c> states for its own streaming path and for the same reason: a stream holds its permit
/// for the whole response, so a consumer that simply stops enumerating would pin it until the enumerator is
/// finally disposed. Bounding a long-lived stream needs a lease the consumer cannot forget, which this is
/// not.</para></param>
public sealed class MediaRouter(
    IEnumerable<IModelProvider> providers,
    MediaRoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null,
    Func<IModelProvider, ProviderKey?>? configuration = null,
    IProviderAdmission? admission = null) : IMediaRouter
{
    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];
    private readonly MediaRoutingPolicy _policy = policy ?? new MediaRoutingPolicy();

    // resolved once: the no-delegate case must cost nothing per attempt, and a null-returning delegate must
    // be indistinguishable from no delegate at all
    private readonly Func<IModelProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    /// <inheritdoc/>
    public async Task<MediaResponse> GenerateAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, ProviderOperation.Complete);
        MediaResponse? firstFailure = null;     // the first SUBSTANTIVE failure — what the caller is told
        MediaResponse? firstBlameless = null;   // …kept apart, so it answers only when nothing really failed
        var tried = 0;
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            tried++;
            var result = await AttemptAsync(provider, resolved, ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                deadHosts?.RecordSuccess(CooldownKey(provider));
                return result;
            }

            // not-configured / unsupported aren't faults worth reporting over a real failure — but every
            // other verdict is remembered BEFORE the surface check, so advancing past one (a host that
            // configured Refused -> Advance) still reports it when nothing else succeeds
            if (!result.Verdict.IsBlameless())
                firstFailure ??= result;
            // …and a blameless backend that EXPLAINED itself goes in the other slot. It can never mask a real
            // failure (the return below settles that), but "your prompt is too long for me" beats a synthetic
            // "nothing is configured" when it is the only thing that happened. A blameless result with an
            // EMPTY detail is skipped deliberately: the synthetic sentence says strictly more than it does.
            else if (!string.IsNullOrWhiteSpace(result.Detail))
                firstBlameless ??= result;

            switch (_policy.ActionFor(result.Verdict))
            {
                case FallbackAction.Surface:
                    return result;
                case FallbackAction.CooldownAndAdvance:
                    deadHosts?.MarkDead(CooldownKey(provider));
                    break;
                case FallbackAction.PenalizeAndAdvance:
                    deadHosts?.RecordFailure(CooldownKey(provider));
                    break;
            }
        }

        if (tried == 0)
            return MediaResponse.Failure(
                benched > 0 ? ProviderVerdict.RateLimited : ProviderVerdict.Unsupported,
                benched > 0
                    ? $"every capable media backend for kind '{request.Kind}' is on dead-host cooldown " +
                      $"({benched} of [{string.Join(", ", candidates.Select(c => c.ProviderId))}])"
                    : $"no capable media backend for kind '{request.Kind}' via {ProviderOperation.Complete} " +
                      $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]");

        // a real failure outranks a blameless reason; with no real failure the blameless backend's own words
        // are the honest answer (a host turns "not configured" into a setup prompt, and "too long" into a
        // shorter prompt), and only a run in which nothing said anything at all falls through to the
        // synthetic reply. Same three-slot rule as TextRouter.CompleteAsync's last ?? lastBlameless ?? …
        return firstFailure ?? firstBlameless ?? MediaResponse.Failure(ProviderVerdict.NotConfigured,
            "every capable backend reported it is not configured");
    }

    /// <inheritdoc/>
    public async Task<MediaSubmission> SubmitAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, ProviderOperation.Queued);
        var benched = 0;

        // the FIRST substantive rejection, kept the way GenerateAsync keeps its firstFailure: the backend
        // that explained why is the only thing in this run a caller can act on, and the synthesized message
        // below is otherwise a list of ids that says nothing about what went wrong
        (string ProviderId, string? Detail, ProviderVerdict Verdict)? firstFailure = null;

        // …and the first BLAMELESS rejection that still gave a reason, in the second slot GenerateAsync keeps
        // for the same purpose: reported ONLY when no substantive rejection happened, so "nothing is set up"
        // never masks "the one you configured refused the job". Without it, every reason a blameless verdict
        // carries — a queue saying the prompt is too long, one saying which key it wants — is dropped and the
        // job handler fails the job with a bare list of candidate ids.
        (string ProviderId, string? Detail, ProviderVerdict Verdict)? firstBlameless = null;
        var attempted = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (provider is not IMediaJobProvider job) continue;   // capability says Job; the type must agree
            if (IsBenched(provider, capable.Count)) { benched++; continue; }
            attempted++;

            // submitting is what commits the money, so it respects the same bound as an inline render. The
            // permit is scoped to THIS iteration: a submission that fails releases it before the next
            // candidate is tried.
            using (await EnterAdmissionAsync(provider, ct).ConfigureAwait(false))
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
                catch (Exception ex) when (!NeverReachedTheBackend(ex))
                {
                    // A throw during SUBMIT that MAY have been delivered is INCONCLUSIVE — the backend may
                    // already hold a billable render, so advancing to the next candidate would buy the same
                    // generation twice.
                    //
                    // A throw that provably never left this process is NOT caught here (see the filter): it
                    // committed nothing, so it propagates and the durable-job runner applies its ordinary
                    // retry.
                    operation = new QueuedOperation("", QueuedOperationStatus.Failed,
                        Detail: $"{provider.Id}: {ex.Message}") { Inconclusive = true };
                }
                LyntaiDiagnostics.RecordSubmission(span, provider.Id, resolved.Kind, operation.Id, operation.Status,
                    Stopwatch.GetElapsedTime(started).TotalSeconds, operation.Inconclusive);

                if (operation.Status != QueuedOperationStatus.Failed)
                {
                    deadHosts?.RecordSuccess(CooldownKey(provider));
                    return new MediaSubmission(provider.Id, operation);
                }

                // An INCONCLUSIVE submission SURFACES, like a refusal does: the backend never answered, so it
                // may already hold a billable render, and trying the next candidate would buy the same
                // generation twice — the duplicate-payment case the job handler's checkpoint-first ordering
                // exists to prevent. The provider id rides along because "who might have it?" is the only
                // question worth asking next. No RecordFailure either: no answer is no evidence of ill health,
                // and benching a working backend on a slow network helps nobody.
                if (operation.Inconclusive) return new MediaSubmission(provider.Id, operation);

                // A rejected submission gets a VERDICT, because "advance and always take a dead-host strike"
                // is wrong for the same reason it is wrong inline: an unconfigured queue backend
                // (FalQueueProvider answers "not configured: …" before it opens a socket) would be penalised
                // on every attempt for a fact known before the call — precisely the harm NotConfigured was
                // introduced to prevent (docs/DECISIONS.md D31). The backend's own verdict wins where it gives
                // one; otherwise it comes from classifying the backend's words through the shared corpus, and
                // an unclassifiable rejection still lands on Failed, which is what it did before.
                var verdict = operation.Verdict ?? ProviderVerdictClassifier.FromErrorText(operation.Detail);

                // blameless verdicts do not outrank reasons — remembered apart, exactly as GenerateAsync does,
                // so "nothing is set up" never masks "the one you configured refused the job", while a
                // blameless rejection that explained itself is still better than a list of ids
                if (!verdict.IsBlameless())
                    firstFailure ??= (provider.Id, operation.Detail, verdict);
                else if (!string.IsNullOrWhiteSpace(operation.Detail))
                    firstBlameless ??= (provider.Id, operation.Detail, verdict);

                switch (_policy.ActionFor(verdict))
                {
                    case FallbackAction.Surface:
                        // a content refusal is the backend judging the PROMPT, and re-submitting it to the
                        // next vendor is not a library's decision — the same rule the inline path follows,
                        // and overridable the same way (On(Refused, Advance)). ProviderId stays empty: it
                        // means "no candidate accepted", and a refusal is a refusal, not an acceptance.
                        return new MediaSubmission("", operation with { Verdict = verdict });
                    case FallbackAction.CooldownAndAdvance:
                        deadHosts?.MarkDead(CooldownKey(provider));
                        break;
                    case FallbackAction.PenalizeAndAdvance:
                        // a queue that won't take the job is exactly the dead-host case the LLM side benches
                        // for: the next submission a second later has no reason to fare better
                        deadHosts?.RecordFailure(CooldownKey(provider));
                        break;
                }
            }
        }

        // the verdict is chosen as GenerateAsync chooses the one it reports, so "nobody could serve it" says why
        var reported = firstFailure ?? firstBlameless;
        return new MediaSubmission("", new QueuedOperation("", QueuedOperationStatus.Failed,
            Detail: (benched > 0
                ? $"every capable media backend for a '{request.Kind}' job is on dead-host cooldown"
                : $"no capable media backend accepted a '{request.Kind}' job among " +
                  $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]") +
                Because(reported is { } r ? (r.ProviderId, r.Detail) : null))
        {
            Verdict = reported?.Verdict ?? (attempted > 0 ? ProviderVerdict.NotConfigured
                : benched > 0 ? ProviderVerdict.RateLimited : ProviderVerdict.Unsupported),
        });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, ProviderOperation.Stream);
        MediaChunk? firstFailure = null;     // the first SUBSTANTIVE failure — the same two-slot rule
        MediaChunk? firstBlameless = null;   // …GenerateAsync follows, so all three doors answer alike
        var tried = 0;
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            // A backend that DECLARES Stream and does not serve it is still a configuration fault — the
            // router is the trust boundary for what a third-party backend claims about itself — but it is no
            // longer one this door can see before calling. Until D127 there was a separate streaming
            // interface and a type test here; the collapse made `StreamAsync(MediaRequest, …)` a
            // DEFAULT interface member answering Unsupported, so such a backend is now indistinguishable
            // from a capable one until it answers, and it lands in the ordinary pre-commit failure path
            // below carrying its own `NotServed` detail. The declaration is checked where it can be:
            // `GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements`.
            tried++;
            var attempt = new StreamAttempt();
            await foreach (var chunk in StreamAttemptAsync(provider, resolved, attempt, ct).ConfigureAwait(false))
                yield return chunk;
            if (attempt.Done) yield break;

            var failure = attempt.Failure!;
            var verdict = failure.Error ?? ProviderVerdict.Failed;
            if (!verdict.IsBlameless()) firstFailure ??= failure;
            else if (!string.IsNullOrWhiteSpace(failure.Detail)) firstBlameless ??= failure;

            switch (_policy.ActionFor(verdict))
            {
                case FallbackAction.Surface:
                    yield return failure;
                    yield break;
                case FallbackAction.CooldownAndAdvance:
                    deadHosts?.MarkDead(CooldownKey(provider));
                    break;
                case FallbackAction.PenalizeAndAdvance:
                    deadHosts?.RecordFailure(CooldownKey(provider));
                    break;
            }
        }

        // Nothing produced a stream — still exactly one terminal chunk, so a consumer's loop shape is the
        // same whether five backends were tried or none existed.
        //
        // A REASON SOMEBODY GAVE OUTRANKS A SYNTHESIZED ONE. Checking the two slots first keeps a backend's
        // own words — the only sentence in the run that names the actual problem — from being replaced by
        // "no capable media backend for kind …", which describes the roster rather than the failure.
        yield return firstFailure ?? firstBlameless ?? NothingStreamed(candidates, request, tried, benched);
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
                deadHosts?.RecordSuccess(CooldownKey(streamer));
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
                deadHosts?.RecordSuccess(CooldownKey(streamer));
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

    /// <summary>The synthesized terminal for a run in which no backend produced a stream. Reached only when
    /// nobody said anything of their own, which is why the caller consults its two slots first.</summary>
    private static MediaChunk NothingStreamed(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, int tried, int benched) =>
        tried == 0
            ? MediaChunk.Failure(
                benched > 0 ? ProviderVerdict.RateLimited : ProviderVerdict.Unsupported,
                benched > 0
                    ? $"every capable media backend for kind '{request.Kind}' is on dead-host cooldown " +
                      $"({benched} of [{string.Join(", ", candidates.Select(c => c.ProviderId))}])"
                    : $"no capable media backend for kind '{request.Kind}' via {ProviderOperation.Stream} " +
                      $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]")
            : MediaChunk.Failure(ProviderVerdict.NotConfigured,
                "every capable backend reported it is not configured");

    /// <summary>The first rejecting backend's own words, folded onto the synthesized "nobody took it" message
    /// — the first SUBSTANTIVE rejection where there was one, otherwise the first blameless rejection that
    /// still gave a reason. Without it the only thing that survives a failed submission is a list of candidate
    /// ids — and that is what <c>GenerationRenderJobHandler</c> fails the job with and what
    /// <c>GenerationSubmitTool</c> hands a model, neither of which can act on "these three didn't work".
    /// <para>The reasonless branch below is therefore reached only by a SUBSTANTIVE rejection that said
    /// nothing: a blameless one with an empty detail is never remembered at all, because naming a backend
    /// that was merely skipped blamelessly would read as an accusation.</para>
    /// <para><see cref="MediaSubmission.ProviderId"/> stays EMPTY regardless: <see cref="IMediaRouter"/>
    /// documents empty as "no candidate accepted", and both callers branch on it. The id belongs in the
    /// sentence, not in that field.</para></summary>
    private static string Because((string ProviderId, string? Detail)? failure)
    {
        // nothing was even attempted (every entry was incapable, not job-capable, or benched)
        if (!failure.HasValue) return "";

        var (providerId, detail) = failure.Value;
        // a rejection with no reason still names WHO, which is more than the id list says
        return string.IsNullOrWhiteSpace(detail)
            ? $" — '{providerId}' rejected it without giving a reason"
            : $" — '{providerId}' said: {detail}";
    }

    /// <summary>One backend attempt, wrapped in a span + duration/cost metrics. Per ATTEMPT, not per request:
    /// a trace of a fallback run has to show the attempt that failed as well as the one that worked.</summary>
    private async Task<MediaResponse> AttemptAsync(
        IModelProvider provider, MediaRequest request, CancellationToken ct)
    {
        // the permit is taken BEFORE the clock starts, so a queued render's wait never inflates the
        // backend's reported latency
        using var permit = await EnterAdmissionAsync(provider, ct).ConfigureAwait(false);

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

    /// <summary>Whether a thrown exception proves the request never left this process — a refused
    /// connection, a name that would not resolve, a TLS handshake that never completed. Nothing was
    /// submitted, so nothing was charged.
    ///
    /// <para>Used ONLY on the submit path, and only to decide whether to swallow the throw at all. A submit
    /// that may have been delivered becomes an <c>Inconclusive</c> result this router surfaces rather than
    /// advancing past; one that provably was not propagates untouched, so the durable-job runner applies its
    /// ordinary retry. Getting this wrong in the safe-looking direction — treating everything as ambiguous —
    /// converts a transient blip into a dead-lettered job, which is what the first version of this catch
    /// did. The inline <see cref="GenerateAsync"/> path needs none of this: nothing there is billed by the
    /// act of asking.</para></summary>
    private static bool NeverReachedTheBackend(Exception ex) => ex switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError
                                              or HttpRequestError.NameResolutionError
                                              or HttpRequestError.SecureConnectionError } => true,
        System.Net.Sockets.SocketException => true,
        // Deliberately NOT a catch-all: a timeout, a protocol error mid-response, or anything unrecognised
        // may have been delivered, and the expensive mistake is assuming it was not.
        _ => false,
    };

    /// <summary>Take a concurrency permit for this provider's CONFIGURATION, or nothing at all when no
    /// admission is wired or the configuration is unknown. Never returns a handle the caller may skip
    /// disposing: a permit that is not returned pins its gate for the life of the process, so every call site
    /// scopes the result with <c>using</c> and lets success, failure, a throw and cancellation all release
    /// it the same way.</summary>
    private async ValueTask<IDisposable?> EnterAdmissionAsync(IModelProvider provider, CancellationToken ct) =>
        admission is not null && _configuration(provider) is { } key
            ? await admission.EnterAsync(key, ct).ConfigureAwait(false)
            : null;

    /// <summary>Whether this backend is benched — honouring the sole-candidate exemption, so the only capable
    /// backend is always tried.</summary>
    private bool IsBenched(IModelProvider provider, int capableCount) =>
        deadHosts is not null &&
        !(capableCount == 1 && _policy.ExemptSoleCandidate) &&
        deadHosts.IsDead(CooldownKey(provider));

    /// <summary>The tracker is shared with <see cref="TextRouter"/>, so keys carry their domain: a host with a chat
    /// provider and an image backend both called "openai" must not have one bench the other.
    ///
    /// <para>Within the domain the key is the CONFIGURATION when one is known, falling back to the backend id
    /// otherwise — the two configurations of one backend id that a pool keeps live must bench independently,
    /// while two consumers of one downed self-hosted host must share a bench.</para></summary>
    private string CooldownKey(IModelProvider provider) =>
        $"generation::{_configuration(provider)?.ToString() ?? provider.Id}";

    /// <summary>Candidates that exist, are registered, are DISTINCT, and DECLARE they can serve this
    /// request/delivery — with the candidate's model applied to the request when it pins one. Materialized
    /// because the sole-candidate cooldown exemption needs to know how many there are before trying the
    /// first.
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
            var provider = providers.FirstOrDefault(p =>
                string.Equals(p.Id, candidate.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is null) continue;   // an unknown id is a config typo, not a crash

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
