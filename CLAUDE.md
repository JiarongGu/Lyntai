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

**Released: v2.5.0.** Twelve packages; public API frozen under SemVer 2.0 since 1.0, **with no carve-out** —
the one that existed (the `Lyntai.Generation` PACKAGE, from 2.0.1) was withdrawn in 3.0, `docs/DECISIONS.md`
**D70**.

The whole pre-1.0 line shipped (routing depth, LLM-ops, three storage backends, BYO
resource seams, local GGUF, agentic tool-calling native + prompt, MCP both directions, durable jobs, the §9
platform kit, OTel, governance decorators, semantic + curated memory, the agent-session primitive), then
**1.0** froze the API, **1.1** generalized CLI tool-hosting, **1.2** added turn-free backend probe/auth +
pinned self-install, **2.0.1** landed the generation platform and the package graph it needed, **2.1.0**
made the generation backends registerable in one line each, **2.2.0** shipped the provider-lifetime seam
(`Lyntai.Lifecycle`; D30) with `LlmVerdict.NotConfigured` (D31), `AddSemanticMemory` (D34), honest
`MigrateUpAsync` twins (D33) and `CodexAgentSession` (D35), **2.3.0** carried the pre-release whole-library
review that 2.2.0 shipped without (D18, D37), **2.4.0** gave an agent session the host's own MCP servers on
either CLI backend (D38), and **2.5.0** shipped the **long-term memory subsystem**. Per-release detail is
`CHANGELOG.md`; the reasoning is `docs/DECISIONS.md` (D1–D74 — the memory subsystem is **D39–D41**,
**D45–D62**, **D63** and **D72**; D42–D44 are the doc and packaging decisions that landed beside it, not
memory ones). **D67–D74 are the 3.0 work that came AFTER the pre-freeze review** and are the ones a session
is most likely to have stale assumptions about: the generation stream door (**D67**), the accelerator-derived
diffusion ceiling (**D68**), every unmeasured generation mapping becoming a host OPTION (**D69**), the
withdrawal of the generation SemVer exemption (**D70**), tool calls on the streaming contract (**D71**), and
the forget/prune capability split with the `IMemoryReapPolicy` seam (**D72**), and the cross-process
job cap as a heartbeated slot table (**D73**), and the retirement of "native tool-calling for
ClaudeCli/Local" as misframed (**D74**).

**Long-term memory (2.5.0) is the newest subsystem** and the one a session is most likely to reason about
wrongly, because it is not the three older memory surfaces: named engines resolved by name like
`IHttpClientFactory` (`IMemoryEngine` / `IMemoryEngineFactory` / `AddMemoryEngine`; **D39**), a graph engine
whose entries decay, connect and open as a cheap index (`UseGraph()`), decay measured in **interference
rather than elapsed time** with the age policy as a seam (**D40**), **burial rather than deletion** (**D41**),
InMemory + SQLite + Postgres backends under one contract, and `AddMemoryTools` exposing recall/expand to the
model. It was purely additive — `IMemoryStore`, `ISemanticMemory` and `ICuratedMemoryStore` are unchanged and
co-exist with it. The contract is design §5.7.

**`## Unreleased` is NOT empty, and the memory subsystem has been RESHAPED in it — read `### Breaking`
before assuming any memory behaviour.** The eleven facts a fresh session most needs — seven from the
reshape itself, then four from the pre-freeze sweep below:

1. **Every seam was renamed to one shape, `IMemory<Domain>Policy`** (**D47**). `IMemoryClock` →
   `IMemoryAgePolicy` (there was never a clock — age is *interference*), `IRetrievabilityPolicy` →
   `IMemoryRetrievabilityPolicy`, `IRetentionModulator` → `IMemoryRetentionPolicy`, `ISalienceAppraiser` →
   `IMemorySaliencePolicy`.
2. **A seam is SINGULAR or PLURAL depending on whether its implementations read the same aspect** (**D48**).
   Age, salience and retention are **plural** — implementations coexist and each plural domain owns a
   **composition policy** (the engine composes nothing itself). Retrievability and ranking stay **singular**;
   `CompositeRankingPolicy` is ONE policy built from two, not two running side by side.
3. **Age is DERIVED, not stored** — nodes carry policy-independent primitives (encoding ordinal, cumulative
   characters, timestamp) and each policy projects its own view. **Except** `BurstDampenedAgePolicy`, which
   declares `MemoryAgeKind.Accumulating` because its position is path-dependent; it keeps the accumulator.
   That policy is the shipped default, so the default path is unchanged.
