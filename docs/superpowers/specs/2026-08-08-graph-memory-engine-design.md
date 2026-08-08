# Graph memory engine — decay, relinking, and progressive retrieval (Spec B)

> **Status:** design, agreed 2026-08-08. **Not implemented** — no code exists.
> **Task:** `TASKS.md` Part 46 / **MEM2**, with the decay constants closed by **MEM-TUNE** (§11).
> **Domain:** contracts in Core (`Lyntai.Memory`), implementations in the three existing storage adapters
> (`Lyntai.Storage.Sqlite`, `.Postgres`, `.InMemory`). **Compatibility:** purely **additive** — a new
> domain interface, a new migration, new registration extensions. Minor bump; not D24 material.
> **Depends on:** `2026-08-08-memory-engine-seam-design.md` (Spec A), which must land first — building
> this engine without the seam means wiring it bespoke and reworking it afterwards.
> **Carries an unmeasured surface:** the decay constants in §11 are guesses. Read §11 before treating
> any of them as tuned.

## 1. The problem

Recall today is one shot: take the top *k* by similarity or keyword, flatten them into a 4000-character
block, send. Three things follow from that shape, and all three get worse the longer an application runs.

- **Nothing fades.** An `IMemoryStore` entry has a hard TTL set at write time and nothing else. A fact
  recalled fifty times and a fact written once and never used again are ranked identically until the
  timer fires. That is the opposite of how retrieval actually ought to behave.
- **Nothing connects.** Entries are independent rows. Recalling "the build gate" tells the system nothing
  about the fact that it was learned in the same breath as "the console is GBK", so there is no way to
  follow one thread into the next.
- **The first load is the whole load.** To have context available you must put it in the prompt, so the
  cost of a large memory is paid on every single turn, whether or not any of it turns out to be relevant.

The goal is the shape a human actually uses: open with a cheap index, and pay for depth only along the
direction you turn out to need.

## 2. The model

An entry is a **node**. Nodes carry a short headline and full content; recall returns headlines, and
expansion returns content plus neighbours. Nodes are connected by weighted **edges**, and every node has a
**retrievability** that decays with time and is reinforced by use.

```
RecallAsync(...)                      ~200 chars opens the session
  "user prefers terse commit messages"    #12  ->4  r=.91
  "build gate is dev.mjs verify"          #47  ->9  r=.88
  "GBK console mangles UTF-8 writes"      #31  ->2  r=.55

ExpandAsync(#47)                      depth costs one hop
  full content of #47 + its 9 neighbours' headlines
```

Both halves of Spec A's dual-consumer decision sit on this one engine: a **composer** walks it
automatically under a budget with no extra turns, and a **tool set** lets the model walk it itself. The
tool set is the one that literally delivers "the further it digs in one direction, the more it gets";
the composer is the one that works with every provider, including those with no tool support.

## 3. Retrievability — how forgetting works

Stored per node: `created_at`, `last_recalled_at`, `recall_count`, `stability`.

```
r = 2 ^ ( -age_since_recall / stability )        1.0 when fresh -> 0 when stale
rank = relevance * r * 0.5^hop_distance
```

On a **successful recall** — meaning the node was actually returned, not merely traversed — one batched
statement bumps `last_recalled_at` to now, increments `recall_count`, and multiplies `stability` by
`(1 + Reinforce)`. The half-life grows with use, so repeated context becomes durable and one-off noise
fades. This is the whole of the forgetting mechanism; there is no sweeper and no background job.

**Decay never deletes.** It only ranks. Deletion stays explicit and opt-in through
`IForgettableMemory.PruneAsync(minRetrievability:)`, exactly as `IMemoryStore.PruneAsync` is explicit
today. Authoritative nodes hold retrievability 1.0 permanently and are therefore never eligible for a
retrievability-based prune — which falls out of the formula and needs no special case in the query.

### 3.-1 Amendment 2026-08-08 — decay is measured in EVENTS, not wall-clock time

_Supersedes the "days" dimension used throughout §3 below; read this first._

**The defect.** The model was borrowed wholesale from human memory research, where decay is measured in real
time because that is what a person experiences between encounters. An agent does not work that way. Two
engines with identical content:

- one session, eight hours, 500 writes — wall clock says almost nothing decayed;
- two sessions a month apart, three writes each — wall clock says everything decayed.

