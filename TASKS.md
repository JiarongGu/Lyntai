# Lyntai (灵台) — Active Task Backlog

> **This file holds OPEN tasks only** — the live backlog. Completed work is not left here: once a task is
> fully done (committed + verified), its entry is **moved to [`docs/task-archive.md`](docs/task-archive.md)**
> (the completed-task record) rather than checked off in place. See the lifecycle rule
> `.claude/rules/task-lifecycle.md`. `CHANGELOG.md` remains the release-facing log; the archive is the
> per-task record (why/how). The design contract is `docs/2026-07-17-lyntai-design.md`; the forward
> sequence is `docs/ROADMAP.md`.

**Goal:** a NuGet-packable, DI-first .NET 10 library — an LLM provider abstraction (routing + fallback
across CLI / HTTP / lambda-bridged providers), pluggable storage (SQLite / Postgres / in-memory / file system), and the
LLM-ops layer (prompt registry, scoring, traces, memory). `AddLyntai(...)` and go.

---

<!-- open-items:begin — GENERATED. Edit the per-item `item:` markers, never this table. -->

## Open items — 25 across 7 Parts: 21 startable, 2 blocked, 2 watch

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 126 | 33 | GEN-VERIFY-FAL — one submit → poll → fetch against fal.ai with a real key | blocked · env | a free Hugging Face account (an hf_ token) — its router proxies fal's own q… |
| 173 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 196 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 252 | 99 | `SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another ca… | watch · data | a recurrence with a full stack — the three hypotheses a reading can reach a… |
| 282 | 294 | Give the vector-store, verification and annotation contracts an abstract Fa… | startable |  |
| 285 | 294 | Sweep test comments for history narration that carries no tag or date | startable |  |
| 295 | 298 | SentencePiece tokenization for the ONNX provider — D122's trigger has fired | startable |  |
| 299 | 298 | Change the embedder without rebuilding the graph | startable |  |
| 301 | 298 | Read a stored vector back by id, or cache embeddings on the vector call | startable |  |
| 303 | 298 | Filtered nearest-neighbour search | startable |  |
| 305 | 298 | Edit the text provider set at run time | startable |  |
| 308 | 298 | Schedules added at run time, persisted | startable |  |
| 310 | 298 | Trace and score front-door calls without a wrapper | startable |  |
| 313 | 298 | Job progress as a message code plus arguments | startable |  |
| 320 | 299 | Snapshot `ToolsByConsumer` when the provisioner is built, and name the cons… | startable |  |
| 324 | 299 | Pin the refusal through the builder, and say when it fires | startable |  |
| 327 | 299 | Two test gaps in the CLI tool seam | startable |  |
| 329 | 299 | A one-line `ToolsByConsumer` recipe in README's MCP section | startable |  |
| 346 | 301 | Let the shipped LLM annotator say it did not answer, so `MemorySources.Anno… | startable |  |
| 363 | 301 | Let a graph write skip its annotation when its vector fails — D175's deferr… | startable |  |
| 381 | 301 | Let a deployment choose a segmented rerank call's pieces per REQUEST | startable |  |
| 400 | 301 | Bound a reranker document's pieces independently of the query | startable |  |
| 411 | 301 | Classify llama.cpp's physical-batch refusal as `ContextWindowExceeded` | startable |  |
| 426 | 301 | Warn about leftover keys under a model-key prefix of the app's own | startable |  |
| 437 | 301 | Say when a server refuses the configured `SuppressReasoningFields` | startable |  |

<!-- open-items:end -->

---

## Active backlog

_**The archive is where closed work lives** — `docs/task-archive.md`, one Part per task, with why and how;
this file does not summarize it. `CHANGELOG.md` is the release-facing log, and everything before 3.0 is
history rather than context (`repo-mechanics.md`)._

**What is open is the TABLE at the head of this file, and it is GENERATED.** Every checkbox carries an
`<!-- item: state=… kind=… needs="…" -->` marker; `node devtools/dev.mjs check-backlog --write` rebuilds the
table from those markers and `verify` fails while the two disagree, so the roster and the items can no
longer drift apart. Edit the marker, never the table — the COUNT lives there and nowhere else, so this
sentence names no number — **nor a Part**, the same rot one step out: it named Part 102 as "most of what is
startable" until Part 103 opened bigger. **The accumulation that hid these is GATED, not watched for**:
`check-backlog` fails a `## Part` holding no open checkbox.

