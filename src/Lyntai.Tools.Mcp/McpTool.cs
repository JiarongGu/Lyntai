using Lyntai.Agents;

namespace Lyntai.Tools.Mcp;

/// <summary>
/// A Lyntai <see cref="ITool"/> backed by a single tool on a Model Context Protocol (MCP) server. The
/// actual call is a delegate (<paramref name="invoke"/>) so the SDK's concrete <c>McpClientTool</c>
/// stays out of the contract and the adapter is unit-testable with a fake. <see cref="McpToolset"/>
/// builds these from a connected client; the app owns the client's lifecycle (BYO connection).
/// </summary>
public sealed class McpTool(
    string name,
    string? description,
    string? parametersJsonSchema,
    Func<string, CancellationToken, Task<string>> invoke) : ITool
{
    public string Name => name;
    public string? Description => description;
    public string? ParametersJsonSchema => parametersJsonSchema;

    public Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default) => invoke(argumentsJson, ct);
}
