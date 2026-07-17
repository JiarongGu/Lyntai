namespace Lyntai.Agents;

/// <summary>A tool defined inline from a delegate — lets a consumer register a tool without writing a
/// class (<c>builder.AddTool(_ =&gt; new FunctionTool("now", (_, _) =&gt; Task.FromResult(DateTime.UtcNow.ToString("o"))))</c>).
/// For anything with dependencies, implement <see cref="ITool"/> directly so DI can inject them.</summary>
public sealed class FunctionTool(
    string name,
    Func<string, CancellationToken, Task<string>> invoke,
    string? description = null,
    string? parametersJsonSchema = null) : ITool
{
    public string Name => name;
    public string? Description => description;
    public string? ParametersJsonSchema => parametersJsonSchema;

    public Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default) => invoke(argumentsJson, ct);
}