**Re-checking an `env` item asks whether the artifact is OBTAINABLE, not whether it is installed** —
`task-lifecycle.md` §A blocked item, which two re-checks here got wrong before a reader did.
**An `env` blocker also EXPIRES
SILENTLY** — Part 65 read "this machine holds exactly one chat model" for two weeks while three sat on
disk, every one already used by other measurements here. Nothing fails when an environment GROWS, so
nothing announces it; **re-check every `env` item before concluding there is no work.**
**A REFUTATION is the fifth route an item arrives by, and the most reliable**: disproving the obvious answer
names the next candidate, which is how Part 217 arrived narrowed to one signal of three. The others are a
ruling, a captured failure, and a design REVIEW — the only one schedulable on purpose, and the only one
that has twice filled a whole Part in one pass (Part 179, then Part 221) where every other route yields
ones and twos. `decision-only` was invented for GEN6 and earns its keep whenever a SWEEP declines to answer
a question rather than settle it by momentum — a rename nobody examined decides something invisibly. That
sentence is hand-written on purpose: the banner it replaces advertised finished work **four** times.

**Where things stand is NOT summarized here, deliberately.** `docs/memory-measurements.md` §5 is the measurement record,
`docs/task-archive.md` holds one Part per closed task, `docs/DECISIONS.md` holds what was decided and why,
and `docs/FIXES.md` holds per-incident fixes. A copy of any of those goes stale the moment the original is
amended, which is why this section stopped carrying one on 2026-09-10.

_**Why this section is short, and the discipline that keeps it short.** It reached 478 lines carrying ZERO
open checkboxes — five stacked `HANDOVER` blocks and a running tally of what had closed, for 17 open items
in a 1308-line file. `task-lifecycle.md` already forbade exactly that ("never let the backlog SUMMARIZE the
archive"), and the section had even documented deleting a 49-line tally for that reason on 2026-09-03 —
then regrew a 19-line one in its place. **A rule that keeps being violated is a missing gate**, so
`check-backlog` now bounds this section; see `devtools/scripts/check-backlog.mjs`. The content was not lost:
the handovers describe Parts 143–176, which is where they live._

**BLOCKED IS PER ITEM, never per Part** — which is why the state lives on the checkbox rather than in a
roster of Parts. Part 33 was once marked blocked in full while two startable pieces sat inside it (they
closed as **D67** and **D68**). A blocked item names its blocker's KIND as well — `tree`/`env`/`decision`/
`data` — because each is refuted by looking somewhere different, and the rest of that discipline is
`task-lifecycle.md`'s. `watch` and `decision-only` exist because a checkbox can express neither, and both
were being counted as startable work.

## Part 33 — generation platform: remaining backends + composition

_Part 32 (MED1: the generation platform + the 2.0.1 package restructure) landed 2026-08-04 — see
`docs/task-archive.md` Part 32, `docs/DECISIONS.md` D24/D25, and the plans of record
`local/superpowers/plans/2026-08-04-generation-platform-plan.md` +
`local/superpowers/plans/2026-08-04-restructure-2.0.1-plan.md`. Its Plans 3–7 have
all closed (`docs/task-archive.md` Parts 33, 126, 261, 262, 264, 266 and 291), leaving fal's wire alone below.
<br>**Nothing below EXECUTES from that plan any more, which is why it left `docs/` (D149).** Its Plan 6
still names a streaming interface **D127** deleted and its Plan 7 predates the 2026-08-30 3D survey and
GEN7a shipping, so the one item body below is the current framing and the plan is the record of how the
core was built._

_GEN3 (local `sd-cli`), GEN4 (durable renders + the fal.ai queue backend), GEN6's tool/MCP bridge half and
GEN5 (governance + telemetry parity) all landed 2026-08-04 — see `docs/task-archive.md` Part 33._

_**What remains unmeasured: fal's wire format, and it alone needs a vendor.** `sd-cli` left this list on
2026-09-19 (argv + clamp measured against a real engine, correcting the retired `img2img` mode value —
`docs/task-archive.md` Part 261), and ComfyUI followed the same day IN FULL: the HTTP surface over an image
graph (Part 262), then a video-producing workflow (Part 264) — which needed no video MODEL, because the
provider only ever reads the history document, and a core-node MP4 measures that document's video shape.
The fal-first naming that once hid ComfyUI inside this list is recorded in
`.claude/knowledge/pitfalls.md`._

- [ ] **GEN-VERIFY-FAL — one submit → poll → fetch against fal.ai with a real key**, checking the status <!-- item: state=blocked kind=env needs="a free Hugging Face account (an hf_ token) — its router proxies fal's own queue; or a fal.ai key" -->
  vocabulary, the result field names and what `cost` reports. Then delete that backend's "unverified" notes
  or fix the mapping.
  <br>**PARKED 2026-09-25 by the owner, who holds no fal.ai account** and did not know why fal was the vendor
  chosen: it arrived with GEN4 (2026-08-04) as the hosted queue-shaped video backend the durable render job was
  built against, written from fal's public docs, and has never been called. **The full review kept it** (`docs/task-archive.md`
  Part 295, owner) and found a cheaper verifier: the Hugging Face router proxies fal's queue for a free account, and
  `FalOptions.AuthScheme` / `QueryParameters` make that route configuration (`docs/generation.md` §3). One run
  there confirms the status vocabulary, the `COMPLETED`+`error` failure shape (`FalOptions.ErrorField`), the result
  fields and the sub-path question; it does not confirm fal's own `Key` auth or billing.

  _**This is fal's OWN wire format and nothing else.** It is not the generation platform's verification
  story and must not be treated as one — `sd-cli` and ComfyUI, once bundled with it, were measured without a
  vendor (the note above), and the video DELIVERY path is reachable through ComfyUI. Filed narrowly on
  purpose, because the old bundled item let "we are waiting on fal" stand in for "the platform is unverified"._

  _**This half IS blocked, and it is the rule's own example of one**: a vendor key or an account, which no
  download can supply. Split from the `sd-cli` half on 2026-09-16 because they were always independent —
  the old single item said so itself — and bundling them let the real blocker hide a startable one behind
  it for weeks._

  _**The BLOCKING half is gone (2026-08-16, `docs/DECISIONS.md` D69) — what is left is confirmation, not
  repair.** Every mapping the docs name is now a host option: fal's status vocabulary and cost fields.
  So an adopting
  application that discovers the real wire format fixes it in `appsettings.json` and keeps going — it no
  longer waits on a Lyntai release, which is what made this item block anything. Reframed deliberately: the
  old wording made a third party's availability a precondition for this backlog being clean, and the library
  cannot promise to have called every vendor._

  _**What a real run is still for**, stated so this is not read as closed: a STRUCTURAL difference — a status
  that is not a string field at all, a history document shaped differently — is not fixable by a per-field
  option. The residual risk is a shape, not a spelling._

