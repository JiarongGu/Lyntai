namespace Lyntai.Storage;

/// <summary>The relational <see cref="IConversationStore"/> statements, shared by both backends because
/// every one runs unchanged on each (<c>docs/DECISIONS.md</c> D81) — the same shape as
/// <see cref="JobStoreSql"/>. The Postgres append's bounded retry is a concurrency strategy, not a spelling,
/// and stays in that backend.</summary>
public static class ConversationStoreSql
{
    /// <summary>The thread column list, in the order <c>ChatThread</c> materializes.</summary>
    public const string ThreadCols =
        "SELECT id AS Id, title AS Title, created_at AS CreatedAt, metadata AS Metadata FROM lyntai_thread";

    public const string InsertThread =
        "INSERT INTO lyntai_thread (id, title, created_at, metadata) VALUES (@Id, @Title, @CreatedAt, @Metadata)";

    public const string GetThread = $"{ThreadCols} WHERE id = @id";

    /// <summary>Threads newest first. <c>id DESC</c> is the deterministic tiebreaker when two threads share a
    /// <c>created_at</c> tick — and it is the SAME ordering <see cref="PageThreadsAfter"/> compares its cursor
    /// with, which is what keeps a same-tick thread from being skipped or duplicated across pages.</summary>
    public const string ListThreads = $"{ThreadCols} ORDER BY created_at DESC, id DESC LIMIT @limit";

    /// <summary>Keyset paging over <see cref="ListThreads"/>' own ordering, strictly after the cursor.</summary>
    public const string PageThreadsAfter =
        $"{ThreadCols} WHERE created_at < @AfterCreatedAt OR (created_at = @AfterCreatedAt AND id < @AfterId) "
        + "ORDER BY created_at DESC, id DESC LIMIT @limit";

    public const string CountThreads = "SELECT COUNT(*) FROM lyntai_thread";

    public const string SetThreadMetadata = "UPDATE lyntai_thread SET metadata = @metadata WHERE id = @id";

    /// <summary>Messages go with the thread through <c>ON DELETE CASCADE</c> — which on SQLite only fires
    /// because the connection factory sets <c>foreign_keys=ON</c> per connection.</summary>
    public const string DeleteThread = "DELETE FROM lyntai_thread WHERE id = @id";

    /// <summary>Append one message, computing its 1-based per-thread <c>seq</c> atomically inside the
    /// INSERT rather than reading it first.</summary>
    public const string AppendMessage = """
        INSERT INTO lyntai_message (id, thread_id, seq, kind, payload, metadata, created_at)
        VALUES (@id, @threadId, (SELECT COALESCE(MAX(seq), 0) + 1 FROM lyntai_message WHERE thread_id = @threadId),
                @kind, @payload, @metadata, @createdAt)
        RETURNING seq
        """;

    public const string GetMessages = """
        SELECT id AS Id, thread_id AS ThreadId, seq AS Seq, kind AS Kind, payload AS Payload, metadata AS Metadata, created_at AS CreatedAt
        FROM lyntai_message WHERE thread_id = @threadId ORDER BY seq
        """;
}
