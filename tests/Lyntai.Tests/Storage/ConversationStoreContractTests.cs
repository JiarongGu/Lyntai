using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="ConversationStoreContract"/> against the InMemory backend.</summary>
public class InMemoryConversationStoreContractTests
{
    private static InMemoryConversationStore New() => new();

    [Fact] public Task Create_get() => ConversationStoreContract.Create_and_get_thread(New(), "k");
    [Fact] public Task Metadata() => ConversationStoreContract.Thread_metadata_round_trips_and_updates(New(), "k");
    [Fact] public Task Mixed_events() => ConversationStoreContract.Appends_mixed_kind_events_with_json_payloads_in_seq_order(New(), "k");
    [Fact] public Task Cjk() => ConversationStoreContract.Cjk_payload_round_trips(New(), "k");
    [Fact] public Task Seq_and_metadata() => ConversationStoreContract.Seq_is_1_based_and_restarts_per_thread_with_guid_ids_and_per_message_metadata(New(), "k");
    [Fact] public Task Aliases() => ConversationStoreContract.Role_content_aliases_map_to_kind_payload(New(), "k");
    [Fact] public Task Cascade() => ConversationStoreContract.Delete_thread_cascades_to_messages(New(), "k");
    [Fact] public Task List_newest_first() => ConversationStoreContract.List_threads_returns_newest_first(New(), "k");
}

/// <summary>Runs the <see cref="ConversationStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteConversationStoreContractTests : IDisposable
{
    private readonly TempDb _db = new();
    private SqliteConversationStore Store => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact] public Task Create_get() => ConversationStoreContract.Create_and_get_thread(Store, "k");
    [Fact] public Task Metadata() => ConversationStoreContract.Thread_metadata_round_trips_and_updates(Store, "k");
    [Fact] public Task Mixed_events() => ConversationStoreContract.Appends_mixed_kind_events_with_json_payloads_in_seq_order(Store, "k");
    [Fact] public Task Cjk() => ConversationStoreContract.Cjk_payload_round_trips(Store, "k");
    [Fact] public Task Seq_and_metadata() => ConversationStoreContract.Seq_is_1_based_and_restarts_per_thread_with_guid_ids_and_per_message_metadata(Store, "k");
    [Fact] public Task Aliases() => ConversationStoreContract.Role_content_aliases_map_to_kind_payload(Store, "k");
    [Fact] public Task Cascade() => ConversationStoreContract.Delete_thread_cascades_to_messages(Store, "k");
    [Fact] public Task List_newest_first() => ConversationStoreContract.List_threads_returns_newest_first(Store, "k");
}
