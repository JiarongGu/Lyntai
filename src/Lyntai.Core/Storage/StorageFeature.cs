namespace Lyntai.Storage;

/// <summary>Selects which storage-domain features a backend wires. Each flag maps a domain to its store(s)
/// AND its migration(s): a feature the app DISABLES registers no store and lands no table (no unused
/// <c>lyntai_*</c> tables for domains you don't use). Default is <see cref="All"/> — every domain, the
/// historical behavior. Compose with <c>|</c>, e.g. <c>StorageFeature.Score | StorageFeature.Conversation</c>.</summary>
[Flags]
public enum StorageFeature
{
    /// <summary>No storage domains (only the migration version table is created).</summary>
    None = 0,

    /// <summary>Key-value store (<c>lyntai_kv</c>) — prompt/model overrides, scheduler next-run, secret vault.</summary>
    KeyValue = 1 << 0,

    /// <summary>Conversation store (<c>lyntai_thread</c> + <c>lyntai_message</c>).</summary>
    Conversation = 1 << 1,

    /// <summary>Task-scoped memory store (<c>lyntai_memory_entry</c> + FTS).</summary>
    Memory = 1 << 2,

    /// <summary>Scorer results (<c>lyntai_score_result</c>).</summary>
    Score = 1 << 3,

    /// <summary>Run traces (<c>lyntai_run_trace</c> + <c>lyntai_trace_step</c>).</summary>
    Trace = 1 << 4,

    /// <summary>Versioned prompts (<c>lyntai_prompt_version</c>).</summary>
    PromptVersion = 1 << 5,

    /// <summary>Durable jobs (<c>lyntai_job</c>).</summary>
    Jobs = 1 << 6,

    /// <summary>Front-door governance persistence (response cache, usage tracker, vector store).</summary>
    Governance = 1 << 7,

    /// <summary>Curated memory catalog (<c>lyntai_curated_memory</c>).</summary>
    CuratedMemory = 1 << 8,

    /// <summary>Every storage domain (the default — unchanged historical behavior).</summary>
    All = KeyValue | Conversation | Memory | Score | Trace | PromptVersion | Jobs | Governance | CuratedMemory,
}

/// <summary>Maps <see cref="StorageFeature"/> flags to the migration tag names the runner activates. Each
/// migration is tagged with its feature's name (<c>[Tags(nameof(StorageFeature.X))]</c>); the runner applies
/// only migrations whose tag is in the active set, so a disabled feature's tables are never created.</summary>
public static class StorageFeatures
{
    /// <summary>A tag carried by EVERY migration (alongside its feature tag), so the <see cref="StorageFeature.All"/>
    /// path runs in a single pass requesting just this tag — FluentMigrator runs a migration only when the
    /// requested tags are all present on it, and every migration has this one. Underscored to never collide
    /// with a feature name.</summary>
    public const string AllTag = "__all__";

    /// <summary>The feature tag names for the selected features — the individual flags that are set (never
    /// <see cref="StorageFeature.None"/>/<see cref="StorageFeature.All"/> themselves). A SUBSET migrates one
    /// pass per tag (each migration carries exactly one feature tag); <see cref="StorageFeature.All"/> uses
    /// <see cref="AllTag"/> instead (one pass).</summary>
    public static string[] TagsFor(StorageFeature features) =>
        [.. Enum.GetValues<StorageFeature>()
            .Where(f => f is not (StorageFeature.None or StorageFeature.All) && features.HasFlag(f))
            .Select(f => f.ToString())];
}
