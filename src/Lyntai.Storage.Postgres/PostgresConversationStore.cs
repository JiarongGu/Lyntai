using Dapper;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresConversationStore(IDbConnectionFactory factory) : IConversationStore
{
    public async Task<ChatThread> CreateThreadAsync(string id, string? title = null, string? metadata = null, CancellationToken ct = default)
    {
        var thread = new ChatThread(id, title, DateTimeOffset.UtcNow, metadata);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            ConversationStoreSql.InsertThread,
            thread, cancellationToken: ct)).ConfigureAwait(false);
        return thread;
    }

    public async Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<ChatThread>(new CommandDefinition(
            ConversationStoreSql.GetThread,
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<ChatThread>(new CommandDefinition(
            ConversationStoreSql.ListThreads,
            new { limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<int> CountThreadsAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            ConversationStoreSql.CountThreads, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatThread>> ListThreadsPageAsync(int limit, ChatThread? after = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // keyset paging: the (created_at, id) cursor is compared with the SAME ordering ListThreads uses
        // (created_at DESC, id DESC) so same-timestamp threads are neither skipped nor duplicated.
        var sql = after is null ? ConversationStoreSql.ListThreads : ConversationStoreSql.PageThreadsAfter;
        var rows = await conn.QueryAsync<ChatThread>(new CommandDefinition(sql,
            new { limit, AfterCreatedAt = after?.CreatedAt, AfterId = after?.Id }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            ConversationStoreSql.SetThreadMetadata,
            new { id, metadata }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, string? metadata = null, CancellationToken ct = default)
    {
        // Id is a store-assigned GUID handle; Seq is the 1-based per-thread order. The UNIQUE(thread_id, seq)
        // index rejects a duplicate if two writers race the MAX(seq)+1 subquery (single-writer-per-thread is
        // the normal case) — a rare concurrent append is retried HERE (bounded), recomputing MAX(seq)+1, so
        // the caller never sees a raw unique-violation for a transient race (SQLite serializes writes and
        // InMemory locks — this keeps the three backends' observable behavior aligned).
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var seq = await conn.ExecuteScalarAsync<long>(new CommandDefinition(ConversationStoreSql.AppendMessage, new { id, threadId, kind, payload, metadata, createdAt }, cancellationToken: ct)).ConfigureAwait(false);
                return new ChatMessage(id, threadId, seq, kind, payload, metadata, createdAt);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505" && attempt < 3)
            {
                // 23505 unique_violation on (thread_id, seq): a concurrent append won the slot — recompute
            }
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<ChatMessage>(new CommandDefinition(ConversationStoreSql.GetMessages, new { threadId }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task DeleteThreadAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            ConversationStoreSql.DeleteThread, new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
