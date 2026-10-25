using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lyntai.Tests.Inference.ChatClients;

/// <summary>A scripted <see cref="IChatClient"/> that records what it was asked and whether it was disposed.</summary>
internal sealed class FakeChatClient : IChatClient
{
    public Func<List<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> OnResponse { get; set; } =
        (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

    public Func<List<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> OnStream { get; set; } =
        (_, _, _) => Updates(new ChatResponseUpdate(ChatRole.Assistant, "ok"));

    public List<List<ChatMessage>> Calls { get; } = [];
    public ChatOptions? LastOptions { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public int Disposed { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add(list);
        (LastOptions, LastToken) = (options, cancellationToken);
        return OnResponse(list, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add(list);
        (LastOptions, LastToken) = (options, cancellationToken);
        return OnStream(list, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Disposed++;

    public static async IAsyncEnumerable<ChatResponseUpdate> Updates(params ChatResponseUpdate[] updates)
    {
        await Task.CompletedTask;
        foreach (var u in updates) yield return u;
    }

    /// <summary>Yields <paramref name="first"/>, then fails — a stream that breaks after it has begun.</summary>
    public static async IAsyncEnumerable<ChatResponseUpdate> Breaks(Exception ex, params ChatResponseUpdate[] first)
    {
        await Task.CompletedTask;
        foreach (var u in first) yield return u;
        throw ex;
    }

    public static async IAsyncEnumerable<ChatResponseUpdate> Silent([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }
}
