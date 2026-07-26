using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Routing;

/// <summary>Fallback router over the DI collection of <see cref="ILlmProvider"/>s (design §6).
/// Behavior is driven by <see cref="RoutingPolicy"/> (<see cref="LyntaiOptions.Routing"/>): the
/// defaults reproduce §6 exactly, so an untouched policy behaves as documented.</summary>
public sealed class LlmRouter(
    IEnumerable<ILlmProvider> providers,
    DeadHostTracker deadHosts,
    LyntaiOptions options,
    ILogger<LlmRouter>? logger = null,
    IModelRoutingStore? modelRouting = null) : ILlmRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<LlmRouter>.Instance;
    private RoutingPolicy Policy => options.Routing;

    // provider lookup by id, built once — O(1) per candidate/retry instead of a linear scan. First
    // registration wins on a duplicate id (preserving the prior FirstOrDefault semantics).
    private readonly Lazy<IReadOnlyDictionary<string, ILlmProvider>> _byId = new(() =>
    {
        var map = new Dictionary<string, ILlmProvider>();
        foreach (var p in providers) map.TryAdd(p.Id, p);
        return map;
    });

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default)
    {
        var liveModel = await LiveModelAsync(req.Consumer, ct).ConfigureAwait(false);
        LlmReply? last = null;

        foreach (var (provider, effectiveModel, key) in LiveCandidates(candidates, req, liveModel))
        {
            // retry-then-advance: the same candidate may be retried on transient faults before advancing
            var retries = 0;
            while (true)
            {
                var reply = await TryCompleteAsync(provider, effectiveModel, req, ct).ConfigureAwait(false);
                _logger.LogInformation("router: {Provider} (model {Model}) → {Verdict}{Detail}",
                    provider.Id, effectiveModel ?? "(default)", reply.Verdict,
                    reply.Verdict == LlmVerdict.Ok ? "" : $" — {reply.Detail}");

                if (reply.Verdict == LlmVerdict.Ok)
                {
                    deadHosts.RecordSuccess(key);
                    return reply;
                }

                var action = Policy.ActionFor(reply.Verdict);
                if (action == FallbackAction.Surface)
                    return reply; // content policy follows the prompt, not the host — surface as-is

                last = reply;
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

        return last ?? new LlmReply("", LlmVerdict.Failed, Detail: "no live candidate (all skipped: unknown, unavailable, or dead)");
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var liveModel = await LiveModelAsync(req.Consumer, ct).ConfigureAwait(false);
        LlmChunk? lastError = null;

        foreach (var (provider, effectiveModel, key) in LiveCandidates(candidates, req, liveModel))
        {
            var effective = req with { Model = effectiveModel };

            // pre-content retry-then-advance: streaming can only retry BEFORE the first token (after
            // it, no fallback at all). Each pass is a fresh stream attempt on the same candidate.
            var advance = false;
            var retries = 0;
            while (!advance)
            {
                var committed = false;   // once real content is yielded, no fallback — pass everything through
                var retryVerdict = LlmVerdict.Ok;

                var activity = LyntaiDiagnostics.StartChat(provider.Id, effective.Model);
                var start = Stopwatch.GetTimestamp();
                LlmVerdict outcome = LlmVerdict.Ok;
                LlmUsage? usage = null;
                string? outcomeDetail = null;
                try
                {
                    var enumerator = provider.StreamAsync(effective, ct).GetAsyncEnumerator(ct);
                    await using (enumerator.ConfigureAwait(false))
                    {
                        while (true)
                        {
                            LlmChunk? chunk;
                            try
                            {
                                chunk = await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null;
                            }
                            // only the CALLER's cancel aborts the router (the trust boundary); a provider's
                            // OWN OperationCanceledException (ct not cancelled — e.g. its internal timeout)
                            // becomes a fall-over-able Error chunk, not an abort of the whole stream
                            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                            {
                                // a mid-iteration throw becomes an Error chunk, classified through the shared
                                // taxonomy (thrown 429→RateLimited, provider-internal OCE→Timeout, …)
                                chunk = LlmChunk.Error(ClassifyThrown(ex), ex.Message);
                            }
                            if (chunk is null) break;

                            // Trust boundary: a Final with NO preceding content is the empty-reply trap in
                            // stream form (pitfalls: empty output must be fall-over-able, never a clean end) —
                            // convert it to an Error so the pre-content fallback path below handles it.
                            if (chunk.Kind == LlmChunkKind.Final && !committed)
                                chunk = LlmChunk.Error(LlmVerdict.Failed, $"{provider.Id}: empty stream (Final with no content)");

                            if (chunk.Kind == LlmChunkKind.Error)
                            {
                                outcome = chunk.Verdict;
                                outcomeDetail = chunk.Detail;
                            }
                            if (chunk.Kind == LlmChunkKind.Final) usage = chunk.Usage;

                            if (chunk.Kind == LlmChunkKind.Error && !committed)
                            {
                                lastError = chunk;
                                var action = Policy.ActionFor(chunk.Verdict);
                                if (action == FallbackAction.Surface)
                                {
                                    yield return chunk; // no fallback (same as non-streaming)
                                    yield break;
                                }
                                _logger.LogWarning("router: {Provider} failed pre-content ({Verdict} — {Detail}); trying next candidate",
                                    provider.Id, chunk.Verdict, chunk.Detail);
                                if (action == FallbackAction.CooldownAndAdvance) deadHosts.MarkDead(key);
                                // PenalizeAndAdvance records ONE failure on advance (below), not per retry
                                retryVerdict = chunk.Verdict;
                                break; // leave the enumerator; decide retry-vs-advance below
                            }

                            if (chunk.Kind == LlmChunkKind.Content)
                            {
                                // an empty/role-only content chunk is NOT real content: never yield it
                                // (no leak to the consumer) and it must not commit the stream / disable fallback
                                if (chunk.Text.Length == 0) continue;
                                if (!committed)
                                {
                                    committed = true;
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
                            yield return chunk;
                            yield break;
                        }
                    }

                    if (committed) yield break; // ended after content — done
                    if (retryVerdict == LlmVerdict.Ok)
                    {
                        // the enumerator ended with NO chunks at all — a contract-violating empty stream
                        // (providers must end with exactly one Final or Error). Same trust-boundary rule as
                        // an empty reply: treat as Failed and retry/advance rather than ending the router's
                        // own stream silently with no terminal chunk.
                        retryVerdict = LlmVerdict.Failed;
                        outcome = LlmVerdict.Failed;
                        outcomeDetail = "empty stream (no chunks)";
                        lastError = LlmChunk.Error(LlmVerdict.Failed, $"{provider.Id}: empty stream (no chunks)");
                        _logger.LogWarning("router: {Provider} produced an empty stream (no chunks); treating as Failed", provider.Id);
                    }
                }
                finally
                {
                    LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, outcome, usage,
                        Stopwatch.GetElapsedTime(start).TotalSeconds, outcomeDetail);
                    activity?.Dispose();
                }

                // pre-content failure: retry the same candidate if the policy allows, else advance
                if (Policy.ShouldRetrySameCandidate(retryVerdict, ++retries))
                {
                    _logger.LogDebug("router: retrying stream {Provider} ({Retry}/{Budget}) after {Verdict}",
                        provider.Id, retries, Policy.RetriesFor(retryVerdict), retryVerdict);
                    if (Policy.RetryBackoff > TimeSpan.Zero) await Task.Delay(Policy.RetryBackoff, ct).ConfigureAwait(false);
                    continue; // retries are part of ONE attempt — no failure recorded yet
                }
                // retries exhausted: record exactly ONE failure for a penalize verdict (not one per retry)
                if (Policy.ActionFor(retryVerdict) == FallbackAction.PenalizeAndAdvance)
                    deadHosts.RecordFailure(key);
                advance = true;
            }
        }

        yield return lastError ?? LlmChunk.Error(LlmVerdict.Failed, "no live candidate (all skipped: unknown, unavailable, or dead)");
    }

    /// <summary>Native tool support for a candidate list: the first live candidate (registered,
    /// available, not on cooldown) decides — matching how the router commits to the first working one.
    /// A tool-capable fallback that would never be reached must not flip this true. Being a SYNC probe,
    /// it resolves against the CONFIGURED default model (no live <see cref="IModelRoutingStore"/> read) —
    /// under <see cref="CooldownScope.ProviderAndModel"/> plus a live override, the probe's cooldown key
    /// can differ from the completion's.</summary>
    public bool SupportsToolCalls(IReadOnlyList<LlmCandidate> candidates, LlmRequest req)
    {
        foreach (var candidate in LiveCandidates(candidates, req, liveModel: null))
            return candidate.Provider.SupportsToolCalls; // first live candidate decides
        return false;
    }

    /// <summary>The shared candidate-selection preamble every door runs (this used to be written out three
    /// times): dedup the list, resolve each candidate's EFFECTIVE model (candidate override → request →
    /// consumer default → live override when supplied), skip unknown/unavailable/cooling providers (with
    /// the sole-candidate exemption), and pair each survivor with its cooldown key.</summary>
    private IEnumerable<(ILlmProvider Provider, string? Model, string Key)> LiveCandidates(
        IReadOnlyList<LlmCandidate> candidates, LlmRequest req, string? liveModel)
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
            yield return (provider, effectiveModel, CooldownKey(provider.Id, effectiveModel));
        }
    }

    private async Task<LlmReply> TryCompleteAsync(ILlmProvider provider, string? effectiveModel, LlmRequest req, CancellationToken ct)
    {
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
            reply = new LlmReply("", ClassifyThrown(ex), Detail: $"{provider.Id}: {ex.Message}");
        }
        LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, reply.Verdict, reply.Usage,
            Stopwatch.GetElapsedTime(start).TotalSeconds, reply.Detail);
        return reply;
    }

    /// <summary>Classify a THROWN provider exception, with Refused clamped to Failed: a throw is
    /// transport-layer (an error page mentioning "content filter" at a proxy/CDN, not the model
    /// declining), and Refused is TERMINAL under §6 (Surface, no fallback) — a keyword match in an
    /// exception message must never stop the router from trying a healthy candidate. Providers signal
    /// real refusals with a verdict REPLY, never an exception.</summary>
    private static LlmVerdict ClassifyThrown(Exception ex)
    {
        var verdict = LlmVerdictClassifier.FromException(ex);
        return verdict == LlmVerdict.Refused ? LlmVerdict.Failed : verdict;
    }

    /// <summary>The live per-consumer model override (null when live routing isn't wired) — read once per
    /// call and passed into <see cref="LyntaiOptions.ResolveModel(string,string,string)"/> for each candidate.
    /// (The sync <see cref="SupportsToolCalls"/> capability probe stays on the configured default.)</summary>
    private async Task<string?> LiveModelAsync(string consumer, CancellationToken ct) =>
        modelRouting is null ? null : await modelRouting.GetModelOverrideAsync(consumer, ct).ConfigureAwait(false);

    /// <summary>The dead-host key for a candidate, at the configured cooldown granularity.</summary>
    private string CooldownKey(string providerId, string? effectiveModel) =>
        Policy.CooldownScope == CooldownScope.ProviderAndModel
            ? $"{providerId}::{effectiveModel ?? "(default)"}"
            : providerId;

    private ILlmProvider? SelectLive(LlmCandidate candidate, string? effectiveModel, bool soleCandidate, out string skipReason)
    {
        if (!_byId.Value.TryGetValue(candidate.ProviderId, out var provider))
        { skipReason = "no provider with this id registered"; return null; }
        if (!provider.IsAvailable) { skipReason = "provider reports unavailable"; return null; }

        // sole-candidate exemption: benching the only option just guarantees a synthetic failure —
        // try it and let it fail with a real error / maybe succeed if the cooldown was stale
        var exempt = soleCandidate && Policy.ExemptSoleCandidate;
        if (!exempt && deadHosts.IsDead(CooldownKey(provider.Id, effectiveModel)))
        {
            skipReason = "dead-host cooldown";
            return null;
        }
        skipReason = "";
        return provider;
    }
}