The second experienced almost nothing and forgot everything; the first experienced a great deal and forgot
nothing. That is backwards, and it is exactly the burst-shaped usage this library targets.

**The model is interference, which is what the research actually says causes forgetting**: a trace fades
because *newer material competes with it*, not because seconds elapsed. So the age is a difference on a
monotone scalar the store keeps per engine:

```
age = current_position − node.last_recalled_position
r   = 2 ^ ( −age / stability )              // stability is measured in the SAME units
```

**What that scalar COUNTS is a seam, not a decision.** "How much has happened" is genuinely ambiguous — it
could be the number of things written, how much material was written, how much real time passed between
messages, or a blend — and picking one would be exactly the kind of hard-coding this design has avoided
elsewhere. So the increment is supplied:

```csharp
public interface IMemoryClock { double Advance(MemoryWrite write); }

PerWriteClock     → 1                      // interference by count — the DEFAULT
ContentSizeClock  → write.Content.Length   // bigger material crowds harder
ElapsedClock      → days since last write  // calendar decay, for a project memory
```

Three shipped implementations, so nobody has to write one, and a project engine can decay by real time
while a chat engine decays by volume **in the same application**. The stored form and every query are
identical whichever is chosen; the dimension is decided once, in C#, at registration. That is also why the
constants below carry no unit in their type — a `TimeSpan` would assert a dimension the application has not
picked yet.

**Only writes advance it.** Remembering something — new or refreshed — advances the position, because new
material is what competes. Recall does not: it reinforces what it returned, so a long read-only session
costs nothing and a read-only agent never forgets by reading.

**The property this buys, which is the point of the whole change:** a rarely-used memory decays slowly and
a heavily-used one decays fast, automatically. The position advances only when that memory is written to,
so an engine nobody touches keeps everything while a busy one lets old material fall behind. Wall-clock
gets this exactly backwards — it ages the quiet system at the same rate as the busy one. `ElapsedClock`
deliberately gives the property up, because a *project* memory sometimes should fade on the calendar; that
is the trade-off it exists to offer, per engine and opt-in.

**The position is per engine.** A global one would let a busy engine age a quiet one's memories — the same
failure one level up. Concurrent writers can collide on a value, which merely counts two writes as one
advance — benign for a soft counter, and not worth a lock.

**Three consequences, all simplifications:**

- **`julianday` / `EXTRACT(EPOCH …)` disappear.** The candidate filter becomes integer subtraction over a
  plain column. The `MAX(stability, ε)` divide-by-zero guard stays, but the far worse hazard it sat next
  to — SQLite's `julianday` returning NULL on a timestamp format it cannot parse, silently excluding every
  row — stops existing.
- **`TimeSpan` leaves the options.** `InitialStability = TimeSpan.FromDays(7)` asserts "wall clock" in the
  type signature; a count of events is a `double`, and the type stops claiming something untrue.
- **No injected clock in the decay path.** Advancing the sequence is just writing, so tests need no fake
  time at all.

**Wall-clock timestamps stay on the table**, for the two things they are honestly for: `PruneAsync(olderThan:)`
("reap anything created over 90 days ago" is a real calendar concern) and auditing. They no longer feed decay.

### 3.0 Amendment 2026-08-08 — connectedness feeds decay, and edges decay too

_Added after MEM2a shipped, because the first cut had the graph affecting only REACHABILITY._

**The defect.** `r = 2^(-age/stability)` with stability growing only from direct recall means a node
recalled once and connected to twenty things decays exactly like a node recalled once and connected to
nothing. That is backwards from both the brain analogy and the reason edges exist: an isolated fact should
fade, a fact woven into a network should persist.

**The mirror defect, found at the same time.** Edges only ever grew — `LinkAsync` did
`weight = existing + weight` and nothing weakened one. Over a long run every pair that ever co-occurred
stays linked at a monotonically increasing weight, the graph saturates, and spreading stops discriminating
because everything reaches everything. Fixing only the first half would have made it worse: stale links
would prop memories up forever.

**The model.** Connectedness raises *stability*, never `r` directly, and edge weight decays on the same
read-time principle as nodes:

```
effective_edge_weight = weight × 2 ^ ( -edge_age / EdgeHalfLife )
strength              = Σ effective_edge_weight over a node's edges
effective_stability   = min( stability × min(1 + Boost·ln(1+strength), MaxBoost), MaxStability )
r                     = 2 ^ ( -age / effective_stability )
```

