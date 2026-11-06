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

## Open items — 18 across 5 Parts: 12 startable, 3 blocked, 3 watch

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 119 | 33 | GEN-VERIFY-FAL, the last line — the Hugging Face route | blocked · env | an hf_ token for the router route |
| 160 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 187 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 246 | 99 | `InputSegmenterTests.Counting_NFKC_is_LINEAR_in_the_length_of_the_text` fai… | watch · data | a recurrence under the full parallel run, with the ratio it read — the mess… |
| 264 | 99 | `AgentUsageProcessTests.A_server_that_never_answers_is_ended_with_its_child… | watch · data | a recurrence under the full parallel run, with the load it ran under; it fa… |
| 302 | 417 | MC8 — run the wrong-end instrument on an adjudicated corpus, with a bar wri… | blocked · decision+data | two reviewers' independent labels on a sample of the pairs, and a bar a per… |
| 328 | 446 | AG38 — a permission request waits for the person when the host says so | startable |  |
| 337 | 446 | AG28 — the protocol door carries a permission mode and an effort | startable |  |
| 343 | 446 | AG29 — a turn's own variables | startable |  |
| 349 | 446 | AG32 — an unread frame leaves a bounded trace | startable |  |
| 355 | 446 | AG33 — the Claude door states a turn's own report: denials and usage by mod… | startable |  |
| 361 | 446 | AG36 — the protocol door states what the open offered | startable |  |
| 367 | 446 | AG40 — why a person must act, and what kind of access failed | startable |  |
| 373 | 446 | AG41 — a limit names its window and a stated reset keeps its parts | startable |  |
| 380 | 446 | AG42 — every allowance report is an event | startable |  |
| 386 | 446 | AG44 — the restriction rules are public over a snapshot the host stores | startable |  |
| 392 | 446 | AG45 — a host's line reader runs under the CLI turn's rules | startable |  |
| 398 | 446 | AG39 — a run kept across turns: the design | startable |  |

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

_**What remains unmeasured: two lines of fal's wire, and they alone need a vendor.** `sd-cli` left this list on
2026-09-19 (argv + clamp measured against a real engine, correcting the retired `img2img` mode value —
`docs/task-archive.md` Part 261), and ComfyUI followed the same day IN FULL: the HTTP surface over an image
graph (Part 262), then a video-producing workflow (Part 264) — which needed no video MODEL, because the
provider only ever reads the history document, and a core-node MP4 measures that document's video shape.
The fal-first naming that once hid ComfyUI inside this list is recorded in
`.agents/knowledge/pitfalls.md`._

- [ ] **GEN-VERIFY-FAL, the last line — the Hugging Face route**, so it is <!-- item: state=blocked kind=env needs="an hf_ token for the router route" -->
  confirmed and its docs say "measured", or is corrected. Three funded runs and a billing read measured the rest of
  `FalProvider`'s defaults (`docs/task-archive.md` Parts 410, 413 and 414). **Left:** the Hugging Face route
  (`docs/generation.md` §3), which needs an `hf_` token.
  <br>**A YES** deletes the route's "nobody has run it" note from `docs/generation.md` §3; **a NO** corrects the mapping or
  the default. Run `FalLiveTests` with `FAL_KEY` on purpose, never in `verify`, under a spend bound the owner sets first; the
  tests skip, naming the balance, when the account has none.

  _**This is fal's OWN wire format and nothing else.** It is not the generation platform's verification
  story and must not be treated as one — `sd-cli` and ComfyUI, once bundled with it, were measured without a
  vendor (the note above), and the video DELIVERY path is reachable through ComfyUI. Filed narrowly on
  purpose, because the old bundled item let "we are waiting on fal" stand in for "the platform is unverified"._

  _**The BLOCKING half is gone (2026-08-16, `docs/DECISIONS.md` D69) — what is left is confirmation, not
  repair.** Every mapping the docs name is now a host option: fal's status vocabulary and cost fields.
  So an adopting
  application that discovers the real wire format fixes it in `appsettings.json` and keeps going — it no
  longer waits on a Lyntai release, which is what made this item block anything. Reframed deliberately: the
  old wording made a third party's availability a precondition for this backlog being clean, and the library
  cannot promise to have called every vendor._

  _**What a real run is still for**, stated so this is not read as closed: a STRUCTURAL difference — a status
  that is not a string field at all, a history document shaped differently — is not fixable by a per-field
  option. The residual risk is a shape, not a spelling, and the second run found one: a failed request that
  polls `COMPLETED` and fails at the result fetch. The third found none: a cancel landing as `COMPLETED` plus
  `error` fits `ErrorField`._

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
three are startable" until 2026-08-28, after two of them had gone. A third, also a WATCH item, joined them on
2026-10-28: the bridge's stream-timeout test and the chatty-child tests closed that day (`docs/task-archive.md` Parts 395
and 396) and the NFKC-linearity test, listed beside them, stayed. A fourth, the usage-process test last in this Part, joined
the same day. The second of the two, the `Dedup_race` item, closed on 2026-10-29 as `docs/task-archive.md` Part 409: a race in
the driver's pool, fixed in the factory._

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
  <br>**2026-10-29: the SQLite pool race (`docs/FIXES.md`) does not explain this.** Seven of the nine open no SQLite store, and
  the two `CortexIntegrationTests` that do would fail with a transaction or disposed-handle error, where the one captured
  failure was `IsAvailable` false on a command that did not resolve. The item stays as it is.

- [ ] **`InputSegmenterTests.Counting_NFKC_is_LINEAR_in_the_length_of_the_text` failed once in AG6's `verify` run.** <!-- item: state=watch kind=data needs="a recurrence under the full parallel run, with the ratio it read — the message prints it, and a single failure with no ratio cannot say whether a stall or a real quadratic did it" -->
  It spawns no process and holds no ceiling: it takes a RATIO of two in-process timings (`InputSegmenter.Split` over
  25,000 and 100,000 repeats, 4x apart, best of three each), and asserts `ratio < 10`, where linear work reads about
  4 and quadratic about 16. It was listed beside the `ProcessRunnerTests` chatty-child tests and the bridge's
  stream-timeout test, which have since closed (`docs/task-archive.md` Parts 395 and 396).
  <br>**Not those, and so not fixed by what fixed them.** Neither a real-time margin nor a node start is in it, so
  there is no clock for the test to own. The reading is that a stall landing inside one size's three runs and not the
  other's moves a ratio of two timings; that is a reading, not a capture, and one failure is all there is.
  <br>**Recurred 2026-10-28 with its ratio: 17.3** (0.027 s for the smaller size, 0.462 s for the larger), in a full
  `verify` that also failed the usage-process test above, and green in the two other full runs of that pass. A ratio
  near 16 that does not come back is one reading, so it does not separate a stall from a quadratic; a quadratic would
  not have been green twice.
  <br>**Watch, not startable:** nothing here is known to be wrong. A recurrence with the ratio the assertion
  prints either shows a stall (the ratio far from 4 in either direction) or a real quadratic (near 16 every time),
  and only then is there something to change.
  <br>**2026-10-29: not the SQLite pool race** (`docs/FIXES.md`). The test times two in-process runs of `InputSegmenter.Split`
  and opens no connection.

- [ ] **`AgentUsageProcessTests.A_server_that_never_answers_is_ended_with_its_children_when_the_time_runs_out` failed in two of the fal session's three `verify` runs.** <!-- item: state=watch kind=data needs="a recurrence under the full parallel run, with the load it ran under; it failed twice there and passes alone" -->
  It spawns a node stand-in that never answers, gives the usage source a 3 s timeout, and failed with "the stand-in
  never wrote its pids" in two of three full `verify` runs on 2026-10-28, passing run alone. It is a different test from
  the three above, and the same shape.
  <br>**Recurred in the steward pass that recorded it** (its second full `verify`, then green on the unmodified re-run,
  as its first had been): the same message, at `Pids` (`AgentUsageProcessTests.cs:90`), after 21 s, and the same run
  failed the NFKC test below, so a loaded machine took two timing tests at once. That is three failures in six full runs
  across both sessions, none standalone.
  <br>**Recurred again in the Z-Image-Turbo session's `verify`** (`21de4e6a`): its first run failed on this test and the
  re-run was green with nothing changed: four failures in eight full runs across three sessions, still none
  standalone. The load that run ran under was not captured.
  <br>**Recurred three more times in the image-pipeline session** (`9b270b06`): its first two `verify` runs and one full
  `dotnet test` failed on it, about 50 s into the run, with the same message, and it passed alone, with that session's own
  tests, and in a run without them; its third `verify` was green. That machine was shared with other sessions' builds and the
  GPU was busy, which is the load it ran under, uncaptured as a figure. With the fourteenth steward pass's first `verify`
  (green), that is seven failures in thirteen full runs across five sessions, none standalone.
  <br>**Twice more in the last fal session** (two `verify` runs in a row, the same message, 21 s in), and the test passed
  alone straight after. Nine failures in sixteen full runs across six sessions, none standalone.
  <br>**A reading, not a capture:** the test reads the pid file AFTER the usage source has returned, so a node that
  starts later than the 3 s ceiling is killed before it writes anything, and the assertion reports exactly that message.
  That is the family of the timing tests above and of those already closed (`docs/task-archive.md` Parts 395 and 396):
  a real process's start measured against a real ceiling, on a machine loaded by the parallel suite. **Not yet known:**
  whether the child was late or something else emptied the file.
  <br>**Watch, not startable:** nothing here is known to be wrong in shipped code, and a widening chosen from one
  failure with no capture would repeat Part 395's first one, which failed once in twenty runs anyway. A recurrence with
  the load it ran under says whether the ceiling or the start owns it.
  <br>**2026-10-29: not the SQLite pool race** (`docs/FIXES.md`). The test spawns a node stand-in and reads its pid file, and
  opens no connection.

## Part 417 — keeping an agent's memory current, after Daoris's requests LQ16 to LQ20 (2026-10-30)

_The study is `docs/memory-currency-study.md`, the rulings are **D239** and **D240**, and the synthesis that orders and
amends them is `docs/memory-currency-synthesis.md` (**D241**). Each row is one sitting and none is built; the order is MC4,
MC6, MC2, MC5, MC7, with MC8 free to run now that MC3 has landed. Every member a row adds is
unreleased until a release carries it (**D70**, **D235**). **What a YES does to the library is each row's contract**;
**D242**'s MC11 (a caller's key and receipt), MC12 (the one-change supersession) and MC7 (the widened read-only view) are built (`docs/task-archive.md` Part 424, 425 and 426). MC3 is measured (`docs/task-archive.md` Part 440), and MC8's instrument states what a YES and a NO change, and a measured step pairs its arms
(`docs/memory-currency-synthesis.md` §5)._

- [ ] **MC8 — run the wrong-end instrument on an adjudicated corpus, with a bar written first** (lane `memory`; <!-- item: state=blocked kind=decision,data needs="two reviewers' independent labels on a sample of the pairs, and a bar a person writes and commits before the first model run" -->
  **D241**).
  **Not startable by a session alone:** the two things it waits on are a person's, never an agent's guess: **D241** asks the cases
  "independently adjudicated" and the bounds "written before the run", and a bar or a label an agent supplied itself would pre-register
  nothing. A session starts it the moment both exist.
  **The harness is built**: `node devtools/dev.mjs memory-end-proposal` (`MemoryEndProposalBench.cs`) ingests each `--haystack` question once, clones
  the database per arm (`SweepDb.Clone`) so one arm's reinforcement and links never reach another, counterbalances the arm order across questions,
  repeats the walk inside each clone, and prints four rates separately — a wrong end hiding a fact that still held, a missed replacement, an
  unaffected claim lost, a stale fact kept — beside lost current claims and lost historical evidence. Proposers: `none`, `first` and `oracle`
  (controls, model-free) and `model` (a chat model through `LYNTAI_LIVE_CHAT_URL`). `--adjudication` takes two reviewers' labels per pair and a
  disagreement abstains; `--bar` takes ceilings written before the run. **It withholds its verdict without both and promotes nothing.**
  **What remains**: two reviewers independently label a sample of the pairs (replaces / amends / unrelated), a bar is written and committed before
  the first model run, then one run per (model, corpus) pair. **What a YES does:** names that pair in `docs/memory.md` and
  `docs/memory-measurements.md` §5 as one whose proposal a host may confirm by review of a bounded claim pair; **a NO changes nothing** and is
  recorded as a refutation. **Stop rule.** The first actionable rate stops the instrument; no run is spent after it.

## Part 446 — the agent-provider layer, after Daoris's second request A-LQ3 to A-LQ17 (2026-11-06)

_The study is `docs/agent-provider-daoris-study.md` §10 and the rulings are **D244** to **D249**; A-LQ10 is declined (**D249**) and
files no row. Every row is one sitting, none is built, and every member a row adds is unreleased until a release carries it
(**D70**, **D235**), so its `ApiSurface` baseline moves from the emitted `.actual` with a `CHANGELOG.md` entry under `## Unreleased`.
Captures a row's tests use are labelled REAL or SOURCE-DERIVED as `tests/Lyntai.Tests/Providers/AcpAgentSessionTests.cs` labels them;
a SOURCE-DERIVED one rests on Daoris's recorded captures, which a real one replaces when it arrives. Order: AG43 first (a defect
in built code), AG37 before AG36 and AG42, AG25 to AG27 before AG28, AG30 before AG31, AG40 before AG41, AG43 before AG44, and AG39
last because it designs the largest piece; the rest in any order. No row spends an account or calls a paid service._

