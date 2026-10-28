using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Agents;

namespace Lyntai.Providers.Acp;

/// <summary>The JSON-RPC 2.0 frames the ACP door sends, one line each. The protocol is newline-delimited, so a
/// frame never holds a raw line break: <see cref="JsonNode.ToJsonString(JsonSerializerOptions?)"/> escapes every
/// one inside a string.</summary>
internal static class AcpFrames
{
    /// <summary>The only protocol version this door speaks.</summary>
    public const int ProtocolVersion = 1;

    public const string AirSessionFailure = "sessionFailure";

    // fs and terminal are declined: the agent works in its own tools. The AIR capability is the one way an agent
    // that would otherwise end a failed turn with end_turn says the failure as a field (codex-acp does).
    public static string Initialize(int id, string clientVersion) => Request(id, "initialize", new JsonObject
    {
        ["protocolVersion"] = ProtocolVersion,
        ["clientCapabilities"] = new JsonObject
        {
            ["fs"] = new JsonObject { ["readTextFile"] = false, ["writeTextFile"] = false },
            ["terminal"] = false,
            ["_meta"] = new JsonObject
            {
                ["jetbrains"] = new JsonObject
                {
                    ["air"] = new JsonObject
                    {
                        ["version"] = 1,
                        ["capabilities"] = new JsonArray(JsonValue.Create(AirSessionFailure)),
                    },
                },
            },
        },
        ["clientInfo"] = new JsonObject { ["name"] = "lyntai", ["title"] = "Lyntai", ["version"] = clientVersion },
    });

    /// <summary><c>session/new</c>, or <c>session/load</c> / <c>session/resume</c> when <paramref name="sessionId"/>
    /// names the conversation to continue.</summary>
    public static string Open(int id, string method, string? sessionId, string cwd, JsonArray mcpServers)
    {
        var parameters = new JsonObject();
        if (sessionId is not null) parameters["sessionId"] = sessionId;
        parameters["cwd"] = cwd;
        parameters["mcpServers"] = mcpServers;
        return Request(id, method, parameters);
    }

    public static string Prompt(int id, string sessionId, IEnumerable<string> blocks) => Request(id, "session/prompt", new JsonObject
    {
        ["sessionId"] = sessionId,
        ["prompt"] = new JsonArray([.. blocks.Select(text =>
            (JsonNode)new JsonObject { ["type"] = "text", ["text"] = text })]),
    });

    public static string Cancel(string sessionId) =>
        Notification("session/cancel", new JsonObject { ["sessionId"] = sessionId });

    public static string PermissionSelected(JsonElement id, string optionId) => Result(id, new JsonObject
    {
        ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = optionId },
    });

    public static string PermissionCancelled(JsonElement id) => Result(id, new JsonObject
    {
        ["outcome"] = new JsonObject { ["outcome"] = "cancelled" },
    });

    /// <summary>The answer to a request this client does not implement. An unanswered request is a hung turn.</summary>
    public static string MethodNotFound(JsonElement id, string? method) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = JsonNode.Parse(id.GetRawText()),
        ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"lyntai does not implement {method}" },
    }.ToJsonString();

    /// <summary>The <c>mcpServers</c> a turn hands the agent, or null with the reason one cannot be rendered. Stdio
    /// is the protocol's baseline; HTTP needs the agent's <c>mcpCapabilities.http</c>.</summary>
    public static JsonArray? McpServers(IReadOnlyList<AgentMcpServer> servers, AcpCapabilities agent, out string? refusal)
    {
        refusal = null;
        var rendered = new JsonArray();
        foreach (var server in servers)
        {
            if (server.Transport == McpTransport.Http)
            {
                if (!agent.McpHttp)
                {
                    refusal = $"MCP server '{server.Name}' is Http, and the agent does not state mcpCapabilities.http.";
                    return null;
                }

                var headers = new JsonArray();
                if (server.AuthToken is { Length: > 0 } token)
                    headers.Add((JsonNode)new JsonObject { ["name"] = "Authorization", ["value"] = $"Bearer {token}" });
                rendered.Add((JsonNode)new JsonObject { ["type"] = "http", ["name"] = server.Name, ["url"] = server.Url, ["headers"] = headers });
                continue;
            }

            rendered.Add((JsonNode)new JsonObject
            {
                ["name"] = server.Name,
                ["command"] = server.Command,
                ["args"] = new JsonArray([.. server.Arguments.Select(a => (JsonNode)JsonValue.Create(a)!)]),
                ["env"] = new JsonArray([.. server.Environment.Select(pair =>
                    (JsonNode)new JsonObject { ["name"] = pair.Key, ["value"] = pair.Value })]),
            });
        }
        return rendered;
    }

    private static string Request(int id, string method, JsonNode parameters) => new JsonObject
    {
        ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters,
    }.ToJsonString();

    private static string Notification(string method, JsonNode parameters) => new JsonObject
    {
        ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters,
    }.ToJsonString();

    private static string Result(JsonElement id, JsonNode result) => new JsonObject
    {
        ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["result"] = result,
    }.ToJsonString();
}
