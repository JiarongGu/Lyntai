using System.Text;
using Lyntai.Memory;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Cortex;

/// <summary>Default composer: appends a bounded "learned facts" section recalled from memory. HYBRID when
/// an <see cref="ISemanticMemory"/> is wired (i.e. embeddings are registered) — meaning-based hits lead
/// (they're query-relevant even without keyword overlap), then lexical <see cref="IMemoryStore"/> entries
/// fill in, deduped. Never throws — an outage in either source yields whatever the other returned (or the
/// base prompt).</summary>
public sealed class MemoryPromptComposer(
    IMemoryStore? memory = null,
    ISemanticMemory? semantic = null,
    ILogger<MemoryPromptComposer>? logger = null,
    int maxChars = 4000) : IPromptComposer
{
    private const int DefaultSemanticK = 10;
    private readonly ILogger _logger = logger ?? NullLogger<MemoryPromptComposer>.Instance;

    public async Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        if (memory is null && semantic is null) return basePrompt;

        // union of both sources, deduped by exact content, preserving insertion order (semantic first)
        var seen = new HashSet<string>();
        var contents = new List<string>();

        // semantic recall leads — but only with a concrete scope (a vector collection is per task+scope)
        // and a query to embed. Fail-open: a broken embedder/store falls through to lexical.
        if (semantic is not null && scope is not null && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var hits = await semantic.RecallAsync(taskKey, scope, query, limit ?? DefaultSemanticK, ct: ct).ConfigureAwait(false);
                foreach (var hit in hits) if (seen.Add(hit.Content)) contents.Add(hit.Content);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "semantic recall failed while composing for {Task}; continuing with lexical", taskKey); }
        }

        if (memory is not null)
        {
            try
            {
                var entries = await memory.RecallAsync(taskKey, scope, query, limit, ct).ConfigureAwait(false);
                foreach (var entry in entries) if (seen.Add(entry.Content)) contents.Add(entry.Content);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // recall is contractually fail-open, but a broken custom store must not sink the prompt
                _logger.LogWarning(ex, "memory recall failed while composing for {Task}; using what semantic returned", taskKey);
            }
        }

        if (contents.Count == 0) return basePrompt;

        // bound the appended section by BOTH entry count (via the recall limit) and a character budget,
        // so a handful of multi-KB facts can't blow the context window (design §8: max entries / char cap).
        var facts = new StringBuilder();
        var budget = maxChars;
        foreach (var content in contents)
        {
            var line = $"- {content}\n";
            if (line.Length > budget) break; // stop once the section budget is spent
            facts.Append(line);
            budget -= line.Length;
        }
        if (facts.Length == 0) return basePrompt; // even the first fact overflowed the budget

        return new StringBuilder(basePrompt)
            .Append("\n\n## Learned facts (").Append(taskKey).Append(")\n")
            .Append(facts)
            .ToString().TrimEnd();
    }
}
