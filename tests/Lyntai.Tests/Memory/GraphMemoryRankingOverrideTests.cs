using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>Task 6 of the 2026-08-10 memory-policy-seams plan: a named engine can pick its OWN ranking policy
/// (<c>UseGraph</c>'s <c>ranking</c> parameter — per-ENGINE selection) and expose named alternates a single
/// call can select BY NAME (<see cref="MemoryQuery.RankingPolicyName"/> — per-CALL override), with an unknown
/// name erroring rather than silently falling back to the engine's own default.
/// <para><b>Runs against SQLite, not InMemory, and that is load-bearing.</b>
/// <see cref="Lyntai.Storage.InMemory.InMemoryMemoryGraphStore"/>'s <c>SeedAsync</c> matches a query as a
/// contiguous SUBSTRING of content, so a realistic two-fact corpus recalls nothing there and a test built on
/// it would silently exercise only the write path (`.claude/knowledge/pitfalls.md`) — exactly the trap that
/// cost this project two tasks four days apart. The two fixtures below use trivial, hand-controlled ranking
/// fakes rather than either shipped formula, so the expected order is exact and needs no arithmetic — what is
/// genuinely under test is the ENGINE's resolution logic and the SQLite store's real seed/gather pipeline,
/// not a ranking formula (already covered elsewhere).</para></summary>
public sealed class GraphMemoryRankingOverrideTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    /// <summary>Orders whatever it is given by <see cref="GraphNode.Id"/>, ascending or descending — trivial
    /// and unambiguous, so a fact can tell which policy actually ran without reasoning about a real
    /// formula.</summary>
    private sealed class OrderById(bool ascending) : IMemoryRankingPolicy
    {
        public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
            in MemoryRankingContext context)
        {
            var ordered = ascending
                ? candidates.OrderBy(c => c.Node.Id)
                : candidates.OrderByDescending(c => c.Node.Id);
            return ordered.Select((c, i) => new RankedMemory(c, -(double)i)).ToList();
        }
    }

    private GraphMemoryEngine Engine(IMemoryRankingPolicy? ranking = null,
        IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies = null) =>
        new("e", new SqliteMemoryGraphStore(_db.Factory), ranking: ranking,
            namedRankingPolicies: namedRankingPolicies);

    [Fact]
    public async Task An_engines_own_ranking_policy_is_used_for_every_ordinary_call()
    {
        var engine = Engine(ranking: new OrderById(ascending: true));
        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "gadget alpha note"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "gadget beta note"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "gadget", Limit: 10));

        Assert.Equal([first.Id, second.Id], recalled.Items.Select(i => i.Reference.Id).ToArray());
    }

    [Fact]
    public async Task A_per_call_override_by_name_replaces_the_engines_own_default_for_that_call_only()
    {
        var engine = Engine(ranking: new OrderById(ascending: true),
            namedRankingPolicies: new Dictionary<string, IMemoryRankingPolicy>
            {
                ["descending"] = new OrderById(ascending: false),
            });
        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "widget alpha note"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "widget beta note"));

        var withoutOverride = await engine.RecallAsync(new MemoryQuery("t", "s", "widget", Limit: 10));
        var withOverride = await engine.RecallAsync(
            new MemoryQuery("t", "s", "widget", Limit: 10, RankingPolicyName: "descending"));

        // the engine's own default (ascending) is unaffected by the override having been used once
        Assert.Equal([first.Id, second.Id], withoutOverride.Items.Select(i => i.Reference.Id).ToArray());
        Assert.Equal([second.Id, first.Id], withOverride.Items.Select(i => i.Reference.Id).ToArray());
    }

    [Fact]
    public async Task An_unknown_override_name_throws_rather_than_silently_using_the_default()
    {
        var engine = Engine(namedRankingPolicies: new Dictionary<string, IMemoryRankingPolicy>
        {
            ["descending"] = new OrderById(ascending: false),
        });
        await engine.RememberAsync(new MemoryWrite("t", "s", "console alpha note"));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => engine.RecallAsync(
            new MemoryQuery("t", "s", "console", RankingPolicyName: "nope")));

        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
        Assert.Contains("descending", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_override_name_throws_even_when_the_engine_exposes_no_named_alternates_at_all()
    {
        // no namedRankingPolicies at all — not "falls back to the default", an error, exactly like naming
        // one that genuinely isn't registered.
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "beacon alpha note"));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => engine.RecallAsync(
            new MemoryQuery("t", "s", "beacon", RankingPolicyName: "anything")));

        Assert.Contains("(none)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseGraph_lets_a_named_engine_override_the_containers_ranking_policy()
    {
        // The DI-level half of the same story: UseGraph's `ranking` parameter reaches the built engine even
        // with a DIFFERENT IMemoryRankingPolicy registered container-wide — per-engine selection actually
        // OVERRIDES the container default for this named engine, rather than merely adding to it. Every
        // other named engine (none registered here) and the container registration itself are unaffected —
        // "container registration stays the default for engines that name nothing".
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(_db.Factory));
        services.AddSingleton<IMemoryRankingPolicy>(new OrderById(ascending: false)); // container default
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("special", e => e.UseGraph(ranking: new OrderById(ascending: true))));
        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("special/graph");

        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "socket alpha note"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "socket beta note"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "socket", Limit: 10));

        // ascending — THIS named engine's own explicit `ranking`, not the container's descending default
        Assert.Equal([first.Id, second.Id], recalled.Items.Select(i => i.Reference.Id).ToArray());
    }

    [Fact]
    public async Task UseGraph_with_no_explicit_ranking_still_uses_the_container_default()
    {
        // The mirror image of the fact above: an engine that names nothing here behaves exactly as before
        // this parameter existed — the container registration is still consulted.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(_db.Factory));
        services.AddSingleton<IMemoryRankingPolicy>(new OrderById(ascending: false)); // container default
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("plain", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("plain/graph");

        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "beam alpha note"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "beam beta note"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "beam", Limit: 10));

        Assert.Equal([second.Id, first.Id], recalled.Items.Select(i => i.Reference.Id).ToArray());
    }

    [Fact]
    public async Task UseGraph_wires_named_alternates_through_to_the_engine_for_a_per_call_override()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(_db.Factory));
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("named", e => e.UseGraph(
                ranking: new OrderById(ascending: true),
                namedRankingPolicies: new Dictionary<string, IMemoryRankingPolicy>
                {
                    ["descending"] = new OrderById(ascending: false),
                })));
        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("named/graph");

        var first = await engine.RememberAsync(new MemoryWrite("t", "s", "relay alpha note"));
        var second = await engine.RememberAsync(new MemoryWrite("t", "s", "relay beta note"));

        var withOverride = await engine.RecallAsync(
            new MemoryQuery("t", "s", "relay", Limit: 10, RankingPolicyName: "descending"));

        Assert.Equal([second.Id, first.Id], withOverride.Items.Select(i => i.Reference.Id).ToArray());
    }
}
