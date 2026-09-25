using System.Data.Common;
using Lyntai.Storage.Relational;
using Npgsql;

namespace Lyntai.Storage.Postgres;

/// <summary>Opens Npgsql connections. Connection pooling is on by default in the connection string;
/// each <see cref="Open"/> returns a ready pooled connection.</summary>
public sealed class PostgresConnectionFactory : IDbConnectionFactory
{
    static PostgresConnectionFactory() => DapperConventions.Register();

    private readonly string _connectionString;

    public PostgresConnectionFactory(string connectionString) => _connectionString = connectionString;

    public DbConnection Open()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false); // async pooled connect (network round-trip)
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
