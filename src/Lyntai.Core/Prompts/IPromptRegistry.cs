namespace Lyntai.Prompts;

/// <summary>Render a named prompt: an override stored under <c>lyntai.prompt.&lt;name&gt;</c> in the
/// key-value store replaces the code default, then <c>{placeholder}</c>s are filled from vars.</summary>
public interface IPromptRegistry
{
    Task<string> RenderAsync(string name, string defaultTemplate,
        IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default);

    /// <summary>Validate a candidate override against the default WITHOUT persisting: returns the default's
    /// <c>{placeholders}</c> the candidate would DROP (empty = valid). An admin save-flow calls this to
    /// REJECT a bad override up front with the exact missing tokens, rather than relying on the silent
    /// runtime fall-back <see cref="RenderAsync"/> does.</summary>
    IReadOnlyList<string> ValidateOverride(string defaultTemplate, string candidate);
}
