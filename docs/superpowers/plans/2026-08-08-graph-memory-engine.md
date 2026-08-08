# Graph memory engine, part A (MEM2a) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** A memory engine whose entries decay, connect, and open with a cheap index — working end to end on
the InMemory backend, so the design is proven before it is written three times in SQL.

**Architecture:** A decay curve behind a seam (`IRetrievabilityPolicy`) with a registered exponential
default, a storage contract (`IMemoryGraphStore`) that never evaluates that curve, and a
`GraphMemoryEngine` that seeds → spreads → scores → budgets → touches. The engine plugs into the MEM1 seam
as one more `Use*` on `MemoryEngineBuilder`, so nothing about composition, grading or the factory changes.

**Tech Stack:** .NET 10, xUnit, Microsoft.Extensions.DependencyInjection / .Logging.Abstractions.

**Spec:** `docs/superpowers/specs/2026-08-08-graph-memory-engine-design.md`. **Task:** `TASKS.md` Part 46 /
MEM2. **Depends on:** MEM1 (shipped 2026-08-08, `docs/task-archive.md` Part 46).

## Scope: this is part A of three

| Part | Contents | Ships on its own? |
|---|---|---|
| **MEM2a — this plan** | `IRetrievabilityPolicy` + `HalfLifeRetrievability`, `IMemoryGraphStore`, `GraphMemoryEngine`, the InMemory backend, `UseGraph()` | **Yes** — a working graph memory over `UseInMemoryStorage()` |
| MEM2b | SQLite migration + store, Postgres store, `MemoryGraphStoreContract` across all three | Yes |
| MEM2c | Agent tools (`{engine}_recall` / `{engine}_expand`), similarity enrichment, MEM-TUNE simulation | Yes |

Do not start MEM2b or MEM2c from this plan. Each gets its own, written after the previous one lands — the
SQL shape should be settled by a working reference implementation, not guessed alongside it.

## Global Constraints

- **No new package.** Core types in `src/Lyntai.Core/Memory/`, the InMemory store in
  `src/Lyntai.Storage.InMemory/`. Do not touch `devtools/project.config.mjs`.
- **No third-party dependency may be added to `Lyntai.Core`.** DI + Logging abstractions only.
- **Purely additive.** Nothing released changes signature or semantics. MEM1's types are extended only by
  new members with defaults.
- **XML docs on every public member** — `check-warnings` fails the build on an unresolved `cref` and is part
  of `verify`.
- **Never name a type `*Dto`.** Use `*Options` / `*Request` / `*Result` / `*Entry` / `*Record` / `*Row`.
- **Time is injected, never `DateTimeOffset.UtcNow` inside the store.** The engine owns the clock and passes
  `now` to the store on every call. A decay model tested against the wall clock cannot be tested at all.
- **Running tests:** `node devtools/dev.mjs test --filter "FullyQualifiedName~Graph"`. A bare `~Name` is not
  valid VSTest syntax; and `dev.mjs test -- --filter X` (with the `--`) silently runs the whole suite.
  **Always read the matched/total count** — a filter matching zero tests passes vacuously.