> Add new tasks here as checklist items with an `id` and a short `file:line` where known. Group related
> tasks under a `## Part N — <theme>` heading. Move an item to the archive when it lands — don't leave a
> `[x]` here.

---


## Part 75 — what the pre-3.0 review deferred, and why (2026-08-15)

_Opened by `docs/task-archive.md` **Part 74**. Each of these was found, verified and deliberately NOT fixed
in that pass — and every one was startable, which is why the banner stopped claiming otherwise while they
were open. All have now closed (archive Parts 76, 78–81 and 84) except the one below, whose blocker is not
a design question: it needs two or three real aggregators to measure against._

- [ ] **Decide what an aggregator's in-band `code` means.** `HttpBody.InBandError` deliberately reports <!-- item: state=blocked kind=env,data needs="two or three real aggregators to measure an in-band code against" -->
  only THAT an `error` member is present and what it says; it does not read a numeric `code` as an HTTP
  status, because that mapping is not measured across the gateways this provider serves. A 200 carrying
  `{"error":{"code":429}}` therefore classifies from the message text alone. Measuring two or three real
  aggregators would let the code lead, which is strictly better than text matching — but reading it
  unmeasured is the documented-not-measured trap GEN-VERIFY exists to correct.

## Part 99 — what the memory pass did NOT close (2026-08-26)

_Opened by `docs/task-archive.md` **Part 97**, which closed every other Phase-1 invariant of the memory
proposal. Named here rather than left implied, because a scorecard that reads as complete is how a gap stops
being looked for._

_It opened holding the two Phase-1 gaps. One of those (cross-tenant isolation) closed the same day as
**Part 100** and left a DECISION behind it; the flake below arrived from watching `verify` rather than from
the proposal. **Two items are left open here, both WATCH items** — the rest closed into the archive, and this line said "all
three are startable" until 2026-08-28, after two of them had gone._

_**The flake below is a WATCH item, not startable work, and the banner counted it as startable until 2026-08-28.** The
suspected cause is fixed AND pinned — `ProcessRunnerTests.A_FAILED_path_lookup_is_not_cached_so_one_transient_locator_failure_is_not_permanent`,
which carries a positive control so it cannot pass on an implementation that simply caches nothing. So
there is nothing here to code: what remains is evidence only recurrence can supply._