`ln(1+strength)` gives diminishing returns so a hub does not dominate. **`MaxStability` still applies to the
EFFECTIVE value** — otherwise connectedness reintroduces the permanence defect that ceiling was added to
close, one layer up, and a well-connected associative node becomes immortal while still labelled
associative. Same trap, new place.

**`MaxBoost` is not decoration — without it the `CandidateCutoff` contract breaks.** The store filters on
*stored* stability. If effective stability could exceed stored without bound, a well-connected node could
fall outside `age/stored <= cut` while its true retrievability was still above the floor, and the store
would exclude a node the policy would have kept — exactly the silent loss §3.2's superset property exists
to prevent. Bounding the boost makes the correction exact:

```
CandidateCutoff(minR) = -log2(minR) × MaxBoost
```

**One documented approximation, in the safe direction.** Decaying every edge individually at read time
would need a per-edge `pow` inside the aggregate, which no backend can do portably (§3.2). So a store
reports `Strength` as the sum of RAW edge weights plus `StrengthAsOf` = the most recent strengthening, and
the policy decays that aggregate once. This treats a neighbourhood as being as fresh as its freshest link,
so it **over**-estimates durability. That is deliberate: over-estimating raises `r`, and only raising `r`
keeps the cutoff a conservative superset. Under-estimating would lose memories.

### 3.1 The curve is a seam, and its constants are an options record

Exposing `Reinforce` and `InitialStability` as loose doubles would settle the *numbers* while freezing the
*formula* — a consumer wanting power-law decay or a spacing-repetition schedule would be stuck. So the
curve is a seam with a registered default, and the default's constants are an options record:

```csharp
public readonly record struct MemoryDecayState(
    DateTimeOffset CreatedAt, DateTimeOffset LastRecalledAt, int RecallCount, double Stability);

/// <summary>The model of forgetting. Swappable; the default is registered for you, so nothing has to be
/// implemented to use graph memory.</summary>
public interface IRetrievabilityPolicy
{
    /// <summary>Retrievability in [0,1] for a node's state at <paramref name="now"/>.</summary>
    double Retrievability(in MemoryDecayState state, DateTimeOffset now);

    /// <summary>The node's new stability after a successful recall.</summary>
    double Reinforce(in MemoryDecayState state, DateTimeOffset now);

    /// <summary>Stability for a brand-new node, in days.</summary>
    double InitialStability { get; }

    /// <summary>A CONSERVATIVE bound on <c>age_days / stability</c> for a given minimum retrievability:
    /// no node whose true retrievability is >= <paramref name="minRetrievability"/> may exceed it. The
    /// store uses it to bound the candidate set in SQL WITHOUT encoding the curve (§3.2). A policy that
    /// cannot bound its curve returns <see cref="double.PositiveInfinity"/> — correct, at the cost of an
    /// in-scope scan.</summary>
    double CandidateCutoff(double minRetrievability);
}

public sealed record HalfLifeOptions
{
    public TimeSpan InitialStability { get; init; } = TimeSpan.FromDays(7);
    public double   ReinforceFactor  { get; init; } = 0.5;
    public TimeSpan MaxStability     { get; init; } = TimeSpan.FromDays(365);
}
```

`HalfLifeRetrievability` is the default implementation and is registered automatically. An application
tunes the numbers with `UseGraph(g => g.Decay = new HalfLifeOptions { ReinforceFactor = 0.3 })`, or
replaces the curve entirely by registering its own `IRetrievabilityPolicy`.

**`MaxStability` is not a rounding-out knob — it closes a real defect.** Unbounded
`stability *= 1 + Reinforce` compounds: at the default factor, roughly twenty recalls turn a seven-day
half-life into sixty-four years. A frequently-recalled associative node would therefore become
*permanently* retrievable while still being labelled associative — silently acquiring the durability of
authoritative material without any of its guarantees, which is exactly the grade confusion §4 of Spec A
exists to prevent. The cap bounds the ceiling at a year by default, so "very durable" never becomes
"never forgotten".

### 3.2 The `pow` trap, and why the SQL has no exponent in it

