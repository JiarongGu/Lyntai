using System.Text;

namespace Lyntai.Memory;

/// <summary>How an engine's recall is rendered into a prompt.</summary>
public sealed record MemoryCompositionOptions
{
    /// <summary>Total characters the appended sections may use.</summary>
    public int Budget { get; init; } = 4000;

    /// <summary>Characters reserved for authoritative material and allocated BEFORE any associative
    /// content is admitted. Associative content then spends what remains of <see cref="Budget"/>.
    /// <para>A flat first-come budget lets a burst of loosely-relevant recall push a hard constraint out of
    /// the prompt entirely, with nothing reporting it — the prompt still looks full. That is the failure
    /// this reservation exists to prevent.</para></summary>
    public int AuthoritativeReserve { get; init; } = 1000;

    /// <summary>Heading for exact material.</summary>
    public string AuthoritativeHeading { get; init; } = "## Known facts (authoritative)";

    /// <summary>Heading for recalled material.</summary>
    public string AssociativeHeading { get; init; } =
        "## Recalled context (associative — may be stale or partial)";
}

/// <summary>Renders an engine's recall into a prompt. Composing is something an engine DOES, so there is no
/// separate composer type — "several composers" is already "several engines".</summary>
public static class MemoryComposition
{
    /// <summary>Append this engine's relevant material to <paramref name="basePrompt"/> as two labelled
    /// sections, authoritative first — so the model is told which material is exact rather than left to
    /// infer it.
    /// <para>Never throws except on cancellation: a faulting engine yields the base prompt unchanged.</para></summary>
    /// <param name="engine">The engine to recall from.</param>
    /// <param name="basePrompt">The prompt to append to.</param>
    /// <param name="query">What to recall.</param>
    /// <param name="options">Budget and headings; null takes the defaults.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    public static async Task<string> ComposeAsync(this IMemoryEngine engine, string basePrompt,
        MemoryQuery query, MemoryCompositionOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var opts = options ?? new MemoryCompositionOptions();

        MemoryRecall recall;
        try
        {
            recall = await engine.RecallAsync(query, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // recall is contractually fail-open, but a BYO engine that ignores that must not sink the prompt
            return basePrompt;
        }

        if (recall.Items.Count == 0) return basePrompt;

        var exact = recall.Items.Where(i => i.Grade == MemoryGrade.Authoritative).ToList();
        var recalled = recall.Items.Where(i => i.Grade != MemoryGrade.Authoritative).ToList();

        // authoritative out of its own reserve FIRST, then associative from whatever of the total is left
        var (exactText, omitted) = Fill(exact, Math.Min(opts.AuthoritativeReserve, opts.Budget), verbatim: true);
        var (recalledText, _) = Fill(recalled, Math.Max(0, opts.Budget - exactText.Length), verbatim: false);

        if (exactText.Length == 0 && recalledText.Length == 0) return basePrompt;

        var sb = new StringBuilder(basePrompt);
        if (exactText.Length > 0)
        {
            sb.Append("\n\n").Append(opts.AuthoritativeHeading).Append('\n').Append(exactText);
            if (omitted > 0)
                sb.Append("… ").Append(omitted).Append(" further authoritative facts omitted (budget)\n");
        }
        if (recalledText.Length > 0)
            sb.Append("\n\n").Append(opts.AssociativeHeading).Append('\n').Append(recalledText);

        return sb.ToString().TrimEnd();

        static (string Text, int Omitted) Fill(List<MemoryItem> items, int budget, bool verbatim)
        {
            var sb = new StringBuilder();
            var omitted = 0;
            foreach (var item in items)
            {
                // authoritative material is NEVER truncated: a headline reading "the build gate is
                // dev.mjs" when the content says "dev.mjs verify" is worse than having no memory at all.
                var text = verbatim ? item.Content ?? item.Headline : item.Headline;
                var line = $"- {text}\n";
                // `continue`, not `break` — one oversized item must not hide every shorter one behind it,
                // and every skipped authoritative item has to be counted so the omission line is truthful
                if (line.Length > budget) { omitted++; continue; }
                sb.Append(line);
                budget -= line.Length;
            }
            return (sb.ToString(), omitted);
        }
    }
}
