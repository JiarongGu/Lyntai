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
    private readonly LlmRequest _req = new() { Messages = [LlmMessage.User("bench")] };
    private readonly IReadOnlyList<LlmCandidate> _single = [new LlmCandidate("ok")];
    private readonly IReadOnlyList<LlmCandidate> _two = [new LlmCandidate("down"), new LlmCandidate("ok")];

    [GlobalSetup]
    public void Setup()
    {
        var options = new LyntaiOptions();
        var ok = new NoopProvider("ok", LlmVerdict.Ok);
        var down = new NoopProvider("down", LlmVerdict.Failed);
        _router = new LlmRouter([ok], new DeadHostTracker(), options);
        _routerFallover = new LlmRouter([down, ok], new DeadHostTracker(), options);
    }

    [Benchmark(Baseline = true)]
    public async Task<LlmReply> SingleCandidate_Ok() => await _router.CompleteAsync(_single, _req);

    [Benchmark]
    public async Task<LlmReply> TwoCandidates_FirstFails() => await _routerFallover.CompleteAsync(_two, _req);

    [Benchmark]
    public async Task<string> Streaming_SingleCandidate()
    {
        var last = "";
        await foreach (var chunk in _router.StreamAsync(_single, _req))
            if (chunk.Kind == LlmChunkKind.Content) last = chunk.Text;
        return last;
    }

    private sealed class NoopProvider(string id, LlmVerdict verdict) : ILlmProvider
    {
        public string Id => id;
        public bool IsAvailable => true;

        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
            Task.FromResult(verdict == LlmVerdict.Ok
                ? new LlmReply("ok", LlmVerdict.Ok, new LlmUsage(10, 5))
                : new LlmReply("", verdict, Detail: "noop-down"));

        public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (verdict != LlmVerdict.Ok) { yield return LlmChunk.Error(verdict, "noop-down"); yield break; }
            yield return LlmChunk.Content("chunk");
            yield return LlmChunk.Final(new LlmUsage(10, 5));
            await Task.CompletedTask;
        }
    }
}
