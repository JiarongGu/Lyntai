namespace Lyntai.Storage;

/// <summary>A conversation thread. (Named ChatThread — plain "Thread" collides with System.Threading.)</summary>
public sealed record ChatThread(string Id, string? Title, DateTimeOffset CreatedAt);

/// <summary>One message in a thread; Id is the store-assigned sequence.</summary>
public sealed record ChatMessage(long Id, string ThreadId, string Role, string Content, DateTimeOffset CreatedAt);

/// <summary>Threads + messages. Deleting a thread cascades to its messages.</summary>
public interface IConversationStore
{
    Task<ChatThread> CreateThreadAsync(string id, string? title = null, CancellationToken ct = default);

    Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default);

    Task<ChatMessage> AppendMessageAsync(string threadId, string role, string content, CancellationToken ct = default);

    /// <summary>Messages of a thread in append order.</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default);

    Task DeleteThreadAsync(string id, CancellationToken ct = default);
}
