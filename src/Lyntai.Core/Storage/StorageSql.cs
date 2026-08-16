namespace Lyntai.Storage;

/// <summary>
/// The dialect-NEUTRAL SQL for the two store domains whose statements carry no dialect AT ALL, following
/// <see cref="JobStoreSql"/> and <see cref="MemoryGraphSql"/> (<c>docs/DECISIONS.md</c> D81).
///
/// <para><b>Only two domains are here, and that is a measured result rather than a stopping point.</b>
/// Comparing every SQL statement in each relational store against its twin: conversation is <b>9 of 9</b>
/// identical and trace <b>4 of 5</b> — the whole surface, so a shared copy removes a real drift channel.
/// Every other store shares only one-line <c>DELETE</c>s and <c>SELECT</c>s (two here, three there), where
/// a shared constant buys indirection rather than safety, and the queries that matter genuinely differ by
/// dialect.</para>
///
/// <para><b>Why this is a weaker case than the row types, stated so the two are not confused.</b> A
/// column↔property mismatch in a row type is a SILENT null; a column list that drifts between two INSERT
/// statements fails loudly at the database. This is worth sharing because the statements are word-for-word
/// identical and there is nothing dialect-specific left to justify a second copy — not because a drift here
/// would go unnoticed.</para>
/// </summary>
public static class ConversationStoreSql
{
    /// <summary>The thread column list, in the order <c>ChatThread</c> materializes.</summary>
    public const string ThreadCols =
        "SELECT id AS Id, title AS Title, created_at AS CreatedAt, metadata AS Metadata FROM lyntai_thread";

    public const string InsertThread =
        "INSERT INTO lyntai_thread (id, title, created_at, metadata) VALUES (@Id, @Title, @CreatedAt, @Metadata)";

    public const string GetThread = $"{ThreadCols} WHERE id = @id";

    /// <summary><c>id DESC</c> is the deterministic tiebreaker when two threads share a <c>created_at</c>
    /// tick — and it is the SAME ordering <see cref="PageThreadsAfter"/> compares its cursor with, which is
    /// what keeps a same-tick thread from being skipped or duplicated across pages.</summary>
    public const string ListThreads = $"{ThreadCols} ORDER BY created_at DESC, id DESC LIMIT @limit";

    /// <summary>Keyset paging over <see cref="ListThreads"/>' own ordering.</summary>
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

/// <summary>The dialect-neutral half of the relational <see cref="ITraceStore"/> backends — four of its
/// five statements. See <see cref="ConversationStoreSql"/> for why only these two domains are shared.</summary>
public static class TraceStoreSql
{
    public const string InsertTrace = """
        INSERT INTO lyntai_run_trace (session_id, mode, started_at, ended_at, trace_id)
        VALUES (@SessionId, @Mode, @StartedAt, @EndedAt, @TraceId)
        """;

    public const string InsertStep = """
        INSERT INTO lyntai_trace_step (session_id, seq, offset_ms, kind, label, input_tokens, output_tokens, cost_usd, duration_ms, detail)
        VALUES (@SessionId, @seq, @OffsetMs, @Kind, @Label, @InputTokens, @OutputTokens, @CostUsd, @DurationMs, @Detail)
        """;

    public const string GetTrace = """
        SELECT session_id AS SessionId, mode AS Mode, started_at AS StartedAt, ended_at AS EndedAt, trace_id AS TraceId
        FROM lyntai_run_trace WHERE session_id = @sessionId
        """;

    /// <summary>Replacing a trace deletes the old one first; the steps follow by cascade.</summary>
    public const string DeleteTrace = "DELETE FROM lyntai_run_trace WHERE session_id = @SessionId";
}
