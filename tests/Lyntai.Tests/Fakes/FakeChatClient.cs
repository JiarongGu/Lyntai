using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted Microsoft.Extensions.AI client for bridge tests.</summary>
public sealed class FakeChatClient : IChatClient
{
    public ChatResponse Response { get; set; } = new(new ChatMessage(ChatRole.Assistant, "fake meai reply"));

    public List<ChatResponseUpdate> Updates { get; } = [];

    public Exception? ThrowOnCall { get; set; }

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(([.. messages], options));
        if (ThrowOnCall is not null) throw ThrowOnCall;
        return Task.FromResult(Response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(([.. messages], options));
        if (ThrowOnCall is not null) throw ThrowOnCall;
        foreach (var update in Updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
