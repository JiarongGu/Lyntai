using System.Data.Common;

namespace Lyntai.Storage;

/// <summary>
/// Decorates an <see cref="IDbConnectionFactory"/> to run <paramref name="migrateOnce"/> exactly once,
/// lazily, on the FIRST successful open (sync or async) — so "migrate on first use" wiring does no I/O
/// during DI composition. Thread-safe: concurrent first-opens block until the single migration completes,
/// and a TRANSIENT first-migration failure is retried on the next open (the flag flips only on success —
/// no permanently-cached exception, unlike a <see cref="Lazy{T}"/>). The storage packages' migrating
/// factories wrap this so the gate's semantics live in ONE place; a BYO backend can reuse it the same way.
/// </summary>
public sealed class LazyMigratingConnectionFactory(IDbConnectionFactory inner, Action migrateOnce) : IDbConnectionFactory
{
    private readonly Lock _gate = new();
    private volatile bool _migrated;

    public DbConnection Open()
    {
        EnsureMigrated();
        return inner.Open();
    }

    public Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        // the migration itself is synchronous (FluentMigrator) and one-time; only the open is async —
        // and the inner factory's genuinely-async open must not be flattened by this decorator
        EnsureMigrated();
        return inner.OpenAsync(ct);
    }

    private void EnsureMigrated()
    {
        if (_migrated) return;
        lock (_gate)
        {
            if (_migrated) return;
            migrateOnce(); // throws → _migrated stays false → the next open retries
            _migrated = true;
        }
    }
}
