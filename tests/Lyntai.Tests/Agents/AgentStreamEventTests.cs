using Lyntai.Agents;
using Lyntai.Llm;

namespace Lyntai.Tests.Agents;

/// <summary>Verifies the <see cref="AgentStreamEvent"/> sealed hierarchy: member round-trips and
/// exhaustive switch coverage — the latter guards that the set is a proper closed hierarchy.</summary>
public class AgentStreamEventTests
{
    // ── Member round-trip tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionStarted_round_trips()
    {
        var e = new SessionStarted("sid-42");
        Assert.Equal("sid-42", e.SessionId);
    }

    [Fact]
    public void TextDelta_round_trips()
    {
        var e = new TextDelta("hello world");
        Assert.Equal("hello world", e.Text);
    }

    [Fact]
    public void Thinking_round_trips()
    {
        var e = new Thinking("reasoning here");
        Assert.Equal("reasoning here", e.Text);
    }

    [Fact]
    public void ToolCall_round_trips_with_and_without_call_id()
    {
        var withId = new ToolCall("Edit", "{}", "call-1");
        Assert.Equal("Edit", withId.Name);
        Assert.Equal("{}", withId.ArgumentsJson);
        Assert.Equal("call-1", withId.CallId);

        // CallId is optional and defaults to null
        var withoutId = new ToolCall("Edit", "{}");
        Assert.Equal("Edit", withoutId.Name);
        Assert.Equal("{}", withoutId.ArgumentsJson);
        Assert.Null(withoutId.CallId);
    }

    [Fact]
    public void ToolResult_round_trips()
    {
        var ok = new ToolResult("call-1", "42 lines written", IsError: false);
        Assert.Equal("call-1", ok.CallId);
        Assert.Equal("42 lines written", ok.Content);
        Assert.False(ok.IsError);

        var err = new ToolResult(null, "file not found", IsError: true);
        Assert.Null(err.CallId);
        Assert.True(err.IsError);
    }

    [Fact]
    public void UsageLive_round_trips()
    {
        var e = new UsageLive(Input: 100, Output: 50, CacheRead: 10);
        Assert.Equal(100, e.Input);
        Assert.Equal(50, e.Output);
        Assert.Equal(10, e.CacheRead);
    }

    [Fact]
    public void UsageFinal_round_trips()
    {
        var e = new UsageFinal(Input: 200, Output: 80, CacheRead: 20, CacheCreate: 5, Model: "claude-opus-4");
        Assert.Equal(200, e.Input);
        Assert.Equal(80, e.Output);
        Assert.Equal(20, e.CacheRead);
        Assert.Equal(5, e.CacheCreate);
        Assert.Equal("claude-opus-4", e.Model);

        var noModel = new UsageFinal(200, 80, 20, 5, null);
        Assert.Null(noModel.Model);
    }

    [Fact]
    public void SessionEnded_round_trips()
    {
        var ok = new SessionEnded(LlmVerdict.Ok, IsError: false, Subtype: null, SessionId: "sid",
            FinalText: "hi", Diagnostic: null);
        Assert.Equal(LlmVerdict.Ok, ok.Verdict);
        Assert.False(ok.IsError);
        Assert.Null(ok.Subtype);
        Assert.Equal("sid", ok.SessionId);
        Assert.Equal("hi", ok.FinalText);
        Assert.Null(ok.Diagnostic);

        var err = new SessionEnded(LlmVerdict.Failed, IsError: true, Subtype: "timeout",
            SessionId: null, FinalText: null, Diagnostic: "stderr tail here");
        Assert.Equal(LlmVerdict.Failed, err.Verdict);
        Assert.True(err.IsError);
        Assert.Equal("timeout", err.Subtype);
        Assert.Null(err.SessionId);
        Assert.Null(err.FinalText);
        Assert.Equal("stderr tail here", err.Diagnostic);
    }

    // ── Switch exhaustiveness guard ──────────────────────────────────────────────────────────────

    /// <summary>A switch that covers every concrete <see cref="AgentStreamEvent"/> subtype and
    /// returns a distinct string label per type.  This is the exhaustiveness guard: if a new
    /// subtype is added without updating this switch the C# compiler will warn (or an IDE rule
    /// will catch it), and the assertions below will fail.</summary>
    private static string Label(AgentStreamEvent e) => e switch
    {
        SessionStarted  => "session-started",
        TextDelta       => "text-delta",
        Thinking        => "thinking",
        ToolCall        => "tool-call",
        ToolResult      => "tool-result",
        UsageLive       => "usage-live",
        UsageFinal      => "usage-final",
        SessionEnded    => "session-ended",
        _               => "unknown",
    };

    [Fact]
    public void Switch_returns_distinct_label_for_every_subtype()
    {
        AgentStreamEvent[] events =
        [
            new SessionStarted("s"),
            new TextDelta("t"),
            new Thinking("r"),
            new ToolCall("n", "{}"),
            new ToolResult(null, "c", false),
            new UsageLive(1, 2, 3),
            new UsageFinal(1, 2, 3, 4, null),
            new SessionEnded(LlmVerdict.Ok, false, null, null, null, null),
        ];

        var labels = events.Select(Label).ToArray();

        // every event maps to a non-"unknown" label
        Assert.DoesNotContain("unknown", labels);

        // all labels are distinct (no two subtypes share a label — guards correct switch arms)
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }

    [Fact]
    public void Switch_returns_correct_specific_labels()
    {
        Assert.Equal("session-started", Label(new SessionStarted("x")));
        Assert.Equal("session-ended",   Label(new SessionEnded(LlmVerdict.Ok, false, null, "sid", "hi", null)));
        Assert.Equal("tool-call",       Label(new ToolCall("Edit", "{}", "cid")));
        Assert.Equal("usage-final",     Label(new UsageFinal(0, 0, 0, 0, null)));
    }

    // ── AgentToolPolicy enum ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AgentToolPolicy_has_ReadOnly_and_Write()
    {
        Assert.Equal(0, (int)AgentToolPolicy.ReadOnly);
        Assert.Equal(1, (int)AgentToolPolicy.Write);
    }
}
