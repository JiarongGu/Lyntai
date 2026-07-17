using System.Runtime.CompilerServices;
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
                    // §6 as amended 2026-07-17: a 429 is terminal for THIS host (immediate cooldown,
                    // never re-ask the same window) but transient for the fleet — a different
                    // candidate has a different quota, so advance instead of failing the request.
                    deadHosts.MarkDead(provider.Id);
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
                        if (chunk.Verdict == LlmVerdict.RateLimited)
                            deadHosts.MarkDead(provider.Id); // amended §6: cool this host, advance
                        else
                            deadHosts.RecordFailure(provider.Id);
                        failedPreContent = true;
                        break;
                    }

                    if (chunk.Kind == LlmChunkKind.Content && !committed)
                    {
                        committed = true;
                        deadHosts.RecordSuccess(provider.Id);
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

        yield return lastError ?? LlmChunk.Error(LlmVerdict.Failed, "no live candidate (all skipped: unknown, unavailable, or dead)");
    }

    private async Task<LlmReply> TryCompleteAsync(ILlmProvider provider, LlmCandidate candidate, LlmRequest req, CancellationToken ct)
    {
        var effective = req with { Model = options.ResolveModel(req.Consumer, candidate.Model ?? req.Model) };
        try
        {
            return await provider.CompleteAsync(effective, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-initiated cancel is not a provider failure
        }
        catch (Exception ex)
        {
            return new LlmReply("", LlmVerdict.Failed, Detail: $"{provider.Id}: {ex.Message}");
        }
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
