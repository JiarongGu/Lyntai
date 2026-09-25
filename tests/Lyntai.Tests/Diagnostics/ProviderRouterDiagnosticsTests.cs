using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai.Diagnostics;
using Lyntai.Inference;

namespace Lyntai.Tests.Diagnostics;

/// <summary>The generic router — every embed on memory write and recall, every scoring-verification rerank —
/// emits the same per-attempt telemetry the text and media routers do, so a slow reranker shows in a host's
/// traces. Each test filters on a unique provider id: the listener is process-global.</summary>
public class ProviderRouterDiagnosticsTests
{
    private static ActivityListener SpanListener(List<Activity> sink) => new()
    {
        ShouldListenTo = s => s.Name == LyntaiDiagnostics.ActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = a => { lock (sink) sink.Add(a); },
    };

    private static List<Activity> SpansFor(List<Activity> spans, string providerId)
    {
        lock (spans) return [.. spans.Where(s => Equals(s.GetTagItem("gen_ai.system"), providerId))];
    }

    private static ProviderRouter<VectorRequest, VectorResponse> Embedder(IModelProvider provider) =>
        new([provider], VectorResponse.Failure, ProviderShapesForTests.Embeds);

    [Fact]
    public async Task An_embed_attempt_emits_an_embeddings_span_with_its_usage()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var backend = new Vectors("embed-span-ok", VectorResponse.Success([[1f]]) with { Usage = new ProviderUsage(InputTokens: 7) });
        await Embedder(backend).CallAsync(new VectorRequest(["hello"]));

        var span = Assert.Single(SpansFor(spans, "embed-span-ok"));
        Assert.Equal("embeddings", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal(7L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Null(span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task A_failed_rerank_attempt_records_its_verdict_on_the_span_and_the_duration_metric()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);
        var durations = new List<(string? Operation, string? Error)>();
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == LyntaiDiagnostics.MeterName && instrument.Name == "gen_ai.client.operation.duration")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        meters.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            string? system = null, operation = null, error = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "gen_ai.system") system = tag.Value as string;
                if (tag.Key == "gen_ai.operation.name") operation = tag.Value as string;
                if (tag.Key == "error.type") error = tag.Value as string;
            }
            if (system == "rerank-span-err") lock (durations) durations.Add((operation, error));
        });
        meters.Start();

        var backend = new Scores("rerank-span-err", ScoreResponse.Failure(ProviderVerdict.Timeout, "too slow"));
        await new ProviderRouter<ScoreRequest, ScoreResponse>([backend], ScoreResponse.Failure)
            .CallAsync(new ScoreRequest("q", ["d"]));

        var span = Assert.Single(SpansFor(spans, "rerank-span-err"));
        Assert.Equal("rerank", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("Timeout", span.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        lock (durations) Assert.Equal([("rerank", "Timeout")], durations);
    }

    private static class ProviderShapesForTests
    {
        public static bool Embeds(ProviderCapabilities c) =>
            c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text);
    }

    private sealed class Vectors(string id, VectorResponse response) : IVectorProvider
    {
        public string Id => id;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    private sealed class Scores(string id, ScoreResponse response) : IScoreProvider
    {
        public string Id => id;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Score],
            Operations = [ProviderOperation.Complete],
        };

        public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default) =>
            Task.FromResult(response);
    }
}
