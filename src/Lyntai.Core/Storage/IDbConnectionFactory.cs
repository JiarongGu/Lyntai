using System.Data.Common;

namespace Lyntai.Storage;

/// <summary>Opens ready-to-use database connections (backend pragmas already applied — for SQLite:
/// WAL journal, busy_timeout, foreign_keys=ON). The caller disposes.</summary>
public interface IDbConnectionFactory
{
    DbConnection Open();

    /// <summary>Async open — awaits the driver's connect (+ any pragmas) instead of blocking a threadpool
    /// thread, which matters for a networked/pooled backend (Postgres). Defaults to the sync
    /// <see cref="Open"/> wrapped in a completed task; the built-in factories override it with a genuinely
    /// async open.</summary>
    Task<DbConnection> OpenAsync(CancellationToken ct = default) => Task.FromResult(Open());
}
