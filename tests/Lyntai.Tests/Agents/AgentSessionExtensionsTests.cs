using Lyntai.Agents;
using Lyntai.Llm;

namespace Lyntai.Tests.Agents;

/// <summary>Verifies <see cref="AgentSessionExtensions.RunAsync"/> — the result-door fold over the
/// event stream — and the defaults of <see cref="AgentSessionOptions"/>.</summary>
public class AgentSessionExtensionsTests
{
    // ── Fake ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeSession(IReadOnlyList<AgentStreamEvent> events) : IAgentSession
    {
        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
            AgentSessionOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var e in events)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
                await Task.Yield();
            }
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fold_happy_path_produces_correct_result()
    {
        var events = new AgentStreamEvent[]
        {
            new SessionStarted("sid"),
            new TextDelta("hi"),
            new UsageLive(1, 2, 0),
            new UsageFinal(10, 20, 5, 2, "claude-x"),
            new SessionEnded(LlmVerdict.Ok, IsError: false, Subtype: null, SessionId: "sid",
                FinalText: "final answer", Diagnostic: null),
        };
        var session = new FakeSession(events);

        var result = await session.RunAsync(new AgentSessionOptions { Prompt = "q" });

        Assert.Equal("sid", result.SessionId);
        Assert.Equal("final answer", result.FinalText);
        Assert.Equal(LlmVerdict.Ok, result.Verdict);
        Assert.False(result.IsError);
        Assert.NotNull(result.Usage);
        Assert.Equal(10, result.Usage!.Input);
        Assert.Equal("claude-x", result.Usage.Model);
    }

    [Fact]
    public async Task OnEvent_fires_for_every_event_in_order()
    {
        var events = new AgentStreamEvent[]
        {
            new SessionStarted("sid"),
            new TextDelta("hi"),
            new UsageLive(1, 2, 0),
            new UsageFinal(10, 20, 5, 2, "claude-x"),
            new SessionEnded(LlmVerdict.Ok, IsError: false, Subtype: null, SessionId: "sid",
                FinalText: "final answer", Diagnostic: null),
        };
        var session = new FakeSession(events);
        var received = new List<AgentStreamEvent>();

        await session.RunAsync(new AgentSessionOptions { Prompt = "q" }, onEvent: e => received.Add(e));

        Assert.Equal(5, received.Count);
        Assert.Equal(events, received);
    }

    [Fact]
    public async Task Fold_empty_final_text_falls_back_to_accumulated_text_deltas()
    {
        var events = new AgentStreamEvent[]
        {
            new SessionStarted("sid"),
            new TextDelta("Hello "),
            new Thinking("(ignored)"),
            new TextDelta("world"),
            new SessionEnded(LlmVerdict.Ok, IsError: false, Subtype: null, SessionId: "sid",
                FinalText: "", Diagnostic: null),
        };
        var session = new FakeSession(events);

        var result = await session.RunAsync(new AgentSessionOptions { Prompt = "q" });

        Assert.Equal("Hello world", result.FinalText);
    }

    [Fact]
    public async Task No_terminal_event_yields_failed_result_with_diagnostic()
    {
        var events = new AgentStreamEvent[]
        {
            new SessionStarted("s2"),
            new TextDelta("x"),
        };
        var session = new FakeSession(events);

        var result = await session.RunAsync(new AgentSessionOptions { Prompt = "q" });

        Assert.Equal(LlmVerdict.Failed, result.Verdict);
        Assert.True(result.IsError);
        Assert.NotNull(result.Diagnostic);
        Assert.Contains("terminal", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("s2", result.SessionId);
    }

    [Fact]
    public void AgentSessionOptions_defaults_are_correct()
    {
        var opts = new AgentSessionOptions { Prompt = "p" };

        Assert.Equal(AgentToolPolicy.ReadOnly, opts.ToolPolicy);
        Assert.Empty(opts.DisallowedTools);
    }
}