4. **`Stability` has ONE meaning, enforced by a contract fact**: the position delta at which retrievability
   is `0.5`. FSRS anchors at 90% — adopting that convention would silently reinterpret every stored value,
   so the fact exists to make it unshippable. `Reinforce` now returns **state**, not a `double`, so a richer
   model has somewhere to put what it owns.
5. **Each entry records WHICH policy computed its state** (`provenance_retrievability` / `provenance_salience`,
   flags in `MemoryProvenance`), so "never computed" is distinguishable from "zero".

6. **BOTH memory defaults changed, and one curve was DELETED** (**D49**, amended twice). `DsrRetrievability`
   (FSRS's power law) is the only shipped forgetting curve — `HalfLifeRetrievability`/`HalfLifeOptions` are
   **gone, with no restore path**, because their central `× 1.5` reinforcement was admittedly unmeasured and
   measured compounding to **2.1×** over a four-touch batch. `ReciprocalRankFusionPolicy` is now the
   **registered ranking default** (RRF beat `MultiplicativeRankingPolicy` on `topical` in all six shapes);
   Multiplicative stays shipped and is one line to restore. **No data migration** either way — `Stability`'s
   unit contract is what made deleting a curve free.
7. **FSRS's difficulty axis is LIVE** — `Reinforce` maintains `MemoryDecayState.Difficulty` per review,
   deriving a grade from retrievability-at-recall. **Neutral is `5`, not `1`**: `1` is FSRS's floor
   ("easiest possible"), which pinned the axis at the clamp so it could never vary. Reviews are logged
   (`GraphMemoryOptions.LogReviews`) so parameters can be fitted later; nothing reads that log at runtime.

Also: `GraphMemoryOptions` lost `HopAttenuation`, `RelativeFloor` and `SalienceRankWeight` to
`MultiplicativeRankingOptions` (`Lyntai.Memory.Ranking`). And `IMemoryGraphStore` gained **FIVE required
members, none with a default body** — `DeleteAsync`, `RecordReviewsAsync`, `ReviewsAsync`,
`RecordSubjectsAsync`, `NodesBySubjectAsync` — so a BYO store must implement all five. The subject pair came
later than the other three, which is why this line said "three" until 2026-08-14; the only 3.0 addition that
is NOT required is `KnownSubjectsAsync`, which defaults to an empty list.

**Four more facts landed in the 3.0 pre-freeze sweep (`docs/DECISIONS.md` D52 and D56):**

8. **An EDGE now carries the same three age primitives a node does**, so `StrengthAge` is swap-safe too and
   `PruneAsync`'s derivable path is EXACT rather than conservative. Before this, `Age` re-derived from the
   primitives while `StrengthAge` stayed the raw accumulator — two units in one retrievability expression for
   any `Derivable` age policy (three of the four shipped) — and the engine compensated by refusing to delete
   ANY connected entry, leaving a genuinely unretrievable one unreapable forever. `GraphNode` gained
   `StrengthOrdinalAge`/`StrengthVolumeAge`/`StrengthElapsedAge` + `StrengthAgeSample`, and `GraphNeighbour`
   gained the same three + `EdgeAgeSample` — **all THREE age axes now speak one unit**, so
   `GraphMemoryOptions.EdgeHalfLife` is denominated in whatever the policies count. **The shipped
   `Accumulating` default is byte-identical on every axis.**
9. **3.0 ships ONE memory RETENTION migration**, `M202608121100_MemoryRetentionModel` — the six that landed
   after `v2.5.0` were folded into it under D9 (none was ever released), so a fresh database applies **12**
   migrations. `M202608081215_MemoryGraph` is deliberately NOT folded in: it shipped in 2.5.0, and editing a
   migration a database has already recorded by NUMBER is silently skipped. The schema goldens, captured
   pre-squash, still match — that is the proof, not an assertion.
   <br>The twelfth is `M202608161159_JobSlots`, the cross-process concurrency semaphore (**D73**) — a
   JOBS-feature migration that lands on BOTH backends, so it moves the two counts together and leaves the
   asymmetry below unchanged.
   <br>**The count is 12 on SQLite and 13 on POSTGRES**, and the asymmetry is deliberate:
   `M202608152310_MemoryHeadlineSearch` adds a trigram index on `headline` so recall can match an authored
   one without a sequential scan, and SQLite needs no counterpart because its FTS5 mirror has indexed
   `headline, content` since the graph store shipped. Migrations are per-backend projects; forcing the two
   numbers to match would mean shipping a SQLite migration that does nothing. It is its OWN migration rather
   than a line in the retention one precisely so that one's goldens keep proving the fold.
10. **An authoritative fact the query did not match reports `Relevance` 0 on every backend.** All three used
    to disagree and SQLite disagreed with itself (FTS path: tail; substring path: head). Admission is
    unaffected — it comes from the grade carve-out and the engine's re-admission, never from relevance.
11. **An authoritative fact now takes a slot WITHIN a recall's limit and displaces ordinary hits** (**D56**).
    Through 2.5.x it was appended after the ranked set and cut by `Take(limit)` — documented in four places,
    and wrong: design §5.7.0 makes "never lose an authoritative fact" objective (1), the ONLY objective with
    no acceptable failure rate. It had never been measured (the corpus held zero grades); the first
    measurement lost all three facts in all five languages. `GraphMemoryOptions.AuthoritativeReserve` bounds
    how many slots exact facts may take (`null` = unbounded = default; `0` restores 2.5 and re-breaks the
    objective). **Do not "fix" a small-limit recall returning fewer ordinary hits — that is the promise
    working.** Guarded by `MemoryAuthoritativeSurvivalTests` + a control that requires the same facts to be
    LOST without the grade.

**A 2.5 consumer's ordered upgrade path is `docs/migration-2.5-to-3.0.md`** — point them there rather than
reconstructing it from `CHANGELOG.md`, whose `## Unreleased` records each change as it landed and therefore
contains entries later ones supersede.

**The packaging rules are now gated, not remembered** — `verify` runs fourteen checks, four of them added at
2.0.1 and `check-docs` added with the memory work (a doc that uses vocabulary a decision retired fails the
build — the prose counterpart to `check-warnings`; **D42**): `check-warnings` (a warning in a published project fails the build, because an unfailed IL2026 is a
FALSE trim promise), `check-packages` (a package must be registered in all nine registries — a missing
`ApiSurfaceTests` entry means no API gate at all), `check-bundle` (the bundle's dependency closure cannot
grow without a decision), plus `consumer-smoke` outside `verify` (pack, then restore/build/run a fresh app
against the PACKAGES). Adding a package is `node devtools/dev.mjs new-package <Lyntai.X>`.

Tests/e2e green: **2997 passed / 3018 total, 21 skipped** (live-backend only — Ollama, MCP, a real CLI, a
real annotating/judging model, a real embedder), e2e 3/3, guard-script tests 345/345, doc samples 76/76.
**A skip count WELL above 21 means Docker is down and the whole
Postgres leg is silently unexercised** — start it and re-run before believing a green suite (archive Part 58,
which caught a missing table exactly that way; it happened again on 2026-08-12, which is why the count above
is worth comparing against). The old form of this line named a specific Docker-down figure; it is now a
RELATION rather than a number, because Part 70 turned the Postgres contract from one test into 69 theory
cases and any figure quoted here would be one restructure from wrong — and a wrong number is what teaches a
reader to stop comparing. **Re-measure these four numbers whenever you change them** — all four had gone
stale by the 3.0 pre-freeze sweep (2652/2664/12, 225, 58), and a stale baseline is worse than none here: the
whole point is that a reader can compare, and a count that no longer matches a green run teaches them to stop
comparing.

**The records, and what each is for:**
- `docs/2026-07-17-lyntai-design.md` — the **contract** (interfaces, fork decisions, semantics —
  note the dated §6 amendments; §6 is now the default `RoutingPolicy`). Read it first.
- `docs/ROADMAP.md` — what is shipped per version, then `## Planned`: what a real run still has to confirm
  in the generation backends, and the standing maintenance policies.
- `CHANGELOG.md` — per-release detail; breaking changes called out.
- `docs/DECISIONS.md` — the rationale log, in the present tense: what each decision IS today. Contiguous
  `D1..Dn`, no stubs. **Numbers were reassigned 2026-08-14**, so a `D<n>` in older git history means a
  different entry.
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
  lying exit codes). Those are canonical (synced by `daoris`) and state the PRINCIPLE; this repo's
  concrete bindings — package names and the packable/version layout, the `Dto`-free naming invariant,
  guard scripts, version-authorship policy, the dev loop and test conventions, scratch paths — live in
  the local, never-synced `repo-mechanics.md`. See `.claude/rules/RULES_INDEX.md` (generated).