- [ ] **AG38 — a permission request waits for the person when the host says so** (lanes `inference` + `platform`, **D248**). <!-- item: state=startable -->
  `IAcpPermissionApprover` refuses at half the turn's inactivity window and the runner's clock ends a silent agent. **Contract.** An
  `AddAcpAgentSession` option states the wait is the approver's: a request waits as long as the approver holds it and the runner is
  told, through an optional member of `IProcessRunner` (default: not supported, as `ExchangeLinesAsync` is), not to read that wait as
  inactivity; a runner that cannot be told refuses the option at registration. The default stays half the window. **Proof.** A
  stand-in that asks and stays silent: answered after longer than the window by a held approver (the turn goes on), refused at half
  the window without the option, and the cancelled outcome on a cancelled turn; the shipped runner honours the member under a short
  window.

- [ ] **AG28 — the protocol door carries a permission mode and an effort** (lane `inference`, after AG25 and AG26, **D244**). <!-- item: state=startable -->
  **Contract.** As the model is (**D243**): a mode is set through `session/set_mode` with an id the open's `modes` answer offers, an
  effort through the config option of its category; the agent's answer decides, a refusal ends the turn before the prompt
  (`option-unsupported`, `NeverSent`), and an agent that offers neither ends it the same way. Both abilities are `Unknown`,
  `Negotiated`. **Proof.** SOURCE-DERIVED opens from Daoris's recorded captures: set and taken, set and refused, no such option.