- **Commit per task**; ask before committing unless the user has given standing approval.
- Branch is `feat/memory-engine-seam` (MEM1's branch, already checked out).

## File Structure

| File | Responsibility |
|---|---|
| `src/Lyntai.Core/Memory/IRetrievabilityPolicy.cs` | the decay seam, `MemoryDecayState`, `HalfLifeOptions`, `HalfLifeRetrievability` |
| `src/Lyntai.Core/Memory/IMemoryGraphStore.cs` | the storage contract, `GraphNode`, `GraphNodeWrite`, `GraphTouch` |
| `src/Lyntai.Core/Memory/MemoryHeadline.cs` | headline derivation (associative only) |
| `src/Lyntai.Core/Memory/GraphMemoryOptions.cs` | `Hops`, `MinRetrievability`, `HeadlineChars`, `CoActivationCap`, `CandidateMultiplier`, `Decay` |
| `src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs` | seed → spread → score → budget → touch; expand; link; prune |
| `src/Lyntai.Core/Memory/MemoryEngineBuilder.cs` (modify) | add `UseGraph(...)` |
| `src/Lyntai.Storage.InMemory/InMemoryMemoryGraphStore.cs` | the reference backend |
| `src/Lyntai.Storage.InMemory/…ServiceCollectionExtensions.cs` (modify) | register it under `UseInMemoryStorage` |
| `tests/Lyntai.Tests/Memory/RetrievabilityPolicyContract.cs` | facts every policy satisfies |
| `tests/Lyntai.Tests/Memory/HalfLifeRetrievabilityTests.cs` | the default curve, including the stability ceiling |
| `tests/Lyntai.Tests/Memory/GraphMemoryEngineTests.cs` | recall, expand, reinforcement, progressive disclosure |
| `tests/Lyntai.Tests/Memory/GraphMemoryWiringTests.cs` | `UseGraph` through DI, blended with a curated member |

---

### Task 1: The decay curve as a seam

**Files:**
- Create: `src/Lyntai.Core/Memory/IRetrievabilityPolicy.cs`
- Test: `tests/Lyntai.Tests/Memory/RetrievabilityPolicyContract.cs`
- Test: `tests/Lyntai.Tests/Memory/HalfLifeRetrievabilityTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `MemoryDecayState(DateTimeOffset CreatedAt, DateTimeOffset LastRecalledAt, int RecallCount,
  double Stability)`; `IRetrievabilityPolicy` with `Retrievability(in MemoryDecayState, DateTimeOffset)`,
  `Reinforce(in MemoryDecayState, DateTimeOffset)`, `InitialStability` (days), `CandidateCutoff(double)`;
  `HalfLifeOptions { TimeSpan InitialStability, double ReinforceFactor, TimeSpan MaxStability }`;
  `HalfLifeRetrievability(HalfLifeOptions?)`.

- [ ] **Step 1: Write the failing contract**

`tests/Lyntai.Tests/Memory/RetrievabilityPolicyContract.cs`:

```csharp
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Facts every <see cref="IRetrievabilityPolicy"/> satisfies, run against the default curve and
/// against a deliberately awkward one, so a custom policy cannot quietly break the store's candidate
/// query.</summary>
public static class RetrievabilityPolicyContract
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Retrievability_is_a_probability(IRetrievabilityPolicy policy)
    {
        foreach (var days in new[] { 0, 1, 7, 30, 365, 3650 })
        {
            var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);
            var r = policy.Retrievability(state, T0.AddDays(days));
            Assert.InRange(r, 0, 1);
        }
    }

    public static void It_is_one_at_zero_elapsed_time(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);
        Assert.Equal(1.0, policy.Retrievability(state, T0), precision: 9);
    }

    public static void It_never_increases_with_age(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);
        var previous = 1.0;
        for (var days = 0; days <= 400; days += 5)
        {
            var r = policy.Retrievability(state, T0.AddDays(days));
            Assert.True(r <= previous + 1e-12, $"retrievability rose at day {days}: {r} > {previous}");
            previous = r;
        }
    }

    public static void Reinforcement_never_shortens_a_memory(IRetrievabilityPolicy policy)
    {
        var state = new MemoryDecayState(T0, T0, 0, policy.InitialStability);
        Assert.True(policy.Reinforce(state, T0.AddDays(1)) >= state.Stability);
    }

    /// <summary>THE load-bearing one. The store filters candidates with a plain
    /// <c>age_days / stability &lt;= cutoff</c> comparison and never evaluates the curve, so a policy whose
    /// cutoff excluded a node it would have kept would silently lose memories.</summary>
    public static void CandidateCutoff_is_a_conservative_superset(IRetrievabilityPolicy policy)
    {
        const double minR = 0.05;
        var cutoff = policy.CandidateCutoff(minR);

        foreach (var stability in new[] { 0.5, 1, 7, 30, 365, 3650.0 })
        foreach (var days in new[] { 0, 1, 3, 7, 14, 30, 90, 365, 1000, 5000.0 })
        {
            var state = new MemoryDecayState(T0, T0, 0, stability);
            var r = policy.Retrievability(state, T0.AddDays(days));
            if (r < minR) continue;                       // may be excluded; we only care about keepers
            Assert.True(days / stability <= cutoff,
                $"a node with r={r:F4} (age {days}d, stability {stability}) falls outside cutoff {cutoff}");
        }
    }

    public static void An_unbounded_policy_is_still_correct(IRetrievabilityPolicy policy)
    {
        // returning infinity is the documented escape hatch: correct, at the cost of a full in-scope scan
        Assert.True(policy.CandidateCutoff(0) > 0);
    }
}
```

`tests/Lyntai.Tests/Memory/HalfLifeRetrievabilityTests.cs`:

```csharp
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class HalfLifeRetrievabilityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static IRetrievabilityPolicy Default() => new HalfLifeRetrievability();

    [Fact] public void Probability() => RetrievabilityPolicyContract.Retrievability_is_a_probability(Default());
    [Fact] public void One_at_zero() => RetrievabilityPolicyContract.It_is_one_at_zero_elapsed_time(Default());
    [Fact] public void Monotone() => RetrievabilityPolicyContract.It_never_increases_with_age(Default());
    [Fact] public void Reinforce_grows() => RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory(Default());
    [Fact] public void Cutoff_superset() => RetrievabilityPolicyContract.CandidateCutoff_is_a_conservative_superset(Default());
    [Fact] public void Unbounded_ok() => RetrievabilityPolicyContract.An_unbounded_policy_is_still_correct(Default());

    [Fact]
    public void One_half_life_halves_retrievability()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { InitialStability = TimeSpan.FromDays(7) });
        var state = new MemoryDecayState(T0, T0, 0, 7);

        Assert.Equal(0.5, policy.Retrievability(state, T0.AddDays(7)), precision: 6);
        Assert.Equal(0.25, policy.Retrievability(state, T0.AddDays(14)), precision: 6);
    }

    [Fact]
    public void Stability_stops_growing_at_the_ceiling()
    {
        // Unbounded `stability *= 1 + Reinforce` compounds: at the default factor roughly twenty recalls
        // turn a 7-day half-life into 64 YEARS, so a hot ASSOCIATIVE node would silently acquire
        // authoritative durability without any of its guarantees. The ceiling is what stops that.
        var policy = new HalfLifeRetrievability(new HalfLifeOptions
        {
            InitialStability = TimeSpan.FromDays(7),
            ReinforceFactor = 0.5,
            MaxStability = TimeSpan.FromDays(365),
        });

        var stability = 7.0;
        for (var i = 0; i < 100; i++)
            stability = policy.Reinforce(new MemoryDecayState(T0, T0, i, stability), T0.AddDays(i));

        Assert.Equal(365, stability, precision: 6);
    }

    [Fact]
    public void A_recall_makes_the_next_forgetting_slower()
    {
        var policy = new HalfLifeRetrievability(new HalfLifeOptions { ReinforceFactor = 0.5 });
        var before = new MemoryDecayState(T0, T0, 0, 7);
        var after = before with { Stability = policy.Reinforce(before, T0), LastRecalledAt = T0 };

        var at30 = T0.AddDays(30);
        Assert.True(policy.Retrievability(after, at30) > policy.Retrievability(before, at30));
    }

    [Fact]
    public void The_cutoff_is_the_exact_inverse_of_the_curve()
    {
        var policy = new HalfLifeRetrievability();
        // r = 2^(-age/stability) = minR  <=>  age/stability = -log2(minR)
        Assert.Equal(Math.Log2(1 / 0.05), policy.CandidateCutoff(0.05), precision: 9);
    }

    [Fact]
    public void A_zero_or_negative_minimum_means_no_bound()
    {
        Assert.True(double.IsPositiveInfinity(new HalfLifeRetrievability().CandidateCutoff(0)));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~HalfLifeRetrievability"`
Expected: compile failure — none of these types exist.

- [ ] **Step 3: Write the policy**

`src/Lyntai.Core/Memory/IRetrievabilityPolicy.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>What a decay curve needs to know about one entry. Carries no content, so a policy can be pure
/// arithmetic and trivially testable.</summary>
/// <param name="CreatedAt">When the entry was first stored.</param>
/// <param name="LastRecalledAt">When it was last successfully recalled; equals <paramref name="CreatedAt"/>
/// until it has been.</param>
/// <param name="RecallCount">How many times it has been recalled.</param>
/// <param name="Stability">Its half-life in DAYS — the quantity reinforcement grows.</param>
public readonly record struct MemoryDecayState(
    DateTimeOffset CreatedAt,
    DateTimeOffset LastRecalledAt,
    int RecallCount,
    double Stability);

/// <summary>
/// The model of forgetting. Swappable, and the default is registered for you — nothing has to be
/// implemented to use graph memory.
/// <para>Exposing the constants as loose numbers would settle the VALUES while freezing the FORMULA, so an
/// application can tune <see cref="HalfLifeOptions"/> or replace the curve entirely, and neither choice
/// forecloses the other.</para>
/// </summary>
public interface IRetrievabilityPolicy
{
    /// <summary>Retrievability in [0,1] for a node's state at <paramref name="now"/>. Must be 1 at zero
    /// elapsed time and must never increase with age.</summary>
    double Retrievability(in MemoryDecayState state, DateTimeOffset now);

    /// <summary>The node's new <see cref="MemoryDecayState.Stability"/> after a successful recall. Must
    /// never be smaller than the current one.</summary>
    double Reinforce(in MemoryDecayState state, DateTimeOffset now);

    /// <summary>Stability, in days, for a brand-new node.</summary>
    double InitialStability { get; }

    /// <summary>
    /// A CONSERVATIVE bound on <c>age_days / stability</c> for a given minimum retrievability: no node whose
    /// true retrievability is at least <paramref name="minRetrievability"/> may exceed it.
    /// <para>This is what lets the store bound its candidate set with plain division and never evaluate the
    /// curve — which matters twice over: SQLite has <c>pow</c> only when built with
    /// <c>SQLITE_ENABLE_MATH_FUNCTIONS</c>, and no fixed SQL expression could encode a policy the
    /// application supplies. A policy that cannot bound its curve returns
    /// <see cref="double.PositiveInfinity"/> — correct, at the cost of an in-scope scan.</para>
    /// </summary>
    double CandidateCutoff(double minRetrievability);
}

/// <summary>Constants of the default exponential curve.</summary>
public sealed record HalfLifeOptions
{
    /// <summary>Half-life of a brand-new entry. <b>Unmeasured</b> — see MEM-TUNE.</summary>
    public TimeSpan InitialStability { get; init; } = TimeSpan.FromDays(7);

    /// <summary>How much a successful recall multiplies the half-life by, as <c>1 + factor</c>.
    /// <b>Unmeasured</b> — see MEM-TUNE.</summary>
    public double ReinforceFactor { get; init; } = 0.5;

    /// <summary>The ceiling reinforcement cannot grow past.
    /// <para><b>Not a rounding-out knob — it closes a real defect.</b> Unbounded compounding turns a
    /// seven-day half-life into sixty-four years in about twenty recalls at the default factor, so a
    /// frequently-recalled ASSOCIATIVE entry would become permanently retrievable while still labelled
    /// associative — silently acquiring authoritative durability with none of its guarantees.</para>
    /// <b>Unmeasured</b> — see MEM-TUNE.</summary>
    public TimeSpan MaxStability { get; init; } = TimeSpan.FromDays(365);
}

/// <summary>The default curve: <c>r = 2 ^ (-age_since_recall / stability)</c>, with the half-life growing on
/// each successful recall up to <see cref="HalfLifeOptions.MaxStability"/>. Registered automatically.</summary>
/// <param name="options">Constants; null takes the defaults.</param>
public sealed class HalfLifeRetrievability(HalfLifeOptions? options = null) : IRetrievabilityPolicy
{
    private readonly HalfLifeOptions _options = options ?? new HalfLifeOptions();

    /// <inheritdoc />
    public double InitialStability => _options.InitialStability.TotalDays;

    /// <inheritdoc />
    public double Retrievability(in MemoryDecayState state, DateTimeOffset now)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        var ageDays = (now - state.LastRecalledAt).TotalDays;
        if (ageDays <= 0) return 1;
        return Math.Clamp(Math.Pow(2, -ageDays / stability), 0, 1);
    }

    /// <inheritdoc />
    public double Reinforce(in MemoryDecayState state, DateTimeOffset now)
    {
        var stability = state.Stability > 0 ? state.Stability : InitialStability;
        return Math.Min(stability * (1 + _options.ReinforceFactor), _options.MaxStability.TotalDays);
    }

    /// <inheritdoc />
    public double CandidateCutoff(double minRetrievability) =>
        minRetrievability <= 0 || minRetrievability > 1
            ? double.PositiveInfinity
            : Math.Log2(1 / minRetrievability);
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~HalfLifeRetrievability"`
Expected: PASS, 11 matched.

- [ ] **Step 5: Build clean**

Run: `node devtools/dev.mjs check-warnings`
Expected: no warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): add the retrievability policy seam and its half-life default"
```

---

### Task 2: The graph store contract and the InMemory backend

**Files:**
- Create: `src/Lyntai.Core/Memory/IMemoryGraphStore.cs`
- Create: `src/Lyntai.Storage.InMemory/InMemoryMemoryGraphStore.cs`
- Modify: `src/Lyntai.Storage.InMemory/`'s `UseInMemoryStorage` registration
- Test: `tests/Lyntai.Tests/Memory/MemoryGraphStoreContract.cs`
- Test: `tests/Lyntai.Tests/Memory/InMemoryMemoryGraphStoreTests.cs`

**Interfaces:**
- Consumes: `MemoryGrade` (MEM1), `MemoryDecayState` (Task 1).
- Produces: `GraphNode`, `GraphNodeWrite`, `GraphTouch`, `IMemoryGraphStore` with `UpsertAsync`,
  `SeedAsync`, `NeighboursAsync`, `GetAsync`, `TouchAsync`, `LinkAsync`, `PruneAsync`, `ForgetAsync` — exact
  signatures in Step 3.

- [ ] **Step 1: Write the failing contract**

`tests/Lyntai.Tests/Memory/MemoryGraphStoreContract.cs`:

```csharp
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Backend-agnostic <see cref="IMemoryGraphStore"/> facts. MEM2b runs these against SQLite and
/// Postgres unchanged; per `storage.md` the contract IS the deduplication mechanism for the relational
/// pair, not a shared base class.
/// <para>DELIBERATELY OMITTED, because the backends diverge by design exactly as
/// <c>IMemoryStore.RecallAsync</c> already documents: same-match ORDERING (SQLite ranks by bm25, the others
/// by recency) and MULTI-TOKEN matching. The portable guarantee asserted here is the single-token
/// one.</para></summary>
public static class MemoryGraphStoreContract
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static GraphNodeWrite Write(string engine, string key, string content,
        MemoryGrade grade = MemoryGrade.Associative) =>
        new(engine, key, "s", content, content, grade, 7, null);

    public static async Task Upsert_then_seed_by_single_token_substring(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the deploy pipeline requires manual approval"), T0);
        await store.UpsertAsync(Write("e", key, "rollbacks must page the on-call"), T0);

        var hits = await store.SeedAsync("e", key, "s", "pipeline", null, 10, T0);

        Assert.Single(hits);
        Assert.Contains("manual approval", hits[0].Content, StringComparison.Ordinal);
    }

    public static async Task Upserting_identical_content_refreshes_rather_than_duplicating(
        IMemoryGraphStore store, string key)
    {
        var first = await store.UpsertAsync(Write("e", key, "one fact"), T0);
        var second = await store.UpsertAsync(Write("e", key, "one fact"), T0);

        Assert.Equal(first, second);
        Assert.Single(await store.SeedAsync("e", key, "s", null, null, 10, T0));
    }

    public static async Task Engines_are_isolated_from_one_another(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("engine-a", key, "belongs to a"), T0);
        await store.UpsertAsync(Write("engine-b", key, "belongs to b"), T0);

        var hits = await store.SeedAsync("engine-a", key, "s", null, null, 10, T0);

        Assert.Single(hits);
        Assert.Equal("belongs to a", hits[0].Content);
    }

    public static async Task The_candidate_cutoff_excludes_stale_associative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "stale associative note"), T0);

        // 100 days at stability 7 is ~14 half-lives; a cutoff of 4.32 (minR .05) must exclude it
        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10, T0.AddDays(100));

        Assert.Empty(hits);
    }

    public static async Task The_candidate_cutoff_never_excludes_authoritative_nodes(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "an exact fact", MemoryGrade.Authoritative), T0);

        var hits = await store.SeedAsync("e", key, "s", null, 4.32, 10, T0.AddDays(10_000));

        Assert.Single(hits);
    }

    public static async Task Touch_records_reinforcement(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "reinforced"), T0);
        var at = T0.AddDays(3);

        await store.TouchAsync([new GraphTouch(id, at, 10.5)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(at, node!.LastRecalledAt);
        Assert.Equal(10.5, node.Stability, precision: 6);
        Assert.Equal(1, node.RecallCount);
    }

    public static async Task Linked_nodes_are_reachable_as_neighbours(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"), T0);
        var b = await store.UpsertAsync(Write("e", key, "beta"), T0);
        await store.LinkAsync(a, b, null, 1, symmetric: true, T0);

        var neighbours = await store.NeighboursAsync("e", [a], 10, T0);

        Assert.Single(neighbours);
        Assert.Equal(b, neighbours[0].Id);
    }

    public static async Task Linking_the_same_pair_again_strengthens_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"), T0);
        var b = await store.UpsertAsync(Write("e", key, "beta"), T0);
        await store.LinkAsync(a, b, null, 1, symmetric: false, T0);
        await store.LinkAsync(a, b, null, 1, symmetric: false, T0);

        var neighbours = await store.NeighboursAsync("e", [a], 10, T0);

        Assert.Single(neighbours);       // one edge, not two
        Assert.True(neighbours[0].Degree >= 1);
    }

    public static async Task Degree_counts_connections(IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"), T0);
        foreach (var spoke in new[] { "one", "two", "three" })
        {
            var id = await store.UpsertAsync(Write("e", key, spoke), T0);
            await store.LinkAsync(hub, id, null, 1, symmetric: true, T0);
        }

        var node = await store.GetAsync("e", hub);

        Assert.Equal(3, node!.Degree);
    }

    public static async Task Prune_removes_only_what_it_is_told_to(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "faded note"), T0);
        await store.UpsertAsync(Write("e", key, "exact note", MemoryGrade.Authoritative), T0);

        var removed = await store.PruneAsync("e", key, "s", 4.32, null, T0.AddDays(100));

        Assert.Equal(1, removed);        // the authoritative one is never eligible
        Assert.Single(await store.SeedAsync("e", key, "s", null, null, 10, T0.AddDays(100)));
    }

    public static async Task Forget_clears_a_scope(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "gone"), T0);

        await store.ForgetAsync("e", key, "s");

        Assert.Empty(await store.SeedAsync("e", key, "s", null, null, 10, T0));
    }

    public static async Task Deleting_a_node_takes_its_edges_with_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"), T0);
        var b = await store.UpsertAsync(Write("e", key, "beta"), T0);
        await store.LinkAsync(a, b, null, 1, symmetric: true, T0);

        await store.ForgetAsync("e", key, "s");
        await store.UpsertAsync(Write("e", key, "alpha"), T0);   // same content, new row

        var reborn = await store.SeedAsync("e", key, "s", "alpha", null, 10, T0);
        Assert.Single(reborn);
        Assert.Equal(0, reborn[0].Degree);                        // no dangling edge survived
    }
}
```

`tests/Lyntai.Tests/Memory/InMemoryMemoryGraphStoreTests.cs`:

```csharp
using Lyntai.Memory;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the InMemory backend. MEM2b adds the
/// SQLite and Postgres classes deriving from the same facts.</summary>
public class InMemoryMemoryGraphStoreTests
{
    private static IMemoryGraphStore New() => new InMemoryMemoryGraphStore();