- [ ] **`verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted mid-run.** Observed <!-- item: state=watch kind=data needs="the same nine tests to recur — the fix is unconfirmed as the cure, and a green run is not evidence" -->
  twice in roughly ten `verify` runs on 2026-08-26, and **not once in any standalone `node devtools/dev.mjs
  test`**, which passed every time including immediately before and after a failing `verify`. A third run
  ABORTED at 2088/2108 — a crash rather than a failure — and took 6m20s against the usual ~3m.
  <br>**CAPTURED on the third occurrence, and the names refute the obvious hypothesis.** The guess was
  Postgres — 9 is a plausible size for one Testcontainers fixture class, and it is the only part of the
  suite with an external dependency. **Not one of the nine is a Postgres test:**

  ```
  CortexIntegrationTests.Evaluate_persists_results_to_the_score_store
  CortexIntegrationTests.Llm_judge_scorer_returns_the_stub_verdict
  RouterEndToEndTests.Healthy_primary_cli_serves_and_http_is_never_called
  RouterEndToEndTests.Streaming_never_falls_back_after_the_first_token
  RouterEndToEndTests.Dead_host_cooldown_skips_then_retries_after_expiry
  AddClaudeCliProviderTests.Registered_provider_serves_through_the_router_by_id
  ClaudeCliProviderTests.Explicit_command_makes_the_provider_available
  CodexCliProviderTests.A_portable_install_is_wired_without_touching_the_process_environment
  ProcessRunnerTests.Resolve_command_path_finds_node_and_caches
  ```

  **Every one of them spawns a process or resolves a command on PATH**, and the whole set is explained by
  the last one failing: the CLI-provider and router-e2e tests all reach the deterministic provider-stub
  through `LYNTAI_PROVIDER_CMD`, which is `node`. If `ProcessRunner` cannot resolve `node`, all nine fall
  together — one cause, nine symptoms, and a constant count is exactly what that predicts.
  <br>**Why only under `verify`** is then the question worth asking: on 2026-08-26 its test step ran after
  `test-devtools`, a build and nine gates, `check-samples` spawning Roslyn over ~78 samples — heavy process
  churn. The cache in `ProcessRunner.ResolveCommandPath` was the first suspect, since a cache that memoizes a
  transient failure would produce precisely this: intermittent, all-or-nothing, and invisible to a standalone
  run that starts clean.
  <br>**RECURRED 2026-09-15, and ALONE — which narrows the hypothesis rather than confirming it.** One of
  the nine failed by itself under `verify`
  (`CodexCliProviderTests.A_portable_install_is_wired_without_touching_the_process_environment`,
  `Assert.True` on `IsAvailable`), passed 3/3 standalone immediately after, and the very next `verify` was
  green with the same tree. So the "one cause, nine symptoms, constant count" reading is too strong: a
  resolution can fail for ONE caller without taking the other eight, which an all-or-nothing PATH outage
  would not do. A per-entry cache race fits; a global `node`-not-on-PATH window does not.
  <br>**The count is therefore NOT the signature** — nine was one observation of it, not its shape, and a
  future single-test failure in this list is the same bug rather than a new one.
  <br>**Do not close this by observing a green run.** It was green 11 times out of 14, including twice
  consecutively while trying to reproduce it on purpose.
  <br>**A REAL BUG matching every symptom was found and fixed the same day** (`docs/FIXES.md`,
  `pitfalls.md`): `ProcessRunner.ResolveCommandPath` cached FAILED lookups into a process-wide static, so
  one transient `where.exe` failure made `node` unresolvable for the rest of the run — which fails exactly
  these nine at once and explains the constant count, the all-or-nothing shape, and why a standalone run
  that starts clean never saw it.
  <br>**This item stays OPEN, on purpose.** The mechanism fits every observation and the flake was never
  reproduced on demand, so the fix is unconfirmed as the CURE. **If the nine recur, this was not it** — and
  that is the only evidence that can close this. Watch for it; do not close it by observing green runs,
  which is what the line above already says and is now doubly true.

  <br>**A ONE-test flake was seen 2026-09-09** under `verify`, green on the standalone run immediately after
  and green on the next `verify`. It is recorded here rather than as a new item because it is the same
  SHAPE — intermittent, only under `verify`, unreproducible on demand — but it is NOT the nine, so it does
  not confirm or refute the `ProcessRunner` fix this item watches. The name was not captured; capture it if
  it recurs, which is the only thing that would make it actionable.

- [ ] **`SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another caller is still using.** <!-- item: state=watch kind=data needs="a recurrence with a full stack — the three hypotheses a reading can reach are refuted, so the next move needs the frame that raised it" -->
  **Captured 2026-09-14 — a name, an exception, and a reproduction, which is what the note above asked for
  and did not get.** `System.ObjectDisposedException: Cannot access a disposed object. Object name:
  'SQLitePCL.sqlite3'`, thrown inside `SqliteConnectionFactory.OpenAsync`
  (`src/Lyntai.Storage.Sqlite/SqliteConnectionFactory.cs`) while the test exercises concurrent dedup.
  <br>**It is NOT the flake above, and the difference is the whole point:** that one is *only* under
  `verify` and has never reproduced standalone. This one failed **1 of 3 standalone runs** of its own
  class — so it is not `verify`-specific, not process-churn, and not the nine. Nothing here touches
  `ProcessRunner`.
  <br>**The reproduction is the cheap part** — loop the single class until it fails. What
  makes it worth doing rather than muting: the exception says a connection was DISPOSED while in use, and
  the same factory serves every SQLite domain. A test that fails a third of the time is also a gate that
  passes two thirds of the time for the wrong reason.
  <br>**2026-09-15: did not reproduce in 8 consecutive runs, and the "shipped storage code" reading is
  REFUTED — three hypotheses, each checked by reading rather than by re-running.** (1) A double dispose in
  `SqliteCuratedMemoryStore.AddAsync`: no — connection and transaction are both `await using`, disposed in
  the right order, and the factory builds a fresh `SqliteConnection` per call and disposes it on failure.
  (2) `SqliteConnection.ClearAllPools()` evicting a concurrent test's handle: already fixed —
  `TempDbPath.Dispose` clears only its OWN db's pool and `TempDb` delegates to it. The trap is now in
  `pitfalls.md`, where it was not. (3) Work outliving the test method: no — every task in
  `Dedup_add_race_settles_to_a_stable_id` is awaited through `Task.WhenAll`.
  <br>**So it is `watch · data` rather than startable**: what it needs is a recurrence carrying the frame
  BELOW `OpenAsync`, because the three causes a reading can reach are gone and the remaining ones are all
  in the runner's resource behaviour — the same shape as Part 99 above, by a different mechanism.