- **`.claude/knowledge/`** (on-demand deep dives — read the one you're touching):
  `extending-lyntai.md` (the six extension points — provider, generation backend, storage backend, scorer,
  CLI tool-hosting dialect, migration), `llm-and-router.md` (verdict taxonomy, fallback §6 amended,
  streaming-commit + inactivity-clock invariants, CLI hygiene), `storage.md` (Dapper/CAST/FTS5
  trigram triggers/pragmas/`lyntai_` prefix), **`pitfalls.md` (traps that pass the build/tests while
  being wrong — read before extending)**, `generic-library.md` (turning a consumer ask into app-agnostic
  surface), `input-is-thinking-not-doctrine.md` — plus the canonical `library-api-design.md` (generalize
  the ask, never ship its shape), `sql-storage.md` (the SQL traps that return wrong data rather than
  failing), and `model-decoupling.md` (which model is a DEPLOYMENT choice, never part of a feature's
  definition).
- **`.claude/skills/`** — five LOCAL (extension tasks `add-provider`, `add-storage-backend`, `add-scorer`,
  `add-migration`; process `archive-task` — move a finished task from `TASKS.md` to the archive) plus the
  five canonical (`doc-loader`, `pattern-finder`, `post-feature`, `fix-log`, `caveman`). The local five and
  the six local knowledge documents carry no `daoris` provenance header, which is the exposure
  `repo-mechanics.md` opens with: a sync can delete them and nothing fails.
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**
- **Backlog vs archive:** `TASKS.md` holds only OPEN tasks; completed work is moved to
  `docs/task-archive.md` (see `task-lifecycle.md`), and `CHANGELOG.md` is the release-facing log.
- Working files (probes, scratch) go under `devtools/_*` (gitignored), never OS temp.
- **This machine's console is GBK** — write files with the Write/Edit tools (in a script,
  `fs.writeFileSync` or `-Encoding utf8`, which adds a BOM on PowerShell 5); never `echo`/`Set-Content`
  UTF-8 through the console (it lossily mangles CJK/em-dashes). See `pitfalls.md` / `windows-machine.md`.

