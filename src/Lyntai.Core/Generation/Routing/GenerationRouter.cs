using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Lyntai.Lifecycle;
using Lyntai.Llm.Routing;

namespace Lyntai.Generation.Routing;

/// <summary>The default <see cref="IGenerationRouter"/>: capability pre-filter, then verdict-driven fallback
/// with dead-host cooldown, plus a span and metrics per attempt.
///
/// Fallback semantics come from <see cref="GenerationRoutingPolicy"/>, whose defaults follow the SHAPE of the
/// LLM router's (design §6) — a content refusal surfaces, a rate limit or a rejected key benches, a fault
/// penalizes — and deliberately differ on <see cref="GenerationVerdict.Unsupported"/>, which advances here and
/// surfaces there (<see cref="GenerationVerdictClassifier"/> states why: chat candidates share a capability
/// gap, media backends do not):
/// <list type="bullet">
/// <item><see cref="GenerationVerdict.Refused"/> SURFACES — a content refusal is not a transport fault, and
///   quietly re-submitting a refused prompt to another vendor is not a library's decision. A host that
///   deliberately pairs a hosted backend with a permissive local one can override exactly that.</item>
/// <item><see cref="GenerationVerdict.NotConfigured"/> and <see cref="GenerationVerdict.Unsupported"/> ADVANCE without
///   blame — the backend isn't broken, it just isn't the one for this request.</item>
/// <item><see cref="GenerationVerdict.RateLimited"/> / <see cref="GenerationVerdict.AuthFailed"/> BENCH the backend for
///   the cooldown window; a timeout or an outright failure counts toward the threshold.</item>
/// <item>everything else advances, keeping the FIRST substantive failure as the reported reason: the first
///   backend's error explains the run better than the last one's.</item>
/// </list>
///
/// <para><b>Reporting keeps TWO slots, and a blameless one never outranks a real failure.</b> The first
/// substantive failure is what the caller is told; the first BLAMELESS result that actually explained itself
/// is remembered apart and reported only when nothing substantive failed at all. Without the first half,
/// <c>[downHost → Failed, neverConfigured → NotConfigured]</c> would send a caller off to set up a key while
/// the backend they HAD configured is the one that is down (<c>docs/DECISIONS.md</c> D31). Without the second,
/// a run in which every candidate said "your prompt is too long for me" was answered with a synthetic "every
/// capable backend reported it is not configured" — neither true nor actionable, and what forced
/// <c>ContextWindowExceeded</c> to translate to a BLAMING verdict to stay reportable at all
/// (<c>TASKS.md</c> Part 40). <c>LlmRouter.CompleteAsync</c> holds the same two slots — one answer per
/// situation across both domains, so change one and check the other.</para>
///
/// <para><b>Submission has one rule of its own:</b> a failed submission marked
/// <see cref="GenerationOperation.Inconclusive"/> SURFACES rather than advancing, because a backend that never
/// answered may already hold a billable render and the next candidate would buy the same generation
/// twice.</para>
///
/// <para><b>The policy governs submission too, at one remove.</b> A submission comes back carrying a
/// <see cref="GenerationOperationStatus"/>, not a verdict, so a rejected one is classified from the backend's
/// own <see cref="GenerationOperation.Detail"/> through <see cref="GenerationVerdictClassifier"/> and then
/// answered by the same table — a rate limit benches, an unconfigured backend advances blamelessly, an
/// unclassifiable rejection counts toward the threshold as it always did. Two things stay true regardless of
/// what the text says: the Inconclusive case above is decided BEFORE the verdict is, and a submission the
/// router does not accept reports an EMPTY <see cref="GenerationSubmission.ProviderId"/> — which
/// <see cref="IGenerationRouter"/> defines as "no candidate accepted" — with the first rejection's backend and
/// reason folded into the detail instead. Which rejection that is follows the two-slot rule above: the first
/// SUBSTANTIVE one, or a blameless one that still gave a reason when there was no substantive one.</para>
///
/// <para><b>The candidate list is deduped on the RESOLVED (backend, model) pair</b> before anything is
/// attempted, so a repeated spec is one candidate — and, the sharper half, two entries naming one backend no
/// longer inflate the count <see cref="GenerationRoutingPolicy.ExemptSoleCandidate"/> reads.</para></summary>
/// <param name="providers">The registered backends.</param>
/// <param name="policy">Per-verdict fallback behaviour; null = the defaults above.</param>
/// <param name="deadHosts">Cooldown bookkeeping — the SAME <see cref="DeadHostTracker"/> the LLM router uses,
/// deliberately: "this host keeps failing, stop asking" is transport bookkeeping keyed by a string, not an LLM
/// concept, and a second copy would be a second set of bugs (and a second threshold to configure). A media key
/// is prefixed <c>generation::</c> while a chat key is the BARE provider/configuration identity (plus
/// <c>::model</c> under <see cref="CooldownScope.ProviderAndModel"/>) — one prefixed namespace and one
/// unprefixed one cannot collide, so a chat outage never benches a generation backend that happens to share its
/// id. Null disables cooldown entirely (a hand-built router in a test).</param>
/// <param name="configuration">Which CONFIGURATION a provider is running under, used to key dead-host
/// cooldown and admission. Null (or a null return) = key cooldown on
/// <see cref="Lyntai.Lifecycle.IProviderIdentity.Id"/> and apply no admission, which is the historical
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
/// wrapping a provider, because a wrapper implementing only <see cref="IGenerationProvider"/> erases the
/// optional capability interfaces (<see cref="IGenerationJobProvider"/>) this router type-tests, which
/// would silently stop every queued render from routing.</param>
public sealed class GenerationRouter(
    IEnumerable<IGenerationProvider> providers,
    GenerationRoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null,
    Func<IGenerationProvider, ProviderKey?>? configuration = null,
    IProviderAdmission? admission = null) : IGenerationRouter
{
    private readonly IReadOnlyList<IGenerationProvider> _providers = [.. providers];
    private readonly GenerationRoutingPolicy _policy = policy ?? new GenerationRoutingPolicy();

    // resolved once: the no-delegate case must cost nothing per attempt, and a null-returning delegate must
    // be indistinguishable from no delegate at all
    private readonly Func<IGenerationProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, GenerationDelivery.Inline);
        GenerationResult? firstFailure = null;     // the first SUBSTANTIVE failure — what the caller is told
        GenerationResult? firstBlameless = null;   // …kept apart, so it answers only when nothing really failed
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
            if (!IsBlameless(result.Verdict))
                firstFailure ??= result;
            // …and a blameless backend that EXPLAINED itself goes in the other slot. It can never mask a real
            // failure (the return below settles that), but "your prompt is too long for me" beats a synthetic
            // "nothing is configured" when it is the only thing that happened. A blameless result with an
            // EMPTY detail is skipped deliberately: the synthetic sentence says strictly more than it does.
            else if (!string.IsNullOrWhiteSpace(result.Detail))
                firstBlameless ??= result;

            switch (_policy.ActionFor(result.Verdict))
            {
                case GenerationFallbackAction.Surface:
                    return result;
                case GenerationFallbackAction.CooldownAndAdvance:
                    deadHosts?.MarkDead(CooldownKey(provider));
                    break;
                case GenerationFallbackAction.PenalizeAndAdvance:
                    deadHosts?.RecordFailure(CooldownKey(provider));
                    break;
            }
        }

        if (tried == 0)
            return GenerationResult.Failure(
                benched > 0 ? GenerationVerdict.RateLimited : GenerationVerdict.Unsupported,
                benched > 0
                    ? $"every capable media backend for kind '{request.Kind}' is on dead-host cooldown " +
                      $"({benched} of [{string.Join(", ", candidates.Select(c => c.ProviderId))}])"
                    : $"no capable media backend for kind '{request.Kind}' via {GenerationDelivery.Inline} " +
                      $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]");

        // a real failure outranks a blameless reason; with no real failure the blameless backend's own words
        // are the honest answer (a host turns "not configured" into a setup prompt, and "too long" into a
        // shorter prompt), and only a run in which nothing said anything at all falls through to the
        // synthetic reply. Same three-slot rule as LlmRouter.CompleteAsync's last ?? lastBlameless ?? …
        return firstFailure ?? firstBlameless ?? GenerationResult.Failure(GenerationVerdict.NotConfigured,
            "every capable backend reported it is not configured");
    }

    /// <inheritdoc/>
    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, GenerationDelivery.Job);
        var benched = 0;

        // the FIRST substantive rejection, kept the way GenerateAsync keeps its firstFailure: the backend
        // that explained why is the only thing in this run a caller can act on, and the synthesized message
        // below is otherwise a list of ids that says nothing about what went wrong
        (string ProviderId, string? Detail)? firstFailure = null;

        // …and the first BLAMELESS rejection that still gave a reason, in the second slot GenerateAsync keeps
        // for the same purpose: reported ONLY when no substantive rejection happened, so "nothing is set up"
        // never masks "the one you configured refused the job". Without it, every reason a blameless verdict
        // carries — a queue saying the prompt is too long, one saying which key it wants — is dropped and the
        // job handler fails the job with a bare list of candidate ids.
        (string ProviderId, string? Detail)? firstBlameless = null;

        foreach (var (provider, resolved) in capable)
        {
            if (provider is not IGenerationJobProvider job) continue;   // capability says Job; the type must agree
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            // submitting is what commits the money, so it respects the same bound as an inline render. The
            // permit is scoped to THIS iteration: a submission that fails releases it before the next
            // candidate is tried.
            using (await EnterAdmissionAsync(provider, ct).ConfigureAwait(false))
            {
                var started = Stopwatch.GetTimestamp();
                using var span = LyntaiDiagnostics.StartGeneration("submit", provider.Id, resolved.Kind, resolved.Model);
                GenerationOperation operation;
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
                    // A throw during SUBMIT that MAY have been delivered is INCONCLUSIVE, and that is the one
                    // place this path deliberately differs from the inline one above. Submitting is what
                    // commits the money: the backend may already hold a billable render, so advancing to the
                    // next candidate would buy the same generation twice — the duplicate-payment case the
                    // Inconclusive arm below exists to prevent.
                    //
                    // A throw that provably never left this process is NOT caught here (see the filter): it
                    // committed nothing, so it propagates and the durable-job runner retries it exactly as it
                    // did before this router had any catch at all. Round 2 of the 3.0 review caught the first
                    // version swallowing those too, which turned a connection-refused blip during a deploy
                    // into a permanently dead-lettered job — a regression, and on the same distinction this
                    // round taught FalQueueProvider one file over.
                    operation = new GenerationOperation("", GenerationOperationStatus.Failed,
                        Detail: $"{provider.Id}: {ex.Message}") { Inconclusive = true };
                }
                LyntaiDiagnostics.RecordSubmission(span, provider.Id, resolved.Kind, operation.Id, operation.Status,
                    Stopwatch.GetElapsedTime(started).TotalSeconds, operation.Inconclusive);

                if (operation.Status != GenerationOperationStatus.Failed)
                {
                    deadHosts?.RecordSuccess(CooldownKey(provider));
                    return new GenerationSubmission(provider.Id, operation);
                }

                // An INCONCLUSIVE submission SURFACES, like a refusal does: the backend never answered, so it
                // may already hold a billable render, and trying the next candidate would buy the same
                // generation twice — the duplicate-payment case the job handler's checkpoint-first ordering
                // exists to prevent. The provider id rides along because "who might have it?" is the only
                // question worth asking next. No RecordFailure either: no answer is no evidence of ill health,
                // and benching a working backend on a slow network helps nobody.
                if (operation.Inconclusive) return new GenerationSubmission(provider.Id, operation);

                // A rejected submission gets a VERDICT, because "advance and always take a dead-host strike"
                // is wrong for the same reason it is wrong inline: an unconfigured queue backend
                // (FalQueueProvider answers "not configured: …" before it opens a socket) would be penalised
                // on every attempt for a fact known before the call — precisely the harm NotConfigured was
                // introduced to prevent (docs/DECISIONS.md D31). An operation carries a STATUS, not a verdict,
                // so the verdict comes from classifying the backend's own words through the shared corpus;
                // an unclassifiable rejection still lands on Failed, which is what it did before.
                var verdict = GenerationVerdictClassifier.FromErrorText(operation.Detail);

                // blameless verdicts do not outrank reasons — remembered apart, exactly as GenerateAsync does,
                // so "nothing is set up" never masks "the one you configured refused the job", while a
                // blameless rejection that explained itself is still better than a list of ids
                if (!IsBlameless(verdict))
                    firstFailure ??= (provider.Id, operation.Detail);
                else if (!string.IsNullOrWhiteSpace(operation.Detail))
                    firstBlameless ??= (provider.Id, operation.Detail);

                switch (_policy.ActionFor(verdict))
                {
                    case GenerationFallbackAction.Surface:
                        // a content refusal is the backend judging the PROMPT, and re-submitting it to the
                        // next vendor is not a library's decision — the same rule the inline path follows,
                        // and overridable the same way (On(Refused, Advance)). ProviderId stays empty: it
                        // means "no candidate accepted", and a refusal is a refusal, not an acceptance.
                        return new GenerationSubmission("", operation);
                    case GenerationFallbackAction.CooldownAndAdvance:
                        deadHosts?.MarkDead(CooldownKey(provider));
                        break;
                    case GenerationFallbackAction.PenalizeAndAdvance:
                        // a queue that won't take the job is exactly the dead-host case the LLM side benches
                        // for: the next submission a second later has no reason to fare better
                        deadHosts?.RecordFailure(CooldownKey(provider));
                        break;
                }
            }
        }

        return new GenerationSubmission("", new GenerationOperation("", GenerationOperationStatus.Failed,
            Detail: (benched > 0
                ? $"every capable media backend for a '{request.Kind}' job is on dead-host cooldown"
                : $"no capable media backend accepted a '{request.Kind}' job among " +
                  $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]") +
                Because(firstFailure ?? firstBlameless)));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<GenerationChunk> StreamAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, GenerationDelivery.Stream);
        GenerationChunk? firstFailure = null;     // the first SUBSTANTIVE failure — the same two-slot rule
        GenerationChunk? firstBlameless = null;   // …GenerateAsync follows, so all three doors answer alike
        var tried = 0;
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            // Declaring Stream in Capabilities and not implementing the seam is a configuration fault, not a
            // crash: the router is the trust boundary for what a third-party backend claims about itself.
            if (provider is not IGenerationStreamProvider streamer)
            {
                firstBlameless ??= GenerationChunk.Failure(GenerationVerdict.Unsupported,
                    $"{provider.Id}: advertises {GenerationDelivery.Stream} delivery but does not implement " +
                    $"{nameof(IGenerationStreamProvider)}");
                continue;
            }

            tried++;
            var committed = false;             // invariant 2: set only by a chunk carrying real DATA
            var closed = false;                // did the backend send a terminal chunk of its own?
            GenerationChunk? failure = null;

            await using var chunks = streamer.StreamAsync(resolved, ct).GetAsyncEnumerator(ct);
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
                catch (Exception ex) when (!NeverReachedTheBackend(ex))
                {
                    failure = GenerationChunk.Failure(ClassifyThrown(ex), $"{provider.Id}: {ex.Message}");
                    break;
                }

                if (!moved) break;
                var chunk = chunks.Current;

                if (chunk.Error is not null)
                {
                    closed = true;
                    if (committed) { yield return chunk; yield break; }   // invariant 1: no fallback after commit
                    failure = chunk;
                    break;
                }

                if (chunk.Final)
                {
                    closed = true;
                    deadHosts?.RecordSuccess(CooldownKey(provider));
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
                    deadHosts?.RecordSuccess(CooldownKey(provider));
                    yield return GenerationChunk.Completed();
                    yield break;
                }
                failure = GenerationChunk.Failure(GenerationVerdict.Failed,
                    $"{provider.Id}: the stream ended without producing data or a terminal chunk");
            }

            // Committed and then failed: the caller already holds bytes, so this is the answer whatever the
            // fallback policy says about the verdict.
            if (committed) { yield return failure!; yield break; }

            var verdict = failure!.Error ?? GenerationVerdict.Failed;
            if (!IsBlameless(verdict)) firstFailure ??= failure;
            else if (!string.IsNullOrWhiteSpace(failure.Detail)) firstBlameless ??= failure;

            switch (_policy.ActionFor(verdict))
            {
                case GenerationFallbackAction.Surface:
                    yield return failure;
                    yield break;
                case GenerationFallbackAction.CooldownAndAdvance:
                    deadHosts?.MarkDead(CooldownKey(provider));
                    break;
                case GenerationFallbackAction.PenalizeAndAdvance:
                    deadHosts?.RecordFailure(CooldownKey(provider));
                    break;
            }
        }

        // Nothing produced a stream — still exactly one terminal chunk, so a consumer's loop shape is the
        // same whether five backends were tried or none existed.
        //
        // A REASON SOMEBODY GAVE OUTRANKS A SYNTHESIZED ONE, and the ordering here is not the inline door's
        // for a reason that only exists on this path: a backend can be disqualified WITHOUT being called (it
        // advertised Stream and does not implement the seam), so `tried == 0` no longer implies "nothing was
        // learned". Checking the two slots first is what keeps that backend's own words — the only sentence
        // in the run that names the actual problem — from being replaced by "no capable media backend".
        yield return firstFailure ?? firstBlameless ?? (tried == 0
            ? GenerationChunk.Failure(
                benched > 0 ? GenerationVerdict.RateLimited : GenerationVerdict.Unsupported,
                benched > 0
                    ? $"every capable media backend for kind '{request.Kind}' is on dead-host cooldown " +
                      $"({benched} of [{string.Join(", ", candidates.Select(c => c.ProviderId))}])"
                    : $"no capable media backend for kind '{request.Kind}' via {GenerationDelivery.Stream} " +
                      $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]")
            : GenerationChunk.Failure(GenerationVerdict.NotConfigured,
                "every capable backend reported it is not configured"));
    }

    /// <summary>Verdicts that are not FAULTS — the backend simply wasn't the one for this request, so it is
    /// remembered apart from real failures and reported only when nothing really failed.
    ///
    /// <para>Without it, <c>[configuredBackendRefusedTheJob, neverConfigured]</c> tells the caller "nothing is
    /// configured" and sends them to set up a key, while the backend they HAD configured is the one that
    /// declined.</para>
    ///
    /// <para><b>Deliberately the same two verdicts as <c>LlmRouter.IsBlameless</c></b>, which is the point:
    /// one answer per situation across both domains. It cannot be one shared function — the two domains have
    /// separate verdict enums — so it is the same NAMED predicate on each side instead, and this sentence is
    /// what connects them. It was inlined at both call sites here until the 3.0 pre-freeze review: the LLM
    /// side had the rule named and documented while this side had two unnamed copies of the pattern, so the
    /// parity the other side's docblock ASSERTS was anchored to nothing a reader or a rename could
    /// follow.</para>
    ///
    /// <para>Note this is about ELIGIBILITY only, never about which failure wins: this router keeps the FIRST
    /// substantive failure where <c>LlmRouter</c> keeps the LAST, and that difference is untouched.</para>
    /// </summary>
    private static bool IsBlameless(GenerationVerdict verdict) =>
        verdict is GenerationVerdict.NotConfigured or GenerationVerdict.Unsupported;

    /// <summary>The first rejecting backend's own words, folded onto the synthesized "nobody took it" message
    /// — the first SUBSTANTIVE rejection where there was one, otherwise the first blameless rejection that
    /// still gave a reason. Without it the only thing that survives a failed submission is a list of candidate
    /// ids — and that is what <c>GenerationRenderJobHandler</c> fails the job with and what
    /// <c>GenerationSubmitTool</c> hands a model, neither of which can act on "these three didn't work".
    /// <para>The reasonless branch below is therefore reached only by a SUBSTANTIVE rejection that said
    /// nothing: a blameless one with an empty detail is never remembered at all, because naming a backend
    /// that was merely skipped blamelessly would read as an accusation.</para>
    /// <para><see cref="GenerationSubmission.ProviderId"/> stays EMPTY regardless: <see cref="IGenerationRouter"/>
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
    private async Task<GenerationResult> AttemptAsync(
        IGenerationProvider provider, GenerationRequest request, CancellationToken ct)
    {
        // the permit is taken BEFORE the clock starts, so a queued render's wait never inflates the
        // backend's reported latency
        using var permit = await EnterAdmissionAsync(provider, ct).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        using var span = LyntaiDiagnostics.StartGeneration("generate", provider.Id, request.Kind, request.Model);
        GenerationResult result;
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
            // THE TRUST BOUNDARY. IGenerationProvider documents "a value with a verdict, never a throw", and
            // AddGenerationProvider is a documented BYO seam — so a backend that breaks that contract is a
            // case this router HANDLES rather than a case that cannot happen. Without this, one throwing
            // backend killed the whole chain: the healthy candidate was never tried, RecordGeneration never
            // fired so the attempt was invisible in telemetry, and the caller got a raw exception.
            // Classified through the shared taxonomy for the same reason the LLM side gives — hand-rolling
            // Failed here would hammer a rate-limited host instead of cooling it.
            result = GenerationResult.Failure(ClassifyThrown(ex), $"{provider.Id}: {ex.Message}");
        }
        LyntaiDiagnostics.RecordGeneration(span, provider.Id, request.Kind, result.Verdict,
            // an Ok result always has artifacts (GenerationResult.Success enforces it), so the count is
            // reported even by a backend that returns no usage of its own
            result.IsOk ? result.Usage ?? new GenerationUsage(Count: result.Artifacts.Count) : result.Usage,
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

    /// <summary>Classify a THROWN backend exception, with <see cref="GenerationVerdict.Refused"/> clamped to
    /// <see cref="GenerationVerdict.Failed"/> — the same clamp <c>LlmRouter.ClassifyThrown</c> makes, for the
    /// same reason. A throw is transport-layer (an error page mentioning "content filter" at a proxy or CDN,
    /// not the model declining), and Refused is TERMINAL under the routing policy — it Surfaces, with no
    /// fallback. A keyword match in an exception message must never stop this router from trying a healthy
    /// candidate. Backends signal a real refusal with a verdict RESULT, never an exception.</summary>
    private static GenerationVerdict ClassifyThrown(Exception ex)
    {
        var verdict = GenerationVerdictClassifier.FromException(ex);
        return verdict == GenerationVerdict.Refused ? GenerationVerdict.Failed : verdict;
    }

    /// <summary>Take a concurrency permit for this provider's CONFIGURATION, or nothing at all when no
    /// admission is wired or the configuration is unknown. Never returns a handle the caller may skip
    /// disposing: a permit that is not returned pins its gate for the life of the process, so every call site
    /// scopes the result with <c>using</c> and lets success, failure, a throw and cancellation all release
    /// it the same way.</summary>
    private async ValueTask<IDisposable?> EnterAdmissionAsync(IGenerationProvider provider, CancellationToken ct) =>
        admission is not null && _configuration(provider) is { } key
            ? await admission.EnterAsync(key, ct).ConfigureAwait(false)
            : null;

    /// <summary>Whether this backend is benched — honouring the sole-candidate exemption, so the only capable
    /// backend is always tried.</summary>
    private bool IsBenched(IGenerationProvider provider, int capableCount) =>
        deadHosts is not null &&
        !(capableCount == 1 && _policy.ExemptSoleCandidate) &&
        deadHosts.IsDead(CooldownKey(provider));

    /// <summary>The tracker is shared with the LLM router, so keys carry their domain: a host with a chat
    /// provider and an image backend both called "openai" must not have one bench the other.
    ///
    /// <para>Within the domain the key is the CONFIGURATION when one is known, falling back to the backend id
    /// otherwise — the two configurations of one backend id that a pool keeps live must bench independently,
    /// while two consumers of one downed self-hosted host must share a bench.</para></summary>
    private string CooldownKey(IGenerationProvider provider) =>
        $"generation::{_configuration(provider)?.ToString() ?? provider.Id}";

    /// <summary>Candidates that exist, are registered, are DISTINCT, and DECLARE they can serve this
    /// request/delivery — with the candidate's model applied to the request when it pins one. Materialized
    /// because the sole-candidate cooldown exemption needs to know how many there are before trying the
    /// first.
    ///
    /// <para><b>Dedup happens on the RESOLVED pair, and it happens HERE.</b> Resolved, because that is the
    /// pair that decides what is actually called: <c>"fal"</c> and <c>"FAL"</c> select one provider (ids match
    /// case-insensitively), and a candidate pinning the model the request already names resolves to the same
    /// call as one that pins nothing. Here, because <see cref="GenerationRoutingPolicy.ExemptSoleCandidate"/>
    /// reads the COUNT of this list — so two entries naming one backend would both re-attempt a backend that
    /// just failed AND make the sole capable backend look like two, silently withdrawing the exemption. A
    /// dedup applied after the count is taken fixes neither.</para>
    ///
    /// <para>The dedup itself is the LLM router's (<see cref="CandidateDedup"/>) — first wins, order preserved
    /// — rather than a second copy of it here.</para></summary>
    private List<(IGenerationProvider Provider, GenerationRequest Request)> Capable(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, GenerationDelivery delivery)
    {
        var resolved = new List<(IGenerationProvider Provider, GenerationRequest Request)>();
        foreach (var candidate in candidates)
        {
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Id, candidate.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is null) continue;   // an unknown id is a config typo, not a crash

            resolved.Add((provider,
                candidate.Model is { Length: > 0 } model ? request with { Model = model } : request));
        }

        // capability LAST: a duplicate is dropped before it is asked, and the surviving count is the number
        // of distinct backends this request could actually reach
        var capable = new List<(IGenerationProvider, GenerationRequest)>();
        foreach (var entry in CandidateDedup.Dedup(resolved, e => (e.Provider.Id, e.Request.Model)))
        {
            if (entry.Provider.Capabilities.Supports(entry.Request, delivery)) capable.Add(entry);
        }
        return capable;
    }
}
