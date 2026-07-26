namespace Lyntai.Storage;

/// <summary>When (and whether) a <c>Use*Storage</c> call runs Lyntai's schema migrations — ONE knob
/// instead of two bools whose combination could contradict. The modes are mutually exclusive by
/// construction.</summary>
public enum SchemaMigration
{
    /// <summary>Migrate inside the <c>Use*Storage</c> call (the default — the historical behavior):
    /// DI composition pays the migration I/O once, and every store finds its schema ready.</summary>
    OnStartup = 0,

    /// <summary>Defer migrations to the FIRST connection open (via the lazy migrating factory), so DI
    /// composition does no I/O — for AOT/startup-sensitive hosts and container health checks. A transient
    /// first-migration failure is retried on the next open.</summary>
    OnFirstUse,

    /// <summary>The APP owns the schema — Lyntai runs no migrations and assumes the <c>lyntai_*</c>
    /// tables already exist (run the package's <c>MigrationRunnerService.MigrateUp</c> yourself to create
    /// them on your own terms).</summary>
    None,
}
