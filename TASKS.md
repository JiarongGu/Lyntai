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

## Open items — 24 across 6 Parts: 19 startable, 2 blocked, 3 watch

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 125 | 33 | GEN-VERIFY-FAL, the last line — the Hugging Face route | blocked · env | an hf_ token for the router route |
| 166 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 193 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 252 | 99 | `InputSegmenterTests.Counting_NFKC_is_LINEAR_in_the_length_of_the_text` fai… | watch · data | a recurrence under the full parallel run, with the ratio it read — the mess… |
| 270 | 99 | `AgentUsageProcessTests.A_server_that_never_answers_is_ended_with_its_child… | watch · data | a recurrence under the full parallel run, with the load it ran under; it fa… |
| 305 | 412 | AG10 — the Claude CLI door keeps a subagent's lines apart from the session's | startable |  |
| 318 | 412 | AG11 — the ACP door carries `AgentSessionOptions.Model`, or refuses it | startable |  |
| 328 | 412 | AG12 — a refused model is a hold the pool can keep against one seat | startable |  |
| 336 | 412 | AG13 — an HTTP response's own facts are read as an `AgentFailure` | startable |  |
| 345 | 412 | AG14 — an HTTP door: `IToolLoop` as an agent provider | startable |  |
| 355 | 412 | AG15 — an ACP dialect seam | startable |  |
| 363 | 412 | AG16 — the event-stream rules, held by one theory | startable |  |
| 371 | 412 | AG17 — an account that is a key, and the variables a provider reads | startable |  |
| 380 | 412 | AG18 — background work started and ended, as events | startable |  |
| 387 | 412 | AG19 — an ACP permission request the host can answer | startable |  |
| 402 | 416 | AG21 — the usage exchange writes each request after the answer it depends on | startable |  |
| 414 | 416 | AG22 — the host names where the usage server runs and what it is told of it… | startable |  |
| 427 | 416 | AG23 — an allowance reading carries what the server stated | startable |  |
| 442 | 416 | AG24 — a host's policy for a version the library has not validated | startable |  |
| 463 | 417 | MC3 — measure the candidate list's similarity basis | startable |  |
| 471 | 417 | MC5 — a stored fact can be re-checked by its host, and a failed check ends … | startable |  |
| 487 | 417 | MC7 — a read-only view of the owner's task, as a blend member | startable |  |
| 497 | 417 | MC8 — the wrong-end instrument: what a model's proposed end costs, per mode… | startable |  |
| 508 | 417 | MC10 — a scope dense in ended entries still returns its current ones | startable |  |

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

## Part 412 — the agent-provider layer after Daoris's accepted provider design (2026-10-29)

_The study is `docs/agent-provider-daoris-study.md`: it answers every Daoris row, and these are the general gaps it found.
Each is one sitting, none is built, and each capture a test uses is labelled REAL or SOURCE-DERIVED. The decision they
stand on is **D233**, and the study's §7 is the order they are built in. The first is startable and needs nothing._

- [ ] **AG10 — the Claude CLI door keeps a subagent's lines apart from the session's** (lane `inference`). Daoris <!-- item: state=startable -->
  measured that every line a subagent produces arrives on the session's stdout with `parent_tool_use_id` set to the
  spawning call, and `StreamJsonAgentReader` (`StreamJsonAgentReader.cs:119`) never reads the field: a subagent's
  `TodoWrite` replaces the session's plan, its tool calls read as the session's, its usage ticks as the turn's, and its
  last text and error field can become the turn's fallback text and failure reading (`:130`, `:168`, `:258`).
  <br>**Contract.** `ToolCall` and `ToolResult` gain an init member `ParentCallId` (null for the session's own, the
  call that spawned it otherwise); a line carrying the field never moves the session's plan, its `UsageLive`, its
  fallback text or its failure reading. **Proof.** A SOURCE-DERIVED fixture in the shape of Daoris's capture (a
  subagent's `assistant` line with a `Read`, its `user` result and its text, beside the session's own): the child's
  calls carry the parent id, the plan and fallback text stay the session's, a line with no field reads as before (every
  existing reader test unchanged); the `ApiSurface` baselines move from the emitted `.actual`. **Not established:** a
  synchronous subagent, a nested one and a failed one were not seen there, so the fixture names which it is.

- [ ] **AG11 — the ACP door carries `AgentSessionOptions.Model`, or refuses it** (lane `inference`, then `platform` for <!-- item: state=startable -->
  the ability). The door never reads the option (nothing under `Acp/` names it, `configOptions` or
  `session/set_config_option`), so `AgentPick.Apply` handing a pool choice's model to an ACP provider drops it silently,
  which is what `docs/agent-provider-layer.md` §5.1 forbids. **Contract.** A `model` ability in
  `AgentAbilities.All` (both CLI doors `Supported`, `Declared`; the ACP door `Unknown` until the agent's `session/new`
  answer lists a model option, `Negotiated`, as resume is); a turn naming a model the agent did not offer ends before
  the prompt with `Unsupported`, `NeverSent` and a diagnostic naming the offered values; one it offered is set by
  `session/set_config_option` before the prompt. **Proof.** SOURCE-DERIVED captures through `acp-stub.mjs` (offered and
  not offered), the conformance theory over the new ability, the descriptor tests, and the `ApiSurface` baselines.

- [ ] **AG12 — a refused model is a hold the pool can keep against one seat** (lanes `platform` + `inference`, after <!-- item: state=startable -->
  AG11). A `model_not_found` reads `Fatal` with no restriction (`StreamJsonFailure.cs:126`) and the reducer holds a fatal
  failure only when it carries one, so the pool picks the same account and model again. **Contract.** A refused model
  (both CLI doors' category, the ACP door's AG11 refusal) is `Fatal` with a restriction whose subject is the model and
  whose recovery is `OwnerAction`, so the pool holds that account and model and nothing wider; no enum member is added
  and no duration is chosen (a host clears it). **Proof.** the failure tables held both ways on recorded sentences, and a
  reducer case: the seat is skipped for that model, the same account serves another model, `ClearAsync` frees it.

- [ ] **AG13 — an HTTP response's own facts are read as an `AgentFailure`** (lane `inference`). Today <!-- item: state=startable -->
  `ProviderVerdictClassifier.FromHttpFailure` returns a verdict from the status and the body and no header is read, so a
  `Retry-After` is lost and a 402 is not a credit hold. **Contract.** A pure reader over a status, the headers, the body
  and a `TimeProvider`: 429 a rate limit whose recovery is `RetryNotBefore` from `Retry-After` (seconds or an HTTP date)
  and `Unknown` when absent (never defaulted); 402 or a credit code `OwnerAction`; 401 and 403 access; 408, 5xx and 529
  transient with the scope the envelope names; the JSON error envelope's `code` and `type` read before the sentence; an
  error under a 2xx (`HttpBody.InBandError`) the same. **Proof.** SOURCE-DERIVED responses (OpenAI-style, Anthropic-style
  and OpenRouter-style envelopes) held both ways, including a limit a tool's output merely quotes, which never classifies.

- [ ] **AG14 — an HTTP door: `IToolLoop` as an agent provider** (lanes `platform` + `inference`, after AG13, **D233**). <!-- item: state=startable -->
  `ToolLoop.StreamAsync` yields `AgentStreamEvent`s and one `SessionEnded`, but nothing describes it as a provider.
  **Contract.** `AddHttpAgentSession(id, …)` registers a keyed session and an `IAgentProvider` with one door of kind
  `http`: abilities stated from what the loop measurably does and `Unknown` otherwise (resume `Unsupported` until a
  conversation token exists), `SessionEnded.Failure` from AG13 with a delivery (`NeverSent` when no byte left,
  `Taken` after the first response, `Uncertain` after a tool ran), **one pinned route and no fallback** so the retry
  budget stays `RetryingAgentSession`'s, and an evidence line saying the turn's content goes to the endpoint.
  **Proof.** A fake HTTP handler through the conformance theory (AG16's cases join when it lands), a refused option before
  any call, a 429 with and without `Retry-After`, a cancelled call ending as a stop, and the `ApiSurface` baselines.

- [ ] **AG15 — an ACP dialect seam** (lane `inference`). `AcpAgentReader` and `AcpFailure` read both vendors' extensions <!-- item: state=startable -->
  inline for every agent (`AcpAgentReader.cs:251`), so a third agent's `_meta`, error `data` and limit frames cannot be
  taught without a fork. **Contract.** A public dialect interface registered through `AddAcpAgentSession`: it reads an
  update's extension, an error's `data` and a turn's end, answering events or an `AgentFailure`; the built-in reads
  become the default composite, so nothing changes for a host that names none, and a host naming a set narrows what its
  door reads. **Proof.** Every existing ACP test unchanged; a stand-in with a vocabulary of its own (extension keys no
  built-in reads) read by a registered dialect; the built-in reads fed that stand-in's keys give nothing.

- [ ] **AG16 — the event-stream rules, held by one theory** (lanes `platform` + `tools`). The conformance theory holds the <!-- item: state=startable -->
  descriptor and the spawn, and each stream rule is held door by door. **Contract.** A theory over the registry, from one
  source (**D211**'s shape), driven by the existing stubs: exactly one terminal and nothing after it; a failed terminal
  carries a `Failure` where the door states `typed-failures`; delivery `NeverSent` for a process that dies before any
  event, `Taken` after a start and `Uncertain` after a tool call; a limit sentence a tool printed never classifies;
  cancellation ends the turn without a second terminal. **Proof.** the cases themselves, each shown failing against a
  door that breaks it (a planted double terminal); a stand-in a host registers is held to them with no edit here.

- [ ] **AG17 — an account that is a key, and the variables a provider reads** (lanes `inference` + `platform`). <!-- item: state=startable -->
  `AgentAccount` is a provider, a name and a home, so an agent run on an API key or through a gateway cannot vary per
  turn, and a host that wants an agent not to inherit another provider's key has no list to strip from. **Contract.**
  `AgentAccount` gains optional environment entries the host supplies for the turn; `AgentAccountMapping` names the
  variables the provider reads, and an entry outside that set is refused before a spawn. The value is never stored, never
  in argv, a log line, a diagnostic, the pool's persisted hold or a transcript (**D20**; `AgentMcpServer.AuthToken`'s
  handling is the precedent). **Proof.** A turn under a key account reaches the child; a name outside the set is refused
  with nothing spawned; a sentinel value appears nowhere in the pool's store, the events or the diagnostics.

- [ ] **AG18 — background work started and ended, as events** (lane `inference`, after AG10). Daoris measured the <!-- item: state=startable -->
  `task_started`, `task_updated` and `task_notification` lines of `stream-json` and that the binary ends its background
  work when the turn ends, so a `SessionEnded` does not mean the work finished. **Contract.** `AgentTaskStarted` (a task
  id, the agent's own kind word, a description, the call that started it) and `AgentTaskEnded` (the id, the agent's
  status word), the Claude door's reading of those lines, a task never reaching the session's own events.
  **Proof.** a SOURCE-DERIVED fixture of the shape Daoris captured, including a task still open at the result.

- [ ] **AG19 — an ACP permission request the host can answer** (lane `inference`). A read-only turn rejects every <!-- item: state=startable -->
  request and a write turn allows each once, so an application that lets a person decide cannot. **Contract.** An
  optional host callback on the ACP door's registration (the shape of `IToolApprover`), an ability `permission-requests`
  stated `Unsupported` where none is registered, the response written once with the answer, and no answer within the
  turn's inactivity clock a refusal. **Proof.** SOURCE-DERIVED `session/request_permission` captures: answered allow,
  answered deny, never answered; the default (no callback) behaves exactly as today.

## Part 416 — the usage reading, as a host that keeps an account's history needs it (2026-10-30)

_A host that consumes the layer asked three things of the usage reading (`D236` to `D238` answer them, each by the
generic-library test; none asks for a consumer's shape). The types are unreleased, so a row reshapes them with its
`ApiSurface` baseline moved from the emitted `.actual` and no `### Breaking` (**D235**). Order: AG21, then AG22 (it edits
the first step AG21 makes), then AG23 and AG24 in either. No row spends an account or calls a paid service; the unmeasured
fact is named where it bites. Captures are labelled REAL or SOURCE-DERIVED as `UsageCaptures` does._

