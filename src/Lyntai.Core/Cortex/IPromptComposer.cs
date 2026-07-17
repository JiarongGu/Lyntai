namespace Lyntai.Cortex;

/// <summary>Composes a prompt with task-scoped recalled memory appended (the Sonora pattern).
/// Fail-open: no memory store, a storage outage, or zero recalls → the base prompt unchanged.</summary>
public interface IPromptComposer
{
    Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default);
}
