using System.Diagnostics;
using Lyntai.Diagnostics;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Diagnostics;

/// <summary>The front door opens ONE span per call, carrying the consumer, and every provider attempt nests
/// under it — so a fallback reads as one call with two attempts rather than as unrelated spans (D207). Each
/// test filters on a unique consumer: the listener is process-global.</summary>
public class CallSpanTests
{
    private const string Prompt = "the secret prompt body";
    private const string Answer = "the secret reply body";

    private static ActivityListener SpanListener(List<Activity> sink) => new()
    {
        ShouldListenTo = s => s.Name == LyntaiDiagnostics.ActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = a => { lock (sink) sink.Add(a); },
    };

    private static Activity CallSpan(List<Activity> spans, string consumer)
    {
        lock (spans)
            return Assert.Single(spans, s => s.OperationName == LyntaiDiagnostics.CallSpanName
                && Equals(s.GetTagItem("lyntai.consumer"), consumer));
    }

    private static List<Activity> AttemptsUnder(List<Activity> spans, Activity call)
    {
        lock (spans) return [.. spans.Where(s => s.ParentSpanId == call.SpanId && s.TraceId == call.TraceId)];
    }

    private static TextRequest Req(string consumer) => new() { Messages = [TextMessage.User(Prompt)], Consumer = consumer };