- [ ] **AG21 — the usage exchange writes each request after the answer it depends on** (lane `inference`, **D238**). <!-- item: state=startable -->
  `CliUsageSource.ExchangeAsync` writes every request before reading any answer (`CliUsageSource.cs:133-134`;
  `CodexUsageDialect.cs:33-38`), the order **D226** refuses as a default; a server that answers `initialize` with an
  error is still sent the question. **Contract.** `IUsageDialect` states the exchange as steps (internal, so no baseline
  moves): the first request is written, each next only after the answer to the one before has arrived, and a notification
  goes with the request after it. An error answer to any step ends the read as unknown (`not-signed-in` where the dialect
  reads it so, else `unrecognised-answer`) with the process ended; a line the server pushes between answers is read past,
  as today. **Proof.** A `node` stand-in beside `AgentUsageProcessTests` that records arrival order and exits when the
  second request arrives before the first's answer was written (RED against today's source, green after), one that
  answers `initialize` with an error and must receive nothing more, and every existing usage test unchanged.
  **Not established:** whether the installed servers tolerate the eager order; nothing here needs it measured.

- [ ] **AG22 — the host names where the usage server runs and what it is told of its client** (lanes `inference` + <!-- item: state=startable -->
  `platform`, after AG21, **D238**). The read starts the vendor's server in a fresh folder under the system's temporary
  folder (`CliUsageSource.cs:84`) and tells Codex it is client `lyntai` version `1` (`CodexUsageDialect.cs:35`), neither of
  which a host can change, though a vendor keys project state by the folder and a server may act on its client's name.
  **Contract.** `AgentUsageReadingOptions` gains `WorkingDirectory` (a function of the account: a fully qualified path or
  null for today's fresh folder; a named folder is created when absent and never removed or emptied; a path not fully
  qualified is `account-refused` before a spawn), `ClientName` and `ClientVersion` (defaults as today, validated at
  registration, written by a JSON writer where the protocol carries a client and ignored where it carries none). Each has
  an XML doc, as `check-options` requires, and `docs/guide.md` states the folder is a project to the vendor. **Proof.** A
  stand-in that echoes its working directory and the `initialize` line: the named folder reached and left in place, the
  default folder removed, a name holding a quote or a newline written intact or refused at registration, and the Claude
  door's request unchanged by a client name.

