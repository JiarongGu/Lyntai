using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>What the agent's open answer offers, read for <see cref="AgentOffers"/>: every config option (its category,
/// id, current value and values) and every mode, each list cut to <see cref="Max"/> entries, and nothing inferred from
/// either. A shape that does not read is left out, never guessed at.</summary>
internal static class AcpOffers
{
    /// <summary>The bound on every list, the one the refusal diagnostic's list of models already keeps.</summary>
    public const int Max = 25;

    public static AgentOffers Read(JsonElement open)
    {
        var truncated = false;
        var options = new List<AgentOfferedOption>();
        if (WireJson.Array(open, "configOptions") is { } list)
        {
            foreach (var option in list.EnumerateArray())
            {
                if (option.ValueKind != JsonValueKind.Object || WireJson.String(option, "id") is not { Length: > 0 } id) continue;
                if (options.Count == Max) { truncated = true; break; }
                var values = WireJson.Array(option, "options") is { } choices ? AcpModelOption.Values(choices).ToList() : [];
                if (values.Count > Max) { truncated = true; values = values.GetRange(0, Max); }
                options.Add(new AgentOfferedOption(WireJson.String(option, "category"), id, WireJson.String(option, "currentValue"), values));
            }
        }

        var modes = new List<AgentOfferedMode>();
        string? currentMode = null;
        if (WireJson.Object(open, "modes") is { } holder)
        {
            currentMode = WireJson.String(holder, "currentModeId");
            if (WireJson.Array(holder, "availableModes") is { } available)
            {
                foreach (var mode in available.EnumerateArray())
                {
                    if (mode.ValueKind != JsonValueKind.Object || WireJson.String(mode, "id") is not { Length: > 0 } id) continue;
                    if (modes.Count == Max) { truncated = true; break; }
                    modes.Add(new AgentOfferedMode(id, WireJson.String(mode, "name")));
                }
            }
        }
        return new AgentOffers(options, modes, currentMode, truncated);
    }
}