    private static ServiceProvider FrontDoor(params FakeTextProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            foreach (var p in providers) b.AddProvider(_ => p);
            b.UseDefaultCandidates([.. providers.Select(p => p.Id)]);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_fallback_reads_as_one_call_span_with_both_attempts_under_it()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var down = new FakeTextProvider("cs-down");
        down.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "boom"));
        var up = new FakeTextProvider("cs-up");
        up.Replies.Enqueue(new TextResponse(Answer, ProviderVerdict.Ok));
        using var sp = FrontDoor(down, up);

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req("span-fallback"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        var call = CallSpan(spans, "span-fallback");
        Assert.Equal("chat", call.GetTagItem("lyntai.call.operation"));
        Assert.Equal("complete", call.GetTagItem("lyntai.call.door"));
        Assert.Equal(ActivityKind.Internal, call.Kind);
        Assert.NotEqual(ActivityStatusCode.Error, call.Status);
        Assert.Null(call.GetTagItem("error.type"));
        // the call span carries no gen_ai.* operation tag, so a backend counting chat spans counts attempts only
        Assert.Null(call.GetTagItem("gen_ai.operation.name"));

        var attempts = AttemptsUnder(spans, call);
        Assert.Equal(["cs-down", "cs-up"], attempts.OrderBy(a => a.StartTimeUtc).Select(a => a.GetTagItem("gen_ai.system")));
        Assert.All(attempts, a => Assert.Equal("chat", a.GetTagItem("gen_ai.operation.name")));
    }

    [Fact]
    public async Task A_call_every_attempt_failed_fails_its_span_with_the_verdict_the_caller_got()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var a = new FakeTextProvider("cs-fail-a");
        a.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "a down"));
        var b = new FakeTextProvider("cs-fail-b");
        b.Replies.Enqueue(new TextResponse("", ProviderVerdict.Timeout, Detail: "b slow"));
        using var sp = FrontDoor(a, b);

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Req("span-allfail"));

        var call = CallSpan(spans, "span-allfail");
        Assert.Equal(ActivityStatusCode.Error, call.Status);
        Assert.Equal(reply.Verdict.ToString(), call.GetTagItem("error.type"));
        Assert.Equal(2, AttemptsUnder(spans, call).Count);
    }

    [Fact]
    public async Task A_stream_s_call_span_holds_its_pre_content_fallback_and_ends_with_the_stream()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var down = new FakeTextProvider("cs-sdown") { StreamThrow = new InvalidOperationException("dead") };
        var up = new FakeTextProvider("cs-sup")
        {
            StreamScript = _ => [TextChunk.Content("a"), TextChunk.Content("b"), TextChunk.Final()],
        };
        using var sp = FrontDoor(down, up);

        var text = "";
        await foreach (var chunk in sp.GetRequiredService<ITextClient>().StreamAsync(Req("span-stream")))
        {
            if (chunk.Kind == TextChunkKind.Content) text += chunk.Text;
            lock (spans) Assert.DoesNotContain(spans, s => Equals(s.GetTagItem("lyntai.consumer"), "span-stream"));
        }

        Assert.Equal("ab", text);
        var call = CallSpan(spans, "span-stream");
        Assert.Equal("stream", call.GetTagItem("lyntai.call.door"));
        Assert.NotEqual(ActivityStatusCode.Error, call.Status);
        Assert.Equal(["cs-sdown", "cs-sup"],
            AttemptsUnder(spans, call).OrderBy(a => a.StartTimeUtc).Select(a => a.GetTagItem("gen_ai.system")));
    }

    [Fact]
    public async Task An_abandoned_stream_still_ends_its_call_span()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeTextProvider("cs-abandon")
        {
            StreamScript = _ => [TextChunk.Content("a"), TextChunk.Content("b"), TextChunk.Final()],
        };
        using var sp = FrontDoor(p);

        await foreach (var _ in sp.GetRequiredService<ITextClient>().StreamAsync(Req("span-abandon")))
            break;

        var call = CallSpan(spans, "span-abandon");
        Assert.Single(AttemptsUnder(spans, call));
    }

    [Fact]
    public async Task A_stream_that_faults_fails_its_call_span()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeTextProvider("cs-cancel")
        {
            StreamScript = _ => [TextChunk.Content("a"), TextChunk.Content("b"), TextChunk.Final()],
        };
        using var sp = FrontDoor(p);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sp.GetRequiredService<ITextClient>().StreamAsync(Req("span-fault"), cts.Token))
                await cts.CancelAsync();
        });

        var call = CallSpan(spans, "span-fault");
        Assert.Equal(ActivityStatusCode.Error, call.Status);
        Assert.NotNull(call.GetTagItem("error.type"));
    }

    [Fact]
    public async Task No_span_records_the_body()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeTextProvider("cs-body");
        p.Replies.Enqueue(new TextResponse(Answer, ProviderVerdict.Ok));
        using var sp = FrontDoor(p);

        await sp.GetRequiredService<ITextClient>().CompleteAsync(Req("span-body"));

        var call = CallSpan(spans, "span-body");
        foreach (var span in AttemptsUnder(spans, call).Append(call))
            Assert.All(span.TagObjects, tag =>
            {
                var value = tag.Value?.ToString() ?? "";
                Assert.DoesNotContain(Prompt, value);
                Assert.DoesNotContain(Answer, value);
            });
    }

    [Fact]
    public async Task A_cache_hit_is_a_call_span_with_no_attempt_under_it()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeTextProvider("cs-cache");
        p.Replies.Enqueue(new TextResponse(Answer, ProviderVerdict.Ok));
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => p).AddResponseCache().UseDefaultCandidates("cs-cache"));
        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<ITextClient>();

        await client.CompleteAsync(Req("span-cache"));
        await client.CompleteAsync(Req("span-cache"));

        List<Activity> calls;
        lock (spans) calls = [.. spans.Where(s => Equals(s.GetTagItem("lyntai.consumer"), "span-cache"))];
        Assert.Equal(2, calls.Count);
        Assert.Equal([1, 0], calls.OrderBy(c => c.StartTimeUtc).Select(c => AttemptsUnder(spans, c).Count));
    }

    [Fact]
    public async Task A_named_client_opens_the_same_call_span()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var p = new FakeTextProvider("cs-named");
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => p).UseDefaultCandidates("cs-named")
            .AddTextClient("judge", c => c.UseProviders("cs-named")));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ITextClientFactory>().Get("judge").CompleteAsync(Req("span-named"));

        Assert.Single(AttemptsUnder(spans, CallSpan(spans, "span-named")));
    }

    [Fact]
    public async Task The_generic_router_opens_one_call_span_over_its_attempts()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);

        var router = new ProviderRouter<VectorRequest, VectorResponse>(
            [new Vectors("cs-embed-down", VectorResponse.Failure(ProviderVerdict.Failed, "down")),
             new Vectors("cs-embed-up", VectorResponse.Success([[1f]]))],
            VectorResponse.Failure);

        var response = await router.CallAsync(new VectorRequest([Prompt], Consumer: "span-embed"));

        Assert.Equal(ProviderVerdict.Ok, response.Verdict);
        var call = CallSpan(spans, "span-embed");
        Assert.Equal("embeddings", call.GetTagItem("lyntai.call.operation"));
        Assert.Equal(["cs-embed-down", "cs-embed-up"],
            AttemptsUnder(spans, call).OrderBy(a => a.StartTimeUtc).Select(a => a.GetTagItem("gen_ai.system")));
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
}
