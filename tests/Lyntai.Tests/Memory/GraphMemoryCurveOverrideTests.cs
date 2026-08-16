using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Modulation;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary><c>docs/DECISIONS.md</c> D50 (<c>docs/task-archive.md</c> Part 54, DSR5): a named engine can pick its OWN
/// forgetting curve through <c>UseGraph</c>'s <c>policy</c> parameter, so two graph engines in one process
/// can run two curves. Until 3.0 the curve was resolved from the global container alone, making it a
/// per-PROCESS choice while <c>ranking</c> — the subsystem's only other SINGULAR seam (D48) — was already
/// per-engine.
/// <para><b>The load-bearing half is the NULL path</b>, not the override: <c>retrievability: null</c> must resolve
/// exactly as it did before this parameter existed — the container's registration, else
/// <see cref="DsrRetrievability"/> (<c>AddMemoryEngine</c>'s own <c>TryAdd</c>, D49). A consumer passing
/// nothing seeing no behavioural difference is the whole promise of an appended optional parameter, so both
/// null cases are pinned below alongside the two-curve case.</para>
/// <para>Every fact observes WHICH curve ran through
/// <see cref="GraphNode.ProvenanceRetrievability"/> and the stability the write actually stored, read back
/// from a real <see cref="SqliteMemoryGraphStore"/> — not through a recall's ordering, which would make the
/// ranking policy a confound. The two fakes below declare provenance bits at 40 and 41, well inside the
/// consumer range this library reserves (bits 32 and above —
/// <see cref="MemoryRetrievabilityProvenance"/>'s own remarks), so neither can collide with a shipped
/// policy's bit.</para></summary>
public sealed class GraphMemoryCurveOverrideTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    /// <summary>A curve identified by its own <see cref="InitialStability"/> and provenance bit, so a fact
    /// can tell which one actually ran without reasoning about a real formula. Deliberately NOT a
    /// <see cref="DsrRetrievability"/> with different options: the subject is which INSTANCE the wiring
    /// selected, and two configurations of one type could not be told apart by provenance at all.</summary>
    private sealed class MarkedCurve(double initialStability, int provenanceBit) : IMemoryRetrievabilityPolicy
    {
        public double InitialStability => initialStability;

        public MemoryRetrievabilityProvenance Provenance => (MemoryRetrievabilityProvenance)(1L << provenanceBit);

        public double Retrievability(in MemoryDecayState state) =>
            state.Age <= 0 ? 1 : Math.Clamp(Math.Pow(2, -state.Age / Math.Max(1e-9, state.Stability)), 0, 1);

        public MemoryDecayState Reinforce(in MemoryDecayState state) => state;

        public double CandidateCutoff(double minRetrievability) => double.PositiveInfinity;
    }

    private static readonly MemoryRetrievabilityProvenance CurveA = (MemoryRetrievabilityProvenance)(1L << 40);
    private static readonly MemoryRetrievabilityProvenance CurveB = (MemoryRetrievabilityProvenance)(1L << 41);

    private ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(_db.Factory));
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    /// <summary>Writes one entry through the named engine and reads the row back, so the assertions are made
    /// against what STORAGE holds rather than against anything the engine reported.</summary>
    private async Task<GraphNode> WriteAndRead(IServiceProvider sp, string engineName, string content)
    {
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get($"{engineName}/graph");
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var store = sp.GetRequiredService<IMemoryGraphStore>();
        var node = await store.GetAsync($"{engineName}/graph",
            long.Parse(reference.Id, System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(node);
        return node!;
    }

    [Fact]
    public async Task Two_engines_in_one_process_can_run_two_different_curves()
    {
        // THE feature. Before D50 both engines resolved the same IMemoryRetrievabilityPolicy from the
        // container and this was inexpressible.
        using var sp = Build(b => b
            .AddMemoryEngine("alpha", e => e.UseGraph(retrievability: new MarkedCurve(11, 40)))
            .AddMemoryEngine("beta", e => e.UseGraph(retrievability: new MarkedCurve(77, 41))));

        var alpha = await WriteAndRead(sp, "alpha", "gadget alpha note");
        var beta = await WriteAndRead(sp, "beta", "gadget beta note");

        Assert.Equal((long)CurveA, alpha.ProvenanceRetrievability);
        Assert.Equal((long)CurveB, beta.ProvenanceRetrievability);
        // and the curves were genuinely in force, not merely recorded: each engine seeded its entry from its
        // OWN InitialStability
        Assert.Equal(11, alpha.Stability, precision: 6);
        Assert.Equal(77, beta.Stability, precision: 6);
    }

    [Fact]
    public async Task An_explicit_curve_overrides_the_containers_registration_for_that_engine_alone()
    {
        // Per-engine selection OVERRIDES the container default rather than merely adding to it — and the
        // engine that names nothing still gets the container's registration, unaffected by its neighbour's
        // choice. "Container registration stays the default for engines that name nothing."
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(_db.Factory));
        services.AddSingleton<IMemoryRetrievabilityPolicy>(new MarkedCurve(63, 41)); // container default
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("named", e => e.UseGraph(retrievability: new MarkedCurve(11, 40)))
            .AddMemoryEngine("plain", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        var named = await WriteAndRead(sp, "named", "socket alpha note");
        var plain = await WriteAndRead(sp, "plain", "socket beta note");

        Assert.Equal((long)CurveA, named.ProvenanceRetrievability);
        Assert.Equal(11, named.Stability, precision: 6);
        Assert.Equal((long)CurveB, plain.ProvenanceRetrievability);
        Assert.Equal(63, plain.Stability, precision: 6);
    }

    [Fact]
    public async Task A_null_curve_still_resolves_the_containers_registration()
    {
        // HALF ONE of the non-negotiable: with a curve registered container-wide, an engine that names
        // nothing must get THAT one — exactly as before the parameter existed.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(_db.Factory));
        services.AddSingleton<IMemoryRetrievabilityPolicy>(new MarkedCurve(63, 41));
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("plain", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        var node = await WriteAndRead(sp, "plain", "beam alpha note");

        Assert.Equal((long)CurveB, node.ProvenanceRetrievability);
        Assert.Equal(63, node.Stability, precision: 6);
    }

    [Fact]
    public async Task A_null_curve_with_nothing_registered_still_resolves_the_shipped_default()
    {
        // HALF TWO: nothing registered at all falls through to AddMemoryEngine's own TryAdd of
        // DsrRetrievability (docs/DECISIONS.md D49) — the zero-configuration path, which is what most
        // consumers are actually on.
        using var sp = Build(b => b.AddMemoryEngine("plain", e => e.UseGraph()));

        var node = await WriteAndRead(sp, "plain", "relay alpha note");

        Assert.Equal((long)MemoryRetrievabilityProvenance.Dsr, node.ProvenanceRetrievability);
        Assert.Equal(new DsrOptions().InitialStability, node.Stability, precision: 6);
    }

    /// <summary>Lengthens every entry's half-life by a fixed factor, so retention modulation is either
    /// visibly in force or visibly absent.</summary>
    private sealed class FixedRetentionPolicy(double factor) : IMemoryRetentionPolicy
    {
        public string Name => "fixed";
        public double MaxStabilityFactor => factor;
        public double StabilityFactor(in MemoryDecayState state) => factor;
    }

    [Fact]
    public async Task A_selected_curve_is_still_wrapped_in_retention_modulation()
    {
        // Selecting a curve chooses the CURVE and nothing else: the resolved policy is still wrapped in
        // ModulatedRetrievability over the registered IMemoryRetentionPolicy collection, so a consumer who
        // names a curve does not silently opt out of the retention policies every other graph engine gets.
        // Handing the argument to GraphMemoryEngine's own `policy:` slot instead of the wrapper's INNER slot
        // is the mistake this pins — it would compile, satisfy every fact above, and drop modulation for
        // exactly the engines that named a curve.
        //
        // Observed through MemoryItem.Retrievability, which is what modulation actually moves, rather than
        // through provenance (ModulatedRetrievability reports the inner curve's bit verbatim, by design, so
        // provenance cannot distinguish wrapped from unwrapped) or through reflection over a private field
        // (a rename site no build can see — `.claude/knowledge/pitfalls.md`).
        var withModulation = await RecalledRetrievability(retention: 8);
        var without = await RecalledRetrievability(retention: null);

        Assert.True(withModulation > without + 1e-9,
            $"a curve selected through UseGraph's `policy` argument lost retention modulation: " +
            $"retrievability was {withModulation} with a x8 retention policy registered and {without} " +
            "without — identical means the selected curve reached the engine unwrapped.");
    }

    /// <summary>Writes one entry through an engine whose curve was SELECTED (never resolved from the
    /// container), ages it with further writes, and reports the retrievability the recall itself carries.
    /// Its own database, so the two arms cannot interfere through the store's shared position
    /// accumulator.</summary>
    private static async Task<double> RecalledRetrievability(double? retention)
    {
        using var db = new TempDb();
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(db.Factory));
        if (retention is { } factor)
            services.AddSingleton<IMemoryRetentionPolicy>(new FixedRetentionPolicy(factor));
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("named", e => e.UseGraph(retrievability: new MarkedCurve(11, 40))));
        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("named/graph");

        await engine.RememberAsync(new MemoryWrite("t", "s", "console alpha note"));
        // age it: only a WRITE advances the engine's position, so without these the entry sits at age 0 and
        // BOTH arms report exactly 1 — a fact that could never fail
        for (var i = 0; i < 12; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated filler entry number {i}"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "console", Limit: 10));
        var item = Assert.Single(recalled.Items,
            i => i.Headline.Contains("console", StringComparison.Ordinal));

        // a guard that cannot observe the thing it guards is worse than none: at retrievability 1 the two
        // arms are equal no matter what the wiring did
        Assert.True(item.Retrievability < 1,
            $"the entry never aged (retrievability {item.Retrievability}), so comparing the two arms would " +
            "prove nothing — the filler writes did not advance the engine's position.");
        return item.Retrievability;
    }
}
