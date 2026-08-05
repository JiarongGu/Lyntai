namespace Lyntai.Prompts;

/// <summary>Render a named prompt: an override replaces the code default — the active
/// <see cref="Lyntai.Storage.IPromptVersionStore"/> revision when a version store is wired, else the
/// key-value entry under the registry's configured <see cref="PromptRegistry.KeyPrefix"/> (default
/// <c>lyntai.prompt.</c>, settable via <c>LyntaiOptions.PromptKeyPrefix</c>) — then
/// <c>{placeholder}</c>s are filled from vars.</summary>
public interface IPromptRegistry
{
    Task<string> RenderAsync(string name, string defaultTemplate,
        IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default);

    /// <summary>Validate a candidate override against the default WITHOUT persisting: returns the default's
    /// <c>{placeholders}</c> the candidate would DROP (empty = valid). An admin save-flow calls this to
    /// REJECT a bad override up front with the exact missing tokens, rather than relying on the silent
    /// runtime fall-back <see cref="RenderAsync"/> does.
    /// <para>Validation sees IDENTIFIER-shaped placeholders only (<c>[A-Za-z_][A-Za-z0-9_]*</c>), while
    /// <see cref="RenderAsync"/> substitutes ANY var key — hyphen, dot and CJK keys included. So a default
    /// template carrying a non-identifier <c>{placeholder}</c> renders fine, but an override that drops it
    /// is reported valid here and the content is lost silently.</para></summary>
    IReadOnlyList<string> ValidateOverride(string defaultTemplate, string candidate);
}
