using System.Text.Json;

using Lyntai.Generation;
using Lyntai.Generation.Tools;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>
/// <c>generate_backends</c> — the tool an agent is told to call FIRST, which is what makes its worst case
/// everyone's worst case.
///
/// <para><b>What it did until 2026-08-17 (archive Part 86).</b> It awaited <c>ProbeAsync</c> on every
/// registered provider in SEQUENCE, with no aggregate bound and no <c>try</c>. Each probe was capped only by
/// that backend's own <c>Timeout</c> — and the same option governs a render, so
/// <c>Automatic1111Options</c> and <c>OpenAiImageOptions</c> both default it to TEN MINUTES. Two HTTP
/// backends that accept a connection and stall therefore blocked the tool for about twenty. Every backend
/// disclosed its own timeout honestly; the COMPOSITION disclosed nothing.</para>
/// </summary>
public class GenerationBackendsToolTests
{
    private static GenerationBackendsTool Tool(TimeSpan deadline, params IGenerationProvider[] providers)
    {
        var options = new GenerationOptions { ProbeDeadline = deadline };
        return new GenerationBackendsTool(providers, options);
    }

    private static JsonElement Backends(string observation) =>
        JsonDocument.Parse(observation).RootElement.GetProperty("backends");

    private static JsonElement Backend(string observation, string id) =>
        Backends(observation).EnumerateArray().Single(b => b.GetProperty("id").GetString() == id);

    [Fact]
    public async Task It_lists_every_backend_with_what_each_supports()
    {
        var tool = Tool(TimeSpan.FromSeconds(5),
            new FakeGenerationProvider { Id = "images" },
            new FakeGenerationJobProvider { Id = "video" });

        var observation = await tool.InvokeAsync("{}");

        Assert.Equal(2, Backends(observation).GetArrayLength());
        Assert.True(Backend(observation, "images").GetProperty("usable").GetBoolean());
        Assert.Equal("job", Backend(observation, "video").GetProperty("delivery")[0].GetString());
    }

    /// <summary><b>The whole listing is bounded, not each probe.</b> A stalled backend must not be able to
    /// spend a caller's patience on a call whose entire job is to say what exists.
    /// <para><b>No wall-clock assertion, deliberately.</b> The stalled probe is
    /// <c>Task.Delay(Timeout.Infinite, ct)</c>, so a deadline that did not bind does not make this SLOW — it
    /// makes it hang forever, which the test runner reports far more loudly than any elapsed-time bound
    /// would. Asserting "under N seconds" here would add nothing except a dependency on machine load, which
    /// `pitfalls.md` records as reading like coverage and not being any.</para></summary>
    [Fact]
    public async Task A_stalled_backend_cannot_hold_the_listing_past_its_deadline()
    {
        var tool = Tool(TimeSpan.FromMilliseconds(200),
            new FakeGenerationProvider { Id = "healthy" },
            new BadProbeProvider { Id = "stalled" });

        var observation = await tool.InvokeAsync("{}");

        Assert.True(Backend(observation, "healthy").GetProperty("usable").GetBoolean());
        Assert.False(Backend(observation, "stalled").GetProperty("usable").GetBoolean());
    }