- [ ] **AG23 — an allowance reading carries what the server stated** (lanes `platform` + `inference`, **D237**). <!-- item: state=startable -->
  `AgentAllowanceWindow` is a name, an integer percent, a length and a reset with no scope (`AgentUsageSource.cs:27`); the
  Claude reader takes two windows (`ClaudeUsageDialect.cs:70-94`) and the Codex reader only `rateLimits`, where a fractional
  percent makes the whole reading unknown (`CodexUsageDialect.cs:68-93`). **Contract.** As **D237**: `Scope`,
  `UsedPercent` as `double?` with `UsedStep`, a window said in part kept, `Limits` (Claude's `limits[]` rows by kind and
  scope, never severity), `LimitId`, Claude's `seven_day_<scope>` windows and Codex's `rateLimitsByLimitId` limits other than
  the own one read as scoped windows, `HasRoom` over the unscoped windows only, a present unrecognisable value still unknown.
  The XML doc names what the reading does not carry (the account id, the credits, the severity). **Proof.** Every existing
  capture reads as before apart from the type; SOURCE-DERIVED captures for a fractional use on each vendor, a scoped week
  at 100 with the account's own windows under it (`HasRoom` true), a window with a use and no reset and one with a reset and
  no use, a `limits[]` row with a scope, and `rateLimitsByLimitId` with a second limit, each held both ways beside a
  malformed value that stays unknown. **Not established:** the shape of a scoped row and of a second Codex limit on a real
  signed-in account; the host holds recorded answers and can share them, and a capture of it should replace the
  SOURCE-DERIVED one when it arrives.

