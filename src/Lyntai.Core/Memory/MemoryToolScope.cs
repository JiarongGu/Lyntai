namespace Lyntai.Memory;

/// <summary>
/// Which task and scope the memory tools read and write, for the current asynchronous flow.
/// <para>A tool is a singleton, but the task a conversation belongs to is not — a chat application has one
/// task per conversation. Registration binds a DEFAULT, and this overrides it for the duration of a turn,
/// so both shapes work without anyone implementing <see cref="Lyntai.Agents.ITool"/> themselves.</para>
/// <para>Backed by <see cref="AsyncLocal{T}"/>, so concurrent turns on the same process cannot read each
/// other's scope.</para>
/// </summary>
public static class MemoryToolScope
{
    private static readonly AsyncLocal<(string TaskKey, string? Scope)?> Current = new();

    /// <summary>Point the memory tools at <paramref name="taskKey"/> until the returned handle is
    /// disposed. Restores whatever was in force before, so nesting behaves.</summary>
    /// <param name="taskKey">The task the current turn belongs to.</param>
    /// <param name="scope">Its variant, or null for every scope of the task.</param>
    public static IDisposable Use(string taskKey, string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        var previous = Current.Value;
        Current.Value = (taskKey, scope);
        return new Restore(previous);
    }

    /// <summary>The scope in force, or the supplied fallback when none has been set.</summary>
    /// <param name="fallbackTaskKey">The task bound at registration.</param>
    /// <param name="fallbackScope">The scope bound at registration.</param>
    internal static (string TaskKey, string? Scope) Resolve(string fallbackTaskKey, string? fallbackScope) =>
        Current.Value ?? (fallbackTaskKey, fallbackScope);

    private sealed class Restore((string TaskKey, string? Scope)? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
