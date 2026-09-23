namespace Lyntai;

/// <summary>Knobs for <see cref="FileSystemStorageBuilderExtensions.UseFileSystemStorage"/>.</summary>
public sealed class FileSystemStorageOptions
{
    /// <summary>The directory every served domain writes under. <b>Required, and never defaulted</b>: where
    /// an application's data lives is the application's decision, so an unset root fails the wiring rather
    /// than landing in a guessed location. Created when absent.
    /// <para><b>One process owns a root at a time.</b> The stores hold their records in memory and write
    /// through, so a second writer would make both views stale; a second owner is refused when its first store
    /// is built. Read the files whenever you like — they are always current — but edit them only while no
    /// process owns the root, because an owning process does not see an edit made under it.</para></summary>
    public string? Root { get; set; }
}