- [ ] **AG29 — a turn's own variables** (lanes `platform` + `inference`, **D244**). <!-- item: state=startable -->
  **Contract.** `AgentSessionOptions.Environment` (a null value removes) and the ability `environment`, applied over the
  registration's environment and beneath the account's on the CLI and protocol doors; the HTTP door states `Unsupported`. A value is
  a secret and follows `AgentAccount.Environment`'s rules (never argv, a log line or an event). **Proof.** A turn's variable reaches
  the child, a null removes an inherited one, the account's home still wins, and `ToString` lists no value.

- [ ] **AG32 — an unread frame leaves a bounded trace** (lane `inference`, **D245**). <!-- item: state=startable -->
  **Contract.** `SessionEnded` gains `Unread`: for each frame type and subtype no reader case took, a count and one sample, at most 16
  entries of 512 characters; the readers stay tolerant and add no event. It lists what the reader read nothing from, so a frame it
  turned into an event is not here. **Proof.** A stream with three unknown types and a flood of one: three entries, one bounded
  sample each, the count exact, and a stream with none leaves `Unread` empty on both CLI doors and the protocol door.

- [ ] **AG33 — the Claude door states a turn's own report: denials and usage by model** (lane `inference`, **D245**). <!-- item: state=startable -->
  **Contract.** An event for each tool call the harness denied (`permission_denials`: the tool, its call id, the reason where the
  field gives one, else null), and a usage-by-model reading from `modelUsage`: each model's tokens and context window, the
  session's model marked, a subagent's and the harness's own left as the rows they are. `UsageFinal.Model` keeps its meaning. No cost
  field. **Proof.** SOURCE-DERIVED `result` frames with one model and with three, a denial with and without a reason.