- [ ] **AG24 — a host's policy for a version the library has not validated** (lanes `platform` + `inference`, **D236**). <!-- item: state=startable -->
  The version gate is internal (`CliUsageSource.cs:77-82`; `requireValidatedVersion`), so a host that keeps an agent at a
  version it chose can only get `version-not-validated`. **Contract.** `IAgentUsageVersionPolicy` (whether the version is
  probed; given an `AgentUsageVersionQuery` of the provider, the account, the version and whether the library validated it,
  whether the server starts), a `VersionPolicy` on `AgentUsageReadingOptions` defaulting to `ValidatedOnly`, and a shipped
  `AnyVersion` (no probe). The reading names the version it was read on and whether the library validated it. The
  descriptor's `UsageReading` claim, its validated versions and its side effects are untouched, and an unrecognised answer
  is unknown under every policy. **Proof.** An unvalidated version refused by default, read under a host policy naming
  it and under `AnyVersion` with no probe spawned, the reading's version fields set in each, `Describe()` equal with and
  without a policy, a throwing policy refusing as `version-not-validated` without a throw, and an unrecognised answer
  still unknown under `AnyVersion`.

## Part 417 — keeping an agent's memory current, after Daoris's requests LQ16 to LQ20 (2026-10-30)

_The study is `docs/memory-currency-study.md`, the rulings are **D239** and **D240**, and the synthesis that orders and
amends them is `docs/memory-currency-synthesis.md` (**D241**). Each row is one sitting and none is built; the order is MC3,
MC4, MC6, MC2, MC5, MC7, with MC8 able to run once MC3 lands and MC10 waiting for nothing. Every member a row adds is
unreleased until a release carries it (**D70**, **D235**). **What a YES does to the library is each row's contract**;
MC3's measured rate and MC8's instrument each state what a YES and a NO change, and a measured step pairs its arms
(`docs/memory-currency-synthesis.md` §5)._