The obvious implementation ranks with `POWER(2, -age/stability)` in SQL. SQLite only has `pow` when built
with `SQLITE_ENABLE_MATH_FUNCTIONS`, so that is a bet on the shipped native bundle — the kind of
assumption that is green on the development machine and fails on somebody's deployment.

It is also unnecessary — and once the curve became a seam (§3.1), it became *wrong*, since no fixed SQL
expression can encode a policy the application supplies. The database therefore never evaluates the
curve at all. It **bounds a candidate set**; the policy ranks it.

- **The candidate filter** is `age_days / stability <= @cut`, where `@cut` is
  `policy.CandidateCutoff(minRetrievability)` — `-log2(minR)` for the default half-life curve, computed
  in application code and passed as a parameter. The contract is that the bound is a **conservative
  superset**: it may admit nodes the policy will later reject, and must never exclude one the policy
  would have kept.
- **Ordering** inside the candidate set is `age_days / stability` ascending, which is a correct
  pre-sort for any curve decreasing in that ratio. It exists to make the candidate cap meaningful, not
  to be the final order.
- **Exact retrievability and final ranking** are computed in application code by the policy, over a
  candidate set capped at a bounded multiple of the requested limit.

So SQL does division and comparison, nothing more. No exponent is evaluated in any database, and a
custom curve needs no SQL of its own. A policy that cannot bound its curve returns
`double.PositiveInfinity` and gets a correct — if unindexed — in-scope scan.

## 4. Where edges come from

The floor is **model-free**, because `model-decoupling` requires the feature to work with no embedder
registered, and because the association mechanism that is most like a brain happens to be the one that
costs nothing.

| Source | Trigger | Needs a model |
|---|---|---|
| **Co-activation (Hebbian)** | nodes returned together in one recall get an edge; recurrence strengthens it | no |
| **Structural** | same scope, or a shared metadata key/value | no |
| **Explicit** | `ILinkableMemory.LinkAsync(from, to, kind, weight, symmetric)` | no |
| **Similarity** *(enrichment)* | on write, link to the *k* nearest existing nodes | yes — `IEmbedder` + `IVectorStore` |

Co-activation writes **symmetric pairs** (two rows), so traversal is a single `WHERE from_id = @id` with
no canonical-ordering rules to get wrong. Explicit links are directed unless `symmetric: true`.

Co-activation is **capped at the top 5 returned nodes** — 10 edges — because a k=10 recall would otherwise
write 45 edges on every single turn.

Similarity is strictly enrichment layered on the floor, never the floor itself. When no embedder is
registered the graph still forms, and `MemoryRecall.Ran` omits `MemorySources.Similarity` so a caller can
tell "nothing similar" from "similarity is not configured". That flag is deliberately distinct from
`MemorySources.Semantic`, which reports that a semantic-memory *member* produced hits — both require an
embedder and they fail independently.

## 5. Recall

1. **Seed.** FTS/lexical match on the query, plus semantic seeds when an embedder exists. Authoritative
   in-scope nodes are admitted **unconditionally**, query or no query.
2. **Spread.** For hops 1..`Hops`, pull the frontier's neighbours ordered by `weight × r`, capped per hop.
3. **Score.** `relevance × r × 0.5^hop`. Associative nodes whose `r` is below **`MinRetrievability`**
   (default `0.05`) are dropped here — that is the point at which a memory has effectively been
   forgotten, and it is the same threshold `PruneAsync` uses to decide what may be reaped. Authoritative
   nodes hold `r = 1.0` and are never affected by it.
4. **Budget.** Authoritative reserve allocated first (Spec A §4.3), then associative by rank until the
   character budget is spent.
5. **Touch.** One batched `UPDATE` reinforcing the returned nodes, plus the capped co-activation edges.

Returned associative items carry `Headline` with `Content` null — that is what makes the first load cheap.
Authoritative items always carry full `Content`; they are never returned truncated.

### 5.1 Recall now writes

This is a genuine change in character from every other recall path in the library, and it has
consequences that must be designed rather than discovered:

- The touch is **best-effort**. A failure logs and the hits are still returned — so a read-only database
  degrades to "no learning", never to "no memory".
- Concurrent touches are last-write-wins and benign: two sessions reinforcing the same node both mean to
  reinforce it.
- Cost is one `UPDATE` plus one small `INSERT … ON CONFLICT` per recall. Bounded by the caps in §4.
- `OperationCanceledException` still propagates; it belongs to the caller.

