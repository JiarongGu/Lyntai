using Dapper;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresConversationStore(IDbConnectionFactory factory) : IConversationStore
{
    public async Task<ChatThread> CreateThreadAsync(string id, string? title = null, string? metadata = null, CancellationToken ct = default)
    {
        var thread = new ChatThread(id, title, DateTimeOffset.UtcNow, metadata);
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO lyntai_thread (id, title, created_at, metadata) VALUES (@Id, @Title, @CreatedAt, @Metadata)",
            thread, cancellationToken: ct)).ConfigureAwait(false);
        return thread;
    }

    public async Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        return await conn.QuerySingleOrDefaultAsync<ChatThread>(new CommandDefinition(
            "SELECT id AS Id, title AS Title, created_at AS CreatedAt, metadata AS Metadata FROM lyntai_thread WHERE id = @id",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<ChatThread>(new CommandDefinition(
            "SELECT id AS Id, title AS Title, created_at AS CreatedAt, metadata AS Metadata FROM lyntai_thread ORDER BY created_at DESC, id DESC LIMIT @limit",
            new { limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_thread SET metadata = @metadata WHERE id = @id",
            new { id, metadata }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, string? metadata = null, CancellationToken ct = default)
    {
        // Id is a store-assigned GUID handle; Seq is the 1-based per-thread order. The UNIQUE(thread_id, seq)
        // index rejects a duplicate if two writers race the MAX(seq)+1 subquery (single-writer-per-thread is
        // the normal case; a rare concurrent append surfaces as a unique violation to retry).
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        using var conn = factory.Open();
        var seq = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_message (id, thread_id, seq, kind, payload, metadata, created_at)
            VALUES (@id, @threadId, (SELECT COALESCE(MAX(seq), 0) + 1 FROM lyntai_message WHERE thread_id = @threadId),
                    @kind, @payload, @metadata, @createdAt)
            RETURNING seq
            """, new { id, threadId, kind, payload, metadata, createdAt }, cancellationToken: ct)).ConfigureAwait(false);
        return new ChatMessage(id, threadId, seq, kind, payload, metadata, createdAt);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<ChatMessage>(new CommandDefinition("""
            SELECT id AS Id, thread_id AS ThreadId, seq AS Seq, kind AS Kind, payload AS Payload, metadata AS Metadata, created_at AS CreatedAt
            FROM lyntai_message WHERE thread_id = @threadId ORDER BY seq
            """, new { threadId }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task DeleteThreadAsync(string id, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_thread WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