- [ ] **MC3 — measure the candidate list's similarity basis** (lane `memory`; LQ17, **D239**). <!-- item: state=startable -->
  The contract is built and the lexical basis is measured (`docs/memory-measurements.md` §5, `update-candidates-lexical-haystack-n70`:
  the true predecessor in the first four for 54% of pairs, in the first ten for 89%, so the default limit is ten). **What
  remains is the same measurement with an embedder wired**: serve an embedding model from your own llama-server on its own port,
  run the 70 pairs with `Similarity` on, report the rate per basis beside the lexical one. A YES moves the guidance in
  `docs/memory.md`; a NO changes nothing. The measurement's scratch harness was deleted: rebuild it from the `knowledge-update`
  loading in `MemoryLongMemEvalBench.cs`.

- [ ] **MC5 — a stored fact can be re-checked by its host, and a failed check ends it** (lanes `memory` + `storage`, after <!-- item: state=startable -->
  MC1; LQ19, **D240**). `IMemoryVerificationPolicy` judges relevance, not truth, and nothing records when a fact was last
  confirmed. **Contract.** `MemoryWrite` gains a `Recheck`, an opaque bounded text the library never reads;
  `IConfirmableMemory.ConfirmAsync(taskKey, entry, outcome, at?, note?, expected?)` where `Held` stamps `ConfirmedAt` (never
  backwards; no other field moves, so it is not a reinforcement) and `Failed` ends the entry through MC1's call, at the
  confirmation instant and never an earlier one, with the note as its required reason and the due read's revision as the
  expected one. **A due sweep is eventual repair, not proof of currency at presentation**, and says so in its doc. **`Failed` is documented as the host having confirmed the named claim no longer holds**; a run that did not
  finish (a timeout, missing access, an ambiguous answer) is reported by calling nothing, records no state and stays due
  (**D241**); `DueAsync(taskKey, scope, notConfirmedSince, limit)` answering the live entries that carry a `Recheck`
  and were last confirmed before an instant or never; a `Confirmation` on `MemoryItem` on read; a `_confirm` tool when MC2 is
  built. Optional, as MC1: nullable `recheck` and `confirmed_at` columns in their own migration, a second optional store
  interface, the portable line, the blend. **Proof.** A held check stamps and changes nothing else; a failed one is gone
  from `Current` and read under `History` with its reason; a re-remember of identical content keeps the stamp, and one that
  names no `Recheck` keeps the stored one; the due query both ways; the library runs nothing (no process or network type in the
  change); an end whose expected revision no longer matches changed nothing, including when a rewrite changed the headline, grade, metadata or check and left the confirmation as it was; and the `ApiSurface` baselines.