## Part 294 — what the full review left open (2026-09-25)

_Opened by `docs/task-archive.md` **Part 295**, the full review of code, tests, tooling and docs. Each was
found and deliberately not done in that pass; everything else it found is fixed and archived._

- [ ] **Give the vector-store, verification and annotation contracts an abstract Facts base**, as the engine, <!-- item: state=startable -->
  ranking and retrievability contracts now have (`tests/Lyntai.Tests/Memory/`), so no fact can be wired to one
  implementation and silently skipped on another. The vector-store one spans `tests/Lyntai.Tests/Storage/`.
- [ ] **Sweep test comments for history narration that carries no tag or date.** The review cut every tagged or <!-- item: state=startable -->
  dated provenance line (74 hits to 1), but untagged narration ("this used to…", "until the fix…") needs a
  reading pass, not a regex — `code-commentary.md` applies to tests as it does to `src/`.

## Part 298 — what the consuming apps work around (2026-09-26)

_Filed from a read of the four applications that consume the library, for what each wraps, re-implements or
compensates for. Each item is the general need and its evidence; each has one app behind it, so the design is
part of the work._

- [ ] **SentencePiece tokenization for the ONNX provider — D122's trigger has fired.** `Lyntai.Providers.Onnx` <!-- item: state=startable -->
  tokenizes with WordPiece only, so a multilingual embedding or rerank export, usually SentencePiece, cannot
  load; an adopting app hand-wrote an ONNX embedder for one. The choice is D122's: own the tokenizer, tested
  id-for-id against a reference, or take a dependency.
- [ ] **Change the embedder without rebuilding the graph.** A new embedding model means a destructive rebuild <!-- item: state=startable -->
  that discards decay state and links; a re-embed pass over the stored nodes would keep both.
- [ ] **Read a stored vector back by id, or cache embeddings on the vector call.** `IVectorStore` searches but <!-- item: state=startable -->
  cannot return a stored vector, so an app re-embeds its corpus on every refresh or keeps its own memo.
- [ ] **Filtered nearest-neighbour search.** `IVectorStore.SearchAsync` takes no filter, so an app over-fetches <!-- item: state=startable -->
  and filters afterwards.
- [ ] **Edit the text provider set at run time.** An app whose users add and edit endpoints rebuilds its whole <!-- item: state=startable -->
  container to change them, losing in-memory state such as the usage budget; the media side has a provider
  pool for exactly this (`docs/generation.md` §10).
- [ ] **Schedules added at run time, persisted.** `JobScheduler` runs only the schedules registered at build <!-- item: state=startable -->
  time, so an app with user-authored schedules runs its own cron ticker beside it.
- [ ] **Trace and score front-door calls without a wrapper.** An app wraps `ITextClient` to write a run trace <!-- item: state=startable -->
  and run the deterministic scorers on every call; nothing in the inference layer calls the trace or scoring
  services.
- [ ] **Job progress as a message code plus arguments.** `JobContext` progress takes a plain string, so an app <!-- item: state=startable -->
  that localizes its status text keeps its own job system for that alone.

## Part 299 — what D190's review deferred (2026-09-26)

_The final review of the per-request CLI tools work (**D190**) graded these Minor; each is small and startable._

- [ ] **Snapshot `ToolsByConsumer` when the provisioner is built, and name the consumer in the refusal.** The map <!-- item: state=startable -->
  is validated at construction but read live, so a caller-held list mutated later slips past the unknown-name
  check and a null list value throws a bare `NullReferenceException`; the refusal also does not say which
  consumer key held the bad name (`src/Lyntai.Tools.Mcp/McpToolHostProvisioner.cs`).
- [ ] **Pin the refusal through the builder, and say when it fires.** No test drives <!-- item: state=startable -->
  `AddMcpToolHost(connector, o => o.ToolsByConsumer[…])`; the refusal fires when the CLI provider is first built,
  which fails the whole provider enumeration rather than `BuildServiceProvider` — true, defensible, unstated.
