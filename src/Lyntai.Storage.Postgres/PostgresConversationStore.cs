using Dapper;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresConversationStore(IDbConnectionFactory factory) : IConversationStore
{
    public async Task<ChatThread> CreateThreadAsync(string id, string? title = null, CancellationToken ct = default)
    {
        var thread = new ChatThread(id, title, DateTimeOffset.UtcNow);
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO lyntai_thread (id, title, created_at) VALUES (@Id, @Title, @CreatedAt)",
            thread, cancellationToken: ct)).ConfigureAwait(false);
        return thread;
    }

    public async Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        return await conn.QuerySingleOrDefaultAsync<ChatThread>(new CommandDefinition(
            "SELECT id AS Id, title AS Title, created_at AS CreatedAt FROM lyntai_thread WHERE id = @id",
            new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<ChatThread>(new CommandDefinition(
            "SELECT id AS Id, title AS Title, created_at AS CreatedAt FROM lyntai_thread ORDER BY created_at DESC, id DESC LIMIT @limit",
            new { limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<ChatMessage> AppendMessageAsync(string threadId, string role, string content, CancellationToken ct = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        using var conn = factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_message (thread_id, role, content, created_at) VALUES (@threadId, @role, @content, @createdAt)
            RETURNING id
            """, new { threadId, role, content, createdAt }, cancellationToken: ct)).ConfigureAwait(false);
        return new ChatMessage(id, threadId, role, content, createdAt);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<ChatMessage>(new CommandDefinition("""
            SELECT id AS Id, thread_id AS ThreadId, role AS Role, content AS Content, created_at AS CreatedAt
            FROM lyntai_message WHERE thread_id = @threadId ORDER BY id
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
