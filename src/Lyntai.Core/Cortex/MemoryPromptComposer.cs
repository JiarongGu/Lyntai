using System.Text;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Cortex;

/// <summary>Default composer: appends a bounded "learned facts" section recalled from
/// <see cref="IMemoryStore"/>. Never throws — an outage yields the base prompt.</summary>
public sealed class MemoryPromptComposer(
    IMemoryStore? memory = null,
    ILogger<MemoryPromptComposer>? logger = null) : IPromptComposer
{
    private readonly ILogger _logger = logger ?? NullLogger<MemoryPromptComposer>.Instance;

    public async Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        if (memory is null) return basePrompt;

        IReadOnlyList<MemoryEntry> entries;
        try
        {
            entries = await memory.RecallAsync(taskKey, scope, query, limit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // recall is contractually fail-open, but a broken custom store must not sink the prompt
            _logger.LogWarning(ex, "memory recall failed while composing for {Task}; base prompt only", taskKey);
            return basePrompt;
        }
        if (entries.Count == 0) return basePrompt;

        var sb = new StringBuilder(basePrompt);
        sb.Append("\n\n## Learned facts (").Append(taskKey).Append(")\n");
        foreach (var entry in entries)
            sb.Append("- ").Append(entry.Content).Append('\n');
        return sb.ToString().TrimEnd();
    }
}
