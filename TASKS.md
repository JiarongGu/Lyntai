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

## Open items — 8 across 4 Parts: 3 startable, 3 blocked, 2 watch

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 109 | 33 | GEN-VERIFY-FAL — one submit → poll → fetch against fal.ai with a real key | blocked · env | a free Hugging Face account (an hf_ token) — its router proxies fal's own q… |
| 156 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 180 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 236 | 99 | `SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another ca… | watch · data | a recurrence with a full stack — the three hypotheses a reading can reach a… |
| 269 | 99 | The bridge's stream-timeout test fails under the parallel `verify` run and … | startable |  |
| 288 | 380 | A binary vector column for the SQLite vector store | startable |  |
| 293 | 380 | Postgres graph store: one transaction for a batch of edges and touches | blocked · env | Docker up, so the Postgres leg runs; LYNTAI_ALLOW_POSTGRES_SKIP=1 would ski… |
| 298 | 380 | The keyword store's count cap sorts the whole scope on every write | startable |  |

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
`.agents/knowledge/pitfalls.md`._

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
the proposal. **Two items were left open here, both WATCH items** — the rest closed into the archive, and this line said "all
three are startable" until 2026-08-28, after two of them had gone. A third, a startable timing test, joined them
on 2026-10-28._

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
  <br>**2026-09-27: 0 of 40 more fresh-process runs of its class** (19 tests each, `Dedup_race` among them), so
  1 in 51 standalone overall. **Looping is not how this gets caught**; the stack has to come from the run it
  fails in.
  <br>**2026-10-28, a neighbour with a full stack:** one full `verify` failed
  `SqliteCuratedMemoryStoreTests.Satisfies_the_contract("Racing_writers_on_one_revision_let_exactly_one_win")` with
  `SQLite Error 1: 'cannot start a transaction within a transaction'`, thrown in `BeginTransaction` inside
  `SqliteCuratedMemoryStore.UpdateCoreAsync`; the unmodified re-run passed. It is not this exception, but it is the
  same shape (one connection in use by two callers), in the same store, and it carries the stack this item waits for.

- [ ] **The bridge's stream-timeout test fails under the parallel `verify` run and passes alone** (lane <!-- item: state=startable -->
  `inference`). `BridgeProviderTests.The_stream_timeout_is_an_INACTIVITY_clock_a_slow_but_alive_stream_finishes`
  has five chunks 100 ms apart finish inside a 250 ms inactivity window, so a 150 ms stall of the test's own
  scheduling reads as silence and the stream ends in `Timeout`. It failed in the `verify` runs of AG3, AG5, AG8 and
  RV28 and passed unmodified on the re-run; AG6's run saw it beside three more timing tests (an `InputSegmenterTests`
  NFKC-linearity test and two `ProcessRunnerTests` chatty-child tests) that passed 108 of 108 alone. `docs/FIXES.md`
  (2026-10-25, the bridge's timeout) records the test as the pin for the clock's kind.
  <br>**Not the two items above:** their cause is in a runner or a connection, this one's is a margin in the test,
  and a loaded machine spends it. **Contract:** the test still proves what its name says (a stream never silent for
  the window finishes though the whole run is longer than the window). **Proof:** the margin widened, or the guard
  driven from a clock the test controls; the test goes red when the guard bounds the whole stream instead of each
  read; twenty full parallel `test` runs without it failing.

## Part 380 — what the real-corpus memory run left (2026-10-28)

_From `docs/memory-measurements.md` §5 (`corpus-cost-before-after`) and `docs/task-archive.md` Part 379. Three
cost findings that run named and did not fix. Each is additive or internal: none changes a recall's result, and
the store contracts hold them._

- [ ] **A binary vector column for the SQLite vector store** (lane `storage`). A search still parses each <!-- item: state=startable -->
  vector's JSON text, which is 515 ms a query at 11,112 vectors after the single-pass fix. **Contract:** vectors
  are stored as a blob by an additive migration (`.agents/skills/add-migration/SKILL.md`), rows written before it
  still read, and every score is bit-identical. **Proof:** `VectorStoreContract` and `SqliteVectorStoreScanTests`
  unchanged; the same recall p50 re-measured with `memory-corpus` and recorded beside `corpus-cost-before-after`.
- [ ] **Postgres graph store: one transaction for a batch of edges and touches** (lane `storage`). The SQLite store <!-- item: state=blocked kind=env needs="Docker up, so the Postgres leg runs; LYNTAI_ALLOW_POSTGRES_SKIP=1 would skip the only proof" -->
  commits a batch once (`bda3c5e1`); `PostgresMemoryGraphStore`'s edge loop runs each `StrengthenAsync` as its own
  autocommit statement and was neither measured nor changed. **Contract:** one transaction per `LinkManyAsync`
  and per write-back, reviews outside it as `IMemoryGraphStore` requires. **Proof:** the shared graph-store
  contract on Postgres, and a per-write time before and after at several thousand entries.
- [ ] **The keyword store's count cap sorts the whole scope on every write** (lane `storage`). `CapEvictSql` <!-- item: state=startable -->
  runs an ordered sub-select per write, so `CountCap(int.MaxValue)` is 6 times slower than `Manual` on 11,112
  writes (394 s against 66 s) and the default 500 cap costs 185 s. **Contract:** the survivors are exactly
  `MemoryEviction.Survivors`' (newest `@cap` live entries, expired first, id as the tie-break), and a scope under
  its cap deletes nothing and sorts nothing. **Proof:** the memory-store contract unchanged on both dialects,
  and a write-time curve against scope size that is flat below the cap.

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
  `.agents/knowledge/*` (migrations → `storage.md` / `sql-storage.md`; spawn hygiene →
  `llm-and-router.md`) + `.agents/skills/*` before extending.
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
