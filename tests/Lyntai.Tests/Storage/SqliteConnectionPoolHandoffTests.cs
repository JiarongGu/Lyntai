using System.Data.Common;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Lyntai.Tests.Storage;

/// <summary>What the pooled <see cref="SqliteConnectionFactory"/> hands out: a handle that went back to the
/// pool is NOT inside a transaction, and a handle that is out is held by ONE caller. The pool is the driver's,
/// so these pin what the store relies on rather than what the store does.</summary>
public class SqliteConnectionPoolHandoffTests
{
    private static nint HandleOf(DbConnection c) => ((SqliteConnection)c).Handle!.DangerousGetHandle();

    private static bool InAutocommit(DbConnection c) => raw.sqlite3_get_autocommit(((SqliteConnection)c).Handle) != 0;

    private static void Exec(DbConnection c, string sql, DbTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    private static async Task<nint> Fail(SqliteConnectionFactory factory, string shape)
    {
        nint used = 0;
        try
        {
            await using var conn = await factory.OpenAsync();
            used = HandleOf(conn);
            if (shape == "busy")
            {
                // a second writer holds the lock, so this caller's BEGIN IMMEDIATE times out
                await using var holder = await factory.OpenAsync();
                await using var held = await holder.BeginTransactionAsync();
                Exec(conn, "PRAGMA busy_timeout=50");
                ((SqliteConnection)conn).DefaultTimeout = 1;
                await using var tx = await conn.BeginTransactionAsync();
                return used;
            }
            await using (var tx = await conn.BeginTransactionAsync())
            {
                switch (shape)
                {
                    case "constraint":
                        Exec(conn, "INSERT INTO t (v) VALUES ('x')", tx);
                        Exec(conn, "INSERT INTO t (v) VALUES ('x')", tx);
                        break;
                    case "cancelled":
                        using (var cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            await using var cmd = conn.CreateCommand();
                            cmd.CommandText = "INSERT INTO t (v) VALUES ('y')";
                            cmd.Transaction = tx;
                            await cmd.ExecuteNonQueryAsync(cts.Token);
                        }
                        break;
                    case "early-return":
                        Exec(conn, "INSERT INTO t (v) VALUES ('z')", tx);
                        return used; // the store's Stale / Refused / NotFound paths leave without committing
                    case "commit-fails":
                        Exec(conn, "PRAGMA defer_foreign_keys=ON", tx);
                        Exec(conn, "INSERT INTO child (parent_id) VALUES (999)", tx);
                        await tx.CommitAsync();
                        break;
                }
            }
        }
        catch (Exception e) when (e is SqliteException or OperationCanceledException)
        {
            return used;
        }
        throw new InvalidOperationException($"the '{shape}' shape did not fail");
    }

    [Theory]
    [InlineData("constraint")]
    [InlineData("cancelled")]
    [InlineData("early-return")]
    [InlineData("commit-fails")]
    [InlineData("busy")]
    public async Task A_handle_returns_to_the_pool_outside_any_transaction_after_a_failure_inside_one(string shape)
    {
        using var temp = new TempDbPath("handoff");
        var factory = new SqliteConnectionFactory(temp.Path);
        await using (var setup = await factory.OpenAsync())
        {
            Exec(setup, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT UNIQUE)");
            Exec(setup, "CREATE TABLE parent (id INTEGER PRIMARY KEY)");
            Exec(setup, "CREATE TABLE child (id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES parent(id))");
        }

        var used = await Fail(factory, shape);

        // rent everything the pool holds at once: the handle that failed must be among them, and clean
        var rented = new List<DbConnection>();
        try
        {
            for (var i = 0; i < 3; i++) rented.Add(await factory.OpenAsync());
            Assert.Contains(used, rented.Select(HandleOf));
            Assert.All(rented, c => Assert.True(InAutocommit(c), "a pooled handle came out inside a transaction"));
            await using var tx = await rented[0].BeginTransactionAsync();
            await tx.CommitAsync();
        }
        finally
        {
            foreach (var c in rented) await c.DisposeAsync();
        }
    }

    // 8 callers rent from a pool with nothing idle at the same instant, 200 rounds. Each must get its OWN handle:
    // the driver can hand one handle to two of them when a rent reads another's half-activated connection as
    // leaked and reclaims it. The window is narrow, so this is a statistical pin: the unfixed factory failed it in
    // 3 of 200 rounds on the run that found it.
    [Fact]
    public void Callers_renting_at_once_from_an_empty_pool_each_get_their_own_handle()
    {
        const int callers = 8, rounds = 200;
        using var temp = new TempDbPath("handoff");
        var factory = new SqliteConnectionFactory(temp.Path);
        var shared = new List<int>();

        for (var round = 0; round < rounds; round++)
        {
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString()))
                SqliteConnection.ClearPool(c);

            var start = new Barrier(callers);
            var allOut = new Barrier(callers);
            var handles = new nint[callers];
            var errors = new Exception?[callers];
            var threads = Enumerable.Range(0, callers).Select(i => new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    using var conn = factory.Open();
                    handles[i] = HandleOf(conn);
                    allOut.SignalAndWait();
                }
                catch (Exception e) { errors[i] = e; }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            Assert.All(errors, Assert.Null);
            if (handles.Distinct().Count() != callers) shared.Add(round);
        }

        Assert.True(shared.Count == 0, $"two callers held one handle in {shared.Count} of {rounds} rounds");
    }
}
