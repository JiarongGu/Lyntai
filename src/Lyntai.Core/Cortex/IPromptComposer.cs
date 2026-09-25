namespace Lyntai.Cortex;

/// <summary>A chat's memory binding: composes a prompt with task-scoped recalled memory appended, and
/// remembers an exchange where the next compose reads it back.
/// <para>Both halves live on one seam so the store a chat WRITES is the store it READS. A composer reading one
/// store while the chat writes another recalls nothing the chat said, and nothing reports it.</para></summary>
public interface IPromptComposer
{
    /// <summary>Append recalled memory to <paramref name="basePrompt"/> as a bounded section.
    /// <para>Fail-open: no memory, a storage outage, or zero recalls yields the base prompt unchanged. Only the
    /// caller's cancellation propagates.</para></summary>
    /// <param name="basePrompt">The prompt to append to.</param>
    /// <param name="taskKey">The task whose memory to recall.</param>
    /// <param name="scope">Narrows the recall to one scope; null reads every scope of the task.</param>
    /// <param name="query">What to recall; null leaves the choice to the memory behind this composer.</param>
    /// <param name="limit">How many entries to recall; null takes the memory's own default.</param>
    /// <param name="ct">Cancellation.</param>
    Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default);

    /// <summary>Remember <paramref name="content"/> where <see cref="ComposeAsync"/> will read it back.
    /// <para>Surfaces failures: the caller decides whether a lost write is fatal (<c>ChatOrchestrator</c> logs it
    /// and keeps the turn). A composer with no writable memory behind it returns without writing.</para></summary>
    /// <param name="taskKey">The task to remember under.</param>
    /// <param name="scope">The scope to remember under.</param>
    /// <param name="content">What to remember.</param>
    /// <param name="ct">Cancellation.</param>
    Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default);
}