## 6. Expand

```csharp
Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1,
    int? charBudget = null, CancellationToken ct = default);
```

Returns the node's full content plus its neighbours' headlines, ordered by `weight × r`, capped by budget
— and **touches the expanded node**. Digging in one direction is therefore exactly what makes that
direction more retrievable next time, which is the property the whole design is named for.

Through a composite, expansion routes by `MemoryRef.Engine` to the owning member (Spec A §3.4).

## 7. Agent tools

`AddMemoryTools("project")` registers `project_recall` and `project_expand` into the tool loop and across
the MCP bridge. Names are engine-prefixed so registering tools for several engines cannot collide, and so
the model can never consult the wrong memory — the multiplexed alternative (`memory_recall(engine:…)`)
is fewer tools but reintroduces exactly the accuracy failure Spec A §4 exists to prevent.

The tools return the same `MemoryItem` shape the composer uses, rendered compactly. Tool arguments carry
no application vocabulary — task key, scope, query, budget, and a `MemoryRef` string.

## 8. Storage

### 8.1 Tables

Two tables, `lyntai_`-prefixed because Lyntai may share a database with the consuming application's own
schema.

```
lyntai_memory_node                       lyntai_memory_edge
  id               PK                      from_id   FK -> node ON DELETE CASCADE
  engine           TEXT                    to_id     FK -> node ON DELETE CASCADE
  task_key         TEXT                    kind      TEXT   -- '' = untyped
  scope            TEXT                    weight    REAL
  headline         TEXT                    updated_at
  content          TEXT                    PRIMARY KEY (from_id, to_id, kind)
  content_hash     TEXT   -- dedup
  grade            INTEGER -- MemoryGrade: 1 = associative, 2 = authoritative
  metadata_json    TEXT NULL
  created_at
  last_recalled_at
  recall_count     INTEGER
  stability        REAL   -- half-life in days
  UNIQUE (engine, task_key, scope, content_hash)
```

The composite primary key and both foreign keys are declared **inline at `Create.Table`** — SQLite has no
`ALTER ADD CONSTRAINT`, and discovering that afterwards means a table rebuild.

Dedup is by SHA-256 `content_hash` rather than by the text itself, so the unique index stays small; the
response cache already keys this way. Re-remembering identical content in the same scope refreshes the
existing node instead of duplicating it, matching `IMemoryStore` and `ISemanticMemory`.

### 8.2 Full-text search

External-content FTS5 over `headline` and `content` with the **trigram** tokenizer — `unicode61` treats a
whole CJK phrase as one token and would return nothing, silently, for exactly the corpora that need
substring recall most.

Kept in sync by **three** triggers (insert, delete, update), each emitting the special FTS `'delete'`
command row before re-inserting on update *and* on delete. Missing that row corrupts the index silently:
stale rows keep matching forever. Existing rows are backfilled **in the same migration**. Copy
`M202607280003_Memory.cs` and adjust the columns; the two-column FTS table means the delete command row
must supply both column values.

Match expressions come only from `FtsQuery.Build` — never raw user text — which drops sub-3-character
tokens, quotes the rest, and returns null so the caller falls back to `ESCAPE`-guarded `LIKE`.

### 8.3 Migration

Scaffolded with `node devtools/dev.mjs new-migration` so the number is unique and monotonic — a reused
number is *silently skipped* and the table simply never appears. Tagged
`[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]`: **both** tags, always, because an
untagged migration runs under every feature set and would land a table for a domain the application
disabled.

Similarity enrichment reads `IVectorStore`, which ships under `StorageFeature.Governance`. The graph does
not require it: with no vector store registered, enrichment is skipped and reported through
`MemoryRecall.Ran`. No new `RequireGovernance` check is needed, because the existing
`UseSqliteVectorStore` guard already covers the case where a vector store is registered over a table the
feature set never created.

### 8.4 Reading rows

`stability` and `weight` are floating-point columns, so **every** read is `CAST(col AS REAL)` — SQLite
stores `1.0` as an INTEGER and `0.5` as a REAL in the same column, and Dapper will hand a `double`
property a boxed `long`. Rows land in a settable-property `MemoryNodeRow` / `MemoryEdgeRow` and are
projected to the immutable records; never a positional record constructor, and never named `*Dto`.

