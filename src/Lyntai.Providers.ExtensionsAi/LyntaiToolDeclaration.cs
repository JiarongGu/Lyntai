using System.Text.Json;
using Lyntai.Llm;
using Microsoft.Extensions.AI;

namespace Lyntai.Providers.ExtensionsAi;

/// <summary>
/// Bridges a Lyntai <see cref="LlmTool"/> to a Microsoft.Extensions.AI <see cref="AIFunctionDeclaration"/>
/// — a <em>declaration-only</em> tool (name + description + JSON schema, no invocable body). Lyntai's
/// own <see cref="Lyntai.Agents.IToolLoop"/> executes the tool and feeds results back, so the model just
/// needs to be told the tool exists; we deliberately don't wrap the client in a function-invoking
/// client. This is why we subclass <see cref="AIFunctionDeclaration"/> rather than <see cref="AIFunction"/>
/// (which would require an <c>InvokeCoreAsync</c> we never want called).
/// </summary>
internal sealed class LyntaiToolDeclaration(LlmTool tool) : AIFunctionDeclaration
{
    // JsonDocument.Parse (not JsonSerializer) so the package stays trim/AOT-clean — no reflection.
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private readonly JsonElement _schema = ParseSchema(tool.ParametersJsonSchema);

    public override string Name => tool.Name;

    public override string Description => tool.Description ?? "";

    public override JsonElement JsonSchema => _schema;

    private static JsonElement ParseSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return EmptyObjectSchema;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyObjectSchema;
        }
    }
}
