using Lyntai.Inference;
using Lyntai.Memory;

namespace Lyntai.Agents;

/// <summary>Narrows the tool roster BEFORE the model sees it. <see cref="IToolRegistry"/> hands the loop every
/// registered tool on every iteration, and a model supplies no bound of its own — it calls a tool on most
/// requests nothing on the roster serves, and the protocol's wording does not change that
/// (<c>docs/memory-measurements.md</c> §5, <c>affordance-false-call-4b</c>).
///
/// <para><b>Fail-open.</b> A selector is an OPTIMISATION: <see cref="ToolLoop"/> shows the whole roster
/// when this faults or returns nothing, so a broken, slow or empty one costs tokens and never the tool the
/// request needed. Only the caller's own cancellation propagates.</para>
///
/// <para>Unregistered by default, and the loop then shows every tool.</para>
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
        TextRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default);
}

/// <summary>Knobs for <see cref="VectorToolSelector"/>.</summary>
public sealed class ToolSelectorOptions
{
    /// <summary>The most tools to show. <b>Zero or less narrows NOTHING</b> — a misconfiguration must not be
    /// able to blind the loop, and "show no tools" is already sayable by registering none.
    /// <para>Unmeasured as a default: the evidence sizes the selector's accuracy
    /// (<c>docs/memory-measurements.md</c> §5, <c>affordance-roster-catalogue</c>), not where to cut.</para></summary>
    public int Limit { get; set; } = 8;
}

/// <summary>The shipped <see cref="IToolSelector"/>: cosine similarity between the request and each tool's
/// own description, keeping the best <see cref="ToolSelectorOptions.Limit"/>.
///
/// <para><b>Model-free and the cheapest arm measured</b>, ahead of every generative arm at or under its size
/// class (<c>docs/memory-measurements.md</c> §5, <c>affordance-roster-catalogue</c> — an optimistic bound,
/// since that fixture's distractors are semantically distant).</para>
///
/// <para><b>A tool's description is what gets embedded</b>, so a roster whose descriptions do not say what
/// each tool is FOR cannot be narrowed well by this — which is a property of the descriptions rather than
/// of the vector backend, and the one thing a deployment can fix directly.</para></summary>
public sealed class VectorToolSelector(
    IEnumerable<IModelProvider> providers, ToolSelectorOptions? options = null,
    IProviderRouterFactory? routing = null)
    : IToolSelector
{
    private readonly ToolSelectorOptions _options = options ?? new ToolSelectorOptions();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ITool>> SelectAsync(
        TextRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default)
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
            providers, query, EmbeddingRole.Query, routing: routing,
            consumer: ProviderConsumers.Agent, ct: ct).ConfigureAwait(false);
        var described = tools.Select(Describe).ToList();
        var toolVectors = await EmbeddingRouting.EmbedAsync(
            providers, described, EmbeddingRole.Document, routing: routing,
            consumer: ProviderConsumers.Agent, ct: ct).ConfigureAwait(false);

        // Ordered by score, then by ORIGINAL POSITION so a tie is broken the way the registry listed them
        // rather than arbitrarily — two tools with identical descriptions must not reorder run to run.
        return [.. tools
            .Select((tool, index) => (tool, index, score: VectorMath.Cosine(queryVector, toolVectors[index])))
            .OrderByDescending(x => x.score).ThenBy(x => x.index)
            .Take(limit)
            .OrderBy(x => x.index)          // shown in registration order, as an un-narrowed roster is
            .Select(x => x.tool)];
    }

    /// <summary>Name AND description, because a name alone is often the only thing that says what a
    /// sparsely-described tool does.</summary>
    private static string Describe(ITool tool) => $"{tool.Name} {tool.Description}";
}