    [Fact] public Task Seed() => MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(New(), "k1");
    [Fact] public Task Dedup() => MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(New(), "k2");
    [Fact] public Task Engine_isolation() => MemoryGraphStoreContract.Engines_are_isolated_from_one_another(New(), "k3");
    [Fact] public Task Cutoff_excludes() => MemoryGraphStoreContract.The_candidate_cutoff_excludes_stale_associative_nodes(New(), "k4");
    [Fact] public Task Cutoff_spares_exact() => MemoryGraphStoreContract.The_candidate_cutoff_never_excludes_authoritative_nodes(New(), "k5");
    [Fact] public Task Touch() => MemoryGraphStoreContract.Touch_records_reinforcement(New(), "k6");
    [Fact] public Task Neighbours() => MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(New(), "k7");
    [Fact] public Task Relink() => MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(New(), "k8");
    [Fact] public Task Degree() => MemoryGraphStoreContract.Degree_counts_connections(New(), "k9");
    [Fact] public Task Prune() => MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(New(), "k10");
    [Fact] public Task Forget() => MemoryGraphStoreContract.Forget_clears_a_scope(New(), "k11");
    [Fact] public Task Cascade() => MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(New(), "k12");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~MemoryGraphStore"`
Expected: compile failure — `IMemoryGraphStore`, `GraphNode`, `GraphNodeWrite`, `GraphTouch`,
`InMemoryMemoryGraphStore` do not exist.

- [ ] **Step 3: Write the contract**

`src/Lyntai.Core/Memory/IMemoryGraphStore.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>One stored node.</summary>
/// <param name="Id">Store-assigned, unique within the store.</param>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="TaskKey">Consumer/purpose scope.</param>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form recall returns.</param>
/// <param name="Content">The full text expansion returns.</param>
/// <param name="Grade">Associative or authoritative; never <see cref="MemoryGrade.Inherit"/>.</param>
/// <param name="CreatedAt">When first stored.</param>
/// <param name="LastRecalledAt">When last successfully recalled.</param>
/// <param name="RecallCount">How many times recalled.</param>
/// <param name="Stability">Half-life in days.</param>
/// <param name="Relevance">How well it matched the seeding query, 0..1. A backend that ranks by recency
/// rather than relevance reports 1.</param>
/// <param name="Degree">How many edges it has.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
public sealed record GraphNode(
    long Id, string Engine, string TaskKey, string Scope, string Headline, string Content,
    MemoryGrade Grade, DateTimeOffset CreatedAt, DateTimeOffset LastRecalledAt, int RecallCount,
    double Stability, double Relevance, int Degree, IReadOnlyDictionary<string, string>? Metadata)
{
    /// <summary>This node's decay state, for an <see cref="IRetrievabilityPolicy"/>.</summary>
    public MemoryDecayState DecayState => new(CreatedAt, LastRecalledAt, RecallCount, Stability);
}

/// <summary>A node to store. Identity is (<paramref name="Engine"/>, <paramref name="TaskKey"/>,
/// <paramref name="Scope"/>, <paramref name="Content"/>) — storing identical content refreshes rather than
/// duplicating, matching every other memory surface in the library.</summary>
/// <param name="Engine">The owning engine's name.</param>
/// <param name="TaskKey">Consumer/purpose scope.</param>
/// <param name="Scope">Variant scope.</param>
/// <param name="Headline">The one-line form; the engine derives it when the caller authored none.</param>
/// <param name="Content">The full text.</param>
/// <param name="Grade">Associative or authoritative.</param>
/// <param name="InitialStability">Half-life in days for a new node, from the policy.</param>
/// <param name="Metadata">App-owned extra data, or null.</param>
public sealed record GraphNodeWrite(
    string Engine, string TaskKey, string Scope, string Headline, string Content, MemoryGrade Grade,
    double InitialStability, IReadOnlyDictionary<string, string>? Metadata);

/// <summary>A reinforcement to record against one node.</summary>
/// <param name="Id">The node.</param>
/// <param name="LastRecalledAt">The recall time.</param>
/// <param name="Stability">Its new half-life in days, from the policy.</param>
public readonly record struct GraphTouch(long Id, DateTimeOffset LastRecalledAt, double Stability);

/// <summary>
/// Storage for the graph memory engine: nodes, weighted edges, and the decay bookkeeping.
/// <para><b>The store never evaluates the decay curve.</b> It filters candidates with a plain
/// <c>age_days / stability &lt;= cutoff</c> comparison supplied by
/// <see cref="IRetrievabilityPolicy.CandidateCutoff"/> and orders by the same ratio; exact retrievability
/// and final ranking happen in the engine. SQLite has <c>pow</c> only when built with a specific flag, and
/// no fixed SQL expression could encode a policy the application supplies.</para>
/// <para>Every method takes <c>now</c> rather than reading a clock, so decay is deterministic under
/// test.</para>
/// </summary>
public interface IMemoryGraphStore
{
    /// <summary>Store a node, or refresh the existing one with identical content. Returns its id.</summary>
    Task<long> UpsertAsync(GraphNodeWrite write, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>The candidate set for a recall: nodes in (<paramref name="engine"/>,
    /// <paramref name="taskKey"/>, <paramref name="scope"/>) matching <paramref name="query"/>, bounded by
    /// <paramref name="maxAgeOverStability"/> and capped at <paramref name="limit"/>.
    /// <para>A null or whitespace <paramref name="query"/> takes the most recent. AUTHORITATIVE nodes are
    /// admitted unconditionally — neither the query nor the cutoff excludes them.</para>
    /// <para>Portable guarantee, and the same one <c>IMemoryStore.RecallAsync</c> states: a node whose
    /// content contains a single ≥3-character query token as a substring is found on every backend.
    /// Multi-token matching and same-match ordering diverge by design.</para></summary>
    Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope, string? query,
        double? maxAgeOverStability, int limit, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Nodes connected to any of <paramref name="ids"/>, strongest edge first, excluding the
    /// <paramref name="ids"/> themselves.</summary>
    Task<IReadOnlyList<GraphNode>> NeighboursAsync(string engine, IReadOnlyCollection<long> ids, int limit,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>One node by id, or null.</summary>
    Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default);

    /// <summary>Record reinforcement for the nodes actually returned by a recall. Best-effort by contract:
    /// the caller treats a failure here as "no learning", never as "no memory".</summary>
    Task TouchAsync(IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default);

    /// <summary>Connect two nodes, strengthening the edge if it already exists. Directed unless
    /// <paramref name="symmetric"/>.</summary>
    Task LinkAsync(long from, long to, string? kind, double weight, bool symmetric, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Reap nodes. AUTHORITATIVE nodes are never eligible for
    /// <paramref name="maxAgeOverStability"/>. Returns how many were removed.</summary>
    Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
        TimeSpan? olderThan, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Remove every node in the scope, and every edge touching one.</summary>
    Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the InMemory backend**

`src/Lyntai.Storage.InMemory/InMemoryMemoryGraphStore.cs`. Match the existing InMemory recall semantics —
the query matches as a **contiguous, case-insensitive substring** and ties break by recency — because that
is what `InMemoryMemoryStore` already does and the contract's portable guarantee is single-token only.

```csharp
using System.Collections.Concurrent;
using Lyntai.Memory;

namespace Lyntai.Storage.InMemory;

/// <summary>In-process <see cref="IMemoryGraphStore"/> — the zero-dependency default and the reference
/// implementation the SQL backends are held to by <c>MemoryGraphStoreContract</c>.
/// <para>Recall matches the query as a CONTIGUOUS case-insensitive substring and ranks by recency, exactly
/// like <c>InMemoryMemoryStore</c>; the SQLite backend's trigram/bm25 behaviour diverges by design and the
/// contract asserts only the portable single-token guarantee.</para></summary>
public sealed class InMemoryMemoryGraphStore : IMemoryGraphStore
{
    private readonly object _gate = new();
    private readonly Dictionary<long, GraphNode> _nodes = [];
    private readonly Dictionary<(long From, long To, string Kind), double> _edges = [];
    private long _next = 1;