Every `SELECT` aliases explicitly (`SELECT id AS Id, …`) — a name mismatch yields a silent null, not an
error. Every `ORDER BY` carries a unique tiebreaker (`… , id DESC`) so ties do not wobble. Connections come
only from `IDbConnectionFactory`, or per-connection `foreign_keys=ON` is lost and the cascades above stop
working.

### 8.5 Three backends, one contract, no shared SQL

`Lyntai.Storage.Sqlite` and `.Postgres` implement `IMemoryGraphStore` in parallel, and that parallelism
is deliberate — `storage.md` records that a review of every existing pair found the divergence to be
dialect necessity in every case, not drift. Do not extract a shared base: the moment an extraction needs
`bool isSqlite` or a `Real(col)` helper, stop. `MemoryGraphStoreContract` running against all three
backends is the deduplication mechanism.

Recall divergence is **inherited and documented, not fixed**: SQLite matches any ≥3-character token via
the trigram index ranked by bm25; Postgres (pg_trgm) and InMemory match the query as a contiguous
substring ranked by recency. The portable guarantee is the same single-token one `IMemoryStore.RecallAsync`
already states.

## 9. Error handling

- Recall degrades FTS → LIKE → most-recent → empty and never throws on a short or unmatchable query.
  Only `OperationCanceledException` propagates.
- The touch and co-activation writes are best-effort (§5.1).
- `RememberAsync` surfaces failures — losing a write silently is worse than a throw the caller can see.
- An authoritative write that cannot be honoured is routed or throws; never downgraded (Spec A §4.2).
- Missing backing store throws at startup, not at first recall (Spec A §5.4).

## 10. Testing

- **`MemoryGraphStoreContract`** across InMemory + SQLite + Postgres, mirroring
  `CuratedMemoryStoreContract`.
- **Affinity round-trip** — `stability` and `weight` survive exactly, in the shape of
  `ScoreStoreTests.Doubles_round_trip_exactly_the_affinity_trap`.
- **FTS sync** — after an update and after a delete, the old text matches nothing. This is the single most
  botched thing in this repository's storage layer.
- **Retrievability math**, hop attenuation, budget allocation, headline derivation and co-activation
  capping as pure unit tests with fakes and no I/O.
- **`IRetrievabilityPolicyContract`** — one set of facts every policy must satisfy, run against the
  default and against a deliberately awkward fake:
  - `Retrievability` is in `[0,1]`, is 1.0 at zero elapsed time, and never increases with age.
  - **`CandidateCutoff` is a conservative superset.** Over a generated table spanning several orders of
    magnitude of age and stability, no node with true `r >= minR` falls outside the cutoff. This is the
    test that protects the §3.2 trick, and it is stated as a *superset* property rather than as exact
    equivalence precisely because a custom curve is allowed to be looser than the default.
  - A policy returning `PositiveInfinity` still yields correct results, just slower.
- **Stability is capped.** Recall a node far more times than `MaxStability / InitialStability` allows and
  assert its half-life stops growing — otherwise a hot associative node silently acquires authoritative
  durability without the guarantees (§3.1).
- **Reinforcement** — a node recalled repeatedly outranks a fresher node that never was, after enough
  simulated elapsed time. Time is injected, never `DateTimeOffset.UtcNow` in the store.
- **Read-only degradation** — a store whose touch throws still returns hits.
- **Expansion through a composite** reaches the graph (Spec A §8).
- Deterministic fake embedder; no test spends a token or hits a live endpoint. Every await that can block
  is bounded, so a regression arrives as an assertion, not as a hung `verify`.

## 11. MEM-TUNE — CLOSED 2026-08-08

_The table below is kept as the record of what was open. The constants are now chosen against
`MemoryDecaySimulationTests`, which drives a corpus with a known reuse/noise split through the whole model
at once and asserts outcomes rather than values:_

- _**Retention** — at least 90% of the reused set is still recallable at the end of the run._
- _**Decay** — at most 10% of the one-off material written in the FIRST HALF of the run is. Measured over
  the first half deliberately: material written moments ago should still be recallable, because it is
  recent. What must fade is what was mentioned once, long ago._
- _**Separation** — the weakest reused fact outranks the strongest surviving old one-off._
- _**Stability of the answer** — all of it still holds when the run is driven twice as long, so the values
  are not fitted to one point on the curve._