- [ ] **AG36 — the protocol door states what the open offered** (lane `inference`, after AG37, **D245**). <!-- item: state=startable -->
  **Contract.** An event after the open's answer and before any set or prompt: each option the agent offered (category, id, current
  value, values) and each mode, bounded as `MaxOfferedNamed` bounds the diagnostic; a refused turn carries it too. It never counts
  toward delivery. **Proof.** SOURCE-DERIVED open answers from both measured agents, a turn refused on a model still delivering the
  offers, and the delivery unchanged.

- [ ] **AG40 — why a person must act, and what kind of access failed** (lane `platform` + `inference`, **D246**). <!-- item: state=startable -->
  **Contract.** `RestrictionRecovery.Reason` for an owner action (refill, sign in, grant, configure, unknown), from fields only; an
  access failure carries two readings, by a field the door defines and by the words, each a sign-in again, a refused credential or
  null, with no winner. **Proof.** Each Claude `error` category and the protocol agents' fields reading as today with the reason
  added, a field and a sentence that disagree both kept, and a failure no field reads leaving the field reading null.

- [ ] **AG41 — a limit names its window and a stated reset keeps its parts** (lane `platform` + `inference`, after AG40, **D246**). <!-- item: state=startable -->
  **Contract.** `AgentRestriction` gains `Window` and `Scope` (the door's own words); `RestrictionRecovery` gains the stated parts
  (shape, date and time parts, zone named or assumed, wait) beside its resolution; an unresolved zone is an unknown recovery with the
  parts; `ClaudeAgentSession` and the Codex session take a `TimeProvider`. **Proof.** A rejected `five_hour` frame naming its window,
  each sentence shape (moment, date and time, weekday and time, time of day, wait) with its parts, a zone the machine lacks keeping
  its parts, and a clock handed to the session reaching the reading.

- [ ] **AG42 — every allowance report is an event** (lanes `platform` + `inference`, after AG37, **D246**). <!-- item: state=startable -->
  **Contract.** An event per `rate_limit_event` frame, allowed or rejected, carrying the window as `AgentAllowanceWindow` states it,
  the report's standing and the overage and credit state it names, on the Claude door and from the protocol door's
  `_claude/rateLimit` update; the rejected frame still ends a failing turn as today. **Proof.** SOURCE-DERIVED allowed, warning and
  rejected frames, a frame with a use and no reset, and the turn's delivery unchanged by a report.

- [ ] **AG44 — the restriction rules are public over a snapshot the host stores** (lane `platform`, after AG43, **D247**). <!-- item: state=startable -->
  **Contract.** An immutable snapshot and the operations **D247** lists, as pure functions returning the next snapshot and an
  outcome, with the pool rebuilt on them; an evidence observation names the hold's id, revision and subject key and the recovery
  kinds it answers. **Proof.** Every existing pool test unchanged, a stale, wrong-scope and other-subject observation discharging
  nothing, a lease report by a token that lost it ignored, and a snapshot round-tripped through a host's own serialisation.

- [ ] **AG45 — a host's line reader runs under the CLI turn's rules** (lane `inference`, **D249**). <!-- item: state=startable -->
  **Contract.** `CliAgentLoop`'s turn is public over `ICliAgentLineReader` (a line to events; the end with its exit code to a
  terminal or none); the loop keeps one terminal, the fault terminal and the delivery stamp, and the built-in doors run through the
  same path. **Proof.** A plain-line stand-in that exits 0 with no terminal (the reader's end yields success), one that exits 2
  (the reader's terminal or the fatal default), one that prints a terminal and then more, and every existing door test unchanged.

- [ ] **AG39 — a run kept across turns: the design** (lanes `platform` + `inference`, last, **D248**). <!-- item: state=startable -->
  A study and a decision, no code. **Contract.** From Daoris's recorded measurements of which agents accept a word mid-turn, a stop
  that keeps the conversation, a model re-asked and background work read until it ends, say which doors can carry a kept run, its
  type beside `IAgentSession`, how a held permission request, a repeatable open, a replayed frame told from a live one and the turn a
  task's notification wakes are stated, and which of these a door states `Unsupported`. **Proof.** The study names what a YES does
  to the library and what a NO does before any row it files; a door that cannot be measured from a record is `Unknown` and not
  filed as supported. **Not established:** whether any CLI door accepts input mid-turn.

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
