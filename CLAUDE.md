# CLAUDE.md — Lyntai (灵台)

> Auto-loaded every session. Keep short — details live in `docs/` and `.claude/rules/`.

## What this is

**Lyntai** (灵台, "the numinous platform" — the seat of the mind) is a reusable **.NET 10 library**: the
shared **cortex + persistence** substrate extracted from the sibling apps (Gatherlight, Vidora, Sonora)
and the mastra/odysseus studies, so a new project gets **LLM providers + pluggable storage + LLM-ops**
without rebuilding them. It is a *library* (a set of NuGet-packable projects), not an app — no server,
no host, no UI.

The two things it provides: (1) an **LLM provider abstraction** with routing + **fallback** across CLI /
API / `Microsoft.Extensions.AI`-bridged providers, and (2) **pluggable storage** (SQLite now, interfaces
so other backends follow as separate packages) — plus the LLM-ops layer (prompt registry, scoring/eval,
run traces, task-scoped memory) and DI wiring (`AddLyntai(...)`).

## Current state

**Released: v3.1.0 (2026-08-23).** Twelve packages; public API frozen under SemVer 2.0 since 1.0, **with no
carve-out** — the one that existed (the `Lyntai.Generation` PACKAGE, from 2.0.1) was withdrawn in 3.0,
`docs/DECISIONS.md` **D70**.

**Everything before 3.0 is HISTORY, not context** — a boundary that is deliberately 3.0 and does NOT track
the current release, so do not advance it when the version above moves. `CHANGELOG.md` logs it and that is the only place it needs
to exist — nothing is deployed on a pre-3.0 version, so a session never has to reason about what a
2.x release did, reconstruct an upgrade path, or justify a design by what an older release preserved. Read
the current code and the records below.

The reasoning is `docs/DECISIONS.md`, **D1–D104**. The two groups worth knowing before you touch anything:
**D83–D103 are post-3.0** — mostly additive, every one from a seam an adopting application had to work around
or a default nobody had measured (**D89** moves `SalienceWeight` to 0: salience does not vote on ranking;
**D90** puts four INVARIANTS above the memory objective's optimization targets, and says which two of them
the base engine can be held to today) —
read
them before assuming a memory registration that resolves is a memory registration that runs, or that a NAMED
`ILlmClient` can reach the backends it names (**D87**). **D95** and **D96** are the odd ones out in that
group — neither touches a library surface: one declares the repository's line endings (`* text=auto eol=lf`),
the other puts a length ratchet on the entries of this very list. **D97** is the one to read before touching
ranking: a candidate nobody scored used to report the MAXIMUM relevance and outrank everything that was
scored, and `GraphNode.Matched` is what lets a policy tell "scored zero" from "never asked".
**D98** is the one to read before touching TRAVERSAL: `EdgeHalfLife` decays the EDGE and nothing
consulted the ENTRY, so an expansion handed back exactly what a recall had buried until
`ExpansionRetrievabilityFloor` existed — off by default, because it buys precision with recall.
<br>**D67–D82** are the ones a session most often holds
stale assumptions about, because they landed after the pre-freeze review: the generation stream door
(**D67**), the accelerator-derived diffusion ceiling (**D68**), unmeasured generation mappings becoming host
OPTIONS (**D69**), the withdrawal of the generation SemVer exemption (**D70**), tool calls on the streaming
contract (**D71**), the forget/prune split with `IMemoryRemovalPolicy` (**D72**), the cross-process job cap
as a heartbeated slot table (**D73**), the guard-parity split — forced in FORCE, accidental in SIGNAL
(**D75**), the two relational memory-graph stores sharing their materialization (**D77**, **D80**, **D81**),
and RRF ranking by COMPETITION so an uninformative signal contributes nothing (**D82**). The memory subsystem
overall is **D39–D41**, **D45–D63**, **D72**, **D76–D79**, **D83–D86**, **D88**, **D89**, **D90**, **D91**,
**D92**, **D93**, **D94**, **D97**, **D98**, **D99**, **D100**, **D101**, **D102**, **D103** and **D104**.

**The memory engine is evaluated as an n-shot WALK, not a single top-k** (**D100**, 2026-08-29). A recall
returns HEADLINES — associative content is withheld until an expansion asks for it, which is what makes the
first load cheap — and `ExpandAsync` reinforces what it walks. So a one-shot metric is blind to the mode the
engine is built for, and **every recall-quality figure published before that date scores one shot**.
<br>**`MemoryWalk.WalkAsync` ships that walk as a SURFACE** (**D102**, 2026-08-30): an extension over the two
seams, yielding a step at a time so the caller's `break` is the stop condition — depth is a property of the
question. It adds nothing to `IMemoryEngine`, and its public names are SETTLED — the naming pass landed the
same day, five renames (`docs/task-archive.md` **Part 121**). Measured:
on LongMemEval knowledge-update, shot 1 returns the current fact and not the superseded one 31.4% of the time
on 1,169 characters against plain cosine's 10.0% on 10,387 (all 70 questions; the 25-question sample this
line quoted until 2026-08-29 read 40.0% against 16.0% — a lower LEVEL, a higher RATIO). The useful shot
count is a property of the QUESTION, so it is not a constant, and no default moved.
<br>**"Search wants two shots" was withdrawn on 2026-08-29** (D100's own amendment, `docs/task-archive.md`
Part 118): LoCoMo questions shared a store, and isolating them flattened the shot curve from +6.0/+0.5 to
**+1.5/+1.0** on a shot 1 that was 24.5 points too low. **Every LoCoMo figure in `docs/memory.md` moved by
20–25 points** while `vector` — which never touches the graph store — did not move at all. Read a LoCoMo
number there for the regime it names.
**`TASKS.md`'s own banner is the live work — read it rather than any Part named here.** Part 116 was the
2026-08-29 handover, and 2026-08-30 closed most of it (`docs/task-archive.md` **Parts 117–121** and **Part
123**), so it is no longer "the" live work and the startable set is no longer one Part. Deliberately no
count and no list: this line is auto-loaded, and a banner it duplicates is one amended in place.

**Long-term memory is the newest subsystem** and the one a session is most likely to reason about wrongly,
because it is not the three older memory surfaces: named engines resolved by name like `IHttpClientFactory`
(`IMemoryEngine` / `IMemoryEngineFactory` / `AddMemoryEngine`; **D39**), a graph engine whose entries decay,
connect and open as a cheap index (`UseGraph()`), decay measured in **interference rather than elapsed time**
with the age policy as a seam (**D40**), **burial rather than deletion** (**D41**), InMemory + SQLite +
Postgres backends under one contract, and `AddMemoryTools` exposing recall/expand to the model.
`IMemoryStore`, `ISemanticMemory` and `ICuratedMemoryStore` co-exist with it. The contract is design §5.7.
<br>**A SUBJECT handle is readable** (`SubjectSeedOptions.K`, **D88**): a recall matches its query against
the handles in use and seeds the entries recorded under whichever ones it names. The channel is **ON by
default** — `AddMemoryEngine` registers it unconditionally, and `AddMemorySubjectSeeds` only CONFIGURES it —
unlike the semantic seed, which `AddMemorySemanticSeeds` must register before anything runs, because a
handle exists only where an annotator was registered and paid for.

**The memory subsystem's load-bearing invariants**, stated as what they ARE. Each one is a rule a change can
silently break; the reasoning is in the decision named beside it.

1. **Every seam is `IMemory<Domain>Policy`** (**D47**) — `IMemoryAgePolicy` (age is *interference*, never a
   clock), `IMemoryRetrievabilityPolicy`, `IMemoryRetentionPolicy`, `IMemorySaliencePolicy`.