- [ ] **Two test gaps in the CLI tool seam.** A provisioner implementing only the request-blind member is tested <!-- item: state=startable -->
  on `CompleteAsync` but not `StreamAsync`, and no case has every mapped name unknown.
- [ ] **A one-line `ToolsByConsumer` recipe in README's MCP section**, compiled by `check-samples`, where it <!-- item: state=startable -->
  describes the map in prose today. Make it the deny-by-default one, and say why a map matters even to an app
  that registers tools only for its own calls: the library's model seams spawn the CLI under THEIR consumers —
  `memory` for annotation and verification (`src/Lyntai.Core/Memory/MemoryModelCall.cs:29`), `scoring` for the
  LLM scorers (`src/Lyntai.Core/Cortex/LlmScorerBase.cs:26`) — so with no map each of those calls hosts every
  registered tool and starts a host (`src/Lyntai.Tools.Mcp/McpToolHostProvisioner.cs:28-31`). README states the
  fallback to every tool but not that the library's own calls reach it. An adopting app upgrading from 3.2.0
  found its memory calls hosting, on every call, file-reading tools only its scorers use, while an annotation
  prompt carries stored, consumer-authored content.

## Part 301 — what an adopter's 3.2.0 → 3.4.0 upgrade found (2026-09-26)

_Reported by an adopting application planning its upgrade from 3.2.0 to 3.4.0. The upstream fixes for all six
of its workarounds shipped in 3.3.0; these are what the upgrade found beside them, each checked against the tree
at `v3.4.0` (HEAD changes nothing under `src/` since) when it was filed. Its evidence about the CLI tool host
went into `TASKS.md` Part 299's recipe item instead._

- [ ] **Let the shipped LLM annotator say it did not answer, so `MemorySources.Annotation` sees a failure.** <!-- item: state=startable -->
  `LlmMemoryAnnotationPolicy.AnnotateAsync` returns `MemoryAnnotation.None` on a non-Ok verdict or an empty
  reply, on its own timeout or any exception, and on a reply holding no JSON
  (`src/Lyntai.Core/Memory/Annotation/LlmMemoryAnnotationPolicy.cs:125-128`, `:137-141`, `:176-178`) — the same
  value a real "about nothing" answer parses to. The engine counts any return as answered
  (`src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs:314-315`), so the flag **D175**'s 2026-09-26 addendum
  added is SET for a signed-out CLI, a refused call or a timeout, while `CHANGELOG.md` 3.4.0 and `docs/memory.md`
  §Know whether a write kept its vector say it is absent when the annotator "failed or timed out". That holds
  only for an annotator that throws, the one failure `GraphAnnotationRanTests` drives
  (`tests/Lyntai.Tests/Memory/GraphAnnotationRanTests.cs:20`); for the adopter whose rebuild triggered the
  addendum, the flag detects only a refused subject-index write. Suggested: the distinction verification already
  has — `MemoryVerification.NoOpinion` is `Judged: false`, apart from `NothingRelevant`
  (`src/Lyntai.Core/Memory/Verification/IMemoryVerificationPolicy.cs:125-130`) — as an unanswered
  `MemoryAnnotation` the engine reads as not answered and the shipped policy returns on each failure path
  (whether an unparseable reply is one is part of the design); failing that, correct both documents. A test with
  a policy that fails WITHOUT throwing.

- [ ] **Let a graph write skip its annotation when its vector fails — D175's deferred trigger, with its cost.** <!-- item: state=startable -->
  `GraphMemoryEngine.RememberAsync` annotates first and embeds after, best-effort
  (`src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs:244`, `:268`). A consumer that retries every write whose
  `Ran` lacks `Similarity` — **D175**'s intended use — pays an annotation call per write per attempt while its
  embedder is down, and again on the retry that keeps the vector: an adopting app with a CLI annotator,
  re-indexing pending facts at each start, spends account quota per pending fact per start through an outage.
  Stopping a batch at its first vector-less write bounds a back-fill to one wasted call per attempt; every
  ordinary write made during the outage is still annotated twice. D175 deferred a readiness probe until "a
  consumer that must decide BEFORE writing anything"; this one must decide whether a write is worth its
  annotation. Suggested, as the smaller surface: embed BEFORE annotating — the embed reads only `write.Content`
  (`src/Lyntai.Core/Memory/Engines/GraphVectorProjection.cs:42-59`) and the annotator need only precede the
  upsert, for its suggested grade — plus an opt-in on `GraphMemoryOptions` that skips annotation when an embedder
  is wired and this write's embed failed, so `Ran` carries neither flag and the retry annotates once. One option
  and no new type, where the probe would publish the internal route filter D175 declined to and can pass a moment
  before the write fails. An engine option, not a `MemoryWrite` field, which D175 rejected as `RequireVector`
  because a fanned-out write carries it to members that cannot honour it. The grade caveat D175 records for a
  failed annotator applies.