    /// <inheritdoc />
    public Task<long> UpsertAsync(GraphNodeWrite write, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var existing = _nodes.Values.FirstOrDefault(n =>
                n.Engine == write.Engine && n.TaskKey == write.TaskKey && n.Scope == write.Scope &&
                n.Content == write.Content);
            if (existing is not null)
            {
                // identical content REFRESHES, matching IMemoryStore and ISemanticMemory
                _nodes[existing.Id] = existing with { LastRecalledAt = now, Grade = write.Grade };
                return Task.FromResult(existing.Id);
            }

            var id = _next++;
            _nodes[id] = new GraphNode(id, write.Engine, write.TaskKey, write.Scope, write.Headline,
                write.Content, write.Grade, now, now, 0, write.InitialStability, 1, 0, write.Metadata);
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, double? maxAgeOverStability, int limit, DateTimeOffset now,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var hits = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                .Where(n => n.Grade == MemoryGrade.Authoritative || Matches(n, query))
                .Where(n => n.Grade == MemoryGrade.Authoritative || WithinCutoff(n, maxAgeOverStability, now))
                .OrderByDescending(n => n.LastRecalledAt)
                .ThenByDescending(n => n.Id)          // unique tiebreaker: ties must not wobble
                .Take(limit)
                .Select(WithDegree)
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNode>>(hits);
        }

        static bool Matches(GraphNode n, string? q) =>
            string.IsNullOrWhiteSpace(q) || n.Content.Contains(q, StringComparison.OrdinalIgnoreCase);