2. **A seam is SINGULAR or PLURAL by whether its implementations read the same aspect** (**D48**). Age,
   salience and retention are **plural**, and each plural domain owns a **composition policy** — the engine
   composes nothing itself. Retrievability and ranking are singular; `CompositeRankingPolicy` is ONE policy
   built from two, not two running side by side.
3. **Age is DERIVED, not stored.** Nodes carry policy-independent primitives (encoding ordinal, cumulative
   characters, timestamp) and each policy projects its own view. **Except** `BurstDampenedAgePolicy`, which
   declares `MemoryAgeKind.Accumulating` because its position is path-dependent and keeps the accumulator —
   and which is the shipped default, so the default path reads the accumulator.
4. **`Stability` has ONE meaning, enforced by a contract fact**: the position delta at which retrievability
   is `0.5`. FSRS anchors at 90%, and adopting that convention would silently reinterpret every stored value
   — the fact exists to make it unshippable. `Reinforce` returns **state**, not a `double`.
5. **Each entry records WHICH policy computed its state** (`provenance_retrievability` /
   `provenance_salience`, flags in `MemoryProvenance`), so "never computed" is distinguishable from "zero".
6. **`DsrRetrievability` (FSRS's power law) is the only shipped forgetting curve**, and
   `ReciprocalRankFusionPolicy` is the registered ranking default (**D49**). `MultiplicativeRankingPolicy`
   ships and is one line to restore; its own knobs live on `MultiplicativeRankingOptions`.
7. **FSRS's difficulty axis is LIVE** — `Reinforce` maintains `MemoryDecayState.Difficulty` per review,
   deriving a grade from retrievability-at-recall. **Neutral is `5`, not `1`**: `1` is FSRS's floor, which
   pins the axis at the clamp so it can never vary. Reviews are logged (`GraphMemoryOptions.LogReviews`) so
   parameters can be fitted later; nothing reads that log at runtime.
8. **`IMemoryGraphStore` has THIRTEEN required members**, of which the FIVE this major added took no default
   body deliberately — `DeleteAsync`, `RecordReviewsAsync`, `ReviewsAsync`, `RecordSubjectsAsync`,
   `NodesBySubjectAsync`. This line said "has FIVE required members" until 2026-08-31, dropping the
   qualifier **D67** carries ("the choice `IMemoryGraphStore`'s five members took *in this major*") and
   restating it as a total — which misleads precisely the reader it addresses, since a BYO store author
   reads "implement all five" and meets thirteen. **THREE members carry a default body**, and the difference
   between them matters:
   `KnownSubjectsAsync` defaults to an empty list, so a BYO store silently gets no subject seeding
   (**D88**); `LinkManyAsync` (**D99**) and `WriteBackAsync` (**D101**) default to the calls the engine used
   to make inline, so a BYO store loses no behaviour at all and is merely no faster. **`WriteBackAsync`
   carries an ORDER as contract** — the review log last, so a broken log cannot cost the touch or the edges
   — and an override that reorders it is wrong however fast it is.
9. **All THREE age axes speak one unit** (**D52**). An edge carries the same three primitives a node does
   (`StrengthOrdinalAge`/`StrengthVolumeAge`/`StrengthElapsedAge` + `StrengthAgeSample` on `GraphNode`, and
   the same plus `EdgeAgeSample` on `GraphNeighbour`), so `StrengthAge` is swap-safe and `PruneAsync`'s
   derivable path is EXACT. `GraphMemoryOptions.EdgeHalfLife` is denominated in whatever the policies count.
10. **A fresh database applies 12 migrations on SQLite and 13 on POSTGRES**, and the asymmetry is deliberate:
    `M202608152310_MemoryHeadlineSearch` adds a trigram index on `headline` so recall can match an authored
    one without a sequential scan, and SQLite needs no counterpart because its FTS5 mirror has indexed
    `headline, content` since the graph store shipped. Migrations are per-backend projects; forcing the
    numbers to match would mean shipping a SQLite migration that does nothing.
11. **An authoritative fact the query did not match reports `Relevance` 0 on every backend.** Admission comes
    from the grade carve-out and the engine's re-admission, never from relevance.
12. **An authoritative fact takes a slot WITHIN a recall's limit and displaces ordinary hits** (**D56**) —
    design §5.7.0's objective (1), the only one with no acceptable failure rate.
    `GraphMemoryOptions.AuthoritativeReserve` bounds how many slots exact facts may take (`null` = unbounded
    = default). **Do not "fix" a small-limit recall returning fewer ordinary hits — that is the promise
    working.** Guarded by `MemoryAuthoritativeSurvivalTests` plus a control requiring the same facts to be
    LOST without the grade.

**The packaging rules are gated, not remembered** — `verify` runs seventeen checks. `check-warnings` (a warning
in a published project fails the build, because an unfailed IL2026 is a FALSE trim promise), `check-packages`
(a package must be registered in all nine registries — a missing `ApiSurfaceTests` entry means no API gate at
all), `check-bundle` (the bundle's dependency closure cannot grow without a decision), `check-docs` (a doc
using vocabulary a decision retired fails the build — the prose counterpart to `check-warnings`; **D42**),
plus `consumer-smoke` outside `verify` (pack, then restore/build/run a fresh app against the PACKAGES).
Adding a package is `node devtools/dev.mjs new-package <Lyntai.X>`.

Tests/e2e green: **3552 passed / 3573 total, 21 skipped** (live-backend only — Ollama, MCP, a real CLI, a
real annotating/judging model, a real embedder), e2e 3/3, guard-script tests 470/470, doc samples 80/80.
**A skip count WELL above 21 means Docker is down and the whole
Postgres leg is silently unexercised** — start it and re-run before believing a green suite (archive Part 58,
which caught a missing table exactly that way; it happened again on 2026-08-12, which is why the count above
is worth comparing against). The old form of this line named a specific Docker-down figure; it is now a
RELATION rather than a number, because Part 70 turned the Postgres contract from one test into 69 theory
cases and any figure quoted here would be one restructure from wrong — and a wrong number is what teaches a
reader to stop comparing.

**WHICH of these numbers a gate holds, because the answer is not "all of them".** `guard-script tests`,
`e2e` and the migration counts are derivable from the tree and are gated by `check-counts`; `doc samples` is
derivable only from a RUN, so `check-samples` asserts it directly — a run-derived number is checked by the
gate that PRODUCES it, never by a static counter that would have to reimplement it. **The xUnit trio
(`passed / total / skipped`) is gated by NOTHING**: only `dotnet test` knows it, and capturing that output is
the shape `check-warnings` already hit ENOBUFS on. So those three are the ones to **re-measure by hand after
`verify`** — do not extrapolate them from a diff, which is exactly how `3266/3287` got written here on
2026-08-23 against a tree that ran 3264/3285. A stale baseline is worse than none: the whole point is that a
reader can compare, and a count that no longer matches a green run teaches them to stop comparing.

**The records, and what each is for:**
- `docs/2026-07-17-lyntai-design.md` — the **contract** (interfaces, fork decisions, semantics —
  note the dated §6 amendments; §6 is now the default `RoutingPolicy`). Read it first.
- `docs/ROADMAP.md` — what is shipped per version, then `## Planned`: what a real run still has to confirm
  in the generation backends, and the standing maintenance policies.
- `CHANGELOG.md` — per-release detail; breaking changes called out.
- `docs/DECISIONS.md` — the rationale log, in the present tense: what each decision IS today. Contiguous
  `D1..Dn` — a number is never reclaimed, so an entry that stops being its own decision becomes a **stub**
  naming where its content went (merged into another entry, or relocated to the record that owns it), which
  keeps every inbound `D<n>` resolving; see its own §How to read it. **Numbers were reassigned 2026-08-14**,
  so a `D<n>` in older git history means a different entry. Entry length is gated — **D96**.
- `README.md` — the consuming story (install, `AddLyntai`, the add-ons, semantics).
- `TASKS.md` — the **active** backlog (open tasks only); `docs/task-archive.md` — the completed-task
  history (the frozen implementation plan + closed backlogs). See the `task-lifecycle.md` rule.
- `docs/FIXES.md` — the fix log: per-incident symptom, root cause, fix and verification (the `fix-log`
  skill's target; created with the first entry — see `repo-mechanics.md` §Fix log).
- `docs/<date>-*.md` — point-in-time designs, not maintained state: check the status banner (or the date
  against `CHANGELOG.md`) before treating one as executable.
- `docs/superpowers/INDEX.md` — the tracked LIST of per-version design records; the records themselves are
  **untracked**, in `local/superpowers/{specs,plans}/`. They describe one version's work and stop being
  true once it ships, so they are a working record, not a contract — **anything in one that must outlive
  its version belongs in a maintained document** (the contract, `DECISIONS.md`, `pitfalls.md`, the
  archive). Write new ones straight into `local/`; the brainstorming/writing-plans skills default to
  `docs/superpowers/`, so redirect them.

**There is NO SemVer carve-out any more — every package needs a major, `Lyntai.Generation` included.** It
held one from 2.0.1 and 3.0 withdrew it (**D70**), because the carve-out named three reasons and all three
closed: the two vendor-doc backends and the ported `sd-cli` argv now expose every mapping they could have got
wrong as a host OPTION (**D69**), and the stream seam is reachable through the router (**D67**). A session
reading older text — a 2.x `CHANGELOG.md` entry, an archived Part — will find the exemption described in the
present tense; it was accurate then. `check-docs` fails any MAINTAINED document that reintroduces it.
<br>The still-live half of that old paragraph, because it was always the part people got wrong: the
generation CONTRACTS (`GenerationResult`, the routing policy, `GenerationVerdictClassifier`, …) live in the
`Lyntai.Generation` NAMESPACE *inside `Lyntai.Core`*, while the BACKENDS are the separate
`Lyntai.Generation` package. That split is now justified by dependency footprint alone — the package sits
outside the `Lyntai` bundle so a one-line install does not drag the media backends — since D25's other
reason for it, release cadence, went with the carve-out (D70 records the withdrawal).

Namespace map (Core): `Lyntai.Llm` (contract types) / `Lyntai.Llm.Cli` (the shared spawned-CLI engine +
per-CLI `ICliProviderDialect` — a new CLI backend is a dialect, never a new provider; see `DECISIONS.md`
D21/D22) / `Lyntai.Generation` (+ `.Routing`/`.Jobs`/`.Tools`) (the generation platform — image/video/audio/3d behind one
capability-aware seam; the CONTRACTS are in Core, the BACKENDS are the `Lyntai.Generation` package under
`Lyntai.Generation.Providers` — split for release cadence, `DECISIONS.md` D24/D25) /
`Lyntai.Llm.Routing` (router engine) /
`Lyntai.Llm.Caching` (response cache) / `Lyntai.Llm.Budgeting` (usage budget) /
`Lyntai.Llm.RateLimiting` (rate limiter) /
`Lyntai.Embeddings` (embedder seam) / `Lyntai.Memory` (semantic memory + vector store; the graph-memory
DOMAINS are SEVEN: `.Interference` / `.Forgetting` / `.Modulation` / `.Salience` (retention), `.Ranking` (how
a recall's candidates are scored and ordered — `IMemoryRankingPolicy`, default `ReciprocalRankFusionPolicy`),
and the two MODEL-IN-THE-LOOP seams `.Annotation` / `.Verification` (both SINGULAR, both defaulting to
**none**, which is why this list said five until 2026-08-15 — a domain nothing constructs by default is
invisible to everything but the namespace map),
each one seam plus its implementations AND its options — placement is by OWNERSHIP, not consumption, so a
type a sibling domain merely depends on stays with its owner and only a type no domain owns
(`MemoryDecayState`) sits at the root; see design §5.7) /
`Lyntai.Prompts` / `Lyntai.Cortex` (+ `.Scorers`) / `Lyntai.Agents` (tool loop + chat orchestration) /
`Lyntai.Jobs` (durable jobs) / `Lyntai.Guards` (guard rail) / `Lyntai.Secrets` (secret vault: AES-GCM/BYO
+ recovery-key envelope; DPAPI binding in the `Lyntai.Secrets.Dpapi` adapter) /
`Lyntai.Lifecycle` (provider POOL + `ProviderKey` + admission — for an app whose backend configuration is
owned outside the deployment; `DECISIONS.md` D30) /
`Lyntai.Storage` / `Lyntai.Processes` / `Lyntai.Text`; builder + `Add*`/`Use*` extensions live in the
`Lyntai` namespace.

## Rules, knowledge & skills

- **`.claude/rules/`** (always-on) — `dotnet-package-layout.md` (contract in Core, impl in an adapter,
  never adapter→adapter; split by dependency footprint; DI-collection variation points; naming),
  `skills-workflow.md` (start a non-trivial task through the discovery skills — and READ what they route
  you to), `sensitive-info.md` (no dev-machine paths / private tokens; pre-commit guard — install once
  with `node devtools/dev.mjs install-hooks`), `task-lifecycle.md` (`TASKS.md` = OPEN backlog only; a
  completed task MOVES to `docs/task-archive.md`), `persist-working-state.md` (checkpoint a decision or
  finding to its in-repo home WHEN it happens, not at the end), `no-global-memory.md` (project facts live
  IN-REPO — `.claude/**` / `docs/DECISIONS.md` — global memory is user-prefs only),
  `file-tool-discipline.md` (inspect files with `Read`/`Grep`/`Glob` not `Bash cat/ls/find`; never evade
  the permission gate), `no-tmp-for-repo-files.md` (compose with `Write`; scratch → `devtools/_*`, never
  OS temp), and `windows-machine.md` (the traps that succeed WRONGLY — PowerShell 5 round-trips, BOMs,
  lying exit codes). Those state the PRINCIPLE; this repo's concrete bindings — package names and the
  packable/version layout, the `Dto`-free naming invariant, guard scripts, version-authorship policy, the
  dev loop and test conventions, scratch paths — live in `repo-mechanics.md`.
  <br>**`code-commentary.md`** (added 2026-08-16): three tiers, three jobs — the XML doc is the CONTRACT a
  consumer reads, a `//` comment ANNOTATES the code beneath it, and the DESIGN argument lives in a record.
  Written because `src/` had reached **0.86 comment lines per line of real code** and carried 1.6× more
  prose than `DECISIONS.md` + `pitfalls.md` + the archive + the design contract combined — prose that no
  gate could see, in the one place none of them scanned.
  See `.claude/rules/RULES_INDEX.md` for the routing table.
- **`.claude/knowledge/`** (on-demand deep dives — read the one you're touching):
  `extending-lyntai.md` (the six extension points — provider, generation backend, storage backend, scorer,
  CLI tool-hosting dialect, migration), `llm-and-router.md` (verdict taxonomy, fallback §6 amended,
  streaming-commit + inactivity-clock invariants, CLI hygiene), `storage.md` (Dapper/CAST/FTS5
  trigram triggers/pragmas/`lyntai_` prefix), **`pitfalls.md` (traps that pass the build/tests while
  being wrong — read before extending)**, `generic-library.md` (turning a consumer ask into app-agnostic
  surface), `input-is-thinking-not-doctrine.md`, `library-api-design.md` (generalize the ask, never ship
  its shape), `sql-storage.md` (the SQL traps that return wrong data rather than failing), and
  `model-decoupling.md` (which model is a DEPLOYMENT choice, never part of a feature's definition).
- **`.claude/skills/`** — extension tasks (`add-provider`, `add-storage-backend`, `add-scorer`,
  `add-migration`), process (`archive-task` — move a finished task from `TASKS.md` to the archive), and
  workflow (`doc-loader`, `pattern-finder`, `post-feature`, `fix-log`, `caveman`).
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**
- **Backlog vs archive:** `TASKS.md` holds only OPEN tasks; completed work is moved to
  `docs/task-archive.md` (see `task-lifecycle.md`), and `CHANGELOG.md` is the release-facing log.
- Working files (probes, scratch) go under `devtools/_*` (gitignored), never OS temp.
- **This machine's console is GBK** — write files with the Write/Edit tools (in a script,
  `fs.writeFileSync` or `-Encoding utf8`, which adds a BOM on PowerShell 5); never `echo`/`Set-Content`
  UTF-8 through the console (it lossily mangles CJK/em-dashes). See `pitfalls.md` / `windows-machine.md`.

## Dev loop

- **`node devtools/dev.mjs verify`** — the "am I done?" gate, seventeen checks stopping at the first failure:
  **guard tests** → build → warnings → packages → bundle → **encoding** → **docs** → **links** →
  **counts** → **comments** → **decisions** → **decision claims** → **api vocabulary** → **samples** → test → e2e → leak scan. The summary line is DERIVED from
  the step list, so a gate added without updating prose still names itself. Run before
  claiming a change is complete. The guard tests run FIRST on purpose: nothing below that gate can be
  trusted if the gates themselves are broken.
  <br>**It also fails if the TREE CHANGED while it ran** — content-hashed before and after
  (`scripts/_tree-fingerprint.mjs`), every moved file named, the green summary suppressed and a non-zero
  exit. A verdict is only about the bytes the gates read, so an edit mid-run makes the whole report describe
  a tree that no longer exists, green line included. **So start it and keep your hands off the tree**; if
  you need to keep working, re-run it at the end. Added 2026-08-30 after that produced two false greens in
  one session. **And never read its exit code through a pipe** — `| tail` reports TAIL's status, which
  showed `exit code 0` for the very run that proved this guard red (`pitfalls.md` §Environment / tooling).
- `node devtools/dev.mjs build` — build the solution.
- `node devtools/dev.mjs check-packages` — **fail if a package is missing from any registry it needs** (part of
  `verify`). The NINE, in the order the gate checks them: `packableProjects`, the solution, the csproj's
  `<Description>`, `ApiSurfaceTests.Assemblies()`, its SEPARATE `Loaded` anchor map, the baseline file, the
  test project's `ProjectReference`, the `docs/AOT.md` row, the README table row — plus the reverse, so a
  deleted package leaves nothing stale behind. The misses are silent (no `ApiSurfaceTests` entry = no API
  gate at all). Many small packages is the intended shape — `DECISIONS.md` D27.
- `node devtools/dev.mjs check-bundle` — **fail if the `Lyntai` bundle's dependency closure drifted** (part of
  `verify`). The bundle forces every dependency on every one-line-install consumer (an untrimmed publish copies
  the whole graph), so membership is a budget: see `docs/DECISIONS.md` **D26** for the rule and
  `bundle.allowedThirdParty` in `devtools/project.config.mjs` for the approved list.
- `node devtools/dev.mjs check-warnings [--list]` — **fail if any `src/` project compiles with a warning** (part
  of `verify`). Not style policing: `IsAotCompatible=true` stamps `IsTrimmable` into the assembly, so an
  unfailed IL2026/IL3050 is a FALSE trim promise shipping to consumers (four did), and an unresolved doc cref
  ships inside the XML docs consumers read.
- `node devtools/dev.mjs check-encoding` — **fail if a tracked text file contains MOJIBAKE** (part of
  `verify`, and of the pre-commit hook). UTF-8 decoded as another codepage and written back: the file stays
  valid UTF-8, compiles, passes every test and ships, so **no other gate can see it**. Added 2026-08-13
  because the RULE already existed — `windows-machine.md` §Text and encoding — and was still broken **three
  times in one session**, each time caught only because a person or a diff happened to look. A rule that is
  written down and still violated is a missing gate, not a knowledge problem.
  Patterns are stored as CODE POINTS and re-derived in the test (`TextDecoder(enc).decode(...)`) rather than
  quoted, because writing them from memory produced the wrong characters three times — the same mistake the
  guard catches. That also means neither the guard nor its test contains the sequences it hunts, so the
  exclusion list is **empty**: an exclusion is a hole, and the fix was to stop needing one.
- `node devtools/dev.mjs check-docs` — **fail if a doc uses vocabulary a decision retired** (part of
  `verify`). The prose counterpart to `check-warnings`: the CODE is gated from every side while the DOCS are
  gated from none, so a spec paragraph that quietly stops being true survives everything and the next
  session reads it and implements the wrong thing — which happened twice on 2026-08-08, caught both times
  only by a human reading it. The registry is `retiredTerms` in `devtools/project.config.mjs`: a term, what
  to say instead, and why. **Add an entry whenever a decision renames or re-dimensions something.**
  Historical records (`CHANGELOG.md`, `docs/task-archive.md`) are exempt because they are accurate BY using
  the vocabulary of their day; the design records are untracked (`local/superpowers/`) and so are never
  scanned. Everything the gate DOES see is maintained state that has to keep being true. Put `drift-ok` on a
  line that deliberately names the retired thing.
  Unlike `decisions-index` this IS in `verify`: a stale index costs a reader one `Ctrl-F`, a stale contract
  costs an implementation.
- `node devtools/dev.mjs check-links` — **fail if a maintained doc names an in-repo path that is not there**
  (part of `verify`, beside `check-docs`). Its twin from the other side: `check-docs` asks whether a
  document still SAYS what a decision settled, this asks whether what it POINTS AT still exists.
  `docs/superpowers/INDEX.md` has always ended its archiving procedure with *"repoint every inbound
  reference, and check nothing dangles"* — and that step was simply skipped when the ranking × forgetting
  measurement record was untracked under **D43**, leaving **six** dead references in maintained state
  (README ×3, the design contract, `DECISIONS.md` ×2) plus two in `CHANGELOG.md`'s live `## Unreleased`
  prefix. Every gate reported clean; a reader found them. **A written-down rule that is still violated is a
  missing gate**, the same reasoning that produced `check-encoding`.
  It checks a reference **four ways**, because there are four ways one rots. The path half asks whether the
  target still exists. The **Part half** asks whether a reference naming a task record (`` `TASKS.md` <!-- link-ok: an ILLUSTRATION of the shape, not a claim about where Part 53 lives -->
  Part 53 ``) names the record that actually holds it — the path resolves and the Part exists, in the OTHER
  file, so nothing else can see it. **Archiving a task is what breaks these**, silently, for every inbound
  reference; five were live on 2026-08-14, in `CHANGELOG.md`'s Unreleased prefix and `docs/FIXES.md`. A bare
  `Part 53` with no record named is deliberately ignored — only a reference that NAMES one makes a checkable
  claim.
  <br>The **section half** (added 2026-08-28, Part 107) asks whether a `` `docs/memory.md` §7 `` names a
  heading that is there — the path resolves, the record is right, and only the `§` is dead. **Renumbering or
  FOLDING a section is what breaks these**: `docs/memory.md`'s `## 8. What is NOT measured` was folded into
  `## 7` with §9/§10 left un-renumbered, and seven citations across six files kept naming it, in `CLAUDE.md`,
  `dev.mjs`, the archive, the superpowers INDEX and two bench files. **It was MEASURED before it was built**,
  because the honest prior was discouraging (`pitfalls.md` records an all-paths existence check returning ~45
  hits and zero defects): over the pre-fix tree, 100 citations in the unambiguous form, 12 flagged, **8 real
  defects** and every false positive inside the historical archive. Two design consequences, both from that
  run — a **bold bullet lead** counts as an anchor (`` `task-lifecycle.md` §Keep the summary honest `` names
  one, and it was the only recurring false positive), and **every end of a RANGE is a claim**, since `§7–8`
  is invisible to a `§8` grep and resolves under a first-number-only rule. That range form escaped both the
  human pass that filed Part 107 and the first probe written to measure it. **Repointing is the fix and
  renumbering is the trap** — renumbering makes an existing citation resolve silently to the wrong section.
  <br>The **member half** (added 2026-08-30) asks whether a `` `Type.Member` `` citation — or a `see cref`
  naming one — points at an identifier that EXISTS. The other three are structurally blind to it: the path
  resolves, the record is right, the § is there, and only the member name is invented. It was measured the
  same way and the numbers are the best of the four: **770 citations, ONE flag**, and zero in the code tier,
  which is why that tier is included un-narrowed. Two design consequences, both forced by a run — the LEFT
  side must be a type this repository DECLARES (which drops `Lyntai.Bundle`, a package id, by principle
  rather than by an exclusion list, at the stated cost of never checking a BCL member), and the vocabulary is
  harvested from code with **comments STRIPPED**, because an index built from prose lets a citation
  AUTHORIZE ITSELF: write `Type.Nonexistent` in a `//` comment and the name joins the vocabulary, after which
  nothing can ever flag it. That second one was caught by the gate's own test, not by review.
  <br>**It now scans the CODE tiers too** (Part 72, decided 2026-08-15), but narrower than the prose scan on two
  axes: **comment lines only** (a path in a string literal is data the program uses, not a reference a reader
  follows) and **`docs/` targets only** (source files are renamed for legitimate reasons — `pitfalls.md`
  records an all-paths existence check returning ~45 hits and zero defects — while a moved DOCUMENT is the
  defect this gate exists for). The entry proposed a third narrowing, `///` XML docs only, and **the
  measurement refused it**: replaying the pre-repair tree, 9 genuine dead references lived in the code tiers,
  an XML-only rule catches 6, and all 3 it misses were in ordinary `//` comments and all 3 were real. Its
  hypothesis had been that `//` comments are where false positives live; every false positive was in fact a
  guard script naming a FIXTURE, which is what `link-ok` is for. Cost: **six annotations, once.**
  <br>**The `docs/`-only narrowing is the PATH half's alone**, and the section half deliberately does not
  take it: that narrowing exists because source files get renamed for good reasons, an argument that cannot
  apply to a citation whose target is a `.md` by construction. Three of the seven measured dead citations
  lived in the code tiers, and the narrowing would cost most of the tier's coverage: of the **42**
  §-citations those files carry, only **13** name `docs/` — the rest name `pitfalls.md` or another
  `.claude/` document, usually by bare basename.
  **EXISTENCE only, never line numbers** — a `file.cs:123` reference rots on the next edit for entirely
  legitimate reasons, and `pitfalls.md` records line numbers rotting twice and being deleted in favour of
  names; gating them would fail every refactor for no defect. `local/**` is skipped (untracked by design).
  Per-file escapes live in `staleReferenceAllowances` (`devtools/project.config.mjs`) with a reason, and
  **an allowance that stops matching FAILS** so exclusions cannot rot. A single line that deliberately names
  a path as data — a guard FIXTURE's name, say — takes `link-ok`, which is deliberately **not** `drift-ok`:
  one token silencing two unrelated gates is a hole nobody can see opening.
- `node devtools/dev.mjs check-samples [--list]` — **fail if a fenced `csharp` block in the docs does not
  COMPILE** (part of `verify`; 6.0s, which is why it is in rather than out beside `consumer-smoke`). Blocks
  are wrapped by shape and built against the real projects; **default-ON**, because an opt-IN marker makes
  coverage whatever someone remembered to tag. It is the only gate whose subject breaks from editing a `.md`
  alone, which is exactly what a session does right before running `verify`.
  Two annotations go before the fence. **`<!-- compile-given: <declarations> -->`** supplies the reader-side
  context a fragment assumes and keeps the block **compiled** — and the declarations are compiled too, so a
  given naming a nonexistent type fails like any other unresolved name, reported at the annotation's own
  line. It cannot be used to wave a sample through, only to supply a context that genuinely type-checks; 5
  of the first 12 written were wrong and said so. **`<!-- compile-skip: <reason> -->`** takes a block out,
  correct only where no context would help (a partial signature, a before/after pair, a menu of
  alternatives) or where the context needed is a whole program — the BYO-seams tour needs ~26 lines of given
  for a 16-line sample, and that is the line. Both on one block is an **error**, not a precedence rule.
  `<!-- compile-skip-file: … -->` opts out a historical document. Prefer `compile-given`: a skip is
  unchecked, a given is checked.
  Measured cost of not having it: a README sample passing `task:` where the parameter is `taskKey`, and four
  passing `/* … */` where an argument is required — a consumer copying either gets a compile error. **Two
  ways the gate itself lied on its first runs** are in `pitfalls.md`: Roslyn binds NOTHING in a compilation
  carrying a syntax error (so 11 samples naming nonexistent types were reported as compiling), and a type
  declared in a sample OUTRANKS the same type from a referenced assembly compilation-wide (`CS0436` is only
  a warning).
- `node devtools/dev.mjs check-counts` — **fail if a COUNT written in prose disagrees with the tree** (part
  of `verify`, beside `check-docs` and `check-links`). The THIRD member of that family: `check-docs` asks
  whether a document still SAYS what a decision settled, `check-links` whether what it POINTS AT still
  exists, this whether what it COUNTS is still true. `check-docs` structurally cannot see it — its registry
  holds vocabulary a decision RETIRED, and a count going stale retires nothing, so the sentence stays
  grammatical, plausible and wrong.
  Measured cost of not having it, and it is the strongest case any gate here has: `docs/task-archive.md`
  Part 73 found **six** corrections to a counted claim inside sixty commits, and **two more went stale during
  the session that built the gate** — eight incidents, every one caught by a person, four of them by a person who
  happened to be counting something else. Registry is `COUNTED_CLAIMS` in the script itself rather than
  `project.config.mjs`, because an entry is a regex plus a FUNCTION over the tree and that file is data.
  Line escape is **`count-ok`**, deliberately not `drift-ok` — one token silencing two unrelated gates is a
  hole nobody can see opening, the same reasoning `link-ok` carries.
  **Its honest limit, stated rather than discovered: it only covers counts somebody REGISTERED**, so it is a
  gate against recurrence and not a proof that every number in the docs is right. Two rules keep the
  registry from rotting: a claim whose pattern matches NOTHING fails (an entry that cannot expire), and a
  counter that computes nothing is reported as a BROKEN GATE rather than as stale prose — because "fix the
  number" is wrong advice when the counter is what failed.
  **Every counter is pinned by a test against the real tree**, which is not ceremony: the first
  `verify`-gate counter returned the RIGHT total from two cancelling errors (its character class could not
  match `e2e`, and it counted the inner `['--tree']` argument array as a step). Only comparing the parsed
  NAMES caught it — the literal case Part 73 predicted when it said a subtly-wrong counter is worse than none.
- `node devtools/dev.mjs check-comments` — **fail if a comment block outgrows what it explains** (part of
  `verify`, and the FOURTH member of the prose family). Its siblings ask whether a document still SAYS what
  a decision settled, whether what it POINTS AT exists, and whether what it COUNTS is true — all of
  MAINTAINED prose. This one asks whether a COMMENT is still doing a comment's job, and it is the only one
  that looks at CODE — all four tiers (`src`, `tests`, `devtools`, `bench`), `.cs` and `.mjs`.
  Measured cost of not having it: **0.86 comment lines per line of real code** in `src/` (now 0.65), and
  `src/` carrying 1.6× more prose than `DECISIONS.md` + `pitfalls.md` + the task archive + the design
  contract COMBINED — prose in the one place no other gate scanned, rotting exactly as `pitfalls.md`
  records. The rule it enforces is `.claude/rules/code-commentary.md`: **the XML doc is the CONTRACT, a `//`
  comment ANNOTATES the code beneath it, and the DESIGN argument belongs in a record.**
  <br>**It fails three other things besides length**, each added because a real defect walked past it: a doc
  run carrying more than one `<summary>` (eight found — a long block describing member A sitting above
  member B's own summary, so B has two and A has none; the compiler does not warn and it SHIPS), punctuation
  a deleted clause left stranded (two shipped in public XML docs), and an allowance that no longer matches.
  **It is a RATCHET, not a threshold.** 49 files were already over the 25-line limit when it landed (1879
  lines), so a plain threshold would have been switched off on day one. Every over-limit block in a file is
  recorded in `commentBlockAllowances` (`devtools/project.config.mjs`) — the MULTISET, not just the file's
  worst, because one number per file left 279 lines of debt invisible and let a budgeted file grow new long
  blocks behind it. **An allowance looser than the file needs FAILS** — so the numbers only ever come down,
  and a regression back to an old size is caught.
  Paying one down means moving the design argument to the record that owns it and keeping the RULE plus a
  pointer; deleting the entry is the goal, not an edge case. Line escape is **`comment-ok`** on a block's
  first line, deliberately not `drift-ok`.
- `node devtools/dev.mjs check-decisions` — **fail if a `docs/DECISIONS.md` entry outgrows the decision**
  (part of `verify`). The second length ratchet, pointed at the record rather than the code. Measured
  2026-08-28: mean non-blank lines per entry went **11.6** across the log's first third → **14.2** across
  the second → **38.5** across the last, a 3.3× growth in the file this one routes every session to BY
  RANGE. Not the entry count and not data — that last third holds 40 table rows and 19 lines carrying a
  figure, in 1556 lines. It is
  prose: amendment narrative written in place, and several decisions stacked under one number.
  `persist-working-state.md` had already recorded the FIRST version of this and answered it with a rule;
  that rule held on entry COUNT and did nothing about LENGTH, which is the argument for a gate.
  **It is NOT the archive's compression** — a decision's reasoning is its payload, so the gate bounds an
  entry and never asks for one to be summarized away. Paying one down moves MEASUREMENT narrative to the
  design record that owns it and AMENDMENT narrative to git history. Limit **35** non-blank lines (median
  16, p75 35); 21 entries were already over it, so it is a ratchet like `check-comments` —
  `decisionLengthAllowances`, where an allowance looser than the entry needs FAILS. **No escape token**,
  deliberately: an allowance is a visible ratcheted number and is the only way out.
- `node devtools/dev.mjs check-decision-claims` — **fail if a DECISION stops describing the code it
  governs** (part of `verify`). Its sibling above gates an entry's LENGTH; this gates its TRUTH, and it is
  the FIFTH member of the prose family — `check-docs` asks whether a document still SAYS what a decision
  settled, `check-links` whether what it POINTS AT exists, `check-counts` whether what it COUNTS is true,
  `check-comments` whether a comment is still doing a comment's job, and this whether a decision is still
  true of the tree. **None of the other four can see it**: a decision going stale retires no vocabulary,
  dangles no path and moves no registered count, so the sentence stays grammatical, plausible and wrong —
  and `decisions-index` renders a stale TITLE into the index table on top of that.
  Measured cost of not having it (2026-08-31, `TASKS.md` Part 129): auditing the log against the tree found
  it **accurate about VALUES and drifting on COUNTS and CLASSIFICATIONS** — every stated constant verified,
  while D46's own title said "four DOMAINS" against seven and this very file claimed five required
  `IMemoryGraphStore` members against **13**, having dropped the "in this major" qualifier D67 carries.
  Registry is `DECISION_CLAIMS` in the script rather than `project.config.mjs`, because an entry is a
  predicate over the tree and that file is data — the same reasoning `check-counts` carries.
  **Every registered predicate was verified BY HAND before being registered**, and each is driven RED by a
  synthesized tree in its own test: a predicate nobody checked is a second unverified claim, not a gate.
  **Its honest limit**: it covers claims somebody registered, so it is a gate against recurrence rather than
  a proof that every decision is true — and prose claims with no extractable shape are invisible to it.
- `node devtools/dev.mjs check-api-vocabulary` — **fail if the FROZEN public surface reintroduces a name a
  decision retired** (part of `verify`, beside `check-docs`). The two are twins: one asks whether the PROSE
  still says what a decision settled, the other asks it of the SURFACE. It exists because the API baseline
  **records parameter names without judging them** — it reports THAT a name changed, never that one SHOULD
  have — so three retired parameter names reached the eve of the 3.0 freeze and a human, not a gate, caught
  them. (`check-docs` excluded `src/` entirely back then; it now scans code COMMENTS in `src`, `tests` and
  `bench`, which is a different half — a comment is prose, a parameter name is surface.) Registry is `retiredApiNames` in
  `devtools/project.config.mjs` (deliberately NOT `retiredTerms`: prose needs loose patterns, a baseline
  needs whole-identifier equality). Escapes live in the registry, since a generated file cannot hold a
  `drift-ok`, and **an allowance that matches nothing FAILS** so exclusions cannot rot.
  **Its limit, stated rather than oversold:** it catches reintroduction of an EXACT retired identifier, not
  every descendant of a retired word — a rule naming `ISalienceAppraiser` would not have caught the method
  `Appraise`, which is how that one survived.
- `node devtools/dev.mjs test-devtools` — **the guards' own tests** (`node --test`, no dependency), FIRST in
  `verify`. Added because three `check-docs` defects had passed every gate for their whole lifetime, all
  three failing in the PERMISSIVE direction: *a guard whose failure mode is a false PASS cannot be validated
  by running it.* Writing them found two more, one of which meant `check-sensitive --tree` silently skipped
  any file with a non-ASCII NAME — in this repo, `docs/灵台.md` could have carried a leak past a "clean" run. <!-- link-ok: a guard FIXTURE's name, never a file here -->
  The three measured defects are pinned as regression tests and each was mutation-checked against the old
  behaviour. **Every guard script now has a test**; what is left untested is the three deterministic STUBS
  (`provider-stub`, `codex-stub`) and the live pack/restore/build/run of `consumer-smoke` itself, which is
  the honest line — a seam that stubbed the pack would test the bookkeeping and none of the risk.
- `node devtools/dev.mjs test [args]` — run the xUnit tests.
- `node devtools/dev.mjs e2e [pN|all] [--build] [--parallel]` — boot `Lyntai.Playground` against the
  deterministic provider-stub (`LYNTAI_PROVIDER_CMD`) over isolated `devtools/_e2e-*` data folders.
- `node devtools/dev.mjs new-migration <name>` — scaffold the next FluentMigrator migration (unique number).
- `node devtools/dev.mjs new-package <Lyntai.X> [--description "…"]` — scaffold an adapter package (csproj +
  its `Add*` entry point) and register it in all **nine** registries `check-packages` gates. Bundle membership
  is deliberately NOT automatic (D26). See `DECISIONS.md` D27.
- `node devtools/dev.mjs playground` — run the sample console app.
- `node devtools/dev.mjs bench [-- --filter *X*]` — BenchmarkDotNet (Release) router/FTS benchmarks.
- `node devtools/dev.mjs memory-sweep` — replays the deterministic memory corpus against a LIVE SQLite graph
  engine under the four `{ranking x forgetting}` policy arms and prints a miss-rate/pollution-rate table
  (`bench/Lyntai.Benchmarks/MemoryPolicySweep.cs`). Not a benchmark — `--sweep` is branched on in `Program.cs`
  **before** `BenchmarkSwitcher` ever runs, because BDN measures wall-clock time per operation, not recall
  quality. Slow by design (a fresh migrated SQLite db per arm × shape), so deliberately NOT in `verify` — the
  same reasoning `consumer-smoke` carries.
  **Its corpus holds neither authoritative material nor an authored headline BY DEFAULT** — `CorpusShape.AuthoritativeCount` is opt-in and
  `0` unless a caller asks, so the sweep's arms are byte-identical to their pre-3.0 selves and any change to
  grade behaviour is still unreachable *there*. An unchanged sweep number is "not exercised", never "no
  regression". The promise itself is measured by `MemoryAuthoritativeSurvivalTests` (five languages + a
  control), which is what found objective (1) broken — `DECISIONS.md` D56, and `pitfalls.md` for why a
  documented blind spot is still blind.
- `node devtools/dev.mjs memory-language` — the same harness over ONE factor: **`CorpusLanguage`**
  (`bench/Lyntai.Benchmarks/MemoryLanguageSweep.cs`), now **FIVE arms — English / Chinese / Japanese /
  Korean / `ChineseMixed`** (the roster is `Enum.GetValues<CorpusLanguage>()`, so it is whatever that enum
  declares; this line said four until 2026-08-14). Every
  recall-quality figure this repository published before 2026-08-12 was measured on English, space-separated
  text — the friendliest tokenization the library supports, recorded as a blind spot in design §5.7.0 and
  never measured. `MemoryCorpus` takes `CorpusShape.Language` (default `English`, **byte-identical when
  unset** — proved by the goldens in `MemoryCorpusGoldenTests` that were captured BEFORE the axis existed
  and did not move when it landed; that file now pins **eight** golden shapes in all, the sixth for the
  routine class, the seventh for its STANDING answer arm and the eighth for its SETTLE gap, all of which
  postdate the axis and so pin only their own shape).
  Every arm replays a **structurally identical** corpus — same steps, same ids, same ground truth, only the text
  differs — so a gap is the LANGUAGE and not the timeline; pinned in the corpus tests AND re-checked per cell
  at run time. Adopts nothing: the language is the consumer's, not a setting. See `DECISIONS.md` **D55**.
  <br>**The four non-English arms are not interchangeable, and that is the point of having four.** Chinese is a
  spaceless run of Han characters; Japanese is a spaceless run MIXING kanji/hiragana/katakana, where kana's
  small inventory makes trigram collisions likelier; **Korean WRITES SPACES** and is expanded anyway because
  Hangul sits in `SearchTerms`' spaceless range — defensible only because Korean is agglutinative (배우자는 /
  배우자의 share the stem), so it is the arm where the expansion would first cost more than it recovers.
  `CorpusLexicon.WritesWordSpaces` is what keeps that difference assertable instead of assumed.
  **`ChineseMixed` is the fifth and the one closest to real deployment** — Chinese technical prose with
  English terms embedded WITHOUT spaces (`部署pipeline`), which is where a Latin word inside a CJK run used to
  be shredded into fragments that are words in no language. Every other arm is monolingual prose plus ASCII
  ids, so it exercises the script boundary only at a token edge; this one puts it mid-run, where the defect
  lived.
- `node devtools/dev.mjs memory-spacing` — the same harness, same corpus, same seed pairing, over ONE factor:
  `DsrOptions.SpacingWeight` (`bench/Lyntai.Benchmarks/MemorySpacingSweep.cs`). It asks whether the `topical`
  regression D49 shipped knowingly is even RESPONSIVE to the knob `TASKS.md` Part 56 named as its suspect, and
  **adopts nothing** — that is what makes it legitimate where parameter FITTING is not. Fitting `DsrOptions`
  against this library's own review log is circular by construction (the grade is a function of the model's
  own prediction, and the log can only ever contain successes) — `DECISIONS.md` **D51**'s 2026-08-12
  amendment. A sensitivity curve makes no claim about the true value, so neither objection touches it. Tens of
  minutes, so out of `verify` for the same reason.
- **Every OTHER `memory-*` command is a one-factor sweep**, all out of `verify` for the
  same cost reason, and all listed here because a roster naming a SUBSET is how a reader learns the roster is
  not one (the same drift `dev.mjs`'s own usage line had, fixed in 208a7ca — on
  `backup/pre-squash-2026-08-14`, D61). **The authoritative list is `node devtools/dev.mjs` with no
  argument**, which derives it. Three of them are exceptions to the sentence above and each is exceptional in
  a different way: `memory-sweep` (the 2×2) is not one-factor; **`memory-scale` does not use that
  corpus harness at all** — its subject is COST, so it generates plain entries and reports no miss or
  pollution; and **`memory-support` is not one-factor either** — it crosses rule × θ × clock ×
  `ConnectionBoost` curve, carries a second `--screen` mode for the model ladder, and has an additive model
  arm that prints an explicit SKIPPED line rather than a silent cap (`docs/memory.md` §5, **D94**).
  This paragraph said "six more … three of nine" while
  enumerating seven of ten, then led with "Seven more" while enumerating eight — twice the same drift, which
  is why it now names no number at all:
  `memory-reinforcement` isolates law 3's
  `r`-dependence from reinforcement MAGNITUDE, which `memory-spacing`'s knob provably cannot separate;
  `memory-bounded` varies the FORM of the growth rule rather than its constants, and is what decided
  `DsrOptions.ReinforceGain` for 3.0; `memory-salience` holds enrichment constant so only salience varies —
  the first measurement of a default that ships ON for two of its three consumers; `memory-annotation`
  measures subject linking with a PERFECT annotator, so its numbers are the mechanism's CEILING rather than
  any model's accuracy. **`memory-verification`** (2026-08-15) is the same stance for the judge seam and is
  the FIRST measurement of what a model in the loop is worth — every other figure here is model-free — and
  **`memory-fan`** is the axis that measured ACT-R's fan effect and REFUSED it (**D62**).
  <br>**`memory-importance`** (2026-08-27) is the pair to `memory-salience-weight` and asks the other half of
  its question: that one priced how LOUD salience is, this one prices WHAT IT MEASURES — the shipped novelty
  policy against a perfect importance ORACLE, salience-off as control. **D89 is not a finding about
  importance**: novelty is monotone in "unlike anything already stored", so sustained significance decays on
  that axis exactly as it is confirmed while a one-off triviality reads as maximal. Its decisive shape is
  `diverse-noise`, because under templated noise the second entry onward reads as FAMILIAR and the failure
  mode is unreachable by construction. Ranking is held at the shipped `SalienceWeight = 0`, so it prices
  SURVIVAL — decay resistance and store admission — which is D45's actual claim for what salience means.
  <br>**It needed no library change, and finding that out cost a wrong turn worth recording**: a policy
  already receives the whole `MemoryWrite`, so content and caller metadata were always available.
  `SalienceContext` carries only what the ENGINE measured — the engine name, novelty, comparables and
  `SimilarCount` — and reading that record ALONE says a policy can judge nothing else, which is what the seam
  looks like until you read the method signature.
  <br>**`memory-density`** (2026-08-27) is shaped as a REFUTATION rather than a study, and is the cheapest run
  in this roster: does a CORRECTION separate from a RECURRENCE on `SalienceContext.SimilarCount` at all? A
  correction resembles exactly ONE stored entry and a recurrence resembles MANY, so **pairwise similarity
  cannot tell them apart** — only the count above `MinSimilarity` can. Authored fixtures and a real embedder,
  no corpus and no tier, so a negative result would kill a whole design for the price of one run. It measured
  a pooled AUC of **1.000** over the five writing systems against a `novel` control.
  <br>**Read that as a FLOOR, which its own "what this does NOT settle" says outright**: `SimilarityK = 5`
  and `MinSimilarity = 0.6` bound the count and are both unmeasured, so a ceiling AND a floor effect are
  live, and the best threshold it reports is `SimilarityK + 1` — the SEARCH WINDOW, where `recurrence`
  saturates — rather than a learned boundary. Separability on authored fixtures is not separability on a real
  corpus. Its one non-obvious control is that every population's store is padded to the SAME total from one
  shared distractor pool: short of `SimilarityK + 1` entries it is the STORE and not the embedder that caps
  `SimilarCount`, which is a way to measure the fixture rather than the signal.
  **`memory-enrichment`** (2026-08-15) answered the oldest open question in that backlog — WHY registering an
  embedder costs recall quality — and was the **first sweep to call a REAL model**, EXITING rather than
  substituting a double, because the arm it replaces was measured through a bag-of-words fake in which
  "semantic similarity" IS word overlap. Its answer is that the two write-time mechanisms have different
  SHAPES, which is why one number never explained it: **similarity linking is a REDISTRIBUTION** (`topical`
  −0.30, `attribute` −0.28, `critical-rare` **+0.68**) whose aggregate looks small only because those
  cancel, while **novelty→salience is a broad shallow cost** that turns beneficial only under high noise.
  It needed no new API — `MinSimilarity` above 1 keeps the embed and writes no edge; a neutral salience
  policy keeps the edges and drops novelty. **That first recipe now also zeroes `SalienceContext.SimilarCount`**,
  which counts against the SAME floor: a registered salience policy reads `0` on every write and cannot tell
  that from a store nothing resembles. Novelty is unaffected — it reads the probe's top score, not the floor.
  <br>**`memory-salience-weight`** (2026-08-23) is the SECOND real-model sweep, and it needs one for a
  sharper reason than cost: without an embedder salience declines on every write, and RRF ranks by
  COMPETITION (**D82**), so a uniformly-tied signal contributes the same constant at every weight — the curve
  would be flat as an ARTIFACT with every ordinary control green. It therefore reports **distinct salience
  values**, not how often salience fired, and refuses to interpret its own table when that count is 1.
  Its finding: `SalienceWeight = 0` beats the shipped `1.0` on every shape, so salience's RANKING voice is a
  net cost and **D45's argument was right without a measurement**. The default did NOT move on one run.
  <br>**`memory-scale`** (2026-08-26) is the odd one out and says so in its own header: **its subject is
  COST, not recall quality**, so it reports latency, throughput and bytes and has no ground truth at all.
  It closes the blind spot `docs/memory.md` §7 conceded outright — nothing exceeded a few hundred
  entries — which `MemoryRecallBenchmarks` did NOT already cover: that one runs 1k/10k/100k against
  `SqliteMemoryStore`, the KEYWORD store, so the graph engine's own write and read paths were unmeasured at
  any size. Two arms (`shipped` / `read-only`) exist to SPLIT a default recall's latency into the read and
  the write-back it performs afterwards. It runs **sequentially** where every other sweep fans out, because
  contention cannot bias a rate and biases a latency silently — and it reports a **hit-rate** control,
  because a recall matching nothing is fast and a table of fast empty recalls reads as good news.
  `node devtools/dev.mjs` with no argument is the authoritative list; this section is
  a curated one and says why each entry earns its place.
- **`memory-locomo` and `memory-longmemeval` are NOT sweeps and belong to a different family** — they are
  the FIELD's benchmarks, measured on data this repository did not build, and they are named here because
  the paragraph above says outright that a roster listing a subset teaches a reader the roster is not one.
  Both are model-free (the dataset names the evidence turn, so no reader and no judge can be credited or
  blamed), both need a dataset the command prints a `curl` for, and neither is in `verify`.
  <br>**They answer opposite questions, which is why both exist.** LoCoMo spreads its questions uniformly
  over months of history, so it rewards a perfect archive and penalises forgetting BY CONSTRUCTION — it is a
  differential instrument rather than a scoreboard, and it earned its place by exposing **D97**, which 3429
  tests were blind to. `memory-longmemeval` is the shape this design actually claims: prefer a revised fact
  over the one it superseded. **Run it with `--haystack`.** The oracle variant returns 40% of its store at
  `k = 10`, so it barely tests retrieval — and it is biased per class in a direction nobody predicted
  correctly (`docs/task-archive.md` Part 112), which is exactly why a cheap variant is not a safe default.
- `node devtools/dev.mjs pack` — `dotnet pack` the libraries → `publish/packages/`.
- `node devtools/dev.mjs consumer-smoke` — **the release gate**: packs every package to a scratch feed under a
  throwaway version, then restores + builds + runs a fresh console app against the PACKAGES (not project
  references). The only check that exercises what actually ships — nuspecs, dependency groups, symbol packages,
  the bundle restore. Minutes, so deliberately NOT in `verify`; run before a release or after touching packaging.
- `node devtools/dev.mjs check-sensitive [--tree]` — leak scan.
- `node devtools/dev.mjs doctor [--fix]` — THREE version checks, all reported in one pass: README `## Status`
  ↔ `VersionPrefix`, **CLAUDE.md's `**Released:**` claim ↔ `VersionPrefix` and ↔ the CHANGELOG heading's
  date**, and `VersionPrefix` ↔ the newest `v*` tag (**never hand-edit the version** — see
  `repo-mechanics.md` §Never hand-edit the version / `DECISIONS.md` D19).
  <br>The middle one was added 2026-08-30, after this file announced **v3.0.0** for a whole release while the
  other two agreed on 3.1.0: they check each other and CLAUDE.md was in neither, so the one copy auto-loaded
  into every session was the one nothing held. It checks the DATE too, because the half-fix — bump the
  version, leave the date — reads as synced. Check-only, and deliberately NOT in `verify`: the release
  workflow bumps `VersionPrefix` before anything stamps the CHANGELOG, and a gate that is red during every
  release is one people learn to skip.
- `node devtools/dev.mjs check-version` — the pre-commit version-authorship guard, run by hand.
- `node devtools/dev.mjs decisions-index [--check]` — regenerate the index table at the top of
  `docs/DECISIONS.md` from its own headings. **Run it after adding a `D<n>` entry** (`--check` reports
  staleness without writing). Deliberately NOT in `verify`: a stale index costs a reader one `Ctrl-F`, and
  `verify` stays the build/test gate rather than growing a documentation check.
