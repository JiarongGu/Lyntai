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
    ILogger<LlmRouter>? logger = null) : ILlmRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<LlmRouter>.Instance;
    private RoutingPolicy Policy => options.Routing;

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default)
    {
        var deduped = CandidateDedup.Dedup(candidates);
        var soleCandidate = deduped.Count == 1;
        LlmReply? last = null;

        foreach (var candidate in deduped)
        {
            var effectiveModel = options.ResolveModel(req.Consumer, candidate.Model ?? req.Model);
            var provider = SelectLive(candidate, effectiveModel, soleCandidate, out var skipReason);
            if (provider is null)
            {
                _logger.LogDebug("router: skipping {Candidate} — {Reason}", candidate.ProviderId, skipReason);
                continue;
            }
            var key = CooldownKey(provider.Id, effectiveModel);

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
                    deadHosts.RecordFailure(key);
                    if (Policy.ShouldRetrySameCandidate(reply.Verdict, ++retries))
                    {
                        _logger.LogDebug("router: retrying {Provider} ({Retry}/{Budget}) after {Verdict}",
                            provider.Id, retries, Policy.RetriesFor(reply.Verdict), reply.Verdict);
                        if (Policy.RetryBackoff > TimeSpan.Zero) await Task.Delay(Policy.RetryBackoff, ct).ConfigureAwait(false);
                        continue;
                    }
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
        var deduped = CandidateDedup.Dedup(candidates);
        var soleCandidate = deduped.Count == 1;
        LlmChunk? lastError = null;

        foreach (var candidate in deduped)
        {
            var effectiveModel = options.ResolveModel(req.Consumer, candidate.Model ?? req.Model);
            var provider = SelectLive(candidate, effectiveModel, soleCandidate, out var skipReason);
            if (provider is null)
            {
                _logger.LogDebug("router: skipping {Candidate} — {Reason}", candidate.ProviderId, skipReason);
                continue;
            }
            var key = CooldownKey(provider.Id, effectiveModel);
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
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                chunk = LlmChunk.Error(LlmVerdict.Failed, ex.Message); // a mid-iteration throw is an Error chunk
                            }
                            if (chunk is null) break;

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
                                else if (action == FallbackAction.PenalizeAndAdvance) deadHosts.RecordFailure(key);
                                retryVerdict = chunk.Verdict;
                                break; // leave the enumerator; decide retry-vs-advance below
                            }

                            // only REAL content commits the stream. An empty/role-only first Content chunk
                            // must not disable fallback — the router is the trust boundary and can't assume
                            // every third-party provider guards this itself.
                            if (chunk.Kind == LlmChunkKind.Content && chunk.Text.Length > 0 && !committed)
                            {
                                committed = true;
                                deadHosts.RecordSuccess(key);
                                LyntaiDiagnostics.RecordFirstChunk(provider.Id, effective.Model,
                                    Stopwatch.GetElapsedTime(start).TotalSeconds);
                                _logger.LogInformation("router: streaming from {Provider} (model {Model})",
                                    provider.Id, effective.Model ?? "(default)");
                            }

                            yield return chunk; // committed (or benign pre-content Final): pass through unchanged
                            if (chunk.Kind is LlmChunkKind.Final or LlmChunkKind.Error) yield break;
                        }
                    }

                    if (committed) yield break;                     // ended after content — done
                    if (retryVerdict == LlmVerdict.Ok) yield break; // clean empty stream — done
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
                    continue;
                }
                advance = true;
            }
        }

        yield return lastError ?? LlmChunk.Error(LlmVerdict.Failed, "no live candidate (all skipped: unknown, unavailable, or dead)");
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
            reply = new LlmReply("", LlmVerdict.Failed, Detail: $"{provider.Id}: {ex.Message}");
        }
        LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, reply.Verdict, reply.Usage,
            Stopwatch.GetElapsedTime(start).TotalSeconds, reply.Detail);
        return reply;
    }

    /// <summary>The dead-host key for a candidate, at the configured cooldown granularity.</summary>
    private string CooldownKey(string providerId, string? effectiveModel) =>
        Policy.CooldownScope == CooldownScope.ProviderAndModel
            ? $"{providerId}::{effectiveModel ?? "(default)"}"
            : providerId;

    private ILlmProvider? SelectLive(LlmCandidate candidate, string? effectiveModel, bool soleCandidate, out string skipReason)
    {
        var provider = providers.FirstOrDefault(p => p.Id == candidate.ProviderId);
        if (provider is null) { skipReason = "no provider with this id registered"; return null; }
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
