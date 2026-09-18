using Lyntai.Inference;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;

namespace Lyntai.Benchmarks;

/// <summary>Router overhead per attempt — how much the fallback/policy machinery adds on top of a
/// bare provider call. The provider is an in-memory no-op, so the measured time is pure router cost.</summary>
[MemoryDiagnoser]
public class RouterBenchmarks
{
    private LlmRouter _router = null!;
    private LlmRouter _routerFallover = null!;
    private readonly TextRequest _req = new() { Messages = [TextMessage.User("bench")] };
    private readonly IReadOnlyList<ProviderCandidate> _single = [new ProviderCandidate("ok")];
    private readonly IReadOnlyList<ProviderCandidate> _two = [new ProviderCandidate("down"), new ProviderCandidate("ok")];

    [GlobalSetup]
    public void Setup()
    {
        var options = new LyntaiOptions();
        var ok = new NoopProvider("ok", ProviderVerdict.Ok);
        var down = new NoopProvider("down", ProviderVerdict.Failed);
        _router = new LlmRouter([ok], new DeadHostTracker(), options);
        _routerFallover = new LlmRouter([down, ok], new DeadHostTracker(), options);
    }

    [Benchmark(Baseline = true)]
    public async Task<TextResponse> SingleCandidate_Ok() => await _router.CompleteAsync(_single, _req);

    [Benchmark]
    public async Task<TextResponse> TwoCandidates_FirstFails() => await _routerFallover.CompleteAsync(_two, _req);

    [Benchmark]
    public async Task<string> Streaming_SingleCandidate()
    {
        var last = "";
        await foreach (var chunk in _router.StreamAsync(_single, _req))
            if (chunk.Kind == TextChunkKind.Content) last = chunk.Text;
        return last;
    }

    private sealed class NoopProvider(string id, ProviderVerdict verdict) : IModelProvider
    {
        public string Id => id;

        public ProviderCapabilities Capabilities { get; set; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = [ProviderOperation.Complete, ProviderOperation.Stream],
        };
        public bool IsAvailable => true;

        public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
            Task.FromResult(verdict == ProviderVerdict.Ok
                ? new TextResponse("ok", ProviderVerdict.Ok, new TextUsage(10, 5))
                : new TextResponse("", verdict, Detail: "noop-down"));

        public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (verdict != ProviderVerdict.Ok) { yield return TextChunk.Error(verdict, "noop-down"); yield break; }
            yield return TextChunk.Content("chunk");
            yield return TextChunk.Final(new TextUsage(10, 5));
            await Task.CompletedTask;
        }
    }
}
