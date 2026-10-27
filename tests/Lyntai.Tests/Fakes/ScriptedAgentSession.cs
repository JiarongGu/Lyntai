using System.Runtime.CompilerServices;
using Lyntai.Agents;

namespace Lyntai.Tests.Fakes;

/// <summary>An <see cref="IAgentSession"/> stand-in that plays one scripted attempt per call and keeps what each
/// call was handed, so a test reads exactly what a decorator sent. Spends no process and no token.</summary>
public sealed class ScriptedAgentSession(params IReadOnlyList<AgentStreamEvent>[] attempts) : IAgentSession
{
    /// <summary>The options of each call, in order.</summary>
    public List<AgentSessionOptions> Received { get; } = [];

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Received.Add(options);
        if (Received.Count > attempts.Length)
            throw new InvalidOperationException($"attempt {Received.Count} was not scripted");
        foreach (var e in attempts[Received.Count - 1])
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return e;
        }
    }

    /// <summary>The same failed attempt, <paramref name="count"/> times over.</summary>
    public static ScriptedAgentSession Failing(int count, IReadOnlyList<AgentStreamEvent> attempt) =>
        new([.. Enumerable.Repeat(attempt, count)]);
}