- [ ] **Let a deployment choose a segmented rerank call's pieces per REQUEST.** <!-- item: state=startable -->
  `InputSegmentation.MaxPiecesPerInput` is fixed at registration: the HTTP transport reads it from the
  registration's record and the request carries none
  (`src/Lyntai.Providers.Basic/Http/HttpRerankTransport.cs:67-79`). **D177** rejected a per-CALL cap because "a
  call's pieces are already its inputs × `MaxPiecesPerInput`, and fitting a latency budget is the deployment's
  policy", which assumes the deployment can choose that cap per call. It cannot, and one registration's calls
  vary widely: few long candidates or many, a GPU or a CPU. The adopter measured a 480-window call at ~20 s on
  one GPU and its reranker at ~3.1 s per 1,000 pair tokens on a CPU, where a call sized for the GPU waits out a
  60 s verification deadline for no verdict. It sizes each call by measured time, down to one piece per input,
  and keeps its own segmenting score decorator — a second segmenter beside D177's — for that alone. Suggested:
  an optional per-request override on `ScoreRequest`, e.g. `int? MaxPiecesPerInput`, honoured when set and never
  above the registration's own cap, as `ScoreRequest.TimeoutSeconds` already overrides a registration's deadline
  per call, clamped to `LyntaiOptions.MaxProviderTimeout` (`src/Lyntai.Core/Inference/IScoreProvider.cs:12-19`),
  on every provider that honours `Segmentation`. The library still sets no budget and measures nothing: the
  number, and how it is chosen, stay the deployment's. Unlike the rejected cap it bounds each input, the quantity
  D177 names. A deployment sizing by time also predicts a call before sending it, and `InputSegmentation.Spread`
  is public while the segmenter producing the pieces is not; a way to count an input's pieces would keep that
  prediction from drifting.

- [ ] **Bound a reranker document's pieces independently of the query.** On a Score registration <!-- item: state=startable -->
  `MaxInputChars` is the PAIR window, and a document keeps what the query leaves, `window − Measure(query)`
  (`src/Lyntai.Providers.Basic/Http/HttpRerankTransport.cs:70-71`), so piece length moves with the question. A
  deployment that chose its piece length by measurement cannot keep it: a window sized for 1,000-character pieces
  beside a short query cuts a long query and shortens the pieces beside it. An adopting app segments for a
  reranker that declares no window into pieces of at most 1,000 characters whatever the query (the windows behind
  the two such rows of `rerank-segmented-adopter-long-notes`) and caps the query separately; D177 cannot express
  that, so a within-run comparison of the two segmenters would compare piece lengths too. Suggested: an optional
  per-piece bound on `InputSegmentation`, in the provider's window unit, a document piece taking the smaller of it
  and `window − query`; null keeps today's rule.

- [ ] **Classify llama.cpp's physical-batch refusal as `ContextWindowExceeded`.** An input longer than <!-- item: state=startable -->
  llama-server's physical batch, a rerank pair or an embedding input, is refused with HTTP 500:
  `input (5218 tokens) is too large to process. increase the physical batch size (current batch size: 4096)`
  measured by an adopting app on b10549, and the same at 512 on a small-window reranker;
  `.claude/knowledge/pitfalls.md` records it for an embedder. `ProviderVerdictClassifier`'s context pattern
  (`src/Lyntai.Core/Inference/ProviderVerdictClassifier.cs:130`) has matched the 400 "larger than the max context
  size" since `docs/FIXES.md` 2026-09-24, not this, and `FromHttpFailure` reads a 500 by its body alone
  (`:94-99`), so the rerank and vector transports (`src/Lyntai.Providers.Basic/Http/HttpJsonCall.cs:57-62`)
  report `Failed`: counted toward benching the host (`LyntaiOptions.DeadHostThreshold`) and, from a reranker
  judge, logged at Debug as transient — the two outcomes that fix removed for the 400. A **D177** bound avoids it
  where it is set right, but it counts characters and the batch counts tokens. Suggested: match it narrowly, on
  "physical batch size" or "too large to process" beside it, since "too large to process" alone could be an
  upload refused for its size; the captured body in `ProviderVerdictClassifierTests`, and an HTTP 500 case beside
  `HttpRerankTransportTests.An_input_over_the_models_window_is_CONTEXT_WINDOW_EXCEEDED_not_a_host_fault`.

