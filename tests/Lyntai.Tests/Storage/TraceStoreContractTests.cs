using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="TraceStoreContract"/> method as a [Fact] — derive with a store factory and
/// the whole contract runs on that backend automatically (T11: no silent skips). Postgres deliberately
/// does NOT derive: it runs the Uid-namespaced subset on the shared container.</summary>
public abstract class TraceStoreContractFacts
{
    protected abstract ITraceStore NewStore();

    [Fact] public Task Save_load() => TraceStoreContract.Save_and_load_with_steps_totals_and_trace_id(NewStore(), "k");
    [Fact] public Task Resave_replaces() => TraceStoreContract.Saving_the_same_session_replaces_the_trace(NewStore(), "k");
    [Fact] public Task Unknown() => TraceStoreContract.Unknown_session_returns_null(NewStore(), "k");
    [Fact] public Task Seq_offset() => TraceStoreContract.Step_sequence_and_offset_round_trip(NewStore(), "k");
}

/// <summary>The <see cref="TraceStoreContract"/> against the InMemory backend.</summary>
public class InMemoryTraceStoreContractTests : TraceStoreContractFacts
{
    protected override ITraceStore NewStore() => new InMemoryTraceStore();
}

/// <summary>The <see cref="TraceStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteTraceStoreContractTests : TraceStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override ITraceStore NewStore() => new SqliteTraceStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
