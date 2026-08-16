# Changelog

All packages version in lockstep from `src/Directory.Build.props` (`VersionPrefix`).
From 1.0, **SemVer 2.0** applies: breaking public-API changes require a major bump (gated by
`ApiSurfaceTests`; see `docs/DECISIONS.md` — the 1.0 API freeze). Pre-1.0 (≤ 0.31.x) minor bumps could carry breaking
changes — each is called out below.

**Amended 2026-07-29 (`docs/DECISIONS.md` — the deferred-SemVer-strictness rule):** while every consumer is one of the owner's own
applications, a **documented** break may ship in a MINOR release. Every break is still gated by
`ApiSurfaceTests` and still called out under a **Breaking** heading here — only the version-number
consequence is relaxed. Strict SemVer resumes as soon as any third party depends on Lyntai.

## Unreleased — the memory retention model + the pre-freeze whole-library review

**Upgrading from 2.5? Start at `docs/migration-2.5-to-3.0.md`** — one ordered path through everything
below: the seam renames (**two** of the four `IMemory*Policy` seams were renamed from types 2.5 shipped —
the other two are new capabilities that never shipped under any name), `Reinforce`'s new return type, the
required members added to seams a consumer implements, the **three** changed registered defaults (and the
one deletion with no restore), the 3.0 naming sweep, and why stored data needs no migration at all. The
entries below are the per-change record with the full reasoning; the guide is the walk-through with a
worked before/after.

> **Grouped by KIND, not by the order things landed.** This section carried four `### Added` blocks, five
> `### Fixed` and five `### Breaking` while 3.0 was in development — a faithful development log, and one in
> which `CLAUDE.md`'s own instruction to *"read `### Breaking` before assuming any memory behaviour"* named
> a section a reader could not find by scanning. It is now one block per kind, every entry preserved
> verbatim and in its original relative order.
>
> **Some entries are still SUPERSEDED by later ones, and each says so inline.** A curve introduced here was
> deleted further down; an option moved to another record; a guard was added and then removed once its cause
> was fixed properly. Those annotations matter more at release than before it: this heading becomes
> `## 3.0.0`, `check-docs` and `check-links` stop scanning it, and every un-annotated claim freezes into the
> shipped record. **For what 3.0 actually DOES, read `docs/migration-2.5-to-3.0.md` or `docs/memory.md`,
> never a single entry here.**

### Breaking

- **`IJobStore` gains `TryAcquireSlotAsync`, `ReleaseSlotAsync` and `HeartbeatSlotsAsync`**, none with a default body, for the
  cross-process job cap above (`docs/DECISIONS.md` **D73**). Only a hand-written store is affected; all three
  shipped stores implement them, and the compiler names every site. A store that does not want to support
  the cap can return `null` from the acquire — the runner then simply never claims while
  `GlobalMaxConcurrency` is set, which is a visible refusal rather than a silently ignored limit. It is the
  third member `IJobStore` gained this release, after `PollAgainAsync`.

- **`IForgettableMemory` is SPLIT: `PruneAsync` moves to the new `IPrunableMemory`**, and `IMemoryEngine`
  gains nothing; which members a reap visits is decided by the new `IMemoryReapPolicy` seam —
  `docs/DECISIONS.md` **D72**. A custom engine keeps compiling for `ForgetAsync` and must add
  `IPrunableMemory` to its declaration to keep serving prunes; the compiler names every site. The two verbs
  answer different questions — a forget must be COMPLETE, where a partial one is a broken promise, while a
  prune is best-effort capacity management — and one interface forced an engine to claim both or neither,
  which a vector store cannot honestly do. **It also closes a real hole:** a composite could only pre-check
  that a member implemented the interface, so a member that implemented it and threw from one of the two
  methods produced exactly the partial-reap-then-exception the pre-check exists to prevent.

- **`Lyntai.Generation` is no longer exempt from the SemVer promise** (`docs/DECISIONS.md` **D70**). <!-- drift-ok: the entry ANNOUNCING the withdrawal has to name what was withdrawn -->
  The package shipped EXPERIMENTAL from 2.0.1, and the exemption is **withdrawn**, not merely satisfied — every
  package now needs a major to reshape. It named three reasons and each is closed: the two backends written
  from vendor documentation and the third's ported `sd-cli` argv now expose every mapping they could have got
  wrong as a host option (**D69**), and `IGenerationStreamProvider` is reachable through the router (**D67**).
  Listed under Breaking because it is a change to what the library PROMISES, not to any signature: nothing
  here stops compiling, and a consumer who was relying on these backends being reshapable in a minor should
  know they are now frozen. What a real run can still surprise is a wire format's SHAPE rather than a value,
  and that is now a major-version risk taken deliberately.

- **`IGenerationRouter` gained a third door, `StreamAsync`** — a required member with no default body, so a
  BYO router implementation must add it (`docs/migration-2.5-to-3.0.md` Step 8). The built-in decorators
  implement it; only a hand-written `IGenerationRouter` is affected, and the compiler names it. Before this,
  a backend advertising `GenerationDelivery.Stream` was unreachable through the platform — the capability
  pre-filter was only ever asked about `Inline` and `Job` — so `IGenerationStreamProvider` was a `Lyntai.Core`
  contract about to be frozen under the full SemVer promise having never been exercised.
  `docs/DECISIONS.md` **D67** for why it was wired rather than deleted or moved to the then-exempt package,
  and for the two invariants it INHERITS from the LLM router rather than inventing.

- **The 3.0 naming sweep — names that MISLED are changed; names that merely differed are not**
  (`docs/DECISIONS.md` **D66**). All source-level. **Seven** of the retired spellings are registered in
  `retiredApiNames` so they cannot come back quietly; **four cannot be**, because each is still live and
  correct elsewhere on the surface — `AuthoritativeReserve` on `GraphMemoryOptions` (the slots one, which
  keeps its name), `policy` on `InMemorySecretVault`, `Strength` on `GraphNode`, and `candidates` throughout
  the routing surface. D66 has the table and the reason.

  | was | is |
  |---|---|
  | `MemoryCompositionOptions.AuthoritativeReserve` | `AuthoritativeCharacters` |
  | `MemoryEngineBuilder.Reserve(characters)` | `ReserveCharacters(characters)` |   <!-- drift-ok: a rename record NAMES the retired spelling -->
  | `IProviderInstallation` | `IProviderProbe` |   <!-- drift-ok: a rename record NAMES the retired spelling -->
  | `GraphMemoryEngine(policy:)` / `UseGraph(policy:)` | `retrievability:` |
  | `CuratedMemorySections(task:)` | `taskKey:` |
  | `MemoryProvenance.EnsureEachBitIsSingleRealAndUnique` | `ValidateProvenanceBits` |   <!-- drift-ok: a rename record NAMES the retired spelling -->
  | `IMemoryRetentionCompositionPolicy.Compose` | `StabilityFactor` |
  | `IMemorySalienceCompositionPolicy.Compose` | `Signals` |
  | `SummedAgeComposition` / `MultiplicativeRetentionComposition` / `MaximalSalienceComposition` | `…CompositionPolicy` |   <!-- drift-ok: a rename record NAMES the retired spelling -->
  | `LocalDiffusionOptions.Strength` | `DenoisingStrength` |
  | `UseDefaultGenerationCandidates(candidates:)` | `providerIds:` |

  <br>**The sharpest one, if you read only one row:** `AuthoritativeReserve` named TWO quantities — recall
  SLOTS on `GraphMemoryOptions`, prompt CHARACTERS on `MemoryCompositionOptions` — in the same namespace,
  with different null conventions, both reachable from a single `MemoryEngineBuilder` chain. A consumer
  reading "reserve 2" as slots was setting a two-character budget, which truncates every authoritative fact
  to nothing. The slots one keeps its name; the characters one now says its unit.
  <br>`IProviderInstallation` declared a single `ProbeAsync` and installed nothing, one word away from   <!-- drift-ok: a rename record NAMES the retired spelling -->
  `IProviderVersionInstaller`, which does — and the documented use is a capability type-test, so the name
  was the whole API for a reader choosing between them.

- **`MemoryEngineComposition` and `BudgetedGenerationRouter.RecordAsync` are now `internal`.** Neither had
  an external caller; the latter's own doc said "public so the durable-render handler can record …", and
  that handler is in the same assembly, so `internal` satisfied the stated reason all along.

  <br>**Deliberately NOT renamed, recorded so it is settled rather than rediscovered:** `LlmRequest req`
  versus `request` (74 signatures), `httpClient` for a `Func<…,HttpClient>` on ten builder extensions, and
  `AgentStreamEvent`'s eight subtypes carrying no `*Event` suffix. All are real inconsistencies; none makes
  a reader believe a false thing, and `AgentStreamEvent` is a sealed hierarchy consumers `switch` over, so
  renaming it churns every call site to settle a preference. A break must buy a reader something.



- **`ICliProviderDialect.BuildCompletionArgs` takes the tool-host args**
  (`BuildCompletionArgs(LlmRequest request, IReadOnlyList<string> toolHostArgs)`), and the engine no longer
  appends them itself. Breaking for a BYO dialect; both in-tree dialects are updated. See
  `docs/DECISIONS.md` **D65**.
  <br>**What was wrong.** `CliProviderEngine` appended an `ICliToolProvisioner`'s args after the dialect's
  argv — correct for a CLI whose argv ends in OPTIONS, wrong for one ending in a POSITIONAL. On `codex` the
  argv ends in the `-` stdin marker, so the MCP config overrides landed in the `[PROMPT]` slot: the tools
  the provisioner exists to expose were absent and **the turn was spent**, because that CLI reads an
  unrecognised token as a prompt rather than erroring. `CodexExecArgs` had documented this hazard and taken
  an `extraOptions` parameter for it, and the agent path used it — the completion path had no way to.
  <br>It never bit because `claude` is the only CLI that had driven that path, and appending is correct
  there. A BYO dialect that ends its argv in options can simply append (`[.. mine, .. toolHostArgs]`), which
  is what `ClaudeCliDialect` now does explicitly.


- **`GenerationRouter` is now a TRUST BOUNDARY: a backend that throws is classified and fallen over instead
  of propagating.** See `docs/DECISIONS.md` **D64**. Behavioural, with no compile-time signal — a caller
  that today catches an exception out of `GenerateAsync`/`SubmitAsync` will instead receive a
  `GenerationResult`/`GenerationSubmission` carrying a verdict. `AddGenerationProvider` is a documented BYO
  seam, so the throwing party is frequently neither this library nor the caller, and discarding every
  remaining candidate for a third-party defect is the outcome fallback exists to prevent.
  <br>**The two paths differ deliberately, and it is about money.** Inline generation ADVANCES to the next
  candidate. A thrown SUBMIT is reported `Inconclusive` and SURFACES, because submitting commits the spend:
  the backend never answered, so it may already hold a billable render and advancing would buy the same
  generation twice.
  <br>`OperationCanceledException` under the caller's own token still propagates on both paths — a caller
  must be able to tell their own cancellation from a backend's failure. A thrown `Refused` is clamped to
  `Failed`, so a "content policy" string in a proxy's error page cannot stop the chain.



- **A memory engine you can actually delete from: `CompositeMemoryEngine` implements `IForgettableMemory`,
  and `IForgettableMemory` gains `ForgetAsync`.** Breaking for a BYO implementor of that interface (the only
  in-tree one is `GraphMemoryEngine`, which already had a matching method). See `docs/DECISIONS.md` **D63**.
  <br>**Why it could not wait for 3.1.** `MemoryEngineBuilder.Build` returns a composite for *every*
  registration — single-member included, and it is documented as doing so — and the composite did not
  implement `IForgettableMemory`. So `engine is IForgettableMemory` was **false** for everything
  `IMemoryEngineFactory` hands back, and `ForgetAsync` was a bare public method on `GraphMemoryEngine`
  declared on no interface at all. A consumer holding an `IMemoryEngine` could reach neither: through 2.5.x
  the shipped memory subsystem had **no supported way to delete anything**.
  <br>Reaping FANS OUT to every capable member and sums what they removed, where `ExpandAsync`/`LinkAsync`
  ROUTE to one owner — the argument is the reason, not taste: a `MemoryRef` names exactly one member, a
  (task, scope) may be held by all of them. A blend where no member can reap **throws**
  `NotSupportedException` naming the members considered, rather than reporting `0`: `PruneAsync` returns a
  count and `0` already means "nothing matched", so a caller reaping for a consent withdrawal must not read
  "nothing here can ever reap" as "done".


- **Every generation backend registers with a CONFIGURE CALLBACK, like the rest of the library.**
  `AddOpenAiImageProvider`, `AddAutomatic1111Provider`, `AddComfyUiProvider`, `AddFalProvider` and
  `AddLocalDiffusionProvider` now take `Action<TOptions> configure` instead of a constructed options object —
  the same shape as `AddOpenAiCompatibleProvider(id, o => …)` on the LLM side and `AddMemoryEngine(name,
  e => …)` in 3.0's memory work.

  <!-- compile-skip: a side-by-side before/after pair, and the "before" half is the 2.5 API -->
  ```csharp
  // before                                        // after
  .AddOpenAiImageProvider(new OpenAiImageOptions    .AddOpenAiImageProvider(o =>
      { BaseUrl = "…/v1", ApiKey = key })               { o.ApiKey = key; })
  ```

  <br>The four HTTP options types became mutable classes (they were records with `init` members), and the
  three `required BaseUrl` members gained the default their own documentation already named — the backend's
  conventional local URL, or the vendor's API root. **A registration that previously had to state a base URL
  now takes that default instead of failing to compile**, and `with` expressions and value equality on those
  four types no longer work. A blank base URL still reports `NotConfigured` at render time.
  <br>The registration keeps the instance the callback configured, so paths that only exist after a setup step
  can be set afterwards (`b.AddLocalDiffusionProvider(o => opts = o)`).

- **`MemoryRetentionPolicy` is now `MemoryEvictionPolicy`, and `LyntaiOptions.MemoryRetention` is <!-- drift-ok: an entry announcing a rename names the old name -->
  `LyntaiOptions.MemoryEviction`** — a rename with no behaviour change, resolving a name collision this
  release created. 3.0's graph engine introduced `IMemoryRetentionPolicy`, which *lengthens* a memory's
  half-life; the storage type *removes* entries from `IMemoryStore`. The two most opposite operations in the
  subsystem sat one `I` apart, and in .NET `IFoo` reads as the interface of `Foo`.
  <br>The storage side moved because it was already surrounded by the other vocabulary —
  `MemoryEvictionMode`, `MemoryEviction.Survivors` and `LYNTAI_MEMORY_EVICTION` all predate the rename — and
  because renaming the seam instead would have broken the one `IMemory<Domain>Policy` shape that the same
  release established. The policy's own `Eviction` property becomes `Mode`, which no longer stutters against
  its type.
  <br>**Nothing else changed:** same namespace (so no `using` moves), same presets, same 500-entry per-scope
  FIFO default, same `LYNTAI_MEMORY_*` environment variables, same eviction behaviour. No schema change.
  <br>**Evidence the collision was already costing something:** `docs/memory.md` listed <!-- drift-ok: as above -->
  `MemoryRetentionPolicy` as the *default implementation* of the retention seam <!-- drift-ok: as above --> — it is neither
  the seam nor an implementation of it. The `retiredTerms` rule added with this rename surfaced that line.
  <br>Both names are now fenced: `check-api-vocabulary` fails on the old identifier reappearing in the public
  surface, `check-docs` on it reappearing in maintained prose. `docs/DECISIONS.md` D13.
- **An authoritative memory now takes a slot WITHIN a recall's limit instead of being cut by it — the
  library's highest-priority promise was measured for the first time and was not being kept.** Design §5.7.0
  orders the engine's objectives lexicographically, and objective (1) — *never lose an authoritative fact* —
  is the only one with **no acceptable failure rate**. A recall re-admitted exact facts the query had not
  matched, then appended them AFTER the ranked set, where `Take(limit)` cut them. Measured on a corpus
  carrying graded material for the first time: **all three authoritative facts lost, in all five languages.**
  <br>**This was documented behaviour in four places, not a slip.** The argument was that letting an exact
  fact survive the limit "would let one authoritative entry evict every ordinary hit" — true, and it loses to
  objective (1), which does not trade. What changes is the ordering *within* the limit; nothing changes about
  what is admitted, since admission comes from the grade carve-out and never from relevance.
  <br>**Consumer-visible effect:** a recall with a small `limit` against a store holding authoritative
  material now returns fewer ordinary hits — the exact facts displace them. Recalls against a store with no
  authoritative entries are byte-identical, which is every consumer who has not used `MemoryGrade`.
  <br>**The eviction objection is bounded rather than dismissed**: new
  `GraphMemoryOptions.AuthoritativeReserve` caps how many slots exact facts may take (`null`, unbounded, is
  the default). The promise then degrades to "an exact fact is displaced only by ANOTHER exact fact" rather
  than to nothing. Setting it to `0` restores the pre-3.0 behaviour exactly — and re-breaks objective (1),
  which is why it is not the default.
  <br>No schema change and no migration: the grade was always stored, only the ordering was wrong.
- **`IMemoryGraphStore` gains two more REQUIRED members — `RecordSubjectsAsync` and `NodesBySubjectAsync`.**
  A BYO store must now implement **five** additions in this release (these two plus `DeleteAsync`,
  `RecordReviewsAsync` and `ReviewsAsync`). Both are a compile error naming the member, never a silent
  binding.
  <br>They exist because subject linking cannot be done by searching. Linking a fact to what a SEARCH for its
  subject finds needs some entry to name that subject in its own text — and the case that matters has none:
  "the spouse is Alice", "the deploy key is in the vault" and "the client is northern logistics" are all
  about the same owner, and none of them contains "owner". That is exactly the shape the measured no-graph
  floor comes from, so a search-based version would have passed every unit test and moved no measurement.
  <br>A store that does not care about annotation can implement both as no-ops (`Task.CompletedTask` and an
  empty list); the engine then behaves exactly as it does with no annotator registered. **Subjects steer
  LINKING and never recall**, so a no-op costs nothing but the feature itself.
  <br>Schema: one table (`lyntai_memory_subject`) and one index on both relational backends, folded into
  3.0's single memory migration. Purely additive — visible in the regenerated schema goldens.
- **Provenance: each memory now tags which policy computed its persisted state (design doc §5.7, Task 4),
  answering "is this entry fit for the current policy set" instead of guessing.** The concrete case, as it
  stood when this landed: our own `DsrRetrievability` was a PARTIAL FSRS — real FSRS updates difficulty on
  every review, ours only took `MemorySignals.WellKnown.Difficulty` out of the signals bag. A model that
  DOES maintain difficulty needs to tell "never computed" apart from "computed as zero", and a bare number
  cannot — provenance is what makes that distinguishable. (`DsrRetrievability` became exactly that model
  later in this same cycle — see `MemoryDecayState`'s seventh member below — which is the case FOR the
  column, not against it: rows written by the earlier policy are now distinguishable from rows written by
  the later one.)
  <br>**Only the two domains that WRITE persisted state a later policy might need and not find get a
  column**: retrievability (`Stability`) and salience (the signals bag). Age's three primitives are written
  UNCONDITIONALLY regardless of which policy is installed, so every age policy can always derive its own
  view — there is nothing to be unfit for. Retention is read-only and persists nothing. Neither gets a
  column; their absence is a design conclusion, not an oversight.
  <br>**`IMemoryRetrievabilityPolicy` and `IMemorySaliencePolicy` each gain a required `Provenance` member**
  (`MemoryRetrievabilityProvenance`/`MemorySalienceProvenance` respectively — `[Flags] : long`, singular
  names: provenance is a mass noun, and `…Policies` would sit one character from `IMemoryRetrievabilityPolicy`)
  — source-breaking for a third-party implementation, no default. `HalfLifeRetrievability` declares
  `HalfLife = 0x1`, `DsrRetrievability` declares `Dsr = 0x2`, `StructuralSaliencePolicy` declares
  `Structural = 0x1`; <!-- drift-ok: an amendment naming what it corrects -->
  **(SUPERSEDED: the curve declaring `HalfLife = 0x1` was deleted later in this section. The BIT is retired
  rather than reused — a stored row written by it must stay distinguishable — so 3.0 ships `Dsr = 0x2` as the
  only live retrievability provenance.)** `ModulatedRetrievability` forwards to its wrapped policy unchanged (modulation is a
  read-time view, so whichever policy actually computed the stored state is the one provenance must credit).
  Bits 0-31 are reserved for this library; bits 32-62 are open for a consumer's own policy — cast any single
  bit in that range to the enum, exactly as a named member is. **Bit 63 is never set**: SQLite `INTEGER` and
  Postgres `BIGINT` are both signed 64-bit integers, so a top-bit value would round-trip negative — equality
  would still work (a fitness check would look correct) while ordering, range queries and indexes misbehave.
  <br>**`Lyntai.Memory.MemoryProvenance` owns every bit operation on a provenance value** — `Pack` (OR
  several policies' contributions into one, masked so bit 63 can never appear in the result — structural,
  not a runtime check), `Unpack` (the same mask, applied defensively on read) and `Fits(stored, required)`
  (`(stored & required) == required`). No inline bit math appears anywhere else: one stored value read
  several ways with several different rules is the exact defect that produced `MemorySignals.Salience`.
  Gated by two facts C# does not enforce on its own — `MemoryProvenanceTests` asserts every shipped policy's
  bit is unique and single (`BitOperations.PopCount == 1`), and demonstrates the check catching both a
  duplicated bit and a two-bit member.
  <br>**`GraphNode` gains `ProvenanceRetrievability`/`ProvenanceSalience` (`long`, defaulting to `0`/`None`,
  appended after `ElapsedAge`); `GraphNodeWrite` gains the same two; `GraphTouch` gains
  `ProvenanceRetrievability`.** Additive in SOURCE (every existing positional construction still compiles)
  but BINARY-breaking for the same reason the age primitives were: the constructor and `Deconstruct` both
  change shape. `GraphMemoryEngine.RememberAsync` stamps the active retrievability policy's own `Provenance`
  plus the OR of every salience appraiser that returned a NON-EMPTY result (one that declined or threw
  contributes nothing — provenance records who PRODUCED a signal, not who merely ran); `ReinforceAsync`
  re-stamps retrievability provenance on every touch. **A plain re-remember of identical content never
  revisits `ProvenanceRetrievability`**, mirroring `Stability` itself (neither is in a store's refresh
  branch); **an empty incoming salience bag never blanks `ProvenanceSalience`**, mirroring `Signals`' own
  "keep what's stored" rule, while a fresh non-empty appraisal replaces it exactly as it replaces the bag.
  <br>**Schema** (in `M202608121100_MemoryRetentionModel`, the single 3.0 migration — see the squash entry at
  the top of this section): `provenance_retrievability`/`provenance_salience` on
  `lyntai_memory_node`, `NOT NULL DEFAULT 0` — `0`
  (`None`) is not a neutral stand-in here, it is the honest fact: no policy computed anything for a row that
  predates this migration, and nothing in the table records which policy to attribute retroactively, unlike
  the age primitives that could be derived exactly. No index (nothing sorts or filters on either column) and
  no `CHECK` constraint (this repository has zero today; the guard lives in `MemoryProvenance`, not the
  schema).
- **`IMemoryGraphStore` gains a required `DeleteAsync(engine, ids, ct)`.** Unlike the additions below it
  carries **no default**, so a custom store implementation stops compiling until it adds one — the honest
  shape, because a store that silently no-ops here would leak entries the engine believes it deleted.
  <br>It exists because `PruneAsync`'s ratio filter cannot express *"delete exactly these ids, which the
  caller has already evaluated"*. Once an age policy can DERIVE its age from the stored primitives rather
  than read the accumulator, prune can no longer be a store-side ratio comparison: the engine has to
  evaluate the same `Retrievability` function recall uses and then remove precisely what failed. Pushing
  that arithmetic into the store instead was rejected — `ElapsedAgePolicy` needs .NET date arithmetic no
  portable SQL expresses.
  <br>**The bug it fixes deletes data.** Before this, prune compared against the raw accumulator while
  recall used the composed age, so after a policy swap the engine rated an entry retrievable and prune
  reaped it anyway — measured at **49 wrongful deletions** in the swap scenario now pinned by
  `Prune_agrees_with_recall_after_a_policy_swap_rather_than_reaping_the_stale_accumulator`. The engine still
  takes the cheap store-side path when no derivable policy is registered, where it is provably exact.
- **`MemoryDecayState` gains a sixth member, `Signals` (`MemorySignals`, defaulting to empty).** Every
  existing construction still compiles — it is the last parameter and carries a default — and decays
  identically, since an empty bag is neutral to every modulator. A custom `IMemoryRetrievabilityPolicy` that
  positionally deconstructs the record must account for it.
- **`GraphNodeWrite` and `GraphNode` (`IMemoryGraphStore`) each gain a trailing `Signals` parameter.** Same
  shape: defaulted, so existing callers and a positional `Deconstruct` both still compile. Every built-in
  backend (InMemory, SQLite, Postgres) persists it as of this release; a custom `IMemoryGraphStore`
  implementation should start persisting it too.
- **The graph-memory retention types moved out of the flat `Lyntai.Memory` namespace into four sub-namespaces
  matching their domain: `IMemoryAgePolicy`/`MemoryTick`/`PerWriteAgePolicy`/`ContentSizeAgePolicy`/`ElapsedAgePolicy`/
  `BurstDampenedAgePolicy` are now `Lyntai.Memory.Interference`; `IMemoryRetrievabilityPolicy`/`HalfLifeOptions`/
  `HalfLifeRetrievability` are now `Lyntai.Memory.Forgetting` (the latter two moved here mid-window and are
  then DELETED further down this same section — net effect for a consumer: they are gone, not relocated);
  `IMemoryRetentionPolicy`/
  `ModulatedRetrievability`/`SalienceRetentionPolicy` are now `Lyntai.Memory.Modulation`; `IMemorySaliencePolicy`/
  `SalienceContext`/`SalienceOptions`/`StructuralSaliencePolicy` are now `Lyntai.Memory.Salience`.** No
  type's shape changed — a consumer's fix is an added `using`, never a code change. `MemoryDecayState`,
  `MemorySignals`, the engine contract, the storage contract and the vector/semantic surface all stay at the
  root `Lyntai.Memory`. **The four seam interfaces carry their FINAL names here**, because the same
  unreleased window also unified all five policy seams onto one `IMemory*Policy` shape (see the entry below)
  — so this section describes where consumers actually land, not the intermediate names. **Two of those four
  old names DID ship and do break a 2.5 consumer**: `IMemoryClock` and `IRetrievabilityPolicy` are both in
  `v2.5.0`. `IRetentionModulator` and `ISalienceAppraiser` are the two that never shipped under any name —
  they were working names mid-window for capabilities 2.5 did not have. Corrected 2026-08-11: this paragraph
  previously said all four never shipped, which told the one audience it is written for that the only two
  renames affecting them were not their problem. <!-- drift-ok -->
- **The four remaining policy seams are now one naming shape, matching `IMemoryRankingPolicy`.**
  `IMemoryClock` → `IMemoryAgePolicy` (there is no clock — age is a monotone position delta, and the old name
  said otherwise), `IRetrievabilityPolicy` → `IMemoryRetrievabilityPolicy`, `IRetentionModulator` →
  `IMemoryRetentionPolicy`, `ISalienceAppraiser` → `IMemorySaliencePolicy`. Every registered implementation
  whose own name embedded the retired word renamed with it: `PerWriteClock`/`ContentSizeClock`/`ElapsedClock`/
  `BurstDampenedClock` → `PerWriteAgePolicy`/`ContentSizeAgePolicy`/`ElapsedAgePolicy`/`BurstDampenedAgePolicy`,
  `SalienceModulator` → `SalienceRetentionPolicy`, `StructuralSalienceAppraiser` → `StructuralSaliencePolicy`.
  Purely mechanical — no behaviour changed, and every existing test asserts exactly what it did before, under
  the new name (`docs/DECISIONS.md` — the `IMemory<Domain>Policy` naming shape). `ModulatedRetrievability`, `HalfLifeRetrievability` and
  `DsrRetrievability` keep their names: `Retrievability` was never the retired word, only the `I…Policy`
  interface shape was.
- **Three constructor parameters renamed to finish that sweep, and the surrounding prose with them:**
  `GraphMemoryEngine(ageClocks:)` → `agePolicies:`, `GraphMemoryEngine(appraisers:)` →
  `saliencePolicies:`, and `ModulatedRetrievability(modulators:)` → `retentionPolicies:`.
  <br>**Only ONE of the three can reach a 2.5 consumer, and `docs/migration-2.5-to-3.0.md` says so rather
  than listing all three as migration steps.** Checked against the `v2.5.0` API baseline: that release's
  `GraphMemoryEngine` constructor took eight parameters ending at `vectors`, with **no `appraisers`** and no
  ranking or composition seams, and **`ModulatedRetrievability` did not exist at all**. So `appraisers:` and
  `modulators:` were both introduced AND renamed inside this unreleased window — no consumer ever wrote
  them, and presenting them as 2.5→3.0 steps would have been false. The real migration step is
  `memoryClock:` → `agePolicies:` (2.5's parameter was `IMemoryClock memoryClock`, singular). Where any of
  the three does bite, it is **source-breaking only for a caller using NAMED arguments** — positional
  callers and every DI consumer are unaffected.
  <br>The seam rename above reached the types but stopped at the parameters, so each one still spoke the
  word its own seam had retired: `ageClocks` said **clock** on a seam whose stated reason for renaming was
  *"there was never a clock — age is interference"*; `appraisers` and `modulators` said **appraiser** and
  **modulator** after `ISalienceAppraiser` and `IRetentionModulator` had become `IMemorySaliencePolicy` and
  `IMemoryRetentionPolicy`. `ageClocks` was also actively ambiguous: the same parameter list ends with
  `Func<DateTimeOffset>? clock`, a genuine wall clock, so the parameter named "clocks" was the one that is
  not a clock.
  <br>**Every `Func<DateTimeOffset> clock` parameter in the library is untouched and correct**, and
  `ModulatedRetrievability` and the `Lyntai.Memory.Modulation` namespace keep their names — the `IMemory<Domain>Policy` naming shape retired
  those words as names for the POLICY SEAMS, never as concepts. The XML documentation and the design record
  were swept for the same vocabulary, because `repo-mechanics.md` §Naming says prose seeds a retired name
  back in on the next change, and that is demonstrably what happened here: the parameters were named after
  the prose.
  <br>Done now because named arguments are public surface and 3.0 is the window; after the freeze this costs
  a major. Caught by a whole-branch review rather than by a gate — `check-docs` deliberately excludes
  `src/`, and the API-surface baseline records parameter names without judging them, so a stale name
  round-trips through the very gate that exists to notice API changes.
- **`IMemorySaliencePolicy.Appraise` is renamed `Signals`** — source-breaking for anyone implementing that
  seam, which is new in 3.0, so no 2.5 consumer can be holding it. Every other seam method in this domain is
  named for **what it returns** (`IMemoryRetrievabilityPolicy.Retrievability`, `IMemoryAgePolicy.Age`,
  `IMemoryRetentionPolicy.StabilityFactor`, `IMemoryRankingPolicy.Rank`); `Appraise` alone was named for the
  ACT, in the verb form of the retired `ISalienceAppraiser`, while returning `MemorySignals`. Its own XML
  summary already read *"Signals for this write"* — the documentation had settled on the return-shaped name
  and only the identifier lagged.
  <br>**How it survived the the `IMemory<Domain>Policy` naming shape sweep is the part worth recording.** It was kept by an implementer's
  judgement; a reviewer then flagged that it was *missing* from the `IMemory<Domain>Policy` naming shape's "deliberately unchanged" list — kept,
  but never recorded as deliberate — and the rationale that justified keeping it was composed while closing
  that review finding, then repeated elsewhere as settled. It became "deliberate" at the moment it was
  written down. Naming is never measurable, but it was CHECKABLE against a pattern the codebase already
  had, and checking beats composing a justification.
- **A ranking score can no longer reach `+Infinity` from FINITE inputs — fixed in all THREE policies**, not
  the two the defect was filed against. Each policy now drops a candidate whose own computed score is
  non-finite, closing the poisoned-PRODUCT class the earlier input filter could not: `Relevance = 1e308` and
  `Retrievability = 1e308` are each finite and admitted, and their product is not.
  <br>**`CompositeRankingPolicy` carried the same latent route as `ReciprocalRankFusionPolicy`, and both fail
  WORSE than the product case.** Both argued inline that their score was "finite by construction — a sum of
  positive, bounded reciprocal terms", which holds at shipped weights and is false in general: every weight is
  validated finite and `>= 0` with **no upper bound**, so two terms of `double.MaxValue / 1.5` overflow their
  sum. And because both ship `RelativeFloor = 0`, `+Infinity × 0` is `NaN` — the recall came back **completely
  empty**, measured, rather than collapsing to the corrupted entry. **Not reachable from anything shipped**
  (every shipped store reports `Relevance` in `(0,1]`, every shipped curve clamps to `[0,1]`), so this is
  BYO-only exposure — fixed anyway, and the docs that over-claimed the class was closed are corrected,
  including a contract fact that called RRF safe "academically".
  <br>Both filters earn their keep: the input filter is now redundant for `MultiplicativeRankingPolicy` but
  still **required** by RRF and Composite, which never read the raw fields into their score.
- **A stability ceiling now caps GROWTH and can no longer CUT what is already stored** — a behaviour change
  for any deployment holding a stability above its configured `DsrOptions.MaxStability`. `Reinforce` ended in
  `Math.Min(grown, MaxStability)`, so reinforcing an over-ceiling entry returned a value **below its own
  input**: a stored `100000` came back as `2000`, a **50× shortening**, reproduced live against a running
  engine and persisted by `TouchAsync`. That violated `IMemoryRetrievabilityPolicy.Reinforce`'s own written
  guarantee — *"must never be smaller than the current one"* — which the seam's doc had been disclosing as an
  exception rather than fixing. **The exception is gone from that doc; a stale carve-out in a contract is
  worse than none.** An over-ceiling entry is now FROZEN (it cannot grow further) rather than truncated,
  which is what the ceiling is documented to be for: stopping unbounded compounding, not cutting what exists.
  Reachable by lowering the ceiling under an existing corpus, or by any stability written outside the policy.
  <br>The fix is the shape `EffectiveStability` already used one method away in the same file —
  `Math.Max(stability, Math.Min(…, MaxStability))`. **No migration:** a stored value that was previously
  about to be cut is simply left alone.
  <br>**Why the guard that existed did not catch it, which is the part worth keeping:**
  `RetrievabilityPolicyContract.Reinforcement_never_shortens_a_memory` ran exactly one fixture, at
  `InitialStability` — *structurally below any ceiling*, so it could never reach the failing case. The
  guarantee was false and its own contract fact passed every run. It now also reinforces `1e6` at four ages.
  **A monotonicity guarantee needs a fixture that can actually violate it**, and `Math.Min` alone silently
  implements "is never above X" when the promise was "cannot grow beyond X" — two different claims
  (`.claude/knowledge/pitfalls.md`).
- **Five `DsrOptions` that shipped unguarded now throw on construction** — `MaxStability` (finite, `> 0`),
  `ConnectionBoost` (finite, `>= 0`), `MaxConnectionBoost` (finite, `>= 1`), `EdgeHalfLife` (finite, `> 0`)
  and `ReinforceGain` (finite, `>= 0`), matching the five that were already guarded in the same record.
  **`MaxStability = NaN` was silent, permanent data corruption from a public option**: `NaN` propagates
  through `Math.Min`, `GraphMemoryEngine` feeds `Reinforce`'s return straight into `TouchAsync`, and the
  written-back `NaN` then compares false against every threshold — so the entry neither ranks, nor prunes,
  nor reports as broken. Reachable by CONFIGURATION, not only by a BYO policy.
  <br>**What the other four actually do is quieter and worth stating precisely**, because the obvious reading
  is wrong: a negative `ConnectionBoost` cannot shorten a half-life (`EffectiveStability` floors at the stored
  value), a `MaxConnectionBoost` below 1 is silently floored to 1 by both readers, and a negative
  `ReinforceGain` cannot shrink stability (`Math.Max(0, increase)`). They **switch the mechanism off while
  every call site still reads as configured** — a knob that means a different number than it says is harder
  to diagnose than either a throw or a visible reduction.
- **`MemoryEngineBuilder.UseGraph` gains an optional trailing `IMemoryRetrievabilityPolicy? policy`** — the
  forgetting curve is selectable per engine, so one process can run two graph engines on two curves.
  Source-compatible, **binary-breaking** (`docs/DECISIONS.md` — the per-engine forgetting curve), and taken inside this window purely
  because it costs nothing here and a whole major afterwards. `ranking` was already per-engine while the
  curve was not, and under the singular-vs-plural seam rule those are the same class of seam — the two SINGULAR ones. `retrievability: null`
  resolves exactly as before. The argument substitutes at the inner resolution, inside the
  `ModulatedRetrievability` wrapper: handing it to the engine's own curve slot would have compiled,
  passed every selection test, and silently dropped retention modulation for exactly the engines that named a
  curve (the per-engine forgetting curve's amendment records that its own first wording said to do that).
- **`IMemoryAgePolicy` gains a second member, `Age(MemoryAgeSample sample)` — a source-breaking change for a
  third-party implementation.** The seam's ONLY member used to be `Advance`, a write-time judgment; `Age` is
  its read-time counterpart, projecting a policy's own view of an entry's age from three primitives a store
  now tracks unconditionally (`MemoryAgeSample.Ordinal`/`.Volume`/`.ElapsedDays` — writes, characters and real
  time since the entry was last used, design doc §5.7). All four shipped implementations project the primitive
  their `Advance` already measures (`PerWriteAgePolicy` → `Ordinal`, `ContentSizeAgePolicy` → `Volume /
  perUnit`, `ElapsedAgePolicy` → `ElapsedDays`); `BurstDampenedAgePolicy` delegates to its wrapped policy,
  documented as the one exception — burst damping depends on the timing of every intervening write, which two
  snapshots cannot reconstruct, and that limitation is not new here, only newly nameable.
  **`GraphNode` gains three matching fields, `OrdinalAge`/`VolumeAge`/`ElapsedAge` (all `double`, defaulting to
  `0`, appended after `Signals`) plus a computed `AgeSample` property — additive in SOURCE, but BOTH the
  constructor and `Deconstruct` change SHAPE, which is not "purely additive" once binary compatibility is the
  bar.** Every existing positional CONSTRUCTION still compiles (C# bakes trailing-default-parameter call
  sites in at compile time) but is BINARY-breaking: a caller compiled against the old constructor calls the
  old overload, which no longer exists once this ships. `Deconstruct` gained the same three `out` parameters
  — SOURCE-breaking for any positional deconstruction (`var (id, engine, …) = node;` now needs three more
  variables or a discard) as well as binary-breaking, since nothing in this tree deconstructs `GraphNode`
  positionally to have caught it otherwise. **`GraphNode.Age` and `GraphNodeWrite.Advance` are UNCHANGED**: the pre-existing, single
  `Advance`-driven accumulator (`lyntai_memory_position.position` / `lyntai_memory_node.last_recalled_position`)
  keeps meaning exactly what it means today, on all three backends. As landed here the primitives were a
  genuinely independent, coexisting view a store ALSO maintains, not yet consumed anywhere — wired into
  `GraphMemoryEngine`'s actual retrievability computation by the entry below, in the same unreleased window, so
  no default's numbers moved either way (proven by a corpus-replay identity test for `PerWriteAgePolicy`,
  `tests/Lyntai.Tests/Memory/MemoryAgePrimitiveIdentityTests.cs`, and by every pre-existing memory test passing
  untouched, including the burst-damping ones).
- **Age and salience are now plural, matching `IMemoryRetentionPolicy`'s existing shape — several coexisting
  policies rather than one swappable one, because writes/characters/elapsed-time and
  structural-novelty/semantic-weight/explicit-marking are different ASPECTS of "how much happened" and "how
  strongly encoded", not competing answers to the same question.** `GraphMemoryEngine`'s constructor:
  `memoryClock` (`IMemoryAgePolicy?`) is now `ageClocks` (`IEnumerable<IMemoryAgePolicy>?`); `appraiser`
  (`IMemorySaliencePolicy?`) is now `appraisers` (`IEnumerable<IMemorySaliencePolicy>?`). Both are
  source-breaking for a positional or named-argument caller passing a single instance — wrap it in a
  collection: `memoryClock: new PerWriteAgePolicy()` becomes `ageClocks: [new PerWriteAgePolicy()]`.
  **SUPERSEDED — `ageClocks:` and `appraisers:` were renamed again before release** (see "Three constructor
  parameters renamed to finish that sweep" below): the 3.0 names are `agePolicies:` and `saliencePolicies:`,
  so a 2.5 consumer's one real migration step is `memoryClock:` → `agePolicies:`. Both retired parameter
  names are now fenced by `check-api-vocabulary`. <!-- drift-ok: an amendment naming what it corrects --> `null` or
  an empty sequence still takes the engine's own default (one burst-damped per-write clock; one
  `StructuralSaliencePolicy`) exactly as before — composing a one-element list is the identity, so a consumer
  registering exactly one `IMemoryAgePolicy` via DI sees no behaviour change (it carries no default
  registration either way, before or after this task).
  **`IMemorySaliencePolicy` is DIFFERENT, and this is a real, ordering-dependent behaviour change (fix round
  1, I-2) — not "no behaviour change" as this entry first, wrongly, claimed.** Its default
  (`StructuralSaliencePolicy`) is seeded by `TryAddSingleton`, and moving its resolution from `GetService`
  (returns the LAST registration, so registering after `AddLyntai` used to WIN cleanly either way) to
  `GetServices` (returns EVERY registration) means the two orderings now genuinely differ: registered BEFORE
  `AddLyntai`, a consumer's own appraiser still replaces the default outright (`TryAddSingleton` sees an
  existing registration and never seeds one); registered AFTER, the default is already seeded, so the
  consumer's own registration ADDS alongside it rather than replacing it, and both run, composed by
  `MaximalSalienceCompositionPolicy`. A consumer who wants a pure replacement registers before `AddLyntai`, in
  either direction. `GraphMemoryWiringTests` now pins both orderings explicitly.
  **Every plural domain — including the two above, and the ALREADY-plural `IMemoryRetentionPolicy` — now
  routes its combination rule through a named, swappable composition policy instead of a hardcoded formula**:
  `IMemoryAgeCompositionPolicy`/`SummedAgeCompositionPolicy` (`Lyntai.Memory.Interference`; sums positions and ages,
  multiplies encodings), `IMemoryRetentionCompositionPolicy`/`MultiplicativeRetentionCompositionPolicy`
  (`Lyntai.Memory.Modulation`; `ModulatedRetrievability` gains an optional third constructor parameter,
  `composition`, defaulting to this — the exact multiply it already did, now named and replaceable), and
  `IMemorySalienceCompositionPolicy`/`MaximalSalienceCompositionPolicy` (`Lyntai.Memory.Salience`; per signal name,
  the largest value any appraiser reported). Every shipped default composition reduces a one-element (or
  empty, for retention) input to the pre-existing behaviour exactly, which is what keeps every default
  unchanged by this.
  **Age becomes derived for the first time in production, honouring the `Kind` each policy declares** —
  `IMemoryAgePolicy` gains a `MemoryAgeKind Kind { get; }` member (`Derivable` or `Accumulating`; source-breaking
  for a third-party implementation) and `Advance` gains a second parameter, `string engine` (also
  source-breaking). `GraphMemoryEngine.Retrievability`/`ReinforceAsync` now read each registered policy's OWN
  resolved age — projected from the primitives (`GraphNode.AgeSample`) for a `Derivable` policy, or the store's
  `Advance`-driven accumulator (`GraphNode.Age`, unchanged) for an `Accumulating` one — composed into one value
  by the age composition policy. `PerWriteAgePolicy`, `ContentSizeAgePolicy` and `ElapsedAgePolicy` are
  `Derivable`; `BurstDampenedAgePolicy` — the engine's shipped DEFAULT — is `Accumulating`, because burst
  damping's position advance depends on the timing of every intervening write, which no snapshot of the
  primitives can reconstruct (documented on `BurstDampenedAgePolicy.Age` since the primitives shipped). **The
  default's numbers are therefore still exactly unchanged**: a one-policy, `Accumulating` default composes to
  precisely `GraphNode.Age`, byte for byte — pinned by
  `MemoryDecaySimulationTests.A_bulk_ingest_does_not_wash_out_what_was_already_known`, which fails immediately
  under mutation if the `Kind` routing is ever bypassed.
  **Schema** (in `M202608121100_MemoryRetentionModel`, the single 3.0 migration):
  `encoding_ordinal`/`encoding_chars`/`encoding_at` on `lyntai_memory_node` and `ordinal`/`chars`/`encoded_at`
  on `lyntai_memory_position`, all `NOT NULL` with a computable default — never nullable, the same reason
  that migration gives for `salience`. Backfilled EXACTLY for existing rows from data already
  in the table: each node's own `(created_at, id)`-ordered position within its engine and the running sum of
  `LENGTH(content)` in that order — ordinals need only be monotone, not dense, so a later deletion leaves a
  harmless gap rather than forcing a renumbering.
- **`GraphMemoryOptions.HopAttenuation`, `.RelativeFloor` and `.SalienceRankWeight` are removed** — ranking is
  now a swappable seam, `IMemoryRankingPolicy` (`Lyntai.Memory.Ranking`), and `GraphMemoryEngine.RecallAsync`
  calls whatever is registered instead of hardcoding the formula against its own options. The shipped default,
  `MultiplicativeRankingPolicy`, computes the SAME score for every candidate as the formula it replaces — same
  `Relevance × Retrievability × boost × HopAttenuation^hop`, then the same relative floor — and the three
  constants moved verbatim onto its own `MultiplicativeRankingOptions`, with the same names and the same
  defaults (`HopAttenuation = 0.5`, `RelativeFloor = 0.02`, `SalienceRankWeight = 0`). **That identity is
  scoped to the POLICY, not the engine's composed recall order**: with two or more `Authoritative` candidates
  below the floor, the old inline code kept them in the SAME rank-sorted list as everything else, so their
  relative order among themselves was still by score; the new engine re-admits them separately, appended
  after the policy's own order, so with two or more they can come back in a different relative order than
  before (one candidate is unaffected either way). This is inherent in having a policy the engine cannot ask
  "what score did you privately compute for the thing you dropped" — not something a future engine change
  should try to restore. **That reorder is not merely cosmetic**: `ReinforceAsync` writes symmetric
  co-activation edges pairwise across the top `CoActivationCap` entries in THIS order, so a changed relative
  order among re-admitted entries changes which edges get PERMANENTLY written to the store, not just what a
  reader of the returned list sees. `MultiplicativeRankingOptions` also validates its three properties at construction
  (`ArgumentOutOfRangeException` on e.g. `HopAttenuation` outside `(0, 1]`); `GraphMemoryOptions` never
  validated them, so a previously-silent `new GraphMemoryOptions { HopAttenuation = 1.5 }` now throws at the
  composition root instead of quietly producing nonsense rank.

  A consumer who never set any of the three needs no code change (the validated defaults equal the old
  unvalidated ones). One who did:
  <!-- compile-skip: a before/after migration pair — the "before" half names the three properties on
       GraphMemoryOptions precisely because they are gone -->
  ```csharp
  // before
  services.AddLyntai(b => b.AddMemoryEngine("m", e => e.UseGraph(
      new GraphMemoryOptions { SalienceRankWeight = 1.0, RelativeFloor = 0.1 })));

  // after
  services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy(
      new MultiplicativeRankingOptions { SalienceRankWeight = 1.0, RelativeFloor = 0.1 }));
  services.AddLyntai(b => b.AddMemoryEngine("m", e => e.UseGraph()));
  ```
  Register `IMemoryRankingPolicy` before or after `AddLyntai` — `AddMemoryEngine` seeds the default with
  `TryAddSingleton`, so a consumer's own registration always wins, the same promise `IMemorySaliencePolicy` and
  `IMemoryRetrievabilityPolicy` already make. **Not a new guarantee, a strengthened one**: the old inline code
  already kept every `Authoritative` candidate regardless of the floor (`Grade == Authoritative || Rank >=
  floor`); what is new is that the guarantee now holds against a policy that DROPS a candidate, including a
  third-party one the library did not write and that has never heard of grades — the engine re-admits any
  candidate the policy dropped, appended after the policy's own order, rather than trusting the policy to
  honour the exemption itself. **Precisely scoped**: the re-admission check is keyed on `Node.Id` alone, so
  it is a guarantee against a policy that drops an authoritative candidate, not against one that substitutes
  a fabricated entry under the same id instead — see `IMemoryRankingPolicy`'s own remarks.
- **`Stability` now means exactly one thing, enforced rather than merely documented: the position delta at
  which retrievability is 0.5.** `RetrievabilityPolicyContract` gains `Stability_is_the_position_delta_at_which_retrievability_is_half`,
  run against every shipped implementation. Both curves already conform — `DsrRetrievability` derives its
  curve factor (`F = 0.5^(1/decay) - 1`) precisely so this holds — so the fact PINS existing behaviour
  rather than changing it. FSRS (which the DSR curve adapts) anchors stability at its own 90%-retention
  convention; this library anchors at 50%, and always has. **This one enforced fact is what let an entire
  first-draft design be deleted**: a second convention (a curve preferring FSRS's own framing) can never
  ship without failing this test on the way in, so no stored `Stability` value is ever ambiguous and
  nothing ever needs an adoption/conversion story between two conventions.
- **`IMemoryRetrievabilityPolicy.Reinforce` now returns the FULL `MemoryDecayState`, not a `double`** —
  binary- and source-breaking for a third-party implementation or caller. A scalar return can only ever
  persist `Stability`, which is precisely why `DsrRetrievability` was a PARTIAL FSRS at this point in the
  cycle (real FSRS updates difficulty on every review; a scalar gave that richer model nowhere to put what
  it owns). Returning the state gives a policy room to own more — and `DsrRetrievability` took that room
  later in this same cycle, maintaining `Difficulty` on every review (see `MemoryDecayState`'s seventh
  member below). The store persists whatever comes back, and provenance (the entry above this release
  cycle) already records who computed it.
  <br>**Both curves shipping AT THE TIME computed exactly the same number as before, now wrapped**:
  `HalfLifeRetrievability.Reinforce`/`DsrRetrievability.Reinforce` end in `state with { Stability = grown }`,
  and every other field passes through unchanged — pinned by a new contract fact,
  `Reinforcement_leaves_every_field_it_does_not_own_unchanged`, run against both. **`HalfLifeRetrievability`
  is DELETED later in this same cycle**, so by release there is one curve and this fact runs against it
  alone; the past tense here is the accurate one for an entry describing a step, not the end state.
  <!-- drift-ok: names the deleted curve deliberately, as the state when this change landed --> Every pre-existing
  assertion on the returned number passes unmodified, now spelled `policy.Reinforce(state).Stability`.
  `ModulatedRetrievability.Reinforce` still forwards to the inner policy with the state UNMODULATED — a
  documented, deliberately-unchanged asymmetry (`TASKS.md` tracks it separately) — so it needed only the
  signature change, not a behaviour change.
  <br>**`GraphMemoryEngine.ReinforceAsync` reads `.Stability` off the returned state to build `GraphTouch`.**
  Nothing else is dropped: neither shipped curve sets anything beyond `Stability`, and the contract above now
  requires every field a policy does not own to come back unchanged, so extracting one field here is exactly
  as complete as persisting the whole state — there is nothing else to lose today. A future policy that owns
  a second field (difficulty, say) needs `GraphTouch` widened to carry it before this line can reach it.
  <br>A third-party implementation migrates by wrapping its existing scalar: `double Reinforce(...)` becomes
  `MemoryDecayState Reinforce(in MemoryDecayState state) => state with { Stability = /* the old expression */ };`.
- **`MemoryEngineBuilder.UseGraph` gains two trailing parameters, `ranking` and `namedRankingPolicies` —
  additive in SOURCE (every existing call still compiles) but BINARY-breaking, because C# bakes
  optional-parameter defaults into the call site.** `ranking` lets ONE named engine pick its own
  `IMemoryRankingPolicy`, ahead of whatever is registered in the container — omitted, the container
  registration is still the default, so `UseGraph()` with no arguments behaves exactly as before this
  change. `namedRankingPolicies` gives that engine a small catalog a per-call `MemoryQuery.RankingPolicyName`
  (see below) can select from; a name not in the catalog throws rather than silently using the default.
- **`MemoryQuery` gains a trailing `RankingPolicyName` (`string?`, defaulting to `null`) — additive in
  SOURCE but BINARY-breaking (the constructor and `Deconstruct` both change shape).** Selects an alternate
  ranking policy BY NAME for one call, resolved against the recalling engine's own
  `namedRankingPolicies` (above). Deliberately a NAME, not a policy instance — a live `IMemoryRankingPolicy`
  is a service, and this record is otherwise plain data, serialized/logged/traced. An engine with no ranking
  concept (lexical, semantic, curated) simply ignores the field; only `GraphMemoryEngine` consults it, and an
  unknown name throws `KeyNotFoundException` rather than silently falling back to the engine's default.
- **`DsrRetrievability` replaces `HalfLifeRetrievability` as the REGISTERED default forgetting curve** — a
  behaviour change for every consumer who configures nothing (`MemoryEngineRegistration.AddMemoryEngine` now
  `TryAdd`s `IMemoryRetrievabilityPolicy`; see `docs/DECISIONS.md` — the `DsrRetrievability` default curve for the evidence, and for the one
  measured, known-and-prioritized regression this shipped with — DSR missed more than HalfLife on
  repeated/reused, competing-candidate material under `MultiplicativeRankingPolicy`, offset by the opposite
  pattern on freshly-written material). **The one-line restore this entry originally documented
  (`services.AddSingleton<IMemoryRetrievabilityPolicy>(new HalfLifeRetrievability())`) no longer exists —
  see the very next entry below, which deletes the curve this restored, in the SAME release.** That measured
  regression is exactly why: the flat, unmeasured `× 1.5` reinforcement this comparison caught the exponential
  curve winning with is the mechanism the next entry's own deletion reasoning names.
- **`HalfLifeRetrievability` and `HalfLifeOptions` are DELETED — no restore path.** The curve's own doc
  admitted its central `× 1.5` reinforcement constant was "reasoned, not measured", and a later measurement
  found it compounds to **2.1×** a correctly-behaving curve's stability over a four-touch reuse batch —
  over-crediting massed repetition, the exact behaviour FSRS exists to correct (`TASKS.md` Part 56, FSRS-C).
  `DsrRetrievability` is now the ONLY shipped forgetting curve. There is no
  `services.AddSingleton<IMemoryRetrievabilityPolicy>(new HalfLifeRetrievability())` escape hatch any more —
  the entry above this one documented that exact line, for the release this same changelog section describes
  — because the type no longer exists. A consumer who genuinely needs the old exponential shape has to
  implement `IMemoryRetrievabilityPolicy` themselves; nothing in this library restores it for them.
  <br>**No data migration needed.** `Stability` means one thing across every implementation — the position
  delta at which retrievability is 0.5 — enforced by a contract fact against every shipped curve (Plan 5,
  Task 5), so a 2.5.x row's stored stability is already valid under DSR with no conversion. Pinned by a test
  that writes a node the way 2.5.x actually wrote one (stability in half-life units, the `HalfLife`
  provenance bit set) through a real SQLite round-trip and recalls it correctly under DSR — mutation-checked
  against a changed stability convention.
  <br>**`GraphMemoryOptions.Decay` (`HalfLifeOptions`-typed) is deleted with it** — already silently inert on
  the DI path, since `AddMemoryEngine` registers a default `IMemoryRetrievabilityPolicy` ahead of the
  bare-constructor fallback that used to read it (the entry above). Its one field that was never the curve's
  to begin with, `EdgeHalfLife` (edge-WEIGHT decay, never the curve's own connection boost), moves to a new
  top-level `GraphMemoryOptions.EdgeHalfLife` property with the same default (100) —
  `GraphMemoryOptions { Decay = new HalfLifeOptions { EdgeHalfLife = … } }` becomes
  `GraphMemoryOptions { EdgeHalfLife = … }`.
  <br>**A hand-constructed `GraphMemoryEngine` (bypassing DI) now defaults to `DsrRetrievability` too** — the
  two-defaults split the entry above documented (the bare constructor stayed on the exponential curve while
  DI had already moved to DSR, deliberately, for test stability) is resolved by deletion: there is only one
  curve to default to now.
  <br>**`MemoryRetrievabilityProvenance.HalfLife`'s bit is RETIRED, not reused.** The member and its value
  stay exactly as they were — every row a 2.5.x deployment wrote still carries this bit — but no shipped or
  consumer policy may declare it again; reusing it would silently misattribute a 2.5.x row's state to
  whichever policy claimed the bit next. A fact (`MemoryProvenanceTests`) pins that no shipped policy does.
- **`MemoryDecayState` gains a seventh member, `Difficulty` (`double`, defaulting to `5`, the neutral
  mid-point — corrected 2026-08-11 from an initial `1`, see the correction paragraph at the end of this entry)
  — additive in SOURCE but BINARY-breaking, the same shape every earlier addition to this record has been.**
  `DsrRetrievability` is the first policy to use it: `Reinforce` now MAINTAINS difficulty on every review,
  where it used to only READ `MemorySignals.WellKnown.Difficulty` from the signals bag and never write
  anything back (that PARTIAL-FSRS gap is what `docs/DECISIONS.md` — the `DsrRetrievability` default curve and `TASKS.md` Part 56 FSRS-A
  disclosed; FSRS-A is now closed). **Corrected against primary sources (`py-fsrs/scheduler.py`,
  `fsrs-rs/model.rs`, fsrs4anki v4.7.2, the Anki manual) in a fix round before this shipped** — three
  fidelity defects a first draft carried are named below rather than silently smoothed over.
  <br>**The grade FSRS needs is DERIVED, since nobody grades a graph-memory recall, and is restricted to
  FSRS's SUCCESS sub-range — it never emits `Again`, a LAPSE.** Every graded event here is a success by
  construction (an entry that is not returned never reaches `Reinforce`), so `g = 2 + 2·retrievability ∈
  [2, 4]` (Hard through Easy) stands in for a human's rating — low `r` derives toward Hard, high `r` toward
  Easy, and the no-change reference (`g=3`) lands at `r=0.5`, this library's own half-life anchor. (An
  earlier draft used `g = 1 + 3r ∈ [1, 4]`, which reached `Again` — a lapse this library can never actually
  observe — at the exact retrievability floor, while simultaneously growing stability maximally in the same
  call.) The grade is read from the state BEFORE this reinforcement, never from a value the same call
  produces — the cheap half of the drift guard a SELF-graded curve needs: this is not FSRS's grade (a human
  judging their own recall), it is the curve's own prediction of how retrievable the entry was, and a curve
  that systematically overestimates retrievability could otherwise derive "Easy" and lower its own
  difficulty forever, reinforcing the overestimate. Pinned by
  `The_derived_grade_is_computed_from_the_state_BEFORE_this_reinforcement` and
  `The_derived_grade_never_emits_the_lapse_rating_even_at_the_practical_floor_of_r`
  (`tests/Lyntai.Tests/Memory/DsrRetrievabilityTests.cs`), each mutation-checked.
  <br>**A second, ACROSS-CALL drift the pre-state guard cannot see: a session burst is a free
  difficulty-lowering pump.** A recall does not advance the engine's position, so several recalls of one
  entry with no intervening write all hand `Reinforce` `Age = 0` — which derives Easy every time and lowers
  difficulty every time, purely as a function of recall CADENCE. FSRS's own answer (a same-day/zero-elapsed
  review bypasses the ordinary formulas) is adopted: at `state.Age <= 0`, `Reinforce` now returns
  `Difficulty` UNCHANGED, mirroring the bypass `Stability` already had (an immediate re-recall's spacing
  term is already exactly zero there). Pinned by
  `A_session_burst_with_no_intervening_write_does_not_move_difficulty`, mutation-checked.
  <br>**The update law adapts FSRS-5's `next_difficulty`, with FSRS-6's own recalibrated constants** (FSRS
  v4/v4.5's own law has NO damping term at all; FSRS-5 introduced it and moved the reversion target from
  `D0(3)` to `D0(4)`; FSRS-6 kept that shape): `ΔD = -w6·(g-3)`, linear damping toward the ceiling
  (`D' = D + ΔD·(10-D)/9`), MEAN REVERSION toward a target (`D'' = w7·target + (1-w7)·D'`), clamped to
  `[1, 10]`. **Reversion is RESTORED, not dropped** — an earlier draft dropped it, reasoning (wrongly) that
  it needed a per-grade quantity this library has no analogue for; the target is a PLAIN CONSTANT once the
  grade is fixed at Easy, exactly like every other option in this class, and dropping it made
  `Difficulty = 10` an ABSORBING state (linear damping's own factor is identically zero there, so reversion
  was the only term that could still move it). New `DsrOptions.DifficultyChangeWeight` (`w6`),
  `.DifficultyReversionWeight` (`w7`) and `.DifficultyReversionTarget` (the target, exposed directly since
  this library has no `w4`/`w5` pair to compute FSRS's own `D0` sub-formula from) all adopt FSRS-6's OWN
  published defaults (`3.0194`, `0.001`, `≈-4.77`) rather than invented placeholders — an earlier draft's
  `DifficultyChangeWeight = 0.5` had no such provenance, and disclosing that in the implementer's report
  without also saying so in the doc was itself a defect this fix round closed. FSRS-6's own `w7 = 0.001`
  looks like it disables reversion but the target is computed UNCLAMPED at that magnitude, so there is a
  persistent ~0.015/review downward pull at the ceiling even so — slow, but never exactly zero, a
  qualitative difference from no reversion at all. Pinned by `Difficulty_at_the_ceiling_is_no_longer_absorbing`,
  mutation-checked, plus construction guards on both new options.
  <br>**Two writers, one explicit precedence — corrected to key on the SIGNAL, not on bag emptiness.** A
  write that NAMES a `MemorySignals.WellKnown.Difficulty` signal — a fresh node, or a re-remember whose bag
  carries that specific key — overwrites the live value, so an application's stated judgement always wins;
  between writes, only `Reinforce` (via a touch) moves it further, and never re-reads the bag. **This is
  deliberately NOT `salience`'s own "bag is merely non-empty" trigger**: an earlier draft keyed it that way,
  so a write that appraised salience alone silently reset whatever `Reinforce` had tracked back toward the
  write-time judgement or the neutral default — difficulty has a second writer salience does not, so its
  precedence cannot reuse salience's rule verbatim. Pinned on all three backends by a new
  `MemoryGraphStoreContract` fact, `Re_remembering_with_an_unrelated_signal_does_not_touch_the_tracked_difficulty`,
  mutation-checked.
  <br>**`GraphNode` and `GraphTouch` (`IMemoryGraphStore`) each gain a trailing `Difficulty` (`double`,
  defaulting to `1`) — same additive-source/binary-breaking shape.** `GraphMemoryEngine.ReinforceAsync` now
  extracts BOTH `.Stability` and `.Difficulty` off `Reinforce`'s returned state to build `GraphTouch`, the
  first policy to use the second slot that seam has carried since it started returning the full state.
  <br>**Schema** (in `M202608121100_MemoryRetentionModel`, the single 3.0 migration): `difficulty` on
  `lyntai_memory_node`, `NOT NULL DEFAULT 5` — never nullable, the same reason that migration
  gives for `salience`. (`salience`'s own `DEFAULT 1` is unrelated and stays: `1` genuinely IS neutral on
  that scale. On FSRS's `[1, 10]` difficulty scale `1` is the FLOOR, which is the defect described further
  down this section.) No index: nothing sorts or filters on this column, unlike `salience`. Provenance is
  not duplicated — `provenance_retrievability` already covers this field the same way it covers `Stability`,
  so a row with `None` provenance is how "never computed" is told apart from "computed as neutral" without
  guessing from the value alone; a 2.5-era row (or any pre-Task-2 3.0 row) recalls and reinforces exactly as
  sanely as a freshly-written one.
  <br>**The control for comparing difficulty-live against difficulty-inert DSR needs no second, test-only
  curve type, but (fix round 1) needs BOTH new weights at zero together, not `DifficultyChangeWeight` alone**
  — reversion is a separate force it does not gate: `new DsrRetrievability(new DsrOptions {
  DifficultyChangeWeight = 0, DifficultyReversionWeight = 0 })` writes `Difficulty` back UNCHANGED on every
  review, isolating the one change under measurement.
  <br>**This is still a PARTIAL, UNFITTED FSRS**: no per-review RATING (a derived grade stands in), and
  every constant is FSRS's own published default rather than one fitted against this library's own review
  history — real fitting is design spec §4, not this task.
  <br>**Correction, 2026-08-11 (fsrs-properly plan Task 4 follow-up) — the neutral/absent difficulty value
  was `1`, and that was a defect, not a taste choice: `1` is FSRS's EASIEST value, not "no information".**
  A re-measurement's own exact `0.000 ± 0.000` difficulty-live-vs-inert result (every shape, every class,
  zero seed-to-seed variance) looked like a real null until a diagnostic reading the review log's own
  per-touch trail on a live replay found why: starting every unjudged entry at the floor, combined with a
  derived grade that is overwhelmingly Easy-leaning on a fresh, successful recall (the common case for
  anything actually retrieved), drove the update law's own damping below the floor almost immediately, and
  `Math.Clamp(_, 1, 10)` floored it right back — the corpus's own most-reinforced entries sat at
  `Difficulty ≡ 1` across well over a hundred touches each. **The axis was structurally incapable of
  varying for the population that matters, not merely inert on this corpus by chance.** The neutral is now
  `5` (the mid-point — a STATED CHOICE, not a derivation, since this library has no first rating to derive
  FSRS's own `D0` from) everywhere "absent" is resolved: `MemoryDecayState.Difficulty`'s own default,
  `MemorySignals.Difficulty`'s fallback AND its non-finite coercion (the two are documented as needing to
  agree, since letting them drift apart is exactly how this defect went unnoticed), `GraphNode.Difficulty`/
  `GraphTouch.Difficulty`'s own defaults, and a new `DsrOptions.NeutralDifficulty` (default `5`, guarded to
  `[1, 10]`) that `DsrRetrievability.Reinforce` substitutes for a non-finite incoming `Difficulty` — the
  same substitution pattern `InitialStability` already uses for a non-positive `Stability`. **The clamp
  bounds (`[1, 10]`) and the `g = 2 + 2·r` grade mapping are UNCHANGED** — verified against FSRS's primary
  sources the round before this one; the defect was the starting point, not the transfer function. An
  EXPLICIT out-of-range judgement still clamps to its nearest bound exactly as before (`0`/`-5` still read as
  the floor `1`, `1e9` still reads as the ceiling `10`) — only "no information at all" (absent, or
  non-finite) moved, from the floor to the mid-point.
  <br>**The migration moved with it, and the reason is worth stating because the first attempt got it
  wrong.** The difficulty column originally backfilled `DEFAULT 1` on both backends, and this entry
  originally claimed a row carrying that stored `1` merely "drifts toward the new dynamics as it is touched
  again." **That was false, and in the worst direction: the more a row is recalled, the more firmly it
  stays wrong.** From `D = 1` the damped update escapes the floor only on a recall at `r < 0.499`, while the
  corpus measures **89.6% of derived grades Easy-leaning** (mean `g = 3.81`) — so an *actively recalled*
  migrated row re-clamps to `1` on essentially every touch and is pinned there **permanently**, which is
  precisely the defect the neutral change removed for fresh rows. The migration is unreleased, so the fix is
  the `DEFAULT` itself (now `5`), not a data script. Pinned by
  `DsrRetrievabilityTests.A_row_migrated_under_the_old_default_stays_pinned_while_the_corrected_default_moves`:
  two states differing only in starting difficulty, reinforced at an identical realistic recall (`r ≈ 0.933`,
  `g ≈ 3.87`) — the row starting at `1` reinforces to exactly `1`, the row starting at `5` moves to `≈ 3.541`.
- **The review log: one row per reinforcement, so FSRS parameter fitting finally has something to read
  (design spec §3, 2026-08-11 fsrs-properly plan Task 3) — this library persisted none of that before now.
  `TASKS.md` Part 56 FSRS-B (fitting) is unblocked; `docs/DECISIONS.md` — the `DsrRetrievability` default curve explicitly rejected fitting
  against an invented corpus, and this is what makes a real one possible.** Every reinforcement
  `GraphMemoryEngine.RecallAsync`/`ExpandAsync` performs now also writes the PRE-review state (age,
  stability, difficulty, connection strength and its own staleness), the grade actually used (or null — see
  below), and the POST-review state (stability, difficulty) to `lyntai_memory_review`.
  <br>**It is DATA, never a decision — proved directly, not merely by omission.** Nothing in this engine's
  recall, ranking or pruning path reads the table; a test writes wildly divergent rows into it before both a
  recall and a prune run on otherwise-identical data and asserts each is byte-identical to a clean run
  either way (`GraphMemoryReviewLogTests.The_review_log_never_feeds_recall_ranking_or_pruning`) — pruning
  gets its own comparison rather than reusing the recall one, because `PruneAsync`'s derivable-age branch
  could in principle diverge from `RecallAsync`'s own candidate scoring even though today it happens to call
  the same private helper — the drift design spec §1 already warns against, with an extra step (a stored log
  a future policy could start trusting) this proof forecloses.
  <br>**`IMemoryRetrievabilityPolicy` gains `DerivedGrade(in MemoryDecayState state)` — additive with a
  DEFAULT (returns null), so no existing implementer, shipped or third-party, needs to change.** Exactly one
  shipped policy overrides it, `DsrRetrievability`, and `Reinforce`'s own internal difficulty update now
  ROUTES THROUGH this same member rather than a private formula that merely happened to agree with it — the
  seam a review log reads to record the grade `Reinforce` actually used, never a value re-derived afterward
  from whatever state is at hand. Null means one of two different things that both collapse to "nothing to
  log": this policy has no grade concept at all, or (specifically for `DsrRetrievability`) the
  same-position/session-burst Δt=0 bypass skipped the grade-driven update entirely this time, so recording a
  grade would misrepresent what happened. `ModulatedRetrievability.DerivedGrade` forwards to the wrapped
  policy on the SAME raw, unmodulated state `Reinforce` itself already uses, for the identical reason that
  method gives.
  <br>**`IMemoryGraphStore` gains two REQUIRED members, `RecordReviewsAsync`/`ReviewsAsync` — no default,
  the same shape `DeleteAsync` shipped with above (a custom store implementation stops compiling until it
  adds both).** The honest shape: a store that silently no-op'd logging would make the log an illusion for
  anyone who wrote their own backend. Two new types carry the row, `MemoryReviewWrite` (in) and
  `MemoryReview` (out, adding the store-assigned `Id`/`Engine`/`CreatedAt`).
  <br>**Bounded by default, and NOT by a per-write `DELETE`.** `GraphMemoryOptions.ReviewLogCap` (default
  `10_000`, per engine) evicts down to the newest rows once a soft threshold is crossed — paced from an
  in-process, per-engine write counter (`Lyntai.Memory.MemoryReviewLogPacing.TrimInterval`, a tenth of the
  cap, floored at 1) rather than a `DELETE` issued on every write, which would turn every recall into a
  write-amplifier. The trade-off, stated once: the log can transiently hold up to `cap + TrimInterval(cap) -
  1` rows between trims, and — the counter being in-process, never persisted — a restart can let it grow
  further still before the next trim catches up. Both are acceptable for a log whose job is giving a future
  fitter something to read, not enforcing an exact budget. Pinned on all three backends by
  `MemoryGraphStoreContract.RecordReviewsAsync_evicts_down_to_the_cap`.
  <br>**Opt-out, not opt-in: `GraphMemoryOptions.LogReviews` defaults to `true`.** A consumer who never fits
  pays one small, capped write per reinforcement; a consumer who wants to fit later cannot recover history
  nobody logged. Setting it `false` skips the write entirely rather than discarding it afterward.
  <br>**Best-effort at a STRICTER grain than the reinforcement it logs.** The log write runs in its OWN
  `try`/`catch`, nested INSIDE `ReinforceAsync`'s existing one, so a broken review log costs neither the
  caller's hits (the pre-existing best-effort promise) nor the stability/difficulty update or co-activation
  edges that already succeeded just above and after it in the same method — pinned by
  `GraphMemoryReviewLogTests.A_broken_review_log_costs_neither_the_hits_the_learning_nor_co_activation`.
  <br>**One `Guid` `BatchId` per `RecallAsync`/`ExpandAsync` call, shared across every node it
  reinforces.** A fitter may care that several rows came from the SAME recall — potentially competing
  candidates from one query — even though each row already stands alone as one independent
  `(state, grade, outcome)` observation without it.
  <br>**Schema** (in `M202608121100_MemoryRetentionModel`, the single 3.0 migration): the
  `lyntai_memory_review` table. **`grade` is the one nullable column in this schema — a deliberate exception
  to the "`NOT NULL DEFAULT`, never nullable" convention** the `salience` and `difficulty`
  columns both state and justify: nothing ever orders or filters on `grade`, and
  NULL there is the honest fact ("no grade-driven update happened"), never a stand-in for an unmigrated
  legacy value the way it would be on those other two columns. **No foreign key to `lyntai_memory_node`,
  also deliberately**: a reinforcement's history is meant to OUTLIVE the entry it was about, so
  `PruneAsync`/`DeleteAsync`/`ForgetAsync` removing a node must never erase what a fitter would read about
  how it behaved while it existed.
- **`ReciprocalRankFusionPolicy` replaces `MultiplicativeRankingPolicy` as the registered default ranking
  policy (owner ruling, 2026-08-11)** — `MemoryEngineRegistration.AddMemoryEngine` now `TryAddSingleton`s it
  (resolving `sp.GetService<ReciprocalRankFusionOptions>()`, the same pattern the forgetting-curve default
  already established), and a hand-constructed `GraphMemoryEngine` (`ranking: null`) now defaults to it too —
  no two-defaults split survives this change for either seam. A consumer who configures nothing gets RRF
  from 3.0 on; the one-line restore is
  `services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy())` before `AddLyntai`, or
  after — either direction wins, the same `TryAdd` ordering the forgetting-curve seam already established.
  <br>**The evidence: this library's own measurement, not an external validation** (unlike the forgetting-
  curve default) — `local/superpowers/records/2026-08-09-memory-policy-measurement.md` (fsrs-properly plan Task 4) found RRF
  beating `MultiplicativeRankingPolicy` on the corpus's `topical` class in ALL SIX measured shapes, reproduced
  across two independent runs (`+0.238`..`+0.719` pre a difficulty-neutral fix, `+0.431`..`+0.746` after it —
  same direction, same shapes, both clearing the ±0.10 action threshold). That agrees with the mechanism the
  same measurement pinned earlier: `MultiplicativeRankingPolicy`'s product-of-factors formula rewards RAW
  REINFORCEMENT MAGNITUDE, exactly what let an unmeasured flat multiplier (the now-deleted
  `HalfLifeRetrievability`) out-rank a curve (`DsrRetrievability`) that correctly declined to over-strengthen
  — rank-position fusion does not carry that bias. **`MultiplicativeRankingPolicy` is NOT the
  `HalfLifeRetrievability` case**: its formula is not unmeasured-and-wrong, it simply lost this one measured
  comparison, and it remains the better choice on a scale where raw magnitude is meaningful — it stays
  shipped, unchanged, and registerable in one line.
  <br>**The floor ships at RRF's OWN default (`0`), not the `0.02` the measurement's own confound control
  equalized both ranking arms at — a disclosed gap, verified rather than assumed to make no difference.** A
  direct instrumentation check (replaying every corpus shape at `RelativeFloor = 0.02`) found it cutting ZERO
  candidates across 995 `Rank` calls and 48,120 candidate evaluations — the tightest worst/best score ratio
  observed anywhere was `0.702`, nowhere near the `0.02` needed to bite, because RRF's own compressed score
  range (forty candidates fused at the default `K=60` span only a `100/61 ≈ 1.639×` ratio top to bottom)
  makes a 2% relative floor structurally unable to cut anything at any candidate-set size this library ships
  with. `0.02` and `0` are therefore empirically identical on the measured corpus, so the `topical` result
  transfers to what actually ships. `ReciprocalRankFusionOptions.RelativeFloor`'s own doc now states what
  value would actually bite on this policy's range, for a consumer who wants burial under RRF specifically —
  it is not a value a consumer should have to discover by trial and error.
  <br>**The seven golden characterization facts in `GraphMemoryRankingGoldenTests` that relied on a bare-
  constructed engine's own ranking default now pass `MultiplicativeRankingPolicy` explicitly** (three of
  seven needed this added; two already did, from the ranking-seam extraction) — those facts characterize
  MULTIPLICATIVE-NAMED formula terms (`HopAttenuation`, a relevance-times-retrievability product,
  retrievability's "own multiplicative contribution") that have no RRF analogue, so their SUBJECT does not
  move just because the DEFAULT did; their asserted values are UNCHANGED. The one guard whose actual SUBJECT
  is "today's shipped defaults" (`MemoryDefaultRecallQualityTests`) genuinely re-baselined instead, on the
  SAME fixed corpus point: `MissRate` moved from `0.337931` to `0.179310`, `PollutionRate` from `0.875172` to
  `0.865517` — MEASURED directly, not fitted, and the OLD values stay in a comment rather than being erased.
  <br>**A new fact, mutation-checked two ways**: `GraphMemoryWiringTests.The_zero_configuration_default_is_ReciprocalRankFusionPolicy`
  proves a container with no ranking registration at all produces RRF's own recall ORDER, genuinely different
  from `MultiplicativeRankingPolicy`'s on the identical corpus (not merely a different resolved TYPE) —
  reusing the golden suite's own Fact C corpus, which turns out to make the two policies disagree on the
  WINNER, not just the score (RRF's own tie on that corpus's two candidates is broken by the id-descending
  tiebreak, favouring the later write; Multiplicative's product does not tie). Removing the `TryAdd`
  registration entirely did NOT fail this fact — `GraphMemoryEngine`'s own bare-constructor fallback (also
  RRF now) masks a missing DI registration from any RECALL-BEHAVIOUR check, which is exactly why the cheaper,
  complementary `UseGraph_registers_a_default_ranking_policy_in_the_container` fact checks the CONTAINER
  directly instead; changing WHAT the registration supplied (to Multiplicative) DID fail it, as expected,
  confirming the fact is genuinely sensitive to the DI default rather than passing by coincidence.<!-- drift-ok: Multiplicative is named as the value a MUTATION supplied, never as the default -->

### Added

- **`JobOptions.GlobalMaxConcurrency` — a job concurrency cap across every process sharing one store**
  (`docs/DECISIONS.md` **D73**). The last durable-jobs deferral: `MaxConcurrency` bounds one runner, this
  bounds the deployment, so three workers with a global cap of 5 run five jobs between them rather than
  fifteen. `0` is the default and means unbounded — the pre-3.0 behaviour, with no extra round-trip.
  <br>Enforced by a new `lyntai_job_slot` table (one migration, both backends), NOT by counting running
  jobs: a count cannot gate a claim without racing, and folding the count into the claim statement fixes
  that only on a single-writer store — Postgres claims with `FOR UPDATE SKIP LOCKED` precisely so workers do
  not block each other. A slot is a ROW, so exclusion comes from the atomic-claim pattern that already works
  on both backends, and `SKIP LOCKED` then helps rather than hurts: two workers taking two different slots
  is the correct outcome, so the cap is exact **and** claiming stays parallel.
  <br>The cap is pure configuration — slots are created lazily up to it and never selected above it, so
  raising it needs no migration and lowering it needs no cleanup. A slot is released when the job ends, or
  expires after **`JobOptions.SlotLease`** (30s) when a worker dies. That lease is short because a live
  runner HEARTBEATS it: one expiry cannot both detect a dead worker quickly and let a live one run for
  hours, so the lease measures only "how long since we last heard from you" and a job may take as long as it
  needs. Renewal is by worker, so it is one statement however many jobs are in flight — and it mirrors what
  a job's own claim already does, since checkpointing refreshes `claimed_at`.
  **A BYO `IJobStore` must implement `TryAcquireSlotAsync`, `ReleaseSlotAsync` and `HeartbeatSlotsAsync`**
  (see Breaking).

- **A `UseCurated(…).UseGraph()` blend can finally reap** (`docs/DECISIONS.md` **D72**). That blend — from
  this library's own README — could not forget OR prune at ALL, because the curated member cannot reap and
  the composite refuses unless every member can. So an application withdrawing a user's consent had nothing
  to call, and an operator bounding disk had nothing either: `PruneAsync` and its durable
  `MemoryPruneJobHandler` already existed and were unreachable through the common blend. `CuratedMemoryEngine`
  is excluded by the DEFAULT reap policy — operator-authored material is neither the user's to withdraw nor
  what unbounded growth is made of — and the composite SKIPS it (logging the skip) instead of refusing.
  A member that holds user content and cannot reap still refuses loudly: the distinction between "not yours
  to reap" and "cannot reap" is what keeps a gap from becoming a silent partial.
  `LexicalMemoryEngine` gained forget + prune and `SemanticMemoryEngine` gained forget, so the engines now do
  what their stores could always do.

- **The streaming contract carries native tool calls, and an agentic turn can finally stream**
  (`docs/DECISIONS.md` **D71**). `LlmChunk` gains a `ToolCall` kind and payload; `ToolLoop`'s native path
  runs over `StreamAsync` when the provider declares the new `SupportsStreamingToolCalls`. Before this,
  `ToolLoop` had to buffer every native turn through `CompleteAsync` — its own comment said why — so **no
  agentic answer had time-to-first-token at all**, however long the model spent writing before its last tool
  call. A chunk always carries a **complete** call: vendors fragment them (id and name on one line, arguments
  a few characters at a time, interleaved when two tools are called) and the provider assembles, joining by
  the vendor's **index** rather than arrival order. The capability is separate from `SupportsToolCalls` and
  defaults to **false**, so every provider that has not implemented the streaming half keeps its previous
  behaviour exactly; only `OpenAiCompatibleProvider` opts in.

- **Streaming generation is reachable through the platform** — `IGenerationRouter.StreamAsync` selects a
  `GenerationDelivery.Stream`-capable backend and applies the same capability pre-filter, verdict-driven
  fallback, dead-host cooldown, budget and rate limiting the other two doors get. Two invariants are
  inherited from the LLM router rather than invented (`.claude/knowledge/llm-and-router.md` § Streaming):
  **fallback stops at the first chunk carrying real data**, and **only real data commits** — a metadata-only
  opening chunk does not, so a backend that announces a media type and then dies still falls over. A third is
  this door's own: **exactly one terminal chunk, guaranteed by the router**, so a backend whose stream simply
  stops is closed with a synthesized completion (if it produced data) or a failure chunk (if it did not), and
  a consumer's `await foreach` never has to ask whether the loop ended because the media finished or because
  the process died. `docs/DECISIONS.md` **D67**.

- **Every unmeasured generation wire mapping is now a host option** (`docs/DECISIONS.md` **D69**). The
  documented-not-measured backends already made their URLs settable *"for one specific reason: this
  backend's surface was not measured"* — and stopped at the URL, hard-coding the interpretation. That is the
  half that fails quietly: a wrong path is a 404 on the first call, a wrong status string means a finished
  render is polled forever, and a wrong cost field means the budget decorator spends against a number that
  is not the price. Now settable: `FalQueueOptions.StatusVocabulary` and `.CostFields`;
  `ComfyUiOptions.PromptIdField`, `.OutputsField`, `.StatusField`, `.CompletedField`;
  `LocalDiffusionOptions.Flags` (the whole `sd-cli` argv, keyed by meaning rather than spelling), `.Img2ImgMode`
  and `.ExtraArgs`. Declarative rather than delegates, deliberately — these are bound from `appsettings.json`,
  which is how they actually get set. Two rules the overrides cannot weaken: an unmapped status is still
  RUNNING and never failed, and an empty `CostFields` reports NO cost rather than a wrong one. Unconfigured
  behaviour is byte-identical, pinned per backend.

- **`LocalDiffusionOptions.Accelerator` and `MaxDimension`** — the render size ceiling is now the host's
  rather than a constant. `Cpu` (the default) derives 768px, the figure a consuming app measured on a
  GPU-less laptop where an accepted `1024x1792` meant about ten minutes of grinding; **`Gpu` derives no
  ceiling at all**, because the reason for the cap does not survive acceleration and no measurement here
  justifies inventing a different number. It is a declaration, never a probe. The clamp scales both sides by
  one factor so the requested aspect ratio survives. The backend's **advertised** `max-width`/`max-height`
  now follow the configured ceiling too, and are OMITTED when there is none — an absent limit reads as "not
  enumerated", where a number would be a ceiling nobody measured. **Unconfigured behaviour is byte-identical**
  to the hard-coded ceiling it replaces, pinned by its own test. `docs/DECISIONS.md` **D68**.

- **`GraphMemoryOptions.ExpandCharBudget`** — the fallback character budget an expansion uses when the caller
  passes none, which is the *"engine's configured budget"* `IExpandableMemory.ExpandAsync` has documented
  since it shipped without one ever existing. Null (the default) means unbounded, which is what the engine
  already did, so leaving it unset changes nothing. It bounds the NEIGHBOURS only — the expanded entry's own
  content is always returned whole, because that is what expansion is for.

- **`JobOutcome.Poll` — "not finished, and nothing went wrong; look again", which the contract had no word
  for.** A handler that WATCHES a long-running operation somewhere else had to express waiting as
  `JobOutcome.Retry`, and a retry SPENDS an attempt: at the default `MaxAttempts` of 3 a durable render was
  submitted, polled twice, and then dead-lettered as *"retries exhausted"*. At the 15-second default poll
  delay that meant **any hosted render slower than about thirty seconds died**, against a handler whose own
  documentation promised to "poll to completion across as many process lifetimes as it takes" — and the
  dead-letter reason named neither the backend nor the operation id, so the operator was never told a paid
  render was still running, unwatched. `Retry` keeps its meaning (a failed attempt worth repeating, bounded
  by `MaxAttempts`); `Poll` is not bounded by the attempt counter at all, because a look at a healthy
  operation is not an attempt — such a job ends by cancellation, a deadline, or the handler returning `Fail`.
  <br>**BREAKING for a BYO `IJobStore`:** the seam gains a required `PollAgainAsync(id, workerId, runAt, ct)`,
  with no default body. A default body was considered and rejected: the only one it could have is a fallback
  to the attempt-consuming path, which would leave every BYO store silently carrying the bug — the exact
  failure mode this change exists to remove. Implementations must UNDO the increment `ClaimNextAsync`
  applied (the SQL backends do it with `attempts=attempts-1`, the in-process store with a floored
  subtraction) and must be FENCED on the worker id: this is the one outcome that moves a job BACKWARDS, so
  an unfenced version would let a worker whose lease was already reclaimed reset another worker's job
  indefinitely. Pinned by a `JobStoreContract` fact wired to all three backends, plus two runner facts.
  Found by the 2026-08-14 whole-codebase review.

- **Three cross-backend memory rules that were held as private copies now have ONE definition each**, all
  additive and all behaviour-neutral. Each was a CONTRACT fact — something every `IMemoryGraphStore` has to
  agree on — implemented separately in two or three places, which is the shape `pitfalls.md` §Storage records
  for `salience`: *one thing read at N sites grows N rules, and the divergence is silent*.
  `MemorySignals.Salience` was the first fix of that shape; these are the rest of the family.
  - **`Lyntai.Memory.MemoryRelevance.ByRankPosition`** — how a backend with a rank ORDER and no comparable
    score reports `GraphNode.Relevance`. Three copies (SQLite's single-query path AND its two-query merge,
    Postgres's query path). Its `matched: false` clause is the fact **all three backends used to disagree
    about, SQLite with itself** — its FTS path put a grade-admitted non-match at the TAIL of the gradient and
    its substring path at the HEAD. 3.0 settled that on 0 everywhere; three copies is three chances to
    silently reopen it. Its known limit — the gap between positions is candidate-count dependent, so two
    candidates is the WORST case and never a representative one — is now stated once, on the rule.
  - **`Lyntai.Memory.MemoryContentKey.Of`** — the **dedup key**. Two backends hashing content differently do
    not merely differ in storage, they answer *"is this the same memory?"* differently: one refreshes where
    the other appends a second copy. Byte-for-byte with no normalization, deliberately the opposite choice
    from `MemorySubject` — a subject is a handle MEANT to collide, content is the fact itself.
  - **`Lyntai.Memory.MemorySubject`** — see below.

  All three are public rather than internal for the same reason: `IMemoryGraphStore` is a BYO extension
  point, and a store outside this repository that implements any of these rules its own way silently gets
  different semantics with nothing to report it.
- **`Lyntai.Memory.MemorySubject` — the ONE normalization every `IMemoryGraphStore` applies to a subject
  handle** (`Normalize`, `Canonicalize`, `IsUsable`). Additive; nothing behaves differently.
  <br>`IMemoryGraphStore` promised subjects are "compared case-insensitively", and each of the three shipped
  backends met that promise with its own inline `Trim().ToLowerInvariant()` — **six copies of a rule that
  has to be identical across backends or the same annotation links a different cluster depending on where it
  was stored.** That is exactly the shape `pitfalls.md` §Storage records for `salience` (*one stored value
  read at N sites grows N coercion rules, and the divergence is silent*), and `MemorySignals.Salience` is
  the same fix for that one. Public rather than internal because `IMemoryGraphStore` is a BYO extension
  point: a store outside this repository has to normalize identically or its subjects silently stop linking,
  and handing it the rule is cheaper than documenting it and hoping.
  <br>**Invariant casing is load-bearing, not style.** A backend reaching for `ToLower()` folds `"I"` to
  `"ı"` under a Turkish culture, so a handle recorded on one machine would never match the same handle
  looked up on another — pinned by `MemorySubjectTests`.
  <br>**The trim was unguarded on every backend** until this landed: `MemoryGraphStoreContract` pinned only
  CASE, so deleting `.Trim()` left the suite green. It now asserts padding in both directions (recorded
  padded / looked up clean, and the reverse), mutation-checked — the mutation kills five facts.


- **Memory can be told what a fact is ABOUT, and entries concerning the same entity become connected**
  (`Lyntai.Memory.Annotation`). `IMemoryAnnotationPolicy` judges a written fact's subjects; the engine records
  them and links entries that share one. `AddMemoryAnnotation(…)` registers the shipped model-backed
  implementation, `UseGraph(annotation:)` selects one per engine.
  <br>**Why nothing cheaper reaches this.** A graph engine's edges otherwise come from vector similarity or
  from co-activation during recall — and co-activation links whatever a recall happened to RETURN together,
  which is not about two facts concerning the same entity. Measured over a full corpus replay: English wrote
  442 edges of which **2** joined two of three cluster members; Chinese wrote 366 of which **0** did. Cluster
  recall therefore sat at the no-graph floor and was identical at recall limit 10 and 50 — proof those entries
  were never gathered and that no ranking policy could reach them. Facts like "my spouse is Alice", "she works
  as an anaesthetist" and "we met in Kyoto" share only pronouns in any language.
  <br>**Measured with a perfect annotator** (`node devtools/dev.mjs memory-annotation`, 20 seeds, paired — the
  mechanism's CEILING, not a model's accuracy): Chinese cluster miss `0.6667 → 0.0000` on baseline,
  many-candidates and high-noise; English `0.5933 → 0.2256`, `0.2676 → 0.0572`, `0.6778 → 0.2467`. Overall
  recall improves too on every shape bar one neutral. The cost is English cluster pollution
  (`0.3713 → 0.5343`); Chinese stays at `0.0000`.
  <br>**Opt-in and best-effort.** Without it an engine behaves exactly as before; with it, a failing,
  refusing or unparseable model yields no subjects and the write proceeds. An unparseable reply is treated as
  NO OPINION rather than salvaged, because a wrong subject links two unrelated facts permanently while a
  missed link costs one recall.
  <br>**Use a MULTILINGUAL model for non-Latin content.** The shipped prompt names no language and asks for
  subjects in the language of the fact, precisely so the same entity yields the same handle whatever it is
  written in — but the library detects no language, so an English-only model silently becomes the thing that
  decides whether Chinese facts get linked.
- **Named LLM clients** — `AddLlmClient("memory-fast", c => c.UseProviders("ollama", "openai"))` resolved
  through `ILlmClientFactory`, the counterpart of named memory engines and `IHttpClientFactory`. Previously
  backends were addressable but there was exactly ONE composed `ILlmClient` over all of them, so an app could
  not point one subsystem at a cheap backend without hand-building a router and losing the shared dead-host
  cooldown and admission table. **A name selects backends, never permissions**: every named client carries the
  same front-door decorators and refusal screening as the default, so a usage budget cannot be escaped by
  asking for a client by name. An id naming no registered provider throws rather than silently narrowing.
- **`Lyntai.Storage.ScriptProfile`** — the one place every language-dependent tokenization decision lives
  (does this script expand into n-grams; how long must a gram be to be selective; what may a substring scan
  additionally use). It replaced a single "is this spaceless?" boolean, which conflated two questions and
  could not express the one that was measurably wrong.
- **The retention model is now open.** `MemoryDecayState` carries a named `MemorySignals` bag, and an
  `IMemoryRetentionPolicy` (`ModulatedRetrievability`) layers arbitrary retention dimensions over ANY
  `IMemoryRetrievabilityPolicy` — each modulator clamped to its own declared `MaxStabilityFactor`, with
  `CandidateCutoff` widened by the product of every registered maximum so the bound stays a conservative
  superset — which matters because that cutoff's only consumer is `PruneAsync`: an under-wide one permanently
  DELETES entries the modulated curve still rates retrievable, it does not merely shorten a recall (seeding
  applies no faintness bound at all). Adding a dimension is a class plus a registration (a DI collection, the
  standard variation-point shape), never an edit to `MemoryDecayState` or the curve itself.
- **Salience: the first retention dimension. It means "this memory does not fade away", not "first priority"**
  (owner's decision, `docs/DECISIONS.md` — the salience admission-priority correction, corrected by the salience admission-priority correction the same day). `IMemorySaliencePolicy` judges how strongly a write is encoded;
  the registered default (`StructuralSaliencePolicy`) is model-free, using novelty — how unlike anything
  already stored a write is — as a prediction-error proxy, and deliberately reports nothing below
  `SalienceOptions.MinimumComparables` so a nearly-empty engine cannot mark its own first session maximally
  important. The reported value drives three things, two of them on by default: `SalienceRetentionPolicy` turns it
  into decay resistance (lengthens a half-life — this modulator itself still only ever scales stability,
  nothing more); the SQL stores order seed admission on it when a candidate set overflows its budget, so a
  salient candidate is found even when it matches a query poorly — together, those two are the whole of
  "does not fade away", and both ship on. `GraphMemoryEngine` can ALSO lift RANK by it —
  `GraphMemoryOptions.SalienceRankWeight`, as `1 + weight × ln(salience)`, logarithmic and bounded so a
  salient entry MAY outrank a better textual match without a maximally salient one trampling every relevance
  signal outright — but reordering a candidate ahead of a better match is a stronger, separate claim than
  "does not fade away", so **`SalienceRankWeight` defaults to `0` — off**. A consumer opts into ranking
  explicitly; measured against the SQL backends' rank-position `Relevance` normalization, the effect a given
  weight buys is candidate-count dependent (see the type's own doc and `docs/DECISIONS.md` — the salience admission-priority correction) — closing
  that with a proper ranking-policy seam is tracked, not shipped, in `docs/task-archive.md` Part 53.
  <br>**SUPERSEDED in one detail — the property MOVED.** The ranking seam this entry calls "tracked, not
  shipped" landed later in this same section, and took the rank knob with it:
  `GraphMemoryOptions.SalienceRankWeight` is now `Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight`
  (same name, same default of `0`, same reasoning), alongside `HopAttenuation` and `RelativeFloor`. It is also
  inert under the 3.0 registered default, `ReciprocalRankFusionPolicy`, which ranks salience by position and
  has its own weight. <!-- drift-ok: an amendment naming what it corrects -->
- **`GraphMemoryEngine` appraises every write and carries the result through recall, with no consumer change
  required.** `AddMemoryEngine(name, e => e.UseGraph())` (and `UseBestAvailable()`/`AddMemory()`) registers
  the default appraiser and modulator via `TryAdd`, so a consumer's own registration wins and the whole model
  is on by default for anyone already using the graph engine. Novelty is read from the similarity search the
  engine already performs for enrichment — one embed, one vector search per write, shared between appraisal
  and linking rather than paid for twice — and excludes the write's own PRIOR vector (an earlier write of
  identical content) from that comparison, so re-remembering an entry cannot appraise it against itself and
  erase the salience the first write earned. Both the shared search and the appraisal are best-effort,
  exactly like existing enrichment: a failing embedder or a throwing appraiser degrades the write to 2.5.0
  decay behaviour, never to a lost write, and an appraiser that declines to judge a re-remembered entry keeps
  whatever salience is already stored rather than blanking it.
- **The SQL graph backends (SQLite, Postgres) now persist `Signals` too**, closing the gap from the InMemory-only
  release above: one JSON column (`signals`, `TEXT`/`JSONB`, hand-walked — no reflection `JsonSerializer`, the hand-walked-JSON rule)
  carries the whole open bag, so a future retention dimension needs no migration at all. **Salience is promoted
  to its own `salience` column** — indexed on SQLite, where the FTS-merge path's exact-facts sub-query actually
  plans against `(engine, task_key, scope, salience DESC)`; deliberately NOT indexed on Postgres, where both
  seed paths lead with a computed boolean no such prefix can satisfy and an unread index on this table would be
  pure write amplification — (`REAL`/`DOUBLE PRECISION`, `NOT NULL DEFAULT 1` — 1 is the neutral
  value, so a pre-existing row migrates to "no opinion" and orders exactly as before; a nullable column would
  have put every legacy row ahead of every appraised one on Postgres, where `ORDER BY … DESC` sorts NULLs
  first) — a signal earns a column exactly when the database itself must sort on it. **On the RECENCY-ORDERED
  seed paths** — the no-query and substring-fallback paths on all three backends, plus SQLite's separate
  exact-facts sub-query — seeding now orders by `(grade = authoritative) DESC, salience DESC,
  last_recalled_position DESC, id DESC`: grade still leads (an exact fact outranks a merely salient one), then
  salience admits a candidate the query or recency alone would have let the limit cut, then recency breaks the
  remaining ties. **On a MATCH-RANKED path salience is a tiebreak behind the match score, not ahead of it** —
  that is SQLite's FTS branch, `ORDER BY bm25(…), salience DESC, id DESC`, taken for any query of three
  characters or more — because letting salience outrank the score would let a salient POOR match displace a
  strong one. So admission ordering is backend-specific, by the same carve-out `IMemoryGraphStore.SeedAsync`
  already makes for `Relevance`. The column is the COERCED materialisation of the bag's salience (below 1 and
  non-finite both become the neutral 1, via `MemorySignals.Salience`) rather than a copy of it, so a bag
  holding `0.5` reads back as `0.5` while the column holds `1`; both are written from the same bag in the same
  statement, through that one shared rule, so they cannot drift. The bag is read back into the node's
  `Signals` on the way out. A `signals` value that fails to parse — including a row from before this
  release, where it is simply absent — reads back as an empty bag rather than throwing: a lost signal decays
  as if never appraised, which is recoverable, and losing the memory over it would not be.
- **A second forgetting curve: `DsrRetrievability` / `DsrOptions` (`Lyntai.Memory.Forgetting`), a POWER law
  beside the shipped exponential one.**
  <br>**SUPERSEDED — read this entry as history, not as 3.0.** By release `DsrRetrievability` is the ONLY
  forgetting curve and the registered default: the exponential one this describes sitting beside was deleted
  later in this same section, with no restore path, and the "available, not default" line below reversed with
  it. `Lyntai.Memory.Forgetting` ships one implementation. <!-- drift-ok: an amendment naming what it corrects --> `r = (1 + F·age/stability)^decay`, with `F` derived from
  `DsrOptions.Decay` so the half-life anchor holds whatever exponent is chosen, plus FSRS's three
  stability-increase laws in `Reinforce` — stabilization decay, the spacing effect, and difficulty when
  `MemorySignals.WellKnown.Difficulty` is present. Register
  `services.AddSingleton<IMemoryRetrievabilityPolicy>(new DsrRetrievability())` and every `UseGraph()` engine picks
  it up with no other change: neither `AddLyntai` nor `AddMemoryEngine` registers a competing default, so the
  registration works whether it comes before or after `AddLyntai`. The heavier tail makes `CandidateCutoff`
  roughly thirty times wider than the exponential curve's at the same floor, so `PruneAsync` is markedly LESS
  aggressive after switching — the safe direction, since a wider cutoff only ever deletes less. **Available,
  not default**: implementations of a retention seam accumulate rather than replace, and which one is the
  DEFAULT is a separate, versioned decision changed only on measured evidence and never assumed from the
  seam existing (`docs/DECISIONS.md` — the four memory domains placed by ownership). No such measurement has been run for either curve —
  `docs/task-archive.md` **Part 54** is where that work landed, including the two things that would otherwise confound it
  (salience modulation is on by default and inflates DSR's reinforcement but not the exponential curve's;
  `GraphMemoryOptions.Decay` tunes only the exponential arm).


- **`AddMemoryVerification` — a model judges which of a recall's candidates actually ANSWERED the query, and
  an answer the ranking buried is promoted past the limit.** This is the largest recall-quality lever in the
  subsystem, and it exists because of a measurement rather than an intuition.
  <br>**The diagnosis:** decomposing a full corpus replay, of the relevant entries a recall failed to return,
  **100% were reachable candidates that the ranking put below the limit** and **0% were unreachable**. The
  miss rate is a ranking failure end to end — which rules out a better tokenizer, more n-grams or a semantic
  index, because the answers were already candidates. The two shipped model-free ranking policies also return
  byte-identical results, so swapping formulas is not an available fix either.
  <br>**What it is worth, measured across five judges — not inferred.** A ground-truth reference judge takes
  miss `0.5357 → 0.2857` and pollution `0.3331 → 0.1549`. Real judges on the same corpus, all 145 calls
  answered with no parse failures: **Claude Haiku reaches miss `0.1857` — a 65% relative cut, 140% of the
  reference**; `gemma3:4b` (3.3 GB) reaches `0.2571`/`0.2643` with the **lowest pollution of anything
  measured** (`0.0492`, below the reference itself); `qwen2.5-vl:7b` `0.3071`; `llama3.2:3b` ~`0.37–0.39`,
  varying run to run because a model is not deterministic.
  <br>**The ranking is not one-dimensional:** Haiku finds more right answers, `gemma3:4b` admits less junk.
  Which judge is "best" depends on which failure your application pays for — and on cost, since the local
  arms judge in ~1.5 s per recall while a hosted CLI judge took ~5 s.
  <br>**A small current model beat both a bigger older one and the ground-truth reference**, so judge
  quality tracks generation at least as much as parameter count. The reference is not an upper bound: it
  promotes only strictly-relevant entries, which leaves the rest of the limit to the noisy ranking and
  reinforces less, and a judge with a broader notion of relevance does better on both counts.
  <br>Which model to use is a deployment choice this library takes no position on — the figures show the
  shape of the trade, not a recommended tier. Avoid *thinking* models here: `qwen3:4b` spent ~25 s of
  reasoning per judgement against gemma3's ~1.5 s, which is disqualifying for a seam in the latency path
  whatever it scores. A judge
  allowed to see only what already won recovers barely a third of the available improvement whatever its
  size, which is why `GraphMemoryOptions.VerificationDepth` defaults to **4× the recall's limit** — the
  measured saturation point, not a round number.
  <br>Costs one model call per recall, in the latency path of an answer (~0.6–0.9 s locally on the models
  above). Size the judge deliberately via `LlmVerificationOptions.ClientName`. **Nothing changes for a consumer who registers no verifier**; the
  model-free floor is exactly what it was.
  <br>A judgement never removes a result from the caller's answer unless `GraphMemoryOptions.VerificationFilters`
  is set, and authoritative material is exempt whatever the verdict. Any failure leaves the ranking alone
  rather than reporting "nothing was relevant" — the two are opposite instructions, and collapsing them would
  let a model outage teach the engine that its recalls are failing.
  <br>**It also removes both of the blockers `docs/DECISIONS.md` — the 3.0 ships-without record recorded as making parameter fitting
  structurally impossible.** The judgement comes from outside the curve, defeating the circular-grade
  problem; and the review log write is now decoupled from the touch, so a recall logs a row for **every**
  entry it returned — including ones the judge rejected and therefore never reinforced. That is the 3.0 ships-without record's
  "the log can only ever contain successes", closed.
  <br>`MemoryReviewWrite`/`MemoryReview` gain a nullable `Verified` (`true` answered, `false` did not,
  `null` no verifier ran — the three are not interchangeable), with one column folded into the unreleased
  memory migration on SQLite and Postgres. Additive; a store with no verifier logs `null` exactly as before.
  See the ranking-defect diagnosis.
- **`LlmRequest.Reasoning` (`LlmReasoning`)** — ask a backend for no intermediate reasoning. **Advisory**: a
  provider that cannot express it ignores it, and a model that reasons anyway is not a defect. The Ollama
  provider maps `Suppress` to a top-level `think: false`; nothing is sent on the default.
  <br>It exists because a reasoning model is unusable in a latency-path seam: judging one corpus took **~25 s
  per call against ~1.5 s** for a model that answers directly. Deliberately neutral rather than a per-family
  prompt token, so each provider maps it to its own vocabulary. It is part of the response-cache key — the
  same prompt asked with and without reasoning can return different text.
- **`NeutralSaliencePolicy`** — the supported way to turn salience OFF. Registering an *empty* policy
  collection does **not** do it: null-or-empty means "take the shipped default", the same convention the age
  seam uses. That trap cost one of this release's own measurements its control, so there is now a real type
  for it.
- **`GraphMemoryOptions.SemanticSeedK`** — how many semantically-similar entries a recall pulls into its
  candidate set. `0` (the default) pulls none, which is what every earlier version did.
  <br>Until now an embedder was consulted at WRITE time only — novelty and similarity linking — so the engine
  had **no semantic retrieval at all** and a real embedding model recovered 0 of 3 paraphrased facts. With
  it, the query is embedded and its nearest entries join the candidates carrying their cosine as `Relevance`.
  <br>**Reachable is not returned, and no ranking setting closes that gap.** Measured against RRF's
  defaults, an 8× relevance weight, `K = 1`, both together, and `MultiplicativeRankingPolicy`, a
  semantically-seeded paraphrase is outranked by recent unrelated material in **every** configuration. So
  this option is useful **in combination with `AddMemoryVerification`** — seeding widens the candidate set,
  the judge promotes from it — and not on its own. Registering an embedder at all also measured as a net
  cost on a lexical corpus, which is why it is off by default.
- **`GraphMemoryOptions.RecallReinforceCap`** — how many of a recall's returned entries it reinforces, taken
  from the top of the ranking. `null` (the default) reinforces everything returned, unchanged. The graded form
  of `ReinforceOn`: a recall returns ranked GUESSES, and reinforcing the tenth as strongly as the first is
  where learning from your own prior does its damage.
- **`GraphMemoryOptions.ReinforceOn` selects which CALLS reinforce — a recall, an expansion, both (the
  default) or neither.** Composes with `Reinforcement` above: the acts selected here apply the effects
  selected there. Default `All`, so **nothing changes for anyone who does not set it**.
  <br>It exists because the README promises that *material you keep coming back to* becomes durable while the
  implementation reinforces whatever the ranker RETURNED — the loop upvoting its own prior. `ExpandAsync` is a
  caller choosing to pay for full content, which is the closest thing this library observes to a verified
  retrieval, and it was reinforced with exactly the same weight as a speculative recall.
  <br>**Measured** (`docs/DECISIONS.md` — the unverified-signal reinforcement rule): `Expansion` alone beats the default on **both** miss and
  pollution, in both growth configurations — and beats reinforcing *nothing* too, which refutes the earlier
  reading that less reinforcement is simply better. The damage was the signal, not the quantity.
  <br>**The default stays `All` anyway**, because an application that never expands would get the
  reinforce-nothing arm — the worst pollution measured — and the library cannot tell which kind of consumer it
  has. **If your application calls `ExpandAsync` (including via `AddMemoryTools`), `ReinforceOn =
  MemoryReinforcementActs.Expansion` is the measurably better setting.**
- **`GraphMemoryOptions.Reinforcement` separates reinforcement's two effects — the age reset and the
  stability growth — which were welded into one store round-trip.** `MemoryReinforcementEffects` is a flags
  enum (`None`, `AgeReset`, `StabilityGrowth`, `All`); the default is `All`, so **nothing changes for anyone
  who does not set it**.
  <br>The two pull in opposite directions and the evidence for each differs: the age reset is what keeps a
  rarely-queried critical fact alive, while the growth entrenches whatever the ranker already returned,
  because nothing in this library observes whether the return was *correct*. `AgeReset` alone is the
  best-measured configuration.
  <br>**Why an engine option rather than the curve's knob:** it was reachable only through
  `DsrOptions.ReinforceGain = 0`, one shipped curve's private constant — a consumer with their own
  `IMemoryRetrievabilityPolicy` had no equivalent. Which effects a recall applies is a decision about how the
  engine learns, not a property of the forgetting curve.
  <br>**`StabilityGrowth` without `AgeReset` throws** at the line that configures it: the store resets the
  age as an inseparable part of the same write, so that combination could only be honoured by applying
  *neither* effect. Refused rather than implemented — see `docs/DECISIONS.md` — the effects-not-acts reinforcement seam for why adding a sixth
  required `IMemoryGraphStore` member for the worst-measured arm was not worth it.
  <br>Co-activation edges and the review log are deliberately outside this option and keep their own
  switches; the log still records what the policy computed even when the engine does not bank it.


- **The ranking seam itself** (`Lyntai.Memory.Ranking`): `IMemoryRankingPolicy` (`Rank(candidates, context)`,
  set-based rather than per-candidate, because a fusion policy needs to see where every other candidate falls
  on the same signal), `MemoryCandidate`, `MemoryRankingContext`, `RankedMemory`, `MultiplicativeRankingPolicy`
  and `MultiplicativeRankingOptions` — the destination the properties above moved to. A policy may floor
  candidates against its own best score but may not invent or drop an `Authoritative` one on the library's
  behalf; see the Breaking entry above for what the engine itself still guarantees regardless of which policy
  is installed.
- **A second ranking policy: `ReciprocalRankFusionPolicy` / `ReciprocalRankFusionOptions`
  (`Lyntai.Memory.Ranking`).** It was added mid-window as an alternative and **became the REGISTERED default
  later in this same release** once the corpus measurement had run — see the "replaces
  `MultiplicativeRankingPolicy` as the REGISTERED default" entry above for the evidence and the one-line
  restore. This entry describes the policy itself. `Score = Σₛ wₛ / (K + rankₛ)`, summed over four
  signals — relevance, retrievability, salience and hop — each contributing its own 1-based RANK POSITION
  within the candidate set rather than its raw value, so unlike `MultiplicativeRankingPolicy`'s product it
  needs no shared numeric scale across signals. `K` defaults to `60`, Cormack, Clarke & Buettcher's published
  value; every weight defaults to `1`. **Hop is a deliberate fourth signal, ranked ASCENDING (nearer is
  better) where the other three rank descending** — taken literally, the design spec's three-signal list
  would let a hop-2 match outrank a direct hit, which has nothing to do with fusing relevance,
  retrievability and salience. **Ties within one signal SHARE a rank — competition ranking — so the next
  distinct value skips ahead by the tied group's width (`1, 1, 3`, never `1, 1, 2`).** Cormack, Clarke &
  Buettcher fuse independent ranked *lists*, where a tie cannot arise; fusing *signals* it can, and giving
  tied values distinct ranks would turn a fully-tied signal into a pure node-id ordering that still carries
  a full signal's weight — in the model-free default, where every node's salience is the neutral `1`, that
  handed node id the same share as relevance. A candidate whose own `Relevance` or `Retrievability` is
  non-finite is excluded before ranking begins — **the same filter `MultiplicativeRankingPolicy` now
  applies**, so the two policies agree by construction on which memories exist rather than by coincidence.
  **`RelativeFloor` defaults to `0`, not `MultiplicativeRankingOptions`'s `0.02`** — reciprocal
  rank fusion deliberately compresses its own score range (forty candidates fused at the default `K` span a
  `100/61 ≈ 1.639×` ratio top to bottom), so a floor copied from the other policy would never cross a single
  score at that range and the buried-not-cut rule's "buried, not cut" burial would go silently inert rather than merely weaker.
  Every weight must be finite and `>= 0`, `K` finite and `> 0`, and at least one weight must be above `0` —
  all four at zero would score every candidate exactly `0` and hand ordering entirely to the id tiebreak, a
  silent failure rather than a loud one. When this entry was written the policy was registered nowhere and a
  consumer opted in like any other `IMemoryRankingPolicy`; by the end of this release it is what
  `AddMemoryEngine` registers, and `MultiplicativeRankingPolicy` is the one you opt into.
- **The ranking × forgetting-curve corpus measurement landed** (`local/superpowers/records/2026-08-09-memory-policy-measurement.md`),
  the follow-on this section used to point at as future work: a deterministic corpus, two recall-quality
  metrics, and a four-arm `{MultiplicativeRankingPolicy, ReciprocalRankFusionPolicy} × {HalfLifeRetrievability,
  DsrRetrievability}` sweep (`bench/Lyntai.Benchmarks/MemoryPolicySweep.cs`, `node devtools/dev.mjs
  memory-sweep`). **This entry records the FIRST measurement pass and is superseded within this same release
  — read it for the harness, not for the verdict.** <!-- drift-ok --> It changed no default at the time: the
  arms it compared were `{Multiplicative, RRF} × {HalfLife, DSR}`, and it found `MultiplicativeRankingPolicy`'s
  `critical-rare` MissRate `≤` `ReciprocalRankFusionPolicy`'s on every shape in that table (a tie on the
  weakest shape counted), from one seed run three times rather than three independent samples — a defensible
  default *proposal*, not itself a change. It also left `HalfLifeRetrievability`-vs-`DsrRetrievability`
  genuinely unresolved, because the two curves differed in only one of six shapes and which won there flipped
  with the ranking policy it was paired with.
  **What superseded it, later in this release:** the exponential curve was deleted outright on its own
  evidence (the unmeasured `× 1.5`), which collapsed the four arms to two; the corpus was then re-measured
  against the corrected arms and **RRF won every one of the six shapes**, which is the evidence behind the
  ranking-default change above. `docs/task-archive.md` Part 55's open decision is closed by `docs/DECISIONS.md` — the `DsrRetrievability` default curve,
  not the four memory domains placed by ownership. The regression test this entry introduced,
  `tests/Lyntai.Tests/Memory/MemoryDefaultRecallQualityTests.cs`, now pins `ReciprocalRankFusionPolicy` +
  `DsrRetrievability` — it still answers "did we break the default," never "which default is best."
- **A third ranking policy: `CompositeRankingPolicy` / `CompositeRankingOptions` (`Lyntai.Memory.Ranking`) —
  this domain's first genuine COMPOSITE, fusing two other `IMemoryRankingPolicy` members into one order.**
  Fuses by rank POSITION, never raw score — averaging `MultiplicativeRankingPolicy`'s bounded `[0,1]` product
  against `ReciprocalRankFusionPolicy`'s sum (around `0.06` at its own defaults) would be arithmetic over
  quantities that share no scale, which `IMemoryRankingPolicy.Rank`'s own contract already says is
  meaningless. Instead it re-derives each member's own COMPETITION rank position over the candidate set
  (grouping by that member's own tied SCORES, never its output list position — a member's internal id
  tiebreak always makes list position distinct even when every candidate scored identically) and fuses those
  in the same `score = w / (K + rank)` shape `ReciprocalRankFusionPolicy` already uses for its own four raw
  signals. `PrimaryWeight`/`SecondaryWeight` (default `1`/`1`) decide which member's rank position the fused
  score amplifies more; `K` (default `60`, same published constant) and `RelativeFloor` (default `0`, same
  reasoning as `ReciprocalRankFusionOptions`'s own) round out the options. A candidate either member's own
  floor drops is not excluded from the fused result — it is ranked one past that member's own worst kept
  rank, tied with anything else that member also dropped, never fabricated as better or worse than that; a
  member that drops every candidate contributes the SAME constant to everyone rather than distorting the
  order. Not registered anywhere by default; a consumer opts in the same way as any other
  `IMemoryRankingPolicy`.
- **Ranking is now scoped per named engine, and a single call can select an alternate BY NAME.**
  `MemoryEngineBuilder.UseGraph` gains `ranking` (that named engine's own policy, ahead of the container
  registration) and `namedRankingPolicies` (alternates a per-call override may select); `MemoryQuery` gains
  `RankingPolicyName`. See the Breaking entry above for the exact shapes — this entry exists so the
  capability is findable from "Added" too. Resolving BY NAME rather than accepting a policy instance on the
  query keeps `MemoryQuery` plain data; an unknown name is an error, never a silent fallback to the default.

### Changed

- **Keyword recall now matches TERM-WISE on every backend, and works in scripts that write no spaces**
  (`docs/DECISIONS.md` — the one-tokenization rule). New public `Lyntai.Storage.SearchTerms` (+ `LikeTermClause`) owns the one
  tokenization; `FtsQuery` keeps only the FTS5 syntax.
  <br>**What was wrong.** Only SQLite's FTS path split a query into words. Both LIKE fallbacks, all three
  Postgres queries and both InMemory stores matched the WHOLE query as one contiguous substring — so a
  realistic cue (`"what is the spouse called"`, never contiguous in any entry) found the fact on SQLite and
  nothing at all on Postgres or InMemory. This had been recorded as a by-design divergence; it was not. A
  difference in ORDERING between backends is a divergence, a different answer to *is the fact found* is a
  defect. Ranking still differs by backend (bm25 on SQLite, matched-term count elsewhere); admission no
  longer does. `MemoryGraphStoreContract` now asserts multi-token matching on all three backends.
  <br>**And in Chinese, Japanese or Korean it was worse.** Splitting on whitespace hands back a whole
  sentence as one token, so a CJK query could only ever match an entry containing that exact substring —
  English got OR-over-words, CJK got exact-phrase-or-nothing, decided purely by whether the language uses
  spaces. A spaceless run is now expanded into character trigrams, which is the unit BOTH backends already
  index (SQLite `tokenize='trigram'`, Postgres `pg_trgm`) — the storage layer was always script-neutral and
  only the query side was English-shaped. **No configuration: it is the default.** ASCII words are
  deliberately not expanded (a whole word is more precise than its trigrams).
  <br>**Cost, stated.** An `OR` finds strictly more than a substring, so pollution can rise where misses
  fall; the matched-term count now leads the ordering on those paths to bound it. The floor is three
  characters in any script — a two-character CJK overlap is below what a trigram index can match, and falls
  through to the whole-query substring scan as before. The pinned English corpus numbers are UNCHANGED
  (SQLite's FTS path already OR-ed words), so this lands on the backends nobody had measured.
  <br>**And it is now MEASURED, not only fixed.** The measuring corpus gained a language axis
  (`CorpusShape.Language`) and `node devtools/dev.mjs memory-language` reports paired English-vs-Chinese
  recall quality over 30 seeds and 7 shapes; English output is byte-identical when the axis is unset, proved
  by goldens captured before it existed. Building it exposed two bugs that both FLATTERED the Chinese result
  — a shared corpus token below the trigram floor, and a query classifier matching English wording — each now
  guarded. Still unmeasured, and stated rather than implied: Japanese, Korean, and mixed-script content.
- **`DsrOptions.ReinforceGain` now defaults to `0`: a recall REFRESHES an entry's age but no longer lengthens
  its half-life** (`docs/DECISIONS.md` — the `ReinforceGain = 0` default). The single default 3.0 moves on measured evidence.
  <br>Retrieval-driven stability growth made recall measurably WORSE. On the fixed-corpus pin the new default
  reads `miss 0.234483 → 0.103448` (a 56% reduction) and `pollution 0.871034 → 0.857931` — both better, from
  one value — and it won on every one of six corpus shapes across thirty paired seeds. **Every alternative was
  built and lost**, not just the shipped rule: a capped variant, and one computing stability purely from the
  entry's recall COUNT so it cannot compound by construction, both lost to simply not growing.
  <br>**The mechanism**: what a recall is worth comes from the age reset, which EXPIRES — the entry decays
  again at its own rate. A permanent half-life increase instead banks the ranking policy's own errors, so an
  entry wrongly returned becomes more retrievable and is more likely to be returned wrongly again.
  <br>**Durability has not gone away, it has moved to where it measures well**: how NOVEL an entry was when
  written (salience — measured for the first time this release and shown to help) and how CONNECTED it is.
  Both are properties of the material and the graph rather than of what this engine's own ranker chose to
  return.
  <br>**FSRS's three stability-increase laws are kept, not deleted**, and `new DsrOptions { ReinforceGain =
  2.0 }` restores 2.5.x behaviour in one line. The measurement behind the default is one synthetic corpus; a
  deployment with real review data may well find its own value, which is what the review log exists for.
- **3.0 ships ONE memory migration, not six.** `M202608121100_MemoryRetentionModel` (SQLite + Postgres)
  carries the whole retention schema — signals + promoted salience, the node and edge age primitives,
  provenance, live difficulty, and the review log. The six migrations it replaces
  (`MemorySignals`, `MemoryAgePrimitives`, `MemoryProvenance`, `MemoryDifficulty`, `MemoryReviewLog`,
  `MemoryEdgeAgePrimitives`) all landed **after `v2.5.0` was cut**, so no released version ever carried one
  and no consumer database has ever applied one — exactly the condition `docs/DECISIONS.md` — the pre-release migration-folding rule names for
  folding a pre-release migration into its owner, and the same thing 1.0 did when it collapsed the accreted
  0.x set into the nine per-domain baselines this schema still starts from. A fresh 3.0 database now applies
  **12** migrations rather than 16 — the twelfth being `M202608161159_JobSlots`, which is not part of this
  fold at all but of the cross-process job cap below.
  **`M202608081215_MemoryGraph` is deliberately NOT folded in** — it shipped in 2.5.0, so its tables are
  released and every change above stays an `ALTER`. Folding them into that `CREATE TABLE` would be the
  migration-number trap: FluentMigrator records an applied migration by NUMBER, so a 2.5.0 database would
  silently skip the edited version and never receive a single one of these columns.
  **The equivalence is proved, not asserted**: the schema goldens were captured from the PRE-squash set and
  still match, unregenerated, on both backends.
  <br>**Who this can bite, stated rather than discovered:** a database that already applied SOME of the six —
  only ever a local development or test database, never a consumer's — fails on a duplicate column. Delete it
  and re-migrate.

### Fixed

- **A streamed turn that produced prose AND a tool call silently DROPPED the call** — no error, no verdict,
  no log; the caller asked for an agent and got a sentence. The old code named it in a comment (*"fall
  through to a benign Final (tool call dropped)"*) while the roadmap carried the missing payload as "low
  value, revisit on demand": the feature had been priced, the defect underneath it had not. Both are closed
  by the streaming tool-call contract above (`docs/DECISIONS.md` **D71**), and the drop is pinned by a
  regression test. Also changed: a stream that finishes FOR tool calls and assembles none now reports
  `Failed` with what happened, rather than `Unsupported` advising `CompleteAsync` — that advice was honest
  while the contract could not carry calls, and is false now.

- **A deleted memory left its SUBJECT rows behind on both SQL backends — unbounded growth, a corrupted
  reuse list, and `ForgetAsync` leaving model-derived text in the database.** `lyntai_memory_subject` was
  created with a bare `node_id` — no foreign key, no cascade — and `DeleteAsync`/`PruneAsync`/`ForgetAsync`
  touched only the node table, so nothing ever removed a subject row. The in-process store did the opposite,
  so this was a cross-backend divergence rather than a shared choice. The table now declares
  `REFERENCES lyntai_memory_node(id) ON DELETE CASCADE`, exactly as `lyntai_memory_edge` has since it
  shipped; the cascade fires because every connection comes from the shared factory with `foreign_keys=ON`.
  <br>**The contract fact that claims to cover this could not see it**, which is why it survived: it asserted
  only through `NodesBySubjectAsync`, whose JOIN against the node table hides an orphan. It now also asserts
  through `KnownSubjectsAsync`, which does not join — and which is the reader that actually matters, since
  the engine feeds its top-N to the annotator as reuse candidates ordered by `COUNT(*)`, so a fully dead
  subject with many orphans outranked a live one and pushed real handles out of a bounded list. Both schema
  goldens regenerated; the diff is one line each. Found by the 2026-08-14 whole-codebase review.

- **`GraphMemoryOptions.MinRetrievability` was read by nothing, so `PruneAsync` with no explicit floor was a
  silent NO-OP.** The option occurred only in its own declaration and its own validation guard, while
  `PruneAsync` took an independent `minRetrievability` parameter and never consulted it — so with both that
  parameter and `olderThan` null, the candidate filter matched nothing and the documented call
  `engine.PruneAsync("task")` deleted nothing at all, against the option's own summary ("the retrievability
  below which `PruneAsync` may REAP an entry") and design §5.7 ("the absolute `MinRetrievability` governs
  `PruneAsync` alone"). The caller's floor now falls back to the configured one.
  <br>**This is a behaviour change with data-loss implications, stated plainly rather than buried:** a
  deployment calling `PruneAsync` without a floor previously deleted nothing on that criterion and now reaps
  entries below `MinRetrievability` (default `0.05`). **`MinRetrievability = 0` is the opt-out** — a
  retrievability is never below zero, so a floor of zero reaps nothing and restores the old behaviour — and
  that escape is asserted by a test rather than assumed. Authoritative material remains ineligible at any
  floor, which is objective (1) and is also now pinned. Found by the 2026-08-14 whole-codebase review.

- **A curated engine's query-less recall returned every section of the catalog, unbounded.** The branch taken
  when `MemoryQuery.Query` is empty calls `ForCompositionAsync`, which accepts neither `kind` nor a limit,
  while the `SearchAsync` branch one line below passes both. So an engine bound to one section returned the
  whole catalog, every section, with no limit and everything graded `Authoritative` — and a blend of two
  curated engines over one catalog returned each fact once per member, the duplicates consuming the
  authoritative reserve objective (1) exists to protect. Both filters are now applied engine-side, leaving
  `ForCompositionAsync` doing the one job its other caller (`CuratedMemorySections`) needs. Found by the
  2026-08-14 whole-codebase review.

- **A failing embedder at RECALL emptied the whole recall instead of degrading to the lexical hits.**
  `GraphMemoryEngine`'s semantic-seed step had no `try`/`catch` and ran *after* the store had already returned
  lexical seeds, so a transient embedder or vector-store fault threw out of the gather step into
  `RecallAsync`'s best-effort catch and returned `MemoryRecall.Empty` — discarding good seeds and reporting an
  outage as "nothing matched", which a caller cannot distinguish from a genuine miss. Design §5.7.0 is
  explicit that enrichment's failure degrades quality, never correctness, and the WRITE path's twin has always
  been guarded this way. Cancellation is still never swallowed. Found by the 2026-08-14 whole-codebase review.

- **A blended memory engine ignored `MemoryQuery.Limit` entirely, so an N-member blend returned up to N × the
  limit asked for.** `CompositeMemoryEngine.RecallAsync` added every member's items and returned them with no
  cut — and `MemoryEngineBuilder` **always** wraps members in a composite, so this was the shape every
  multi-member consumer had (the README's own headline example is a two-member blend). The same class as the
  `AuthoritativeReserve` defect above: a bound configured on one scope (the query) and enforced on another
  (nothing). The blend is now ordered **authoritative-first, then by relevance**, and cut to the limit.
  <br>Grade leads the ordering because design §5.7.0's objective (1) — never lose an authoritative fact — is
  the only objective with no acceptable failure rate, so an exact fact must survive a cut a higher-scoring
  associative hit would otherwise win; the graph engine already reconciles its own reserve against a limit
  the same way. Relevance breaks ties below that tier, with a limit worth stating plainly: two members score
  on scales that are not comparable, so the ordering is principled about the grade tier and merely reasonable
  within it. **Only an explicit limit cuts** — `Limit: null` documents "the engine's default", a composite
  has no default of its own, and inventing one here would silently truncate a blend somebody configured
  deliberately. Found by the 2026-08-14 whole-codebase review.

- **`ExpandAsync` accepted `hops` and `charBudget`, advertised `hops` to models, and ignored both.** Neither
  identifier appeared anywhere in `GraphMemoryEngine.ExpandAsync`'s body — only in its signature — while the
  method hard-coded a single hop and the engine's own limit. `MemoryTools` forwards a model-supplied `hops`
  **and declares it in the tool JSON schema**, so an agent calling `project_expand {"hops":3}` received a
  one-hop answer with no error and no signal that its request had been dropped. Both are now honoured:
  the walk is breadth-first over the requested number of levels, deduplicated so a symmetric edge cannot
  return to the seed and a diamond yields its far node once, and `hops: 0` genuinely returns the entry alone
  (which a wiring test already *documented* while reading only `Items[0]`, so it passed either way).
  <br>`hops` is CLAMPED to `GraphMemoryOptions.Hops` rather than honoured unbounded — this is a model-facing
  seam, so an agent must not be able to request a walk of the whole graph. `charBudget` bounds the
  neighbours only. Found by the 2026-08-14 whole-codebase review.

- **Two MCP servers whose names differ only by `-` versus `_` sent one server's bearer token to the other's
  URL.** `AgentMcpServer` names legally contain both characters, and the duplicate check keyed on the RAW
  name, so `app-tools` and `app_tools` both validated. codex derives a bearer-token environment variable per
  server by replacing `-` with `_` and upper-casing — so both collapsed onto one variable holding a single
  token, while both servers' `bearer_token_env_var` pointed at it: one server was presented a credential
  issued for a different endpoint, and the other failed to authenticate. `AgentMcpServers.TryValidate` now
  refuses such a pair, on the NAMES rather than on whether a token happens to be set today, because the
  collision is a property of the derived variable and a token-less pair is the same defect waiting for
  someone to add one. The normalisation now lives once, in `AgentMcpServers.EnvKey`, which the codex config
  renderer calls — two copies of a normalisation rule drift, and the drift is invisible until the halves
  disagree about whether two names are the same. Claude-side keyed on the raw name and was never affected.
  Found by the 2026-08-14 whole-codebase review.

- **The pre-commit leak guard never scanned a staged RENAME.** `check-sensitive`'s staged-file list used
  `git diff --cached --diff-filter=ACM`, and rename detection is on by default — so `git mv` plus an edit
  stages as status `R`, the file list came back EMPTY, and the hook exited 0 having printed nothing. This
  repository's own procedures are built on `git mv` (archiving a document, moving a record into `local/`),
  and a committed leak is a HISTORY problem: the `--tree` sweep would only have caught it once it was
  already in history. Now `ACMR`; `D` stays excluded deliberately, since a deletion has no staged blob to
  scan. Pinned by a test whose precondition asserts git really scored the change as a rename, so it cannot
  pass for the wrong reason. Found by the 2026-08-14 whole-codebase review.

- **Four gates printed a tick over an empty scan.** `check-docs`, `check-encoding`, `check-links` and
  `check-samples` all reported success when their file list came back empty — a wrong repository root or a
  broken scope predicate silently disarmed the gate while producing output indistinguishable from a genuinely
  clean tree. `check-api-vocabulary` already carried the rule and is the model the other four now follow. The
  same failure shape as the staged-rename defect above, which is what makes it a class rather than an
  incident: **the permissive direction is the one no run can report.**
  <br>The guard is on the SOURCE list, not the filtered count, because a filtered zero is sometimes honest —
  written the obvious way it immediately broke check-encoding's own `an unscanned extension is ignored` fact,
  and that test was right. An empty source indicts any caller; zero survivors indicts only the full-tree path,
  where this repository cannot produce one (761 text files, 45 maintained docs, 74 samples). Each of the four
  is pinned in BOTH directions and mutation-checked. `.claude/knowledge/pitfalls.md` §Testing carries the
  rule. Found by the 2026-08-14 whole-codebase review.

- **`consumer-smoke`'s symbol-package check had never run on this machine.** It listed each `.snupkg` with
  `tar -tf` under a comment asserting "bsdtar reads zips on win/git-bash" — but GNU tar is what is installed
  here, and GNU tar cannot read zip at all (`exit 2`, *"This does not look like a tar archive"*). The call
  site was `if (listing.status === 0 && !hasPdb) bail(...)`, so a tar that could not open the archive skipped
  the check, and the step printed `every symbol package carries a PDB ✓` regardless. The release gate's last
  word before publishing was a tick over an inspection that never happened.
  <br>Now read from the archive's own CENTRAL DIRECTORY (`zipEntryNames`), which removes the dependency
  rather than swapping it for another guess about which tar is installed, and an unreadable archive or an
  empty symbol-package set now bails. Verified against all **11** real `.snupkg` files on disk plus a zip
  from an independent producer — the hand-built fixture alone would only have proved the parser agrees with
  its own author. Found by the 2026-08-14 whole-codebase review.

- **`check-packages` could not see one package's missing registration.** The `packableProjects` check searched
  the WHOLE of `devtools/project.config.mjs`, which names `src/Lyntai.Bundle` twice — once in that registry
  and once as `bundle.project` (D26's budget) — so deleting the real registration still matched the second
  occurrence and the gate reported *"every package is registered everywhere it needs to be ✓"*. Proved both
  ways against the real tree, not a fixture. The gate had already learned this exact lesson for the two
  `ApiSurfaceTests` registries and says so in a comment; `packableProjects` never got the treatment. Both the
  forward and the reverse check now read the ARRAY.

- **`dev.mjs e2e` exited 0 when it ran nothing.** A missing `devtools/scripts/e2e/` printed a note and
  returned success — and `verify` propagates that code, so the gate could report green having run no suite at
  all. A selector matching nothing did the same, making `e2e p12` (when the suites are p1–p3) indistinguishable
  from a pass. Both now exit 1, and the selector error lists what is available.

- **New sweep: `memory-enrichment` — WHY an embedder costs recall quality** (closes
  `docs/task-archive.md` Part 69). Registering an `IEmbedder` + `IVectorStore` measurably costs recall
  quality on this corpus, and two write-time mechanisms could explain it with nothing separating them:
  similarity LINKING adding edges, and NOVELTY feeding salience. They separate with knobs that already ship —
  `MinSimilarity` above 1 admits no cosine so no edge is written while the embed and search still run, and
  salience is a DI collection so a neutral policy drops novelty while linking continues.
  <br>**Both are real, and they have different SHAPES — which is why one number never explained it.**
  Similarity linking is a **redistribution, not a cost**: averaged over shapes it takes `topical` misses down
  **−0.2963** and `attribute` down **−0.2758** while driving `critical-rare` **+0.6758** (0.16 → 0.77 on the
  baseline shape). Its aggregate reads as a small cost only because those cancel. Novelty→salience is a broad
  shallow cost, positive on nearly every class, and the only arm that ever helps in aggregate — **−0.0532**
  on the high-noise shape, where there is noise to discriminate against. The two are sub-additive in four of
  five shapes.
  <br>**The only sweep here that calls a REAL model, and it exits rather than substituting one** — the arm it
  replaces used a feature-hashed bag of words in which "semantic similarity" IS word overlap, which is why
  those numbers were withdrawn. Embeddings are cached by text (deterministic input, deterministic output), so
  a real-model run at this scale costs 3,549 calls rather than 41,580.
  <br>**The instrument's own trap is recorded because the first run walked into it:** the shape-level table
  averages over classes and therefore over two large OPPOSING effects, reporting linking as a small cost when
  it is a ±0.3–0.7 redistribution. The sweep now prints both tables.

- **Per-backend contract coverage is now STRUCTURAL rather than counted** (closes `docs/task-archive.md`
  Part 70). All three graph-store backends drive `MemoryGraphStoreContract` from one reflection-fed theory
  source, so a fact added to the contract runs on InMemory, SQLite and Postgres the moment it compiles. The
  hand-bumped `covered` literal is gone; per-fact test names survive as the theory argument.
  <br>**The hole it closes is one direction only, and that is why it needed fixing.** The old
  `Assert.Equal(declared, covered)` caught a fact wired NOWHERE and one wired everywhere except Postgres —
  but not one wired to Postgres ALONE, where the author bumps the literal, it passes, and the other two
  backends silently never run it. The invariant at stake ("a cross-backend invariant enforced on ONE
  backend's test class is not enforced") is one this repository has already been bitten by.
  <br>**Proved by mutation, not asserted:** planting one method on the contract took the contract suites from
  407 to 410 passing — exactly +3, one per backend, nothing wired. Three further mutations are caught too, one
  of them at COMPILE time (`xUnit1015`). A fifth test closes what the fix cannot close by itself — a shipped
  store with no suite at all — by checking the suite list against the `IMemoryGraphStore` implementations the
  packages actually ship.
  <br>The deferral's stated risk did not materialise: the Postgres fixture is shared across its collection and
  xUnit runs a class's cases sequentially, so container startup is still paid once (170 tests, 22s). Suite
  totals moved to **2888 / 2909, 21 skipped**.

- **`check-links` now scans the CODE tiers** (closes `docs/task-archive.md` Part 72, which left the scope
  as an open question rather than widening it in the pass that found the defects). Narrower than the prose
  scan on two axes: **comment lines only**, and **`docs/` targets only** — source files are renamed for
  legitimate reasons, and `pitfalls.md` records an all-paths existence check over prose returning ~45 hits
  and zero defects, while a moved DOCUMENT is what this gate exists for.
  <br>**The entry proposed a third narrowing and the measurement refused it.** Replaying the pre-repair tree
  found **9** genuine dead references in `src/` and `tests/`; an XML-doc-only rule catches **6**, and all
  three it misses were in ordinary `//` comments and all three were real. Part 72's stated hypothesis was
  that `//` comments are where false positives live — every false positive turned out to be a guard script
  naming a FIXTURE, so the line belongs at the TARGET, not the comment style. Cost: six `link-ok`
  annotations, once. Verified by planting a dead reference in `src/` and watching the gate fail.
  <br>The code half deliberately has **no fail-closed guard**, unlike every other scanner here: that check
  needs a source the filtered set can be compared against, and this filter has intentional exclusions, so
  "zero survivors" cannot be told from "nothing to scan" without duplicating the filter. Two attempts proved
  it — each broke a legitimate fixture test. The green line reports the count instead (`44 maintained doc(s)
  + 664 code file(s)`), and a test pins the real tree's above zero.

- **Two shipped memory domains were missing from the documented domain list.** `IMemoryAnnotationPolicy`
  (`Lyntai.Memory.Annotation`) and `IMemoryVerificationPolicy` (`Lyntai.Memory.Verification`) are public
  seams on the frozen 3.0 surface — both exposed on `UseGraph(...)` and the engine constructor — while
  design §5.7 said "the five domains so far" and `CLAUDE.md`'s namespace map listed five.  <!-- count-ok: quotes the pre-fix claim -->
  The tree has **seven**.
  <br>**Why they were the two that got missed, which is the part worth keeping:** both are SINGULAR and
  default to **none**, so nothing constructs them, no test names them, and the whole library runs model-free
  unless a consumer registers one. A domain that is invisible to every other signal is exactly what a
  counted-claim gate is for, so `memory policy domains` is now a registered claim — counted from the SEAMS
  rather than the sub-directories, since `.Engines` is a sub-namespace and not a domain and counting folders
  would give the plausible-and-wrong answer of eight.

- **The migration guide never mentioned `KnownSubjectsAsync`.** It is the only 3.0 addition to
  `IMemoryGraphStore` with a default body, so a 2.5 custom store keeps compiling and silently takes the
  empty-list default — five members break your build and this one quietly limits a feature, which is why it
  now has its own paragraph rather than a place in the list of five. The member arithmetic is stated and was
  measured against the `v2.5.0` tag rather than reconstructed: 8 unchanged + 5 required + 1 defaulted = 14.

- **The ROADMAP's thirty pre-1.0 rows collapsed to one.** Every 0.x version is unlisted on nuget.org (D44),
  so none is resolvable by a consumer and none carries the SemVer promise, which begins at 1.0 — thirty
  scannable rows for a line nobody can install is the opposite of what that table is for, and its own
  preamble says the detail belongs to `CHANGELOG.md`. The single row keeps what the era delivered. The four
  live claims anchored to a 0.x number lost the anchor rather than the meaning (the design contract's
  routing-default note, the platform-kit heading, the job-deferral narrative, the verdict-taxonomy
  provenance in `llm-and-router.md`).
  <br>**The dated AMENDMENTS in the design contract keep their version numbers**, deliberately: an amendment
  is a record of what changed on a date and is accurate BY naming the version of its day — the same reason
  `check-docs` exempts `CHANGELOG.md` and the task archive. Rewriting those would falsify the record rather
  than clean it.

- **`CLAUDE.md` claimed the roadmap shipped through `v0.31`, a version that never existed** — the string
  occurs nowhere else in the tree and the ROADMAP's v0.x sequence ends at `v0.30.0`.

- **New gate: `check-counts`, the fourteenth in `verify`** — fails when a COUNT written in prose disagrees
  with the tree. The third member of the maintained-state family: `check-docs` asks whether a document still
  SAYS what a decision settled, `check-links` whether what it POINTS AT still exists, this whether what it
  COUNTS is still true. `check-docs` structurally cannot see it — its registry holds vocabulary a decision
  RETIRED, and a stale count retires nothing, so the sentence stays grammatical, plausible and wrong.
  <br>**Eight measured incidents, zero automated catches.** `docs/task-archive.md` Part 73 found six
  corrections to a counted claim inside sixty commits; two more went stale during the session that built the
  gate, and the gate caught the second itself. Registered claims: packable packages, `verify` gates,
  migrations, guard-script tests, corpus language arms. Line escape is **`count-ok`**, deliberately not
  `drift-ok`.
  <br>**Its own first counter proved Part 73's warning.** The `verify`-gate counter returned the CORRECT
  total from two cancelling errors — its character class could not match `e2e`, and it counted the inner
  `['--tree']` argument array as a step. Only a test comparing the parsed NAMES caught it, which is why every
  counter is pinned against the real tree rather than a fixture. Two rules keep the registry from rotting: a
  claim matching nothing FAILS, and a counter computing nothing is reported as a broken GATE rather than as
  stale prose — "fix the number" being wrong advice when the counter is what failed.

- **`new-package` spliced a lone LF into every CRLF registry it edited.** The insertion joined with a bare
  `\n` while all seven registries are CRLF in a Windows working copy under `core.autocrlf=true`, leaving
  mixed line endings — committed clean here only because autocrlf normalises on the way in, and committed
  MIXED by anyone whose config does not. No gate can see it: mixed endings are not mojibake, so
  `check-encoding` is blind to them by design. It now joins with the line ending the file already uses,
  pinned in both directions.

- **`drift-ok` silenced the line ABOVE it.** The escape is read from line N and N+1 so that annotating either
  half of a WRAPPED claim works — correct, and why the window exists — but it was applied before the
  line-alone test too, so an ordinary line inherited the exemption of whatever happened to follow it. Two
  unrelated adjacent paragraphs were enough: the second legitimately names the retired thing and is
  annotated, the first silently stops being checked. The two matches now take different escapes. No existing
  annotation in the tree relied on the old behaviour.

- **A recall could return MORE entries than its own `Limit`.** `GraphMemoryOptions.AuthoritativeReserve` is
  configured per ENGINE while `MemoryQuery.Limit` arrives per QUERY, and only the `null` default was capped by
  the limit — an explicit value passed straight through, and the `Take(limit - reserve)` that follows floors
  at zero while the reserved facts are concatenated whole. Measured: reserve `5`, `Limit: 2`, three
  authoritative facts → **three items back and not one ordinary hit**.
  <br>Not a corner case: `5` is a sensible bound against the default limit of `10`, and any caller trimming a
  prompt budget passes something smaller. It contradicted all three places the promise is written down —
  design §5.7 ("within the caller's `Limit`"), `README.md`, `docs/memory.md` — which is what makes it a defect
  rather than an undocumented shape. The reserve is now capped at the limit unconditionally, so the option can
  only ever REDUCE displacement, which is the only direction it is documented to move in. Objective (1) is
  untouched; the default was already unbounded-within-the-limit and still is.
  <br>**`AuthoritativeReserve`'s own XML doc was wrong in the other direction** and is corrected with it: its
  last paragraph claimed the reserve "applies only to entries re-admitted BY GRADE" and that a fact the
  ranking returned on merit "is not counted against this". That describes the FIRST version of the mechanism —
  the one the measurement rejected for changing nothing — and it sat three lines from the engine comment
  saying so in capitals. The reserve counts every authoritative candidate.
- **A non-finite age poisoned an entry's persisted `Difficulty`, through the reinforcement path this library
  recommends.** `DsrRetrievability.Reinforce` has always guarded the STABILITY half against a non-finite
  `MemoryDecayState.Age` and explains why in its own comment — the age arrives per-call in the caller's state,
  so `DsrOptions` cannot validate it. The difficulty half, added later, asserted the opposite ("every term
  feeding `D''` is provably finite"), and both cannot be true: the derived grade is a function of
  `Retrievability`, which is a function of that same age, and `Math.Clamp` PROPAGATES `NaN`. So a `NaN` age
  produced a `NaN` grade, a `NaN` difficulty written straight back by the engine, and a `NaN` row in the
  review log that exists to make parameter fitting possible at all.
  <br>**`RecallAsync` was covered by accident and `ExpandAsync` was not** — `MemoryRankingContract.Rankable`
  drops a non-finite candidate before reinforcement, but an expansion reinforces a node the caller named with
  no ranking in between. That is the act `docs/memory.md` and this release recommend reinforcing on, so the
  unguarded route was the one the documentation points at. `DerivedGrade` now reports NO JUDGEMENT for a
  non-finite retrievability, reusing the meaning `null` already carries for the Δt=0 bypass. Reachable through
  the public `IMemoryAgePolicy` seam; BYO-only, like the ranking overflow fixed above, and fixed for the same
  reason. `+Infinity` is deliberately still computable (it means "fully forgotten" and derives a real grade) —
  only `NaN` is uncomputable, and the fact pinning this says which is which.
- **Seven references in code pointed at documents that no longer exist, in the tier no gate reads.**
  `check-links` was added this release after six such references survived an archive — and it scans
  maintained MARKDOWN only, so the same defect was alive in `src/` and `tests/` the whole time, including two
  in the XML documentation that ships to consumers (`ReciprocalRankFusionPolicy`). Its own header defends the
  `src/` exclusion on the grounds that "the compiler already gates their crefs", which is only part true: the
  compiler resolves `<see cref>` and nothing else, and every one of these lived in a `<c>` tag or a `//`
  comment. All seven repointed at `local/superpowers/…`; the two naming working notes that never existed in
  the repository at all now name the plan that holds them.
  <br>**`check-links`' own `PATH_PATTERN` could not match a `.csproj` path**, because regex alternation takes
  the first branch rather than the longest and `cs` sat ahead of `csproj` — so `src/X/X.csproj` was captured <!-- link-ok: an illustrative placeholder, not a path here -->
  as `src/X/X.cs` and would have been reported as dangling. It fails CLOSED, and was latent only because no <!-- link-ok: as above -->
  maintained doc happens to name one. Extensions are ordered longest-first and pinned by a test that asserts
  every multi-character one.
- **A multi-word CJK query produced NO search terms at all, on every storage backend.**
  `SearchTerms.SubstringTerms` short-circuited on the index pass returning empty and never consulted the
  short grams — so `"配偶 客户"` (two ordinary two-character Chinese words) yielded nothing, and every
  substring backend fell back to matching the whole trimmed query as one literal, `%配偶 客户%`, demanding
  that exact phrase *including the space*. A CJK consumer's multi-word keyword recall simply returned no
  hits, indistinguishable from an empty store.
  <br>**The tell was an asymmetry, not the empty list**: the same token survived when a LONGER word
  accompanied it (`"配偶 叫什么名字"`), because that neighbour made the index pass non-empty and the short
  grams were appended after all. A word kept or dropped according to its neighbours is no design.
  <br>It also defeated the rescue built for exactly this case: Postgres's widening pass consults
  `HasShortSpacelessTerms`, which calls the short grams directly and correctly said "yes, widen" — then paid
  a second round trip that returned the same empty list.
  <br>**Single-token and all-ASCII behaviour is unchanged** (`"配偶"` alone already worked, by the
  coincidence that the whole-query fallback produces the same pattern; `Spaced` does not expand). Terms are
  now de-duplicated across the union, which matters only for a consumer-built `ScriptProfile` declaring equal
  index/substring lengths — it would otherwise double-count a gram in `LikeTermClause.MatchCount`.
  Full detail in `docs/FIXES.md`.
- **Three class-doc comments described pre-3.0 relevance behaviour in the present tense.**
  `PostgresMemoryGraphStore` claimed an authoritative node the query did not match "still lands at the HEAD
  of that gradient", conflating a row's ORDER with the `Relevance` it REPORTS — which has been `0` on every
  backend since 3.0, as the same file's `SeedByTermsAsync` doc already said. `InMemoryMemoryGraphStore`
  claimed "a flat `Relevance` of 1" where its own `SeedAsync` reports `1` for a match and `0` for a
  grade-admitted non-match, so the flat 1 holds only for a query-less enumeration. `MemoryRelevance` repeated
  that second claim verbatim — copied from the neighbouring doc when it was introduced earlier in this same
  release, which is how one wrong sentence became three.
- **`AddMemory()` — the one-line path — silently ignored a registered `IMemoryAnnotationPolicy` or
  `IMemoryVerificationPolicy`.** `MemoryEngineBuilder.UseBestAvailable`, what `AddMemory()` resolves to,
  constructed `GraphMemoryEngine` without passing `annotation:` or `verification:` at all, so both fell to
  the engine's model-free floor. A consumer writing `cfg.AddMemory().AddMemoryVerification()` got a
  registered policy that never ran, while the identical registration behind
  `AddMemoryEngine(…, e => e.UseGraph())` worked.
  <br>**Silent in every direction**: nothing threw, nothing was missing from the container, recall still
  returned hits — the only symptom was worse recall quality. It mattered most for the seam whose own
  registration doc calls it *the single largest recall-quality lever the subsystem has* (a reference judge
  takes miss `0.5357 → 0.2857`), and `AddMemory` is the path this library documents as "the one-line path,
  and deliberately so". `pitfalls.md` §DI/config already named this class — *a documented option that isn't
  wired* — from two earlier occurrences.
  <br>**Fixed at the shape, not the symptom.** The cause was TWO construction sites for one engine with the
  argument list duplicated between them, so the copies drifted; there is now one (`BuildGraph`), and a
  parameter added to the engine reaches both paths by construction. Pinned by
  `GraphMemoryWiringTests.The_one_line_AddMemory_path_honours_a_registered_annotation_and_verification_policy`,
  which fails against the old wiring.
- **Two guard-script tests leaked their git fixture on every run**, unbounded. `check-samples.test.mjs`'s
  `repoWithSources()` helper was the one `makeRepo` caller that never handed its directory to `removeTree`,
  against `_fixtures.mjs`' own stated contract — so each `test-devtools` run left exactly two more full
  `.git` trees in the gitignored scratch area, measured at **190 directories** before the fix. Invisible by
  construction (gitignored, and nothing fails), which is why it needed a measurement — count, run, count —
  rather than a reading. Now zero per run.
- **The test project restored a dependency carrying a known high-severity advisory, and no gate could see
  it.** `Testcontainers.PostgreSql` 4.13.0 pulls `SSH.NET` 2025.1.0 (GHSA-q939-rpr3-3284), so every build of
  `Lyntai.Tests` emitted `NU1903` — unread, because `check-warnings` gates `src/` projects only. That
  exclusion is deliberate and stays (a warning in a PUBLISHED project is a false trim promise reaching
  consumers, which is a different and worse thing), but its cost is that a test-only advisory has no home at
  all. Pinned to the fixed `2026.0.0` through central package management; nothing in this repository uses
  SSH, and the pin exists only to raise what Testcontainers resolves. **Consumers were never exposed** — the
  package reaches no shipped assembly. Full solution build is now `0 Warning(s)`.
- **Six references in maintained documentation pointed at a document that no longer exists**, and a
  thirteenth `verify` gate now makes that unshippable. Untracking the 2026-08-09 ranking × forgetting
  measurement record under the archive-finished-documents rule followed every step of `docs/superpowers/INDEX.md`'s archiving procedure
  except its last — *"repoint every inbound reference, and check nothing dangles"*. `README.md` (×3),
  `docs/2026-07-17-lyntai-design.md` and `docs/DECISIONS.md` (×2) kept naming
  `docs/2026-08-09-memory-policy-measurement.md`, <!-- link-ok: the dead path this entry is ABOUT -->
  plus two more in `CHANGELOG.md`'s live `## Unreleased`
  prefix; all eight now point at `local/superpowers/records/…`. Every gate reported clean the whole time and
  a reader found them, so — as with `check-encoding` — the answer is a gate, not a reminder:
  **`node devtools/dev.mjs check-links`**, existence-only (never line numbers, which rot legitimately),
  skipping `local/**`, sharing `check-docs`' own "is this maintained state?" predicates so the two cannot
  answer it differently. Escapes are per-file in `staleReferenceAllowances` with a reason and **fail when
  they stop matching**; a single deliberate mention takes `link-ok`, deliberately not `drift-ok`.
  <br>**It checks a reference TWO ways, because there are two ways one rots.** The path half asks whether the
  target still exists. The **Part half** asks whether a reference naming a task record — `` `TASKS.md` <!-- link-ok: an ILLUSTRATION of the shape, not a claim about where Part 53 lives -->
  Part 53 `` — names the record that actually holds it: the path resolves, the Part exists, in the OTHER
  file, so nothing else can see it. **Archiving a task is what breaks these**, silently and for every inbound
  reference at once. Five were live when the half was added (three in this section, two in `docs/FIXES.md`);
  the ones in released sections and the archive are correct as records of their own day and are exempt by the
  same predicate `check-docs` uses. A bare `Part 53` naming no record is deliberately ignored — only a
  reference that NAMES one makes a checkable claim.
  <br>Consumer-visible only as documentation that no longer sends you to a 404, or to the wrong record.
- **A connected entry's decay was computed from two different age units at once, and pruning papered over it
  rather than fixing it.** `Age` re-derived from the swap-safe primitives while
  `MemoryDecayState.StrengthAge` stayed the store's raw `position - strengthened_position` subtraction, in
  whatever unit was in force when the edge was last strengthened — so `DsrRetrievability` divided a resolved
  age by an effective stability that a *foreign-unit* strength age had lengthened. It bit any consumer
  registering a `Derivable` age policy (`PerWriteAgePolicy`, `ContentSizeAgePolicy`, `ElapsedAgePolicy` —
  three of the four shipped) and was sharpest after a policy swap, where the residue can be stale by orders of
  magnitude and collapses the connection boost to `1×`. Because deleting a retrievable entry is unrecoverable,
  `GraphMemoryEngine.PruneAsync`'s derivable path had been made to refuse to delete ANY connected entry on the
  retrievability criterion — safe, but it also left a genuinely unretrievable connected entry **unreapable
  forever**.
  <br>An edge now stamps `strengthened_ordinal`/`strengthened_chars`/`strengthened_at` beside
  `strengthened_position`, `GraphNode` reports them as `StrengthOrdinalAge`/`StrengthVolumeAge`/
  `StrengthElapsedAge`, and `GraphNode.StrengthAgeSample` hands them to a policy as the same
  `MemoryAgeSample` the encoding side already uses. The engine projects that axis exactly as it projects
  `Age`, so **the shipped default (one `BurstDampenedAgePolicy`, `Accumulating`) is unchanged byte for
  byte** — and the conservative guard is gone, so pruning is now exact for a connected entry too. Closes the
  "future work" design §5.7 and `PruneAsync`'s own remarks had recorded; taken inside the 3.0 window because
  adding a `GraphNode` member costs a major afterwards.
  <br>**The backfill is the one inexact part, and its direction is chosen.** An existing edge's true strength
  age was never persisted, so every pre-existing edge is treated as strengthened at migration time: a fresher
  edge only ever lengthens effective stability and so can only ever RETAIN, where the opposite backfill would
  hand `PruneAsync` a reason to delete genuinely retrievable entries. Self-correcting on the edge's first real
  strengthening.
  <br>**`GraphNode` gains three trailing members and a `StrengthAgeSample` property** — additive in source,
  binary-breaking like every other trailing addition this release, and source-breaking for a positional
  deconstruction.
- **…and the third age axis with it: `GraphNeighbour.EdgeAge` is projected through the age policies too, so
  all three finally speak one unit.** It was the last one still read as the raw accumulator. This one is the
  mildest of the three — it is divided by `GraphMemoryOptions.EdgeHalfLife`, a constant in the same unit, so
  nothing mixed two units inside one expression, and it only ever ORDERS a traversal rather than deleting
  anything — which makes it a coherence gap rather than a data-loss bug. It is taken here anyway because
  `GraphNeighbour` gaining a member is **binary-breaking**, so the choice was this release or a whole major,
  and because the edge primitives above meant it needed no new schema at all.
  <br>`GraphMemoryOptions.EdgeHalfLife` is therefore denominated in whatever the installed policies count.
  **With the shipped `Accumulating` default that is the position accumulator it always was**, unchanged; only
  a `Derivable` configuration — where the number was already speaking a foreign unit — moves.
  <br>**`GraphNeighbour` gains `EdgeOrdinalAge`/`EdgeVolumeAge`/`EdgeElapsedAge` and an `EdgeAgeSample`
  property.** Distinct from `GraphNode.StrengthAgeSample`, which is the MAX across ALL of a node's edges;
  this is the single edge that actually reached it. A custom `IMemoryGraphStore` should populate them —
  leaving them at `0` tells the engine every edge was strengthened just now.

- **An authoritative fact the query never matched now reports `GraphNode.Relevance` 0 on every backend —
  previously all three disagreed, and SQLite disagreed with itself.** The old contract declared the position
  of such a node explicitly backend-specific: SQLite's full-text path put it at the TAIL of the gradient, its
  substring-fallback path at the HEAD (grade leads that `ORDER BY`), Postgres at the head, the in-process
  store a flat 1. So the same data and the same query produced a different recall ORDER per backend — the
  engine multiplies relevance into its rank — and "reconciling the three is open work" was recorded in the
  contract itself rather than in any backlog.
  <br>`0` is what "how well it matched the query" honestly says about something admitted by GRADE instead. A
  fact that genuinely matched keeps its match-derived position, and with no query at all nothing is
  grade-admitted, so each backend's own gradient is untouched.
  <br>**The admission guarantee is unweakened, because relevance was never what carried it**: an exact fact is
  admitted by the grade carve-out in `SeedAsync`, and `GraphMemoryEngine` separately re-admits any
  authoritative candidate a ranking policy dropped. Reporting a relevance it had not earned was a third,
  weaker mechanism competing with those two — and a misleading number besides. Pinned by two contract facts
  across all three backends, one per SQLite query path, each asserting BOTH that the non-match reads 0 and
  that the real match still reads above 0 (asserting only the first would pass against a backend that zeroed
  everything).
- **Graph memory: authoritative ("exact") facts are now genuinely admitted to a recall, on every backend.**
  `IMemoryGraphStore.SeedAsync` has always documented that authoritative nodes are admitted unconditionally —
  the query does not exclude them — and two separate routes made that false in practice. Recall output
  changes for any application storing `MemoryGrade.Authoritative` material, so re-check anything asserting on
  exact recall contents.
  - **A stored fact the query did not match could be missing entirely.** On SQLite, whenever the trigram index
    matched *something else* in scope, the seed returned early on a query that never carried the grade
    carve-out: ask about "restaurant" and a stored dietary constraint never reached the prompt. The FTS path
    now fetches the scope's authoritative facts alongside the matches and merges them, reserving capacity so a
    full page of matches cannot squeeze them out.
  - **A long-quiet fact was cut by the candidate limit on all three backends.** A fact nobody has recalled
    since writing it holds the *oldest* position in its scope, so a recency-ordered candidate query sorted it
    last and the limit dropped it before anything was ranked — the guarantee defeated by ordering rather than
    by filtering. Seeding now orders by grade first and recency second on SQLite, Postgres and the in-process
    store, so the limit cuts the freshest associative material instead.
  - The bound on that guarantee is now stated rather than implied: "admitted unconditionally" means the QUERY
    never excludes an authoritative node, **not** that a finite candidate limit can hold them all. A scope
    holding more authoritative facts than the limit still loses some, by recency, and which ones is
    deliberately unspecified (`docs/DECISIONS.md` — the authoritative-slot-within-limit promise).
  - Where an admitted-but-non-matching authoritative node sits in the reported `GraphNode.Relevance` ordering
    is **backend-specific and not part of the contract** — one backend reports it at the tail of its gradient,
    another at the head, another as a flat 1. Rank authoritative material by its grade, never by its
    relevance.
- **A salience below 1, or a non-finite one, behaved differently at every site that read it** — and both are
  reachable through the public `IMemorySaliencePolicy` extension point. Four read sites had three rules: the two
  SQL stores coerced (`IsFinite ? Math.Max(1, x) : 1`) into the promoted column, the in-process store ordered
  the raw bag value, and `GraphMemoryEngine`'s rank boost applied `Math.Max(1, x)` with no finiteness guard.
  So `{salience: 0.5}` admitted level with unappraised rows on SQL and BELOW them in-process — same data, same
  query, different backend. And `NaN` failed three different ways: SQLite refused to bind it and the whole
  WRITE threw; Postgres bound it into a `NOT NULL` column every seed query orders on, where SQL sorts `NaN`
  above every real number; and with `SalienceRankWeight > 0` the engine's `Math.Max(1, NaN)` is `NaN` per
  IEEE 754, making every candidate's rank — and hence the relative floor — `NaN`, so `Rank >= floor` was false
  for every candidate and recall returned **nothing at all**, silently. There is now ONE coercion,
  `MemorySignals.Salience` (below 1 and non-finite both become the neutral 1), and all four sites read the
  value through it. Pinned on all three backends by the shared `IMemoryGraphStore` contract, not on one of
  them.
- **`ElapsedAgePolicy` and `BurstDampenedAgePolicy` no longer disagree with themselves when one instance is
  shared across engines.** Both kept their write-time bookkeeping (`ElapsedAgePolicy`'s "previous write"
  timestamp; `BurstDampenedAgePolicy`'s burst-window state) in a single scalar per POLICY INSTANCE, so the
  ordinary DI-singleton shape — one registered `IMemoryAgePolicy` resolved for every named engine — measured
  "since the last write to ANY of them" rather than "since the last write to THIS one": engine A's write could
  reset engine B's own elapsed clock, or extend B's burst, with no error and no symptom beyond a wrong number.
  Both now key their bookkeeping on the write's own owning engine name (`Advance`'s new `engine` parameter,
  below), so a shared instance tracks every engine independently.
- **A connected entry's stale `Strength`/`StrengthAge` could no longer be deleted by mistake after a policy
  swap — `GraphMemoryEngine.PruneAsync`'s derivable path is now conservative about the one age-adjacent
  number the earlier fix above did not re-derive.**
  <br>**SUPERSEDED — the conservative guard described here is GONE in 3.0.** It was a stopgap for an edge
  carrying no age primitives of its own, which left a genuinely unretrievable connected entry unreapable
  forever; the entry above ("A connected entry's decay was computed from two different age units at once")
  gave an edge the same three primitives a node has, so `PruneAsync`'s derivable path is now EXACT for a
  connected entry and refuses to delete nothing. The curve named in this entry was also deleted later in this
  same section. <!-- drift-ok: an amendment naming what it corrects --> `Age` re-derives from the swap-safe primitives; `Strength`/
  `StrengthAge` are still a raw `position - strengthened_position` subtraction in whatever unit was in force
  when an edge was last strengthened. After a swap, that residue can be stale by orders of magnitude,
  collapsing `HalfLifeRetrievability`'s connection boost to `1×` regardless of how recently the edge actually
  strengthened in the new unit — a connected entry could read as far less retrievable than it truly is, and
  `PruneAsync` would delete it. Retaining a prunable entry is recoverable; deleting a retrievable one is not,
  so the derivable path now never deletes an entry with `Strength > 0 && StrengthAge > 0` on the
  retrievability criterion alone (the `olderThan` criterion, which reads neither field, is unaffected). The
  method's own remarks previously claimed this path was "an exact evaluation, not merely a conservative
  superset" — that was true for `Age` alone and false for the whole evaluation; the remarks now say so.
  Deriving a per-edge, swap-safe `StrengthAge` is tracked as future work, not shipped here.
  <br>**`GraphMemoryEngine` gains a trailing `clock` parameter** (`Func<DateTimeOffset>?`, defaulting to
  `DateTimeOffset.UtcNow`) — additive in source, binary-breaking like every other trailing optional parameter
  in this release. The derivable prune path's `olderThan` criterion read the wall clock directly and had no
  seam of its own, disagreeing with a test that fakes the STORE's already-injectable clock; it now reads this
  parameter instead.
- **Provenance uniqueness is now checked against whatever is actually REGISTERED, not a hand-listed test
  array.** A third salience appraiser (or a consumer's own retrievability policy) landing on a bit a shipped
  one already used, or declaring `Provenance = None`, previously compiled and ran with nothing reporting it —
  only a test that happened to enumerate every live implementation by hand could catch it, and a fourth
  implementation would simply not be added to that list. `MemoryProvenance.ValidateProvenanceBits`
  is the new production check — real (never `None`), single-bit, and unique among DIFFERENTLY-typed policies
  (two REGISTERED INSTANCES of the SAME type sharing a bit is not a collision: "did this algorithm run" is
  unambiguous either way) — called from `GraphMemoryEngine`'s constructor for both the salience-appraiser
  collection and the single active retrievability policy, so a violation throws `ArgumentException` at
  construction rather than silently corrupting a fitness check later.


- **An OpenAI-compatible backend that reported a failure at HTTP 200 was never classified — and the request
  was re-sent to it first.** A gateway answering `200` with `{"error":{"code":429,…}}` produced
  `Failed: "malformed or empty response after retry"`, having sent the identical request a second time to a
  host that had just reported a rate limit. `Failed` ADVANCES and takes a dead-host strike where
  `RateLimited` COOLS, so an honest backend was penalised toward being benched. Both the buffered and the
  streaming path now read the in-band channel through one reader and classify through the shared corpus,
  with the same `AuthFailed → NotConfigured` promotion the status path makes. **This is the third instance
  of one class** — the CLI engine shipped it twice — so the rule is worth restating: *whenever a backend can
  answer in two channels, decide the precedence explicitly and pin it with a test.* See `docs/FIXES.md`.

- **A durable render polled a backend that could never answer, forever.** `FalQueueProvider` reported EVERY
  status-call failure as `Running`, including a 4xx for an id fal does not know and a backend whose key was
  rotated away — so the job re-checkpointed and polled every 15 seconds for the life of the process, never
  dead-lettered and never failed, with the reason sitting in `Detail` where nothing acts on it. A failure
  that never reached the queue still leaves the render alive; one the queue ANSWERED is now terminal. This
  is `ComfyUiProvider`'s existing rule, which had reasoned the case through in writing while its sibling in
  the same package answered the opposite way.

- **A CLI turn that finished cleanly could be reported as a stall, and paid for twice.** The buffered
  `ProcessRunner.RunAsync` decided "timed out" on the cancellation flag alone, but `KillTree` is a no-op
  against a process that has already gone — so a child exiting `0` as the clock fired had its complete
  stdout discarded and reported as `Timeout`, and `CliProviderEngine` branches on that before it parses
  stdout, so the router paid for a second turn. `StreamLinesAsync` had always asked the fuller question; the
  two now ask it through one function rather than two copies kept in step by review.


- **A headline-only query found the fact on SQLite and nowhere else.** `lyntai_memory_node_fts` declares
  `headline, content` and an unconfined FTS5 expression matches either, while Postgres's trigram index and
  the in-process store match content only — so the same call answered differently per backend, and SQLite
  disagreed with itself depending on whether the trigram index hit. `IMemoryGraphStore.SeedAsync`'s written
  portable guarantee is content-only, so SQLite was the outlier exceeding the contract; the FTS expression
  is now confined to `content`, with no migration and no released schema touched. Pinned by two facts on
  `MemoryGraphStoreContract`, which run on all three backends by construction.
  <br>Widening all three to match headlines instead is a legitimate additive change and a separate
  decision — it needs a Postgres index and a recall-quality measurement, neither of which a divergence fix
  should smuggle in.

- **The in-process stores ranked by recency where their own interface docs promise matched-term count.**
  `IMemoryStore.RecallAsync`, `ICuratedMemoryStore.SearchAsync` and `storage.md` all state that Postgres
  *and* InMemory rank by matched terms then recency; both SQL stores did and neither in-process store did.
  With a `limit` that is a different ANSWER, not a different order — an entry matching one term displaced
  one matching every term by being newer. New `SearchTerms.MatchCount` is the in-process twin of the count
  expression `LikeClause` already builds for SQL, so the split and the scoring stay one rule.

- **`PostgresMemoryGraphStore` opened every connection synchronously — all fourteen sites, alone among the
  twelve Postgres stores.** `SeedAsync` runs on every recall, so under concurrent recalls each call blocked
  a thread-pool thread for a full TCP connect and authentication, and the cancellation token could not reach
  the connect at all. No wrong data; a liveness defect, of the kind `IDbConnectionFactory` exists to prevent.


- **The hosted MCP endpoint ran the application's tools with no guard gating at all.** An app that
  registered an `IGuard` had it enforced through `IToolLoop` and silently NOT enforced when a CLI's own
  agent called the same tool over the hosted endpoint — the same `ITool` instances, reached by a second
  door. `AddMcpToolHost` now resolves `IGuardRail` optionally and gates both the call's args and its
  observation, exactly as the tool loop does. A blocked call does not execute and its payload is never
  produced; with no guards registered nothing changes. See `docs/FIXES.md`.


- **`MemoryRecall.Answered` was `null` on every DI-registered engine, so 3.0's abstention signal was
  unreachable through the documented path.** `CompositeMemoryEngine.RecallAsync` returned
  `new MemoryRecall(items, ran)` — the third positional argument defaulted away — so a judge that ran and
  reported `false` was indistinguishable from no judge at all, which is the one distinction the field
  exists to make. `docs/memory.md`'s own *"know when the memory has nothing useful"* sample tests
  `recall.Answered == false` and could never fire.
  <br>It now folds across members as a three-value lattice: **`true`** if any member's judge found an
  answer, **`false`** if at least one judged and none did, **`null`** only when nothing judged anywhere —
  the last clause being what stops the shipped no-verifier default from ever synthesising `false`, which
  would make a consumer abstaining on `false` abstain on everything.

- **`MemoryQuery.CharBudget` was reconciled by nothing at a blend, so an N-member engine could spend N× the
  budget.** The same two-scopes defect as the `Limit` cut one field over — a bound configured on the query
  and enforced nowhere — and it lands on a value callers set precisely because it is a *prompt* budget. The
  blend now applies `GraphMemoryEngine`'s own rule verbatim, so a blend and a bare engine cannot answer one
  query differently: after the limit so the budget cuts the weakest tail rather than changing what wins, an
  authoritative item is never dropped by it, and a budget too small for even one item still yields one.

### Tests

- **A contract fact that depended on machine load has been made deterministic.**
  `Advance_is_keyed_per_engine` asserts an interleaved run equals an isolated one to nine decimal places —
  which for `ElapsedAgePolicy` is a wall-clock quantity, so the interleaved arm also absorbed however long
  the intervening writes took. It held only while the machine was fast enough for both arms to round to
  zero, and it failed inside a full-suite run and passed alone. That policy now takes a frozen clock for the
  shared fact, and the property the fact was standing in for — elapsed is measured from THIS engine's own
  last write — is asserted directly, on a clock the test moves.

### Internal (no public surface change)

- **The front-door fold is built once for every client the container hands out.** It was written twice — the
  default `ILlmClient` and each named one — with a comment above the second copy asserting the parity the
  two were supposed to maintain by hand. Nothing enforced it: deleting the refusal screening from the named
  copy left the whole suite green, and any new outermost layer added to the default would have been silently
  absent from every named client, which is what `AddMemoryAnnotation` and `AddMemoryVerification` resolve
  through. Only the ROUTER differs now (the default takes the container's, a name takes one narrowed to its
  provider set), and that difference is the parameter.

## 2.5.0 — 2026-08-08

### Added

- **Named memory engines** (`IMemoryEngine`, `IMemoryEngineFactory`, `AddMemory()` /
  `AddMemoryEngine(name, …)` / `UseMemoryComposer(name)`) — several memory systems can now coexist in one
  application and are resolved **by name**, the way `IHttpClientFactory` resolves clients. Until now every
  memory surface was a single unnamed singleton, so an application wanting a *chat* memory and a *project*
  memory had to wrap all of it itself — the same wrapper in every consumer, and none of them able to share
  it. Registration is a DI collection keyed by `Name`, the same variation-point shape as `ILlmProvider`
  keyed by `Id` and picked by `ILlmRouter`, so a fourth kind of memory is a class plus a registration.
  Thin engines adapt the three existing stores (`IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore`),
  and a **blend is itself an engine** (`CompositeMemoryEngine`), so naming, blending, remembering and
  expanding stay one concept. Optional abilities are separate interfaces (`IExpandableMemory`,
  `ILinkableMemory`, `IForgettableMemory`) an engine may also implement, and the composite forwards them by
  routing on `MemoryRef.Engine` rather than guessing — the regression that once made every queue-backed
  render unroutable in the generation router is pinned here by a test.
- **Prompt composition now protects exact facts from being crowded out** (`MemoryComposition.ComposeAsync`,
  `MemoryCompositionOptions`, `MemoryGrade`). Material carries a grade: **authoritative** content never
  decays, is never truncated to a derived headline, is allocated from a **reserved** character budget
  *before* any associative content is admitted, and renders in its own labelled section so the model is
  told which material is exact rather than left to infer it. Today's flat 4000-character budget fills in
  rank order, so a burst of loosely-relevant recall can push a hard constraint out of the prompt entirely
  while the prompt still looks full. Authoritative material that genuinely cannot fit emits an explicit
  `… N further authoritative facts omitted (budget)` line rather than disappearing, and an authoritative
  write is **routed** to an engine that can hold it or throws — never silently downgraded.
- **A one-line path that needs nothing implemented**: `AddMemory()` registers a working engine and backs
  `ChatOrchestrator`'s `IPromptComposer` with it. The fluent builder exists for applications that want to
  *differ*. A duplicate engine name or member label fails at **configure** time, and a member whose backing
  store is absent fails at **startup** naming the store — rather than resolving to a permanently empty
  memory section that reads exactly like "nothing matched".

- **Graph memory: entries that decay, connect, and open as an index** (`GraphMemoryEngine`,
  `IMemoryGraphStore`, `UseGraph()`). Recall returns **headlines** and withholds the full text until
  expansion, so a session opens on a cheap index and pays for depth only along the direction it turns out
  to need. Entries **decay** unless reused — retrievability is computed at read time from stored
  counters, so there is no sweeper and no background job — and each successful recall lengthens an entry's half-life,
  making repeated context durable while one-off noise sinks beneath fresher material. Decay only ever
  **ranks**;
  deletion stays explicit via `PruneAsync`. Entries **connect** model-free: whatever is returned together
  gets linked, the link strengthens on recurrence, and a later recall spreads through those links to reach
  material the query never matched.
- **The decay curve is a seam** (`IRetrievabilityPolicy`, `HalfLifeRetrievability`, `HalfLifeOptions`).
  Exposing the constants alone would settle the values while freezing the formula, so an application can
  tune the numbers *or* replace the model of forgetting, and neither choice forecloses the other. The
  default is registered, so nothing has to be implemented. Storage never evaluates the curve — a policy
  supplies a conservative `CandidateCutoff` and the store bounds its candidate set with plain division,
  which keeps a custom curve possible and avoids depending on SQLite's optional `pow`.
  `HalfLifeOptions.MaxStability` caps reinforcement: unbounded compounding multiplies a half-life by more
  than three thousand in about twenty recalls, which would silently give a frequently-recalled *associative*
  entry the durability of an authoritative one without any of its guarantees.
- **Decay is measured in what has HAPPENED in a memory, not in elapsed time** (`IMemoryClock`,
  `PerWriteClock`, `ContentSizeClock`, `ElapsedClock`, `BurstDampenedClock`). Each engine keeps a position
  that advances when something is written to it; an entry's age is how far that position has moved since it
  was last used. **A rarely-used memory therefore decays slowly and a busy one decays fast** — automatic,
  and the reverse of what wall-clock time gives you. Reading never ages anything. What the position
  *counts* is a seam with four shipped implementations, because "how much has happened" is genuinely
  ambiguous — writes, volume, or real time — and a project memory can decay on the calendar while a chat
  memory decays by volume in the same application.
  **Bursts saturate**, which is a correctness matter rather than a refinement: advancing once per write is
  linear, so a 500-item bulk ingest would age every pre-existing entry by 500 and erase everything known
  before it. A damped burst advances by about `ln n`, and its own entries start with a proportionally
  shorter half-life — so a long document neither wipes your memory nor is itself remembered in full.
  Consequences: no date arithmetic appears in any query (removing the hazard where SQLite's `julianday`
  returning NULL on an unparseable timestamp would silently exclude every row), and the decay constants no
  longer carry `TimeSpan`, which asserted a dimension the application had not chosen.
- **Decay buries an entry rather than cutting it** (`GraphMemoryOptions.RelativeFloor`). Recall ranks
  everything and hides only what falls far below the *strongest* hit, so a faint memory alone in a quiet
  engine still surfaces while the same memory under fresher material does not — and either way it stays
  reachable by reference or through a neighbour, reporting how faint it has become. Seeding applies no
  faintness bound at all; `MinRetrievability` now governs `PruneAsync` alone, where removing a memory is
  the explicit intent.
- **Connectedness feeds decay, and edges decay too.** A memory woven into a dense, repeatedly-reinforced
  neighbourhood now resists forgetting, while an isolated one fades — connections make an entry more
  *durable*, not merely more reachable. Symmetrically, edge weight decays with disuse
  (`HalfLifeOptions.EdgeHalfLife`), so a link that stops recurring stops pulling its neighbour into recall
  and stops propping it up; without that, every pair that had ever co-occurred would stay linked at a rising
  weight until spreading reached everything from everything. Both are read-time, so there is still no
  sweeper.
- **Graph memory persists** — `IMemoryGraphStore` now has SQLite and Postgres backends alongside InMemory,
  all held to one contract. SQLite indexes headline and content through an FTS5 **trigram** mirror (so CJK
  substring recall works, which a word-boundary tokenizer would silently return nothing for); Postgres uses
  a `pg_trgm` GIN index. Neither evaluates the decay curve: candidates are bounded by plain division
  against the cutoff the policy supplies, which is what keeps a caller-supplied curve possible and avoids
  depending on SQLite's optional `pow`.
- **Graph memory is available to the model**, not just to the prompt composer —
  `AddMemoryTools(engine, taskKey)` registers `{engine}_recall` and `{engine}_expand` as ordinary tools, so
  they reach the tool loop and the MCP bridge alike. Recall returns headlines and a `ref`; expand takes
  that ref and returns the full text plus what the item is linked to. Names are prefixed per engine rather
  than one multiplexed tool taking an engine argument, because the multiplexed form lets a model consult
  the *wrong* memory. `MemoryToolScope.Use(taskKey)` overrides the registered default for the current async
  flow, which a chat application needs since its task is per-conversation.
- **Similarity enrichment** — when an `IEmbedder` and an `IVectorStore` are registered, a new graph entry is
  also linked to its nearest existing neighbours (`GraphMemoryOptions.SimilarityK`, `MinSimilarity`). Pure
  enrichment on top of the model-free floor: without it the graph still forms from co-activation and
  explicit links, and a failing embedder costs an entry some links, never the entry itself. Connectedness may only ever *raise* retrievability, and `MaxConnectionBoost` bounds how far —
  which is load-bearing rather than cosmetic, since `CandidateCutoff` widens by exactly that factor and an
  unbounded boost would leave well-connected entries outside any finite cutoff, silently losing the very
  memories connectedness was meant to protect.
  **Several constants are unmeasured and say so in their XML docs** — the MEM-TUNE task closes them, and
  the three governing connectedness have to be measured *together*, since edge decay erodes the strength
  that feeds the boost.

Purely additive: `IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore` and `MemoryPromptComposer` are
unchanged, and an application that never calls `AddMemory`/`AddMemoryEngine` observes no difference.
No new package. Designs: `local/superpowers/specs/2026-08-08-memory-engine-seam-design.md` (MEM1) and
`2026-08-08-graph-memory-engine-design.md` (MEM2a).

## 2.4.0 — 2026-08-05

### Added

- **An agent session can be given the host application's own MCP servers, on either CLI backend**
  (`AgentSessionOptions.McpServers`, `AgentMcpServer`, `McpTransport` — all neutral `Lyntai.Core`). Until now
  the only way to point an `IAgentSession` at app tools was `ClaudeAgentOptions.McpConfigPath`, so the two
  backends were interchangeable only for an agent that needed no app tools — and adopting `CodexAgentSession`
  meant spawning codex with **no MCP servers at all**, a silent capability loss rather than an error. The new
  type expresses **stdio** (command/args/env — how an app ships its own tools as a child process) as well as
  **HTTP**, and each adapter renders it in its own MEASURED vocabulary: claude an owner-only `--mcp-config`
  document deleted when the turn ends, codex repeated `-c mcp_servers.<name>.…` TOML overrides. A caller's
  existing `McpConfigPath` is kept **alongside** the rendered one (the flag takes a list), an `AuthToken`
  never reaches argv, naming a server does **not** pre-approve its tools, and an entry that cannot be
  rendered refuses the turn instead of being dropped. Additive — `McpServers` defaults to empty and no
  existing behaviour changes. Closes CLI14, filed by a consuming app; reasoning in `docs/DECISIONS.md` — the neutral MCP-servers option.

### Fixed — documentation

- **The README no longer says the codex agent session refuses `ResumeToken`.** It has been honoured since
  CLI13 (measured 2026-08-05 via the turn-free `codex exec resume --help`); the bullet describing the old
  refusal was left behind and now describes what actually happens, including the one token shape still
  refused and the unmeasured `SessionId`-on-resume caveat.

### Changed — behaviour

- **A CLI backend's own account of a failed turn now wins over the process exit code.**
  `CliProviderEngine.CompleteAsync` returned on a non-zero exit **before** parsing stdout, so a turn that
  reported its failure in band *and* also exited non-zero was classified from whatever happened to be on
  stderr. Measured on codex-cli 0.146.0 (2026-08-05, an account whose login had expired): the turn printed
  `{"type":"turn.failed","error":{"message":"… 401 …"}}`, exited non-zero, and carried nothing but ordinary
  startup chatter (`Reading prompt from stdin...`) on stderr — so the reply was `Failed` with the detail
  `exit 1: Reading prompt from stdin...`, which is neither the reason nor the right remedy.
  **Observable delta:** that call now reports the classified in-band verdict — here `AuthFailed`, which cools
  the host for the cooldown window rather than merely advancing — and the backend's own message as the
  detail, with the exit code kept as context (`exit 1: <message>`). A non-zero exit with **no** in-band
  failure is unchanged, as are the streaming path and both agent sessions (each of which already preferred
  the in-band terminal). This is the ordering `StatusAsync` has always used — parse the answer, then fall
  back to the exit code — which the completion path never had. Filed as CLI15 by a consuming app that hit
  the same shape in its own reader.

## 2.3.0 — 2026-08-05

_Everything below was finished before 2.2.0 was cut but landed **after** it: the release workflow runs against
the pushed branch, and this work had not been pushed, so 2.2.0 shipped without any of it (see
`.claude/knowledge/pitfalls.md` §Environment/tooling). Nothing here is in the published 2.2.0 packages — it ships in the next release._

### Fixed — the pre-release whole-library review (2026-08-05)

_A review of all twelve packages and the test suite, verified adversarially. Everything in **this** section is
detectable at compile time or observable only in a log or a metric; every change that a consumer cannot detect
at compile time is in **Changed — behaviour fixes…** below, disclosed one by one as `docs/DECISIONS.md` — the deferred-SemVer-strictness rule
requires. (They were split because the behaviour half was originally deferred to a major; the deferred-SemVer-strictness rule removed that
constraint and both halves ship together.) The `Lyntai.Generation` package is EXPERIMENTAL and exempt, so its
behaviour fixes are called out here as well as there._

- **A throttled STREAMING call now reports itself.** `RateLimitedLlmClient.StreamAsync` hand-rolled its
  refusal chunk and called neither the logger nor the counter, so a streaming-only workload being throttled
  reported **zero** on `lyntai.ratelimit.refusals` — the metric the feature ships to be observed through. The
  chunk the caller receives is unchanged; only the log line and the counter that were always meant to fire
  now do.
- **`AddRateLimit` no longer reports "no effective limit" at a consumer it is actively blocking.** A
  per-consumer entry with a zero rate is a deliberate burst-then-block, but the startup predicate required a
  *positive* rate, so a configuration consisting only of those logged "it will not throttle" while throttling.
- **A budgeted call no longer reads the usage total when nothing caps it.** `BudgetedLlmClient` issued a
  pre-call global read on **every** request even with no global cap configured — for the SQLite and Postgres
  trackers, a whole-table `SUM` per request. Now read only when a cap needs it, matching the generation twin.
  Refusal outcomes are identical in every configuration.
- **Dead-host state is dropped on recovery** rather than zeroed, so a long-lived process no longer retains an
  entry per configuration that ever recovered. Every reader already treated missing and zeroed identically.
  This matters more since cooldown began keying on a `ProviderKey`, which changes with every credential
  rotation.
- **A CLI dialect now sees the same line on both paths.** The buffered path split on `\n` and handed dialects
  a trailing `\r` from a CRLF-emitting child, while the streamed path stripped it. No shipped dialect was
  affected (both tolerate it); a third-party dialect doing an exact match was broken on one path only.
- **`Lyntai.Generation` (EXPERIMENTAL), two behaviour fixes.** `FalQueueProvider` **refuses** an input that
  carries only bytes instead of dropping it — the drop submitted, and billed, a text-to-video render for a
  caller who asked for image→video, and the result looked plausible. And `ComfyUiProvider` now distinguishes a
  transport failure from a terminal one when polling: an unreachable server or a 5xx reports `Running` (the
  render is still going), while a 4xx or an unconfigured base URL stays `Failed`, so a poll no longer abandons
  a live render — nor polls a dead id forever.

### Breaking — two documented compile-time breaks (2026-08-05)

Both ship in a minor under `docs/DECISIONS.md` — the deferred-SemVer-strictness rule, which allows a documented, compile-time-**detectable**
break while every consumer is first-party. Neither can bind silently: each is a compile error that names the
fix.

- **`OpenAiCompatibleOptions.ContextSize` → `OllamaContextSize`.** The option only ever affected the
  Ollama-native payload (`options.num_ctx` on `/api/chat`) and was silently ignored by every other flavour —
  including Ollama's *own* OpenAI-compatible `/v1` surface — so the generic name invited exactly the
  configuration that does nothing. The type (`int?`) and behaviour are unchanged; only the name says which
  backend it is for. Verified against the code before renaming.
- **`ICuratedMemoryStore.UpdateAsync` takes `taskKey` and `scope`**, inserted before `metadata` so the
  identity fields read in the same order as `AddAsync`. Named-argument callers are unaffected; a positional
  call that reached `metadata` or `ct` must move to named arguments — always a compile error, never a silent
  re-bind, since the types are not interconvertible. **Every BYO `ICuratedMemoryStore` implementation must add
  the two parameters.**

### Changed — behaviour fixes that a consumer cannot detect at compile time (2026-08-05)

**Read this section before upgrading.** Everything here changes what the library *does*, with no compile-time
signal. It ships in a minor under `docs/DECISIONS.md` — the deferred-SemVer-strictness rule, which amends the deferred-SemVer-strictness rule's third bullet while every
consumer is first-party — and the deferred-SemVer-strictness rule's entire price is that each change names its observable delta, which is what
this section is. (Storage and migration breaks remain major-bump material, unconditionally. None here.)

- **A repeated generation candidate is attempted once, and no longer costs the sole-candidate exemption.**
  Before, a list naming one backend twice called it twice per request *and* made `capable.Count == 2`, so
  `ExemptSoleCandidate` did not fire: the only capable backend was benched after one rate limit, and the next
  call returned a synthesized "every capable media backend is on dead-host cooldown" instead of the backend's
  own actionable verdict. Now repeats collapse (first wins, order preserved). Deduping is on the *resolved*
  (backend, effective model) pair, so `["fal", "FAL"]` is one candidate — but two different models at one
  backend are still two candidates.
- **A rejected media submission is now answered by the routing policy instead of always being penalized.**
  Before, every rejected `SubmitAsync` advanced *and* took a dead-host strike regardless of why. Now the
  backend's own rejection text is classified and the policy decides: a rate-limit/auth rejection benches
  immediately rather than taking 1-of-N strikes; a blameless one (`NotConfigured`/`Unsupported`) advances with
  **no** dead-host bookkeeping, so an unconfigured queue stays in rotation and a key set later is picked up;
  an unclassifiable one behaves exactly as before. **The one delta that can stop a fallback you were getting:**
  a rejection classifying as `Refused` (a content-policy body) now *surfaces* instead of being re-submitted to
  the next queue — matching the inline path. Restore the old behaviour with
  `policy.On(GenerationVerdict.Refused, GenerationFallbackAction.Advance)`.
- **A failed submission now names the backend's reason.** The message keeps its `no capable media backend
  accepted a 'video' job among [...]` prefix (substring checks still hold) and gains
  `— 'fal' said: <the backend's own words>`. `GenerationSubmission.ProviderId` is deliberately still empty on
  every non-accepting path, because `IGenerationRouter` documents empty as "no candidate accepted".
- **`LlmRouter` matches candidate ids case-insensitively**, like every other id lookup in the library. Before,
  `new LlmCandidate("OpenAI")` against a provider reporting `openai` matched nothing and produced
  `"no live candidate"` even as the only registered backend — reachable, because the pool guard deliberately
  accepts a slot whose case differs from the instance's `Id`, so such a provider was built, pooled, and never
  selected. Candidates differing only in id case now dedup to one. Affected: anyone relying on case to keep
  two same-named providers apart.
- **A streamed CLI completion is now bounded by an absolute clock, not only by child inactivity.** Before, a
  child that kept printing but never terminated streamed forever, since every line re-armed the inactivity
  window. Now `LyntaiOptions.MaxProviderTimeout` (default 30 min) is also a ceiling, raised to match a larger
  app-configured `TimeoutByConsumer` budget rather than clamping it. On expiry the process tree is killed and
  the stream ends in a terminal `Timeout` chunk naming the window that fired. **Not** applied to
  `ClaudeAgentSession`/`CodexAgentSession` — those are long-running agent turns and a wall clock there would
  kill healthy hour-long sessions.
- **An empty content event from a CLI dialect is no longer "delivered content".** It is ignored rather than
  yielded, so a stream with no other output now ends `Error(Failed, "no output produced")` and the router can
  fall over — where before it ended `Final`, a fully successful *empty* answer, and a zero-content first chunk
  committed the router's stream and disabled fallback for the whole turn. It also no longer suppresses the
  answer reported on a terminal `result` line. Both shipped dialects already guarded, so `claude` and `codex`
  users see no change; this moves the invariant into the engine so it no longer depends on third-party code.
- **The Ollama-native flavour sends image attachments.** Before, `/api/chat` was posted with text only and the
  attachment vanished — nothing on the wire, nothing logged — while the README promised images were sent. Now
  inline bytes travel as Ollama's own `images` array on the user turn, and a URL-only attachment (which
  `/api/chat` has no form for) is logged as undeliverable instead of vanishing. **Two directions of surprise:**
  a consumer who adapted to the drop now really sends images, so a text-only model may answer worse or error;
  and request bodies get larger.
- **A usage token count that is not an integral number no longer throws.** A fractional or out-of-range count
  from a gateway raised `FormatException` — escaping `CompleteAsync`, escaping a stream *mid-enumeration* after
  content had reached the caller, and breaking the documented "never throws" contract of both claude readers.
  It now reads as 0 and the call completes. A budget or telemetry line under-counts by that field instead:
  deliberate, since a usage count is telemetry and losing one beats failing an answer already paid for.
- **`ClaudeAgentSession` ends a turn once.** A transcript with two terminal `result` lines ended the session
  twice, and `RunAsync`'s last-one-wins fold turned a completed `Ok` turn followed by a stray error into
  `Failed` with empty text. The first terminal now wins, matching the codex twin.
- **`IScorer.Applies` reaches an LLM judge's own predicate.** `((IScorer)judge).Applies(ctx)` returned true for
  every `LlmScorerBase` subclass regardless of its override, because the default interface implementation
  answered. It now returns the subclass's answer, and a *throwing* predicate propagates out of
  `EvaluateAsync` instead of being logged and skipped fail-open. **Persisted scores and token spend are
  unchanged** — `ScoreAsync` always re-checked the gate as its first line, so no judge ever spent a token it
  shouldn't have.
- **`InMemoryVectorStore` top-k is deterministic on tied scores.** Equal-scoring entries came back in
  hash-bucket order, which .NET randomizes per process — so the same store and query returned a different
  order run to run, and with more ties than `k` an arbitrary member was dropped. Ties now order by id.
  Strictly-better scores never move; the SQL-backed vector stores are unchanged, so the cross-backend contract
  remains "unspecified", now stated per backend.
- **A paused job can be cancelled.** `CancelAsync` returned false for a `Paused` job and left it paused, so the
  only route was resume-then-cancel — and a resumed job is Pending, i.e. claimable, so a runner polling that
  lane between the two calls could claim it and the cancel degraded to a cooperative flag on a now-running job.
  Paused → `Cancelled` now happens in one call on all three backends, never briefly claimable.
  `RequestCancelAsync` is unchanged and still refuses a paused job.
- **`Lyntai.Generation` (EXPERIMENTAL): a non-positive size hint falls back to the configured default.**
  `"0x0"`, `"512x0"` and friends were forwarded verbatim to the Automatic1111 WebUI, which errored — while the
  method's own documentation promised the fallback and the sibling local-engine parser already did it. A
  caller relying on a non-positive size to *fail* a render now gets a 512x512 image. A usable hint still
  reaches the WebUI unclamped.

### Changed — the rest of the backlog sweep (2026-08-05)

Same the deferred-SemVer-strictness rule disclosure rule: each names what a caller observes.

- **A media run where nothing substantive failed now reports what the backends actually said.** Before, if
  every candidate returned a blameless verdict, `GenerationRouter.GenerateAsync` returned a synthetic
  `NotConfigured` / "every capable backend reported it is not configured" — inaccurate for a run in which
  every candidate actually said `Unsupported`, and it discarded the backends' own words. Now the first
  blameless result carrying a reason is returned verbatim, with its own verdict. **A real failure still always
  wins** — that guard is untouched. Affects anyone switching on the router's verdict or displaying its detail,
  notably the generation agent tools and the durable-render job's failure message.
- **`ContextWindowExceeded` from a media backend no longer benches it.** It now translates to
  `GenerationVerdict.Unsupported` (Advance) rather than `Failed` (PenalizeAndAdvance), so a few oversized
  prompts in a row stop counting toward the dead-host threshold and stop routing unrelated later requests
  away from a perfectly healthy backend. Nothing is lost from the report: "your prompt is too long" is still
  the run's stated reason, now carried by the blameless slot above. **These two are one change in two halves**
  — the mapping is only safe because the reporting slot landed first.
- **A curated-memory update that would collide is refused.** Re-categorising an entry onto a
  `(kind, content, taskKey, scope)` another entry already holds used to succeed and silently mint the
  duplicate that `AddAsync(dedup: true)` promises cannot exist — after which dedup kept returning the *other*
  row's id. It now returns `false` and writes nothing. `false` already meant "no row updated"; it now also
  means "refused", and `GetAsync(id)` distinguishes them. Best-effort under concurrent writers, like
  `AddAsync`'s own dedup check.
- **`AsChatClient()` throws `LlmVerdictException`** (below) instead of a bare `InvalidOperationException`. It
  **derives from** `InvalidOperationException` and the message text is byte-identical, so `catch` clauses and
  message parsing both keep working. **One residual break, disclosed rather than discovered:** an exact-type
  check (`ex.GetType() == typeof(InvalidOperationException)`) no longer matches.
- **`CodexAgentSession` honours `ResumeToken`.** It previously refused with a single
  `SessionEnded(Unsupported)` and no spawn, because guessing codex's resume shape would have been read as a
  prompt and billed a turn. The shape is now measured against the real CLI
  (`codex exec resume <SESSION_ID> … -`), so a multi-turn agent conversation survives on both CLI backends.

### Added — small surface, from the same pass (2026-08-05)

- **`Lyntai.Llm.LlmVerdictException`** — carries the `LlmVerdict` on the seams that must throw rather than
  return an `LlmReply`. It lives in Core beside the taxonomy it carries, not in the bridge that first needed
  it, so the next seam that has to throw does not mint a second near-identical type. `LlmVerdict.NotConfigured`
  exists so a host can offer *setup* instead of reporting an error; through the `Microsoft.Extensions.AI`
  bridge that was previously recoverable only by string-parsing an exception message.



- **`ChatResult.Usage`** — `ChatOrchestrator` was discarding `ToolLoopResult.Usage` because the result type had
  nowhere to put it, so a two-gate chat could not be metered.
- **`StructureScorer.FormatKey`** — the `ScoreContext.Extra` key the scorer reads, published as a constant the
  way `OutcomeScorer.ErrorKey` already was, so a caller stops hardcoding `"format"`. Same value; no behaviour
  change.
- **`JobSpec.DefaultMaxAttempts`** — the retry default was the literal `3` hand-copied into all three job
  stores, free to drift.
- **`FalQueueOptions.CancelSegment`** (EXPERIMENTAL package) — the one URL segment that was a hardcoded literal
  while its siblings were settable. Default `"cancel"`, so every existing host calls the identical endpoint.
  It matters because cancel is the call that stops a render already costing money, on a backend whose wire
  format is documented-not-measured.

### Documentation (2026-08-05)

No API changed. These were all sentences a consumer or a maintainer would have acted on:

- **The MCP tool host is documented as what it is.** Four shipped XML docs and several README passages still
  said it runs on Kestrel/ASP.NET Core; it has run on `System.Net.HttpListener` since 2.0.1, and the README
  contradicted itself within one page.
- **Sibling application names are gone from the shipped XML docs** — `IPromptComposer` described "the Sonora
  pattern", and two other types named siblings, all of which shipped in the NuGet `.xml` and appeared in
  consumer IntelliSense. Each now states the pattern instead of naming a stranger's app.
- **`LlmVerdict.Unsupported` appears in the taxonomy lists a consumer reads.** It surfaces with no fallback
  and no host penalty, and was missing from `ILlmRouter`'s summary, `LlmVerdict`'s own list, and the internal
  routing table that is meant to be kept in sync with them.
- **The cross-backend memory-recall guarantee is stated correctly.** `IMemoryStore.RecallAsync` and
  `ICuratedMemoryStore.SearchAsync` asserted a guarantee their own next sentence contradicted; it holds for a
  **single-token** query, and a multi-token query is per-token on SQLite and contiguous-substring elsewhere.
- **`IGenerationStreamProvider` says that nothing implements it.** The seam is designed, not exercised: no
  backend implements it and no router path consumes it, so a backend advertising streaming delivery is
  unreachable. Its chunk shape is modelled on the LLM contract rather than measured against a real TTS wire
  format, and the first real backend may reshape it.
- Also corrected: `Automatic1111Provider` renders with whatever checkpoint the WebUI has loaded and does not
  honour a pinned model; the SQLite migration runner's description of its own tag dispatch; `GenerationRouter`
  documenting that the per-verdict policy governs the inline path only; the streamed CLI path having no
  absolute backstop; and a `Paused` job not being cancellable.

### Tooling (2026-08-05)

- **`new-migration` scaffolds the `[Tags(...)]` attribute** — with a placeholder that deliberately does not
  compile until the feature is named. An **untagged** migration is run by FluentMigrator under *every* feature
  set, so a domain the app disabled would still land its table and nothing would report it. A reflection test
  now asserts every migration carries its feature tag plus `StorageFeatures.AllTag`.
- **Nine live-integration tests stopped reporting PASS while asserting nothing** — their documented skip
  mechanism was never wired, so they ran and passed with no endpoint. They now skip honestly. A `SmokeTests`
  class whose entire body was `Assert.True(true)` under the name `SolutionBuilds` was removed; the build gate
  proves that claim.
- A process-wide catch-all error matcher registered by one test could answer for any concurrently running
  test that classified an error — a rare cross-collection flake, now narrowed to a per-test sentinel.

## 2.2.0 — 2026-08-05

### Added
- **`CodexAgentSession` — the agent-session shape is no longer claude-only.** `AddCodexCliAgentSession()`
  registers an `IAgentSession` that drives `codex exec --json` and streams the agent's **tool steps**, so an
  app that shows tool activity can offer both CLI backends through one shape instead of hand-parsing codex's
  JSONL. Both `Add*CliAgentSession` extensions now also register **keyed** by provider id
  (`GetRequiredKeyedService<IAgentSession>("codex-cli")` / `"claude-cli"`), so registering both no longer
  makes the unkeyed resolve depend on registration order.
  **Read the honesty note before adopting** (`docs/DECISIONS.md` — the honest-subset agent-session call): the message/usage/terminal half of
  the mapping is MEASURED against codex-cli 0.146.0, the **tool-step half is INFERRED** — the measured run
  used no tools. It is written shape-driven, which bounds the cost of a wrong guess to two things: **no
  payload is invented or dropped** (a tool step carries codex's own item-type name and its raw item object,
  nothing renamed or normalised) and **every uncertainty stays in the tool-step half**. It does NOT guarantee
  the right KIND of event — the tool arm is reached by elimination against three names, so a non-tool item we
  don't recognise arrives as a fabricated `ToolCall`. Treat a tool step's **kind as provisional, its payload
  as reliable**, and switch on `ToolCall.Name`. Not
  emitted, because codex has no analogue: `UsageLive`, `SessionEnded.Subtype`, `UsageFinal.Model`, and
  token-level text deltas (a `TextDelta` is one whole assistant message). `ResumeToken` is **refused** with
  `LlmVerdict.Unsupported` and no spawn rather than guessed — `codex [OPTIONS] [PROMPT]` reads an unrecognized
  subcommand as a prompt, so a wrong guess would silently spend a turn; `DisallowedTools` is logged as
  unhonoured (codex's gate is the sandbox); `SystemPrompt` travels as a leading block of the prompt.
  Internally both codex paths now build argv from one source, so `--skip-git-repo-check` — the flag whose
  absence works in a dev git repo and breaks in a shipped bundle — cannot go missing from one of them.
- **Provider lifetime as a library seam (`Lyntai.Lifecycle`)** — for the app whose backend configuration is
  owned **outside** the deployment (an end user, or a store the process polls), where several configurations
  of one backend are live at once and any of them can change mid-render. `IProviderPool<TProvider>` takes a
  `ProviderKey` and a factory and hands back the instance for that configuration; `BoundedProviderPool` (the
  default, LRU + idle bounds) reuses while the key is unchanged, `TransientProviderPool` never reuses, and the
  interface is the BYO seam for anything else. **Which one is registered is the only thing that decides
  reuse** — `b.UseProviderPool()` / `b.UseTransientProviders()` at startup, with no edit at any call site.
  A replaced entry is **retired, never disposed**: in-flight calls hold their own reference and finish
  normally, because without leases a pool cannot know when the last of them is done. See
  `docs/DECISIONS.md` — the configuration-keyed provider lifetime.
- **`ProviderKey` and its builder** — the pool's connection string, and the unit reuse, cooldown and
  admission are all keyed on. Every contribution is **named** (`ProviderKey.For(id).With("baseUrl", …)
  .WithSecret("apiKey", …).Build()`), so a forgotten member is visible in review rather than inferred;
  `WithSecret` folds in a digest and never retains the value, and the key's string form carries no secret
  material.
- **Router factories — `IGenerationRouterFactory` and `ILlmRouterFactory`** — one injected thing that
  composes a fully-governed router over the provider set a **caller** chooses, resolving each registration
  through the pool. The long-lived bookkeeping (dead-host tracker, rate limiter, usage ledger, admission
  table) stays a shared singleton across every router they hand out, which is precisely what per-call
  hand-construction throws away: a consumer that rebuilds its tracker with its router can never bench a
  failing backend. Both are registered by `AddLyntai`; the container-composed path is unchanged.
- **Cooldown and admission keyed on the CONFIGURATION, not the provider id.** Both routers take an optional
  `Func<TProvider, ProviderKey?> configuration` delegate (the factories bind it to `pool.TryGetKey`). Without
  it, behaviour is exactly as before — `p => p.Id`. With it, one tenant exhausting its quota no longer benches
  every other tenant sharing that backend, while two consumers of the same downed self-hosted host do share a
  bench.
- **`IProviderAdmission` / `ProviderAdmission` / `b.ConfigureProviderAdmission(…)`** — bounds how many calls
  may run against one configuration at a time, for a locally-run engine where simultaneous renders contend
  for a CPU or GPU. The shipped table bounds one PROCESS; the interface is the seam for a host that has to
  bound a shared engine across several (a distributed lock or lease service behind the same
  `EnterAsync`) — the routers and both factories take the interface, so nothing above it changes.
  Limits are declared per **slot** and enforced per **key**. Applied by the routers rather than by a decorator
  around a provider, because a wrapper implementing only the base seam erases the optional capability
  interfaces the generation router type-tests — which would silently stop every queued render from routing.
  Completion paths only: streams are deliberately not gated, since a stream would hold its permit for the
  whole response.
- **`IProviderIdentity`** — the `string Id { get; }` both `ILlmProvider` and `IGenerationProvider` already
  declared, now a shared base interface so the pool can be one generic type over either seam. **Both
  interfaces keep their own `Id` declaration** (as `new`), so this is binary-compatible for CALLERS as well
  as for implementors: adding a base interface is safe, but removing the member from the derived interface
  would throw `MissingMethodException` in every pre-compiled consumer that reads `provider.Id`, since member
  resolution does not walk base interfaces. The only caveat is source-level and rare — a consumer that
  implemented `Id` *explicitly* (`string ILlmProvider.Id => …`) must now also implement
  `IProviderIdentity.Id`; implicit implementation is unaffected.
- **Call-site verdict predicates — `verdict.IsOk()` and `verdict.IsTransient()`** (`LlmVerdictExtensions`).
  They hang off `LlmVerdict` itself, not off `LlmReply`, so the five released types that carry a verdict
  (`LlmReply`, `LlmChunk`, `SessionEnded`, `AgentSessionResult`, `ToolLoopResult`) share one definition.
  Deliberately **categories, not one method per member**: the enum grows (`NotConfigured` was appended after
  the 1.0 freeze), so an `IsRateLimited`/`IsRefused`/… set would make every future verdict a public-surface
  addition and leave the newest one as the only member without a helper — while `verdict == LlmVerdict.RateLimited`
  already expresses a single member perfectly. `IsTransient()` answers "may the SAME request succeed later?"
  — true for `Failed`/`Timeout`/`RateLimited`, false for everything terminal as sent (and for an unknown
  value, so an unrecognized verdict can never provoke a retry loop). **Known over-report, documented on the
  method:** `Failed` is also the classifier's catch-all, so an unrecognized PERMANENT error (a 400 whose body
  matches no pattern) reads transient. Kept deliberately — `RoutingPolicy.Retry` already re-sends to the same
  candidate only for `Failed`/`Timeout`, and a call-site predicate contradicting the router's own retry rule
  would be worse. Treat it as "worth one BOUNDED attempt", not as a licence to loop. See
  `docs/DECISIONS.md` — the category-predicate rule.
- **`CuratedMemory.MetadataValue(key)`** — the null-safe read of the curated-memory metadata map, which is
  `null` on every entry written without any, so `entry.Metadata!["source"]` both throws on a missing key and
  NREs on the common case. Generic on purpose: CMEM6 retired the purpose-built `Source`/`Title` columns into
  one arbitrary map so a new payload field needs no schema or API change, and a typed accessor per key would
  re-privilege the same handful of names one layer up.
- **`AddMcpTools(params ITool[])`** — an inline overload beside the existing sequence one, for a hand-picked
  subset of a server's toolset or a BYO `ITool` alongside them. The sequence overload remains the one
  `McpToolset.FromClientAsync`'s result flows into, and its documentation now states the two-step
  connect-then-adapt shape as intentional: connecting an MCP client is async and its lifetime outlives
  registration, so the app owns the client and hands Lyntai only the adapted tools.
- **Member and type documentation** on surface the 1.0 review found bare: `ExtensionsAiProvider`'s
  constructor parameters and its `IsAvailable`/`SupportsToolCalls` (why both are unconditionally true),
  `LyntaiChatClientExtensions` (which direction of the MEAI bridge it is), `McpBuilderExtensions`, and
  `ClaudeCliProvider.ProviderId`.
- **`MigrationRunnerService.MigrateUpAsync(…, CancellationToken)`** on both storage backends — the awaitable
  twin an app owning its schema (`SchemaMigration.None`) calls from an async startup path, instead of a
  `GetAwaiter().GetResult()`. **Read what it promises before reaching for it**, because FluentMigrator's
  runner is synchronous and takes no token: the migration runs **inline on the calling thread** (no
  `Task.Run` — that would occupy a pool thread for the whole migration *and* still be uncancellable), and
  the token is honoured at the only two points that exist — **before any work** (a cancelled token leaves
  the SQLite file uncreated / never dials the Postgres connection string) and **between feature passes**.
  Under the default `StorageFeature.All` there is exactly one pass, so there it means "before starting"
  only. A pass in flight cannot be cancelled. See `docs/DECISIONS.md` — the honest `MigrateUpAsync` scope.
- **`b.AddSemanticMemory(…)`** — the wiring seam for semantic recall, so an app enabling it composes with
  builder calls instead of hand-constructing a vector store, a connection factory and an embedder. Overloads
  mirror `AddEmbeddings` (instance / factory / by type), plus a no-argument one for when the embedder arrives
  from elsewhere (`AddOpenAiCompatibleEmbedder`, or a host registration made before `AddLyntai`). Its real
  value is that it **states the intent**: semantic memory was previously enabled purely as a side effect of
  an `IEmbedder` being registered, so forgetting one registered no `ISemanticMemory` at all and every recall
  path skipped it in silence. `AddLyntai` now throws at composition instead. Everything stays substitutable —
  the vector store and `ISemanticMemory` are still `TryAdd`-registered, so a host's own registration wins,
  and the concrete stores stay public for the hand-wired path. Persist the vectors with the existing
  `UseSqliteVectorStore()` / `UsePostgresVectorStore()`; `UseSqliteVectorStore`'s documentation now names the
  non-obvious prerequisite that `lyntai_vector` ships under `StorageFeature.Governance`. See
  `docs/DECISIONS.md` — the named semantic-memory registration.

### Fixed
- **A capability gap no longer benches a healthy media backend.** `GenerationVerdictClassifier` translated
  `LlmVerdict.Unsupported` — "this backend cannot do THIS request" — into `GenerationVerdict.Failed`, even
  though `GenerationVerdict.Unsupported` exists and means the same thing. Since `GenerationRoutingPolicy` maps
  `Failed` to `PenalizeAndAdvance` and `Unsupported` to `Advance`, a capability gap counted toward the
  dead-host threshold, and a few of them in a row put a perfectly healthy backend on cooldown. **Who is
  affected:** anyone whose media backend reports a capability gap through the shared corpus — a
  consumer-registered `LlmVerdictClassifier.AddErrorTextMatcher` returning `Unsupported`, or an exception that
  classifies into it. **What you observe:** such a result now carries `GenerationVerdict.Unsupported` instead
  of `GenerationVerdict.Failed`, so routing advances without blame — and, consistently with every other
  blameless verdict, `GenerationRouter` no longer reports it as the run's failure reason when a real failure
  also occurred. A `switch` on `GenerationVerdict.Failed` that was catching these needs an `Unsupported` arm.
  **Read this before choosing the version to release it in.** This is a `Lyntai.Core` type, so it carries the
  full SemVer promise rather than the `Lyntai.Generation` experimental carve-out — and it is precisely the shape
  `docs/DECISIONS.md` — the deferred-SemVer-strictness rule declines to license in a minor: *"Does NOT: silent behavior changes … or anything
  a consumer can't detect at compile time. Those stay major-bump material regardless."* No API member changed,
  so nothing here breaks a build; a consumer's `switch` on `GenerationVerdict.Failed` keeps compiling and simply
  stops matching these results. **Treat it as major-bump material** — it is recorded under `## Unreleased`,
  which fixes no version, so whoever cuts the release makes that call deliberately.
  `LlmVerdict.ContextWindowExceeded` still collapses to `Failed`, now deliberately and with its reason written
  down — **at a stated price**: `Failed` means `PenalizeAndAdvance`, so repeated oversized prompts can still
  bench a healthy backend, and the LLM domain maps that verdict to `Advance`, so the two now disagree about it.
  Keeping it reportable was judged worth that, because the alternative silently loses "your prompt is too long"
  — the one message a caller can act on. The remedy needs a router change and is filed as `TASKS.md` Part 40.
  The catch-all that hid all this is gone: every `LlmVerdict` member has its own arm, and a test fails until a
  newly added one has both a translation and an arm. See `docs/DECISIONS.md` — the one-arm-per-verdict translation rule.
- **The HTTP generation backends now have the per-call deadline their infinite `HttpClient` timeout was already
  resting on** (`TASKS.md` GEN11). 2.1.0's `Add*` shims register a client with `Timeout.InfiniteTimeSpan`
  because a render routinely outlives the 100-second default — but no deadline existed to take over:
  `GenerationRequest.TimeoutSeconds` was on the contract and **read by nothing**, so a backend that accepted the
  connection and then stalled hung until the caller's token fired, and a background render with no cancel waited
  forever. Unbounded and silent is worse than the cut-off it replaced. Each of `OpenAiImageOptions`,
  `Automatic1111Options`, `ComfyUiOptions` and `FalQueueOptions` now carries a **`Timeout`** — 10 minutes for the
  inline render backends, 2 minutes for the queue ones — overridden per call by `GenerationRequest.TimeoutSeconds`
  where a request exists, and opted out of with `Timeout.InfiniteTimeSpan`. A fired deadline is a
  **`GenerationVerdict.Timeout` result, not a throw** (these backends are contractually fail-safe), while the
  caller's own cancellation still propagates as `OperationCanceledException` — the two are told apart by the
  caller's token, the same discriminator `OpenAiCompatibleProvider` uses on the LLM side. A BYO client's own
  `HttpClient.Timeout` now also surfaces as that verdict instead of escaping as `TaskCanceledException`.
- **What a deadline means for the queue backends is now stated rather than assumed.** For `FalQueueProvider` and
  `ComfyUiProvider` it bounds **one HTTP call** — submit, status, fetch, cancel — never the render, which
  outlives every individual call and is polled across job re-dispatches and process restarts by
  `GenerationRenderJobHandler`; bounding the whole operation is the job's retry budget to do, not the provider's.
  Consequently a timed-out **status poll reports the operation as still `Running`**: no answer is not a failed
  render, and reading it as terminal would abandon a submitted (and billed) generation. A timed-out **submit**
  fails with a detail saying the request may still have been enqueued.
- Probes (`OpenAiImageProvider`, `Automatic1111Provider`, `ComfyUiProvider`) are bounded by the same option — with
  the shim's infinite client they could otherwise stall indefinitely against a host that accepts connections.
- **A submission that gets no answer no longer causes a second, paid submission elsewhere.** `GenerationOperation`
  gained an additive **`Inconclusive`** flag for the one case the `Failed` status cannot express: *the backend
  never answered, so nobody knows whether it took the work*. `GenerationRouter.SubmitAsync` now **surfaces** such a
  submission — carrying the provider id, so the caller learns who might hold it — instead of advancing to the next
  candidate, which would buy the same render twice; and it does not count it against dead-host cooldown, because
  no answer is no evidence of ill health. The status stays `Failed`, so every existing status check behaves
  exactly as before. `GenerationRenderJobHandler` fails such a job with the backend **named** and says it is
  deliberately not retried, since a duplicate submission is a duplicate charge. This is the same reasoning that
  makes a timed-out poll report `Running`, applied to the call that commits the money.
- **The agent tool no longer invites the retry the router just refused.** `generate_submit` reported a failure
  with the operation detail alone, dropping the `ProviderId` — and a model's default reaction to a tool error is
  to call the tool again, which re-submits work a backend may already be billing for. That path has no human in
  it, so it is the one that mattered most. An inconclusive submission now returns an observation that *instructs*:
  it names the backend, says plainly not to retry and why, points at `generate_status` as the alternative, and
  carries an `inconclusive: true` flag so a host can branch without parsing prose.
- Telemetry distinguishes the two: `RecordSubmission` tags an inconclusive submit `error.type = "Inconclusive"`
  (plus `lyntai.generation.inconclusive` on the span) rather than lumping it in with `"Failed"`. Same status, but
  not the same incident — an operator investigating a possible double charge needs something to search on.

### Changed
- **A Governance-backed `Use*` helper now fails at WIRING time when `StorageFeature.Governance` is toggled
  off**, instead of registering a store over a table that was never created. `lyntai_response_cache`,
  `lyntai_usage` and `lyntai_vector` all ship in the one Governance migration, so
  `UseSqliteResponseCache` / `UseSqliteUsageTracking` / `UseSqliteVectorStore` (and the two Postgres
  equivalents) were the only calls that could break `Use*Storage`'s own stated rule — a disabled domain is
  simply unresolvable, and that unresolvability *is* the startup signal. **What you observe:** if you call
  `UseSqliteStorage(path, StorageFeature.Memory)` and then `UseSqliteVectorStore()`, you now get an
  `InvalidOperationException` at `AddLyntai` naming the offending call and the feature to add, where you
  previously got a container that built cleanly and threw "no such table: lyntai_vector" at the first
  recall. The check is order-independent (either call may come first) and fires only on a configuration
  that was already broken — the default `StorageFeature.All` includes Governance, so existing wiring is
  untouched. It also applies **only where Lyntai owns the schema**: under `SchemaMigration.None` or an
  app-supplied `IDbConnectionFactory` no migration was going to run, the feature set never decided which
  tables exist, and adding `StorageFeature.Governance` would create nothing — so those wirings are left
  alone. `UsePostgresVectorStore` is deliberately exempt too: `PostgresVectorStore` creates its own schema
  lazily, so it works with or without the Governance migration.
  - **Two ordering consequences, now written down** — clarifications of what the guard already does, not
    behaviour changes. (1) The check is evaluated **eagerly**, at each `Use*` call, against the last feature
    selection registered *so far*, so `UseSqliteStorage(a, Memory)` → `UseSqliteVectorStore()` →
    `UseSqliteStorage(b, All)` throws at the middle call even though the FINAL selection is valid — state
    the feature set once, or widen it before the helper. (2) A **BYO-factory call made last stands the guard
    down for the whole wiring**: `UseSqliteStorage(path, …, SchemaMigration.OnStartup)` followed by
    `UseSqliteStorage(factory, …)` skips the check entirely, because the app supplied the factory last and
    therefore owns the schema. Both follow one rule — the guard follows the selection, and the last selection
    decides — but each is reachable by reordering two lines you thought were independent.
- **`AddClaudeCliAgentSession` now takes `environment`, so a portable install's `CLAUDE_CONFIG_DIR` reaches
  agent turns too.** `AddClaudeCliProvider`, `AddCodexCliProvider` and `AddCodexCliAgentSession` all already
  had it; the Claude agent session was the only one of the four without, and its sibling's docs instruct a
  host to "pass the same value to both". **What you observe:** a host that did exactly that had the variable
  honoured for completions and **silently dropped** for agent turns — so the portable CLI read and mutated
  the machine-wide install's state, which is the one thing a portable install exists to avoid. Added as a
  trailing optional parameter on both `AddClaudeCliAgentSession` and the `ClaudeAgentSession` constructor:
  source-compatible (existing calls compile and behave identically), but **not binary-compatible** for a
  pre-compiled caller of the old signature, exactly as with the router constructor parameters below.
- **`BoundedProviderPool`'s per-call idle sweep no longer allocates.** `ProviderPoolOptions.IdleTimeout` is
  set by default, so the sweep ran inside the pool's lock on every `GetOrAdd` and materialised a list whether
  or not anything was actually idle; it now scans over the dictionary's struct enumerator and allocates only
  when there is something to evict. LRU overflow eviction likewise finds its victim by a linear minimum scan
  rather than sorting every entry to take one of them. Behaviour — including which entry is evicted — is
  identical.
- **An unconfigured LLM backend is skipped, not benched — `LlmVerdict.NotConfigured`.** When an
  OpenAI-compatible endpoint answers 401/403 to a call that carried **no** credentials,
  `OpenAiCompatibleProvider` now reports the new `LlmVerdict.NotConfigured` instead of `AuthFailed`, and the
  default `RoutingPolicy` maps it to `FallbackAction.Advance`. **What you observe:** a candidate you listed
  but never configured is skipped with no cooldown and no dead-host penalty, where it previously benched that
  provider for the whole cooldown window on every first attempt; when everything is unconfigured, the
  surfaced verdict now says so, which a host can turn into a setup prompt. A key that WAS supplied and got
  rejected still reports `AuthFailed` and still cools the host — the two cases were indistinguishable before.
  It is deliberately not "a key is required": a locally-run OpenAI-compatible server (LM Studio, vLLM,
  Ollama) legitimately needs none, so only the server *demanding* one makes a missing key a configuration
  gap. This closes the asymmetry with the generation domain, which already drew the same distinction
  (`GenerationVerdict.NotConfigured`, 2.0.1); both domains now answer the same situation the same way.
  Also exposed as `LlmVerdictClassifier.FromHttpFailure(status, body, hasCredentials)` for a custom provider,
  and `GenerationVerdictClassifier` no longer flattens a `NotConfigured` from the shared corpus to `Failed`.
  - **If you switch on `LlmVerdict`:** the enum gained a member (appended last, so existing members keep
    their numeric values — binary-compatible, and a compiled consumer keeps working). A **non-exhaustive
    `switch` expression** over `LlmVerdict` will now raise **CS8509** in your build — a warning, not an
    error. Code that treated the old `AuthFailed` as "check your API key" should handle `NotConfigured` as
    "no API key is set" rather than falling into its default branch.
  - **A blameless verdict no longer masks a real failure in the reported reply.** Introducing a verdict that
    advances without blame exposed a second-order bug: `LlmRouter` remembered the last failure
    unconditionally, so for candidates `[a host that is down → Failed, one you never configured →
    NotConfigured]` you were told **"not configured"** and sent to set up a key while the backend you *had*
    configured was the actual problem. `NotConfigured` and `Unsupported` are now remembered separately on
    both the streaming and non-streaming paths and reported only when there was no real failure at all —
    the guard `GenerationRouter` already had. **Unchanged:** which substantive failure wins (still the last
    one attempted), and `ContextWindowExceeded` still surfaces normally — "your prompt is too big" is a real
    answer. When every candidate is unconfigured you still get `NotConfigured`, not a generic error.
  - **`HttpEmbedder` deliberately unchanged in behaviour:** an embedding call has no verdict and no
    fallback — it throws — so there is nothing for it to route around. Its 401 message now says
    `(not configured: no ApiKey)` when no key was supplied, so a host can tell setup from a rejected key.
    Message only; the exception type is still `HttpRequestException`.
- `LlmRouter` and `GenerationRouter` gained two **optional** trailing constructor parameters (`configuration`,
  `admission`). Source-compatible — existing constructions compile and behave identically — but **not binary
  compatible**: a pre-compiled consumer calling the old constructor gets `MissingMethodException` until it
  recompiles against this version. Nothing else changes for an app that configures its backends at startup;
  the routers `AddLyntai` builds still pass neither.

## 2.1.0 — 2026-08-04

### Added
- **Named factories for `GenerationInput`** — `GenerationInput.Init(bytes, "image/png")`, `.FirstFrame(…)`,
  `.Reference(…)`, `.Voice(…)`, and `.From(role, …)` for a role a backend documents itself, each with a
  `System.Uri` overload. The positional constructor is a **silent-misbinding trap**: three of its four slots are
  strings and `Role` is last, so `new GenerationInput(GenerationInputRoles.Init, bytes, "image/png")` compiles,
  binds `"init"` to the media type and leaves `Role` null — an img2img request then degrades to text-to-image
  with **no error anywhere**, the source image simply ignored. The factories make the role impossible to omit.
  Purely additive; the constructor is unchanged. See `docs/DECISIONS.md` — the named-factories rule, which also records why
  `GenerationArtifact` was checked and deliberately left alone.
- **Per-backend `Add*` shims for `Lyntai.Generation`** — `AddOpenAiImageProvider`, `AddAutomatic1111Provider`,
  `AddComfyUiProvider`, `AddFalProvider` and `AddLocalDiffusionProvider`, the media counterpart of
  `AddOpenAiProvider()` / `AddOllamaProvider()`. Every backend previously had to be hand-constructed with its own
  `Func<HttpClient>`. BYO stays optional on all of them; omit it and Lyntai registers a named client with an
  **infinite** `HttpClient` timeout so the per-call deadline owns cancellation (a render routinely outlives the
  100-second default). `GenerationProviderBuilderExtensions.HttpClientName(id)` is public so a host can decorate
  the same client without abandoning the shim.

### Fixed
- **A BYO `HttpClient` handed to a generation backend is no longer disposed by Lyntai.** The backends did
  `using var http = httpFactory()`, so the natural BYO lambda `_ => _myClient` worked for the first render and
  threw `ObjectDisposedException` on the **second**. BYO now means what it means on the LLM side: the host owns
  the lifetime. See `docs/DECISIONS.md` — the BYO-lifetime rule.

### Changed — `Lyntai.Generation` only (EXPERIMENTAL; source-compatible)
- `OpenAiImageProvider`, `Automatic1111Provider`, `ComfyUiProvider` and `FalQueueProvider` take a third
  `bool disposeHttpClient = true` constructor parameter. Defaulted to the previous behaviour, so existing
  hand-constructions compile and behave identically; it is **binary**-breaking for a pre-compiled caller, which
  the `Lyntai.Generation.*` experimental carve-out permits.
- `Lyntai.Generation` now depends on `Microsoft.Extensions.Http` (managed, on the runtime's own version band).
  The package is not in the `Lyntai` bundle, so the one-line install's closure is unchanged.

### Tooling
- **`consumer-smoke` was testing STALE packages** and now isn't. It packs under a fixed throwaway version, and
  NuGet never re-extracts a version already in the global cache — so after the first run ever, every later run
  restored that run's copies and reported success about code it had never compiled against. It now evicts
  `<global-packages>/lyntai.*/<version>` after packing; the first eviction removed **9** stale packages. The
  smoke app also now references `Lyntai.Generation` — the package a consumer is most likely to meet on its own,
  and the only one carrying a dependency the bundle never resolves.

## 2.0.1 — the generation platform + a coherent package graph (2026-08-04)

### Breaking — package graph only; **no namespace, type or API changed**
Every move below is a one-line `PackageReference` edit. No `using`, type name or `Add*` extension changes,
because the restructure was designed around keeping namespaces fixed.

| Was | Now |
|---|---|
| `Lyntai.Providers.ClaudeCli` / `.CodexCli` / `.OpenAiCompatible` | `Lyntai.Providers.Default` |
| `Lyntai.Generation` | `Lyntai.Core` (drop the reference — you already have Core) |
| `Lyntai.Generation.Http` | `Lyntai.Providers.Default` |
| a typical app's whole set | **`Lyntai`** (new metapackage) |

- **`Lyntai.Generation` and `Lyntai.Generation.Http` are gone as packages** — folded into `Lyntai.Core` and
  `Lyntai.Providers.Default` respectively. Both had **zero** external dependencies, so their boundaries
  isolated nothing (`docs/DECISIONS.md` — the dependency-footprint package split). Verified as an exact union: the merged API baselines gained
  precisely the lines the deleted ones held, with zero removals, and all tests pass unedited.
- **New `Lyntai` metapackage** — `dotnet add package Lyntai` gets Core + the dependency-free default backends
  + in-memory storage. It ships no assembly. Anything costly stays an explicit opt-in: `Storage.Sqlite` (native
  binary), `Providers.Local` (LLamaSharp), `Tools.Mcp*` (MEAI/ASP.NET Core), `Secrets.Dpapi` (Windows-only).
  This is deliberately how "most consumers use X" is served — **not** by adding X's dependencies to the
  mandatory package.
- **`Lyntai.Providers.ClaudeCli`, `Lyntai.Providers.CodexCli` and `Lyntai.Providers.OpenAiCompatible` are
  merged into `Lyntai.Providers.Default`.** Packages are now split by **dependency footprint, not by vendor**
  (`docs/DECISIONS.md` — the dependency-footprint package split): those three need nothing beyond Core and managed `Microsoft.Extensions.Http`, so
  bundling them costs a consumer nothing and removes two ids plus their release ceremony. Everything that
  drags something stays separate — `Providers.Local` (LLamaSharp + native backend), `Storage.Sqlite` (native
  SQLite binary), `Tools.Mcp.Hosting` (ASP.NET Core), `Secrets.Dpapi` (Windows-only), `Providers.ExtensionsAi`
  (MEAI) — because a console app wanting the `claude` CLI must not acquire llama.cpp to get it.
  **Migration is one line:** replace those `PackageReference`s with `Lyntai.Providers.Default`. **No code
  changes** — the namespaces (`Lyntai.Providers.ClaudeCli`, `.CodexCli`, `.OpenAiCompatible`) are unchanged, so
  every `using`, type name and `Add*` extension still resolves. The three old ids stop receiving updates at
  1.2.2.

- **The `Lyntai` bundle now includes `Lyntai.Providers.ExtensionsAi`** — the MEAI bridge in both directions
  (`IChatClient` → provider, and `AsChatClient()` back) on the one-line install. It is free: MCP already pins
  `Microsoft.Extensions.AI.Abstractions`, so the bundle's third-party closure is unchanged at 15 packages (only a
  version unification), for 38 KB of managed code that trimming removes entirely.
- **NEW PACKAGE `Lyntai.Generation`** — the five media backends move out of `Lyntai.Providers.Default` into
  their own package, and their namespaces are corrected to `Lyntai.Generation.Providers`. The old names were
  wrong: `Lyntai.Generation.Http` contained `LocalDiffusionProvider`, a *subprocess* backend, whose own namespace
  `Lyntai.Generation.Local` read as the unrelated package `Lyntai.Providers.Local` (GGUF inference). The
  generation *contracts* stay in Core; this is the backend set, and it has **zero third-party dependencies**.
  `dotnet add package Lyntai.Generation` pulls Core with it. **It is deliberately NOT in the `Lyntai` bundle** —
  an experimental domain most consumers don't use should not arrive with the one-line install (the bundle dependency budget). Justified by
  a new axis (`docs/DECISIONS.md` — the release-cadence package split): a split for release CADENCE, since media is where the growth is and
  every new backend would otherwise churn the package every chat consumer installs — 10 of `Providers.Default`'s
  26 public types were media. Done at 2.0.1 precisely because generation has never shipped, so the namespace
  change had zero consumers to protect; after this release the same fix would cost a major bump.
- **`Lyntai.Generation.*` ships EXPERIMENTAL, exempt from the SemVer promise** until GEN-VERIFY closes. The
  platform is complete and tested, but two backends were written from vendor docs with no key to call, one's argv
  is ported rather than measured, and `IGenerationStreamProvider` has no implementation yet — so its shape is
  expected to change on contact with reality. Marking it costs nothing; freezing it would force either a major
  bump for a fix we already anticipate or a known-wrong API left in place. Every other domain carries the full
  promise.
- **`node devtools/dev.mjs consumer-smoke`** — a repeatable RELEASE gate that packs every package to a scratch
  feed and then restores, builds and runs a fresh console app against them. Everything else tests the repo via
  project references; this is the only check that exercises what actually ships (nuspecs, dependency groups,
  symbol packages, the bundle restore). Run by hand once before this release it found two defects nothing else
  could — an empty symbol package on the new bundle, and an unconfigured backend reporting the wrong verdict.
  Deliberately not in `verify`: it is minutes, not seconds.
- **`node devtools/dev.mjs new-package <Lyntai.X>`** — scaffolds an adapter package (csproj + the conventional
  `Add*` builder entry point, which doubles as the API-gate anchor type) and registers it in all seven mechanical
  registries; the API baseline seeds on the next test run. Bundle membership is deliberately not automatic — that
  is a budgeted decision under the bundle dependency budget. Adding the next package is now one command plus writing the adapter.
- **Many small packages is now the settled shape, with tooling to match (`docs/DECISIONS.md` — the many-small-packages shape)** — a package
  as small as `Lyntai.Secrets.Dpapi` (8 KB) earns its id, because the cost of a package is its DEPENDENCY, not its
  size: merging a Windows-only 8 KB adapter into anything larger makes that larger thing unusable off Windows. So
  granularity stays, and the growth is paid for in tooling: `node devtools/dev.mjs check-packages` (now in
  `verify`) treats the filesystem as the source of truth and fails unless every packable project is registered in
  all nine places that must know about it — `packableProjects`, the solution, `ApiSurfaceTests` (both the list and
  the anchor map), the test project's references, a baseline file, the `docs/AOT.md` table and the README table —
  plus the reverse, so a deleted package leaves no orphan baseline or stale entry. The miss that matters most is
  silent: no `ApiSurfaceTests` entry means the package ships with **no public-API gate at all**.
- **A bundle membership POLICY, enforced (`docs/DECISIONS.md` — the bundle dependency budget)** — with the package count set to keep growing,
  what belongs in the one-line install is now a written rule plus a gate rather than a per-package argument. A
  package joins only if it adds no third-party dependency outside the `Microsoft.Extensions.*` band, or if it is
  near-universal and the cost is accepted explicitly and recorded; never if it carries a native payload or a
  platform-specific API. `node devtools/dev.mjs check-bundle` (now in `verify`) reads the bundle's resolved
  dependency closure and fails when anything unapproved appears — or when the allowlist goes stale. Also settled:
  ONE bundle, never a family of curated subsets (they multiply combinatorially and every one is an id that can
  never be unpublished — the burned-2.0.0 call).
- **Renamed the project behind the bundle `Lyntai.Meta` → `Lyntai.Bundle`** (the published id is unchanged and
  stays `Lyntai`). "Metapackage" is NuGet's term, but a project name is read by people deciding where code goes.
- **An unconfigured image backend is skipped, not benched** — `OpenAiImageProvider` guarded `BaseUrl` but not
  `ApiKey`, so with no key it made a live call, got a 401 and reported `AuthFailed`, which (with the new GEN5
  cooldown) benched the backend for the cooldown window on every first attempt. It now reports `NotConfigured` —
  routing skips the candidate blamelessly and a host can offer setup. The distinction lives in Core as
  `GenerationVerdictClassifier.FromHttpFailure(status, body, hasCredentials)`: an auth failure with nothing to
  authenticate WITH is a configuration gap, while a REJECTED key stays `AuthFailed`. It is not simply "require a
  key", because an OpenAI-compatible endpoint run locally (LM Studio, vLLM, Ollama) legitimately has none — only
  the server *demanding* one makes a missing key a config problem.
- **Honest trim/AOT metadata in `Lyntai.Providers.Default`** — three generation backends (`OpenAiImageProvider`,
  `Automatic1111Provider`, `ComfyUiProvider`) built their request bodies by reflection-serializing anonymous
  types, in a package that stamps `IsTrimmable` into its assembly to tell a consumer's trimmer it is safe to
  trim. The claim was false: those calls break under trimming/AOT. They now build `JsonObject`s and serialize
  reflection-free, matching the chat payloads in the same package (`Payloads/OpenAiPayload`). **`verify` now
  fails on ANY warning in a published project** (`node devtools/dev.mjs check-warnings`), because a warning
  nobody fails on is how a shipped claim rots silently — this is what caught it.
- **`GuardedStream.ReadAll` honours `WithCancellation`** — the public async iterator's `CancellationToken`
  parameter lacked `[EnumeratorCancellation]`, so a consumer's `.WithCancellation(ct)` was silently dropped and
  a cancelled caller could receive a fabricated terminal instead of an `OperationCanceledException`. Affects an
  external provider author using the shared read-loop; Lyntai's own providers pass the token explicitly.
- **Governance + telemetry parity for generation (`AddGenerationUsageBudget()`, `AddGenerationRateLimit()`,
  cooldown by default)** — the generation domain now has the LLM front door's governance, REUSING that machinery
  rather than duplicating it: `DeadHostTracker` benches a backend that rate-limits or rejects a key (and counts
  repeated transient faults toward the threshold), `IUsageTracker` accounts spend, and the same token bucket
  throttles. Two boundaries are deliberate and tested. **Cooldown keys are domain-prefixed**, so a host whose
  chat provider and image backend share an id never has a chat outage bench its renders. **Throttling is
  configured separately** (`GenerationOptions.RateLimit`), because a render and a chat turn hit different
  vendors' limits and one shared bucket would let a render starve the chat that requested it. **Spend, by
  contrast, is shared on purpose** — renders record into the same tracker as chat, so "what has this app spent"
  stays one number; only COST caps bind a render (it spends no tokens and claims none). The cap is checked before
  a render *and* before a submission, since submitting is what commits the money for a hosted video whether or
  not anyone fetches it, and `GenerationRenderJobHandler` records what a finished durable render cost — the only
  place that still exists by then. New: `GenerationRequest.Consumer` (round-tripped through the durable-job
  payload, so a resumed render still bills to whoever asked), `GenerationRoutingPolicy.ExemptSoleCandidate` plus
  the `PenalizeAndAdvance`/`CooldownAndAdvance` actions, and a THIRD OTel source/meter `Lyntai.Generation`
  (per-attempt spans, `lyntai.generation.duration`/`.cost`/`.artifacts`) — kept out of the GenAI source because a
  render is not a `gen_ai.*` chat operation, while `gen_ai.system`/`gen_ai.request.model` carry over so spend can
  still be grouped by vendor across both domains. Agent-driven renders default to consumer `"agent"`, so
  `Budget.PerConsumer["agent"]` fences off the runaway-spend case without limiting what a user's own click may
  spend; a model cannot name its own consumer.
- **Generation as agent tools (`AddGenerationTools()`)** — the generation domain exposed as five `ITool`s, which
  is the **entire coupling** between the generation and LLM domains: neither references the other's concrete
  types, and because the LLM side already knows `ITool`, these work in the in-process tool loop *and* — with
  `AddMcpToolHost(...)` — for a CLI agent running its own loop over MCP. `generate_backends` lets a model
  discover what exists and what each backend supports before choosing; `generate` is the inline path; and
  `generate_submit` → `generate_status` → `generate_fetch` is the asynchronous path a video render actually
  needs. **Bytes never enter a tool observation** (a base64 image would blow the context window for nothing): if
  an `IGenerationArtifactSink` is registered the artifacts are delivered to it and the observation says so,
  otherwise it reports type/size/URI. Bad arguments come back as a readable error rather than a throw, so a model
  can correct itself, and unknown arguments pass through as backend options so a model can use a backend's own
  knobs without Lyntai enumerating them.
- **`FalQueueProvider`** (in `Lyntai.Providers.Default`) — the first remote video backend, over **fal.ai's
  queue API**: submit → poll → fetch, pairing with the durable render handler so one integration reaches the
  Wan/Kling/Veo-class models. The **operation id carries its model** (`"model#requestId"`), because the queue's
  status and result URLs need the model while a resumed job hands back only an operation id. A transport failure
  while polling reports **Running, not Failed** — a 500 says nothing about a render that is already paid for and
  probably still going — and an unknown status is likewise not terminal. Artifact reading is shape-tolerant
  (models return `video`/`images`/`audio`), and an unrecognised result is a failure rather than an empty success.
  Cost comes from the response, never inferred from a rate card. **Surface is documented, not measured** (no API
  key to call), so paths are options and the flag says so in the XML docs.
- **`LocalDiffusionProvider`** (in `Lyntai.Providers.Default`) — image generation on a locally-installed
  **stable-diffusion.cpp** (`sd-cli`): no key, no network, no content policy in the path. Inline delivery, since
  a local render blocks until the file exists. The engine and its weights stay the host's to provide (the backend self-maintenance boundary); the
  probe is free and exact (both files present). Two ported details that look incidental and are not: the spawn's
  working directory is the **binary's** directory, because the engine loads `ggml*.dll` from beside itself, and
  sizes are clamped to multiples of 64 within 256–768, which the engine requires and a CPU render makes
  advisable. Unlike the implementation this was ported from, the spawn goes through **`IProcessRunner`** — so it
  gains the BYO-runner seam, kill-the-tree cancellation, and an **inactivity clock with an absolute backstop**
  instead of one wall clock that would kill a healthy slow render. Argv and clamping are production-proven but
  **not measured here** (no engine on the dev machine), so they are pinned by exact-argv tests.
- **Durable renders (`GenerationRenderJobHandler`, `IGenerationArtifactSink`)** — an asynchronous generation
  run as a `Lyntai.Jobs` job, which is where the platform earns its keep over a thin HTTP client: the backend's
  operation id is **checkpointed before the first poll**, so a crash, deploy or restart resumes polling the
  render already in flight instead of submitting — and paying for — a second one. Progress is reported for a UI,
  each poll renews the lease, and a **lost lease stops the handler** (with the operation id in the error) rather
  than letting two workers drive one paid render. A submission no candidate accepts FAILS (a config problem a
  retry can't fix) while a still-working backend retries. Finished artifacts go to an app-implemented
  `IGenerationArtifactSink` — the platform routes and tracks the render; where bytes land stays the app's call
  (the backend self-maintenance boundary/the generation-as-its-own-platform decision). Composes with the existing job machinery (lanes, priorities, backoff, DLQ, cancellation) rather
  than reimplementing any of it, and the payload/checkpoint JSON is hand-written so Core keeps its AOT claim.

### Changed
- **`Lyntai.Tools.Mcp.Hosting` no longer requires ASP.NET Core.** The framework reference on
  `Microsoft.AspNetCore.App` is gone: the MCP protocol lives in `ModelContextProtocol.Core`
  (`StreamableHttpServerTransport` works on plain `Stream`s), and the ASP.NET package only supplied Kestrel
  routing glue. The per-call host now runs on `System.Net.HttpListener` — the right fit for a loopback-only
  ephemeral endpoint, needing no URL ACL or elevation for `127.0.0.1`. A console or desktop app no longer
  acquires the ASP.NET shared framework just to let a CLI agent call its tools. Public API unchanged; the
  existing MCP round-trip tests and the p3 e2e suite pass untouched (and the round-trip now completes in
  under a second).
- **MCP is in the `Lyntai` metapackage**, so a typical install gets both halves without Core acquiring the
  MCP SDK's pinned MEAI abstraction (`docs/DECISIONS.md` — the dependency-footprint package split).

### Added
- **`Lyntai.Generation`** — a NEW package: the generation **platform** (image · video · audio · 3d, and any
  kind you define — `Kind` is an open string, so a non-media artifact uses the same machinery and no `Custom`
  constant is needed). Fallback is a **policy**: `GenerationRoutingPolicy` defaults to the LLM router's §6
  semantics (a content `Refused` surfaces) but a host pairing a hosted backend with a permissive locally-run
  one can set `On(Refused, Advance)` — that is the host's call, not the library's. One capability-aware provider seam
  spanning image, video and audio (and any medium next — `Kind` is an open string, as 3D already ships on real
  aggregators), with three delivery modes because real backends genuinely differ: inline (`IGenerationProvider`),
  async job (`IGenerationJobProvider` — submit → poll → fetch, universal for video and batch music) and streaming
  (`IGenerationStreamProvider` — TTS starts playback before generation ends). Async operations expose their
  **operation id**, so a render survives a restart, composes with `Lyntai.Jobs`, and works with a
  webhook-delivering backend (your app owns the endpoint and calls `FetchAsync`). Backends declare
  `GenerationCapabilities` and the router **pre-filters** on them — unlike chat models, a media backend often simply
  cannot serve a request, and that is a skip rather than a failure. Every backend answers "are you usable?"
  via `ProbeAsync` **without generating anything**, replacing the generate-and-discard test that pattern
  otherwise requires. Chaining is first-class (`artifact.ToInput(role)` → 3d → image → video). Media keeps its
  own `GenerationVerdict` vocabulary but **shares the failure corpus** (`GenerationVerdictClassifier` delegates to
  `LlmVerdictClassifier`), so there is one definition of what a 429 or a content refusal means. Lyntai
  generates nothing itself: no inference, no engine/weights provisioning, no webhook host, no artifact storage
  (`docs/DECISIONS.md` — the backend self-maintenance boundary, the generation-as-its-own-platform decision). The LLM stack gains **zero** dependency on media — the bridge is `ITool`/MCP.
  Async-video/`Jobs` composition, governance parity, the tool bridge and pipelines follow in
  `docs/2026-08-04-generation-platform-plan.md` Plans 4–7.
- **`Lyntai.Generation.Http`** — a NEW package with the first three backends, each an independently registered
  `IGenerationProvider` over a BYO `HttpClient`:
  - **`OpenAiImageProvider`** (inline) — `/images/generations`, switching to `/images/edits` (multipart) when
    the request carries an input image. Both response variants are handled because both occur: inline
    `b64_json`, and a `url`, which is returned **as a URI artifact rather than downloaded** — the platform
    doesn't spend your bandwidth, or guess at auth for someone else's host, uninvited. A URI-only *input* is
    refused for the same reason. A content-policy rejection classifies as **`Refused`**, which is what the
    routing policy acts on.
  - **`Automatic1111Provider`** (inline) — a locally-run Stable Diffusion WebUI: `txt2img`, or `img2img` with
    `init_images` when source bytes are supplied; base64 decodes with or without a `data:` prefix. A WebUI
    that isn't running reports **`NotConfigured`**, not `Failed` — on a fresh machine that's the normal state,
    so routing skips it without penalising it. Its probe asks which **checkpoints are loaded**, because a
    WebUI with none is up but cannot generate.
  - **`ComfyUiProvider`** (**job** delivery) — local and **workflow-driven**: the caller supplies the graph in
    `Options["workflow"]` and optionally `Options["prompt-path"]` (e.g. `"6.inputs.text"`) to say where the
    prompt belongs, so `Prompt` may be null and no default graph is ever invented. Submit → poll history →
    fetch, with outputs returned as **view URIs** (a local video is easily 100 MB). An empty history reads as
    *running*, not failed. **Its surface is documented rather than measured** — no instance was available —
    so every endpoint path is a settable option and the parsing degrades to "not finished" rather than
    inventing an artifact.

  Shapes for the first two are ported from a sibling app's production implementation. 42 tests, all through a
  stubbed handler — nothing leaves the machine and no generation is billed.

## 1.2.2 — turn-free backend auth + pinned self-install (2026-08-03)

Completes the "ask and drive the backend about itself, without spending a turn" family that 1.2.0 started
(`ProbeAsync` / `UpdateAsync`): a host can now also find out **whether the backend is signed in, and as
whom**, drive sign-in/sign-out, and **pin a named version** of the backend. All generic Core capabilities —
the claude CLI is the first implementer, not the shape.

### Added
- **`IProviderAuth` + `ProviderAuthStatus` / `ProviderLoginRequest` / `ProviderAuthResult`** (Core,
  `Lyntai.Llm`) — an OPTIONAL provider capability answering the one thing every consumer needs settled
  before a turn can possibly succeed: `StatusAsync` reports `{ Authenticated, Method?, Account?, Detail? }`
  **without running a completion**. Previously a consumer either ran a completion and pattern-matched the
  failure string (a wasted, possibly billed turn) or shelled out to the backend's CLI itself — the bespoke
  provider handling Lyntai exists to remove. **"Not signed in" is a VALUE, not an exception.** `LoginAsync`
  / `LogoutAsync` drive the backend's own flows. Discovered by pattern-matching
  (`provider is IProviderAuth a`) like the other capabilities, so a backend with no login story (an
  API-key provider, a local GGUF runtime) simply doesn't implement it. `Method` and
  `ProviderLoginRequest.Mode` are free-form strings on purpose: another backend's account kinds must fit
  without an enum change. **Lyntai never stores credentials** — the backend owns its own.
- **`IProviderVersionInstaller` + `ProviderInstallRequest`** (Core, `Lyntai.Llm`) — drive the backend's own
  installer for a **named** version (`{ Version?, Force }` → the existing `ProviderUpdateResult`, so
  callers keep one result type for all self-maintenance). This is the difference between *pinning* a
  known-good version and merely taking whatever `UpdateAsync` gives you; `Updated` therefore also covers a
  deliberate **downgrade**. Lyntai still never downloads or stores a binary itself — see
  `docs/DECISIONS.md` — the backend self-maintenance boundary for where that line now sits.
- **`ClaudeCliProvider` implements both** — `claude auth status --json` / `auth login [--claudeai|--console]
  [--email <e>] [--sso]` / `auth logout`, and `claude install [stable|latest|<version>] [--force]`, through
  the same BYO `IProcessRunner` + command seams (and therefore the same Windows npm-shim handling) as a
  completion, from the neutral working directory, with no stdin. Notes on the contract a UI can rely on:
  `--json` is passed **explicitly** even though it is the CLI's default, so a build that predates `auth`
  rejects an unknown *flag* instead of treating the words as a **prompt** and spending a turn; the parsed
  state **wins over the exit code** (a signed-out CLI may report its state and still exit non-zero — that
  is an answer, not a broken backend); `LoginAsync` **blocks** until the browser flow finishes, fails, or a
  bounded 10-minute budget expires (never a hang), and `ct` abandons the wait; an unrecognized login `Mode`
  or a flag-shaped `Email`/`Version` is **refused without spawning anything**, so a free-form value can
  never become an invented backend flag.

- **`Lyntai.Providers.CodexCli`** — a NEW package: the authenticated OpenAI `codex` CLI as a provider
  (`AddCodexCliProvider()`, id `codex-cli`), so a fallback chain can span two independent CLI agents. It is
  the second implementer of the shared CLI seam below, which is what proves that seam generic rather than
  claude-shaped. Every command, flag and event shape was **measured against codex-cli 0.146.0** — `--help` for
  the argv, plus one real successful turn (through the `--oss` local-model path, so no tokens were spent) and
  one real failed turn for the JSONL. Three measured details that a guess would have gotten wrong:
  `--skip-git-repo-check` is **required** (codex refuses to run outside a git repo, and the engine spawns from
  a neutral temp dir); `codex login status` prints **prose** and rejects `--json`, so its auth parse is a
  conservative prose sniffer that returns "unknown" rather than guessing a signed-in state; and a bare `error`
  line — plus an `item.completed` whose item type is `error` — are **not** terminal (both appeared in the run
  that went on to succeed), so only `turn.failed` fails a call. It deliberately does **not** implement
  `IProviderVersionInstaller`: `codex update` takes no target, so this backend genuinely cannot pin a version.
  Completions run `--sandbox read-only` by default (a text completion shouldn't edit your disk); raise it with
  `new CodexCliDialect { SandboxMode = "workspace-write" }`.
- **Portable (non-global) CLI installs are a first-class wiring** — `AddClaudeCliProvider(command, environment)`,
  `AddClaudeCliAgentSession(command)` and `AddCodexCliProvider(command, environment, dialect)` now take the
  path to a CLI your app ships or unpacks itself, plus extra environment variables for that install (its own
  `CODEX_HOME` / `CLAUDE_CONFIG_DIR`), so a bundled backend needs no process-wide environment variable. The
  environment applies to the maintenance spawns too, so a probe/auth check reports the PORTABLE install's
  state rather than the machine-wide one's. `IsAvailable` now **verifies an explicit command exists** (via the
  new `ProcessRunner.CommandExists`, including an extensionless launcher rescued by its `.cmd` sibling)
  instead of trusting it — a missing portable copy makes the router skip that candidate rather than surface as
  a failed turn.
- **`CliProviderEngine` + `ICliProviderDialect` / `CliProviderDialectBase` / `CliOutputEvent` /
  `CliPromptDelivery` / `CliCommand`** (Core, `Lyntai.Llm.Cli`) — the generic engine behind ANY spawned-CLI
  backend, so adding one is a **dialect class plus a forwarding provider**, not a second copy of the rules.
  The engine owns everything that isn't backend-specific: command resolution (explicit override → the
  dialect's env vars → its default exe), spawn hygiene (no shell, neutral cwd, Windows launcher shims),
  timeouts as an inactivity clock with an absolute backstop, `LlmVerdictClassifier` verdicts, empty output as
  `Failed`, streaming order (content chunks then exactly one `Final`/`Error`), and probe → run → re-probe
  self-maintenance. A dialect supplies the vocabulary: argv, prompt delivery (stdin **or** a trailing
  argument), line parsing, and only those maintenance commands the backend verifiably has —
  `CliProviderDialectBase` claims **no** optional capability by default, so a backend is never credited with
  one that wasn't measured. `ClaudeCliProvider` is now this composition (`ClaudeCliDialect` + the engine);
  its members are unchanged and its ~90 existing tests pass untouched. A dialect can also report an **in-band
  turn failure** (`CliOutputEvent.Failure`) — needed because a CLI can print `turn.failed` and still exit 0,
  which would otherwise be flattened to a bare `Failed` with no reason; the message is classified, so a 401
  becomes `AuthFailed` (cools the host) rather than a generic retry.

### Fixed
- **The leak scan no longer fails closed on a pending deletion** (`devtools/scripts/check-sensitive.mjs
  --tree`) — a tracked file already removed from the working tree but whose deletion isn't staged yet (an
  ordinary mid-refactor state) read as "unscannable" and blocked `verify` with what looked like a leak report.
  A file that is *gone* has no content to leak, so it is now skipped and **reported** (never silently — a scan
  that skipped something must not print a bare "clean"). Fail-closed still applies to a file that exists but
  cannot be READ, which is the case that could actually hide something; both directions are covered by a
  reproduction of the original failure and an unreadable-path check.

### Note on binary compatibility
The optional parameters added to `ClaudeCliProvider`'s constructor and to the `AddClaudeCli*` extensions are
**source**-compatible but not **binary**-compatible: recompile against this version rather than dropping the
assembly in. Allowed in a minor by `docs/DECISIONS.md` — the deferred-SemVer-strictness rule (all consumers are first-party and build from
source); no member or type was removed.

### Changed
- **`node devtools/dev.mjs doctor` also checks version AUTHORSHIP** — `VersionPrefix` must equal the newest
  `v*` release tag, which is true by construction between releases (the release workflow bumps it *as part
  of* releasing). A new `check-version-bump` pre-commit guard blocks a staged hand-edit of `<VersionPrefix>`
  or a hand-stamp/removal of the CHANGELOG's `## Unreleased` heading, with `LYNTAI_RELEASE=1` as the
  pipeline's (and a deliberate repair's) escape hatch. This closes a real failure mode measured in a
  sibling repo: the release workflow bumps *from* whatever `VersionPrefix` says, so a helpful-looking manual
  bump silently moves the baseline and the next release publishes the version **after** the intended one
  (a hand-edited `0.1.2 → 0.2.0` published `0.3.0`; `0.2.0` went from unreleased to skipped). The existing
  consistency checks all stayed green through it — consistency was never the property at risk, authorship
  was. See `docs/DECISIONS.md` — the release-pipeline version-authorship rule.

## 1.2.1 — 2026-07-29

### Fixed
- **A Windows npm/nvm CLI shim now spawns** (`ProcessRunner`, Core) — an npm/nvm global install drops
  three launchers side by side: an EXTENSIONLESS POSIX script (`claude`), a `claude.cmd` and a
  `claude.ps1`. CreateProcess can't exec the extensionless one, so whenever the command resolved to it —
  a `where.exe` hit list without the `.cmd`, or a caller-supplied/`CLAUDE_CMD` path pointing straight at
  the shim — every spawn failed with *"The specified executable is not a valid application for this OS
  platform"* on an install that otherwise works. The runner now swaps a non-exec'able launcher for its
  spawnable **sibling** (`.cmd`/`.bat`/`.exe`/`.com`, then `.ps1` via the existing PowerShell host).
  Only paths with a directory component are probed, so a bare command name can never pick up a same-named
  file from the current directory. This is in the shared spawn path, so it fixes every caller at once —
  the reported symptom was `ClaudeCliProvider.ProbeAsync`/`UpdateAsync` reporting `Available: false` /
  `Succeeded: false` for a working shimmed CLI (`TASKS.md` CLI2, found consuming 1.2.0 on Windows).

## 1.2.0 — turn-free backend probe + self-update seam (2026-07-29)

A host can now show what its LLM backend actually IS — version, and where the backend can say so, model —
without spending a turn to find out, and can drive that backend's own updater. Generic: the claude CLI is
the first implementer of a Core capability, not the shape of it.

### Added
- **`IProviderInstallation` + `ProviderProbeResult`** (Core, `Lyntai.Llm`) — an OPTIONAL provider
  capability: `ProbeAsync` asks the backend what it is **without running a completion** (no tokens, no model
  call) and reports `{ Available, Version, Model?, Detail }`. Stronger than `ILlmProvider.IsAvailable`,
  which is a cheap guess that never contacts anything. Fails safe — an absent/stalled/erroring backend is
  `Available: false` with the reason in `Detail`, never a throw. A separate interface rather than members on
  `ILlmProvider`, so a provider that can't answer cheaply simply doesn't implement it and callers
  pattern-match (`provider is IProviderInstallation p`) over the registered provider collection.
- **`IProviderUpdater` + `ProviderUpdateResult`** (Core, `Lyntai.Llm`) — the sibling capability for a backend
  that ships its own updater: `UpdateAsync` runs it and reports `{ Succeeded, Updated, FromVersion,
  ToVersion, Detail }`. Backends have no check-only mode, so "was an update available?" is answered after the
  fact by `Updated` (the version moved). Lyntai drives the updater the backend already ships; it never
  provisions, downloads or pins a binary (that stays the host's concern).
- **`ClaudeCliProvider` implements both** — `claude --version` for the probe, `claude update` for the
  updater, through the same BYO `IProcessRunner` / `LYNTAI_PROVIDER_CMD` / `CLAUDE_CMD` seams as a
  completion, from the neutral working directory, with no stdin. `--version` is the ONLY turn-free question
  asked, deliberately: the CLI treats an unrecognized token as a **prompt** and spends a turn answering it,
  so probing by guessing subcommands would silently cost tokens. A probe stalls out after 30s (a version
  readout is sub-second work); an update gets the configured provider clocks, since it legitimately
  downloads.
- `ProviderProbeResult.Model` is **null against today's claude CLI** — it has no turn-free way to report its
  resolved model, and the probe never guesses one. Read the model actually used from
  `AgentStreamEvent.UsageFinal.Model` after a turn; the field fills in only if a build labels one on its
  version line (`model: x`), or for a backend that knows its loaded weights.
- The provider stub answers `--version` and `update|upgrade` before reading stdin, so both paths are
  covered by a real spawn in tests.

## 1.1.0 — generic CLI tool-hosting (2026-07-29)

CLI tool-hosting is no longer claude-shaped: the host is generic and each CLI contributes only its flags
and config-file shapes. Rationale in the 1.0 API sign-off pass (git history); the minor-with-a-break versioning call
is the deferred-SemVer-strictness rule.

### Breaking
- **`Lyntai.Providers.ClaudeCli.Mcp` is removed** (package and its `AddClaudeCliMcpTools()` extension).
  It had shrunk to one line of sugar, which does not justify a package id. **Migration — replace the
  package reference with `Lyntai.Tools.Mcp.Hosting` and the call with the dialect:**
  ```diff
  - .AddClaudeCliMcpTools()
  + .AddMcpToolHost(new ClaudeCliMcpDialect())     // Lyntai.Tools.Mcp.Hosting + Lyntai.Providers.ClaudeCli
  ```
  Host tweaks that would have gone to `AddClaudeCliMcpTools(configure)` go to the second parameter of
  `AddMcpToolHost(dialect, configure)`. Behavior is unchanged. Shipping in a minor per the deferred-SemVer-strictness rule (every consumer
  is currently first-party).

### Added
- **`Lyntai.Tools.Mcp.Hosting`** — the provider-neutral half of CLI tool-hosting, split out of the removed
  package (which was named for one consumer but referenced only `Lyntai.Core`). It owns the ephemeral
  loopback MCP server, the `ITool` bridge, token minting, temp-file handling and teardown;
  `AddMcpToolHost(dialect, configure?)` wires it. The OUTBOUND twin of `Lyntai.Tools.Mcp`.
- **`IMcpCliDialect` / `McpEndpoint` / `McpCliContext`** (Core, `Lyntai.Agents`) — the per-CLI half:
  flag names and config-file shapes, and nothing else. Supporting another CLI that runs its own agent
  loop is now one small class, no new package and no change to the host. In Core (not the host package)
  so a provider can ship a dialect without taking on the ASP.NET Core dependency — which would also cost
  the provider its AOT compatibility.
- **`ClaudeCliMcpDialect`** (`Lyntai.Providers.ClaudeCli`) — the `claude` flags/config shapes, now living
  with the claude provider. Costs that package no new dependencies.
- **`McpToolHostOptions`** — the MCP server name and bind address are configurable instead of hard-coded.

### Changed
- **`ICliToolProvisioner` is resolved KEYED by provider id**, first registration also taking the unkeyed
  slot as a fallback. Previously an unkeyed `TryAddSingleton`, so two CLI providers that each run their
  own agent loop collided — first registration won and the wrong dialect was injected into both.
- **`Lyntai.Providers.ClaudeCli` stays AOT-compatible and ASP.NET-free.** Only the dialect moved into it;
  the Kestrel host did not. Apps using the plain CLI provider gain no new runtime requirement.

### Internal (no public surface change)
- **OpenAI-compatible endpoint/auth rules deduped** into `OpenAiEndpoint`. `OpenAiCompatibleProvider` and
  `HttpEmbedder` each carried their own copy of the flavor resolution, the Azure `/openai/v1` rule, the
  `/v1`-suffix logic and the `api-key` header block — differing only in route name. A drift between the two
  copies would have been silent (chat keeps working while embeddings 404, or the reverse).
- **`LyntaiBuilder` qualification cleanup** — ~55 fully-qualified `Lyntai.Llm.Caching.…`-style references
  replaced with usings.

## 1.0.0 — API freeze (2026-07-28)

**Lyntai 1.0.** The adoption gate is met (three sibling apps on 0.31.1) and, before the permanent surface
freeze, a multi-agent adversarial API review + a read-only consumer-usage review settled the public
surface (the 1.0 pre-freeze API review — git history). **From 1.0, SemVer 2.0 is in force** — no breaking public-API change
without a major bump, gated by `ApiSurfaceTests` (the 1.0 API freeze). This release also collapses the accreted 0.x
migration ledger into clean per-domain baselines (the pre-release migration-folding rule one-time exception).

### Breaking
- **Migration baseline reset (consumer action required).** The 0.x SQLite (16) and Postgres (14)
  migrations are collapsed into **9 per-`StorageFeature` baselines** each (`202607280001..0009`). The NET
  schema is UNCHANGED — a schema-equivalence gate (`MigrationSchemaSnapshotTests` byte-level for SQLite,
  `PgMigrationSchemaSnapshotTests` catalog-level for Postgres) proves it — but the ledger is renumbered.
  **Before the first 1.0 run, drop your `lyntai_*` tables (including `lyntai_version_info`) or delete the
  dev database**; Lyntai recreates them. One-time, pre-1.0 only — post-1.0 the ledger is append-only.
- **Public-surface shrink/settle** (pre-freeze): `Lyntai.Tools.Mcp.McpTool`,
  `Lyntai.Providers.OpenAiCompatible.ProviderDetect` (incl. `Detect`), and
  `Lyntai.Providers.ExtensionsAi.LyntaiChatClient` → `internal`; FluentMigrator migration classes removed
  from the public surface; `LocalModelOptions.AntiPrompts` → `StopSequences`;
  `OpenAiCompatibleOptions.Flavor`/`OpenAiCompatibleEmbedderOptions.Flavor` `string` → `OpenAiFlavor` enum;
  `IVectorStore` gains a required `DeleteAsync(collection, id)`.

### Added
- **`IScorer.Applies(ScoreContext)`** — an optional default-interface predicate to gate a scorer
  declaratively, checked by `IScoringService` before `ScoreAsync` (distinct from `ScoreAsync` returning
  null = "ran, no score"). Non-breaking to existing scorers.
- **`IVectorStore.DeleteAsync(collection, id)`** — single-vector removal across InMemory / SQLite /
  Postgres (pgvector), for incremental collection updates without a full rebuild.
- **`OpenAiFlavor` enum** (`Auto`/`OpenAi`/`Ollama`/`OpenRouter`/`AzureOpenAi`) — a typo-safe replacement
  for the old magic-string flavor; `Auto` = URL-detected.
- Member-level XML docs on non-obvious frozen surface: `PostgresVectorStore` search/upsert/remove,
  `HttpEmbedder.EmbedAsync` (throw contract), `DpapiSecretProtector` (all-input → `CryptographicException`),
  `ClaudeCliProvider.IsAvailable` (optimistic BYO-runner).
- **Built-in `IEmbedder` for OpenAI-compatible endpoints** (EMB1): `Lyntai.Providers.OpenAiCompatible` now
  ships `HttpEmbedder` + a `builder.AddOpenAiCompatibleEmbedder(id, o => { o.BaseUrl; o.Model; o.ApiKey; })`
  method, so an app already talking to an OpenAI-compatible chat endpoint can turn on semantic memory
  (`ISemanticMemory`) **without a BYO embedder**. It POSTs the batched `{model, input[]}` body and extracts
  vectors tolerantly from either the OpenAI/LM-Studio `data[].embedding` shape (re-ordered by the
  authoritative `index`) or Ollama's `embeddings[[…]]` shape — one impl covers OpenAI, LM Studio, OpenRouter,
  Azure, and local Ollama. Endpoint + flavor reuse the chat provider's `ProviderDetect` (Ollama → native
  `/api/embed`; a bare Azure resource → `/openai/v1/embeddings`; else `/v1/embeddings`, not double-prefixing a
  `/v1` base) and the same BYO-`HttpClient` seam; the per-call deadline is `LyntaiOptions.ProviderTimeout`.
  `OpenAiCompatibleEmbedderOptions.BatchSize` splits an over-cap input list into several requests. Pairs with
  the existing in-memory / `UseSqliteVectorStore` / pgvector vector stores. Additive — no breaking change.
  Live-gated Ollama coverage sits alongside the chat provider's (`LYNTAI_LIVE_OLLAMA`, embed model via
  `LYNTAI_OLLAMA_EMBED_MODEL`, default `nomic-embed-text`).

## 0.31.0 — 1.0-prep API sign-off + the curated metadata catalog (2026-07-27)

**1.0-prep: infrastructure + the final API sign-off pass**, plus the curated catalog's evolution into a
searchable, metadata-carrying store. The whole public surface was audited name-by-name against the design
contract and the .NET ecosystem's naming conventions; the resulting breaks are pre-1.0 (1.0 itself is
adoption-gated — see ROADMAP). Decisions recorded in the 1.0 API sign-off pass (git history). **One released DB change:**
the CMEM6 migration `202607270003` folds the released `Source` and the same-batch `Title` into the new
`Metadata` field and drops both columns (data-preserving backfill); every other renamed C# member maps onto
a frozen column via SELECT aliases.

### Added
> A push/PR CI workflow was briefly added here and then removed by decision: verification stays MANUAL
> (`node devtools/dev.mjs verify` before any commit/release) and releases stay manual (`release.yml`,
> triggered by hand) — see `docs/DECISIONS.md` — the manual-verification call.
- **`ICuratedMemoryStore` metadata field + query index** (CMEM6): `CuratedMemory` gains an arbitrary
  app-owned `string→string` `Metadata` map (`title`, `source`, `author`, `category`, …) — stored as one
  opaque JSON field per backend (via `CuratedMetadataJson`) and made QUERYABLE by a `metadataMatch` argument
  on `ListAsync`/`SearchAsync` (matches every given key/value pair exactly — AND). A plain relational
  `lyntai_curated_meta(memory_id, key, value)` index backs the query, so metadata filtering is identical
  across SQLite/Postgres/InMemory with no `jsonb`/JSON-function divergence; migration `202607270003` adds the
  column + index. **Breaking (pre-1.0):** `CuratedMemory` drops `Source` and `Title`, and `AddAsync`/
  `UpdateAsync` drop those params — both fold into `Metadata`. The migration backfills the existing
  `source`/`title` values into it before dropping the columns (data-preserving); a titled prompt lead is now
  rendered app-side from a `title` metadata key, and `SearchAsync` narrows to content-only. See
  `local/superpowers/specs/2026-07-27-curated-metadata-design.md`.
- **`ICuratedMemoryStore.UpdateAsync(..., kind:)`** (CMEM5): re-categorise a curated entry in place — an
  optional `kind` (COALESCE; null = leave unchanged) moves a note between kinds keeping its id and
  `created_at` instead of forcing a remove+re-add. All three backends.
- **`ICuratedMemoryStore.SearchAsync`** (CMEM4): keyword search over the curated catalog — matches CONTENT,
  with the `ListAsync`-family strict filters (`kind`/`taskKey`/`scope`, `enabledOnly` default false, the
  `metadataMatch` map) and a `limit` cap; a whitespace query returns empty (`ListAsync` is the enumeration
  path). Reuses the lexical-memory index machinery per backend, with the same documented divergence and
  fail-open behavior as `IMemoryStore.RecallAsync`: SQLite matches any ≥3-char token via a `lyntai_curated_fts`
  FTS5-trigram table (bm25-ranked, LIKE fallback); Postgres matches a contiguous substring via
  pg_trgm-accelerated ILIKE (recency-ranked, GIN-indexed); InMemory substring/recency. The SQL curated stores
  gained the optional `ILogger` constructor parameter the memory stores already had.
- **SourceLink / deterministic release builds** (`src/Directory.Build.props`): `PublishRepositoryUrl` +
  `ContinuousIntegrationBuild` under GitHub Actions (the manual `release.yml` pipeline) — stepping into
  Lyntai from a consuming app resolves sources from the repo.
- **Azure OpenAI as a first-class flavor**: `AddAzureOpenAiProvider(...)` preset,
  `ProviderDetect.AzureOpenAi` (detects `*.openai.azure.com`), bare-resource endpoint completion to
  `/openai/v1/chat/completions`, and the `api-key` header sent alongside `Authorization: Bearer`.
- **`IKeyValueStore.ListKeysAsync(prefix?)`** — enumerate stored keys (ordinal order, case-sensitive
  starts-with) on all three backends; `LikePattern.StartsWith` helper for SQL prefix patterns.
- **`IResponseCache.RemoveAsync(key)`** — evict one poisoned/stale entry without waiting out its TTL,
  on all three backends.
- **`IJobQueue.GetAsync(id)` / `ListAsync(status?, lane?, limit)`** — the front door's read side; apps
  watch jobs without injecting the storage-layer `IJobStore`.
- **`AddPlaintextSecretVault()`** — the LOUD dev-only opt-in for an unencrypted vault (see the
  `AddSecretVault` break below).
- **`LyntaiBuilder.AddScorer(Func<IServiceProvider, IScorer>)`** and **`AddEmbeddings<TEmbedder>()`** —
  factory/generic registration parity with the other seams.
- **`IProcessRunner.StreamLinesAsync` `maxDuration`** — an optional wall-clock backstop alongside the
  inactivity window (a slowly-dripping stream can no longer run unbounded).
- **`Lyntai.Llm.Streaming.GuardedStream` + `InactivityClock`**: the provider streaming read-loop
  (arm-the-inactivity-clock → read → stop-the-clock, caller-cancel rethrow, per-provider fault→terminal
  mapping) now lives once in Core — the hand-rolled copies in all five streaming providers were exactly
  where the wall-clock timeout bug shipped twice. BYO `ILlmProvider` authors should iterate it instead
  of hand-rolling the loop.

### Fixed
- **Rate limiter honors live option retunes** (it documented them but froze rates at construction):
  bucket rate/burst now resolve from current options on every acquire, so a global limit enabled after
  startup (env override / admin) throttles, and a per-consumer rate change applies to the existing
  bucket immediately. An explicit zero-rate consumer now refuses after its burst instead of throwing.
  The verdict classifier's custom-matcher list is also read as a lock-free snapshot per classification.
- **Streamed runs no longer buffer unbounded stderr**: `ProcessRunner.StreamLinesAsync` drains stderr
  as a rolling 500-char tail (all it ever reported) instead of `ReadToEndAsync` — a child spewing MBs
  of diagnostics while streaming can't grow the parent's memory. `RunAsync` still returns full stderr
  (that's its contract).

### Breaking (pre-1.0, unreleased batch)
- **`LyntaiBuilder.DefaultCandidates` → `UseDefaultCandidates`** — hard rename, no shim: the method SETS
  (replaces) the fallback chain, and "Use" is the set-semantics builder family.
- **`UseSqliteStorage` / `UsePostgresStorage` bool pair → `SchemaMigration` enum**
  (`OnStartup` / `OnFirstUse` / `None`) — `UseSqliteStorage(path, migrate: false, deferMigration: true)`
  ambiguity is gone; the enum names the lifecycle.
- **`AddSecretVault` now REQUIRES the encryption key** (throws `ArgumentException` on null/empty): a
  "secret vault" must never silently store plaintext. Dev/testing opt in loudly via
  `AddPlaintextSecretVault()`.
- **`IResponseCache.TryGetAsync` → `GetAsync`** (null-on-miss, matching `IKeyValueStore`/`ISecretVault`
  naming); interface also gained `RemoveAsync` (BYO impls must add it).
- **`IUsageTracker` is fully async**: `Record/Total/Reset` → `RecordAsync/TotalAsync/ResetAsync`
  (`ValueTask`); per-consumer totals aggregate case-insensitively across all three backends.
- **`IProcessRunner`**: `timeout` parameters renamed `inactivityTimeout` on both methods (they always
  were inactivity clocks — the name now says so); `StreamLinesAsync` gained the `maxDuration` parameter
  before `workingDirectory`. `ProcessResult` replaces `TimedOut : bool` (ctor position) with
  `TimeoutKind : ProcessTimeoutKind` (`None`/`Inactivity`/`MaxDuration`); `TimedOut` remains as a
  computed property and `Success` still excludes timeouts.
- **Renames for what things ARE:** `CuratedMemory.Task` → `TaskKey` (a scoping key, not a
  `System.Threading.Tasks.Task` — DB column unchanged, aliased in SELECTs);
  `OpenAiCompatibleOptions.NumCtx` → `ContextSize` (`int?`; `LocalModelOptions.ContextSize`
  `uint?` → `int?` to match); `UsageLive`/`UsageFinal` members gained the `*Tokens` suffix
  (`InputTokens`, `OutputTokens`, `CacheReadTokens`, + `CacheCreateTokens` on final).
- **Wire-format internals are now `internal`:** `ClaudeArgs`, `ClaudeAgentArgs`, `StreamJsonParser`,
  `StreamJsonEvent(+Kind)` (ClaudeCli); `OpenAiPayload`, `OllamaPayload` (OpenAiCompatible). They were
  implementation detail of the providers, never a consumer seam.
- **SQLite `ListKeysAsync`** (new API, noted for completeness): prefix matching is case-SENSITIVE
  (BINARY `substr`, not `LIKE` — SQLite's `LIKE` is ASCII case-insensitive and would have broken the
  cross-backend contract).
- `IScorer.ScoreAsync`'s `CancellationToken` gained `= default` (source-compatible; binary-identical).

`ApiSurface` baselines regenerated deliberately for all 11 assemblies — the baseline renderer itself now
also emits `sealed`/`abstract`/`static`/`required` modifiers, so a future accidental modifier change
fails the surface test too.

## 0.30.0 — 2026-07-26

Two bodies of work: (1) consumer-driven generic ergonomics from an agent-manager desktop adopter
(CLI1/TL1/TL2/PR1) plus the source-study curated-memory papercuts (CM1/CM2) — all additive; and (2) a
**whole-library foundation-hardening pass** (six parallel reviews → ~80 findings → correctness fixes,
structural dedup, and test-suite hygiene across every package, then a second adversarial review of the
pass's own diff). No new migration. **Breaking changes below (pre-1.0 minor-bump policy)** — see the
"Breaking" paragraph in the hardening section.

### Changed / Fixed — foundation-hardening pass
**Correctness (behavior fixes):**
- **Router:** thrown provider errors now classify through `LlmVerdictClassifier` (a thrown 429 cools the
  host instead of hammering it); an EMPTY provider stream (zero chunks, or Final with no content) is a
  failure that falls over / ends with a terminal Error chunk — never a silent end.
- **Rate limiter:** per-consumer buckets are case-insensitive like their options map ("Chat"/"chat" no
  longer each get a full rate); a cancelled wait refunds its permit and PROPAGATES the cancellation
  (was: a fabricated `RateLimited` reply + a polluted refusal metric). **Behavior note** for BYO
  `IRateLimiter` impls: cancellation should now throw, not return false.
- **Env config:** every numeric `LYNTAI_*` knob parses invariant — on a comma-decimal locale (de-DE)
  `"1.5"` used to parse as **15** (a silently 10x-wrong timeout).
- **ProcessRunner:** the stdin observe after stdout EOF is bounded by the inactivity clock (a child that
  closed stdout but never drained stdin could hang the call forever without a `maxDuration`).
- **Guards:** chained arg-rewriting guards now COMPOSE on the tool-call gate (`GuardRail` overrides
  `InspectToolCallAsync` with an args-aware re-thread; the interface default couldn't compose).
- **Chat orchestration:** the input gate runs on the RAW user message BEFORE memory composition — an
  input-gate Replace used to persist the whole redacted composed prompt, re-storing recalled facts as a
  new record every turn (compounding memory growth).
- **Prompts:** placeholder substitution is single-pass — a var value containing `{otherKey}` stays
  literal (was order-dependent injection over dictionary order).
- **Bridges:** prose alongside native tool calls survives transcript replay (OpenAI payload + MEAI
  forward bridge); the reverse bridge maps declaration-only tool schemas (`AIFunctionDeclaration`);
  streamed OpenAI-flavor requests send `stream_options.include_usage` so streams stop bypassing
  budget/telemetry accounting; the ephemeral MCP config (bearer token) is owner-only on Unix.
- **Jobs/scheduler:** a corrupt persisted next-run self-heals (re-anchor + overwrite) instead of
  silently freezing the schedule forever; the impossible-cron error names the expression.
- **DI:** `AddLyntai` now also throws when an `ILlmClient` is pre-registered and an `IRefusalMatcher`
  was added (it would have been silently ignored); conversation-enrichment wrapping preserves a BYO
  store's lifetime; `AddEnvelopeSecretVault` + a BYO `ISecretVault` no longer throws `InvalidCastException`.
- **Storage:** all async store methods open connections via `OpenAsync` (Postgres no longer blocks a
  threadpool thread per call); FTS recall has a deterministic id tiebreak; `InMemoryConversationStore`
  throws on a duplicate thread id like the SQL backends; Postgres `AppendMessageAsync` retries a
  transient seq race instead of surfacing a raw unique-violation.

**Structural dedup (drift-prevention):**
- **`JobStoreSql` + `JobRow` (Core):** the relational job stores' state machine (fence predicate, every
  transition, insert, reads) and row materialization live once — executed by both backends with booleans
  bound as parameters; only the claim statement stays per-dialect.
- **`DelegatingLlmClient` (Core, public):** the decorator base all five front-door decorators now derive
  from — BYO decorators can too.
- **`LazyMigratingConnectionFactory` (Core, public):** the once-only/retry-on-transient-failure lazy
  migration gate (now also covering `OpenAsync`), wrapped by both packages' migrating factories.
- **`VectorMath.Cosine` (Core, public)**, **`StorageFeatures.TagPasses`**, `StreamJsonFields.ReadUsage`,
  `GuardRail` shared gate loop + `Redact`, router `LiveCandidates` preamble, shared `JsonArgs` in the
  MCP toolset, one `LyntaiOptions` env-parse helper set, one non-convergence tail in `ToolLoop`.

**Breaking (pre-1.0):** `ChatResult.BlockReason` → `Detail` (the old name lied for non-blocked failures);
`GuardOutcome.IsAllow` removed (unused); `CandidateDedup` is now internal; `IRateLimiter` cancellation
semantics (above). `ApiSurface` baselines updated deliberately for all of the above.

**Tests:** +~30 targeted regression tests (TDD per fix); shared `Fakes/` helpers (`TestPaths`,
`TempDbPath`, one `FakeProcessRunner`, `MutableClock` relocated); the six process-global
`ClearAllPools()` re-introductions replaced with per-db pool clears; duplicated contract tests removed;
`RecallAsync(limit:)`/scoped `ForgetAsync` and the `SkipAllPermissions`+ReadOnly matrix now pinned.

**Round 2 (adversarial review of the pass itself)** — a workflow-backed review of this branch's own diff
confirmed and fixed five regressions the pass had introduced: the chat orchestrator now gates the
COMPOSED prompt too (recalled memory can't bypass input guards; the memory-compounding fix stands);
prompt placeholder keys are unrestricted again (hyphen/dot/CJK keys substitute — the single-pass rewrite
had narrowed them to ASCII identifiers); the stdin inactivity clock counts a child's active DRAINING as
liveness (pipe-granular slice writes re-arm it) and an observe/reap kill classifies as Timeout; a THROWN
transport error can no longer surface as a terminal Refused on a keyword match; and streamed OpenAI usage
is actually read (the SSE loop runs to `[DONE]` and parses the trailing empty-choices usage chunk).
Plus: consumer identity is now case-insensitive END-TO-END (usage-tracker totals aggregate across casings
— closes a 2× per-consumer budget overspend; **behavior note** for the SQL trackers' `Total(consumer)`/
`Reset(consumer)`), the dialect-neutral job-claim predicate is shared (`JobStoreSql.ClaimCandidateWhere`),
the one remaining sync connection open is async, and the `.ps1`-hosting exception to the spawn-hygiene
rule is documented where the rule lives.

### Added
- **Headless "skip all permissions" for the Claude agent session (CLI1)** — `ClaudeAgentOptions` gains an
  opt-in `SkipAllPermissions` bool. When set, `ClaudeAgentArgs.Build` emits `--dangerously-skip-permissions`
  and SUPPRESSES the conflicting `--permission-mode` / `--allowedTools` (the CLI rejects combining them), so a
  headless `-p` run against the user's OWN machine/resources no longer hangs waiting for a permission
  responder. The always-denied flow tools (AskUserQuestion/ExitPlanMode/EnterPlanMode) and the caller's
  `DisallowedTools` (+ the ReadOnly write-tool denial) are still honored. Documented as opt-in/dangerous.
- **Per-run token usage on `ToolLoopResult` (TL1)** — `ToolLoopResult.Usage` (nullable `LlmUsage`) aggregates
  every front-door call the loop made (summed input/output/cache-read tokens; cost summed when any call
  reported one, else null; null overall when no provider surfaced usage). A tool-loop consumer gets a per-run
  token/cost figure without wrapping `ILlmClient` in its own front-door decorator.
- **Live progress from `IToolLoop` (TL2)** — `IToolLoop.StreamAsync(req, maxIterations?, ct)` yields
  `AgentStreamEvent`s as the loop runs: a `ToolCall` then `ToolResult` per tool round-trip (so an interactive
  UI shows tool chips live), assistant `TextDelta`(s), a `UsageFinal` when usage was reported, and one terminal
  `SessionEnded`. Mirrors `IAgentSession.StreamAsync` (no `SessionStarted` — a Lyntai-driven loop has no
  external session id); events are TURN-granular (the native path needs the whole reply to read its structured
  tool calls). `RunAsync` now folds the same shared core, so both doors stay in lockstep. `StreamAsync` is a
  DEFAULT interface method (a BYO `IToolLoop` that only implements `RunAsync` gets a functional post-hoc stream
  for free; the built-in `ToolLoop` overrides it with a live one) — so this is additive, not a break.
- **Curated-memory dedup-on-add (CM1)** — `ICuratedMemoryStore.AddAsync` gains an opt-in `dedup` bool: when
  true the add is idempotent on the (kind, content, task, scope) identity (returns the existing id, writes no
  second row — mirroring `IMemoryStore.RememberAsync`), so a consumer can write a fact idempotently without a
  pre-`ListAsync`+compare. Default (false) keeps the deliberate-catalog always-insert behavior. Across
  InMemory/SQLite/Postgres (null-safe identity match via `IS` / `IS NOT DISTINCT FROM`).
- **Curated-memory `scope` filter on `ListAsync` (CM2)** — `ICuratedMemoryStore.ListAsync` gains a strict-
  equality `scope` filter (the optimize/admin pass: "all notes for ONE scope, incl. disabled"); null = no
  filter (unchanged). Across all three backends.

### Fixed
- **Default `ProcessRunner` hosts `.ps1` launcher shims on Windows (PR1)** — a `.ps1` launcher (some Windows
  CLIs ship one) can't be exec'd directly by CreateProcess; the runner now resolves it (preference
  `.cmd` → `.exe` → `.ps1`) and hosts it via `powershell -NoProfile -ExecutionPolicy Bypass -File`, rather than
  failing with a Win32Exception. The `.cmd`/`.exe` resolution and BOM-less UTF-8 streams were already in place
  — so the common Windows CLI shims now work out of the box, and a BYO `IProcessRunner` is only needed for a
  genuinely different launch policy (sandbox, remote, custom encoding).

### Docs
- **New `.claude/knowledge/generic-library.md`** — the discipline for turning a consumer-specific request
  ("app X needs Y") into app-agnostic library surface (Core interface / adapter option / BYO seam), with red
  flags and the current backlog as worked examples. Registered in `RULES_INDEX.md`.

## 0.29.3 — 2026-07-23

The 0.29.1–0.29.3 patch series: consumer-driven generic gaps (TASKS.md Part 11 · Part 12) plus CLI-runner
hardening (the `StreamLinesAsync` large-prompt deadlock, then buffered inactivity/dead detection). All
additive; public surface grew (`ApiSurface` baselines updated) — no removals, existing calls
source-compatible. One seam signature grew: the buffered `IProcessRunner.RunAsync` gained an optional
`maxDuration` parameter (calls stay source-compatible; a BYO `IProcessRunner` implementation must add the
parameter to its override) — see Fixed, buffered dead detection.

### Added
- **Opt-in memory-prune cron job (Part 15)** — `builder.AddMemoryPruneJob(cron, olderThan?, taskKey?)`
  registers a durable-job handler that calls `IMemoryStore.PruneAsync` on a cron schedule: background GC
  that reclaims storage from cold/expired `(taskKey, scope)`s that on-write eviction never revisits. Lyntai
  owns the prune work; the app owns the pump (drives `IJobScheduler`/`IJobRunner` — no self-run timer, per
  the "no host" boundary). Idempotent; call it more than once for several schedules.
- **App-configurable memory retention (Part 14)** — `IMemoryStore` size management is now a
  `MemoryRetentionPolicy` (`LyntaiOptions.MemoryRetention` / `ConfigureMemory(...)` / `LYNTAI_MEMORY_*`),
  mirroring the configurable `RoutingPolicy`: a per-scope count cap with **FIFO or LRU** eviction, a default
  TTL, a per-scope size (character) budget, and presets (`CountCap` / `TimeToLive` / `SizeBudget` /
  `Composite` / `Manual`). Eviction is a single pure `MemoryEviction.Survivors` helper shared by all three
  backends (InMemory / SQLite / Postgres). LRU adds a `last_accessed_at` column (migration `202607220002`,
  `ADD COLUMN` + backfill — the SQLite FTS update-trigger is scoped to `content` to avoid churn). The default
  reproduces the historical 500-entry FIFO cap; `MemoryCapPerScope` now proxies it (source-compatible).
- **Curated memory `task` + `scope` (CM1)** — `CuratedMemory` gains optional nullable `Task`/`Scope`;
  `ICuratedMemoryStore` gains `ForCompositionAsync(task, scopes, enabledOnly)` (enabled entries whose task
  matches or is null, and whose scope is null/empty or ∈ scopes; an EMPTY `scopes` disables scope filtering)
  plus a `task` strict-equality filter on `ListAsync`/`AddAsync`. `CuratedMemorySections` gains the shared
  `AppliesTo` predicate and a `(task, scopes)` filter on `Compose`. New migration **`202607220001`** adds
  nullable `task`/`scope` to `lyntai_curated_memory` on SQLite + Postgres (a separate `ADD COLUMN` migration,
  not a fold, since the table shipped in 0.28; no backfill — null = "applies everywhere", so existing rows
  are unchanged). Across InMemory/SQLite/Postgres.
- **`IConversationStore` count + keyset paging (G3)** — `CountThreadsAsync()` and `ListThreadsPageAsync(limit,
  after)` (cursor is the last thread of the previous page; same `created_at DESC, id DESC` order, stable across
  same-timestamp ties). Default interface methods (BYO impls keep working) with efficient `COUNT(*)` / keyset
  overrides on all three backends.

### Fixed
- **Buffered CLI completion uses INACTIVITY-based dead detection, not a wall clock (Part 1)** — the buffered
  path (`ProcessRunner.RunAsync`, driving `ClaudeCliProvider.CompleteAsync`) applied a single wall-clock
  timeout over the whole call, so a slow-but-ALIVE turn (a big prompt, a long tool loop) was killed exactly
  like a dead/stalled one — a consumer couldn't tell "working" from "hung," and raising the wall-clock just
  delayed the false kill. `RunAsync` now treats `timeout` as an **inactivity window**: it reads stdout in
  chunks and re-arms the clock on each, so the child is killed only after the window elapses in **true
  silence** (a streaming/tool-looping child keeps resetting it) — the same discipline `StreamLinesAsync`
  already used. A new **absolute `maxDuration`** backstop bounds a child that never stalls but never finishes;
  `ProcessResult.TimeoutKind` (`Inactivity` vs `MaxDuration`) says which fired, and `CompleteAsync` surfaces
  the distinction in the timeout `Detail`. `ClaudeCliProvider` passes the resolved timeout as the window and
  `MaxProviderTimeout` (raised to the window if a consumer budget exceeds the ceiling) as the backstop.
  **Seam note:** `IProcessRunner.RunAsync` gained an optional `maxDuration` parameter — callers are
  source-compatible, but a BYO `IProcessRunner` must add the parameter to its override.
- **`StreamLinesAsync` no longer deadlocks on a prompt larger than the OS pipe buffer** — the streamed CLI
  runner (`ProcessRunner.StreamLinesAsync`) used to `await` the FULL stdin write and close stdin **before**
  starting the stdout read loop. A child that emits stdout before draining stdin (e.g. `claude
  --output-format stream-json`, which prints its startup / MCP handshake first) then deadlocked on a large
  prompt: the parent blocked filling the stdin pipe while the child blocked filling the stdout pipe the
  parent hadn't begun draining — the turn never started and the call hung to its timeout (a consumer's agent
  loop would silently time out or fall back to a non-LLM path). The stdin write now runs **concurrently**
  with the read loop (its outcome observed after the loop), so stdout drains as stdin is fed — matching
  `RunAsync`'s read-first ordering. Internal behavior only; public surface unchanged.
- **`ClaudeToolCalls.FilePathOf` now reads `notebook_path`/`path` (G1)** — checks `file_path`, then
  `notebook_path` (NotebookEdit), then `path`, so an edit-tracker built from the agent stream no longer
  silently misses NotebookEdit (or any `path`-arg tool) writes.
- **Agent-session `FinalText` falls back to assistant text (G2)** — when a run ends with assistant text but an
  empty/absent terminal `result` (truncation / older CLI / provider variant), both the claude adapter's
  `SessionEnded.FinalText` and the generic `RunAsync` fold (accumulated `TextDelta`s) fall back to the
  assistant text instead of `""`, so consumers that treat empty as failure don't spuriously fail. The fold
  backfills only for SUCCESSFUL terminals — a timed-out/failed run keeps an empty `FinalText` (it is not
  dressed up as a partial success; `Verdict`/`IsError` still report the truth).

## 0.29.0 — 2026-07-20

Part 7 (app-owned storage adoption) + Part 8 (generic/sustainable review sweep) + Part 9 (storage feature
toggles) + Part 10 (actor/mailbox durable jobs).

### Changed
- **Generic typed-event conversation store (Part 7 · P2)** — a conversation is now modelled as a typed
  multi-kind event stream (text / tool-call / tool-result / usage / thinking / phase / error), not only
  role/text chat turns — so an agent transcript or a tool-loop run persists through the same surface, and a
  complex external event log fits without a bespoke schema. The enriched `ChatMessage` is
  `(Id, ThreadId, Seq, Kind, Payload, Metadata, CreatedAt)`: `Id` is a store-generated **GUID** handle;
  `Seq` is the **1-based per-thread** sequence (external event-stream schemas key on `(thread_id, seq)`);
  `Kind` is the event/message type (a role for a plain chat turn; `Role`/`Content` kept as read-only chat
  aliases); `Payload` the body (text or JSON); `Metadata` optional **per-message** JSON. `ChatThread` gains
  optional opaque `Metadata` (thread-level JSON state) with `IConversationStore.SetThreadMetadataAsync` +
  a `metadata` arg on `CreateThreadAsync`. **Add your own additional info without forking the store** via a
  new `IConversationEnricher` DI collection (`AddConversationEnricher<T>` / factory) — Lyntai owns the LLM
  storage, and each registered enricher is invoked after a thread/message write to persist the app's own
  info in its own store (auto-wired by `EnrichingConversationStore` only when an enricher is registered).
  **Breaking (pre-1.0):** `ChatMessage.Id` changes `long`→`string` (GUID); message columns become
  `id(TEXT)/thread_id/seq/kind/payload/metadata/created_at` (was `id(INTEGER)/thread_id/role/content/
  created_at`); threads gain a `metadata` column; the message index is now `UNIQUE(thread_id, seq)`
  (migrations edited in-place, pre-release — no data migration). `AppendMessageAsync` is
  `(threadId, kind, payload, metadata=null, ct)` (the store assigns Id + Seq). All three backends
  (SQLite / InMemory / Postgres).
- **App-owned storage direction (Part 7 · P3)** — settled the design: **Lyntai owns and manages the LLM
  storage schema** (its `lyntai_*` tables + migrations); an app adds its ADDITIONAL INFO via the record
  `metadata` fields and the `IConversationEnricher` seam (above), rather than pointing Lyntai at its own
  tables (which would force the app to manage schema versions). An app that genuinely needs its own backend
  still registers its own domain-store impl (a full BYO — see the `TryAdd` registration change). The
  earlier "configurable table names" direction was dropped as contrary to this.
- **App-owned cortex KV (Part 7 · P1)** — the cortex KV key namespaces are now configurable, so an app can
  point Lyntai's prompt/model overrides straight at its OWN existing keys — no prefix-translating shim, no
  duplicated rows. `PromptRegistry` and `KeyValueModelRoutingStore` take an optional `keyPrefix` ctor
  argument, surfaced on the builder as `LyntaiOptions.PromptKeyPrefix` / `LyntaiOptions.ModelKeyPrefix`.
  Defaults are UNCHANGED (`lyntai.prompt.` / `lyntai.model.`), so existing consumers are unaffected.
  **Breaking (pre-1.0):** the public `KeyPrefix` const on both stores is renamed to `DefaultKeyPrefix`; the
  effective prefix is now the instance `KeyPrefix` property.
- **KV backing table renamed `lyntai_app_config` → `lyntai_kv`** — it is Lyntai's own key→value store
  (prompt/model overrides, scheduler next-run, secret vault), never the *application's* config; the old name
  was a carry-over that read backwards for a library table. Applied in-place to the existing migration
  (pre-release — no data migration). **Breaking (pre-1.0)** for anyone reading the raw table.

### Tests
- **Dapper DateTimeOffset-handler parity pinned (Part 8 · R15)** — the SQLite + Postgres factories each
  register a `DateTimeOffsetHandler` into Dapper's PROCESS-GLOBAL registry (whichever static ctor runs last
  wins process-wide), documented as "must stay identical." Added a Docker-free test asserting the two
  handlers `Parse`/`SetValue` identically across a battery of inputs, so a silent drift is caught the moment
  one is edited (the handlers are now `internal` + `InternalsVisibleTo(Lyntai.Tests)`).
- **Cross-backend storage parity (Part 8 · R5)** — Postgres integration tests no longer false-green: they
  were `if (!pg.Available) return;` (PASS when Docker is absent), now `[SkippableFact]` + `Skip.IfNot` (via
  `Xunit.SkippableFact`) so a Docker-less run reports them SKIPPED, not passed. Extracted backend-agnostic
  shared contracts for `IKeyValueStore` / `IConversationStore` / `IMemoryStore` / `ITraceStore` /
  `IPromptVersionStore` (joining the existing Score/Job/CuratedMemory contracts) and run InMemory + SQLite +
  Postgres through them, replacing Postgres's ad-hoc re-implementations — so the in-memory test double can't
  green-light semantics the SQL stores don't reproduce. Genuinely backend-divergent behavior (memory recall
  ordering / multi-word matching — bm25 vs recency/substring) is deliberately kept OUT of the shared
  contracts as backend-specific tests (tracked as R19).

### Docs
- **Scheduler is single-process (Part 8 · R20)** — documented on `IJobScheduler` that exactly one scheduler
  process must drive the pump: unlike the job runner (N instances via atomic claim), the scheduler's
  read-due → enqueue → persist-next-run sequence is not a compare-and-swap, so two scheduler processes on a
  shared KV store would each fire every schedule once. The runner fleet can still be N; the idempotent-handler
  guidance is the backstop. (A CAS `SetNextAsync` was deferred — it needs an `IKeyValueStore` interface
  change across all backends.)
- **Env-override reference completed (Part 8 · R18)** — the durable-jobs family (`LYNTAI_JOBS_LEASE_SECONDS`
  / `_POLL_SECONDS` / `_MAX_ATTEMPTS` / `_BACKOFF_SECONDS` / `_DEFAULT_CONCURRENCY` / `_MAX_STEP_LOG`) and the
  `LYNTAI_DEFAULT_MODEL` alias were read by `ApplyEnvOverrides` but missing from the `LyntaiOptions` XML-doc
  list + README; added them (plus the cache/budget/rate-limit/tool-loop vars the README had omitted).
- **Semantic `RememberAsync` throw contract + model-swap behavior documented (Part 8 · R16)** — made
  explicit that `ISemanticMemory.RememberAsync` SURFACES failures (deliberately asymmetric with the
  fail-open `RecallAsync` — a silently-lost write is worse than a throw; the orchestrator already guards its
  own call), and that after an embedding-model swap the vector stores degrade gracefully (in-memory/SQLite
  rank a dimension-mismatched row last; pgvector rejects it) so a reindex is required. (A persistent
  per-collection dimension stamp was deliberately not added — it would contradict the intentional
  graceful-degradation design and needs an `IVectorStore` change; the stores already handle the mismatch.)
- **Version-drift reconciliation + guard (Part 8 · R7)** — refreshed the README `## Status` (was stuck at
  v0.15, now v0.28.5 with the full feature arc); relocated the agent-session (Part 6) entries from
  "Unreleased" into a `## 0.28.5` section (with 0.28.2–0.28.4 consolidated) so Unreleased reflects only the
  not-yet-released work; and added `node devtools/dev.mjs doctor` (a `pack` pre-check) that fails if the
  README Status version ≠ `VersionPrefix`, so a shipped nupkg can't advertise a stale version.
- **`ITraceService` is the BYO / app-driven persisted-trace API (Part 8 · R4)** — clarified (XML-doc +
  README) that Lyntai's batteries-included flows do NOT auto-populate a `RunTrace`; the automatic
  observability path is the OpenTelemetry `Activity` spans on the `Lyntai.Llm` / `Lyntai.Agents` sources.
  Use `ITraceService.Begin`/`Record` when you want your own durable, step-shaped run history.

### Added
- **`AddRateLimit` warns when it resolves to no effective limit (Part 8 · R21b)** — calling `AddRateLimit()`
  with all-default options (global `PermitsPerSecond=0` and no per-consumer rate) silently throttled nothing.
  It now logs a warning at front-door resolution (after env overrides are applied) pointing at the setting to
  fix, mirroring the intent of the pre-registered-client guard — while still serving (a no-op passthrough, so
  a limit raised later via env still takes effect). Only the built-in token bucket is checked; a BYO
  `IRateLimiter` owns its own effectiveness.
- **Typed `IRefusalMatcher` seam (Part 8 · R21b)** — a structured alternative to the stringly-typed
  per-request `LlmRequest.RefusalPattern` regex. Register matchers with `AddRefusalMatcher<T>()` / instance /
  factory; the refusal-screening front door runs every one on an Ok reply's text (after the central patterns
  + the request pattern) and surfaces the reply as `Refused` (no fallback) if any returns true. A matcher
  keys off the whole request (consumer/model/language) as well as the text, and a throwing matcher fails open
  (logged + ignored). **Breaking (pre-1.0):** `RefusalScreeningLlmClient`'s constructor gains an optional
  `IEnumerable<IRefusalMatcher>` before the logger (only affects direct positional construction — it's a
  front-door internal, normally wired by `AddLyntai`).
- **Reverse MEAI bridge parity — tools / json-schema / multimodal / tool turns (Part 8 · R21b)** — consuming
  Lyntai `AsChatClient()` (the reverse `IChatClient` bridge) now maps the full request surface the forward
  bridge already did: `ChatOptions.Tools` → `LlmRequest.Tools`, a JSON-schema `ResponseFormat` →
  `LlmRequest.JsonSchema`, image `DataContent`/`UriContent` → `LlmMessage.Attachments`, and assistant
  `FunctionCallContent` / `FunctionResultContent` turns → tool-call / tool-result messages. The response
  completes the round-trip: a reply's native `ToolCalls` surface back as `FunctionCallContent` (with a
  `ToolCalls` finish reason), so an MEAI app can drive tool-calling *through* Lyntai. `JsonArgs` gains a
  `Parse` companion (JSON string → arg dict); the forward bridge's private copy now delegates to it too.
- **`Lyntai.Text.JsonArgs` — shared reflection-free tool-arg serializer (Part 8 · R21b)** — the
  boxed-primitive/`JsonElement`/`JsonNode` → JSON switch that the MCP tool-host (`ToolFunction`) and the MEAI
  provider bridge (`ExtensionsAiProvider`) each carried a private copy of is now one public helper in Core
  (`JsonArgs.ToNode` / `JsonArgs.Serialize`), so the two adapters can't drift on how a `3` vs `"3"` is
  serialized. Public (not `InternalsVisibleTo`-shared) — it's the primitive any custom tool bridge wants.
- **`TraceStep.Sequence` + `TraceStep.OffsetMs` (Part 8 · R21b)** — a run-trace step now carries an explicit
  0-based timeline ordinal (`Sequence`) and its wall-clock offset from the run start in ms (`OffsetMs`),
  stamped by the recorder at `Record` time (using its injectable clock) instead of the timeline relying on
  the store's insertion order. Persisted + ordered-by on all three backends (`offset_ms` column folded into
  the existing trace migration, pre-release; `seq` already existed). Additive — both default to 0 on a
  hand-built step.
- **Durable-job partition keys — actor mailboxes (Part 10 · A1)** — `JobSpec`/`EnqueueAsync` gain an optional
  `partitionKey`: jobs sharing a `(lane, partitionKey)` run **one-at-a-time in FIFO (enqueue) order** (an
  actor mailbox), while different keys run in parallel up to the lane's concurrency. Enforced in the atomic
  claim on all three backends (a candidate with a partition is claimable only if no live-leased Running
  sibling exists; a Pending one additionally requires no Running sibling at all — a stale/crashed Running is
  *reclaimed* first, keeping its slot — and must be the earliest available Pending of the partition; priority
  still orders across partitions). `partitionKey = null` is unchanged behavior. Verified on InMemory, SQLite,
  and Postgres (live container). Builds on the existing durable persistence + atomic claim + crash-resume.
- **Storage feature toggles (Part 9 · F1)** — every storage domain is individually enable/disable-able via a
  `[Flags] StorageFeature` (KeyValue / Conversation / Memory / Score / Trace / PromptVersion / Jobs /
  Governance / CuratedMemory / All): `UseSqliteStorage(path, StorageFeature.Score | …)` /
  `UsePostgresStorage(conn, …)` register only the selected domains' stores AND migrate only their tables — a
  disabled feature lands **no `lyntai_*` table** and registers no store (its null-tolerant consumers skip it;
  a direct `GetRequiredService` throws — the startup signal that a disabled feature is being used). Selective
  migration is tag-driven: each migration carries `[Tags(nameof(StorageFeature.X), StorageFeatures.AllTag)]`;
  `All` runs one pass, a subset one pass per feature. Default (`All`) is the historical behavior. The Postgres
  monolithic `InitialSchema` was split into per-feature migrations for parity (pre-release). Verified on both
  backends against a real Postgres container.
- **Scoring read/aggregate/export on `IScoringService` (Part 8 · R17)** — `AggregateAsync` / `ExportAsync`
  (and per-session `GetAsync`) lived only on `IScoreStore`, forcing a dashboard to inject the storage
  interface and reach past the service seam. They're now on `IScoringService` too (delegating to the store,
  empty when none) — inject the service, not the store, mirroring how `ITraceService.GetAsync` wraps its store.
- **`IDbConnectionFactory.OpenAsync` (Part 8 · R12)** — an async open added NOW (as a default-interface
  method delegating to `Open()`, so it's non-breaking for existing implementers) because adding it to the
  interface after 1.0 would break every implementer. The built-in SQLite + Postgres factories override it
  with a genuinely async open (over the driver's `OpenAsync` + pragmas), so a store can stop blocking a
  threadpool thread on connect — matters most for the networked/pooled Postgres backend.
- **Public front-door decorator seam (Part 8 · R11)** — `LyntaiBuilder.AddFrontDoorDecorator(order, factory)`
  is now public, so an app can fold its OWN cross-cutting `ILlmClient` decorator (PII redaction, request
  logging, a bespoke cache) over the base client along the SAME ordered chain as the built-in governance
  decorators — instead of pre-registering a whole `ILlmClient` (which trips the governance guard). The
  built-in fold orders are exposed as public consts (`RateLimitDecoratorOrder`=5, `BudgetDecoratorOrder`=10,
  `CacheDecoratorOrder`=20) so a custom decorator can position relative to them.
- **`LlmVerdict.Unsupported` (Part 8 · R9)** — a distinct verdict for a capability/transport gap (e.g. a
  native tool call that streaming can't carry — use `CompleteAsync`), previously overloaded onto `Refused`.
  It surfaces like `Refused` (no fallback/cooldown — another candidate has the same limitation; mapped to
  `FallbackAction.Surface` in the default `RoutingPolicy`), but is distinct so telemetry/scorers don't
  conflate a capability gap with a content-policy refusal. The OpenAI-compatible + MEAI streaming providers
  now emit it for the deferred stream-native-tool-calls case.

### Fixed
- **More low-priority nits (Part 8 · R21b)** — the agentic tool loop's native path now preserves any prose
  the model emitted alongside its tool calls (`LlmMessage.AssistantToolCalls` carries content) instead of
  dropping it; added a reflection guard test asserting every `LlmRequest` field is either hashed into the
  response-cache key or consciously excluded (catches a future field that would cause silent cache
  collisions); documented that `ISecretAccessPolicy` gates reads only (writes/enumeration are the
  admin/provisioning path — wrap the vault to gate them).
- **Low-priority nits batch (Part 8 · R21, partial)** — `InMemoryJobStore.ListAsync` now uses an ordinal
  `Id.ToString()` tiebreak to match the SQL stores' TEXT ordering (was `Guid.CompareTo`); `OutcomeScorer`'s
  magic `Extra["error"]` key is exposed as `OutcomeScorer.ErrorKey`; the `LlmScorerBase` judge SYSTEM
  preamble is now an overridable `JudgeSystemPrompt` (was a hardcoded English literal); and doc gaps closed
  (`CompleteJsonAsync` retry double-charges/never cache-hits; `ClaudeCommand.Tokenize` is double-quote-only).
  The more involved nits are tracked as a follow-up (R21b).
- **Curated-list ordering parity across backends (Part 8 · R19)** — the Postgres curated `ListAsync` sorted
  `kind` under the DB locale collation while SQLite uses its default BINARY (byte-ordinal), so the two could
  order differently. Postgres now `ORDER BY kind COLLATE "C"` (byte-ordinal) to match. The memory-recall
  ordering (SQLite bm25 relevance vs Postgres/InMemory recency) is an inherent semantic divergence — kept
  documented + asserted-divergent via the R5 backend-specific tests rather than forced to converge.
- **ClaudeCli warns instead of silently dropping `LlmRequest.Tools` (Part 8 · R14)** — the CLI provider
  doesn't consume request-level tool declarations (`SupportsToolCalls`=false; tool-calling goes through the
  MCP provisioner), so a caller that put tools on the request + routed to `claude-cli` had them dropped with
  no diagnostic. It now logs a warning naming the count + the correct path (the ClaudeCli.Mcp provisioner).
- **Envelope vault zeroizes the unwrapped DEK (Part 8 · R13, crypto)** — `EnvelopeSecretVault` unwrapped the
  master DEK and handed it to `AesGcmSecretProtector` (which clones it) but never scrubbed its own copy, so
  the plaintext key lingered on the managed heap until GC (the transient recovery KEK was already zeroed —
  this closes the same window for the longer-lived DEK). It now `CryptographicOperations.ZeroMemory`s the
  DEK at the single `BuildInner` choke point every unwrap path funnels through.
- **Verdict classifier: extensible + reaches `ContextWindowExceeded` on typed exceptions (Part 8 · R8)** —
  `LlmVerdictClassifier.FromException` now scans the full inner-exception chain, so a typed provider
  exception (e.g. an MEAI "prompt too long") that wraps the real detail in an inner exception classifies as
  `ContextWindowExceeded` (was flattened to `Failed`, defeating the big-context fallback). Added a
  consumer-extensibility seam `AddErrorTextMatcher(Func<string, LlmVerdict?>)` (returns a disposable
  registration) consulted before the built-in English patterns — so an app can teach the classifier a
  non-English provider's phrasing or a bespoke error code without editing Core.
- **SQLite memory dedup is now atomic (Part 8 · R6)** — `SqliteMemoryStore.RememberAsync` did
  UPDATE-then-INSERT with no unique constraint, so two concurrent `RememberAsync` of the same
  `(task, scope, content)` could both fall through the UPDATE and INSERT duplicate rows. Added a
  `UNIQUE(task_key, scope, content)` index (`ux_lyntai_memory_dedup`, replacing the non-unique
  `(task_key, scope)` index whose prefix it subsumes) and switched to `INSERT … ON CONFLICT DO UPDATE` —
  matching `PostgresMemoryStore`'s atomic upsert. The AFTER UPDATE trigger keeps the FTS index in sync.
- **Response-gate `Replace` redacts the whole reply (Part 8 · R3, security)** — the output gate scans a
  reply's `Text` + `Detail` + `ToolCalls`, but a `Replace` outcome only rewrote `Text`, leaving denied
  content in `ToolCalls`/`Detail` to pass through un-redacted (`GuardedLlmClient` and the rail's re-threading
  to later guards). A response `Replace` now also clears `ToolCalls` and `Detail` — the replacement text is
  the whole sanitized reply.
- **Guards now cover the agent tool loop (Part 8 · R2, security)** — `ToolLoop` gated nothing: only the
  chat orchestrator's initial user message + final answer passed the rail, so with `UseTools` on, a denied
  term in a model-emitted tool call's `ArgumentsJson`, or an exfil through a tool observation, bypassed the
  jail. `IGuardRail` gains `InspectToolCallAsync` / `InspectToolResultAsync` (default methods that reuse the
  existing request/response guards — no new per-guard surface), and `ToolLoop` gates each tool call's args
  (before execute) + observation (before feeding it back). A guard Block aborts the loop with a `Refused`
  verdict; a Replace rewrites the args / redacts the observation. Wired via DI; no guards registered → no-op.
- **BYO storage impl now actually wins (Part 8 · R1)** — the SQLite and Postgres storage packages registered
  every domain store with plain `AddSingleton`, so an app that registered its OWN impl BEFORE
  `UseSqliteStorage`/`UsePostgresStorage` was silently clobbered — contradicting the README's "anything you
  register wins (defaults use `TryAdd`)". The domain-store registrations now use `TryAddSingleton` (matching
  `Lyntai.Storage.InMemory`), so a pre-registered app impl wins regardless of call order — the BYO-backend
  escape hatch the app-owned-storage design (P3) relies on.

## 0.28.5 — 2026-07-19

Part 6 — the agentic **self-driving-agent session** primitive (0.28.2–0.28.4 were the cortex/scoring
adoption tail + patch re-releases; consolidated here).

### Added
- **Agentic self-driving-agent session** — a generic primitive for gating an agent that drives its OWN
  tool loop out-of-process (the `claude` CLI now; a future Codex/Gemini-CLI/OpenAI-Responses adapter
  reuses the surface unchanged), distinct from `IToolLoop` (where Lyntai drives the loop). Neutral surface
  in Core `Lyntai.Agents`: `IAgentSession.StreamAsync` yielding an `AgentStreamEvent` transcript
  (`SessionStarted`/`TextDelta`/`Thinking`/`ToolCall`/`ToolResult`/`UsageLive`/`UsageFinal`/`SessionEnded`),
  an `AgentToolPolicy` (ReadOnly plan gate vs Write execute gate), an opaque `ResumeToken` (resume across a
  human gate), and `AgentSessionOptions`/`AgentSessionResult`. **Both consumption doors:**
  `StreamAsync` (live transcript) and the `RunAsync(onEvent)` extension (folds to `AgentSessionResult`),
  mirroring `ILlmProvider.StreamAsync`/`CompleteAsync`. The `claude` adapter
  (`Lyntai.Providers.ClaudeCli`): `ClaudeAgentSession` + `ClaudeAgentOptions` (`--settings` scope-guard
  hooks, `--mcp-config`/`--allowedTools` for an app-hosted MCP server, read-only/write tool policy,
  `--resume`), `ClaudeAgentArgs`, `ClaudeToolCalls.FilePathOf`, and `AddClaudeCliAgentSession()`. Prompt
  over stdin only; diagnosable termination (a no-output run is never silent). Design:
  `local/superpowers/specs/2026-07-19-agent-session-design.md`.
- **`LyntaiOptions.ResolveTimeout(int?)`** — a per-call-seconds timeout overload (value clamped to
  `MaxProviderTimeout`, else the global `ProviderTimeout`), shared by the request path and the agent
  session.
- **`LlmScorerBase` applicability skip hook** (0.28.3) — a scorer can opt out of a given context.

## 0.28.1 — 2026-07-18

Consumer-driven adoption gaps — makes Lyntai's **cortex + scoring** genuinely adoptable (a real app can
retire its own scoring framework + model tuning with no regression) and adds a **per-request timeout**.
Additive only (new overloads / opt-in / default-interface members) — no breaking change.

### Added
- **Per-request timeout override** — `LlmRequest.TimeoutSeconds` (+ a per-consumer
  `LyntaiOptions.TimeoutByConsumer` map) let one long call — e.g. a CLI-agent run driving many steps —
  carry a bigger budget without inflating every short call. `LyntaiOptions.ResolveTimeout` (request >
  consumer-map > "default" > global `ProviderTimeout`), clamped to `MaxProviderTimeout`
  (env `LYNTAI_MAX_TIMEOUT_SECONDS`); honored by all four providers.
- **Score store: upsert + aggregate + export** (`IScoreStore`) — `SaveAsync` now UPSERTs on
  `(session, scorer)` (re-scoring replaces, not accumulates; new `UNIQUE` merged into the score
  migration), plus `AggregateAsync` (per-scorer AVG+COUNT → `ScorerAggregate`) and `ExportAsync`
  (flat `(session, scorer, score)` dump → `ScoreExportRow`) for the eval dashboard + tuning datasets.
- **Dry-run scoring** — `IScoringService.EvaluateAsync(ctx, persist: false)` scores without writing rows
  even when a store is wired (a preview/tuning path).
- **Per-scorer judge model** — `LlmScorerBase` exposes overridable `Model` + `Consumer`, so a cheap judge
  can route to a cheap model per scorer (was hardcoded to the default + `"scoring"`).
- **Applicability skip on `LlmScorerBase`** — a `protected virtual bool Applies(ScoreContext)` (default
  true) checked before the judge call, so a conditional judge (e.g. "faithfulness" applies to a plan, not a
  code-edit turn) returns null WITHOUT spending tokens instead of scoring every context.
- **Live per-consumer model routing** — opt-in `AddLiveModelRouting()` registers an `IModelRoutingStore`
  (KV-backed) the router + cache read each call, so an admin model retune takes effect WITHOUT a restart
  (`lyntai.model.<consumer>`; precedence: explicit → live → configured default). `ResolveModel` gains a
  `liveOverride` overload.
- **Prompt-override validation** — `IPromptRegistry.ValidateOverride(default, candidate)` returns the
  `{placeholders}` a candidate would drop (empty = valid), so an admin save-flow rejects a bad override up
  front instead of relying on `RenderAsync`'s silent runtime fall-back.
- **`IScorer.Description`** (optional default-interface member) for an admin "list scorers" view; documented
  the `ScoreContext.Extra` domain-dimension pattern.

## 0.28.0 — 2026-07-18

Adds a **portable, recoverable secret vault** and **job admission control / pause**, and lands the
review-follow-up backlog (a second multi-agent code review). New package **`Lyntai.Secrets.Dpapi`**;
public-surface additions to `Lyntai.Core` (envelope vault, `IJobAdmissionController`, `JobStatus.Paused`,
live job progress, curated memory) and the three storage backends (`PauseAsync`/`ResumeAsync`, progress
reporting, `ICuratedMemoryStore`) — see below.

### Added
- **DEK-envelope secret vault** (`Lyntai.Core`) — a Lyntai-managed data-encryption key instead of a BYO
  key. `SecretKeyEnvelope` generates a random 256-bit DEK that all secrets are AES-256-GCM encrypted
  under, and wraps the DEK **two ways**: a *machine wrap* (sealed by an injected `ISecretProtector` — the
  fast path, no passphrase on the same host) and a *recovery wrap* (a KEK derived PBKDF2-SHA256 from a
  one-time recovery key — the portability path). `EnvelopeSecretVault` drives the lifecycle:
  `GenerateMasterKeyAsync()` (once, returns the recovery key to record out-of-band), auto-init via the
  machine wrap, and `RecoverAsync(recoveryKey)` on a new host (re-binds the DEK, so later reads take the
  fast path). A machine that can't unseal the machine wrap throws `SecretRecoveryRequiredException` until
  recovered. Wire with `builder.AddEnvelopeSecretVault(machineProtector)`.
- **`Lyntai.Secrets.Dpapi`** (new package) — `DpapiSecretProtector` (Windows DPAPI via
  `System.Security.Cryptography.ProtectedData`, user- or machine-scoped, optional entropy) and
  `builder.AddDpapiSecretVault(...)`, which wires the envelope vault with DPAPI as the machine-binding
  protector: secrets sealed to this Windows host at rest, recoverable off-machine via the recovery key.
  Windows-only at runtime (guarded with a clear `PlatformNotSupportedException`); the envelope crypto
  stays portable in Core, so non-Windows hosts use an AES-GCM protector via `AddEnvelopeSecretVault`.
- **Job admission control + `Paused` state** — `IJobAdmissionController` (default admit-all) is consulted
  by the runner per lane *before* it claims, so an app can throttle lanes by external signals (GPU/CPU
  load, a maintenance window) without Lyntai knowing about them; a held lane's jobs stay Pending (a throw
  is treated as "hold"). Register with `AddJobAdmissionController`. Separately, `JobStatus.Paused` with
  `IJobQueue`/`IJobStore` `PauseAsync`/`ResumeAsync` administratively holds a single Pending job out of the
  claimable set (no schema change — status is TEXT) across all three backends.
- **Live job progress + step reporting** — `JobContext.ReportProgressAsync(done, total, stage)` and
  `ReportStepAsync(message)` let a handler surface live status a UI can read WHILE the job runs (new
  `JobRecord.Progress`/`Total`/`Stage`/`StepLog` + `IJobStore.ReportProgressAsync`/`ReportStepAsync`,
  fenced by the worker id, not lease renewals). The step log is a capped JSON array (`JobStepLog.Parse`/
  `Append` → `JobStep`); `JobContext` also exposes the prior snapshot (`Progress`/`Steps`/…) so a resumed
  handler sees what it already reported. New `lyntai_job` columns folded into the jobs migration
  (pre-release; SQLite + Postgres). InMemory mirrors it.
- **Per-request refusal pattern** — `LlmRequest.RefusalPattern` (a case-insensitive regex) surfaces an
  otherwise-`Ok` reply whose text matches as `Refused` (no fallback) — a caller-supplied check (e.g. a
  per-language "I can't help") on top of the central patterns. Applied by `RefusalScreeningLlmClient`, the
  always-on OUTERMOST front-door layer, so a cached hit is re-screened too; malformed patterns fail open.
- **Curated memory catalog** — `ICuratedMemoryStore` (across InMemory/SQLite/Postgres): a hand-managed
  catalog of `CuratedMemory` entries grouped by `Kind`, each individually enable/disable-able (`Enabled`)
  and editable (`UpdateAsync` with COALESCE semantics) with a `Source` note — distinct from the automatic,
  bounded, dedup/TTL remember/recall *log* (`IMemoryStore`). `CuratedMemorySections.Compose` renders the
  enabled entries into per-kind prompt sections. New `lyntai_curated_memory` table (migration
  `202607180003`, SQLite + Postgres).

### Documented (Sonora-adoption recipes)
- The **"rate-limit → surface"** recipe for single-provider adopters
  (`ConfigureRouting(p => p.On(RateLimited, Surface))` + the `ExemptSoleCandidate` note) — knowledge doc +
  README.

### Fixed
- **Security — denylist guard bypassed via tool calls/attachments** — `DenylistGuard` scanned only message
  text, so a jailed term in a tool-call name/arguments or an attachment URI slipped through. It now scans
  tool-call segments and attachment URIs on the request, and `reply.ToolCalls` on the response.
- **Durable-job poison-pill unbounded on crash-before-run** — a job whose attempts already exceeded
  `MaxAttempts` (e.g. repeatedly claimed then crashed) is now dead-lettered at claim time instead of
  running again.
- **Response cache collided across models** — `ResponseCacheKey` now folds the *effective* (resolved)
  model, so the same request routed to different models no longer serves a cross-model cached reply.
- **Usage-tracker consumer key was case-insensitive** — `InMemoryUsageTracker` now keys consumers
  ordinally (case-sensitive), matching the SQL trackers, so `"App"` and `"app"` bill separately.
- **Semantic recall now fails open** — if the vector backend throws mid-recall, `SemanticMemory.RecallAsync`
  logs and returns no hits (caller cancellation still propagates) rather than failing the whole request.
- **Router misclassified a provider's own cancellation** — a provider that throws
  `OperationCanceledException` for its *own* reasons mid-stream now falls over to the next candidate
  instead of aborting the request; only the caller's cancellation still propagates.
- **In-memory job-claim tiebreaker diverged from SQL** — `InMemoryJobStore` breaks a same-priority,
  same-`available_at` tie by the id string (ordinal), matching the SQL stores' `ORDER BY …, id`.

### Documented
- The soft budget cap's **concurrency overshoot bound** (in-flight calls, not "one past"), the job
  scheduler's **at-least-once** enqueue-then-advance window, the required **constant-time compare** for
  secret/token equality in an `ISecretAccessPolicy`, and the cross-backend memory-recall divergence.
  Corrected the 0.27.1 dimension-mismatch note (in-memory scores 0 since 0.27.2, only Postgres throws).

### Hardening (round-2 review of the new surface)
- **Concurrent step-log reports could lose a step on the SQL job stores** — `ReportStepAsync` is a
  read-modify-write on `step_log`; two concurrent reports from one handler could interleave (InMemory was
  already safe under its lock). Serialized with a per-store lock on both SQL backends.
- **Secret-envelope KDF downgrade / non-crypto exception** — the recovery PBKDF2 iteration count was honored
  from the (possibly tampered) envelope unbounded, and `0` threw `ArgumentOutOfRangeException`. Added a hard
  `MinRecoveryIterations = 100_000` floor enforced at load and at the KDF, as a `CryptographicException`.
- **Envelope `version` was written but never enforced** — a future format opened by this build now throws
  instead of silently misparsing; the transient recovery KEK is zeroed after use.
- **Config + polish** — `JobOptions.MaxStepLog` (env `LYNTAI_JOBS_MAX_STEP_LOG`) makes the step-log cap
  configurable; `CompleteJsonAsync` reuses `JsonExtract.IsValid`; documented that `ICuratedMemoryStore.ListAsync`
  kind-ordering isn't ordinal-stable across backends (the composed prompt re-sorts, so it's stable).

### Refactor (behavior-preserving)
- Consolidated the duplicated "extract a JSON object from an LLM reply, then parse it" scaffolding into
  `JsonExtract.TryParseObject`/`IsValid`; split the ~100-line `AddLyntai` composition root into one focused
  helper per feature area. No behavior change (only the two new `JsonExtract` methods touch the surface).

## 0.27.2 — 2026-07-18

Follow-up hardening from a full-codebase multi-agent code review (45 agents; 35 candidates, 19 refuted by
the review's own verifier). No public API change.

### Fixed
- **Streamed native tool call misclassified as a host failure** — a `StreamAsync` response that was a tool
  call (no text) ended as `Failed` in both the OpenAI-compatible provider (`finish_reason=tool_calls`) and
  the MEAI bridge (`FunctionCallContent`), which cooled down a perfectly healthy host. It now surfaces
  `Refused` (no fallback/cooldown) pointing the caller at `CompleteAsync`. (Full streaming tool-call
  *delivery* stays deferred — the `LlmChunk` streaming contract carries no tool-call payload.)
- **Secret vault: a corrupt/truncated at-rest blob now fails as `CryptographicException`** — `Unprotect`
  used to leak a `FormatException`/`ArgumentOutOfRangeException` from base64 parsing or span slicing;
  callers can now catch one exception type for all at-rest corruption (base64, too-short, or tampered).

### Changed
- **`InMemoryVectorStore` tolerates a dimension mismatch (scores it 0) instead of throwing** — so
  `IVectorStore` behaves consistently across the in-memory / SQLite backends (a stray wrong-dimension row,
  e.g. from a prior embedding model, ranks last rather than sinking the whole search).
- **`DenylistGuard` scans each message directly** (short-circuiting on the first hit) instead of
  allocating a whole-transcript join per request — cheaper on long tool-loop transcripts.

### Reviewed, kept by design
- Incrementing a job's attempt count on a stale-lease reclaim is deliberate poison-pill protection (a
  crash-looping job is bounded by `MaxAttempts`); long handlers renew the lease by checkpointing. The
  per-job cancel poll, the tool-loop's defensive message snapshot, and a few small duplicated helpers were
  judged not worth the churn/risk.

## 0.27.1 — 2026-07-18

Consolidation / hardening pass over v0.16–v0.27 (a three-way adversarial review). No public API change —
all fixes are internal/behavioral.

### Fixed
- **Rate limiter: a cancelled wait now refunds its reserved permit** — a caller that bailed during
  `await Task.Delay(wait)` used to leave the bucket decremented, so a burst of cancellations throttled
  legitimate callers for slots no request ever used.
- **Postgres vector store: a faulted lazy schema no longer bricks the store** — a transient failure on the
  first `CREATE EXTENSION`/`CREATE TABLE` was cached forever (every later call re-threw). It now retries on
  the next call.
- **Cron: inverted/empty ranges are rejected** — `5-3`, `70/5`, `10-40` (out-of-range) parsed cleanly and
  produced a schedule that silently never fired (slipping past `AddCronSchedule`'s eager validation). They
  now throw `FormatException`.
- **Scheduler: an impossible-but-parseable cron (e.g. Feb 30) is quarantined per-schedule** — its
  `Next()` throw no longer aborts the whole tick (skipping later schedules) or spins every poll.
- **Front-door decorators are idempotent** — calling `AddResponseCache`/`AddUsageBudget`/`AddRateLimit`
  twice no longer stacks a second decorator (two rate limiters sharing the singleton would double-charge
  permits).
- **A pre-registered `ILlmClient` + a decorator now throws at composition** instead of silently dropping
  cache/budget/rate-limit governance.
- Postgres response-cache eviction gained a `cache_key` tiebreaker (deterministic trim, matching SQLite);
  `SemanticMemory`'s task+scope separator is now a plain-ASCII `(char)0x1f` constant (was a raw control
  byte in the source); scheduler caches are `ConcurrentDictionary`.

### Known edge cases (documented, not fixed)
- The **pgvector** store can surface a DB error for a **zero-magnitude or non-finite embedding vector**
  (pgvector's `<=>`/parser reject them), where the brute-force in-memory/SQLite stores return a 0 score.
  Real embedders don't emit these for non-empty text. A **dimension mismatch** (e.g. after changing
  embedding models without reindexing) throws in the Postgres store; the in-memory and SQLite stores
  tolerate it (score 0 — in-memory was changed to tolerate in 0.27.2, see above). Either way, reindex on
  a model change.

## 0.27.0 — 2026-07-18

Running-job cancellation — a job that's currently executing can now be stopped (before, only Pending jobs
cancelled). Cooperative: a cancel request sets a flag the runner polls; it cancels the handler's token, and
a handler that honors the token stops. Across all three backends.

### Added
- **`IJobQueue.CancelAsync(id)`** — the single front-door cancel: a Pending job is cancelled outright, a
  Running one has cancellation *requested*.
- **`IJobStore.RequestCancelAsync`** (flag a Running job) + **`CancelRunningAsync`** (the runner marks it
  Cancelled, fenced by worker); **`JobRecord.CancelRequested`**. The runner links a per-job token, polls the
  store (`Jobs.PollInterval`) for the flag, and on seeing it cancels the handler's token → the handler
  stops → the job becomes Cancelled. A cancel already set on a reclaimed (stale-lease) job is honored
  without re-running it. `Replay` clears the flag.

### Breaking (pre-1.0)
- `JobRecord` gains a trailing optional `CancelRequested`; `IJobStore` gains `RequestCancelAsync` /
  `CancelRunningAsync` (custom implementers must add them). The `cancel_requested` column was folded into
  the Jobs migration (pre-release consolidation) — no new migration.

### Notes
- Cancellation is cooperative — a handler must honor its `CancellationToken` to actually stop. Latency is
  up to one `Jobs.PollInterval`.

## 0.26.0 — 2026-07-18

Cron expressions for job schedules — recurring jobs can now run on a real cron schedule, not just a fixed
interval. Dependency-free (a hand-rolled 5-field parser; no cron NuGet pulled into Core).

### Added
- **`AddCronSchedule(name, lane, type, payload, cron, priority)`** — schedule a job on a cron expression.
  The expression is validated at composition (a bad one throws in `AddLyntai`, not silently at tick time).
- **`CronExpression`** (`Parse` / `Next`) — a 5-field UTC cron: `*`, values, ranges `a-b`, steps `*/n` /
  `a-b/n` / `n/step`, comma lists, day-of-week 0–6 (Sunday=0 or 7), the standard day-of-month/day-of-week
  OR rule, and the macros `@hourly @daily @midnight @weekly @monthly @yearly/@annually`.
- `JobSchedule` now carries `Cron` (alongside `Interval`); the scheduler uses the cron's next occurrence
  when set — missed slots still coalesce (the cron's next-after-now skips them).

### Breaking (pre-1.0)
- `JobSchedule.Interval` is now `TimeSpan?` (was `TimeSpan`) and a trailing `Cron` field was added — set
  exactly one of interval/cron. Source-compatible for the existing interval overloads; the positional
  ctor/deconstruct arity changed. Both are normally built via the builder methods.

## 0.25.0 — 2026-07-18

Recurring job scheduling — the last big v0.14-deferred job feature. Register an interval schedule and the
scheduler enqueues a job every interval, durably.

### Added
- **`AddJobSchedule(name, lane, type, payload, every, priority)`** (and a `JobSchedule` overload) — a
  recurring job. **`IJobScheduler`** drives it: `TickAsync` enqueues the due schedules and advances them;
  `RunAsync` loops on `Jobs.PollInterval`. The app owns the pump (host-free), same as the runner.
- Next-run time is **persisted via the key-value store** (keyed by schedule name), so a restart resumes the
  cadence instead of re-anchoring; with no `IKeyValueStore` wired it falls back to in-memory. No new storage
  domain / migration — it reuses `IKeyValueStore`.
- **Missed slots coalesce** into a single enqueue (a ticker that was down doesn't replay a burst); the first
  run waits one interval (no fire-on-startup); a non-positive interval is skipped, not spun.

### Notes
- Interval-based for now (cron expressions are a future enhancement — they'd need a cron parser). Scheduling
  requires durable jobs (the queue throws without a storage backend).

## 0.24.0 — 2026-07-18

Durable-job priorities + a dead-letter queue — two of the deferred v0.14 job features. Across all three
backends (InMemory / SQLite / Postgres), pinned by the shared store contract.

### Added
- **Priorities** — `JobSpec.Priority` (and `IJobQueue.EnqueueAsync(lane, type, payload, priority)`). The
  claim now picks by `priority DESC, available_at, id` — higher runs first within a lane. The claim index
  is recreated to lead with priority (migration `M202607180003`).
- **Dead-letter queue** — exhausted transient retries now go to a new terminal `JobStatus.Dead` (instead of
  a silent `Failed`), which is **inspectable and replayable**: `IJobStore.DeadLetterAsync` /
  `ReplayAsync`, surfaced on the front door as `IJobQueue.ListDeadAsync` / `ReplayAsync`. `Replay` requeues
  a Dead (or Failed) job — Pending, attempts reset, error cleared, available now. The runner dead-letters
  on exhaustion (telemetry outcome `dead`, Error span status); an explicit `JobOutcome.Fail` still → `Failed`.

### Breaking (pre-1.0)
- `JobStatus` gains `Dead`; retries-exhausted jobs are now `Dead`, not `Failed` (an explicit `Fail`
  outcome and the no-handler path stay `Failed`). `JobSpec`/`JobRecord` gain a trailing optional
  `Priority` (source-compatible; positional deconstruct/ctor arity changed). `IJobStore` gains
  `DeadLetterAsync`/`ReplayAsync` (custom implementers must add them).

## 0.23.0 — 2026-07-18

Postgres backends for the governance + semantic-memory seams, mirroring v0.22's SQLite ones — and the
vector store uses **pgvector** so similarity search runs in the database, not brute-force in the app.

### Added (`Lyntai.Storage.Postgres`)
- **`UsePostgresResponseCache()`** — `PostgresResponseCache` (`IResponseCache`): reply JSON + `timestamptz`
  expiry, eviction on write. Persistent and shareable across processes on the same db.
- **`UsePostgresUsageTracking()`** — `PostgresUsageTracker` (`IUsageTracker`): per-consumer rows,
  incremented in place; global total is a `SUM`.
- **`UsePostgresVectorStore()`** — `PostgresVectorStore` (`IVectorStore`) over **pgvector**: the cosine
  `<=>` operator + SQL `ORDER BY … LIMIT k` do the top-k in the database (only the k nearest rows come
  back, vs. loading a whole collection into the app). `ISemanticMemory` persists unchanged.
- Migration `M202607180002_Governance` adds `lyntai_response_cache` / `lyntai_usage`.

### Notes
- **`UsePostgresStorage` does NOT require pgvector.** The vector store creates its `vector` extension +
  `lyntai_vector` table LAZILY on first use (needs rights to `CREATE EXTENSION vector`, or a DBA enabling
  it once) — so only `UsePostgresVectorStore` pulls in pgvector, not the whole storage layer.
- The pgvector column is an unbounded `vector` (dimension-agnostic) and unindexed — the search is exact (a
  sequential scan with pgvector's operator, SQL-side top-k). An ANN index (hnsw/ivfflat, needs a fixed
  embedding dimension) is a future enhancement.
- The Postgres test container image is now `pgvector/pgvector:pg16` (a superset of postgres:16) so the
  vector store's live tests run; all other Postgres tests are unchanged. Tests skip without Docker.

## 0.22.0 — 2026-07-18

Persistent SQLite backends for the governance + semantic-memory seams. The cache, usage tracker, and
vector store shipped with in-memory defaults (in Core); this backs them with SQLite so they survive a
restart — all behind the same interfaces, opt-in, no change to the decorators or `ISemanticMemory`.

### Added (`Lyntai.Storage.Sqlite`)
- **`UseSqliteResponseCache()`** — `SqliteResponseCache` (`IResponseCache`): reply JSON + expiry, eviction
  on write (prune expired, then trim oldest beyond `MaxEntries`). The cache survives restarts.
- **`UseSqliteUsageTracking()`** — `SqliteUsageTracker` (`IUsageTracker`): one row per consumer,
  incremented in place; the global total is a `SUM` across rows — so a usage budget isn't reset every
  deploy.
- **`UseSqliteVectorStore()`** — `SqliteVectorStore` (`IVectorStore`): persistent semantic-memory vectors
  (JSON float arrays), brute-force exact cosine loaded per collection. Plug it in and `ISemanticMemory`
  persists unchanged.
- Migration `M202607180002_Governance` adds `lyntai_response_cache` / `lyntai_usage` / `lyntai_vector`.

### Notes
- These `AddSingleton` over the Core in-memory `TryAdd` defaults (win regardless of call order). Each needs
  the connection factory + schema from `UseSqliteStorage`, so call that first.
- The SQLite vector store is brute-force (not indexed) — persistent and fine to some thousands of vectors
  per collection; a dedicated vector backend (pgvector) is the path for larger corpora. Rate limiting stays
  in-memory by design (a shared limiter is a distributed-cache concern, not SQLite) — its `IRateLimiter`
  seam remains the extension point.

## 0.21.0 — 2026-07-18

Client-side rate limiting — the third front-door governance decorator, completing the trio with response
caching (cost/latency) and usage budgeting (spend): cache · budget · **rate limit** (throughput). All
compose on the same ordered decorator chain.

### Added
- **`AddRateLimit([configure])`** — throttles front-door calls with a token bucket. Over the rate a call
  waits up to `MaxWait`, then is refused (a `RateLimited` reply / an Error stream chunk) without hitting a
  provider. Global rate via `RateLimitOptions` (`PermitsPerSecond` / `Burst` / `MaxWait`) with optional
  per-consumer rates (`ConsumerRate`); also `LYNTAI_RATELIMIT_PERMITS_PER_SECOND` / `_BURST` /
  `_MAX_WAIT_SECONDS`.
- **`IRateLimiter`** (the seam) with the built-in **`TokenBucketRateLimiter`** (continuous refill,
  reservation-based waits, injectable clock). Register your own before `AddRateLimit` for a
  distributed/shared limiter.
- A `lyntai.ratelimit.refusals` counter (tagged by consumer) on the `Lyntai.Agents` meter.

### Composition
- Fold order is now **cache (outer) → budget → rate-limit (inner) → client**, so a **cached hit spends
  nothing** — no budget accounting and no rate-limit permit; the rate limiter throttles only real provider
  calls. Order is deterministic regardless of the order the decorators were added.

## 0.20.0 — 2026-07-18

Semantic memory is now wired into the chat path — the composer and orchestrator use it automatically when
embeddings are registered, closing the "opt-in only" gap from v0.19.

### Changed
- **Hybrid memory recall in `MemoryPromptComposer`** — when an `ISemanticMemory` is present (embeddings
  registered), the composer leads the "Learned facts" section with meaning-based hits, then fills in
  lexical `IMemoryStore` entries, deduped by content and bounded by the same char budget. Fail-open across
  both sources: an outage in either yields whatever the other returned. Lexical-only behavior is unchanged
  when no embedder is wired.
- **`ChatOrchestrator` dual-writes memory** — a remembered exchange is written to both the lexical store
  and semantic memory (when wired), so the next turn's hybrid recall can find it by meaning. Both writes
  are fail-open.
- **Semantic memory is registered only when an embedder is** (`AddEmbeddings`) — absent one, `ISemanticMemory`
  isn't in the container, so the composer/orchestrator resolve null and skip it cleanly (no per-turn throws).

### Breaking (pre-1.0)
- `MemoryPromptComposer` and `ChatOrchestrator` constructors take an added optional `ISemanticMemory?`
  parameter (source-compatible for named/DI use; binary signature changed). Both are normally DI-resolved.

## 0.19.0 — 2026-07-18

Semantic memory — meaning-based recall to complement the lexical memory store. Facts are remembered by
their embedding and recalled by cosine similarity to a query, so retrieval finds relevant memories even
without keyword overlap. Consistent with Lyntai's shape: the app brings the embedding model, Lyntai owns
the recall machinery, and the vector backend is a swappable seam.

### Added
- **`IEmbedder`** (`Lyntai.Embeddings`) — the app-provided embedding model (BYO: an OpenAI/Ollama
  embeddings endpoint, a local model, …), a batch `EmbedAsync` primitive + a single-text convenience.
  Registered with **`builder.AddEmbeddings(...)`**.
- **`ISemanticMemory`** (`Lyntai.Memory`) — `RememberAsync` / `RecallAsync(…, k, minScore)` / `ForgetAsync`,
  scoped by (taskKey, scope) like the lexical store; re-remembering identical content dedups. Auto-wired
  when an embedder is registered; a call throws a clear error if none is.
- **`IVectorStore`** (the vector-persistence seam) with the built-in brute-force **`InMemoryVectorStore`**
  (exact cosine, zero-dependency). Register your own before `AddLyntai` to back recall with pgvector /
  sqlite-vec / a vector DB — the recall logic is unchanged.

### Notes
- The in-memory vector store is exact (brute-force) — fine for up to some thousands of entries per scope;
  for larger corpora or persistence across restarts, plug in a real vector backend via `IVectorStore`.
- First cut: no per-entry TTL on semantic memory (the lexical store keeps that); `ForgetAsync` clears a
  whole scope. Composer/orchestrator integration stays opt-in (call `ISemanticMemory` directly) for now.

## 0.18.0 — 2026-07-18

Usage budgeting — cost/token governance on the front door, the natural companion to the response cache.
Meters spend and refuses further calls once a cap is reached. Same shape as caching: a decorator over the
single `ILlmClient` front door with a swappable accounting seam.

### Added
- **`AddUsageBudget([configure])`** — registers the built-in `InMemoryUsageTracker` and decorates the
  front door with a `BudgetedLlmClient` that records each call's usage and refuses (a `Refused` reply / an
  Error stream chunk, **without** hitting a provider) once the applicable total reaches a cap. Global caps
  via `BudgetOptions` (`MaxCostUsd` / `MaxTokens`) with optional per-consumer overrides
  (`ConsumerBudget`); also `LYNTAI_BUDGET_MAX_COST_USD` / `LYNTAI_BUDGET_MAX_TOKENS`.
- **`IUsageTracker`** (the seam) — accumulates token/cost totals per consumer + globally; query spend
  (`Total`) or reset at a billing-window boundary (`Reset`) at runtime. Register your own before
  `AddUsageBudget` for persistent/shared accounting. `UsageTotals` is the snapshot record.
- A `lyntai.budget.refusals` counter (tagged by the cap hit) on the `Lyntai.Agents` meter.

### Changed
- **Front-door decorators now compose.** The cache/budget decorators are folded over the base client in a
  deterministic order (cache **outermost**), so enabling both works correctly regardless of the order they
  were added — in particular a **cached hit is free and never counts toward the budget**. (Previously each
  decorator wrapped a fresh base client, so a second one would have clobbered the first.)

### Semantics
- The cap is a **soft ceiling**: the applicable total is checked *before* each call, so the call that
  crosses a cap still runs (its cost isn't known until it returns) and the *next* one is refused.

## 0.17.0 — 2026-07-18

Read-through response caching on the front door — an opt-in decorator that turns identical repeated
completions into a stored hit, cutting cost + latency and making repeated runs deterministic. On-brand:
it wraps the single `ILlmClient` front door, so the whole library (tool loop, orchestrator, scorers,
pairwise judge) reads through it once enabled, and the cache backend is a swappable seam.

### Added
- **`AddResponseCache([configure])`** — enables caching. Registers the built-in
  `InMemoryResponseCache` (size-bounded, per-entry TTL) and decorates the front door with a
  `CachingLlmClient`. Tunable via `CacheOptions` (`Ttl` default 1h, `MaxEntries` default 1000) or
  `LYNTAI_CACHE_TTL_SECONDS` / `LYNTAI_CACHE_MAX_ENTRIES`.
- **`IResponseCache`** (the seam) — register your own before `AddResponseCache` for a persistent or
  shared backend (Redis, a distributed KV); the front door caches through it transparently.
- **`ResponseCacheKey.For(req)`** — a stable, length-framed SHA-256 over the output-determining request
  fields (messages incl. tool calls / tool-result ids / attachments, model, max tokens, temperature, JSON
  schema). Deliberately excludes `Consumer` (a routing/telemetry tag) so two consumers share a hit.
- A `lyntai.cache.requests` counter (result `hit`/`miss`) on the `Lyntai.Agents` meter.

### Semantics
- Cached: only clean `Ok` non-streaming completions. **Never** cached: streaming (delivered live),
  requests carrying native `Tools` (the tool loop is stateful and its tools can side-effect), and non-Ok
  replies (a transient failure must not stick). A non-positive `Ttl` disables storing entirely.

## 0.16.0 — 2026-07-18

Observability for the agentic subsystems. The GenAI telemetry (v0.2) covered the LLM call path; this
extends the same OpenTelemetry-native surface to the tool loop, durable jobs, and guards, so an agent run
shows up end-to-end in one trace/metrics backend alongside the `chat` spans.

### Added
- **Agentic telemetry** — a second source/meter, `Lyntai.Agents` (constants
  `LyntaiDiagnostics.AgentActivitySourceName` / `AgentMeterName`), separate from the `Lyntai.Llm` GenAI
  one because these aren't `gen_ai.*` operations. Subscribe with `AddSource("Lyntai.Agents")` /
  `AddMeter("Lyntai.Agents")`. Emits:
  - `tool_loop` spans (tags: consumer, mode = `none`/`native`/`prompt`, step count; Error status on a
    non-Ok verdict) with a child `execute_tool <name>` span per tool call, plus a
    `lyntai.tool.invocations` counter tagged by tool name + error flag.
  - `run_job <type>` spans (tags: lane, type, id, attempt, outcome; Error status on `failed`/`lost_lease`)
    with a `lyntai.jobs.processed` counter (lane + outcome) and a `lyntai.job.duration` histogram (lane).
  - a `lyntai.guard.decisions` counter (gate `input`/`output`, guard name, result `block`/`replace`).
  Nothing is emitted unless a listener is attached — the overhead without observability wiring is a few
  null/`Enabled` checks, matching the GenAI surface.

### Samples / tests
- **Playground now exercises the tool loop** (registers an inline `echo` tool and runs `IToolLoop` over
  the deterministic stub's new `TOOL_DEMO` protocol path) and **subscribes both telemetry surfaces**
  in-process, printing what fired. The `p1` e2e asserts the loop converges via a tool call and that every
  GenAI + agentic span/metric emitted — so the instrumentation is covered end-to-end, not just in units.

## 0.15.1 — 2026-07-18

Correctness + security fixes from a three-pass adversarial review of the v0.14–v0.15 code (the AES-GCM
crypto was reviewed and confirmed correct — fresh per-call nonce, proper layout, tamper detection).

### Fixed
- **Secret vault index collision** — a secret named `__names__` mapped onto the vault's internal index
  key and could corrupt/poison the name list. The index now lives outside the secret-name namespace.
- **Denylist guard bypass** — it scanned only `user` messages, so a denied term in a system/assistant/
  tool message (e.g. a tool result fed back mid-loop) slipped through. It now scans every message and the
  reply's error `Detail` too.
- **Guard rail Replace didn't compose** — a later guard saw the original text, not an earlier guard's
  rewrite. Replacements are now re-threaded so each guard inspects the current effective text.
- **Guarded client skipped non-Ok replies** — an error reply's `Detail` (stderr/HTTP body, which can echo
  content) bypassed the output gate. Every reply is now gated.
- **Job runner lane starvation** — under a global `MaxConcurrency` smaller than the sum of lane limits,
  the first lane monopolized the cap and others starved. Claiming is now round-robin across lanes (with a
  rotating start), and `ActiveLanes` is ordered deterministically.
- **Vision edge cases** — an attachment with neither inline data nor a URL now throws instead of sending
  an empty image URL; attachments on a non-`user` role are dropped (OpenAI rejects them) rather than sent.
- **Orchestrator re-persisted redacted input** — when the input gate rewrote (redacted) the message, the
  memory write stored the *raw* original, re-injecting it on the next recall. It now stores the redacted
  text.

### Docs
- Clarified the deliberate boundaries: the chat orchestrator's two gates cover the turn's entry + final
  answer (tool-loop intermediate turns aren't individually gated — use `GuardedLlmClient` for that);
  `ISecretAccessPolicy` gates reads only; `MaxConcurrency` bounds the per-pass batch; streaming applies
  only the input gate.

## 0.15.0 — 2026-07-18

The rest of the design §9 platform kit, in one release: guards, two-gate chat orchestration, a secret
vault, and vision/multimodal. All additive, all in `Lyntai.Core`.

### Added
- **Scope-guard / jail hooks** (`Lyntai.Guards`) — `IGuard` inspects an outbound request and/or inbound
  reply and can Allow / Block / Replace; `IGuardRail` runs the registered guards (first-non-Allow wins).
  A `DenylistGuard` jails named terms; `GuardedLlmClient` wraps the front door to gate every completion.
  Register via `builder.AddGuard<T>()`.
- **Two-gate chat orchestration** (`IChatOrchestrator` in `Lyntai.Agents`) — one guarded chat turn:
  **input gate** (guards) → memory recall into the prompt → the model *via the tool loop* → **output gate**
  (guards) → remember the exchange. Composes the guard rail, `IPromptComposer`, the tool loop, and memory;
  fail-open around the two gates. Injectable as a batteries-included entry point.
- **Secret vault + access gate** (`Lyntai.Secrets`) — `ISecretVault` (get/set/delete/list), encrypted at
  rest by an `ISecretProtector` (`AesGcmSecretProtector` = AES-256-GCM with a BYO 32-byte key; tamper-
  detecting), backed by the registered `IKeyValueStore` (persistent) or in-memory. An optional
  `ISecretAccessPolicy` gates reads (denied → `UnauthorizedAccessException`). Wire via
  `builder.AddSecretVault(key, policy)`.
- **Vision / multimodal** — `LlmAttachment` (inline bytes or a URL + MIME type) on `LlmMessage.Attachments`,
  with `LlmMessage.UserWithImage(...)` / `UserWithImageUrl(...)`. The OpenAI-compatible provider renders
  them as `image_url` content parts and the MEAI bridge maps them to `DataContent`/`UriContent`; text-only
  providers ignore them.

## 0.14.0 — 2026-07-18

Durable jobs (design §9): lanes + checkpoint/resume, built for running many agents in parallel with
proper concurrency control. New storage domain across all three backends + a runner. Additive.

### Added
- **Durable job store** (`IJobStore` in `Lyntai.Storage`, over a `lyntai_job` table) — enqueue a job
  (lane, type, JSON payload), a runner claims and runs it, the handler checkpoints, and a job whose
  worker crashed is reclaimed and **resumed from its last checkpoint**. The claim is a single atomic
  statement per backend (`UPDATE … RETURNING` on SQLite under the WAL single-writer; `… FOR UPDATE SKIP
  LOCKED …` on Postgres), so multiple workers coordinate without double-claiming. Writes are fenced by
  worker id (a lost lease is abandoned, not clobbered). Backends: SQLite, Postgres, InMemory (the
  Postgres claim proven under real-container concurrency).
- **Runner + handler seam** (`Lyntai.Jobs`) — `IJobHandler` (app work, keyed by type via
  `builder.AddJobHandler<T>()`; the **at-least-once / idempotent-from-checkpoint** contract is in its
  doc), `JobContext` (payload + checkpoint + `SaveCheckpointAsync`, which renews the lease), `JobOutcome`
  (Complete / Retry(delay) / Fail), `IJobQueue`, `IJobRunner`. Retries with backoff up to max attempts; a
  thrown handler is a transient retry.
- **Parallelism + control logic** — one `IJobRunner.RunOnceAsync` claims a bounded set across *all* lanes
  and runs them **concurrently** (true multi-lane parallelism), governed by per-lane limits
  (`JobOptions.LaneConcurrency`) and a global `MaxConcurrency` cap. Scale further by running several
  runner instances (one process or many) — the atomic claim hands each job to exactly one. **The app owns
  the pump** (`RunAsync` from your own `IHostedService`/loop) — Lyntai starts no background threads, so it
  stays host-free. Tuning on `LyntaiOptions.Jobs` + `LYNTAI_JOBS_*` env.
- Demonstrated end-to-end in the Playground (a checkpointing 2-step job over the same SQLite db).

## 0.13.1 — 2026-07-18

Correctness + resource-lifecycle fixes from an adversarial review of the v0.9–v0.13 tool-calling code
(two independent review passes). One small API refinement (`SupportsToolCalls` now takes the request).

### Fixed
- **Kestrel host / `WebApplication` leaks** — the CLI MCP host (`McpToolHost.StartAsync`) and provisioner
  (`McpCliToolProvisioner`) didn't dispose the started host if a later step threw (port-bind failure,
  temp-file write failure, cancellation). Both now dispose on any failure path.
- **`req.Tools` leaked into the prompt-fallback tool loop** — a caller-supplied `LlmRequest.Tools` was
  sent as native declarations *alongside* the JSON protocol prompt, so a partially-tool-aware model could
  emit a native tool-call turn the prompt path never parses. The prompt path now clears `Tools`.
- **Typed tool arguments were stringified** — the MEAI bridge and CLI tool-host serialized boxed CLR
  primitives (`3`, `true`) as JSON strings (`"3"`, `"true"`), so a tool with an `integer`/`boolean`
  schema got the wrong type. Primitives now keep their JSON type (reflection-free).
- **MCP tool results with only non-text blocks** (image/audio/resource) fed an *empty* observation back
  to the model. `McpToolset.ToText` now describes non-text blocks instead of returning "".
- **Capability-vs-routing mismatch** — `SupportsToolCalls` probed a candidate using its raw model while
  `CompleteAsync` resolved a per-consumer/request model, so under `ProviderAndModel` cooldown scope the
  loop could pick the native path while the router served a different (non-native) candidate, silently
  dropping tools. The probe now takes the `LlmRequest` and resolves the identical model/cooldown key
  (minor API change: `ILlmClient.SupportsToolCalls(req)` / `ILlmRouter.SupportsToolCalls(candidates, req)`).

### Security
- **The CLI tool-host now requires a per-call bearer token.** The localhost MCP endpoint *executes* the
  app's tools; a random token is generated per host, passed to the CLI via the `--mcp-config` headers,
  and required on every request (401 otherwise) — so another local process can't invoke the tools during
  the call window. (Loopback-only binding remains the primary mitigation.)

## 0.13.0 — 2026-07-18

Proper tool-calling for the **claude CLI** provider, plus a test-stability fix. Additive.

### Added
- **`Lyntai.Providers.ClaudeCli.Mcp`** — gives the claude CLI provider real tool-calling. The CLI runs
  its own agent loop and reaches custom tools only over MCP, so this hosts the app's registered
  `ITool`s as an **in-process, localhost-only HTTP MCP server** (Kestrel, ephemeral port, started and
  torn down per CLI call) and points `claude -p` at it via a temp `--mcp-config` + a `--settings`
  allow-list (`mcp__lyntai__*`, so only our tools run, non-interactively). Opt-in:
  `builder.AddClaudeCliProvider().AddTool(...).AddClaudeCliMcpTools()`; a completion routed to the CLI
  then lets its agent call the app's tools and returns the tool-informed answer.
  - A small Core seam (`ICliToolProvisioner` / `CliToolSession` in `Lyntai.Agents`) keeps the
    host/ASP.NET dependency out of the base `ClaudeCli` provider — the provider gains an optional
    provisioner and behaves exactly as before when the add-on isn't registered.
  - Each `ITool` is exposed via an invocable `AIFunction` (its own JSON schema, not delegate-inferred).
    Proven end-to-end by hosting the server and connecting with Lyntai's *own* MCP client (the exact
    thing the CLI does) — no real CLI needed for the core test; a gated `LYNTAI_LIVE_CLI_TOOLS` test
    covers the real binary.
  - **Note:** this is a deliberate, scoped exception to the library's "no server/no host" principle —
    an ephemeral localhost listener that exists only during a CLI call, isolated in this opt-in package.

### Fixed
- **Flaky router cooldown test** — the dead-host cooldown integration test depended on wall-clock timing
  (under a saturated parallel runner, call 1's subprocess spawn could outlast the cooldown before call 2
  ran). Rewritten to use `DeadHostTracker`'s injectable clock — fully deterministic. (No library change.)

## 0.12.0 — 2026-07-18

New package **`Lyntai.Tools.Mcp`** — expose a Model Context Protocol (MCP) server's tools to the tool
loop. Additive; new package only.

### Added
- **`Lyntai.Tools.Mcp`** (references `Lyntai.Core` + `ModelContextProtocol.Core`) — adapts each tool on
  a connected MCP server into a Lyntai `ITool`, so the whole MCP tool ecosystem becomes callable from
  `IToolLoop` (native or prompt path, same as any other tool).
  - `McpToolset.FromClientAsync(mcpClient)` lists the server's tools and wraps each as an `McpTool`
    (`ITool`); `builder.AddMcpTools(tools)` registers them into the tool collection. The **app owns the
    `McpClient`** (transport, connection, lifecycle — BYO, consistent with Lyntai's IoC seams); Lyntai
    only adapts. `McpTool` delegates the call through a `Func` seam so the SDK's concrete client stays
    out of the contract and the adapter is unit-testable.
  - Tool results flatten to the observation string the loop feeds back (text blocks joined, or
    structured content as JSON; `error:`-prefixed when the server flags an error).
  - Proven end-to-end against a real `@modelcontextprotocol/server-everything` over stdio (opt-in
    `McpLiveTests`, gated on `LYNTAI_LIVE_MCP`).

## 0.11.0 — 2026-07-18

Native tool-calling through the **MEAI bridge** — the follow-up deferred from v0.10. Now every
`Microsoft.Extensions.AI` `IChatClient` (OpenAI, Azure, Anthropic API, Ollama-via-MEAI, …) gets native
function-calling too, not just the OpenAI-compatible HTTP provider. Additive.

### Added
- **`ExtensionsAiProvider` bridges tools both directions.** `LlmRequest.Tools` map to declaration-only
  `AIFunctionDeclaration`s on `ChatOptions.Tools` (a `LyntaiToolDeclaration` — Lyntai's tool loop drives
  execution, so no invocable `AIFunction`/`FunctionInvokingChatClient` is used); the model's
  `FunctionCallContent` surfaces on `LlmReply.ToolCalls` (empty-content tool-call turn → `Ok` before the
  empty→Failed branch); tool-call/result turns map to `FunctionCallContent`/`FunctionResultContent`.
  `SupportsToolCalls => true`. Proven end-to-end through the tool loop with a scripted `IChatClient`.
- The bridge stays **trim/AOT-clean** (✅): the tool-argument round-trip uses `System.Text.Json.Nodes`
  (no reflection-based `JsonSerializer`).

## 0.10.0 — 2026-07-18

Native (structured) tool-calling — makes the v0.9 tool loop *actually work* over real provider
function-calling instead of only the prompt-based protocol. Additive; the contract additions keep every
existing `new LlmReply`/`LlmMessage` call site source-compatible.

### Added
- **Native tool-calling round-trip.** The model's tool calls now come back as structured data and tool
  results feed back through the contract:
  - `LlmToolCall(Id, Name, ArgumentsJson)`; `LlmReply.ToolCalls`; `LlmMessage.ToolCalls` +
    `LlmMessage.ToolCallId` with factories `AssistantToolCalls(calls)` / `ToolResult(id, content)`.
  - `ILlmProvider.SupportsToolCalls` (default-interface-method, default false),
    `ILlmClient.SupportsToolCalls` / `ILlmRouter.SupportsToolCalls(candidates)` — the loop asks the
    front door whether native tool-calling is available for the default routing (first live candidate)
    without ever seeing the candidate list.
  - **`OpenAiCompatibleProvider`** parses `tool_calls` from the response into `LlmReply.ToolCalls`
    (handling OpenAI's string arguments *and* Ollama's object arguments; synthesizing an id when Ollama
    omits one) and serializes assistant-tool-call turns + `role:"tool"` result turns in both the OpenAI
    and Ollama payloads. `SupportsToolCalls => true`.
- **`IToolLoop` now prefers native, falls back to prompt.** When the routing supports native tool-calling
  the loop sends tool declarations and acts on structured `ToolCalls` (parallel calls in one turn
  supported); otherwise it uses the v0.9 prompt protocol. Both paths execute the same app-registered
  `ITool`s, and unknown/throwing tools stay recoverable. Proven end-to-end against a real local Ollama
  (opt-in `OllamaToolCallLiveTests`, gated on `LYNTAI_LIVE_OLLAMA`).

### Deferred
- Native tool-calling through the **MEAI bridge** (`ExtensionsAiProvider`) — its `SupportsToolCalls`
  stays `false` for now (an argument dict↔JSON serialization spike + a `MapMessages` rewrite); a
  follow-up. Streaming tool-calls and ClaudeCli/Local native tools remain out of scope (they use the
  prompt fallback).

## 0.9.0 — 2026-07-17

First "platform kit" (design §9) capability: agentic tool-calling. Additive, all in `Lyntai.Core`.

### Added
- **Tool-calling loop** (`Lyntai.Agents`) — a provider-agnostic ReAct-style loop over the `ILlmClient`
  front door. `IToolLoop.RunAsync(req)` renders the registered tools into the prompt, asks the model to
  either call a tool or finish (a small JSON protocol: `{"tool":…,"arguments":…}` / `{"final":…}`),
  executes the chosen tool, feeds the observation back, and repeats until it finishes or the iteration
  budget is hit. Because it runs over the text contract (through `CompleteJsonAsync`), it works with
  **any** provider — CLI, HTTP, MEAI bridge, local — with no native tool-calling support required.
  - **`ITool`** — the executable-tool seam (`Name`/`Description`/`ParametersJsonSchema` mirror the
    existing `LlmTool`, plus `InvokeAsync`), registered into a DI collection via `builder.AddTool<T>()`
    / `AddTool(factory)` (the variation point — a tool is a new class + one registration).
  - **`FunctionTool`** — define a tool inline from a delegate, no class needed.
  - **`IToolRegistry`** — name-keyed (case-insensitive, first-wins) resolution over the tool collection.
  - Robust by construction: an unknown tool or a throwing tool becomes an `error: …` observation fed
    back to the model (it can recover) rather than an exception; a non-Ok LLM verdict (refusal, all
    candidates down) is surfaced as-is; a run that doesn't converge returns `Failed` with a reason.
  - Wired by default in `AddLyntai` (resolves with zero tools — it degenerates to one plain
    completion). Budget via `LyntaiOptions.ToolLoopMaxIterations` (default 8) /
    `LYNTAI_TOOL_LOOP_MAX_ITERATIONS`, or a per-call `RunAsync(req, maxIterations)` override.

## 0.8.0 — 2026-07-17

New provider package for in-process local inference. Additive — no changes to existing packages.

### Added
- **`Lyntai.Providers.Local`** — runs a local GGUF model in-process via LLamaSharp (llama.cpp), wired
  with `builder.AddLocalProvider(modelPath, …)`. No network, no API key, no external process; the
  model loads lazily and is reused, and generations are serialized (one local model, one at a time).
  It classifies to the same verdicts the router expects (produced answer → `Ok`; empty generation or
  a load/inference fault → `Failed` so the router falls over; inactivity → `Timeout`).
  - **Managed-only on purpose:** the package references `LLamaSharp` but *not* a backend, so it isn't
    nailed to one runtime — the consuming app adds the `LLamaSharp.Backend.*` (Cpu/Cuda/Vulkan/Metal)
    that matches its hardware. A missing backend surfaces as a `Failed` verdict on the first call
    (the router then falls over), not a startup crash.
  - Applies each model's own chat template (from its GGUF metadata) so instruct-tuned models get the
    prompt format they were trained on. Opt-in live tests gate on `LYNTAI_LIVE_LLAMA` +
    `LYNTAI_LLAMA_MODEL` (like the Ollama live tests), so the default run stays native-dependency-free.

## 0.7.1 — 2026-07-17

Correctness fixes from a high-effort multi-agent code review of the v0.3–v0.7 code. No API break
(one additive optional ctor param).

### Fixed
- **Critical: BYO HttpClient was disposed every call** — an app-supplied shared client threw
  `ObjectDisposedException` on the second request. Lyntai now disposes only clients it created.
- **Memory cap counted expired entries**, evicting live facts while keeping dead ones — the cap now
  evicts expired entries first (all three backends).
- **Memory dedup ranked by a stale id**, so a re-remembered fact recalled as old — recall and the cap
  now order by `created_at` (the refreshed value), honoring the "refreshes recency" contract.
- **Router recorded a dead-host failure per retry attempt** — `Retry(Failed, 2)` benched a host in one
  request; now one failure per request. Router streaming no longer leaks empty content chunks.
- **Dapper `DateTimeOffset` handler collision** between the SQLite and Postgres backends (process-global
  registry) — both handlers are now identical, so loading both is safe.
- **Postgres dedup race** (concurrent Remembers → duplicates) — now an atomic `INSERT … ON CONFLICT`.
- **`MigratingConnectionFactory` cached a transient migration failure forever** — now retries.
- **`ClaudeCliProvider.IsAvailable` bypassed a BYO `IProcessRunner`** — a custom (sandbox/remote) runner
  is now optimistically available so the router reaches it.

## 0.7.0 — 2026-07-17

Bring-your-own resources — inversion of control for the resource-lifecycle concerns. The app owns the
implementation; Lyntai provides the interface. All additive; the `ClaudeCliProvider` ctor now takes
`IProcessRunner` (source-compatible — `ProcessRunner` implements it).

### Added
- **`IProcessRunner`** — the process-spawning seam (default `ProcessRunner`). Register your own to own
  how the `claude` CLI is spawned (sandbox, custom shell, remote/audited execution).
- **BYO HttpClient** — `AddOpenAiCompatibleProvider` (and the presets) accept an optional
  `Func<IServiceProvider, HttpClient>`, so you supply your configured client (Polly, auth handlers,
  proxy, a named `IHttpClientFactory` client) and own its lifecycle.
- **BYO DB connection + schema** — `UseSqliteStorage`/`UsePostgresStorage` gain an
  `IDbConnectionFactory` overload (you own connection creation/pooling/lifecycle) and a `migrate: false`
  flag (you own the schema; Lyntai runs no migrations).
- **Provider presets** — `AddOpenAiProvider`, `AddOllamaProvider`, `AddOpenRouterProvider`,
  `AddAzureOpenAiProvider` — pre-configured defaults over the generic method. The BYO `ILlmProvider`
  path (`AddProvider`) stays open for anything bespoke.

## 0.6.0 — 2026-07-17

Second heavyweight backend + a real-endpoint provider test — the two items previously blocked on
infrastructure, now that a Postgres-capable Docker and a local Ollama are available. Additive.

### Added
- **`Lyntai.Storage.Postgres`** — a full PostgreSQL backend for every storage domain (Npgsql + Dapper
  + FluentMigrator). `lyntai_`-prefixed; memory recall uses `pg_trgm` (GIN trigram index) for ILIKE
  substring search including CJK substrings; `timestamptz` ↔ `DateTimeOffset`; dedup/TTL/cap/prune;
  `UsePostgresStorage(conn, migrateOnFirstUse)`. Integration-tested against a real container via
  Testcontainers (skips when Docker is unavailable). Proves the domain-interface seam holds for a
  heavyweight server DB — three backends now (SQLite, in-memory, Postgres).
- **Opt-in live Ollama test** — validates the OpenAI-compatible provider (Ollama flavor) against a
  real endpoint (completion with real usage, streaming, through the router). Gated on
  `LYNTAI_LIVE_OLLAMA`; the default run stays fast and dependency-free.

## 0.5.0 — 2026-07-17

Ecosystem & backends (roadmap v0.5) + v1.0 API-freeze groundwork. Additive; no behavioral change.

### Added
- **`Lyntai.Storage.InMemory`** — a zero-dependency in-memory backend for every storage domain
  (KV, conversation, memory with dedup/TTL/cap, score, trace, prompt-version). Useful standalone
  (tests, ephemeral/serverless, no file) and as the second real backend proving the domain-interface
  seam. Wired via `builder.UseInMemoryStorage()`.
- **Composite storage** — the mastra "one interface per domain, many backends" pattern expressed
  through DI: mix backends per domain (SQLite for most, in-memory for one) via a per-domain override
  (last registration wins); `UseInMemoryStorage()` uses `TryAdd` so it stands alone or backfills gaps.
- **Public-API baseline** (`ApiSurfaceTests`) — snapshots every packable assembly's public/protected
  surface against a checked-in baseline so API changes are deliberate (pre-1.0 visible in review;
  post-1.0 gate a major bump).
- **`docs/AOT.md`** — per-package trim/AOT status and the Dapper.AOT path for `Lyntai.Storage.Sqlite`.

## 0.4.0 — 2026-07-17

LLM-ops depth (the roadmap's v0.4). No behavioral change to existing paths; all additive except the
`IMemoryStore` signature (a `ttl` param + `PruneAsync`, pre-1.0).

### Added
- **Versioned prompt overrides** — `IPromptVersionStore` + SQLite impl: an audit trail for
  `lyntai.prompt.*` edits (author, monotonic versions, exactly one active) with history and rollback
  that re-activates an earlier revision without rewriting history. The registry renders the active
  versioned override (winning over the plain KV key), placeholder guard still applied.
- **Judge calibration** — `JudgeAgreement` (exact-agreement rate, mean absolute error, Pearson over
  two aligned score series) and `IPairwiseComparer` / `LlmPairwiseComparer` (which-is-better, with
  position-bias mitigation on by default — runs both orders, ties on disagreement).
- **Memory lifecycle** — dedup (remembering an identical fact refreshes rather than duplicates),
  per-entry TTL (expired entries excluded from every recall path), and `PruneAsync(taskKey?, olderThan?)`.
  `SqliteMemoryStore` takes an injectable clock for deterministic TTL tests.
- **Trace ↔ span bridging** — `RunTrace.TraceId` captures the ambient OpenTelemetry W3C trace id at
  `Begin`, persisted and round-tripped, so a stored run trace cross-references the distributed trace.

### Changed
- `IMemoryStore.RememberAsync` gains an optional `ttl`; adds `PruneAsync` (pre-1.0 interface change).
- Migrations 202607170006 (prompt versions), 202607170007 (memory TTL), 202607170008 (trace id).

## 0.3.0 — 2026-07-17

Routing & resilience depth (the roadmap's v0.3), plus a second independent audit pass (four
adversarial reviewers over the router, providers, storage, and cortex) — findings the 0.2.0 review +
221 tests missed.

### Added
- **Configurable `RoutingPolicy`** (`LyntaiOptions.Routing`, `LyntaiBuilder.ConfigureRouting`,
  `LYNTAI_RETRY_*` / `LYNTAI_COOLDOWN_SCOPE` env). The hard-coded §6 router switch becomes the
  *default* policy — every prior router test passes unchanged. Four routing items land at once:
  - **Per-verdict action** (`FallbackAction`: Advance / PenalizeAndAdvance / CooldownAndAdvance /
    Surface), overridable with `.On(verdict, action)`.
  - **Retry-then-advance** — `.Retry(verdict, n)` retries the *same* candidate on a transient fault
    (Failed/Timeout) before falling over; cooled/surfaced/context-window verdicts never retry the
    same host. Optional `RetryBackoff`. Applies to complete + streaming pre-content.
  - **Per-(provider, model) cooldown granularity** (`CooldownScope`) — a rate-limited model no longer
    benches its siblings on the same host. Default `Provider` (unchanged).
  - **Sole-candidate exemption** (default on, LiteLLM parity) — never bench the only candidate.
- **Deferred migrations** — `UseSqliteStorage(path, migrateOnFirstUse: true)` migrates lazily on the
  first store access (thread-safe) so DI composition does no I/O.
- **`bench/Lyntai.Benchmarks`** (BenchmarkDotNet, `dev.mjs bench`) — router overhead per attempt,
  FTS5 recall latency at 1k/10k/100k rows.

### Fixed (audit pass — no API breaks)
- **Streaming timeout is now an inactivity clock in every provider.** The ExtensionsAi and
  OpenAiCompatible streaming paths still armed a single wall-clock `CancelAfter` over the whole stream
  (0.2.0 fixed only the CLI/ProcessRunner path), so a slow-but-healthy stream or a slow consumer got
  killed. Both now re-arm per read and stop the clock while the consumer works.
- **Router won't commit a stream on an empty content chunk** — the commit gate requires non-empty text,
  so a third-party provider yielding an empty/role-only first chunk can't disable fallback.
- **`LYNTAI_MODEL_<CONSUMER>` env override is implemented** (was documented but silently ignored).
- **OTel cost + cache-read tokens are recorded** on the client span (were dropped despite the 0.2.0
  telemetry claim).
- **`CompleteJsonAsync`'s retry now differs from the first attempt** (feeds back the bad reply + a
  JSON-only instruction) instead of re-sending the identical request.
- **`AddLyntai` throws on a second call** instead of shadowing `LyntaiOptions` + duplicating providers.
- **`LlmVerdictClassifier` no longer treats a bare "unauthorized" as `AuthFailed`** (which cools the
  host) — it needs auth context, mirroring the 429 guard.
- **`MigrationRunnerService`** builds its connection string via `SqliteConnectionStringBuilder` (was raw
  interpolation) and sets WAL + `busy_timeout` before migrating.
- **`ConversationStore.ListThreadsAsync`** gets an `id DESC` tiebreaker (deterministic on `created_at`
  ties).
- **`MemoryPromptComposer`** bounds the appended section by a character budget, not just entry count.
- `ILlmRouter` XML doc corrected to the amended fallback semantics.

## 0.2.0 — 2026-07-17

Production-hardening release: everything surfaced by the multi-agent code review and the
2025–2026 best-practices research pass, plus the provider-shaped consumer API.

### Added
- **`ILlmClient`** — the front door: to a consuming app, Lyntai behaves like ONE LLM provider
  (complete/stream over a request; candidates, fallback, and cooldowns stay internal).
- **`AsChatClient()`** — the reverse MEAI bridge: consume a whole Lyntai composition as a
  `Microsoft.Extensions.AI.IChatClient`.
- **`CompleteJsonAsync`** — structured output per design §6: schema-constrained call, tolerant JSON
  extraction from prose/fences, one retry, else `Failed`. `LlmScorerBase` now builds on it.
- **`LlmVerdictClassifier`** — the one shared failure classifier (typed HTTP status first, then
  conservative text heuristics); replaces three drifting per-adapter copies.
- **`LlmVerdict.ContextWindowExceeded`** (advance without penalizing the host — the remedy is a
  larger-context candidate) and **`LlmVerdict.AuthFailed`** (immediate host cooldown + advance).
- **OpenTelemetry GenAI telemetry** — `ActivitySource`/`Meter` "Lyntai.Llm": `chat {model}` client
  spans with `gen_ai.*` attributes, `gen_ai.client.operation.duration`, `gen_ai.client.token.usage`,
  and `gen_ai.client.operation.time_to_first_chunk` (the streaming fallback point of no return).
- Packaging: XML docs, snupkg symbols, embedded sources, package tags, trim/AOT analyzers
  (`IsAotCompatible` everywhere except `Lyntai.Storage.Sqlite`, which opts out honestly over Dapper).
- Playground/e2e exercise streaming end-to-end.

### Changed — breaking
- **`RateLimited` semantics (design §6 amendment):** was circuit-break-hard-stop; now cools the
  host immediately (`DeadHostTracker.MarkDead`) and advances to the next candidate — a 429 is
  terminal for the host's window, not for the fleet. Surfaced only when every candidate is exhausted.
- `LlmScorerBase`/`RelevancyScorer` constructors take `ILlmClient` (was `ILlmRouter` + options).
- SQLite objects are prefixed `lyntai_` (tables, FTS, triggers, indexes, and the FluentMigrator
  version table) so `UseSqliteStorage` can safely target an existing application database.

### Fixed
- `ProcessRunner`: stdin written before the timeout was armed (unbounded hang); streaming timeout
  counted consumer dwell time (healthy streams killed); abandoned enumerators orphaned live CLI
  processes (now killed via try/finally).
- Content-filter verdicts: HTTP-200-with-empty-content and streamed `content_filter` both surfaced
  as `Refused` (never retried, never fallen back) instead of `Failed`/silent-`Final`.
- Zero-content HTTP/MEAI streams now yield `Error(Failed)` so the router can fall over, matching
  the non-streaming path and the CLI provider.
- Claude CLI: content without a terminal result event ends `Final`, not a spurious error; spawns
  from a neutral cwd (no host-project CLAUDE.md/hooks loaded into library calls).
- `http://localhost:11434/v1` (Ollama's OpenAI-compatible surface) detects the OpenAI flavor.
- SQLite `CommandTimeout` set deliberately (the driver's busy-retry loop is independent of
  `PRAGMA busy_timeout`).

## 0.1.0 — 2026-07-17

Initial implementation: the full `TASKS.md` sequence (phases 0–7). Core abstractions + fallback
router, SQLite storage with FTS5-trigram memory, claude-CLI / OpenAI-compatible / MEAI providers,
cortex layer (prompt registry, scorers incl. LLM judge, traces, memory composition), Playground,
devtools e2e harness, NuGet packaging.