- [ ] **MC7 — a read-only view of the owner's task, as a blend member** (lane `memory`, after MC1; Wave 4's first half in <!-- item: state=startable -->
  `docs/ROADMAP.md`; **D241**). A second host needs a scope another host owns and writes. **Contract.** A view of one task key
  on one engine that recalls and expands, **writes, ends, reinforces, links and advances no interference tick through it**,
  refuses a reference outside its task, and reads the owner's `History` and end reading it already has; the owner's end is
  read at the reader's next recall. Owners, grants, the change log and cursors stay out of the library. **Proof.** A write
  through the view refused; a recall and an expansion through it leave the owner's reinforcement, links and age as they were;
  a reference of another task refused; an owner's end seen at the reader's next recall; the blend routing it by
  `MemoryRef.Engine`; the `ApiSurface` baselines. A reader's citation of a shared fact is a `Recheck` (MC5), and no
  cross-task link is added (**D92**).

- [ ] **MC8 — the wrong-end instrument: what a model's proposed end costs, per model and corpus** (lane `memory`, after MC3's <!-- item: state=startable -->
  pairs; **D241**). **What a YES and a NO change, before the first run.** A YES, a model and corpus that clear a bar written
  beforehand, names that pair in `docs/memory.md`'s guidance and `docs/memory-measurements.md` §5 as one whose proposal a host
  may confirm by review of a bounded claim pair; a NO changes nothing and is recorded as a refutation so the next session
  does not repeat it. **Contract.** Independently adjudicated revised-against-superseded pairs from `memory-longmemeval
  --haystack`, a model proposing the end from MC3's candidates, and four rates reported separately: a wrong end hiding the
  true fact, a missed replacement, an unaffected claim lost, a stale fact kept. Paired arms, **each starting from an equivalent isolated snapshot and keeping its mutations inside its own repeated walk**,
  execution order counterbalanced, since a recall mutates (`docs/memory.md` §7) and counterbalancing alone removes order bias, not
  contamination; **this is required before MC8 can promote any step**, and the harness is read first for whether it does it. Disagreement
  between reviewers abstains. **Stop rule.** The first actionable rate stops the instrument; no run is spent after it.

- [ ] **MC10 — a scope dense in ended entries still returns its current ones** (lane `memory`; **D241**, measured by MC1). <!-- item: state=startable -->
  The validity reading is applied to the page after the candidates are gathered, so 100 ended entries that match a query
  better than 5 current ones return **0 of the 5** (`docs/memory-measurements.md` §5, `ended-entries-crowd-page-n4`).
  **Contract.** A `Current` or as-of recall returns the current entries it would have returned with no ended ones in the
  scope; the first step is to read where the window is cut (`TryGatherAsync`) and whether the store can take the reading
  into its seed or the engine can widen and re-seed under a bound, whichever stays additive on `IMemoryGraphStore`
  (**D70**, **D41**: only the ranker buries). **Proof.** The measured corpus returns 5/5 at N = 100 and 500 on every
  shipped store, a recall under `History` unchanged, and the `ApiSurface` baselines.

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
