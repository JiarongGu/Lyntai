using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="TraceStoreContract"/> against the InMemory backend.</summary>
public class InMemoryTraceStoreContractTests
{
    private static InMemoryTraceStore New() => new();

    [Fact] public Task Save_load() => TraceStoreContract.Save_and_load_with_steps_totals_and_trace_id(New(), "k");
    [Fact] public Task Resave_replaces() => TraceStoreContract.Saving_the_same_session_replaces_the_trace(New(), "k");
    [Fact] public Task Unknown() => TraceStoreContract.Unknown_session_returns_null(New(), "k");
}

/// <summary>Runs the <see cref="TraceStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteTraceStoreContractTests : IDisposable
{
    private readonly TempDb _db = new();
    private SqliteTraceStore Store => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact] public Task Save_load() => TraceStoreContract.Save_and_load_with_steps_totals_and_trace_id(Store, "k");
    [Fact] public Task Resave_replaces() => TraceStoreContract.Saving_the_same_session_replaces_the_trace(Store, "k");
    [Fact] public Task Unknown() => TraceStoreContract.Unknown_session_returns_null(Store, "k");
}
