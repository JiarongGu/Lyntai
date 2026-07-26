using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="ConversationStoreContract"/> method as a [Fact] — derive with a store
/// factory and the whole contract runs on that backend automatically (T11: no silent skips). Postgres
/// deliberately does NOT derive: it runs the Uid-namespaced subset on the shared container.</summary>
public abstract class ConversationStoreContractFacts
{
    protected abstract IConversationStore NewStore();

    [Fact] public Task Create_get() => ConversationStoreContract.Create_and_get_thread(NewStore(), "k");
    [Fact] public Task Duplicate_id() => ConversationStoreContract.Duplicate_thread_id_throws_and_preserves_the_original(NewStore(), "k");
    [Fact] public Task Metadata() => ConversationStoreContract.Thread_metadata_round_trips_and_updates(NewStore(), "k");
    [Fact] public Task Mixed_events() => ConversationStoreContract.Appends_mixed_kind_events_with_json_payloads_in_seq_order(NewStore(), "k");
    [Fact] public Task Cjk() => ConversationStoreContract.Cjk_payload_round_trips(NewStore(), "k");
    [Fact] public Task Seq_and_metadata() => ConversationStoreContract.Seq_is_1_based_and_restarts_per_thread_with_guid_ids_and_per_message_metadata(NewStore(), "k");
    [Fact] public Task Aliases() => ConversationStoreContract.Role_content_aliases_map_to_kind_payload(NewStore(), "k");
    [Fact] public Task Cascade() => ConversationStoreContract.Delete_thread_cascades_to_messages(NewStore(), "k");
    [Fact] public Task List_newest_first() => ConversationStoreContract.List_threads_returns_newest_first(NewStore(), "k");
    [Fact] public Task Count() => ConversationStoreContract.Count_reflects_inserted_and_deleted_threads(NewStore(), "k");
    [Fact] public Task Paged() => ConversationStoreContract.Paged_cursor_walks_every_thread_exactly_once(NewStore(), "k");
}

/// <summary>The <see cref="ConversationStoreContract"/> against the InMemory backend.</summary>
public class InMemoryConversationStoreContractTests : ConversationStoreContractFacts
{
    protected override IConversationStore NewStore() => new InMemoryConversationStore();
}

/// <summary>The <see cref="ConversationStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteConversationStoreContractTests : ConversationStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IConversationStore NewStore() => new SqliteConversationStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
