using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Local;

/// <summary>
/// In-process provider that runs a GGUF model locally through LLamaSharp (llama.cpp) — no network,
/// no API key, no external process. The heavy model weights load lazily on the first call and are
/// reused; because there is one local model on finite hardware, generations are <b>serialized</b>
/// (one at a time) rather than run concurrently — concurrent calls queue on an internal gate.
///
/// Verdicts are simple: a produced answer is <see cref="LlmVerdict.Ok"/>, an empty generation or a
/// load/inference fault is <see cref="LlmVerdict.Failed"/> (so the router falls over to the next
/// candidate), and the inactivity deadline is <see cref="LlmVerdict.Timeout"/>. There is no
/// rate-limit or content-filter notion for a local model. Token accounting is not reported (local
/// inference has no billing and exact counts need model-specific tokenization).
/// </summary>
public sealed class LocalProvider(
    string id,
    LocalModelOptions options,
    LyntaiOptions lyntai,
    ILogger<LocalProvider>? logger = null) : ILlmProvider, IDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<LocalProvider>.Instance;
    // one local model, one generation at a time; also single-flights the lazy weight load
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private bool _disposed;

    public string Id => id;

    /// <summary>The model file must exist on disk. (Whether the native backend is present is only
    /// discoverable at load time — a missing backend surfaces as a Failed verdict on the first call,
    /// letting the router fall over.)</summary>
    public bool IsAvailable => File.Exists(options.ModelPath);

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        var text = new StringBuilder();
        LlmUsage? usage = null;
        await foreach (var chunk in StreamAsync(req, ct).ConfigureAwait(false))
        {
            switch (chunk.Kind)
            {
                case LlmChunkKind.Content: text.Append(chunk.Text); break;
                case LlmChunkKind.Final: usage = chunk.Usage; break;
                case LlmChunkKind.Error: return new LlmReply("", chunk.Verdict, Detail: chunk.Detail);
            }
        }
        return new LlmReply(text.ToString(), LlmVerdict.Ok, usage);
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            StatelessExecutor executor;
            string prompt;
            LlmChunk? startupError = null;
            try
            {
                executor = EnsureLoaded();
                prompt = BuildPrompt(req);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Id}: local model load failed", Id);
                startupError = LlmChunk.Error(LlmVerdict.Failed, $"{Id}: model load failed — {ex.Message}");
                executor = null!;
                prompt = "";
            }
            if (startupError is not null)
            {
                yield return startupError; // pre-content error — the router may fall over
                yield break;
            }

            var inference = BuildInferenceParams(req);
            var sawContent = false;

            // the timeout is an INACTIVITY clock: re-armed before each token read and stopped while we
            // and the consumer process a token — a slow-but-healthy local generation isn't killed under
            // a slow reader (mirrors OpenAiCompatibleProvider / ProcessRunner).
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var enumerator = executor.InferAsync(prompt, inference, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    string? piece;
                    LlmChunk? error = null;
                    try
                    {
                        timeoutCts.CancelAfter(lyntai.ProviderTimeout);   // arm: inactivity clock for this token
                        var moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan); // stop the clock while we + the consumer work
                        piece = moved ? enumerator.Current : null;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        var timedOut = timeoutCts.IsCancellationRequested;
                        error = LlmChunk.Error(timedOut ? LlmVerdict.Timeout : LlmVerdict.Failed,
                            timedOut ? $"{Id}: no token within {lyntai.ProviderTimeout}" : $"{Id}: generation broke — {ex.Message}");
                        piece = null;
                    }
                    if (error is not null)
                    {
                        yield return error;
                        yield break;
                    }
                    if (piece is null) break;
                    if (piece.Length == 0) continue; // decoder can emit empty pieces mid multi-byte token
                    sawContent = true;
                    yield return LlmChunk.Content(piece);
                }
            }

            // zero-content generation = the local twin of the HTTP path's empty→Failed, so the router
            // can fall over pre-content instead of reporting a clean empty answer
            if (!sawContent)
            {
                yield return LlmChunk.Error(LlmVerdict.Failed, $"{Id}: no output produced");
                yield break;
            }
            yield return LlmChunk.Final();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Load the weights + executor once (called under <see cref="_gate"/>, so no double-load).</summary>
    private StatelessExecutor EnsureLoaded()
    {
        if (_executor is not null) return _executor;
        var modelParams = new ModelParams(options.ModelPath)
        {
            ContextSize = options.ContextSize,
            GpuLayerCount = options.GpuLayerCount,
        };
        _weights = LLamaWeights.LoadFromFile(modelParams);
        _executor = new StatelessExecutor(_weights, modelParams);
        _logger.LogInformation("{Id}: loaded local model {Path} (gpuLayers={Gpu})", Id, options.ModelPath, options.GpuLayerCount);
        return _executor;
    }

    /// <summary>Apply the model's OWN chat template (from its GGUF metadata) so instruct-tuned models
    /// get the exact prompt format they were trained on — a naive role flatten degrades output badly.</summary>
    private string BuildPrompt(LlmRequest req)
    {
        var template = new LLamaTemplate(_weights!) { AddAssistant = true };
        foreach (var m in req.Messages) template.Add(m.Role, m.Content);
        return Encoding.UTF8.GetString(template.Apply());
    }

    private InferenceParams BuildInferenceParams(LlmRequest req) => new()
    {
        MaxTokens = req.MaxTokens ?? options.MaxTokens ?? -1, // -1 = run to EOS
        AntiPrompts = options.AntiPrompts,
        SamplingPipeline = new DefaultSamplingPipeline { Temperature = (float)(req.Temperature ?? options.Temperature) },
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_executor as IDisposable)?.Dispose();
        _executor = null;
        _weights?.Dispose();
        _weights = null;
        _gate.Dispose();
    }
}
