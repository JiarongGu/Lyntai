using System.Data.Common;

namespace Lyntai.Storage;

/// <summary>Opens ready-to-use database connections (backend pragmas already applied — for SQLite:
/// WAL journal, busy_timeout, foreign_keys=ON). The caller disposes.</summary>
public interface IDbConnectionFactory
{
    DbConnection Open();
}
