using Lyntai.Lifecycle;
using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>Narrows the tool roster BEFORE the model sees it.
///
/// <para><b>Why the library needs a seam at all.</b> <see cref="IToolRegistry"/> hands the loop every
/// registered tool on every iteration, and the model supplies no bound of its own: measured
/// (<c>docs/memory-measurements.md</c> §5) a 4B invokes a tool on <b>90-95%</b> of requests nothing on the
/// roster serves, and two preamble rewrites in opposite directions moved that by NOTHING. So wording is not
/// the lever, and narrowing the roster is what is left. A deployment with a catalogue previously had no
/// seam, no option and no way to narrow it.</para>
///
/// <para><b>Fail-open, like every other model-backed seam here.</b> A selector is an OPTIMISATION; a
/// broken, slow or empty one must cost tokens and never the tool the request actually needed, so
/// <see cref="ToolLoop"/> falls back to the whole roster when this faults or returns nothing.</para>
///
/// <para>Unregistered by default, and the loop then behaves exactly as it did before this existed.</para>
/// </summary>
public interface IToolSelector
{
    /// <summary>The tools worth showing for <paramref name="request"/>, drawn from
    /// <paramref name="tools"/>.</summary>
    /// <param name="request">The call being made — its messages are what the roster is being narrowed for.</param>
    /// <param name="tools">Every registered tool, in registration order.</param>
    /// <param name="ct">Cancellation. The CALLER's; an implementation's own timeout is a fault, and the loop
    /// treats a fault as "show everything" rather than as a failure.</param>
    /// <returns>A subset, ideally in the order it should be shown. Returning every tool is always valid and
    /// is what an implementation should do when it cannot decide.</returns>
    Task<IReadOnlyList<ITool>> SelectAsync(
        LlmRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default);
}

/// <summary>Knobs for <see cref="VectorToolSelector"/>.</summary>
public sealed class ToolSelectorOptions
{
    /// <summary>The most tools to show. <b>Zero or less narrows NOTHING</b> — a misconfiguration must not be
    /// able to blind the loop, and "show no tools" is already sayable by registering none.
    /// <para>Unmeasured as a default. The evidence sizes the SELECTOR, not this number: a model-free
    /// vector backend picks the right tool from 35 options 81.5% of the time (argmax, so recall at a cut of k is
    /// higher), which says narrowing is feasible and says nothing about where to cut.</para></summary>
    public int Limit { get; set; } = 8;
}

/// <summary>The shipped <see cref="IToolSelector"/>: cosine similarity between the request and each tool's
/// own description, keeping the best <see cref="ToolSelectorOptions.Limit"/>.
///
/// <para><b>Model-free and the cheapest arm measured.</b> A 333,590,944 B vector backend scoring tool
/// descriptions reads 81.5% at 35 options where chance is 3% — ahead of every generative arm at or under
/// that size class and far cheaper (<c>affordance-roster-catalogue</c>). The figure is an OPTIMISTIC bound:
/// past the first handful the fixture's distractors are semantically distant, and a real catalogue is a
/// mix.</para>
///
/// <para><b>A tool's description is what gets embedded</b>, so a roster whose descriptions do not say what
/// each tool is FOR cannot be narrowed well by this — which is a property of the descriptions rather than
/// of the vector backend, and the one thing a deployment can fix directly.</para></summary>
public sealed class VectorToolSelector(
    IEnumerable<IModelProvider> providers, ToolSelectorOptions? options = null)
    : IToolSelector
{
    private readonly ToolSelectorOptions _options = options ?? new ToolSelectorOptions();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ITool>> SelectAsync(
        LlmRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tools);

        var limit = _options.Limit;
        if (limit <= 0 || tools.Count <= limit) return tools;

        // The user's own words, never the system preamble: the preamble is the same on every call and would
        // pull every request toward whichever tools happen to share its vocabulary.
        var query = string.Join("\n", request.Messages
            .Where(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content));
        if (string.IsNullOrWhiteSpace(query)) return tools;

        // Role-aware (D116): the request is the QUERY side and the descriptions are DOCUMENTS. On a
        // symmetric model the default body makes this identical to the role-less call.
        var queryVector = await EmbeddingRouting.EmbedOneAsync(
            providers, query, EmbeddingRole.Query, ct: ct).ConfigureAwait(false);
        var described = tools.Select(Describe).ToList();
        var toolVectors = await EmbeddingRouting.EmbedAsync(
            providers, described, EmbeddingRole.Document, ct: ct).ConfigureAwait(false);

        // Ordered by score, then by ORIGINAL POSITION so a tie is broken the way the registry listed them
        // rather than arbitrarily — two tools with identical descriptions must not reorder run to run.
        return [.. tools
            .Select((tool, index) => (tool, index, score: Cosine(queryVector, toolVectors[index])))
            .OrderByDescending(x => x.score).ThenBy(x => x.index)
            .Take(limit)
            .OrderBy(x => x.index)          // shown in registration order, as an un-narrowed roster is
            .Select(x => x.tool)];
    }

    /// <summary>Name AND description, because a name alone is often the only thing that says what a
    /// sparsely-described tool does.</summary>
    private static string Describe(ITool tool) => $"{tool.Name} {tool.Description}";

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
