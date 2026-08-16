# Migrating from 2.5 to 3.0

A 2.5 consumer upgrading to 3.0 does not experience "the policy-seams plan" and "the DSR-default plan"
and "the FSRS-properly plan" as three separate things — they experience one wall of compile errors. This
is the one ordered path through all of it, with a worked before/after and a plain answer to "does my
stored data need anything" (it does not).

The owner's ruling that governs every breaking change below: **3.0 is allowed to break. What it is not
allowed to do is break silently or without a path.** *("3.0 can be a break change we just need to provide
proper migration step/logic.")* Every entry here earns its place under that rule — this guide is the path.

`CHANGELOG.md`'s `## Unreleased` section is the per-change record, with the full reasoning behind each one;
`docs/DECISIONS.md` has the load-bearing calls (D46–D49 cover this window). This guide does not repeat
that detail — it orders it, and shows the diff a consumer actually types.

## The one fact to read before anything else: stored data needs nothing

Every schema change in this window — the node and edge age primitives, provenance, salience/signals,
difficulty, the review log — runs automatically through FluentMigrator on `MigrateUpAsync`. You do not write a
migration, run a script, or touch a row. That is the *automatic* half of this guide, and it is the whole of
it: nothing below asks you to transform stored data.

**3.0 delivers all of it as ONE migration**, `M202608121100_MemoryRetentionModel`, so a 2.5 database applies
exactly one new version and lands at 11 applied migrations. (The six separate migrations an early-access 3.0
build carried were folded into it — they were never released, so nothing in the field has applied one. If you
tracked a pre-release build, delete that database and re-migrate rather than upgrading it: the squashed
migration will fail on a duplicate column.)

**What "automatic" does not do:** it does not fix your source code. A 2.5 consumer's application code
still needs every change from Step 1 onward — the schema migrating cleanly is what makes the rest of this
guide purely a compile-and-decide exercise rather than a data exercise too.

**The specific worry a 2.5 consumer should have, and why it resolves for free:** `HalfLifeRetrievability`
— the exponential forgetting curve 2.5.x shipped — is deleted in 3.0 (below), and `DsrRetrievability`
(FSRS's power-law curve) is now the only shipped curve and the registered default. Do the `Stability`
numbers a 2.5.x deployment already wrote still mean the same thing under the new curve?

**Yes, exactly.** `Stability` means one thing across every implementation this library has ever shipped —
*the position delta at which retrievability is 0.5* — enforced by a contract fact
(`RetrievabilityPolicyContract.Stability_is_the_position_delta_at_which_retrievability_is_half`) against
every curve, old and new. Both curves stored stability in the **same units**, so a 2.5.x row is already
valid under DSR, untouched.

This is proved directly, not asserted:
`DsrRetrievabilityTests.A_row_written_under_2_5s_HalfLife_curve_recalls_correctly_under_Dsr_with_no_migration`
(`tests/Lyntai.Tests/Memory/DsrRetrievabilityTests.cs`) writes a node through a real `SqliteMemoryGraphStore`
exactly the way 2.5.x wrote one — `InitialStability: 20` (2.5.x's own shipped default), the retired
`MemoryRetrievabilityProvenance.HalfLife` provenance bit set — ages it through a bare-constructed
`GraphMemoryEngine` (no `policy:` argument, so it defaults to DSR the 3.0 way), and recalls it. The
assertion is the `r(S) = 0.5` anchor's own fixture state: `Retrievability == 0.5` at `Age == Stability`.
The fact was mutation-checked live while writing it — temporarily anchoring `DsrRetrievability` at a
different age/stability ratio makes the assertion fail, confirming it would actually catch a future
session silently reinterpreting the stability convention.

Keep this fact in mind through every step below: nothing here is a data migration. It is all source code.

## The ordered path

The order below is not the order a consumer's compiler reports errors in — it is the order the *fixes*
depend on. Rename first, because every later step's code samples reference the new names. Then signature
changes, because a renamed type with an old signature still fails to compile. Then the storage contract,
which only matters if you rolled your own store. Then registration and defaults, which are decisions, not
mechanics. New capabilities last, because they need nothing from you at all.

| Step | What changes | Who it affects | Mechanical or a decision? |
|---|---|---|---|
| 1 | **Two** seams renamed and moved namespace (the other two of the four are new in 3.0), plus one storage type | Anyone naming `IMemoryClock`, `IRetrievabilityPolicy` — or configuring the keyword store's size | Mechanical |
| 2 | Signatures on those seams changed/grew | Anyone *implementing* one of the four seams | Mechanical, one exception (see below) |
| 3 | `IMemoryGraphStore` grew five required members | Anyone with a custom `IMemoryGraphStore` | Mechanical |
| 3b | `IJobStore` grew FOUR required members: `PollAgainAsync` and the three slot members | Anyone with a custom `IJobStore` | Mechanical |
| 4 | **Three** registered defaults changed (including: a recall no longer lengthens a half-life); one curve deleted outright; authoritative facts now take slots within the limit | Every consumer, even one who configures nothing | **Decision** |
| 5 | Age/salience plural; ranking selectable; review log runs | Nobody, unless you want it — or deconstruct `MemoryQuery` | No action needed |
| 6 | Generation backends register with a configure callback | Anyone calling `AddOpenAiImageProvider` and friends | Mechanical |
| 7 | Eleven renames, two members made `internal`, three BYO seams grown | Anyone naming one of them, or implementing a CLI dialect / memory engine / job store | Mechanical (one silent case — read it) |
| 8 | `IGenerationRouter` grew a third door, `StreamAsync` | Anyone with a custom `IGenerationRouter` — not users of the built-in one or its decorators | Mechanical |

### Step 1 — Rename the TWO seams 2.5 actually had, and one storage type

Only **two** of the four seams below existed in 2.5 at all. The other two are genuinely new capabilities
this library never shipped before 3.0 — there is nothing to rename for them, because a 2.5 consumer never
had them under any name.

| 2.5 name | 3.0 name | Namespace in 3.0 | Existed in 2.5? |
|---|---|---|---|
| `IMemoryClock` | `IMemoryAgePolicy` | `Lyntai.Memory.Interference` | Yes — real rename + namespace move |
| `IRetrievabilityPolicy` | `IMemoryRetrievabilityPolicy` | `Lyntai.Memory.Forgetting` | Yes — real rename + namespace move |
| *(none)* | `IMemoryRetentionPolicy` | `Lyntai.Memory.Modulation` | No — new in 3.0, ships under this name directly |
| *(none)* | `IMemorySaliencePolicy` | `Lyntai.Memory.Salience` | No — new in 3.0, ships under this name directly |

Every shipped implementation whose own name embedded the retired word renamed with it:
`PerWriteClock`/`ContentSizeClock`/`ElapsedClock`/`BurstDampenedClock` become
`PerWriteAgePolicy`/`ContentSizeAgePolicy`/`ElapsedAgePolicy`/`BurstDampenedAgePolicy`. Both moved into
`Lyntai.Memory.Interference` and `Lyntai.Memory.Forgetting` respectively. `HalfLifeOptions` and
`HalfLifeRetrievability` are **not in that list, and adding a `using` for them will not help**: they are
DELETED in 3.0, declared in no namespace at all, so the fix is the decision in Step 4 rather than a rename.

This step is purely mechanical: add a `using`, change the type name. It changes no behaviour by itself —
the retired working names (`IRetentionModulator`, `ISalienceAppraiser`) that the four-seam-rename note in
`CHANGELOG.md` mentions alongside these two never shipped in any release; they only ever existed mid-development
before landing on the names in the table above. <!-- drift-ok -->

**What actually fails to compile for a 2.5 consumer** — captured for real against this branch:

```
error CS0246: The type or namespace name 'IMemoryClock' could not be found ...
error CS0246: The type or namespace name 'ElapsedClock' could not be found ...
```

Fix: **add** `using Lyntai.Memory.Interference;`, then `IMemoryClock` → `IMemoryAgePolicy`, `ElapsedClock` →
`ElapsedAgePolicy`. Repeat for `IRetrievabilityPolicy` → `IMemoryRetrievabilityPolicy` (**add**
`using Lyntai.Memory.Forgetting;`) wherever you named it.

**Every namespace in this guide is an ADDITION, never a replacement — and reading it the other way is the
one mistake this step can cause.** The four seams moved out of `Lyntai.Memory`; the types they are used
*with* stayed. `GraphMemoryOptions`, `MemoryDecayState`, `MemoryWrite`, `MemoryQuery`, `MemorySignals`,
`IMemoryGraphStore` and `GraphNode` are all still in `Lyntai.Memory`, so a file that *deletes* that import
in favour of the new one stops compiling on types this step never mentions — including this guide's own
Step 2 sample, which is where it was captured:

```
error CS0246: The type or namespace name 'MemoryWrite' could not be found (are you missing a using
  directive or an assembly reference?)
```

One type did move *with* its seam rather than staying behind: `MemoryTick`, the value an age policy's
`Advance` returns, is now in `Lyntai.Memory.Interference`. Renaming the interface pulls that import in
anyway, so it only bites in a file that names the tick without naming the seam — a helper or a test fixture
that builds one:

```
error CS0246: The type or namespace name 'MemoryTick' could not be found ...
```

And there is a **third namespace the table above cannot list, because nothing in it was renamed**:
`Lyntai.Memory.Ranking`. Step 4 moves three properties off `GraphMemoryOptions` onto
`MultiplicativeRankingOptions`, which lives there together with `IMemoryRankingPolicy`,
`MultiplicativeRankingPolicy` and `ReciprocalRankFusionPolicy` — so a consumer who tuned `HopAttenuation` or
`RelativeFloor` needs `using Lyntai.Memory.Ranking;` as well, having renamed nothing at all. The full import
list for a file doing both halves is spelled out with the worked example at the end of this guide.

**If you construct `GraphMemoryEngine` by hand** rather than through `UseGraph`, its clock argument was
renamed *and* went plural in the same release, so the rename alone is not enough — two errors in sequence:

```
error CS1739: The best overload for 'GraphMemoryEngine' does not have a parameter named 'memoryClock'
error CS1503: Argument 3: cannot convert from 'Lyntai.Memory.Interference.ElapsedAgePolicy' to
  'System.Collections.Generic.IEnumerable<Lyntai.Memory.Interference.IMemoryAgePolicy>?'
```

`memoryClock: new ElapsedClock()` becomes `agePolicies: [new ElapsedAgePolicy()]` — a collection, because age
is one of the two seams that became plural (Step 5). Registering through DI is unaffected beyond the type
rename: the engine seeds its own default only when *nothing* is registered, so a single
`AddSingleton<IMemoryAgePolicy>` still replaces it exactly as `AddSingleton<IMemoryClock>` did.

#### Every constructor parameter renamed for the seam vocabulary, in one place

The seam rename above reached the *types* first and the *parameters* named after them only afterwards, so
three parameters moved in total. This is the complete list — there is no fourth:

| Type | Was | Is now | Is this a 2.5 → 3.0 step? |
|---|---|---|---|
| `GraphMemoryEngine` | `memoryClock:` | `agePolicies:` | **Yes** — and it went plural; see above |
| `GraphMemoryEngine` | `appraisers:` | `saliencePolicies:` | No — salience is new in 3.0 (Step 1's table) | <!-- drift-ok -->
| `ModulatedRetrievability` | `modulators:` | `retentionPolicies:` | No — retention is new in 3.0 (Step 1's table) | <!-- drift-ok -->

Only the first row can affect code written against 2.5, because a 2.5 consumer never had a salience or a
retention seam under any name. The other two rows are listed so the record is complete: they matter only if
you tracked a 3.0 pre-release build, where they were briefly `appraisers:` and `modulators:` — names left
over from the retired `ISalienceAppraiser` and `IRetentionModulator`. <!-- drift-ok -->

**All three are source-breaking only for a caller using NAMED arguments.** A positional call compiles
untouched — no parameter changed position, type or default — and so does every consumer who reaches these
types through DI (`AddLyntai`, `AddMemoryEngine`, `UseGraph`), which is the great majority. If you never
wrote `memoryClock:`, `appraisers:` or `modulators:` at a call site, this section costs you nothing. <!-- drift-ok -->

#### The one rename OUTSIDE the graph engine: `MemoryRetentionPolicy` → `MemoryEvictionPolicy` <!-- drift-ok: the rename is the subject -->

This one belongs to the **keyword store**, not the graph engine, and it is in this step because it is a pure
rename like the rest — do them in one pass.

| Was | Is now |
|---|---|
| `Lyntai.Storage.MemoryRetentionPolicy` | `Lyntai.Storage.MemoryEvictionPolicy` | <!-- drift-ok: the migration table -->
| `LyntaiOptions.MemoryRetention` | `LyntaiOptions.MemoryEviction` | <!-- drift-ok: the migration table -->
| `MemoryEvictionPolicy.Eviction` | `MemoryEvictionPolicy.Mode` |

Nothing else about it changed: same presets (`Default`, `Manual`, `CountCap`, `TimeToLive`, `SizeBudget`,
`Composite`), same defaults (a 500-entry per-scope FIFO cap), same `LYNTAI_MEMORY_*` environment variables,
same behaviour. The namespace is unchanged too, so no `using` moves.

<!-- compile-skip: a before/after pair — the 2.5 half names a type that no longer exists, which is the point -->
```csharp
// 2.5
services.AddLyntai(b => b.Options.MemoryRetention = MemoryRetentionPolicy.CountCap(200, MemoryEvictionMode.Lru)); // drift-ok
services.AddLyntai(b => b.ConfigureMemory(p => p.Eviction = MemoryEvictionMode.Lru));

// 3.0
services.AddLyntai(b => b.Options.MemoryEviction = MemoryEvictionPolicy.CountCap(200, MemoryEvictionMode.Lru));
services.AddLyntai(b => b.ConfigureMemory(p => p.Mode = MemoryEvictionMode.Lru));
```

**Why it moved, since a rename with no behaviour change always invites the question.** 3.0's graph engine
introduced `IMemoryRetentionPolicy`, a seam that *lengthens* a memory's half-life. This type *removes*
entries from a store. They sat one `I` apart, and in .NET `IFoo` reads as the interface of `Foo` — so the
two most opposite operations in the subsystem looked like an interface and its implementation. The storage
side moved because it was already surrounded by the other vocabulary (`MemoryEvictionMode`,
`MemoryEviction.Survivors`, `LYNTAI_MEMORY_EVICTION`). See `docs/DECISIONS.md` D13.

### Step 2 — Update anyone who *implements* one of those seams

If you only ever *registered* the shipped implementations, skip to Step 3 — this step is for a consumer
with their own `IMemoryClock`/`IMemoryAgePolicy` or `IRetrievabilityPolicy`/`IMemoryRetrievabilityPolicy`.

**`IMemoryAgePolicy` gained two required members, and its existing one changed shape** (none has a default
body — a 2.5-era implementation stops compiling until all three are dealt with). It needs both
`using Lyntai.Memory.Interference;` *and* `using Lyntai.Memory;`, per Step 1:

```csharp
sealed class MyAgePolicy : IMemoryAgePolicy
{
    public MemoryAgeKind Kind => MemoryAgeKind.Derivable;          // NEW — or .Accumulating
    public MemoryTick Advance(MemoryWrite write, string engine)    // CHANGED — 2.5 took the write alone
        => MemoryTick.One;
    public double Age(MemoryAgeSample sample) => sample.Ordinal;   // NEW — project age from the primitives
}
```

The `engine` parameter is the one a rename alone silently misses, because the method still *exists* under
the old shape — the compiler reports it as an unimplemented interface member rather than as a changed
signature, which sends you looking at the wrong thing:

```
error CS0535: 'MyAgePolicy' does not implement interface member
  'IMemoryAgePolicy.Advance(MemoryWrite, string)'
```

It carries the owning engine's name so a *stateful* policy can key its bookkeeping per engine: one
registered singleton shared by several named engines then measures each engine's own "since the last
write" rather than blending them. A stateless policy can ignore it, but it still has to accept it.

**`IMemoryRetrievabilityPolicy.Reinforce` now returns the full `MemoryDecayState`, not a `double`** — the
one genuinely non-mechanical part of this step, because a scalar return can only ever persist `Stability`,
and the fix is a one-line wrap, not a rewrite:

<!-- compile-skip: a before/after pair of the SAME member signature — two definitions of one member by construction -->
```csharp
// before (2.5)
double Reinforce(in MemoryDecayState state) => state.Stability * 1.5;

// after (3.0)
MemoryDecayState Reinforce(in MemoryDecayState state) => state with { Stability = state.Stability * 1.5 };
```

**`with`, never a fresh construction — this is the one fix in this step no compiler can check for you.**
`MemoryDecayState` gained two members in this window (`Signals`, carrying the write's open retention
signals; and `Difficulty`, the live 1-10 axis `DsrRetrievability` now maintains — Step 5), both trailing
and both defaulted, so `new MemoryDecayState(age, recalls, stability, strength, strengthAge)` still
compiles perfectly. It just returns a state whose `Difficulty` has been reset to the neutral `5` — and
`Difficulty` is one of the two members the engine reads straight back off your return value and writes to
the store (`Stability` is the other). A policy that rebuilds the record instead of copying it therefore
flattens the difficulty axis on **every single review**, with no error anywhere and no test of your own
likely to notice. `Signals` travels the other way, from the stored node into your policy, so rebuilding
costs you nothing there — which is exactly what makes the `Difficulty` half easy to miss.
`state with { … }` carries every member you did not name, including ones added after you wrote the line.

Two members also means the record's `Deconstruct` changed arity, which is the one place the addition is
*not* source-compatible — a positional deconstruction of five is now a compile error rather than a silent
drop, which is the safe direction:

```
error CS8129: No suitable 'Deconstruct' instance or extension method was found for type
  'MemoryDecayState', with 5 out parameters and a void return type
error CS7036: There is no argument given that corresponds to the required parameter 'Signals' of
  'MemoryDecayState.Deconstruct(out double, out int, out double, out double, out double,
  out MemorySignals, out double)'
```

**`IMemoryRetrievabilityPolicy` also gained a required `Provenance` member** (no default — answers "which
policy computed this entry's persisted state", design doc §5.7):

```csharp
public MemoryRetrievabilityProvenance Provenance => (MemoryRetrievabilityProvenance)(1L << 40);
// bits 0-31 are reserved for this library; bits 32-62 are open for a consumer's own policy; never bit 63
```

**It is validated, not merely declared** — a single, non-zero bit, unique among *different* policy types.
Satisfying the compiler with `MemoryRetrievabilityProvenance.None` (or with two bits, or with a bit another
registered policy already claims) fails when the engine is constructed, which under `AddLyntai` means the
first time the container resolves it:

```
System.ArgumentException: NoProvenancePolicy declares Provenance = None (0). Every REAL, running policy
  must declare its own non-zero bit — None is reserved for "nothing computed this," never a policy's own
  identity. (Parameter 'bits')
```

That is deliberate: `None` is the value a *row* carries to mean "no policy has ever computed this", so a
policy answering `None` would make every entry it touched indistinguishable from an untouched one.

**`DerivedGrade` is new but NOT required** — it has a default body (`=> null`), so a 2.5-era policy compiles
without touching it. Only override it if you want the review log (Step 5) to record a grade for your
policy's own reinforcements.

A minimal custom `IMemoryRetrievabilityPolicy` carrying all three changes, verified to compile against this
branch:

```csharp
sealed class ProbeRetrievabilityPolicy : IMemoryRetrievabilityPolicy
{
    public double InitialStability => 20;
    public MemoryRetrievabilityProvenance Provenance => (MemoryRetrievabilityProvenance)(1L << 40);

    public double Retrievability(in MemoryDecayState state) => Math.Pow(2, -state.Age / state.Stability);

    public MemoryDecayState Reinforce(in MemoryDecayState state) =>
        state with { Stability = state.Stability * 1.5 };

    public double CandidateCutoff(double minRetrievability) => -Math.Log2(minRetrievability) * 2000;
}
```

`IMemoryRetentionPolicy` and `IMemorySaliencePolicy` need no "before" here — see Step 1's table. If you are
implementing either for the first time, implement the interface as it ships today; there is no earlier
shape to reconcile.

### Step 3 — Update a custom `IMemoryGraphStore`

Skip this step entirely if you use the shipped SQLite, Postgres or InMemory graph store — all three already
implement everything below. This step is only for a consumer who rolled their own `IMemoryGraphStore`.

`IMemoryGraphStore` grew **five required members, none with a default body** — a custom store stops
compiling until all five are added:

- `DeleteAsync(engine, ids, ct)` — removes specific nodes by id. It exists because a bug in the pre-3.0
  prune path could rate an entry retrievable at recall time and still remove it at prune time after a policy
  swap (measured at 49 wrongful deletions in the scenario that found it) — once an age policy can *derive*
  its age rather than read a stored accumulator, the engine has to evaluate retrievability itself and
  delete precisely what it decided on, which a store-side ratio filter cannot express.
- `RecordReviewsAsync(engine, reviews, cap, ct)` and `ReviewsAsync(engine, ct)` — the review log (Step 5).
- `RecordSubjectsAsync(engine, nodeId, subjects, ct)` and `NodesBySubjectAsync(engine, taskKey, scope,
  subject, limit, ct)` — what a node is ABOUT, so entries concerning the same entity can be linked (Step 5).
  **If you do not want annotation, implement both as no-ops** — `Task.CompletedTask` and an empty list — and
  the engine behaves exactly as it does with no annotator registered. Subjects steer LINKING and never
  recall, so a no-op costs you nothing but the feature.
  <br>They are stored rather than searched for because searching cannot reach the case that matters: linking
  a fact to what a search for its subject finds needs some entry to name that subject in its own text, and
  "the spouse is Alice" / "the deploy key is in the vault" / "the client is northern logistics" are all about
  the same owner while none contains "owner". If you do implement them: record REPLACES the previous set for
  that node (a stale subject links future facts into the wrong cluster forever), match the subject
  case-insensitively, scope the lookup to the task, and drop a node's subjects when the node is deleted.

There is a **sixth** new member, and it is the one you will not be told about by the compiler:
`KnownSubjectsAsync(engine, taskKey, scope, limit, ct)` is the only 3.0 addition with a **default body** (it
returns an empty list), so a 2.5 store keeps compiling and silently takes that default. Nothing breaks —
the default is the honest "this store does not track subjects" answer, and it is exactly right if you
implemented the subject pair as no-ops above. Implement it only if you implemented `RecordSubjectsAsync`
for real: it reports which subjects a task has seen, which an annotator uses to reuse an existing subject
label instead of coining a near-duplicate. **Five members break your build; this one quietly limits a
feature**, which is why it is called out separately rather than listed with them.

Confirmed live against this branch: removing `DeleteAsync` from an otherwise-complete implementation
fails with `error CS0535: '...' does not implement interface member 'IMemoryGraphStore.DeleteAsync(...)'`
— adding it back compiles clean again. And measured against the `v2.5.0` tag rather than reconstructed:
that interface had exactly **eight** members, so 8 unchanged + 5 required + 1 defaulted = the **14** on the
3.0 interface.

A minimal stub showing all five new members (the other eight are unchanged in shape from 2.5):

<!-- compile-skip: a deliberately partial stub: it shows the five new members and elides the eight unchanged ones -->
```csharp
sealed class MyGraphStore : IMemoryGraphStore
{
    // ... UpsertAsync, SeedAsync, NeighboursAsync, GetAsync, TouchAsync, LinkAsync, PruneAsync,
    //     ForgetAsync — unchanged in shape since 2.5 ...

    // required, no default (memory-policy-seams plan): the engine decides WHICH nodes to remove
    public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        Task.FromResult(0);

    // required, no default (fsrs-properly plan Task 3): the review log
    public Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MemoryReview>>([]);

    // required, no default: what a node is ABOUT. Both may be no-ops — annotation simply stays off,
    // and the engine behaves exactly as it does with no annotator registered.
    public Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<long>>([]);
}
```

`GraphNode`, `GraphNodeWrite` and `GraphTouch` also each gained several trailing members (`Signals`,
`OrdinalAge`/`VolumeAge`/`ElapsedAge`, `ProvenanceRetrievability`/`ProvenanceSalience`, `Difficulty`, and on
`GraphNode` also `StrengthOrdinalAge`/`StrengthVolumeAge`/`StrengthElapsedAge`) — additive in source (every
existing positional construction still compiles, because C# bakes trailing default-parameter call sites in at
compile time) but binary-breaking, and source-breaking for anyone who positionally *deconstructs* one of these
records. A custom store should start populating and persisting the new fields; the shipped stores already do.

**Two of those are worth doing properly rather than leaving at their defaults**, because the engine reads them
to decide what to DELETE:

- **The three `Strength*Age` members** are the connection-age counterparts of `OrdinalAge`/`VolumeAge`/
  `ElapsedAge`: how much has happened since any of the node's edges was last strengthened, on each
  policy-independent scale. Stamp them on an edge when you strengthen it (the shipped stores add
  `strengthened_ordinal`/`strengthened_chars`/`strengthened_at` beside `strengthened_position`) and report
  the `MAX` across the node's edges. Leaving them at `0` tells the engine every connection was strengthened
  *just now*, which inflates retrievability and makes `PruneAsync` keep entries it should remove — the
  recoverable direction, but wrong.
- **`Relevance` for an authoritative node your query did not match must be `0`** — see Step 5.

**`GraphNeighbour` gained the same three, plus `EdgeAgeSample`** (`EdgeOrdinalAge`/`EdgeVolumeAge`/
`EdgeElapsedAge`) — the connecting edge's own age, as opposed to `GraphNode.StrengthAgeSample`, which is the
`MAX` across all of a node's edges. `NeighboursAsync` should report them. Leaving them at `0` tells the engine
every edge was strengthened just now, which keeps stale links ranking high during traversal.

### Step 3b — Update a custom `IJobStore`

Only if you implement `IJobStore` yourself. The shipped SQLite, Postgres and in-process stores already do
this. It is listed separately from Step 3 because it is a different seam in a different subsystem, and a
consumer with a custom memory store usually does not have a custom job store.

**`IJobStore` gains FOUR required members, none with a default body**: `PollAgainAsync(id, workerId, runAt,
ct)`, and the slot trio `TryAcquireSlotAsync(cap, workerId, lease, ct)` / `ReleaseSlotAsync(slotIndex,
workerId, ct)` / `HeartbeatSlotsAsync(workerId, ct)`. For `PollAgainAsync` the absence of a default is deliberate: the only body available would fall back to
the attempt-consuming retry path, leaving every BYO store silently carrying the very bug this change exists
to remove. A compile error is the point.

**The slot trio backs `JobOptions.GlobalMaxConcurrency`** — a job cap across every process sharing your
store (D73). **If you do not want to support it, return `null` from the acquire and make the other two
no-ops.** That is a visible refusal rather than a silently ignored limit: a host that sets a global cap
against such a store gets no jobs claimed at all, which is loud, instead of a cap that quietly does nothing.
To support it, add a table of `(slot_index PRIMARY KEY, worker_id NULL, acquired_at NULL)` and acquire the
lowest free-or-expired index below `cap` with the same atomic pattern your `ClaimNextAsync` already uses —
the shipped SQL is in `JobStoreSql.FreeSlotWhere` and the two store implementations.

**What it is for.** `JobOutcome.Poll` is new — *"not finished, and nothing went wrong; look again"*, which
the contract previously had no word for. A handler WATCHING a long-running operation elsewhere had to
express waiting as `JobOutcome.Retry`, and a retry SPENDS an attempt: at the default `MaxAttempts` of 3 a
durable render was submitted, polled twice, then dead-lettered as *"retries exhausted"*. At the 15-second
default poll delay that killed **any hosted render slower than about thirty seconds**, against a handler
whose own documentation promised to poll to completion across as many process lifetimes as it takes.

**Two things your implementation must get right**, both of which the shipped stores demonstrate:

- **UNDO the attempt increment `ClaimNextAsync` applied.** `Poll` is not bounded by the attempt counter at
  all, because a look at a healthy operation is not an attempt. The SQL backends do it with
  `attempts = attempts - 1`; the in-process store uses a floored subtraction so a store reached out of order
  can never report a negative count.
- **FENCE it on the worker id.** This is the one outcome that moves a job BACKWARDS, so an unfenced version
  would let a worker whose lease was already reclaimed reset another worker's job indefinitely.

Such a job now ends by cancellation, a deadline, or the handler returning `Fail` — never by exhausting
attempts it never spent. Pinned by a `JobStoreContract` fact wired to all three backends plus two runner
facts.

### Step 4 — Decide: keep the new defaults, or restore the old ones

This is the step every consumer meets, even one who configures nothing and has no custom policy or store.
**Three** registered defaults changed, one curve was deleted outright with no restore path, and one recall
behaviour changed for anyone who uses `MemoryGrade.Authoritative`. Read this step even if you did nothing
but call `AddMemory()` — the reinforcement default below changes what your existing store returns.

**An authoritative fact now takes a slot WITHIN a recall's `limit` instead of being cut by it.** Through
2.5.x, a recall re-admitted exact facts the query had not matched and then appended them *after* the ranked
set, where `Take(limit)` cut them off — so raising the grade on a fact did not, at a small limit, keep it. On
the corpus that measured this for the first time, **all three authoritative facts were lost in all five
languages.** Design §5.7.0 makes *never lose an authoritative fact* objective (1), the only objective with no
acceptable failure rate, so the ordering was wrong and not the promise.

You will see this only if you write entries with `MemoryGrade.Authoritative`. If you do, a recall with a
small `limit` now returns **fewer ordinary hits** — the exact facts displace them, which is what raising the
grade was always supposed to mean. Nothing about *admission* changed: which entries are candidates comes from
the grade carve-out and never from relevance.

The one thing worth deciding is the bound. The objection the old behaviour was defending against — one
authoritative entry evicting every ordinary hit — is real, so it is bounded rather than dismissed:

```csharp
// cap how many of a recall's slots exact facts may take; null (the default) means "as many as needed",
// and the recall's own limit caps it either way — this option can only ever reduce displacement
services.AddLyntai(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph(
    new GraphMemoryOptions { AuthoritativeReserve = 3 })));
```

With a cap set, the promise degrades to "an exact fact is displaced only by *another* exact fact" rather than
to nothing. **`AuthoritativeReserve = 0` restores the pre-3.0 behaviour exactly** — and re-breaks objective
(1), which is why it is not the default. No migration either way: the grade was always stored, and only the
ordering was wrong.

**`DsrRetrievability` is now the only shipped forgetting curve, and the registered default.**
`HalfLifeRetrievability` and `HalfLifeOptions` are **deleted — there is no restore path.** *("yes delete
it, its more like a defect now.")* Its own doc admitted its central `× 1.5` reinforcement constant was
"reasoned, not measured", and a later measurement found it compounds to **2.1×** a correctly-behaving
curve's stability over a four-touch reuse batch — over-crediting massed repetition, the exact behaviour
FSRS exists to correct. A consumer who genuinely needs that exponential shape has to implement
`IMemoryRetrievabilityPolicy` themselves (Step 2 shows the shape); this library does not ship it any more.
`MemoryRetrievabilityProvenance.HalfLife`'s bit is *retired*, not freed — every row a 2.5.x deployment
wrote still carries it, but no policy may declare it again.

Per Step 3's fact above, this needs **no data migration** — only a decision about whether the numbers you
tuned on `HalfLifeOptions` need an equivalent on `DsrOptions`. They are not a 1:1 mapping (DSR is a
different curve with its own three stability-increase laws), so this is a genuine decision, not a
mechanical rename:

```csharp
// one-line adoption of the new default, with your own tuning
services.AddSingleton<IMemoryRetrievabilityPolicy>(
    new DsrRetrievability(new DsrOptions { InitialStability = 14 }));
// ...or register your own IMemoryRetrievabilityPolicy — before AddLyntai or after, either direction wins
```

**A recall no longer lengthens a memory's half-life — `DsrOptions.ReinforceGain` ships at `0`.** This is the
largest behavioural change in the release for a consumer who configures nothing, and the only default 3.0
sets on measured evidence rather than on a shape argument (`docs/DECISIONS.md` **D54**).

**Be precise about what changed, because it is not a value you can put back.** 2.5.x had no `DsrOptions` at
all: it shipped `HalfLifeRetrievability`, whose `HalfLifeOptions.ReinforceFactor = 0.5` multiplied stability
by **1.5** on every recall. That curve is deleted (above), so the comparison is between two different curves
rather than between two settings of one. `ReinforceGain = 2.0` was a development default inside the
unreleased 3.0 window and **was never released** — if you read that number somewhere, it is not a version
you can have been running.

The age reset a recall performs is unchanged — an entry you keep coming back to still stops looking stale.
What is gone is the PERMANENT stability growth on top of it. On the fixed-corpus pin the new default reads
`miss 0.234 → 0.103` (a 56% reduction) with pollution also slightly better, and it won on every one of six
corpus shapes across thirty paired seeds. Every alternative was built and lost, not just the shipped rule: a
capped variant, and one computing stability purely from an entry's recall COUNT so it cannot compound by
construction, both lost to simply not growing.

The mechanism, because it decides whether you want growth at all: what a recall is worth comes from the age
reset, which EXPIRES — the entry decays again at its own rate. A permanent half-life increase instead banks
the ranking policy's own errors, so an entry wrongly returned becomes more retrievable and is more likely to
be returned wrongly again. Durability has not gone away; it moved to properties of the material and the
graph (write-time salience, and how connected an entry is) rather than of what this engine's own ranker
chose to return.

```csharp
// turn the growth arm back ON, if your deployment measures better with it. This is NOT a 2.5 restore —
// 2.5's curve is gone; this asks the 3.0 curve for the behaviour 2.5's had.
services.AddSingleton<IMemoryRetrievabilityPolicy>(
    new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }));
```

FSRS's three stability-increase laws are kept, not deleted — only the gain is zero. The measurement behind
that is one synthetic corpus, so a deployment with real logged reviews may well find its own value.

**Every `DsrOptions` field is now domain-guarded, and five of them started throwing in 3.0.** `MaxStability`,
`ConnectionBoost`, `MaxConnectionBoost`, `EdgeHalfLife` and `ReinforceGain` joined the guarded half of the
record (`docs/task-archive.md` Part 54, DSR3), so an out-of-domain value is an `ArgumentOutOfRangeException` **at the line
that configured the policy** rather than a wrong answer surfacing deep in the recall path. The domains are
`MaxStability > 0`, `ConnectionBoost >= 0`, `MaxConnectionBoost >= 1`, `EdgeHalfLife > 0`,
`ReinforceGain >= 0`, and every one of them additionally rejects `NaN` and both infinities. This is a
behaviour change you will notice only if you were passing one of those values, and the one worth calling out
is `MaxStability`: a `NaN` there propagated through `Math.Min` and was **written back to the store** by
`GraphMemoryEngine`, where a `NaN` stability compares false against every threshold — so the entry neither
ranked, nor pruned, nor reported as broken. A throw at construction is the intended replacement for that
silence. Note also that `MaxConnectionBoost` below `1` and `EdgeHalfLife` at `0` were never actually in force
before (both readers floored the first; the second read as "no decay at all"), so a rejected value there was
already not doing what it said.

**`MaxStability` now caps GROWTH instead of CUTTING, and that is a behaviour change with data behind it.**
Through 2.5.x, `Reinforce` ended in a bare `Math.Min(grown, MaxStability)`, so recalling an entry whose STORED
stability already exceeded the ceiling wrote the ceiling back — a stored `100000` came back as `2000`, a 50×
shortening, in violation of `IMemoryRetrievabilityPolicy.Reinforce`'s own "never smaller than the current one"
guarantee (`docs/task-archive.md` Part 54, DSR2). In 3.0 the clamp is floored at the entry's own stability, so
such an entry is **frozen** — it can no longer grow — rather than truncated. You will see this only if some
stored stability is above your configured ceiling, which happens when you LOWER `MaxStability` under an
existing corpus or write a stability from outside the policy. Nothing to configure, and no migration: the
affected rows simply stop being shortened on their next recall.

**`ReciprocalRankFusionPolicy` is now the registered default ranking policy**, replacing
`MultiplicativeRankingPolicy` (owner ruling, 2026-08-11 — *"lets use the best so RRF for ranking"*).
Unlike the curve above, **this one has a one-line restore**, because `MultiplicativeRankingPolicy` is not
disqualified, it simply lost a measured comparison on one corpus class and stays fully shipped:

```csharp
// the one-line restore for a consumer who wants MultiplicativeRankingPolicy back as the default
services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy());
```

**`GraphMemoryOptions` lost `HopAttenuation`, `RelativeFloor` and `SalienceRankWeight` — moved onto
whichever ranking policy you use, not deleted.** (Only the first two are a 2.5 concern: `SalienceRankWeight`
was both added and moved on within this same window, so a 2.5 consumer never had it to lose.) If you tuned
either, you have the same decision as the curve above: adopt RRF's own knobs (`K`, `RelevanceWeight`,
`RetrievabilityWeight`, `SalienceWeight`, `HopWeight`, its own `RelativeFloor`), or keep your exact numbers
by restoring `MultiplicativeRankingPolicy` explicitly — note the third import, `Lyntai.Memory.Ranking`,
which is where all three of these types now live:

<!-- compile-skip: the `before (2.5)` half names members 3.0 removed — failing to compile against 3.0 is the point -->
```csharp
// before (2.5) — using Lyntai.Memory;
services.AddLyntai(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph(
    new GraphMemoryOptions { HopAttenuation = 0.6, RelativeFloor = 0.05 })));

// after (3.0) — same numbers, explicitly restoring the formula they belong to
// using Lyntai.Memory; using Lyntai.Memory.Ranking;
services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy(
    new MultiplicativeRankingOptions { HopAttenuation = 0.6, RelativeFloor = 0.05 }));
services.AddLyntai(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph(new GraphMemoryOptions())));
```

**Copying your old numbers across can now throw at startup, where 2.5 accepted them in silence.** The three
properties did not merely move — they arrived somewhere that validates them, in the property `init` itself,
so a bad value fails on the `new MultiplicativeRankingOptions { … }` line during registration rather than
producing a quietly broken ranking on every recall for the rest of the deployment's life. 2.5's
`GraphMemoryOptions` had plain `{ get; init; }` accessors and took anything a `double` could hold.

| Property | Accepted in 3.0 | Default | Now refused |
|---|---|---|---|
| `HopAttenuation` | finite, `(0, 1]` | `0.5` | `0`, anything negative, anything above `1` |
| `RelativeFloor` | finite, `[0, 1)` | `0.02` | anything negative, `1` or above |
| `SalienceRankWeight` | finite, `>= 0` | `0` | anything negative |

`NaN` and both infinities are refused everywhere. Each bound exists because the value past it *inverts* the
knob rather than merely exaggerating it: `HopAttenuation = 0` deletes every candidate beyond the seed
outright instead of attenuating it, above `1` a farther candidate outranks a direct hit, and a negative base
makes `Math.Pow` flip the score's sign by hop parity; `RelativeFloor` at `1` reaches the very best score that
defines it, so only exact ties survive; a negative `SalienceRankWeight` ranks a *more* salient entry below a
neutral one.

**The sample values above, `HopAttenuation = 0.6` and `RelativeFloor = 0.05`, are both legal** — as are
2.5's own defaults, `0.5` and `0.02`, so a consumer who tuned nothing has nothing to check. The realistic
casualty is `HopAttenuation = 0`, a plausible 2.5 setting for "do not spread past the seed at all", which
now fails loudly instead:

```
System.ArgumentOutOfRangeException: MultiplicativeRankingOptions.HopAttenuation must be a finite number
  in (0, 1] — see the property's XML doc for why zero eliminates every candidate beyond the seed, why a
  value above 1 makes a farther candidate outrank a direct hit, and why a negative value flips the sign
  of the result by hop parity rather than merely weakening it. (Parameter 'value')
```

If that is you, `Hops = 0` on `GraphMemoryOptions` expresses the same intent and is not validated, because
it is a count rather than a factor. `ReciprocalRankFusionOptions` validates on the same pattern, so the
same warning applies to the RRF knobs below.

**If you adopt RRF instead, its `RelativeFloor` defaults to `0`, not `0.02`, and burial is effectively
inert at the default** — RRF compresses its own score range tightly enough that a floor copied from
`MultiplicativeRankingOptions` would almost never cut anything. A candidate ranked worst on every signal
scores `(K + 1) / (K + n)` of the best-ranked candidate, so at the shipped `K = 60` with `n = 40`
candidates the worst sits at `0.61×` the best — a `0.02` floor cannot reach it, and it would only start to
bite past roughly 2990 candidates. If you want floor-based burial under RRF, compute a floor from that
formula for your own `K` and expected candidate count; do not reuse `0.02` and expect the same effect.

**`GraphMemoryOptions.Decay` (the deleted curve's own `HalfLifeOptions`) is gone too — one field moves, the
rest have no replacement.** `EdgeHalfLife` — the only field on it that ever belonged to the *engine* rather
than the curve — moved to a new top-level property with the same default (`100`):

```csharp
new GraphMemoryOptions { EdgeHalfLife = 60 }
// was: new GraphMemoryOptions { Decay = new HalfLifeOptions { EdgeHalfLife = 60 } }
```

The other five fields `HalfLifeOptions` carried (`InitialStability`, `ReinforceFactor`, `MaxStability`,
`ConnectionBoost`, `MaxConnectionBoost`) governed the deleted curve's own arithmetic and went with it —
`DsrOptions` has its own versions of most of these names, but they are a different curve's constants, not
a renamed copy.

### Step 5 — New capabilities that need nothing from you

Everything in this step already defaults to the behaviour you had: the review log ships **opt-out**, the
new selection points are **opt-in**. Skip it unless you want to change the default behaviour — or unless
you *deconstruct* `MemoryQuery` positionally, which is the one line in it that will not compile.

**Age and salience became *plural*** (`GetServices`, not `GetService`) — several coexisting policies
rather than one swappable one. This changes nothing for a consumer registering zero or one of either;
composing a one-element list is the identity. **It is a real, ordering-dependent behaviour change only for
`IMemorySaliencePolicy`, and only if you register your own:**

- Registered **before** `AddLyntai` (or `AddMemoryEngine`) — your salience policy *replaces* the shipped default
  outright (`GraphMemoryWiringTests.UseGraph_lets_a_consumer_registered_salience_policy_win_over_the_default`).
- Registered **after** — the default is already seeded, so yours *adds alongside it*; both run, composed
  by whichever `IMemorySalienceCompositionPolicy` is registered
  (`GraphMemoryWiringTests.UseGraph_a_consumer_registered_salience_policy_added_AFTER_AddLyntai_coexists_with_the_default_instead_of_replacing_it`).

A consumer who wants a pure replacement registers before `AddLyntai`, in either direction relative to other
calls. Age is the one that does *not* carry this asymmetry: nothing seeds a default `IMemoryAgePolicy` into
the container, so the engine falls back to its own burst-damped per-write policy exactly when the collection
is empty — one registration still replaces it, whichever side of `AddLyntai` it lands on. Registering *two*
is what is newly possible, and two **accumulating** ones are refused at engine construction with an
`ArgumentException`: the store's position accumulator is a single number and cannot carry two
path-dependent histories at once without silently blending them.

**Reinforcement's two effects became separable**, via `GraphMemoryOptions.Reinforcement`
(`MemoryReinforcementEffects`). A recall does two things to what it returned — resets the entry's age on
every scale, and writes back the stability the curve grew — and 2.5 welded them into one store round-trip.
The default is `All`, so a consumer who sets nothing sees no change:

```csharp
// the best-measured configuration: returning something keeps it alive, but does not entrench it
services.AddLyntai(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph(
    new GraphMemoryOptions { Reinforcement = MemoryReinforcementEffects.AgeReset })));
```

The age reset is what keeps a rarely-queried critical fact alive; the growth is what entrenches whatever the
ranker already returned, since nothing here observes whether the return was *correct*. You could already
reach this through `DsrOptions.ReinforceGain = 0`, but only on the shipped curve — the option asks for it
policy-independently, so it survives you swapping the curve. **`StabilityGrowth` without `AgeReset` throws**:
the store resets the age as part of the same write, so that combination would apply neither effect.

**And which CALLS reinforce is now selectable too**, via `GraphMemoryOptions.ReinforceOn`. The two compose —
the acts you select apply the effects you selected. Default `All`, unchanged:

```csharp
// if your application calls ExpandAsync (including via AddMemoryTools), this is the measured better setting
services.AddLyntai(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph(
    new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.Expansion })));
```

`ExpandAsync` is a caller choosing to pay for full content — the closest thing this library observes to a
*verified* retrieval — while a recall reinforces whatever the ranker returned, mistakes included. Reinforcing
only expansions beat the default on both miss and pollution in every measured cell (`docs/DECISIONS.md`
**D58**). **The default did not move because an application that never expands would then reinforce nothing
at all**, which measured worse than the default on pollution — so this is worth setting deliberately if you
expand, and worth leaving alone if you do not.

**A model can now judge which recalled entries actually ANSWERED the query** — `AddMemoryVerification`, and
it is the largest recall-quality change in this release. Nothing happens unless you register it:

```csharp
services.AddLyntai(cfg => cfg
    .AddOllamaProvider(defaultModel: "qwen2.5-vl:7b")
    .UseDefaultCandidates("ollama")
    .AddMemoryVerification());
```

The reason it exists is a measurement, not a hunch. Decomposing a full corpus replay: of the relevant entries
a recall failed to return, **100% were reachable candidates that the ranking put below the limit** — none
were unreachable. So the miss rate is a ranking failure end to end, and the two shipped model-free ranking
policies return byte-identical results, meaning there was no fix available inside the library's own
arithmetic.

Measured effect on that corpus, with all 145 judgement calls answered: `qwen2.5-vl:7b` takes miss from
`0.5357` to `0.3071` and pollution from `0.3331` to `0.1556` — 91.4% of what a perfect oracle judge achieves.
A 3B model reaches ~60–69% and varies between runs. **Recall quality tracks the judge's capability**, and
which model to use is yours to choose; the library takes no position.

It costs one model call per recall, in the latency path of an answer. A judgement never removes a result
unless you also set `GraphMemoryOptions.VerificationFilters`, authoritative material is exempt whatever the
verdict, and any failure leaves the ranking untouched rather than reporting "nothing was relevant".

**Ranking became selectable per engine and per call.** `UseGraph` gained a `ranking:` parameter (this named
engine's own policy, overriding the container registration for it alone) and a `namedRankingPolicies:`
dictionary of alternates; `MemoryQuery` gained a matching `RankingPolicyName` for picking one of those
alternates on a single recall. Both are opt-in and default to exactly the pre-3.0 behaviour — an engine that
names neither reads the container registration, and a query that names no policy uses the engine's own. Two
things to know if you take it up: a name the engine does not recognize is an **error, never a silent
fallback to the default**, and the catalog is scoped to one named engine, so a name meaningful on one is
simply unknown on another.

**The forgetting curve became selectable per engine too** (`docs/DECISIONS.md` D50). `UseGraph` gained a
`policy:` parameter — this named engine's own `IMemoryRetrievabilityPolicy`, overriding the container
registration for it alone — so one process can now run two graph engines on two curves, which was
inexpressible before. It sits **after `namedRankingPolicies:`** — fifth of seven, with `annotation:` and
`verification:` added after it later in the same window — so every 2.5 positional call still binds to the
same parameters; and `policy: null` resolves exactly as it always did (the
container's registration, else `DsrRetrievability`), so an engine that names nothing sees no difference.
Selecting a curve selects the CURVE and nothing else: the policy you pass is still wrapped in retention
modulation over the registered `IMemoryRetentionPolicy` collection, the same as the resolved default.

```csharp
services.AddLyntai(cfg => cfg
    // the container's curve (or the shipped default, when nothing is registered)
    .AddMemoryEngine("chat", e => e.UseGraph())
    // this engine's own — any IMemoryRetrievabilityPolicy, not only a retuned shipped one
    .AddMemoryEngine("docs", e => e.UseGraph(
        retrievability: new DsrRetrievability(new DsrOptions { InitialStability = 90 }))));
```

`RankingPolicyName` is `MemoryQuery`'s sixth positional member, appended after `CharBudget`, so every
existing construction still compiles — but, like `MemoryDecayState` in Step 2, a positional deconstruction
of five no longer binds:

```
error CS7036: There is no argument given that corresponds to the required parameter 'RankingPolicyName'
  of 'MemoryQuery.Deconstruct(out string, out string?, out string?, out int?, out int?, out string?)'
```

**An authoritative fact your query did not match now reports `GraphNode.Relevance` 0, on every backend.**
2.5 left this explicitly backend-specific and the three genuinely disagreed — SQLite's full-text path put such
a row at the tail of its gradient, its substring-fallback path at the head, Postgres at the head, the
in-process store at a flat 1 — so the same data and query produced a different recall ORDER per backend.
Nothing to configure. It affects you only if you **assert on relevance values** in your own tests, or if you
have a custom `IMemoryGraphStore` (Step 3), which must now report `0` for a node it admitted by GRADE rather
than by matching. Admission itself is unchanged and still guaranteed, by the grade carve-out in `SeedAsync`
and by the engine's own re-admission of authoritative candidates — relevance was never what carried it.

**Keyword recall now matches TERM-WISE on every backend, where only SQLite's full-text path did before**
(`docs/DECISIONS.md` D55). Nothing to configure. It matters most if you run **Postgres or the in-process
store**, where a multi-word cue previously had to appear in an entry *verbatim* to match anything — so
`"what is the spouse called"` found the fact on SQLite and nothing at all on the other two. Those two
backends will now return results where they used to return none, which is the point; the trade is that an
`OR` finds strictly more than a substring, so expect a wider candidate set. Ordering absorbs some of that:
the non-full-text paths now lead with how many terms a row matched, the coarse stand-in for SQLite's `bm25`.
You only need to act if you **assert on exact recall sets** in your own tests.

**And recall now works in Chinese, Japanese and Korean, by default.** Splitting on whitespace hands back a
whole CJK sentence as ONE token, so before 3.0 such a query could only match an entry containing that exact
substring — English got OR-over-words and CJK got exact-phrase-or-nothing, decided purely by whether the
language writes spaces. A spaceless run is now expanded into character trigrams, the unit both backends
already index (SQLite `tokenize='trigram'`, Postgres `pg_trgm`). **No per-language setup, no switch to
enable, no segmenter to install** — if you store non-Latin text, upgrading is the whole action. Two limits
worth knowing: the floor is three characters in any script (a two-character overlap is below what a trigram
index can match, and falls through to a plain substring scan as before), and an ASCII word is deliberately
*not* expanded, since a whole word is more precise than its trigrams.

**`MemoryDecayState` gained `Difficulty`** (neutral value `5`, the scale's mid-point).
`DsrRetrievability.Reinforce` now maintains it on every review — deriving a stand-in grade from
retrievability at recall, since nothing in this library grades a recall the way a human grades a flashcard
— where it used to only read the signal and discard the update. <!-- drift-ok --> Nothing to configure;
this is disclosed as a partial, unfitted signal (`docs/DECISIONS.md` D49), not a tuned one.

**Every reinforcement is now logged** — the pre-review state, the grade actually used, and the post-review
state — to a bounded, per-engine table (`GraphMemoryOptions.LogReviews`, default `true`;
`.ReviewLogCap`, default `10_000`). It exists so a future parameter-fitting task has real data to read
(`TASKS.md` Part 56 FSRS-B); nothing in this engine's recall, ranking or pruning path reads it back — proved
directly, not by omission, by
`GraphMemoryReviewLogTests.The_review_log_never_feeds_recall_ranking_or_pruning`, which pollutes the table
with wildly divergent rows and asserts recall and prune are byte-identical either way. Opt out if you never
intend to fit parameters against it:

```csharp
new GraphMemoryOptions { LogReviews = false }
```

### Step 6 — generation backends take a configure callback

Only for a consumer of the `Lyntai.Generation` package. Every `Add*Provider` now takes
`Action<TOptions> configure` instead of a constructed options object, matching
`AddOpenAiCompatibleProvider(id, o => …)` on the LLM side and `AddMemoryEngine(name, e => …)` above.

<!-- compile-skip: a before/after pair — the "before" is the 2.5 API and cannot compile here -->
```csharp
// 2.5
.AddOpenAiImageProvider(new OpenAiImageOptions { BaseUrl = "https://api.openai.com/v1", ApiKey = key })
.AddAutomatic1111Provider(new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" })

// 3.0 — every option has a sensible default, so set only what differs
.AddOpenAiImageProvider(o => { o.ApiKey = key; })
.AddAutomatic1111Provider(o => { })
```

The compiler finds every site: the parameter type changed, so a 2.5 call fails with `CS1503` naming the
options type. There is no silent-behaviour-change risk here — it is the mechanical kind.

**Two details worth knowing rather than discovering.** The three `required BaseUrl` members are no longer
required and default to the URL their own documentation always named (`http://127.0.0.1:7860`,
`http://127.0.0.1:8188`, `https://api.openai.com/v1`) — so a registration that previously *had* to state a
base URL may now silently take the default instead of failing to compile. If you were relying on `required`
to catch an unset URL, that guarantee is gone; a blank one still reports `NotConfigured` at render time,
which is what it always did. And the four HTTP options types are now mutable classes rather than records, so
`with` expressions and value equality on them no longer compile.

### Step 7 — the renames, and three seams that break a BYO implementation

Mechanical, and a compile error names every site — which is why it is last. Nothing here changes behaviour;
`docs/DECISIONS.md` **D66** has the reasoning and the list of names deliberately left alone.

**Renames.** Every one is source-level:

| 2.5 / early-3.0 name | 3.0 name | Affects you if |
|---|---|---|
| `IProviderInstallation` | `IProviderProbe` | you type-test a provider for probe support |   <!-- drift-ok: a rename table NAMES the retired spelling -->
| `MemoryEngineBuilder.Reserve(n)` | `.ReserveCharacters(n)` | you set the prompt reserve on a blend |   <!-- drift-ok: a rename table NAMES the retired spelling -->
| `MemoryCompositionOptions.AuthoritativeReserve` | `.AuthoritativeCharacters` | you construct that options record |
| `GraphMemoryEngine(policy:)` / `UseGraph(policy:)` | `retrievability:` | you pass the curve by NAME |
| `CuratedMemorySections(task:)` | `taskKey:` | you pass that argument by name |
| `MemoryProvenance.EnsureEachBitIsSingleRealAndUnique` | `.ValidateProvenanceBits` | you implement a provenance-declaring policy |   <!-- drift-ok: a rename table NAMES the retired spelling -->
| `IMemoryRetentionCompositionPolicy.Compose` | `.StabilityFactor` | you implement a retention composition |   <!-- drift-ok: a rename table NAMES the retired spelling -->
| `IMemorySalienceCompositionPolicy.Compose` | `.Signals` | you implement a salience composition |   <!-- drift-ok: a rename table NAMES the retired spelling -->
| `SummedAgeComposition`, `MultiplicativeRetentionComposition`, `MaximalSalienceComposition` | the same names + `Policy` | you name one of the shipped composition policies |   <!-- drift-ok: a rename table NAMES the retired spelling -->
| `LocalDiffusionOptions.Strength` | `.DenoisingStrength` | you set it — **see the warning below** |
| `UseDefaultGenerationCandidates(candidates:)` | `providerIds:` | you pass that argument by name |

**`LocalDiffusionOptions.Strength` is the one rename a recompile will NOT catch.** Every other row is a
type, an interface member or a named argument, so the compiler names the site. That one is a settable
property on a plain class — the shape `IConfiguration.Bind` / `Configure<T>` targets — and config binding
**ignores unknown keys**. An app with `"LocalDiffusion": { "Strength": 0.35 }` in `appsettings.json` builds
clean, starts clean, and silently renders at the default instead. Grep your configuration for it.

**Two members are now `internal`**, neither of which had an external caller: `MemoryEngineComposition` (a DI
carrier record) and `BudgetedGenerationRouter.RecordAsync`. If you were calling the latter, record generation
spend through `IUsageTracker` directly.

**Three seams break a BYO implementation.** All three are compile errors, none is silent:

- **`ICliProviderDialect.BuildCompletionArgs` takes a second parameter**,
  `IReadOnlyList<string> toolHostArgs`. Your dialect decides where they go: append them if your argv ends in
  OPTIONS (`[.. mine, .. toolHostArgs]`, which is what `ClaudeCliDialect` does), or place them earlier if it
  ends in a positional. This exists because appending is wrong for `codex`, whose argv ends in `-` and which
  reads anything after it as prompt text — where a swallowed flag costs a turn rather than erroring (D65).
- **`IForgettableMemory` is now FORGET-ONLY, and `PruneAsync` lives on the new `IPrunableMemory`** (D72).
  A custom engine keeps compiling for `ForgetAsync`; add `IPrunableMemory` to its declaration to keep serving
  prunes, and the compiler names every site. **If your engine can honestly do only one, implement only one**
  — that is the point of the split. A vector store forgets a (task, scope) exactly and cannot prune by age at
  all; under the old combined interface it had to lie about one or give up the other.
  <br>`ForgetAsync` removes a whole (task, scope) and must be COMPLETE — if your engine cannot express a null
  scope ("every scope of the task"), THROW rather than forgetting one or none, because a consent withdrawal
  that silently does less than it says is the failure this surface exists to prevent. `PruneAsync` removes a
  qualifying subset and is best-effort — but **a criterion you cannot express must remove NOTHING rather than
  be ignored**, since pruning on the criteria you have while dropping the ones you do not deletes MORE than
  was asked for.
  <br>A composite still refuses to remove unless every member holding user content can serve the verb (D63) —
  now checked per VERB, so a member that forgets but does not prune no longer half-succeeds and then throws.
- **Which members a blend-level REMOVAL visits is now a policy, `IMemoryRemovalPolicy`** (D72, named per D76). **Nothing to do
  unless you disagree with the default**, which excludes an authoritative-ONLY member — a curated catalogue
  by construction, whoever wrote it — and includes everything else. That reproduces the conventional roles of
  the shipped engines.
  <br>Register your own when your arrangement differs: `services.AddSingleton<IMemoryRemovalPolicy, MyPolicy>()`
  is the whole opt-in. It is asked per member AND per kind, so "keep the glossary out of an automatic prune
  but include it in an explicit consent withdrawal" is expressible.
  <br>**Eligibility is deliberately NOT the same question as the capability interfaces.** A policy decides
  what is IN SCOPE; `IForgettableMemory`/`IPrunableMemory` decide what a store CAN do. A policy that includes
  a member which cannot serve the verb still gets the loud refusal — otherwise one permissive policy could
  silence a genuine gap, and a partial remove would report success.
- **`IJobStore` gains `PollAgainAsync` plus the two slot members** — Step 3b above.

**One behaviour change with no compile-time signal**: `GenerationRouter` no longer lets a backend's
exception escape. A caller that today catches one out of `GenerateAsync`/`SubmitAsync` will instead receive a
result carrying a verdict (D64). Your own cancellation still propagates.

### Step 8 — a custom `IGenerationRouter` gains a third door

Skip this unless you **implement `IGenerationRouter` yourself**. Using the built-in one, or decorating it
with `AddGenerationUsageBudget()` / `AddGenerationRateLimit()`, needs nothing — those ship implemented.

`IGenerationRouter.StreamAsync` is a required member with no default body, so a hand-written router stops
compiling until it has one. That is deliberate: a default body would have let a BYO router silently keep the
old behaviour, and the old behaviour is the defect — a backend advertising `GenerationDelivery.Stream` was
unreachable through the platform, because the capability pre-filter was only ever asked about `Inline` and
`Job`.

<!-- compile-skip: the member as it appears on the interface — a partial signature, not a standalone unit -->
```csharp
IAsyncEnumerable<GenerationChunk> StreamAsync(
    IReadOnlyList<GenerationCandidate> candidates,
    GenerationRequest request,
    CancellationToken ct = default);
```

**If you do not want to support streaming**, the honest implementation is one terminal chunk — not an
exception, because a caller writing `await foreach` should learn this the way they learn about any other
refusal:

<!-- compile-given: using System.Runtime.CompilerServices; using Lyntai.Generation; using Lyntai.Generation.Routing; -->
```csharp
public sealed class MyRouter : IGenerationRouter
{
    public Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
        CancellationToken ct = default) => throw new NotImplementedException("your inline door");

    public Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
        CancellationToken ct = default) => throw new NotImplementedException("your submit door");

    public async IAsyncEnumerable<GenerationChunk> StreamAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return GenerationChunk.Failure(
            GenerationVerdict.Unsupported, "this router does not serve streaming delivery");
    }
}
```

**If you do**, three rules make a streaming router correct, and two of them are not yours to choose — they
are the LLM router's measured invariants, transferred because the failure they prevent (splicing two
responses into one stream) is identical whether the bytes are tokens or audio:

1. **No fallback after commit.** Once you have yielded a chunk carrying real data, the caller holds bytes you
   cannot take back. Every later chunk passes through and the stream ends; do not try another candidate.
2. **Only real data commits.** The gate is `Data` being non-empty. A metadata-only opening chunk — a
   `MediaType` announcement — must NOT commit, or a backend that announces and then dies strands the caller
   on it. You are the trust boundary; a third-party backend may open that way.
3. **Emit exactly one terminal chunk, last.** A backend that just stops is not the caller's problem to
   diagnose: close it yourself with `GenerationChunk.Completed()` if data was produced and
   `GenerationChunk.Failure(...)` if none was. This is the rule that lets a consumer's `await foreach` end
   without asking whether the media finished or the process died.

`docs/DECISIONS.md` **D67** has the reasoning, and `GenerationRouterStreamTests` is the executable version of
all three.

**Decorating rather than replacing?** Apply your policy on all three doors. A decorator that forwards
`StreamAsync` straight through makes streaming the cheapest way around whatever it enforces — the
`.claude/knowledge/pitfalls.md` § "Second doors" shape, which this library has now paid for three times.

## A worked upgrade, before and after

One realistic `AddLyntai` block, close enough to real usage to diff your own against. Both halves below
were compiled for real — the "before" half against the actual `v2.5.0` tag, the "after" half against this
branch — not invented from reading the CHANGELOG. **The imports are part of what was compiled, and are
shown for that reason**: the "after" half is three statements drawing on five namespaces, where 2.5's two
statements needed three, and a guide that shows only the statements hands you a `CS0246` it never mentions.

<!-- compile-skip: the BEFORE half, compiled against `v2.5.0` by its author; against 3.0 it cannot compile by construction -->
```csharp
// ============================================================
// BEFORE — compiles against v2.5.0
// ============================================================
using Lyntai;                             // UseSqliteStorage, AddMemoryEngine
using Lyntai.Memory;                      // IMemoryClock, ElapsedClock, GraphMemoryOptions
using Microsoft.Extensions.DependencyInjection;   // AddLyntai, AddSingleton

services.AddSingleton<IMemoryClock>(new ElapsedClock());
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("app.db")
    .AddMemoryEngine("project", e => e.UseGraph(new GraphMemoryOptions
    {
        Hops = 3,
        HopAttenuation = 0.6,
        RelativeFloor = 0.05,
    })));
```

```csharp
// ============================================================
// AFTER — compiles against 3.0
// ============================================================
using Lyntai;                             // UseSqliteStorage, AddMemoryEngine — unchanged
using Lyntai.Memory;                      // GraphMemoryOptions — STAYS, do not drop this one
using Lyntai.Memory.Interference;         // IMemoryAgePolicy, ElapsedAgePolicy — Step 1's rename
using Lyntai.Memory.Ranking;              // IMemoryRankingPolicy, MultiplicativeRanking{Policy,Options}
using Microsoft.Extensions.DependencyInjection;   // AddLyntai, AddSingleton

services.AddSingleton<IMemoryAgePolicy>(new ElapsedAgePolicy());
services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy(
    new MultiplicativeRankingOptions { HopAttenuation = 0.6, RelativeFloor = 0.05 }));
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("app.db")
    .AddMemoryEngine("project", e => e.UseGraph(new GraphMemoryOptions { Hops = 3 })));
```

Three changes, each traceable to a step above: `IMemoryClock`/`ElapsedClock` → `IMemoryAgePolicy`/
`ElapsedAgePolicy` (Step 1, mechanical); `HopAttenuation`/`RelativeFloor` move off `GraphMemoryOptions`
onto an explicitly-registered `MultiplicativeRankingPolicy`, because this consumer tuned them and wants to
keep the exact numbers rather than adopt RRF (Step 4, a decision, resolved here in favour of "keep the old
behaviour"); `GraphMemoryOptions` itself keeps `Hops` unchanged, because that property never moved.

Two of the five imports are the point. `Lyntai.Memory.Ranking` is named in no rename table anywhere in this
guide, because nothing in it was renamed — it is simply where the moved properties landed, and it is the
import a reader is most likely not to think of. And `Lyntai.Memory` **stays**: Step 1 adds
`Lyntai.Memory.Interference`, it does not swap one for the other, and swapping loses `GraphMemoryOptions` on
the last line of a block that had otherwise been fixed correctly. Getting either wrong produces exactly
this, captured by compiling the "after" block with only the imports a replacement reading would leave:

```
error CS0246: The type or namespace name 'IMemoryRankingPolicy' could not be found ...
error CS0246: The type or namespace name 'MultiplicativeRankingPolicy' could not be found ...
error CS0246: The type or namespace name 'MultiplicativeRankingOptions' could not be found ...
error CS0246: The type or namespace name 'GraphMemoryOptions' could not be found ...
```

Running the "before" block's exact source against this branch reproduces the real compiler output a 2.5
consumer sees:

```
error CS0246: The type or namespace name 'IMemoryClock' could not be found (are you missing a using
  directive or an assembly reference?)
error CS0246: The type or namespace name 'ElapsedClock' could not be found (are you missing a using
  directive or an assembly reference?)
error CS0117: 'GraphMemoryOptions' does not contain a definition for 'HopAttenuation'
error CS0117: 'GraphMemoryOptions' does not contain a definition for 'RelativeFloor'
```

Four errors, all four traceable to Step 1 and Step 4 above — nothing else in this block changed.

## Checklist, in the order the fixes actually depend on

1. **Add** the namespaces, then rename. `Lyntai.Memory.Interference` and `Lyntai.Memory.Forgetting` for the
   seams, `Lyntai.Memory.Ranking` if you tuned `HopAttenuation`/`RelativeFloor` — and **keep**
   `Lyntai.Memory`, which still owns `GraphMemoryOptions`, `MemoryDecayState`, `MemoryWrite`, `MemoryQuery`
   and `IMemoryGraphStore`. Then rename the two seams 2.5 shipped, and their implementations, wherever your
   code names
   them (Step 1). Do this first — every later fix references the new names.
2. **Rename the storage type too, if you bound the keyword store's size**: `MemoryRetentionPolicy` → <!-- drift-ok: the checklist step IS the rename -->
   `MemoryEvictionPolicy`, `LyntaiOptions.MemoryRetention` → `.MemoryEviction`, and the policy's `Eviction` <!-- drift-ok: as above -->
   property → `Mode`. Same namespace, same presets, same behaviour (Step 1). Skip if you never configured it.
3. **Fix signatures** on anything you implement: `Reinforce` returns `MemoryDecayState`, built with
   `with` and never rebuilt from scratch; add `Provenance` — a real, single, unique bit, or the engine
   throws — to a custom retrievability or salience policy; add `Kind`/`Age` to a custom age policy, and
   take `Advance`'s new `engine` argument (Step 2).
4. **Fix a custom `IMemoryGraphStore`**, if you have one: add all five of `DeleteAsync`,
   `RecordReviewsAsync`, `ReviewsAsync`, `RecordSubjectsAsync` and `NodesBySubjectAsync` — the last two may
   be no-ops (Step 3). Skip if you use a shipped store.
5. **Decide** on the three changed defaults: the curve (`DsrRetrievability`, with the exponential one deleted
   and no restore), the ranking policy (`ReciprocalRankFusionPolicy`, or restore `MultiplicativeRankingPolicy`
   explicitly with your old numbers), and **reinforcement** — a recall no longer lengthens a half-life
   (`DsrOptions.ReinforceGain` ships at `0`). The third is the one that changes what your existing store
   returns without you touching anything (Step 4).
6. **If you use `MemoryGrade.Authoritative`, re-check any recall with a small `limit`** — exact facts now
   take slots within it and displace ordinary hits, so a `limit` chosen against 2.5's behaviour may now
   return less ordinary material than you expect. Either raise the limit or bound the reserve with
   `GraphMemoryOptions.AuthoritativeReserve` (Step 4). Nothing to do if you never set the grade.
7. **Re-check any hand-tuned ranking numbers you carried across** — `MultiplicativeRankingOptions` and
   `ReciprocalRankFusionOptions` validate on construction where `GraphMemoryOptions` accepted anything, so a
   value 2.5 took in silence can now throw at startup (Step 4's table has the bounds).
8. **Nothing required** for the plural policies, per-call ranking selection, or the review log — read Step 5
   only if you want to change the default behaviour, or if you deconstruct `MemoryQuery` positionally.
9. **Search for hand-constructed `GraphMemoryEngine`s and positional deconstructions.** Those are the two
   shapes nothing above catches by name: `memoryClock:` is now `agePolicies:` and takes a collection (Step
   1's parameter table lists all three renamed parameters, and why only that one can affect 2.5 code), and
   `MemoryQuery`/`MemoryDecayState` each gained trailing members, so a five-element deconstruction of either
   no longer binds (Steps 1, 2 and 5).
10. **Fix a custom `IJobStore`**, if you have one: add `PollAgainAsync`, undoing the attempt increment and
   fencing on the worker id (Step 3b). Skip if you use a shipped job store — or no jobs at all.
11. **If you consume `Lyntai.Generation`, convert every `Add*Provider` call to the configure callback**
   (Step 6). Skip if you do not. It is last because it is mechanical and independent: a compile error names
   every site.
12. **Apply the renames** (Step 7). A compile error names every site except ONE:
   `LocalDiffusionOptions.Strength` is now `DenoisingStrength`, and configuration binding ignores unknown
   keys — so grep your `appsettings` for it, or renders silently use the default.
13. **Fix a custom `ICliProviderDialect` or memory engine**, if you have one: `BuildCompletionArgs` takes the
   tool-host args; `IForgettableMemory` is forget-only with `PruneAsync` moved to
   `IPrunableMemory`, and which members a removal visits is now `IMemoryRemovalPolicy` (Step 7).
14. **Fix a custom `IGenerationRouter`**, if you have one: it gained a required `StreamAsync` (Step 8). The
   built-in router and its budget/rate-limit decorators ship implemented, so this is only a hand-written one,
   and the compiler names it.
15. **Run your own test suite.** Storage needs nothing: `MigrateUpAsync` carries every schema change
   automatically, and the `Stability` unit contract means your 2.5.x rows are already correct under the new
   curve.

## What this guide does not cover

Deliberately out of scope, per the design spec and the plan that produced this window of changes:

- **Parameter fitting.** Every constant on `DsrOptions` is FSRS's own published default, not one fitted
  against your own review history — that needs a real corpus of logged reviews (the review log in Step 5
  is what makes it possible) and is future work, not a 3.0 concern.
- **Rating input from a consumer.** Nothing here adds a way for an application to grade its own recalls;
  the derived grade `DsrRetrievability` computes internally is a stand-in for a rating this library never
  collects.
- **A further ranking-default change.** RRF becoming the default (Step 4) is the one this window shipped;
  nothing here settles anything past it.
