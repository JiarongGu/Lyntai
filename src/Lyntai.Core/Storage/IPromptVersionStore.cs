namespace Lyntai.Storage;

/// <summary>One stored revision of a named prompt override. Versions are monotonic per name; exactly
/// one is active at a time (the one the prompt registry renders).</summary>
public sealed record PromptVersion(
    string Name,
    int Version,
    string Template,
    string? Author,
    DateTimeOffset CreatedAt,
    bool IsActive);

/// <summary>
/// Versioned prompt overrides with history + rollback — an audit trail for <c>lyntai.prompt.*</c>
/// edits (who changed what, when) and the ability to revert. When registered, this is the source of
/// prompt overrides the registry reads; the plain <see cref="IKeyValueStore"/> path remains for
/// simple, unversioned use.
/// </summary>
public interface IPromptVersionStore
{
    /// <summary>The currently active override for a prompt, or null if none was ever set.</summary>
    Task<PromptVersion?> GetActiveAsync(string name, CancellationToken ct = default);

    /// <summary>Store a new revision and make it active. Returns the created version (its number is
    /// one past the highest existing for this name).</summary>
    Task<PromptVersion> SaveAsync(string name, string template, string? author = null, CancellationToken ct = default);

    /// <summary>All revisions for a prompt, newest version first.</summary>
    Task<IReadOnlyList<PromptVersion>> HistoryAsync(string name, CancellationToken ct = default);

    /// <summary>Re-activate an earlier revision by making it the active pointer (history is preserved —
    /// no revision is deleted or rewritten). Returns the now-active version, or null if that
    /// (name, version) doesn't exist.</summary>
    Task<PromptVersion?> RollbackAsync(string name, int version, CancellationToken ct = default);
}
