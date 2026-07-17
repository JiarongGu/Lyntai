using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Routing;

/// <summary>Fallback router over the DI collection of <see cref="ILlmProvider"/>s (design §6).</summary>
public sealed class LlmRouter(
    IEnumerable<ILlmProvider> providers,
    DeadHostTracker deadHosts,
    LyntaiOptions options,
    ILogger<LlmRouter>? logger = null) : ILlmRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<LlmRouter>.Instance;

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default)
    {
        LlmReply? last = null;
        foreach (var candidate in CandidateDedup.Dedup(candidates))
        {
            var provider = SelectLive(candidate, out var skipReason);
            if (provider is null)
            {
                _logger.LogDebug("router: skipping {Candidate} — {Reason}", candidate.ProviderId, skipReason);
                continue;
            }

            var reply = await TryCompleteAsync(provider, candidate, req, ct).ConfigureAwait(false);
            _logger.LogInformation("router: {Provider} (model {Model}) → {Verdict}{Detail}",
                provider.Id, candidate.Model ?? "(default)", reply.Verdict,
                reply.Verdict == LlmVerdict.Ok ? "" : $" — {reply.Detail}");

            switch (reply.Verdict)
            {
                case LlmVerdict.Ok:
                    deadHosts.RecordSuccess(provider.Id);
                    return reply;

                case LlmVerdict.Failed:
                case LlmVerdict.Timeout:
                    deadHosts.RecordFailure(provider.Id);
                    last = reply;
                    continue; // availability problem — try the next candidate

                case LlmVerdict.RateLimited:
                case LlmVerdict.AuthFailed:
                    // §6 as amended 2026-07-17: terminal for THIS host (immediate cooldown — the
                    // same window/credentials never recover by retrying) but transient for the
                    // fleet: a different candidate has a different quota/key, so advance.
                    deadHosts.MarkDead(provider.Id);
                    last = reply;
                    continue;

                case LlmVerdict.ContextWindowExceeded:
                    // the request is too big for THIS model, not a host fault: no dead-host
                    // penalty — a larger-context candidate is the correct remedy
                    last = reply;
                    continue;

                case LlmVerdict.Refused:
                    return reply; // content policy follows the prompt, not the host — surface as-is
            }
        }

        return last ?? new LlmReply("", LlmVerdict.Failed, Detail: "no live candidate (all skipped: unknown, unavailable, or dead)");
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        LlmChunk? lastError = null;
        foreach (var candidate in CandidateDedup.Dedup(candidates))
        {
            var provider = SelectLive(candidate, out var skipReason);
            if (provider is null)
            {
                _logger.LogDebug("router: skipping {Candidate} — {Reason}", candidate.ProviderId, skipReason);
                continue;
            }

            var effective = req with { Model = options.ResolveModel(req.Consumer, candidate.Model ?? req.Model) };
            var committed = false;   // once any content is yielded, no fallback — pass everything through
            var failedPreContent = false;

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
                            // a provider that throws mid-iteration behaves like an Error chunk
                            chunk = LlmChunk.Error(LlmVerdict.Failed, ex.Message);
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
                            if (chunk.Verdict == LlmVerdict.Refused)
                            {
                                // content policy follows the prompt — no fallback (same as non-streaming)
                                yield return chunk;
                                yield break;
                            }
                            _logger.LogWarning("router: {Provider} failed pre-content ({Verdict} — {Detail}); trying next candidate",
                                provider.Id, chunk.Verdict, chunk.Detail);
                            if (chunk.Verdict is LlmVerdict.RateLimited or LlmVerdict.AuthFailed)
                                deadHosts.MarkDead(provider.Id); // amended §6: cool this host, advance
                            else if (chunk.Verdict != LlmVerdict.ContextWindowExceeded)
                                deadHosts.RecordFailure(provider.Id); // too-big-for-model is not a host fault
                            failedPreContent = true;
                            break;
                        }

                        if (chunk.Kind == LlmChunkKind.Content && !committed)
                        {
                            committed = true;
                            deadHosts.RecordSuccess(provider.Id);
                            LyntaiDiagnostics.RecordFirstChunk(provider.Id, effective.Model,
                                Stopwatch.GetElapsedTime(start).TotalSeconds);
                            _logger.LogInformation("router: streaming from {Provider} (model {Model})",
                                provider.Id, effective.Model ?? "(default)");
                        }

                        yield return chunk; // committed (or benign pre-content Final): pass through unchanged
                        if (chunk.Kind is LlmChunkKind.Final or LlmChunkKind.Error) yield break;
                    }
                }

                if (committed) yield break;       // stream ended (however it ended) after content — done
                if (!failedPreContent) yield break; // ended cleanly with no content (empty stream) — done
            }
            finally
            {
                LyntaiDiagnostics.RecordOutcome(activity, provider.Id, effective.Model, outcome, usage,
                    Stopwatch.GetElapsedTime(start).TotalSeconds, outcomeDetail);
                activity?.Dispose();
            }
        }

        yield return lastError ?? LlmChunk.Error(LlmVerdict.Failed, "no live candidate (all skipped: unknown, unavailable, or dead)");
    }

    private async Task<LlmReply> TryCompleteAsync(ILlmProvider provider, LlmCandidate candidate, LlmRequest req, CancellationToken ct)
    {
        var effective = req with { Model = options.ResolveModel(req.Consumer, candidate.Model ?? req.Model) };
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

    private ILlmProvider? SelectLive(LlmCandidate candidate, out string skipReason)
    {
        var provider = providers.FirstOrDefault(p => p.Id == candidate.ProviderId);
        skipReason = provider is null ? "no provider with this id registered"
            : !provider.IsAvailable ? "provider reports unavailable"
            : deadHosts.IsDead(provider.Id) ? "dead-host cooldown"
            : "";
        return skipReason.Length == 0 ? provider : null;
    }
}