    /// <summary><b>An unreachable backend is REPORTED, never omitted.</b> Dropping it would tell the model the
    /// backend does not exist, which is a different and worse answer than "it is not answering" — the model
    /// would stop naming a backend the host has configured and which may be fine a minute later.</summary>
    [Fact]
    public async Task A_backend_that_misses_the_deadline_is_listed_as_unusable_with_the_reason()
    {
        var tool = Tool(TimeSpan.FromMilliseconds(200), new BadProbeProvider { Id = "stalled" });

        var backend = Backend(await tool.InvokeAsync("{}"), "stalled");

        Assert.False(backend.GetProperty("usable").GetBoolean());
        Assert.Contains("deadline", backend.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><b>One backend's defect does not discard the listing of every other.</b>
    /// <c>GenerationRouter</c> names itself the trust boundary for a BYO backend that throws instead of
    /// returning a verdict (<c>docs/DECISIONS.md</c> D64); this is a SECOND reader of the same registered
    /// collection, and it applied none of that — so a single throwing provider took the whole tool down with
    /// an exception the agent could do nothing with.</summary>
    [Fact]
    public async Task A_throwing_probe_becomes_an_observation_rather_than_failing_the_whole_tool()
    {
        var tool = Tool(TimeSpan.FromSeconds(5),
            new FakeGenerationProvider { Id = "healthy" },
            new BadProbeProvider { Id = "broken", Throws = new InvalidOperationException("boom") });

        var observation = await tool.InvokeAsync("{}");

        Assert.True(Backend(observation, "healthy").GetProperty("usable").GetBoolean());
        var broken = Backend(observation, "broken");
        Assert.False(broken.GetProperty("usable").GetBoolean());
        Assert.Contains("boom", broken.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>Probes run CONCURRENTLY — asserted by OBSERVING overlap rather than by timing it.
    ///
    /// <para><b>The first draft timed it and was flaky, which is the point worth keeping.</b> It ran three
    /// stalled probes under a 400ms deadline and asserted the total stayed under a second: true when run
    /// alone, and it failed inside a full-suite run on a loaded machine. `pitfalls.md` already records that
    /// exact shape from `ElapsedAgePolicy` — a test that depends on machine load reads as coverage and is
    /// not — and this is the same mistake made by the same hand an hour after writing it down.</para>
    ///
    /// <para>Overlap is the property anyway. Serial execution cannot produce a concurrency above 1 however
    /// fast or slow the machine is, so counting it is both deterministic AND a stronger statement than any
    /// elapsed bound.</para></summary>
    [Fact]
    public async Task Probes_run_concurrently_so_one_slow_backend_does_not_serialize_the_rest()
    {
        var live = 0;
        var peak = 0;
        var gate = new TaskCompletionSource();

        IGenerationProvider Counting(string id) => new CountingProbeProvider(id, async ct =>
        {
            var now = Interlocked.Increment(ref live);
            InterlockedMax(ref peak, now);
            if (now == 3) gate.TrySetResult();       // all three are inside ProbeAsync at once
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Decrement(ref live);
        });

        var tool = Tool(TimeSpan.FromSeconds(30), Counting("a"), Counting("b"), Counting("c"));

        await tool.InvokeAsync("{}");

        Assert.Equal(3, peak);   // serially this cannot exceed 1, at any speed
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }

    /// <summary>A backend whose probe runs a supplied body — for observing HOW the tool calls it rather than
    /// what it answers.</summary>
    private sealed class CountingProbeProvider(string id, Func<CancellationToken, Task> body)
        : IGenerationProvider
    {
        public string Id => id;

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Image],
            Deliveries = [GenerationDelivery.Inline],
        };

        public async Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
        {
            await body(ct).ConfigureAwait(false);
            return new GenerationProbeResult(true, "counted");
        }

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Failed, "not used"));
    }

    /// <summary>The CALLER's own cancellation still propagates. The listing's deadline is this tool's clock;
    /// a caller asking to stop is a different statement and must not be reported as a backend that failed to
    /// answer — the same discrimination <c>GenerationDeadline</c> makes for every backend.</summary>
    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_being_reported_as_an_unusable_backend()
    {
        var tool = Tool(TimeSpan.FromSeconds(30), new BadProbeProvider { Id = "stalled" });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.InvokeAsync("{}", cancelled.Token));
    }

    /// <summary>A non-positive deadline means NO deadline — the escape hatch for a host that owns its own
    /// timeouts, matching how every backend's own <c>Timeout</c> already reads a non-positive value.</summary>
    [Fact]
    public async Task A_non_positive_deadline_means_no_deadline()
    {
        var tool = Tool(TimeSpan.Zero, new FakeGenerationProvider { Id = "healthy" });

        var observation = await tool.InvokeAsync("{}");

        Assert.True(Backend(observation, "healthy").GetProperty("usable").GetBoolean());
    }
}