## Dev loop

- **`node devtools/dev.mjs verify`** — the "am I done?" gate, fourteen checks stopping at the first failure:
  **guard tests** → build → warnings → packages → bundle → **encoding** → **docs** → **links** →
  **counts** → **api vocabulary** → **samples** → test → e2e → leak scan. The summary line is DERIVED from
  the step list, so a gate added without updating prose still names itself. Run before
  claiming a change is complete. The guard tests run FIRST on purpose: nothing below that gate can be
  trusted if the gates themselves are broken.
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
  It checks a reference **two ways**, because there are two ways one rots. The path half asks whether the
  target still exists. The **Part half** asks whether a reference naming a task record (`` `TASKS.md` <!-- link-ok: an ILLUSTRATION of the shape, not a claim about where Part 53 lives -->
  Part 53 ``) names the record that actually holds it — the path resolves and the Part exists, in the OTHER
  file, so nothing else can see it. **Archiving a task is what breaks these**, silently, for every inbound
  reference; five were live on 2026-08-14, in `CHANGELOG.md`'s Unreleased prefix and `docs/FIXES.md`. A bare
  `Part 53` with no record named is deliberately ignored — only a reference that NAMES one makes a checkable
  claim.
  **It now scans the CODE tiers too** (Part 72, decided 2026-08-15), but narrower than the prose scan on two
  axes: **comment lines only** (a path in a string literal is data the program uses, not a reference a reader
  follows) and **`docs/` targets only** (source files are renamed for legitimate reasons — `pitfalls.md`
  records an all-paths existence check returning ~45 hits and zero defects — while a moved DOCUMENT is the
  defect this gate exists for). The entry proposed a third narrowing, `///` XML docs only, and **the
  measurement refused it**: replaying the pre-repair tree, 9 genuine dead references lived in the code tiers,
  an XML-only rule catches 6, and all 3 it misses were in ordinary `//` comments and all 3 were real. Its
  hypothesis had been that `//` comments are where false positives live; every false positive was in fact a
  guard script naming a FIXTURE, which is what `link-ok` is for. Cost: **six annotations, once.**
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
- `node devtools/dev.mjs check-api-vocabulary` — **fail if the FROZEN public surface reintroduces a name a
  decision retired** (part of `verify`, beside `check-docs`). The two are twins: one asks whether the PROSE
  still says what a decision settled, the other asks it of the SURFACE. It exists because `check-docs`
  deliberately excludes `src/` and the API baseline **records parameter names without judging them** — it
  reports THAT a name changed, never that one SHOULD have — so three retired parameter names reached the eve
  of the 3.0 freeze and a human, not a gate, caught them. Registry is `retiredApiNames` in
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
  **Its corpus holds no authoritative material BY DEFAULT** — `CorpusShape.AuthoritativeCount` is opt-in and
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
  unset** — proved by five goldens in `MemoryCorpusGoldenTests`, captured before the axis existed). Every arm
  replays a **structurally identical** corpus — same steps, same ids, same ground truth, only the text
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
- **Seven more one-factor sweeps on that same harness**, all out of `verify` for the same cost reason, and
  all listed here because a roster naming a SUBSET is how a reader learns the roster is not one (the same
  drift `dev.mjs`'s own usage line had, fixed in 208a7ca — on `backup/pre-squash-2026-08-14`, D61). **The
  authoritative list is `node devtools/dev.mjs` with no argument**, which derives it; every `memory-*`
  command there is a sweep, and exactly one of them (`memory-sweep`, the 2×2) is not one-factor. This
  paragraph said "six more … three of nine" while enumerating seven of ten, which is the drift it is
  about — so the count is stated as a relation rather than a number:
  `memory-reinforcement` isolates law 3's
  `r`-dependence from reinforcement MAGNITUDE, which `memory-spacing`'s knob provably cannot separate;
  `memory-bounded` varies the FORM of the growth rule rather than its constants, and is what decided
  `DsrOptions.ReinforceGain` for 3.0; `memory-salience` holds enrichment constant so only salience varies —
  the first measurement of a default that ships ON for two of its three consumers; `memory-annotation`
  measures subject linking with a PERFECT annotator, so its numbers are the mechanism's CEILING rather than
  any model's accuracy. **`memory-verification`** (2026-08-15) is the same stance for the judge seam and is
  the FIRST measurement of what a model in the loop is worth — every other figure here is model-free — and
  **`memory-fan`** is the axis that measured ACT-R's fan effect and REFUSED it (**D62**).
  **`memory-enrichment`** (2026-08-15) answered the oldest open question in that backlog — WHY registering an
  embedder costs recall quality — and is the **only sweep that calls a REAL model**, EXITING rather than
  substituting a double, because the arm it replaces was measured through a bag-of-words fake in which
  "semantic similarity" IS word overlap. Its answer is that the two write-time mechanisms have different
  SHAPES, which is why one number never explained it: **similarity linking is a REDISTRIBUTION** (`topical`
  −0.30, `attribute` −0.28, `critical-rare` **+0.68**) whose aggregate looks small only because those
  cancel, while **novelty→salience is a broad shallow cost** that turns beneficial only under high noise.
  It needed no new API — `MinSimilarity` above 1 keeps the embed and writes no edge; a neutral salience
  policy keeps the edges and drops novelty.
  `node devtools/dev.mjs` with no argument is the authoritative list; this section is
  a curated one and says why each entry earns its place.
- `node devtools/dev.mjs pack` — `dotnet pack` the libraries → `publish/packages/`.
- `node devtools/dev.mjs consumer-smoke` — **the release gate**: packs every package to a scratch feed under a
  throwaway version, then restores + builds + runs a fresh console app against the PACKAGES (not project
  references). The only check that exercises what actually ships — nuspecs, dependency groups, symbol packages,
  the bundle restore. Minutes, so deliberately NOT in `verify`; run before a release or after touching packaging.
- `node devtools/dev.mjs check-sensitive [--tree]` — leak scan.
- `node devtools/dev.mjs doctor [--fix]` — README `## Status` version ↔ `VersionPrefix`, and `VersionPrefix`
  ↔ the newest `v*` tag (**never hand-edit the version** — see `repo-mechanics.md` §Never hand-edit the
  version / `DECISIONS.md` D19).
- `node devtools/dev.mjs check-version` — the pre-commit version-authorship guard, run by hand.
- `node devtools/dev.mjs decisions-index [--check]` — regenerate the index table at the top of
  `docs/DECISIONS.md` from its own headings. **Run it after adding a `D<n>` entry** (`--check` reports
  staleness without writing). Deliberately NOT in `verify`: a stale index costs a reader one `Ctrl-F`, and
  `verify` stays the build/test gate rather than growing a documentation check.
