using System.Text.Json;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>What an agent's <c>initialize</c> answer states, read without trusting its shape: a capability of the
/// wrong kind is no capability, and an absent one is not stated.</summary>
/// <param name="ProtocolVersion">The version the agent answered, or null when it stated none.</param>
/// <param name="LoadSession"><c>agentCapabilities.loadSession</c>: it can load a conversation, replaying it as updates.</param>
/// <param name="Resume"><c>agentCapabilities.sessionCapabilities.resume</c>: it can continue one without a replay.</param>
/// <param name="McpHttp"><c>agentCapabilities.mcpCapabilities.http</c>.</param>
internal sealed record AcpCapabilities(int? ProtocolVersion, bool LoadSession, bool Resume, bool McpHttp)
{
    /// <summary>The request that continues a conversation by what the agent states: <c>session/resume</c> first,
    /// since it replays nothing, then <c>session/load</c>; null when it states neither.</summary>
    public string? ResumeMethod => Resume ? "session/resume" : LoadSession ? "session/load" : null;

    public static AcpCapabilities Read(JsonElement result)
    {
        var capabilities = WireJson.Object(result, "agentCapabilities") ?? default;
        var sessions = WireJson.Object(capabilities, "sessionCapabilities") ?? default;
        var mcp = WireJson.Object(capabilities, "mcpCapabilities") ?? default;
        return new AcpCapabilities(
            WireJson.Int32(result, "protocolVersion"),
            capabilities.ValueKind == JsonValueKind.Object && capabilities.TryGetProperty("loadSession", out var load) && load.ValueKind == JsonValueKind.True,
            WireJson.Object(sessions, "resume") is not null,
            mcp.ValueKind == JsonValueKind.Object && mcp.TryGetProperty("http", out var http) && http.ValueKind == JsonValueKind.True);
    }
}