        static bool WithinCutoff(GraphNode n, double? cutoff, DateTimeOffset now)
        {
            if (cutoff is not double c || double.IsPositiveInfinity(c)) return true;
            var stability = n.Stability > 0 ? n.Stability : 1;
            return (now - n.LastRecalledAt).TotalDays / stability <= c;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> NeighboursAsync(string engine, IReadOnlyCollection<long> ids,
        int limit, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var seen = ids.ToHashSet();
            var byWeight = _edges
                .Where(e => seen.Contains(e.Key.From) && !seen.Contains(e.Key.To))
                .GroupBy(e => e.Key.To)
                .Select(g => (Id: g.Key, Weight: g.Max(e => e.Value)))
                .OrderByDescending(x => x.Weight)
                .ThenByDescending(x => x.Id)          // unique tiebreaker
                .Take(limit);

            var hits = byWeight
                .Where(x => _nodes.ContainsKey(x.Id) && _nodes[x.Id].Engine == engine)
                .Select(x => WithDegree(_nodes[x.Id]))
                .ToList();
            return Task.FromResult<IReadOnlyList<GraphNode>>(hits);
        }
    }

    /// <inheritdoc />
    public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_nodes.TryGetValue(id, out var n) && n.Engine == engine
                ? WithDegree(n) : null);
    }

    /// <inheritdoc />
    public Task TouchAsync(IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            foreach (var touch in touches)
                if (_nodes.TryGetValue(touch.Id, out var n))
                    _nodes[touch.Id] = n with
                    {
                        LastRecalledAt = touch.LastRecalledAt,
                        Stability = touch.Stability,
                        RecallCount = n.RecallCount + 1,
                    };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LinkAsync(long from, long to, string? kind, double weight, bool symmetric,
        DateTimeOffset now, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == to) return Task.CompletedTask;    // a self-edge is never useful and skews Degree
        lock (_gate)
        {
            Strengthen(from, to);
            if (symmetric) Strengthen(to, from);
        }
        return Task.CompletedTask;

        void Strengthen(long a, long b)
        {
            var key = (a, b, kind ?? "");
            _edges[key] = _edges.TryGetValue(key, out var existing) ? existing + weight : weight;
        }
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, DateTimeOffset now,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var doomed = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                .Where(n => n.Grade != MemoryGrade.Authoritative)   // never eligible; falls out of r = 1
                .Where(n =>
                    (maxAgeOverStability is double c &&
                     (now - n.LastRecalledAt).TotalDays / (n.Stability > 0 ? n.Stability : 1) > c) ||
                    (olderThan is TimeSpan age && now - n.CreatedAt > age))
                .Select(n => n.Id)
                .ToList();
            foreach (var id in doomed) Remove(id);
            return Task.FromResult(doomed.Count);
        }
    }

    /// <inheritdoc />
    public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var doomed = _nodes.Values
                .Where(n => n.Engine == engine && n.TaskKey == taskKey)
                .Where(n => scope is null || n.Scope == scope)
                .Select(n => n.Id)
                .ToList();
            foreach (var id in doomed) Remove(id);
        }
        return Task.CompletedTask;
    }

    // deleting a node takes its edges with it — the SQL backends get this from ON DELETE CASCADE, and a
    // dangling edge here would resurrect a deleted neighbour on the next traversal
    private void Remove(long id)
    {
        _nodes.Remove(id);
        foreach (var key in _edges.Keys.Where(k => k.From == id || k.To == id).ToList())
            _edges.Remove(key);
    }

    private GraphNode WithDegree(GraphNode node) =>
        node with { Degree = _edges.Keys.Count(k => k.From == node.Id) };
}
```

- [ ] **Step 5: Register it**

Find the InMemory package's `UseInMemoryStorage` extension (search for `InMemoryCuratedMemoryStore` to
locate the registration block) and add, alongside the other stores and guarded by the same
`StorageFeature.Memory` check the memory store already uses:

```csharp
services.TryAddSingleton<IMemoryGraphStore, InMemoryMemoryGraphStore>();
```

- [ ] **Step 6: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~MemoryGraphStore"`
Expected: PASS, 12 matched.

- [ ] **Step 7: Commit**

```bash
git add src/Lyntai.Core/Memory src/Lyntai.Storage.InMemory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): add the graph store contract and its InMemory backend"
```

---

### Task 3: The graph engine

**Files:**
- Create: `src/Lyntai.Core/Memory/MemoryHeadline.cs`
- Create: `src/Lyntai.Core/Memory/GraphMemoryOptions.cs`
- Create: `src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs`
- Test: `tests/Lyntai.Tests/Memory/GraphMemoryEngineTests.cs`
- Test: add `GraphEngineContractTests` to `tests/Lyntai.Tests/Memory/MemoryEngineContractTests.cs`

**Interfaces:**
- Consumes: `IMemoryEngine`, `IExpandableMemory`, `ILinkableMemory`, `IForgettableMemory`, `MemoryWrite`,
  `MemoryQuery`, `MemoryItem`, `MemoryRecall`, `MemorySources`, `MemoryGrade`, `MemoryGrades`, `MemoryRef`
  (MEM1); `IRetrievabilityPolicy` (Task 1); `IMemoryGraphStore` (Task 2).
- Produces: `GraphMemoryOptions`; `GraphMemoryEngine(string name, IMemoryGraphStore store,
  GraphMemoryOptions? options, IRetrievabilityPolicy? policy, Func<DateTimeOffset>? clock,
  ILogger<GraphMemoryEngine>? logger)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

public class GraphMemoryEngineTests
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private GraphMemoryEngine Engine(GraphMemoryOptions? options = null) =>
        new("project/graph", new InMemoryMemoryGraphStore(), options, clock: () => _now);

    [Fact]
    public async Task Recall_returns_headlines_and_withholds_content_until_expansion()
    {
        var engine = Engine();
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s",
            "The build gate is dev.mjs verify, which runs seven checks and stops at the first failure."));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "build"));

        Assert.Single(recall.Items);
        Assert.Null(recall.Items[0].Content);                 // the whole point of a cheap first load
        Assert.True(recall.Items[0].Headline.Length <= new GraphMemoryOptions().HeadlineChars + 1);
        Assert.Equal(MemorySources.Graph, recall.Ran);

        var expanded = await engine.ExpandAsync(reference);
        Assert.Contains("seven checks", expanded.Items[0].Content!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_authored_headline_is_used_as_given()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "a long body of explanatory text",
            Headline: "short form"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "explanatory"));

        Assert.Equal("short form", recall.Items[0].Headline);
    }

    [Fact]
    public async Task Authoritative_material_is_never_shortened()
    {
        var engine = Engine(new GraphMemoryOptions { HeadlineChars = 10 });
        const string exact = "The build gate is node devtools/dev.mjs verify";
        await engine.RememberAsync(new MemoryWrite("t", "s", exact, Grade: MemoryGrade.Authoritative));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.Equal(exact, recall.Items[0].Headline);
        Assert.Equal(exact, recall.Items[0].Content);
        Assert.Equal(1.0, recall.Items[0].Retrievability, precision: 9);
    }

    [Fact]
    public async Task Recall_reinforces_what_it_returned()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "reinforced fact"));

        var before = (await engine.RecallAsync(new MemoryQuery("t", "s", "reinforced"))).Items[0];
        _now = _now.AddDays(14);
        var after = (await engine.RecallAsync(new MemoryQuery("t", "s", "reinforced"))).Items[0];

        // 14 days on a 7-day half-life would be r=0.25; the first recall pushed the half-life out
        Assert.Equal(1.0, before.Retrievability, precision: 6);
        Assert.True(after.Retrievability > 0.25,
            $"reinforcement did not extend the half-life (r={after.Retrievability})");
    }

    [Fact]
    public async Task A_stale_associative_memory_falls_below_the_floor()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "one-off noise"));

        _now = _now.AddDays(365);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "noise"));

        Assert.Empty(recall.Items);
    }

    [Fact]
    public async Task A_stale_authoritative_memory_does_not()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "an exact fact",
            Grade: MemoryGrade.Authoritative));

        _now = _now.AddDays(10_000);
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "exact"));

        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task Items_recalled_together_become_connected()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "alpha relates to the gate"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "beta relates to the gate"));

        await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));       // co-activation happens here
        var again = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.All(again.Items, i => Assert.True(i.Degree >= 1,
            "co-activation did not link the items returned together"));
    }

    [Fact]
    public async Task Expansion_returns_the_neighbours_of_what_it_expanded()
    {
        var engine = Engine();
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "alpha fact"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "beta fact"));
        await engine.LinkAsync(a, b, symmetric: true);

        var expanded = await engine.ExpandAsync(a);

        Assert.Equal(2, expanded.Items.Count);                             // the node plus one neighbour
        Assert.Contains(expanded.Items, i => i.Headline.Contains("beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Spreading_reaches_a_neighbour_the_query_never_matched()
    {
        var engine = Engine(new GraphMemoryOptions { Hops = 1 });
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "rollbacks page the on-call"));
        await engine.LinkAsync(a, b, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "pipeline"));

        Assert.Contains(recall.Items, i => i.Headline.Contains("rollbacks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hop_away_ranks_below_a_direct_match()
    {
        var engine = Engine(new GraphMemoryOptions { Hops = 1 });
        var a = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline"));
        var b = await engine.RememberAsync(new MemoryWrite("t", "s", "rollbacks page the on-call"));
        await engine.LinkAsync(a, b, symmetric: true);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "pipeline"));

        Assert.Contains("pipeline", recall.Items[0].Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_touch_still_returns_the_hits()
    {
        // a read-only database must degrade to "no learning", never to "no memory"
        var engine = new GraphMemoryEngine("project/graph", new TouchHostileGraphStore(), clock: () => _now);
        await engine.RememberAsync(new MemoryWrite("t", "s", "still recalled"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "still"));

        Assert.Single(recall.Items);
    }

    [Fact]
    public async Task It_stores_both_grades()
    {
        Assert.Equal(MemoryGrades.Associative | MemoryGrades.Authoritative, Engine().Supported);
    }

    [Fact]
    public async Task Pruning_reaps_only_the_forgotten()
    {
        var engine = Engine();
        await engine.RememberAsync(new MemoryWrite("t", "s", "faded"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        _now = _now.AddDays(365);
        var removed = await engine.PruneAsync("t", "s", minRetrievability: 0.05);

        Assert.Equal(1, removed);
    }
}
```