- [ ] **Warn about leftover keys under a model-key prefix of the app's own.** **D176**'s warn-once lists <!-- item: state=startable -->
  only the library's retired `lyntai.model.` namespace, a private constant
  (`src/Lyntai.Core/Inference/IModelRoutingStore.cs:53`, the check at `:101-113`). A deployment that had pointed
  3.2's model-key prefix option — the one `LyntaiOptions.RouteKeyPrefix` replaced — at a namespace of its own,
  which that option documented for exactly this, has overrides that go inert after the rename with no warning at
  all, and `CHANGELOG.md` 3.3.0's upgrade note tells it to "move to a new prefix so the old keys go inert". An
  adopting app found its live scorer and memory model overrides would silently return to their defaults, and had
  to write its own migration. Suggested: let the warn-once list the retired prefixes a deployment names, e.g. a
  `LyntaiOptions` list defaulting to `lyntai.model.` and passed to `KeyValueModelRoutingStore`, skipping any
  prefix the current `RouteKeyPrefix` sits under as the check already does for its own.

- [ ] **Say when a server refuses the configured `SuppressReasoningFields`.** **D179** adds the members only to <!-- item: state=startable -->
  calls asking `Suppress` (`src/Lyntai.Providers.Basic/Http/Payloads/OpenAiPayload.cs:92`), and a server that
  rejects one answers a client error that classifies `Failed`
  (`src/Lyntai.Providers.Basic/Http/HttpChatEngine.cs:51-52`, `ProviderVerdictClassifier.FromHttpFailure`): the
  router logs it at Information, the LLM judge at Debug as transient
  (`src/Lyntai.Core/Memory/Verification/LlmMemoryVerificationPolicy.cs:145`), the LLM annotator at Debug
  (`src/Lyntai.Core/Memory/Annotation/LlmMemoryAnnotationPolicy.cs:127`). `docs/memory.md`'s recipe says exactly
  this — a rejected value "looks like no judge at all everywhere but the router's log" — and asks the deployment
  to try the value first; nothing in the code says it. The HTTP provider is the one place that knows the failed
  call carried the fields. Suggested: one Warning per registration when a call carrying them fails with a 4xx the
  classifier leaves `Failed`, naming the option and quoting the server; a probe at startup would cost a
  generation on every start. An adopting app replacing a server-side "reasoning off" preset with this option has
  to prove per model that verdicts still ARRIVE, since a wrong spelling would otherwise show only as worse recall.

## Retired — five Parts that outlived their open work (2026-09-16)

_Parts 109, 116, 128 and 178 held **295 lines and zero open checkboxes** between them: every line was a
"CLOSED as archive Part N" note, which is what `.claude/rules/task-lifecycle.md` forbids in as many words.
They are `docs/task-archive.md` **Parts 233–236** respectively, one per thread, each naming where its
halves landed and what it leaves standing. **Part 233, which filed the retirement, went with them** —
`docs/task-archive.md` **Part 238**, both items._

_**The rule is now a gate**: `check-backlog` fails a `## Part` heading holding no open checkbox, so this
cannot regrow unnoticed a third time. A heading that is not `## Part <n>` — this one — is invisible to it,
which is what lets a retirement leave a pointer behind without tripping the rule it just satisfied._

_**They are numbered 233–236 in the ARCHIVE and were 109/116/128/178 HERE** — the two files number
independently, so a Part keeps its backlog number until it closes and the archive allocates in LANDING
order. Name the file in every citation._

_**BL1 was filed saying all four numbers COLLIDE, and only one does.** `docs/task-archive.md` Part 178 is
an unrelated task (the cold-start measurement); 109, 116 and 128 are simply gaps there. The item's
conclusion survives its wrong premise — they could not keep their numbers anyway, because the archive
allocates in landing order rather than reserving a backlog number — but the reason is the ORDER, not a
clash. Recorded because the wrong version was repeated from the item into this note before anyone checked
it, which is the whole failure mode `check-links` cannot see: a `Part N` claim about a Part that does not
exist reads exactly like one about a Part that does._

---

## How to work a task (evergreen)

- **TDD, every task:** failing test → run it fail → minimal impl → run it pass → commit. Read
  `.claude/rules/dotnet-package-layout.md` (package layout) + `.claude/rules/repo-mechanics.md` (this
  repo's bindings — package names, naming invariant, dev loop, test conventions), then the relevant
  `.claude/knowledge/*` (migrations → `storage.md` / `sql-storage.md`; spawn hygiene →
  `llm-and-router.md`) + `.claude/skills/*` before extending.
- **Commit per task.** **Never commit without the user's approval.** Describe changes structurally in the
  message (no dev-machine paths / private tokens — the pre-commit guard enforces this).
- **This is a generic library** — every task must be a reusable, app-agnostic improvement behind the
  `ITextClient` front door / a BYO seam, never app-specific code. Update the `ApiSurface` baselines
  deliberately on any public-surface change.
- **Deviate from a task's suggested steps when the code disagrees** — the spec's *contract* (interfaces,
  semantics) is authoritative; a task's step list is a suggestion. Record real deviations in the commit
  message.
- **When a task completes, archive it** (`.claude/rules/task-lifecycle.md`): move its entry (with the
  completion date + a one-line **Outcome**) into `docs/task-archive.md`, and delete it from here.