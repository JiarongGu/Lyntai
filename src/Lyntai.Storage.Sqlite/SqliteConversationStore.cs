using Dapper;

namespace Lyntai.Storage.Sqlite;

/// <summary>Conversation store over Lyntai's own <c>lyntai_thread</c> + <c>lyntai_message</c> tables
/// (Lyntai manages the schema). A thread is a typed event stream; the app attaches its OWN additional info
/// via the thread/message <c>metadata</c> JSON — it doesn't manage the tables. An app that needs its own
/// backend registers its own <see cref="IConversationStore"/> impl instead (it wins — <c>TryAdd</c>).</summary>
public sealed class SqliteConversationStore(IDbConnectionFactory factory) : IConversationStore
{
    // Id is a store-assigned GUID handle; Seq is the 1-based per-thread order (computed atomically as
    // MAX(seq)+1 within the INSERT). SQLite serializes writers, so the subquery can't race here.
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
            // id DESC is the deterministic tiebreaker when two threads share a created_at tick
            ConversationStoreSql.ListThreads,
            new { limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<int> CountThreadsAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            ConversationStoreSql.CountThreads, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatThread>> ListThreadsPageAsync(int limit, ChatThread? after = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // keyset paging: the cursor's (created_at, id) is compared with the SAME ordering ListThreads uses,
        // so same-tick threads (tiebroken by id DESC) are neither skipped nor duplicated across pages.
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
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var seq = await conn.ExecuteScalarAsync<long>(new CommandDefinition(ConversationStoreSql.AppendMessage, new { id, threadId, kind, payload, metadata, createdAt }, cancellationToken: ct)).ConfigureAwait(false);
        return new ChatMessage(id, threadId, seq, kind, payload, metadata, createdAt);
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
        // messages go with it via ON DELETE CASCADE (foreign_keys=ON from the factory)
        await conn.ExecuteAsync(new CommandDefinition(
            ConversationStoreSql.DeleteThread, new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