Add `TouchHostileGraphStore` to `tests/Lyntai.Tests/Memory/FakeMemoryEngines.cs`: it delegates to an inner
`InMemoryMemoryGraphStore` for everything and throws `InvalidOperationException` from `TouchAsync` and
`LinkAsync`.

Add to `MemoryEngineContractTests.cs`:

```csharp
public class GraphEngineContractTests
{
    private static IMemoryEngine New() =>
        new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore());

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GraphMemoryEngine"`
Expected: compile failure — `GraphMemoryEngine` and `GraphMemoryOptions` do not exist.

- [ ] **Step 3: Write the headline deriver**

`src/Lyntai.Core/Memory/MemoryHeadline.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>Derives the one-line form recall returns for ASSOCIATIVE material.
/// <para><b>It does not split on sentences</b>, deliberately. "The build gate is dev.mjs verify" cut at the
/// first period reads "The build gate is dev." — a confidently wrong headline, which is worse than no
/// memory at all. Cutting on a word boundary and marking the truncation is honest instead: the reader can
/// see something was elided. Authoritative material is never passed through here at all.</para></summary>
internal static class MemoryHeadline
{
    public static string Derive(string content, int maxChars)
    {
        var text = content.Trim().ReplaceLineEndings(" ");
        if (text.Length <= maxChars) return text;

        var cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
        if (cut <= 0) cut = maxChars;                 // a single very long token: hard cut rather than none
        return string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
    }
}
```

- [ ] **Step 4: Write the options**

`src/Lyntai.Core/Memory/GraphMemoryOptions.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>How the graph engine retrieves. Every value is defaulted; several are <b>unmeasured</b> and
/// marked as such — see the MEM-TUNE task before treating them as tuned.</summary>
public sealed record GraphMemoryOptions
{
    /// <summary>How far to spread from the seed set. Three or more hops reaches most of a connected graph,
    /// which defeats the purpose. Reasoned, not measured.</summary>
    public int Hops { get; init; } = 2;

    /// <summary>Rank attenuation per hop: material one hop out is worth this fraction of a direct match.
    /// Halving keeps hop-2 material below hop-1. Reasoned, not measured.</summary>
    public double HopAttenuation { get; init; } = 0.5;

    /// <summary>Associative material below this retrievability is dropped — the point at which something
    /// counts as forgotten, and the same threshold <c>PruneAsync</c> reaps by. Authoritative material holds
    /// 1.0 and is never affected. <b>Unmeasured</b> — see MEM-TUNE.</summary>
    public double MinRetrievability { get; init; } = 0.05;

    /// <summary>Length cap for a derived headline. <b>Unmeasured</b> — see MEM-TUNE.</summary>
    public int HeadlineChars { get; init; } = 120;

    /// <summary>How many of the returned nodes get co-activation edges. A k=10 recall would otherwise write
    /// 45 edges every turn. Reasoned, not measured.</summary>
    public int CoActivationCap { get; init; } = 5;

    /// <summary>How many candidates to fetch per requested item. The store bounds the candidate set with
    /// plain arithmetic and the policy does exact ranking afterwards, so a multiple above 1 is what keeps
    /// that ranking meaningful.</summary>
    public int CandidateMultiplier { get; init; } = 4;

    /// <summary>Items returned when the query names no limit.</summary>
    public int DefaultLimit { get; init; } = 10;

    /// <summary>Constants of the default decay curve. Ignored when a custom
    /// <see cref="IRetrievabilityPolicy"/> is supplied.</summary>
    public HalfLifeOptions Decay { get; init; } = new();
}
```

- [ ] **Step 5: Write the engine**

`src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs`:

```csharp
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>
/// Memory that forgets and relinks: entries decay unless reused, connect to what they were recalled
/// beside, and open as a cheap index that expands on demand.
/// <para>Recall runs seed → spread → score → budget → touch. It is the one recall path in this library that
/// WRITES — reinforcement and co-activation are recorded for what it returned — so both writes are
/// best-effort: a failure logs and the hits still come back, and a read-only database therefore degrades to
/// "no learning" rather than "no memory".</para>
/// </summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="store">Node and edge storage.</param>
/// <param name="options">Retrieval knobs; null takes the defaults.</param>
/// <param name="policy">The decay curve; null takes <see cref="HalfLifeRetrievability"/>.</param>
/// <param name="clock">Injected time — decay tested against the wall clock cannot be tested.</param>
/// <param name="logger">Optional.</param>
public sealed class GraphMemoryEngine(
    string name,
    IMemoryGraphStore store,
    GraphMemoryOptions? options = null,
    IRetrievabilityPolicy? policy = null,
    Func<DateTimeOffset>? clock = null,
    ILogger<GraphMemoryEngine>? logger = null)
    : IMemoryEngine, IExpandableMemory, ILinkableMemory, IForgettableMemory
{
    private readonly GraphMemoryOptions _options = options ?? new GraphMemoryOptions();
    private readonly IRetrievabilityPolicy _policy =
        policy ?? new HalfLifeRetrievability((options ?? new GraphMemoryOptions()).Decay);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ILogger _logger = logger ?? NullLogger<GraphMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var grade = write.Grade == MemoryGrade.Inherit ? MemoryGrade.Associative : write.Grade;

        // authoritative material is NEVER passed through headline derivation — a truncated exact fact is
        // confidently wrong, which is worse than having no memory at all
        var headline = write.Headline
            ?? (grade == MemoryGrade.Authoritative
                ? write.Content
                : MemoryHeadline.Derive(write.Content, _options.HeadlineChars));

        var id = await store.UpsertAsync(
            new GraphNodeWrite(Name, write.TaskKey, write.Scope, headline, write.Content, grade,
                _policy.InitialStability, write.Metadata),
            _clock(), ct).ConfigureAwait(false);

        return new MemoryRef(Name, id.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var now = _clock();
        var limit = query.Limit ?? _options.DefaultLimit;

        List<(GraphNode Node, int Hop)> found;
        try
        {
            found = await GatherAsync(query, limit, now, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "graph recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }

        var scored = found
            .Select(f => (f.Node, Retrievability: Retrievability(f.Node, now),
                Rank: f.Node.Relevance * Retrievability(f.Node, now)
                      * Math.Pow(_options.HopAttenuation, f.Hop)))
            .Where(x => x.Node.Grade == MemoryGrade.Authoritative
                     || x.Retrievability >= _options.MinRetrievability)
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.Node.Id)          // unique tiebreaker: ties must not wobble
            .Take(limit)
            .ToList();

        if (scored.Count == 0) return MemoryRecall.Empty;

        await ReinforceAsync([.. scored.Select(x => x.Node)], now, ct).ConfigureAwait(false);

        var items = scored
            .Select(x => new MemoryItem(
                new MemoryRef(Name, x.Node.Id.ToString(CultureInfo.InvariantCulture)),
                x.Node.Headline,
                // associative content is withheld until expansion — that is what makes the first load
                // cheap; authoritative content is always present, because it is never returned truncated
                x.Node.Grade == MemoryGrade.Authoritative ? x.Node.Content : null,
                x.Node.Grade, x.Node.Relevance, x.Retrievability, x.Node.Degree))
            .ToList();

        return new MemoryRecall(items, MemorySources.Graph);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!long.TryParse(reference.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return MemoryRecall.Empty;

        var now = _clock();
        var node = await store.GetAsync(Name, id, ct).ConfigureAwait(false);
        if (node is null) return MemoryRecall.Empty;

        var neighbours = await store
            .NeighboursAsync(Name, [id], _options.DefaultLimit, now, ct).ConfigureAwait(false);

        // expanding a node reinforces it — digging in one direction is exactly what should make that
        // direction more retrievable next time
        await ReinforceAsync([node], now, ct).ConfigureAwait(false);

        var items = new List<MemoryItem>(neighbours.Count + 1)
        {
            // the expanded node carries its FULL content, whatever its grade — that is what expansion is
            new(reference, node.Headline, node.Content, node.Grade, 1, Retrievability(node, now), node.Degree),
        };
        items.AddRange(neighbours.Select(n => new MemoryItem(
            new MemoryRef(Name, n.Id.ToString(CultureInfo.InvariantCulture)),
            n.Headline, n.Grade == MemoryGrade.Authoritative ? n.Content : null,
            n.Grade, n.Relevance, Retrievability(n, now), n.Degree)));

        return new MemoryRecall(items, MemorySources.Graph);
    }

    /// <inheritdoc />
    public async Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default)
    {
        if (!long.TryParse(from.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ||
            !long.TryParse(to.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            throw new ArgumentException(
                $"Memory engine '{Name}' addresses nodes by numeric id; got '{from.Id}' and '{to.Id}'.");

        // an EXPLICIT link is a write, so it surfaces its failure — unlike the co-activation edges recall
        // records opportunistically, which are best-effort
        await store.LinkAsync(a, b, kind, weight, symmetric, _clock(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default) =>
        store.PruneAsync(Name, taskKey, scope,
            minRetrievability is double m ? _policy.CandidateCutoff(m) : null,
            olderThan, _clock(), ct);

    /// <summary>Forget everything under (taskKey, scope) — explicit, never a side effect of decay.</summary>
    /// <param name="taskKey">The task to clear.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
        store.ForgetAsync(Name, taskKey, scope, ct);

    private double Retrievability(GraphNode node, DateTimeOffset now) =>
        node.Grade == MemoryGrade.Authoritative ? 1 : _policy.Retrievability(node.DecayState, now);

    private async Task<List<(GraphNode Node, int Hop)>> GatherAsync(MemoryQuery query, int limit,
        DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = _policy.CandidateCutoff(_options.MinRetrievability);
        var candidates = limit * Math.Max(1, _options.CandidateMultiplier);

        var seeds = await store.SeedAsync(Name, query.TaskKey, query.Scope, query.Query,
            double.IsPositiveInfinity(cutoff) ? null : cutoff, candidates, now, ct).ConfigureAwait(false);

        var found = new List<(GraphNode Node, int Hop)>(seeds.Select(n => (n, 0)));
        var seen = seeds.Select(n => n.Id).ToHashSet();
        var frontier = seen.ToList();

        for (var hop = 1; hop <= _options.Hops && frontier.Count > 0; hop++)
        {
            ct.ThrowIfCancellationRequested();
            var neighbours = await store
                .NeighboursAsync(Name, frontier, candidates, now, ct).ConfigureAwait(false);

            frontier = [];
            foreach (var neighbour in neighbours)
                if (seen.Add(neighbour.Id))
                {
                    found.Add((neighbour, hop));
                    frontier.Add(neighbour.Id);
                }
        }

        return found;
    }

    /// <summary>Record reinforcement and co-activation for what a recall actually returned.
    /// <para>BEST-EFFORT by design: a failure here logs and the caller keeps its hits, so a read-only
    /// database degrades to "no learning" rather than "no memory". Co-activation is capped, or a ten-item
    /// recall would write forty-five edges on every turn.</para></summary>
    private async Task ReinforceAsync(IReadOnlyList<GraphNode> nodes, DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var touches = nodes
                .Where(n => n.Grade != MemoryGrade.Authoritative)   // nothing to reinforce at r = 1
                .Select(n => new GraphTouch(n.Id, now, _policy.Reinforce(n.DecayState, now)))
                .ToList();
            if (touches.Count > 0) await store.TouchAsync(touches, ct).ConfigureAwait(false);

            var top = nodes.Take(Math.Max(0, _options.CoActivationCap)).Select(n => n.Id).ToList();
            for (var i = 0; i < top.Count; i++)
                for (var j = i + 1; j < top.Count; j++)
                    await store.LinkAsync(top[i], top[j], null, 1, symmetric: true, now, ct)
                        .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "graph reinforcement failed for {Engine}; returning hits without learning", Name);
        }
    }
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GraphMemoryEngine"`
Expected: PASS, 13 matched. Then
`node devtools/dev.mjs test --filter "FullyQualifiedName~EngineContract"` — expected PASS, 40 matched
(5 engines × 8).

- [ ] **Step 7: Commit**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): add the graph engine — decay, spreading, and progressive disclosure"
```

---

### Task 4: Wire it into the builder, and close out

**Files:**
- Modify: `src/Lyntai.Core/Memory/MemoryEngineBuilder.cs`
- Modify: `src/Lyntai.Core/Memory/MemoryEngineRegistration.cs` (`AddMemory` prefers the graph)
- Test: `tests/Lyntai.Tests/Memory/GraphMemoryWiringTests.cs`
- Modify: `tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt`, `CHANGELOG.md`, `README.md`, `TASKS.md`,
  `docs/task-archive.md`

**Interfaces:**
- Consumes: everything from Tasks 1–3, plus MEM1's `MemoryEngineBuilder`.
- Produces: `MemoryEngineBuilder.UseGraph(Action<GraphMemoryOptions>? configure = null,
  string label = "graph")`.

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

public class GraphMemoryWiringTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        services.AddSingleton<IMemoryGraphStore>(new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UseGraph_wires_a_working_graph_engine()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph()));

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
        await engine.RememberAsync(new MemoryWrite("t", "s", "the build gate runs seven checks"));
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.Single(recall.Items);
        Assert.Equal(MemorySources.Graph, recall.Ran);
    }

    [Fact]
    public void UseGraph_without_a_graph_store_fails_at_startup_naming_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains("IMemoryGraphStore", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_graph_member_blends_with_an_authoritative_curated_member()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e
                .UseCurated("glossary").Reserve(200)
                .UseGraph()
                .Budget(600))
            .UseMemoryComposer("project"));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        await factory.Get("project/glossary").RememberAsync(new MemoryWrite("t", "s",
            "the build gate is dev.mjs verify", Grade: MemoryGrade.Authoritative));
        for (var i = 0; i < 50; i++)
            await factory.Get("project/graph").RememberAsync(
                new MemoryWrite("t", "s", $"gate related chatter number {i} at some length"));

        var composed = await sp.GetRequiredService<IPromptComposer>()
            .ComposeAsync("BASE", "t", "s", "gate");

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
        Assert.Contains("## Recalled context (associative", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expansion_routes_through_the_blend_to_the_graph_member()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseCurated().UseGraph()));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        var blend = (IExpandableMemory)factory.Get("project");
        var reference = await factory.Get("project/graph").RememberAsync(
            new MemoryWrite("t", "s", "a long fact whose content is withheld until it is expanded"));

        var expanded = await blend.ExpandAsync(reference);

        Assert.Contains("withheld until it is expanded", expanded.Items[0].Content!, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GraphMemoryWiring"`
Expected: compile failure — `UseGraph` does not exist.

- [ ] **Step 3: Add `UseGraph` to the builder**

In `src/Lyntai.Core/Memory/MemoryEngineBuilder.cs`, beside `UseCurated`:

