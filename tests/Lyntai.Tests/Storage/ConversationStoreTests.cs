using Dapper;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>SQLite-SPECIFIC conversation-store concerns. The cross-backend semantics (create/get, thread +
/// per-message metadata, mixed-kind events, per-thread seq, GUID ids, role/content aliases, cascade,
/// list newest-first, CJK payloads) are pinned by <see cref="ConversationStoreContract"/>
/// (<see cref="SqliteConversationStoreContractTests"/>).</summary>
public class ConversationStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly SqliteConversationStore _store;

    public ConversationStoreTests() => _store = new SqliteConversationStore(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Delete_thread_cascades_to_the_message_table_via_fk()
    {
        // the contract proves the cascade through GetMessagesAsync; this additionally proves the FK
        // ON DELETE CASCADE actually removes the underlying lyntai_message rows (foreign_keys=ON).
        await _store.CreateThreadAsync("t1");
        await _store.AppendMessageAsync("t1", "user", "doomed");

        await _store.DeleteThreadAsync("t1");

        using var conn = _db.Factory.Open();
        Assert.Equal(0L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_message"));
    }
}
