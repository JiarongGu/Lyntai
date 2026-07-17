namespace Lyntai.Prompt;

/// <summary>Render a named prompt: an override stored under <c>lyntai.prompt.&lt;name&gt;</c> in the
/// key-value store replaces the code default, then <c>{placeholder}</c>s are filled from vars.</summary>
public interface IPromptRegistry
{
    Task<string> RenderAsync(string name, string defaultTemplate,
        IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default);
}
