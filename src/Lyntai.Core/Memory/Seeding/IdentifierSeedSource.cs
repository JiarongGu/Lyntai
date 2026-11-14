using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Seeding;

/// <summary>Constants for <see cref="IdentifierSeedSource"/>. An init-only record taken by value, like
/// <see cref="SemanticSeedOptions"/> and <see cref="SubjectSeedOptions"/>.</summary>
public sealed record IdentifierSeedOptions
{
    private readonly int _maxIdentifiers = 8;
    private readonly int _probe = 200;

    /// <summary>How many identifier-shaped words of one query this channel looks up; the leading ones. Each costs one store
    /// read, so this bounds a recall's added work.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below one.</exception>
    public int MaxIdentifiers
    {
        get => _maxIdentifiers;
        init => _maxIdentifiers = MemoryOption.Require(value, 1, nameof(IdentifierSeedOptions),
            "a channel that looks up no identifier is one that was not registered.");
    }

    /// <summary>Whether an entry holding the query's identifier LEADS the recall (<see cref="IMemorySeedSource.Pins"/>),
    /// default <c>true</c>. Off, the channel is one more vote in rank fusion, and measured that is not enough: a
    /// long entry that tops the text ranking and cites the identifier once ties the short entry about it.</summary>
    public bool Pin { get; init; } = true;

    /// <summary>How many of the store's matches for ONE identifier are read before the whole-word test, at least the
    /// recall's own candidate window. The store matches by substring, so <c>D12</c> also fetches <c>D120</c> to <c>D129</c>, and
    /// a window as narrow as a recall's could cut the entry that holds it whole before the test sees it. Raise it for a
    /// corpus where a short identifier is a prefix of many.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below one.</exception>
    public int Probe
    {
        get => _probe;
        init => _probe = MemoryOption.Require(value, 1, nameof(IdentifierSeedOptions),
            "a probe of no entries reads nothing for the whole-word test to keep.");
    }
}

/// <summary>
/// The channel that finds an entry by an IDENTIFIER the query names — a row id like <c>ABC1d</c>, a decision number
/// like <c>D123</c> — where the entry holds it WHOLE and in the same case. A text channel cannot: it matches by
/// substring and ranks by how often the query's words recur, so <c>D123</c> brings <c>D1234</c> and a long entry
/// that mentions it ten times above the short entry about it.
///
/// <para><b>Each identifier is looked up alone</b>, through the store's own text search, so the entry is in the pool
/// even when the rest of the query's words would have buried it. Of what comes back only entries holding the
/// identifier whole survive, ranked by where it stands (the headline above the content), how often, and how short the
/// entry is, summed over the query's identifiers: the entry that is ABOUT <c>D123</c> leads the one that cites it. A
/// query with no identifier-shaped word makes no read at all (<c>docs/memory.md</c> §Find an entry by its
/// identifier).</para>
///
/// <para><b>Opt-in</b> (<c>AddMemoryIdentifierSeeds</c>), because it adds a store read per identifier to a recall and
/// changes which entries lead for a query that names one: <b>they lead every other</b>
/// (<see cref="IdentifierSeedOptions.Pin"/>), the rest keeping the ranking policy's order.</para>
/// </summary>
/// <param name="options">Constants; null takes the defaults.</param>
/// <param name="logger">Where a failed store read is reported; null logs nothing.</param>
public sealed class IdentifierSeedSource(
    IdentifierSeedOptions? options = null, ILogger<IdentifierSeedSource>? logger = null) : IMemorySeedSource
{
    private readonly IdentifierSeedOptions _options = options ?? new IdentifierSeedOptions();
    private readonly ILogger _logger = logger ?? NullLogger<IdentifierSeedSource>.Instance;

    /// <inheritdoc />
    public string Name => "identifier";

    /// <inheritdoc />
    public MemorySeedKind Kind => MemorySeedKind.Lexical;

    /// <inheritdoc />
    public bool Pins => _options.Pin;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct)
    {
        var identifiers = MemoryIdentifiers.Extract(request.Query.Query, _options.MaxIdentifiers);
        if (identifiers.Count == 0 || request.Limit <= 0) return [];

        var probe = Math.Max(request.Limit, _options.Probe);
        var held = new Dictionary<long, (GraphNode Node, double Weight)>();
        foreach (var id in identifiers)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<GraphNode> hits;
            try
            {
                hits = await request.Store.SeedAsync(request.Engine, request.Query.TaskKey, request.Query.Scope, id, probe, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // best-effort: the other channels still answer
                _logger.LogWarning(ex, "identifier seed failed for {Engine}/{Task}; this channel contributes nothing",
                    request.Engine, request.Query.TaskKey);
                continue;
            }

            foreach (var node in hits)
            {
                if (node.Matched == false) continue;   // admitted by grade, never by the query
                var weight = Weigh(node, id);
                if (weight <= 0) continue;
                held[node.Id] = held.TryGetValue(node.Id, out var earlier) ? (earlier.Node, earlier.Weight + weight) : (node, weight);
            }
        }

        // two per identifier at most: one for the headline, under one for the content
        var scale = 2.0 * identifiers.Count;
        return [.. held.Values
            .OrderByDescending(h => h.Weight)
            .ThenByDescending(h => h.Node.Id)
            .Take(request.Limit)
            .Select(h => h.Node with { Relevance = h.Weight / scale, Matched = true })];
    }

    /// <summary>1 for the headline holding it, plus under 1 for the content: more as it recurs, less as the entry grows.</summary>
    private static double Weigh(GraphNode node, string id)
    {
        var inHeadline = MemoryIdentifiers.Find(node.Headline, id).Any() ? 1.0 : 0.0;
        var times = MemoryIdentifiers.Find(node.Content, id).Count();
        var inContent = times == 0 ? 0.0 : times / (times + 1.0) * 1000.0 / (1000.0 + node.Content.Length);
        return inHeadline + inContent;
    }
}
