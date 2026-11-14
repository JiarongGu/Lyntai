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

## Open items — 17 across 12 Parts: 2 startable, 7 blocked, 1 watch, 7 decision-only

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 118 | 33 | GEN-VERIFY-FAL, the last line — the Hugging Face route | blocked · env | an hf_ token for the router route; re-checked 2026-11-15, none on this mach… |
| 159 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against; re-checke… |
| 186 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 260 | 417 | MC8 — run the wrong-end instrument on an adjudicated corpus, with a bar wri… | blocked · decision+data | two reviewers' independent labels on a sample of the pairs, and a bar a per… |
| 285 | 479 | A write tool's metadata stamp replaces a held entry's stored bag on a refre… | decision-only | a ruling: keep a held entry's stored metadata on a refresh (the shape of D3… |
| 288 | 479 | A stated but malformed `validFrom`, `validTo` or `at` is read as absent | decision-only | a ruling: refuse a stated instant that does not parse, naming the field, or… |
| 298 | 481 | A rejected limit is never cleared inside a turn | blocked · data | a capture of the CLI sending a rejected rate_limit_event and then an allowe… |
| 309 | 485 | How long a quiet Codex step runs is unmeasured | blocked · env | one real Codex turn on an account with room: re-checked 2026-11-15, the own… |
| 321 | 486 | The HTTP chat engine's own failures still drop the usage its wire read | startable |  |
| 330 | 490 | `Model2VecProvider` embeds and counts an unknown token | decision-only | a ruling: skip the unknown id, which changes every vector already stored fr… |
| 332 | 490 | A `ContextSize` past the model's trained context is not refused | decision-only | a ruling: refuse a ContextSize past the model's trained context (read from … |
| 340 | 491 | A ComfyUI run lost to a server restart polls for ever | decision-only | a ruling: consult /queue and fail or call it inconclusive when the id is in… |
| 342 | 491 | Cancelling a durable generation job does not cancel the backend render | decision-only | a ruling: a best-effort CancelAsync when a cancel is seen with an operation… |
| 343 | 491 | `generate_fetch` bills and delivers again on every call, and the job engine… | decision-only | a ruling: whether the spend ledger or the tool owns de-duplication per (bac… |
| 351 | 495 | The usage read's scratch folder is created with the default mode | blocked · env | a Unix run: the mode is not observable on Windows, so a test of the fix can… |
| 357 | 518 | AG50 — a kept run on `codex-acp` and dsh (A-LQ19) | blocked · data | Daoris's recorded Codex sitting and dsh probe frames; re-checked 2026-11-15… |
| 359 | 518 | AG51 — a kept run on the Claude CLI with stdin held (A-LQ21) | startable |  |

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

- [ ] **GEN-VERIFY-FAL, the last line — the Hugging Face route**, so it is <!-- item: state=blocked kind=env needs="an hf_ token for the router route; re-checked 2026-11-15, none on this machine and the owner keeps it blocked" -->
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

- [ ] **Decide what an aggregator's in-band `code` means.** `HttpBody.InBandError` deliberately reports <!-- item: state=blocked kind=env,data needs="two or three real aggregators to measure an in-band code against; re-checked 2026-11-15, no aggregator key on this machine and the owner keeps it blocked" -->
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

  _(The second router test was renamed on 2026-11-08 to `A_healthy_primary_stream_is_served_and_the_fallback_is_never_touched`; it was captured under the name above.)_

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
  <br>**2026-11-07: tried to reproduce on demand, and could not.** 24 runs of the nine's classes alone and beside CPU load
  (twice as many busy node processes as cores), and the 106-test set around them under the same load, all green. The two
  timing flakes beside it WERE reproduced the same way and had causes in their own tests (`docs/FIXES.md`), so the load
  was real enough to fail something; it did not fail these. The item stays a watch, still waiting on the nine to recur.

## Part 417 — keeping an agent's memory current, after Daoris's requests LQ16 to LQ20 (2026-10-30)

_The study is `docs/memory-currency-study.md`, the rulings are **D239** and **D240**, and the synthesis that orders and
amends them is `docs/memory-currency-synthesis.md` (**D241**). MC8 is the one row still open; the others, MC1 to MC7 and MC9 to
MC12, are closed in `docs/task-archive.md`. Every member a row adds is
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

## Part 479 — what the 2026-11-07 review of the memory-currency layer found and did not fix yet

_Opened by `docs/task-archive.md` **Part 479**: three read-only reviews of what MC1 to MC12 built (the relational receipt and
supersession runners, the file and in-process stores, and the engine, the gate, the write tools and the view), each finding read
against the code before it was filed. The certain and small ones closed with a test in that Part. What stays needs a design
choice, or is a crash window to close with a store test of its own. Lanes `memory` and `storage`._

_The seven rows it opened with are built (`docs/task-archive.md` Part 528, **D361** to **D366**); the two below were found by that build._

- [ ] **A write tool's metadata stamp replaces a held entry's stored bag on a refresh.** `MemoryGraphState.Refresh` takes the write's <!-- item: state=decision-only needs="a ruling: keep a held entry's stored metadata on a refresh (the shape of D361's grade rule), merge the stamp into it, or keep replacing it as documented" -->
  non-empty `Metadata` over the stored one, and the SQL upserts `COALESCE(@metadata, …)`, so a model restating a host entry's text overwrites
  the host's metadata and moves its revision, which makes a pending review stale. **D361** kept the grade for the same reason and left this.
- [ ] **A stated but malformed `validFrom`, `validTo` or `at` is read as absent.** The write tools parse an instant leniently, so a model's <!-- item: state=decision-only needs="a ruling: refuse a stated instant that does not parse, naming the field, or keep reading it as absent and say so in the schema" -->
  unparseable `at` on the end tool becomes "now" and an unparseable bound becomes "unbounded", with no error back to the model.

## Part 481 — what the 2026-11-08 review of the Claude door and the agent core found and did not fix yet

_Opened by `docs/task-archive.md` **Part 481**; its five startable rows closed in `docs/task-archive.md` **Part 482**, leaving the five that need a ruling. Two read-only reviews of what the earlier agent-layer review did not read — the Claude
stream-json door (`StreamJsonAgentReader`, `StreamJsonFailure`, `StreamJsonFields`) and the shared core with the Codex and HTTP
doors (`RetryingAgentSession`, the HTTP door and its chat engine, the transcript recorder) — each finding read against the code
before it was filed. Lanes `inference` and `platform`._

- [ ] **A rejected limit is never cleared inside a turn.** `StreamJsonAgentReader.NoteRateLimit` keeps `_rejectedLimit` once seen, so a later <!-- item: state=blocked kind=data needs="a capture of the CLI sending a rejected rate_limit_event and then an allowed one in one turn; re-checked 2026-11-15: Daoris holds 117 recorded frames, every one allowed, and no rejected frame" -->
  `allowed` frame does not clear it and an unrelated `is_error` result is classified `Limit`. Whether the CLI sends `allowed` after `rejected`
  in one turn is not shown by any capture here.
  **The rule a capture would select is written first:** the latest report per `rateLimitType` stands, so an `allowed` clears only a rejection of the
  same window; a capture with no such pair closes the row as unreachable.

## Part 485 — what the 2026-11-08 review of the Codex door and the process runner found and did not fix yet

_Opened by `docs/task-archive.md` **Part 483**: a read-only review of the Codex exec door and `ProcessRunner`, each finding read against the code before it
was filed. Three closed in that Part. What stays needs a measurement against a real `codex`. Lane `inference`._

- [ ] **How long a quiet Codex step runs is unmeasured.** The agent doors now arm `LyntaiOptions.AgentTimeout` (15 minutes) in place of two, but no real `codex exec --json` turn has been timed through a long `dotnet test`, <!-- item: state=blocked kind=env needs="one real Codex turn on an account with room: re-checked 2026-11-15, the owner's Codex account is at its workspace spend cap" -->
  so the figure is a steward's default, not a measurement, and a turn spends an account.
  **The question is narrower than "does 15 hold":** whether `codex exec --json` is silent for the whole of a command. One run of a step of known length
  answers it (`codex exec --json --sandbox read-only` asked to sleep 120 s, each line timestamped): no line between the command's `item.started` and
  `item.completed` means the window must cover the longest step, which only the host knows (`LyntaiOptions.AgentTimeout`); lines between mean
  15 minutes bounds true inactivity.

## Part 486 — what the 2026-11-08 review of the front door, the router, the cache and the budget found and did not fix yet

_Opened by `docs/task-archive.md` **Part 484**: a read-only review of `Lyntai.Core/Inference` outside the agent layer, each finding read against the code before it was
filed and none reproduced. The certain and small findings closed in that Part. Lane `inference`._

- [ ] **The HTTP chat engine's own failures still drop the usage its wire read.** **D350** has a failed reply carry what was billed, and the CLI engine, the router and <!-- item: state=startable -->
  the budget do; `HttpChatEngine`'s streamed content-filter, tool-call-failure and zero-content `Error` terminals and its buffered empty-body failure (with its one resend)
  still carry none. Found by the builder of `docs/task-archive.md` Part 525; summing the resend's two attempts needs `TextUsage.Sum` reachable from `Lyntai.Providers.Basic`.

## Part 490 — what the 2026-11-08 review of the local inference backends found and did not fix yet

_Opened by `docs/task-archive.md` **Part 488**: a read-only review of `Lyntai.Providers.Onnx`, `LlamaSharp`, `Model2Vec`, `Text`, `Diagnostics` and `Prompts`. The certain and small findings closed in
that Part (the `JsonExtract` first-brace row was already filed in Part 487). A row resting on a reference implementation says so. Lane `inference`. Five of its rows closed and one narrowed in `docs/task-archive.md` Part 511._

- [ ] **`Model2VecProvider` embeds and counts an unknown token.** `EncodeToIds` emits `[UNK]`, which `Embed` averages in; the reference `model2vec` is believed to drop it before pooling (read <!-- item: state=decision-only needs="a ruling: skip the unknown id, which changes every vector already stored from this provider, or keep it as the table was built against; the upstream behaviour is to be read, not assumed" -->
  from the upstream source by the review, not here). An all-unknown text would then be the zero vector.
- [ ] **A `ContextSize` past the model's trained context is not refused.** Measured against a real 135M GGUF and the CPU backend: `ContextSize = int.MaxValue / 2` <!-- item: state=decision-only needs="a ruling: refuse a ContextSize past the model's trained context (read from the GGUF) before the native call, which narrows a published option, or leave it as the host's to bound (documented on the option)" -->
  does not throw out of `LLamaWeights` or `StatelessExecutor`, it kills the test host natively, so no `catch` in `EnsureLoaded` and no `Failed` verdict can answer it. The option's doc now says the host bounds it.

## Part 491 — what the 2026-11-08 review of the generation platform found and did not fix yet

_Opened by `docs/task-archive.md` **Part 488**: a read-only review of `Lyntai.Generation` and `Lyntai.Core/Generation`, each finding read against the code, one (the deadline's range) run. The certain and small findings closed in that
Part. Lane `generation`._

- [ ] **A ComfyUI run lost to a server restart polls for ever.** Its queue and history are in memory, so `/history/{id}` answers `{}` after a restart, which reads as not landed yet, and a `Poll` outcome has <!-- item: state=decision-only needs="a ruling: consult /queue and fail or call it inconclusive when the id is in neither, or a maximum poll duration on GenerationPipelineJobOptions, and how many confirming polls before a run is declared lost" -->
  no deadline, so a durable job meant to survive restarts stays `Running`.
- [ ] **Cancelling a durable generation job does not cancel the backend render.** The engine never calls `IMediaJobProvider.CancelAsync`, so a paid fal render or a ComfyUI GPU run goes on unattended. <!-- item: state=decision-only needs="a ruling: a best-effort CancelAsync when a cancel is seen with an operation id checkpointed, or leave it to the host and say so; ComfyUI's interrupt is server-wide and can kill another tenant's run" -->
- [ ] **`generate_fetch` bills and delivers again on every call, and the job engine bills before it checkpoints.** A model that calls the tool twice records the cost and sends the artifacts twice, and a lease lost between <!-- item: state=decision-only needs="a ruling: whether the spend ledger or the tool owns de-duplication per (backend, operation id); both over-count, so they fail closed" -->
  the bill and the checkpoint is billed again by the worker that reclaims it.

## Part 495 — what the 2026-11-08 review of the one-shot CLI providers and the agent briefs found and did not fix yet

_Opened by `docs/task-archive.md` **Part 497**: a read-only review of `CliProviderEngine` and the Claude and Codex ONE-SHOT providers, the usage source and the hand-over brief, each finding read against the code, none run.
The certain and small findings closed in that Part. The `.cmd` shim re-parsing argv through `cmd.exe` is already `TASKS.md` Part 485's first row and is not filed again. Lanes `inference` and `platform`._

- [ ] **The usage read's scratch folder is created with the default mode.** `CliUsageSource` makes `lyntai-usage-<guid>` under the temp folder with `Directory.CreateDirectory`, where the engine's working directory is user-only on Unix. <!-- item: state=blocked kind=env needs="a Unix run: the mode is not observable on Windows, so a test of the fix cannot fail here" -->

## Part 518 — what Daoris's third request to the agent layer left to build (A-LQ18, A-LQ19, A-LQ21 and A-LQ24's half)

_Opened by `docs/task-archive.md` **Part 518**, which built A-LQ20, A-LQ22, A-LQ23 and A-LQ24's statement (**D340** to **D343**) and answered the rest in `docs/agent-provider-daoris-study.md` §12. Every row below is one of Daoris's needs that rests on a record already read or on one Daoris has said it will send. Lanes `inference` and `platform`._

- [ ] **AG50 — a kept run on `codex-acp` and dsh (A-LQ19).** The study's `Unknown` cells (`docs/agent-provider-daoris-study.md` §11.1) for those two doors are Daoris's to fill from <!-- item: state=blocked kind=data needs="Daoris's recorded Codex sitting and dsh probe frames; re-checked 2026-11-15 with Daoris: not recorded, its Codex account is at its spend cap and its dsh build has no session to keep" -->
  records it is taking. Each cell moves only to what a record shows, and a record that shows a second prompt replacing the first leaves `queued-words` `Unsupported` for `codex-acp`.
- [ ] **AG51 — a kept run on the Claude CLI with stdin held (A-LQ21).** `claude -p --input-format stream-json` taking a user line per turn, an interrupt that keeps the session, and <!-- item: state=startable -->
  background work read until it ends: a second `IAgentRun` implementation over the existing `IProcessRunner.ExchangeLinesAsync`, with its abilities stated from Daoris's measured stream-json note
  (two turns on one process and an interrupt, REAL; a line framed mid-turn and held-stdin background work, never run, so `Unknown`). Design first, as AG39 was (**D251**): the CLI has no
  open to repeat and its turn ends on a `result` frame, so the run's reader is the existing `StreamJsonAgentReader` with per-run state, which is the work.

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
