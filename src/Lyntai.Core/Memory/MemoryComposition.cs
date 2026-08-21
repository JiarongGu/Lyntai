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
    /// this reservation exists to prevent.</para>
    /// <para><b>It protects exact material from the BUDGET, never from your RETRIEVAL.</b> Only what the
    /// recall returned can be reserved, so a fact your selection already dropped is not rescued here — and
    /// the section still looks full. If your own retrieval ranks authoritative material against ordinary
    /// hits, offer the exact material to this renderer IN FULL and let the reserve do the bounding.</para>
    /// <para><b>CHARACTERS, and named for it.</b> Through 2.5.x this was <c>AuthoritativeReserve</c> — the
    /// same identifier as <see cref="GraphMemoryOptions.AuthoritativeReserve"/>, in the same namespace, in
    /// different UNITS (that one reserves recall SLOTS) and with different null conventions, both reachable
    /// from one <see cref="MemoryEngineBuilder"/> chain. A consumer reading "reserve 2" as slots got two
    /// characters, which truncates every authoritative fact to nothing.</para></summary>
    public int AuthoritativeCharacters { get; init; } = 1000;

    /// <summary>Heading for exact material.</summary>
    public string AuthoritativeHeading { get; init; } = "## Known facts (authoritative)";

    /// <summary>Heading for recalled material.</summary>
    public string AssociativeHeading { get; init; } =
        "## Recalled context (associative — may be stale or partial)";
}

/// <summary>Renders an engine's recall into a prompt. Composing is something an engine DOES, so there is no
/// separate composer type — "several composers" is already "several engines".
/// <para>Two doors: <see cref="ComposeAsync"/> recalls and renders, <see cref="Render"/> renders material a
/// caller already has.</para></summary>
public static class MemoryComposition
{
    /// <summary>Append this engine's relevant material to <paramref name="basePrompt"/> as two labelled
    /// sections, authoritative first — so the model is told which material is exact rather than left to
    /// infer it.
    /// <para>Never throws except on cancellation: a faulting engine yields the base prompt unchanged.</para>
    /// <para>Recall your own way instead? <see cref="Render"/> is this method's second half with the engine
    /// taken out.</para></summary>
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

        return Render(basePrompt, recall.Items, opts);
    }

    /// <summary>Append <paramref name="items"/> to <paramref name="basePrompt"/> as two labelled sections,
    /// authoritative first — the half of <see cref="ComposeAsync"/> that does not touch an engine.
    /// <para>Exposed because a consumer with its OWN retrieval (a fused semantic + keyword selection, say)
    /// wants this rendering and not a recall: reaching it through <see cref="ComposeAsync"/> forced an
    /// <see cref="IMemoryEngine"/> whose <c>RecallAsync</c> returned material the caller had already chosen —
    /// an engine written only to reach a formatter. The seam was always here; this is where it is cut.</para>
    /// <para>Grades are read off the items, so a caller supplying its own must set
    /// <see cref="MemoryItem.Grade"/>: everything not <see cref="MemoryGrade.Authoritative"/> renders as
    /// associative and MAY be truncated to its headline. Authoritative content is never truncated.</para>
    /// <para>Pure and total: it performs no I/O, throws only on a null argument, and returns
    /// <paramref name="basePrompt"/> unchanged when nothing fits.</para></summary>
    /// <param name="basePrompt">The prompt to append to.</param>
    /// <param name="items">What to render, in the order they should appear within their section.</param>
    /// <param name="options">Budget and headings; null takes the defaults.</param>
    public static string Render(string basePrompt, IReadOnlyList<MemoryItem> items,
        MemoryCompositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var opts = options ?? new MemoryCompositionOptions();
        if (items.Count == 0) return basePrompt;

        var exact = items.Where(i => i.Grade == MemoryGrade.Authoritative).ToList();
        var recalled = items.Where(i => i.Grade != MemoryGrade.Authoritative).ToList();

        // authoritative out of its own reserve FIRST, then associative from whatever of the total is left
        var (exactText, omitted) = Fill(exact, Math.Min(opts.AuthoritativeCharacters, opts.Budget), verbatim: true);
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
