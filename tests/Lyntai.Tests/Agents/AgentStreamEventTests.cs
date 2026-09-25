using Lyntai.Agents;

namespace Lyntai.Tests.Agents;

/// <summary>The <see cref="AgentStreamEvent"/> hierarchy is CLOSED: a consumer switches over it, so a new
/// subtype is a change every consumer's switch has to absorb, and it must be made on purpose.</summary>
public class AgentStreamEventTests
{
    [Fact]
    public void The_hierarchy_is_exactly_the_known_sealed_events()
    {
        var subtypes = typeof(AgentStreamEvent).Assembly.GetTypes()
            .Where(t => t != typeof(AgentStreamEvent) && typeof(AgentStreamEvent).IsAssignableFrom(t))
            .ToList();

        Assert.True(typeof(AgentStreamEvent).IsAbstract);
        Assert.All(subtypes, t => Assert.True(t.IsSealed, $"{t.Name} is not sealed, so the hierarchy is open"));
        Assert.Equal(
            ["SessionEnded", "SessionStarted", "TextDelta", "Thinking", "ToolCall", "ToolResult", "UsageFinal", "UsageLive"],
            subtypes.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AgentToolPolicy_has_ReadOnly_and_Write()
    {
        Assert.Equal(0, (int)AgentToolPolicy.ReadOnly);
        Assert.Equal(1, (int)AgentToolPolicy.Write);
    }
}