```csharp
    /// <summary>Draw on the decaying, linked graph store — the engine that forgets what goes unused,
    /// connects what is recalled together, and returns headlines that expand on demand. Holds BOTH grades,
    /// so it can carry exact facts alongside recalled ones.</summary>
    /// <param name="configure">Retrieval knobs and the decay constants.</param>
    /// <param name="label">Distinguishes several members of the same kind.</param>
    public MemoryEngineBuilder UseGraph(Action<GraphMemoryOptions>? configure = null,
        string label = "graph")
    {
        var options = new GraphMemoryOptions();
        if (configure is not null)
        {
            // GraphMemoryOptions is an init-only record, so the callback mutates a builder-shaped copy
            var draft = new GraphMemoryOptionsBuilder(options);
            configure(draft.Options);
            options = draft.Options;
        }
        _members.Add(new MemberSpec(label, (sp, full) => new GraphMemoryEngine(
            full, Required<IMemoryGraphStore>(sp), options, sp.GetService<IRetrievabilityPolicy>(),
            logger: sp.GetService<ILogger<GraphMemoryEngine>>())));
        return this;
    }
```

**That draft type is unnecessary — delete it.** `Action<GraphMemoryOptions>` cannot mutate an init-only
record at all, so the callback would silently do nothing: exactly the "documented option that isn't wired"
failure `pitfalls.md` records. Take the options **by value** instead, which is honest about the record being
immutable:

```csharp
    /// <summary>Draw on the decaying, linked graph store — the engine that forgets what goes unused,
    /// connects what is recalled together, and returns headlines that expand on demand. Holds BOTH grades,
    /// so it can carry exact facts alongside recalled ones.</summary>
    /// <param name="options">Retrieval knobs and decay constants; null takes the defaults.</param>
    /// <param name="label">Distinguishes several members of the same kind.</param>
    public MemoryEngineBuilder UseGraph(GraphMemoryOptions? options = null, string label = "graph")
    {
        var resolved = options ?? new GraphMemoryOptions();
        _members.Add(new MemberSpec(label, (sp, full) => new GraphMemoryEngine(
            full, Required<IMemoryGraphStore>(sp), resolved, sp.GetService<IRetrievabilityPolicy>(),
            logger: sp.GetService<ILogger<GraphMemoryEngine>>())));
        return this;
    }
```

Callers write `.UseGraph(new GraphMemoryOptions { Hops = 3 })`, which is the `with`-friendly shape the rest
of the record surface already uses.

- [ ] **Step 4: Prefer the graph in `AddMemory`**

Spec A §5.1 says the zero-config engine is "graph if a graph store is available, else lexical + semantic".
`IMemoryGraphStore` now exists, so in `MemoryEngineRegistration.AddMemory` replace the body:

```csharp
    public static LyntaiBuilder AddMemory(this LyntaiBuilder builder, string name = "default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        // graph memory when a graph store reached the container, lexical otherwise — decided at BUILD
        // time, not here, because a storage backend may be registered after this call
        return builder
            .AddMemoryEngine(name, e => e.UseBestAvailable())
            .UseMemoryComposer(name);
    }
```

and add to `MemoryEngineBuilder`:

```csharp
    /// <summary>The zero-configuration member: the graph engine when an <see cref="IMemoryGraphStore"/>
    /// reached the container, the keyword store otherwise. Resolved when the container is BUILT, not when
    /// this is called, because a storage backend may be registered afterwards.</summary>
    internal MemoryEngineBuilder UseBestAvailable()
    {
        _members.Add(new MemberSpec("memory", (sp, full) =>
            sp.GetService<IMemoryGraphStore>() is { } graph
                ? new GraphMemoryEngine(full, graph, policy: sp.GetService<IRetrievabilityPolicy>(),
                    logger: sp.GetService<ILogger<GraphMemoryEngine>>())
                : new LexicalMemoryEngine(full, Required<IMemoryStore>(sp),
                    sp.GetService<ILogger<LexicalMemoryEngine>>())));
        return this;
    }
```

**This changes MEM1's `AddMemory` behaviour**, so update the MEM1 registration test
`AddMemory_registers_one_working_engine_with_no_configuration` if it asserts the member's name — it
asserts only `engine.Name == "default"`, which still holds.

- [ ] **Step 5: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~Lyntai.Tests.Memory"`
Expected: PASS. Read the matched count and confirm it grew by this task's four tests plus Task 3's.

- [ ] **Step 6: Regenerate the API baseline and read the diff**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~ApiSurface"` — expected FAIL on
`Lyntai.Core`. Then:

```bash
cp tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt.actual tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
rm tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt.actual
git diff --ignore-cr-at-eol --stat -- tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
```

**Use `--ignore-cr-at-eol`**: the checked-in baseline is CRLF and the emitted `.actual` is LF, so a plain
diff reports all ~2200 lines as changed and buries the real delta. Confirm the change is **insertions
only** — a deletion means something was removed from the public surface, which this task must not do.

- [ ] **Step 7: Full gate**

Run: `node devtools/dev.mjs verify`
Expected: all seven checks pass. `check-packages` and `check-bundle` are unchanged — no package was added
and Core took no new dependency.

- [ ] **Step 8: Changelog, README, archive**

Add to `CHANGELOG.md` under the existing `## Unreleased` → `### Added`:

```markdown
- **Graph memory** (`GraphMemoryEngine`, `IMemoryGraphStore`, `IRetrievabilityPolicy`, `UseGraph()`) — a
  memory engine whose entries **decay** unless reused, **connect** to whatever was recalled beside them,
  and open as a cheap index of headlines that expand on demand. Retrievability is a read-time function of
  stored timestamps, so there is no sweeper and no background job; each successful recall lengthens an
  entry's half-life, so repeated context becomes durable while one-off noise fades. Decay only ever
  **ranks** — deletion stays explicit. The curve is a seam with a registered exponential default
  (`HalfLifeRetrievability`), so an application can tune the constants (`HalfLifeOptions`) *or* replace the
  model of forgetting, and neither choice forecloses the other. Edges form model-free: entries returned
  together are linked, and the link strengthens on recurrence. This release ships the InMemory backend;
  SQLite and Postgres follow.
```

Add a README subsection under **Named memory engines** showing `UseGraph()` and the recall→expand shape.

Then move **MEM2a** out of `TASKS.md` into `docs/task-archive.md` with the completion date and outcome,
leaving **MEM2b**, **MEM2c** and **MEM-TUNE** open — and make sure Part 46's summary in `TASKS.md` does not
read as finished.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(memory): wire the graph engine into the builder and record MEM2a"
```

---

## Self-Review

**Spec coverage (Spec B).** §2 the model → Tasks 2–3. §3 retrievability → Task 1. §3.1 the policy seam and
`MaxStability` → Task 1. §3.2 the candidate-cutoff trick → `IMemoryGraphStore.SeedAsync` (Task 2) and
`GatherAsync` (Task 3), pinned by the conservative-superset contract fact. §4 edge sources → co-activation
and explicit links in Task 3; **structural and similarity edges are MEM2c**. §5 the five recall steps →
`GatherAsync` + `RecallAsync` + `ReinforceAsync`. §5.1 recall-writes → the best-effort `ReinforceAsync` and
its read-only-degradation test. §6 expand → Task 3. §8 storage → Task 2 for InMemory; **§8.1–8.5 tables,
FTS, migration and the relational pair are MEM2b**. §9 error handling → the fail-open paths throughout.
§10 testing → each task's tests; the policy contract and stability ceiling are Task 1.

**Not covered here, by design:** §7 agent tools, §4's similarity row, §8's SQL backends, §11 MEM-TUNE.
Those are MEM2b and MEM2c, and the table at the top says so.

**Type consistency check.** `GraphNode`/`GraphNodeWrite`/`GraphTouch` are declared once (Task 2) and used
with those exact member names in Task 3. `IRetrievabilityPolicy`'s four members are used exactly as
declared. `MemoryEngineBuilder`'s private `MemberSpec` and `Required<T>` are MEM1 members this plan reuses
without redefining.

**One rough edge to settle during Task 4:** `GraphMemoryEngine` exposes `ForgetAsync(taskKey, scope, ct)`,
which is *not* on any interface — `IForgettableMemory` declares only `PruneAsync`. Either add
`ForgetAsync` to `IForgettableMemory` (a breaking change to an interface MEM1 shipped, so it needs a
deliberate decision) or leave it as a concrete-type convenience. **Leave it concrete for now** and note it;
MEM2b or MEM2c can promote it once a caller actually needs it through the interface.
