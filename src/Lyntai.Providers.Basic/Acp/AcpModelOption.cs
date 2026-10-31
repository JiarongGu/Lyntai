using System.Text.Json;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>The session's model option, as an agent's <c>configOptions</c> list states it: found by its
/// <c>category</c>, never by its id (<c>claude-agent-acp</c> calls it <c>model</c>, a host's own may not), and kept
/// only where it is a choice among values. Read without trusting the shape: an absent or malformed list is no
/// option.</summary>
/// <param name="Id">What <c>session/set_config_option</c> names it by.</param>
/// <param name="Current">The value it is set to now, or null when the agent stated none.</param>
/// <param name="Offered">Every value it may be set to, grouped choices flattened, in the agent's order.</param>
internal sealed record AcpModelOption(string Id, string? Current, IReadOnlyList<string> Offered)
{
    public const string Category = "model";

    /// <summary>The model option of <paramref name="holder"/>'s <c>configOptions</c> — a <c>session/new</c>,
    /// <c>load</c> or <c>resume</c> answer, or a <c>session/set_config_option</c> answer — or null.</summary>
    public static AcpModelOption? Read(JsonElement holder)
    {
        if (WireJson.Array(holder, "configOptions") is not { } list) return null;
        foreach (var option in list.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object || WireJson.String(option, "category") != Category) continue;
            if (WireJson.String(option, "id") is not { Length: > 0 } id || WireJson.Array(option, "options") is not { } choices) continue;
            return new AcpModelOption(id, WireJson.String(option, "currentValue"), [.. Values(choices)]);
        }
        return null;
    }

    private static IEnumerable<string> Values(JsonElement choices)
    {
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object) continue;
            if (WireJson.String(choice, "value") is { } value) yield return value;
            else if (WireJson.Array(choice, "options") is { } group)
                foreach (var inner in Values(group)) yield return inner;
        }
    }
}
