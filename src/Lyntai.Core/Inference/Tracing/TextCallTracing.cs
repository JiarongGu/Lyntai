using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Lyntai.Cortex;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference.Tracing;

/// <summary>Groups traced front-door calls into one run. Inside <see cref="Into"/>, every call that
/// <c>AddTextCallTracing</c> traces records its step on the given recorder instead of beginning a trace of its own.</summary>
public static class TextCallTracing
{
    private static readonly AsyncLocal<ITraceRecorder?> Scope = new();

    internal static ITraceRecorder? Current => Scope.Value;

    /// <summary>Record the steps of the calls this async flow makes on <paramref name="recorder"/> until the returned
    /// scope is disposed. The caller owns the recorder and completes it; scores are saved under its session id.
    /// Scopes nest: disposing one restores the recorder it replaced. Without <c>AddTextCallTracing</c> nothing is
    /// recorded.</summary>
    public static IDisposable Into(ITraceRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var previous = Scope.Value;
        Scope.Value = recorder;
        return new Restore(previous);
    }

    private sealed class Restore(ITraceRecorder? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Scope.Value = previous;
        }
    }
}

/// <summary>The front-door decorator <c>AddTextCallTracing</c> folds in: one trace step per call, then the selected
/// scorers over a finished reply — both after the reply, and neither able to fail it.</summary>
internal sealed class TracingTextClient : DelegatingTextClient
{
    // set while scorers run, so an LLM scorer's own front-door call passes straight through
    private static readonly AsyncLocal<bool> Scoring = new();

    private readonly TextCallTracingOptions _options;
    private readonly ILogger _logger;
    private readonly Lazy<Sinks> _sinks;

    private sealed record Sinks(ITraceService Traces, ScoringService? Scoring);

    public TracingTextClient(
        ITextClient inner, IServiceProvider services, TextCallTracingOptions options, ILogger<TracingTextClient>? logger = null)
        : base(inner)
    {
        _options = options;
        _logger = logger ?? NullLogger<TracingTextClient>.Instance;
        // resolved on first use, never while the ITextClient singleton is being built: an LLM scorer needs it
        _sinks = new(() => Resolve(services, options));
    }

    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        if (!Traced(req)) return await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        var reply = await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
        await RecordAsync(req, reply.Verdict, reply.Text, reply.Usage, reply.Detail, clock.ElapsedMilliseconds, ct)
            .ConfigureAwait(false);
        return reply;
    }

    public override IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        Traced(req) ? StreamTracedAsync(req, ct) : Inner.StreamAsync(req, ct);

    private async IAsyncEnumerable<TextChunk> StreamTracedAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var text = new StringBuilder();
        TextChunk? end = null;
        var threw = false;
        var chunks = Inner.StreamAsync(req, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                try
                {
                    if (!await chunks.MoveNextAsync().ConfigureAwait(false)) break;
                }
                catch
                {
                    threw = true;
                    throw;
                }
                var chunk = chunks.Current;
                if (chunk.Kind == TextChunkKind.Content) text.Append(chunk.Text);
                else if (chunk.Kind is TextChunkKind.Final or TextChunkKind.Error) end = chunk;
                yield return chunk;
            }
        }
        finally
        {
            await chunks.DisposeAsync().ConfigureAwait(false);
            // an abandoned stream is recorded here, on dispose; one that threw is not, as a thrown completion is not
            if (!threw)
                await RecordAsync(req, end?.Verdict, text.ToString(), end?.Usage, end?.Detail, clock.ElapsedMilliseconds, ct)
                    .ConfigureAwait(false);
        }
    }

    private bool Traced(TextRequest req)
    {
        if (Scoring.Value) return false;
        try
        {
            return _options.Include?.Invoke(req) ?? true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "the tracing Include predicate threw for {Consumer}; the call is not traced", req.Consumer);
            return false;
        }
    }

    // verdict is null for a stream that ended before its Final or Error chunk: traced, but not scored
    private async Task RecordAsync(
        TextRequest req, ProviderVerdict? verdict, string text, TextUsage? usage, string? detail, long ms, CancellationToken ct)
    {
        try
        {
            var sinks = _sinks.Value;
            var scope = TextCallTracing.Current;
            var recorder = scope ?? sinks.Traces.Begin(Guid.NewGuid().ToString("N"), _options.Mode);
            recorder.Record(new TraceStep
            {
                Kind = "llm",
                Label = req.Consumer,
                InputTokens = usage?.InputTokens ?? 0,
                OutputTokens = usage?.OutputTokens ?? 0,
                CostUsd = usage?.CostUsd ?? 0,
                DurationMs = ms,
                Detail = Describe(req, verdict, text),
            });
            if (scope is null) await recorder.CompleteAsync(ct).ConfigureAwait(false);
            if (verdict is { } finished && sinks.Scoring is { } scoring)
                await ScoreAsync(scoring, recorder.SessionId, req, finished, text, detail, ct).ConfigureAwait(false);
        }
        // the reply already succeeded, so a late cancellation is swallowed with everything else
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "tracing the {Consumer} call failed; the reply is returned (fail-open)", req.Consumer);
        }
    }

    private static async Task ScoreAsync(
        ScoringService scoring, string sessionId, TextRequest req, ProviderVerdict verdict, string text, string? detail,
        CancellationToken ct)
    {
        Scoring.Value = true;                        // reverts when this method returns: an async method's own context
        Dictionary<string, string>? extra = null;
        if (req.JsonSchema is not null) (extra ??= [])["format"] = "json";
        if (verdict != ProviderVerdict.Ok) (extra ??= [])["error"] = detail ?? verdict.ToString();
        await scoring.EvaluateAsync(new ScoreContext
        {
            SessionId = sessionId,
            Input = req.Messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content,
            Output = text,
            Extra = extra,
        }, ct).ConfigureAwait(false);
    }

    private string Describe(TextRequest req, ProviderVerdict? verdict, string text)
    {
        var detail = $"verdict={verdict?.ToString() ?? "incomplete"}" + (req.Model is null ? "" : $"; model={req.Model}");
        return _options.RecordText ? $"{detail}; reply={Cut(text, _options.MaxRecordedChars)}" : detail;
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        var end = max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;   // never half a surrogate pair
        return text[..end];
    }

    private static Sinks Resolve(IServiceProvider services, TextCallTracingOptions options)
    {
        var scorers = services.GetServices<IScorer>().Where(options.Scorers).ToList();
        return new Sinks(
            services.GetRequiredService<ITraceService>(),
            scorers.Count == 0
                ? null
                : new ScoringService(scorers, services.GetService<IScoreStore>(), services.GetService<ILogger<ScoringService>>()));
    }
}
