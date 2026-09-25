using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>The relational stores' fail-open reads degrade on a deadline of their OWN and still propagate the
/// CALLER's cancellation — told apart by <c>ct.IsCancellationRequested</c>, never by the exception's type,
/// because a BYO factory's open deadline arrives as <see cref="TaskCanceledException"/>, which IS an
/// <see cref="OperationCanceledException"/>. The factory throws before any SQL runs, so the Postgres stores
/// need no database here.</summary>
public class StoreFailOpenCancellationTests
{
    private static readonly LyntaiOptions Options = new();

    public static TheoryData<string> Reads() => ["sqlite-memory", "postgres-memory", "sqlite-curated", "postgres-curated"];

    private static Task<int> Read(string which, IDbConnectionFactory factory, CancellationToken ct) => which switch
    {
        "sqlite-memory" => Count(new SqliteMemoryStore(factory, Options).RecallAsync("t", query: "deploy", ct: ct)),
        "postgres-memory" => Count(new PostgresMemoryStore(factory, Options).RecallAsync("t", query: "deploy", ct: ct)),
        "sqlite-curated" => Count(new SqliteCuratedMemoryStore(factory).SearchAsync("deploy", ct: ct)),
        "postgres-curated" => Count(new PostgresCuratedMemoryStore(factory).SearchAsync("deploy", ct: ct)),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    private static async Task<int> Count<T>(Task<IReadOnlyList<T>> read) => (await read).Count;

    [Theory, MemberData(nameof(Reads))]
    public async Task A_deadline_of_the_stores_own_degrades_to_an_empty_read(string which)
    {
        Assert.Equal(0, await Read(which, new DeadlineFactory(), CancellationToken.None));
    }

    [Theory, MemberData(nameof(Reads))]
    public async Task The_callers_own_cancellation_still_propagates(string which)
    {
        using var cts = new CancellationTokenSource();

        // cancelled MID-call, from inside the open — a pre-cancelled token would be refused before the seam
        await Assert.ThrowsAsync<CallerCancelled>(() => Read(which, new CallerCancellingFactory(cts), cts.Token));
    }

    /// <summary>The LRU touch is best-effort INSIDE a recall that already found its hits, so a deadline there
    /// costs the access time and never the answer.</summary>
    [Fact]
    public async Task A_recall_keeps_its_hits_when_only_the_access_time_write_hits_a_deadline()
    {
        using var db = new TempDb();
        var lru = new LyntaiOptions { MemoryEviction = MemoryEvictionPolicy.CountCap(10, MemoryEvictionMode.Lru) };
        var store = new SqliteMemoryStore(new TouchDeadlineFactory(db.Factory), lru);
        await store.RememberAsync("t", "s", "the deploy pipeline requires approval");

        var hits = await store.RecallAsync("t", "s", "deploy");

        Assert.Single(hits);
    }

    private sealed class CallerCancelled(CancellationToken ct) : OperationCanceledException(ct);

    private sealed class DeadlineFactory : IDbConnectionFactory
    {
        public DbConnection Open() => throw new TaskCanceledException("the factory's own open deadline passed");

        public Task<DbConnection> OpenAsync(CancellationToken ct = default) =>
            Task.FromException<DbConnection>(new TaskCanceledException("the factory's own open deadline passed"));
    }

    private sealed class CallerCancellingFactory(CancellationTokenSource cts) : IDbConnectionFactory
    {
        public DbConnection Open() => throw new NotSupportedException();

        public Task<DbConnection> OpenAsync(CancellationToken ct = default)
        {
            cts.Cancel();
            return Task.FromException<DbConnection>(new CallerCancelled(ct));
        }
    }

    /// <summary>Real SQLite connections whose LRU access-time UPDATE alone hits a deadline.</summary>
    private sealed class TouchDeadlineFactory(IDbConnectionFactory inner) : IDbConnectionFactory
    {
        public DbConnection Open() => new Connection(inner.Open());

        public async Task<DbConnection> OpenAsync(CancellationToken ct = default) =>
            new Connection(await inner.OpenAsync(ct).ConfigureAwait(false));
    }

    private sealed class Connection(DbConnection inner) : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
        public override string Database => inner.Database;
        public override string DataSource => inner.DataSource;
        public override string ServerVersion => inner.ServerVersion;
        public override ConnectionState State => inner.State;
        public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
        public override void Close() => inner.Close();
        public override void Open() => inner.Open();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);
        protected override DbCommand CreateDbCommand() => new Command(inner.CreateCommand(), this);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class Command(DbCommand inner, DbConnection owner) : DbCommand
    {
        [AllowNull]
        public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => owner; set { } }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        protected override DbTransaction? DbTransaction { get => inner.Transaction; set => inner.Transaction = value; }
        public override void Cancel() => inner.Cancel();
        public override void Prepare() => inner.Prepare();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);
        public override object? ExecuteScalar() => inner.ExecuteScalar();

        public override int ExecuteNonQuery() => IsTouch
            ? throw new TaskCanceledException("the access-time write's own deadline passed")
            : inner.ExecuteNonQuery();

        private bool IsTouch => inner.CommandText.Contains("SET last_accessed_at", StringComparison.Ordinal);

        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