- _**Burst survival** — a 500-item bulk ingest does not erase what the memory already held, with a control
  asserting the same ingest UNDAMPED erases all of it, so a regression that silently disabled damping
  cannot pass._

_**What that closes, precisely.** It measures the DYNAMICS and runs in CI, which a production corpus
cannot. It does NOT establish that real usage has the reuse-to-noise ratio modelled here. So the constants
move from "guess" to "measured against a stated model", and the XML docs say exactly that — a starting
point, not a tuned value. Replacing the model with a real corpus later is a strict improvement, not a
prerequisite._

### 11.1 The original table, as filed

Four of the constants below are **guesses**. This repository has a standing norm about shipping unmeasured
surfaces as though they were measured — it is why GEN-VERIFY exists — so they are declared here and must be
marked as unmeasured in the XML documentation until something measures them.

| Default | Basis |
|---|---|
| `Hops = 2` | reasoned — three or more hops reaches most of a connected graph, which defeats the purpose |
| hop attenuation `0.5^hop` | reasoned — halving keeps hop-2 material below hop-1 |
| co-activation cap = top 5 | reasoned — bounds edge writes at 10 per turn instead of 45 |
| `HeadlineChars = 120` | **guess** |
| `Reinforce = 0.5` | **guess** — sets how fast repeated context becomes permanent |
| initial `stability = 7 days` | **guess** — sets how fast one-off noise fades |
| `MinRetrievability = 0.05` | **guess** — the point at which something counts as forgotten |
| `MaxStability = 365 days` | **guess** — the ceiling that stops "durable" becoming "permanent" (§3.1) |
| `ConnectionBoost = 0.5` | **guess** — how much being well-connected extends a half-life (§3.0) |
| `MaxConnectionBoost = 4` | **guess** — but its EXISTENCE is load-bearing, not just its value: `CandidateCutoff` widens by exactly this, so an unbounded boost has no valid cutoff (§3.0) |
| `EdgeHalfLife = 30 days` | **guess** — how fast a link that stops recurring stops mattering (§3.0) |
| `SimilarityK = 5` | **guess** |

_The three added by §3.0 interact, which MEM-TUNE must measure together rather than one at a time: edge
decay erodes the very strength that feeds the connection boost, so the window in which connectedness
rescues a memory is finite and its width is set by all three at once. A test written during MEM2a found
that empirically — a well-connected node survived at 45 days and was gone by 60._

**The task is a test, not a judgement call.** `MemoryDecaySimulation` builds a synthetic corpus with a
*known* split — a reused set touched on a schedule, and one-off noise written once — drives it through
the engine over simulated weeks against an injected clock, and asserts measurable outcomes:

- **Retention.** At week 8, at least 90% of the reused set is still above `MinRetrievability`.
- **Decay.** At week 8, at most 10% of the one-off noise is.
- **Separation.** Every member of the reused set outranks every member of the noise set.
- **Stability of the answer.** The above still holds at week 16, so the constants are not tuned to one
  point on the curve.

The constants are then chosen as values that satisfy those assertions, and the test keeps them honest —
it is both the tuning instrument and a permanent regression guard, so a later change to the formula that
breaks the dynamics fails the build rather than degrading recall quietly.

**What this does and does not prove, stated plainly.** A synthetic corpus measures the *dynamics* — that
reuse outruns decay by the intended margin — and it is genuinely runnable in CI, which a production
corpus is not. It does **not** establish that real usage has the reuse/noise ratio the simulation
assumes. So closing MEM-TUNE on the simulation alone downgrades the constants from *guess* to
*measured against a stated model*, and the XML documentation must say exactly that rather than dropping
the caveat entirely. Replacing the model with a real corpus later is a strict improvement, not a
prerequisite.

## 12. Deferred, with reasons

- **LLM consolidation** — finding a dense cluster of weak related nodes, having a model write one strong
  summary node, and letting the details decay away. The most brain-like piece and the most speculative: it
  needs real recall data to tune, spends tokens on every pass, and can destroy information. It gets its own
  spec once the graph has been running long enough to have something to consolidate.
- **Per-entry confidence scores** — nothing in the library can produce that number honestly.
- **Per-engine databases** — see Spec A §9.
- **A grade filter on `MemoryQuery`** — probably right for an agent that wants only hard facts; deferred
  until a caller asks, since every public member is permanent.
