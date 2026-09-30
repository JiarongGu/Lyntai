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

## Open items — 12 across 8 Parts: 8 startable, 2 blocked, 2 watch

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 113 | 33 | GEN-VERIFY-FAL — one submit → poll → fetch against fal.ai with a real key | blocked · env | a free Hugging Face account (an hf_ token) — its router proxies fal's own q… |
| 160 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 183 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 239 | 99 | `SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another ca… | watch · data | a recurrence with a full stack — the three hypotheses a reading can reach a… |
| 273 | 329 | Record an adopter's within-run measurement of D177's segmentation against i… | startable |  |
| 307 | 329 | Let a segmented rerank call REPORT what it sent, so a deployment timing its… | startable |  |
| 341 | 330 | Let a caller narrow a one-shot CLI call's disallowed tools per consumer, as… | startable |  |
| 374 | 331 | Let a spawn's environment REMOVE a variable, not only set one | startable |  |
| 394 | 331 | Say why a BYO runner loses the availability check, or let the runner answer… | startable |  |
| 415 | 332 | Add a `--setting-sources` seam and a `--strict-mcp-config` switch to `Claud… | startable |  |
| 459 | 333 | Let a caller hand the one-shot CLI path a settings file, per consumer, as t… | startable |  |
| 477 | 333 | Spawn one-shot calls from a directory the library owns, not the shared temp… | startable |  |

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
  <br>**2026-09-27: 0 of 40 more fresh-process runs of its class** (19 tests each, `Dedup_race` among them), so
  1 in 51 standalone overall. **Looping is not how this gets caught**; the stack has to come from the run it
  fails in.

## Part 329 — an adopter measured D177's segmentation against its own, and kept its own (2026-09-27)

_Reported by an adopting application answering `docs/task-archive.md` Part 289, whose outcome asked it to measure
**D177** against its app-side segmenting score-provider decorator once D177 released, and then remove its copy.
Checked against the tree at `v3.5.1` (HEAD changes nothing under `src/` since) when it was filed._

- [ ] **Record an adopter's within-run measurement of D177's segmentation against its own.** It configured D177 <!-- item: state=startable -->
  as close to its own windows as 3.5.1 allows, `docs/task-archive.md` Parts 305 and 306 included — for
  `bge-reranker-v2-m3` Q5_K_M and `LAMAR-600m` Q5_K_M (no declared window, served at 4,096) `MaxInputChars` 4,090,
  `MinDocumentShare` 0.5 and `MaxDocumentPiece` 1,000; for `mmarco-mMiniLMv2` Q8_0 (512) 506 and 0.5; for all three
  `Overlap` 0.25 and `MaxPiecesPerInput` 5 — on llama.cpp b10549, one GPU, a page of 8, no embedder, pre-registered
  and paired within each run. Found@8 on `rerank-segmented-adopter-long-notes`'s fixture (60 notes of 883–1,241
  characters, 240 questions), its own → D177: BGE 201 → 171 (discordant 31/1, p < 0.001), LAMAR 211 → 211 (5/5),
  mMiniLMv2 182 → 196 (10/24, p = 0.024). On 30 short and 30 long notes BGE went 196 → 208 and mMiniLMv2 184 → 198
  (both p < 0.001); on facts of at most 101 characters the two were byte-identical, rerank bodies included. Its
  rule — switch only if significantly better for its recommended reranker, BGE, and worse nowhere — KEEPS its own.
  <br>**Two readings, post hoc and the adopter's.** 26 of BGE's 31 losses were Japanese-worded questions answered
  with a page of four candidates the target was never among: co-recall links the adopter's arm had built by
  mid-run and D177's never did; on the mixed fixture it ran the other way (10 of D177's 12 BGE wins there).
  So that difference is the engine's reinforcement, not segmentation — two verifiers paired on a reinforcing engine
  carry their diverging recall histories too. mMiniLMv2's gain sat where the answer lies late in a note (past 1,000
  characters: 2/15, p = 0.002), plausibly because a piece cut at a sentence boundary splits an answer less often
  than a fixed-position window: the first evidence on D177's PLACEMENT, the one thing no setting could match, and
  confounded with `Overlap` (at most a quarter in D177, at least a quarter in the adopter's). D177 was also faster
  on long notes, by 0.05–0.9 s serial median, plausibly its short last piece.
  <br>**Amended 2026-09-28: the adopter measured the placement reading, and it did not hold.** Its next run put its
  OWN windows' edges at text boundaries — 97–100% of interior edges on a boundary, against 9–15% evenly spaced, with
  the same number of windows, budget, overlap and tail — paired within the run against its evenly spaced windows on
  the same long fixture: mMiniLMv2's found@8 went 182 → 188 (p = 0.146, not significant). And no arm of either run
  split an answer across pieces on these fixtures, so a split answer is not what D177 gained from here. That leaves
  D177's mMiniLMv2 lead to what the run did not change — its piece LENGTHS (half to all of the budget), its overlap
  of at most a quarter, or its piece COUNT — untested. Across runs, and therefore no finding: found@8 196 (D177),
  188 (boundary windows), 182 (evenly spaced); top-1 90, 90, 79.
  <br>Suggested: a `docs/memory-measurements.md` §5 result beside `rerank-segmented-adopter-long-notes`, measured by
  the adopter and `ships=no`, carrying the configuration, the figures, the link-dynamics reading AS a reading, the
  placement reading as REFUTED by the amendment above, and the limits (one GPU, one run per fixture, constructed
  fixtures, no CPU arm), then `check-measurements --write`; D177's Known limits should NOT cite placement as its
  advantage. Both halves are then recorded: `docs/task-archive.md` Part 289 names the decorator to remove, and this
  result says why it stayed.

- [ ] **Let a segmented rerank call REPORT what it sent, so a deployment timing its calls can count them.** <!-- item: state=startable -->
  The adopter's decorator also sizes each call by TIME: it learns ms per pair token from the calls it makes and
  gives each long document only the pieces that fit a 60 s verification deadline, down to one, skipping a recall
  where even one each cannot fit — its reranker scored ~3.1 s per 1,000 pair tokens on one CPU, where an unpaced
  call waits out the deadline for no verdict. Since `docs/task-archive.md` Part 305 such a pace can sit on D177,
  narrowing `ScoreRequest.MaxPiecesPerInput` per call and predicting from the cap, as D177's amendment says it
  should. What it cannot do is LEARN: `HttpRerankTransport.CallAsync` returns `ScoreResponse.Success(scores)` with
  no usage (`src/Lyntai.Providers.Basic/Http/HttpRerankTransport.cs:106`), and the pieces it sent stay in an
  internal `SegmentPlan` (`src/Lyntai.Providers.Basic/Http/InputSegmenter.cs:11`) — `InputSegmentation.Spread` is
  public, the segmenter is not. So a pace divides a call's time by a BOUND on what it sent: the upper bound
  under-estimates the rate and grants too many pieces next time, the unsafe direction, and the safe lower bound
  sits up to ~2× below it on the adopter's long notes.
  <br>**The wire already says.** llama.cpp b10549 answers `/v1/rerank` with
  `"usage":{"prompt_tokens":1075,"total_tokens":1075}` (the adopter's screen: a 7-token query against a 1,041-token
  document and a short one), and `HttpVectorTransport` reads that member for an embed call
  (`src/Lyntai.Providers.Basic/Http/HttpVectorTransport.cs:191-201`). It is a ledger gap as well: **D163** makes a
  rerank token-metered, so a consumer's token cap binds it, yet with no reported usage a cap on `memory` never
  counts what an HTTP reranker spends for the scoring verifier.
  <br>Suggested: the rerank transport reads `usage.prompt_tokens` into `ScoreResponse.Usage` as the vector
  transport does — null, never zero, where the wire says nothing — so the governed router records it like an
  embed's; a server's count also spares a pace its per-script character weights. Where nothing is reported — a
  server without `usage`, or the in-process cross-encoder, whose `Usage` is null because it spends nothing — what
  to report is the design: a per-input piece count, or the characters sent, as an init property beside `Usage`,
  since a count a pace needs is not spend a ledger bills. The library still times nothing and sets no budget — the
  latency policy D177 rejected. Nor is counting an input's pieces BEFORE the call re-proposed: D177's amendment
  refused it because the cap bounds a call from above, which is what a prediction needs, and that holds — the gap
  is learning, after the call. Tests: a segmented call's `Usage` carries the wire's count, and a reply without
  `usage` leaves it null.

## Part 330 — a one-shot CLI call disallows only AskUserQuestion, so a library caller cannot narrow its tools (2026-09-28)

_Reported by an adopting application. Checked against the tree at `v3.5.1` (`ClaudeArgs.cs`, `ClaudeAgentArgs.cs`,
`McpToolHostOptions.ToolsByConsumer`)._

- [ ] **Let a caller narrow a one-shot CLI call's disallowed tools per consumer, as D190 did for the tool host.** <!-- item: state=startable -->
  `ClaudeArgs.Build` (`src/Lyntai.Providers.Basic/ClaudeCli/ClaudeArgs.cs`) opens every print-mode completion — the
  one-shot `ITextClient`/`ClaudeCliProvider` path an adopter uses for scorers, an LLM memory judge, and untagged
  utility calls — with `--disallowed-tools AskUserQuestion` and nothing else. The AGENT path already lets the caller
  add tools: `ClaudeAgentOptions.DisallowedTools` is unioned with the always-denied set in `ClaudeAgentArgs`
  (`AskUserQuestion`/`ExitPlanMode`/`EnterPlanMode` + `ReadOnly`'s `Edit`/`Write`/`NotebookEdit`). The COMPLETION path
  has no equivalent seam. On Windows the CLI's `PowerShell` tool is on by default and `Monitor` uses Bash's permission
  rules, so both are available to a one-shot call, and a `-p` run with no permission host still runs the read-only
  and permission-free tools; a library caller that wants a stricter one-shot tool set (no shell of any kind, say, for
  a judge that should only read) cannot ask for one — it can only set the whole process environment, which is coarse
  and does not remove a default-on tool.
  <br>**Why an adopter feels it.** One adopter's agent runs remove `PowerShell` and `Monitor` at its own agent seam
  (`ClaudeAgentOptions.DisallowedTools`), but its one-shot scorer / memory-judge / rephrase calls go through
  `ClaudeArgs` and cannot — so the same tools it removed from its jailed agent are reachable from its unattended
  utility calls, with no seam to close. It has NO workaround for this half, and is recording the gap here rather than
  shipping one, because the fix belongs in the library: a one-shot call's tool set is the library's argv to build.
  <br>Suggested: give the completion path the same shape as the agent path — an optional per-call disallowed-tools
  list (an `HttpModelOptions`-style option, or a `ClaudeCompletionOptions.DisallowedTools`, resolved per CONSUMER like
  `McpToolHostOptions.ToolsByConsumer` so `scorer` and `memory` can differ from `default`), unioned with the
  always-denied `AskUserQuestion`. Keep the union — a caller adds denials, never removes `AskUserQuestion`. The
  agent path's `ClaudeAgentArgs` is the worked precedent for the union; D190 is the precedent for keying it by
  consumer. Tests: a one-shot call with a caller list denies that list plus `AskUserQuestion`; an empty/absent list
  is byte-identical to today's argv (no regression). **When this ships, the adopter ADDS the same removal to its
  one-shot calls, and deletes nothing.** Its agent-side removal already goes through the AGENT path's own seam
  (`ClaudeAgentOptions.DisallowedTools`, which this item does not touch) and stays exactly as it is; this item covers
  the one-shot path only, where the adopter has no workaround to remove. The reciprocal note lives in the adopter's
  `.claude/rules/dev-conventions.md` under the jail bullet.

## Part 331 — a caller can SET a spawn's environment but never REMOVE an inherited variable (2026-09-28)

_Reported by an adopting application. Checked against the tree at `v3.5.1` (`ProcessRunner.cs`,
`CliProviderEngine.cs`, `ClaudeAgentSession.cs`)._

- [ ] **Let a spawn's environment REMOVE a variable, not only set one.** <!-- item: state=startable -->
  `ProcessRunner.Start` (`src/Lyntai.Core/Processes/ProcessRunner.cs:449-450`) copies the caller's `environment` into
  the child with `psi.Environment[k] = v`, and the seam is `IReadOnlyDictionary<string, string>` all the way up
  (`CliProviderEngine`'s `environment`, `ClaudeAgentSession`'s): a caller can add or override a variable, never take
  away one the host process inherited — an empty string sets it EMPTY, which a CLI may still read as present. A host
  that must keep an inherited API key, a provider switch or another endpoint, a parent session's markers, or its own
  secrets out of the CLI it spawns through Lyntai therefore has one option: strip them from its WHOLE PROCESS at
  startup (remembering the few it still reads), which also strips them from every other child it starts and from
  every spawn it did not mean to narrow.
  <br>**Why an adopter feels it.** One adopter does exactly that: its claude CLI runs (the agent session and the
  one-shot provider alike) must not inherit an API key or a base URL, and it cannot say so per spawn. It is a
  workaround for this item; the adopter's `.claude/rules/dev-conventions.md` (*Data folder discipline*, the
  child-environment bullet) names this Part as what would let it narrow the strip.
  <br>Suggested: `IReadOnlyDictionary<string, string?>` where null REMOVES (or a separate
  `IReadOnlyCollection<string>` of names to remove), threaded through every place a caller passes `environment` —
  non-breaking by an overload; the default (none) is byte-identical to today's child environment. Tests: a variable
  the host holds and the caller names with null is absent in the child; a set one is set; no option changes nothing.
  **When this ships, the adopter can move its CLI policy from the process to the spawn**, keeping a process-level
  strip only for the spawns it does not control (a library's own children).

- [ ] **Say why a BYO runner loses the availability check, or let the runner answer it.** <!-- item: state=startable -->
  `CliProviderEngine.IsAvailable` is `runner is not ProcessRunner || ProcessRunner.CommandExists(...)`
  (`src/Lyntai.Core/Inference/Cli/CliProviderEngine.cs:84`), OPTIMISTIC for a BYO `IProcessRunner` by design — its
  remarks: a sandboxed or remote runner resolves the command in its own environment. But the documented seam for
  controlling a spawn — a BYO runner — is also what a LOCAL host reaches for to adjust one (the item above), and a
  thin decorator over `ProcessRunner` then silently loses the probe: a missing CLI is no longer skipped by the router
  and becomes a failed call on every turn. That is the second reason the adopter above strips its process instead of
  wrapping the runner.
  <br>Suggested: let the runner answer (a default interface member such as `bool CommandExists(string exe)`, whose
  default keeps today's optimism and which `ProcessRunner` — and so any decorator delegating to it — answers by the
  real probe), so a local decorator keeps the check and a remote runner keeps its optimism. Tests: a decorator over
  `ProcessRunner` reports a missing command unavailable; a custom runner that does not implement the member stays
  optimistic, byte-for-byte as today.

## Part 332 — an agent run cannot tell the claude CLI which settings files to load (2026-09-28)

_Reported by an adopting application. Checked against the tree at `v3.5.1` (`ClaudeAgentOptions.cs`,
`ClaudeAgentArgs.cs`, `ClaudeArgs.cs`, `CliCommand.cs`). Both flags VERIFIED by that adopter against the installed CLI
2.1.283 at 0 tokens, the same day (a `UserPromptSubmit` hook in `--settings` exiting 2, so no model call; marker files,
the `system/init` event and an `InstructionsLoaded` log read back)._

- [ ] **Add a `--setting-sources` seam and a `--strict-mcp-config` switch to `ClaudeAgentOptions` (and the one-shot path).** <!-- item: state=startable -->
  A `-p` run loads its working directory's settings files on its own and executes their hooks, and connects the
  servers of a project `.mcp.json`, before any caller-side decision. Where that directory is also used INTERACTIVELY,
  those files are a person's own — Claude Code saves a permission they approve into `.claude/settings.local.json` —
  so a rule like `Bash(rm:*)` they approved for themselves applies to the caller's agent too. `ClaudeAgentArgs` can
  emit neither flag, `AgentSessionOptions` carries no pass-through arguments, and `ClaudeArgs` (the one-shot path)
  has the same gap.
  <br>**What the CLI does, measured** (2.1.283; `claude --help`: `--setting-sources <sources>` "Comma-separated list
  of setting sources to load (user, project, local)", `--strict-mcp-config` "Only use MCP servers from --mcp-config,
  ignoring all other MCP configurations"):
  `--strict-mcp-config` — a project `.mcp.json` stdio server is not started, a `--mcp-config` server still is.
  `--setting-sources` scopes whole SOURCES, and each source is more than its settings file: `project` is also the
  project `CLAUDE.md`, `.claude/rules`, skills, agents and slash commands (with `user` alone every one of them was
  gone, and the project `.mcp.json` too); `local` is `settings.local.json` AND `CLAUDE.local.md`; `user` is the
  config directory's settings, hooks, skills and `CLAUDE.md`. With `project`, the user and local hooks did not run and
  the local deny rule did not apply, while the project's did. A command-line `--settings` applies under every value —
  its hooks ran and its deny rule held — and the login does not depend on the sources (`auth status` under both
  flags reports the same account). Flags placed BEFORE `-p` (as prefix arguments land) are honoured.
  <br>**So the useful value for an agent that runs on a project knowledge base is `project`, not `user`**: the
  project scope cannot be dropped without the knowledge base, which leaves a project `.claude/settings.json` read —
  the caller's residual to state, not the library's to close. The CLI has no way to load a project's `CLAUDE.md`,
  rules and skills without its `settings.json`; if that matters, it is a CLI request, not this item.
  <br>**Why an adopter feels it, and what it does meanwhile.** One adopter now composes its command variable
  (`LYNTAI_PROVIDER_CMD`) as the resolved CLI plus `--setting-sources project --strict-mcp-config`, relying on
  `CliCommand.Resolve` turning the extra tokens into prefix arguments that both `ClaudeAgentSession` and the one-shot
  provider put ahead of their own — recorded as a workaround in its `.claude/rules/dev-conventions.md` (the jail
  bullets, item (7)) with this Part named. It works, and it is fragile: it rides the command's tokenisation (paths
  must be double-quoted), it applies to every call the process makes rather than per run, and an operator's own
  `LYNTAI_PROVIDER_CMD` has to be composed rather than replaced. (It first moved the three files out of the directory
  around every run instead — removing the person's own interactive config, which is why it changed.)
  <br>Suggested: `IReadOnlyList<string>? SettingSources` (null = the CLI's default, byte-identical argv) and
  `bool StrictMcpConfig` on `ClaudeAgentOptions`, emitted by `ClaudeAgentArgs.TryBuild`; the same pair wherever the
  completion path takes per-consumer options (Part 330). Tests: argv carries `--setting-sources project` /
  `--strict-mcp-config` when set, and nothing when unset. **When this ships, the adopter sets `SettingSources =
  ["project"]` and `StrictMcpConfig = true` on its runs and deletes its command composition** (it keeps its own
  run-scoped undo of a run that edits those files, which is its policy, not a library gap).

## Part 333 — a one-shot CLI call takes no settings file, and its neutral cwd is the shared temp directory (2026-09-29)

_Reported by an adopting application. Checked against the tree at `v3.5.1` (`CliProviderEngine.cs`, `ClaudeArgs.cs`,
`ClaudeAgentOptions.cs`). The CLI behaviour below was measured by that adopter on the installed CLI 2.1.283 at 0 tokens
(a `UserPromptSubmit` hook in `--settings` exiting 2, so no model call; the `system/init` event's `apiKeySource` and
marker files read back)._

- [ ] **Let a caller hand the one-shot CLI path a settings file, per consumer, as the agent path already can.** <!-- item: state=startable -->
  `ClaudeAgentOptions.SettingsPath` gives an agent run a command-line `--settings` file, and a command-line settings
  file outranks the project and local scopes. The completion path (`ClaudeArgs.Build`, the `ClaudeCliProvider` an
  adopter uses for scorers, an LLM memory judge and untagged utility calls) has no equivalent, so settings an adopter
  needs on EVERY claude call cannot reach those calls. Two it measured as load-bearing:
  `disableSkillShellExecution: true` — without it a skill's or custom command's `` !`cmd` `` line runs its command as
  prompt preprocessing, before any PreToolUse hook can see it (the hook was never called; with the setting, nothing
  ran, and a project `.claude/settings.json` setting it `false` could not undo it — the CLI treats it as restrictive);
  and blanking the key paths — `"apiKeyHelper": ""` plus the API-key, provider-selector and endpoint variables set to
  `""` in `env` — without which a project `.claude/settings.json` with an `apiKeyHelper` RAN it at init and reported
  `apiKeySource=apiKeyHelper`, and one with `ANTHROPIC_BASE_URL` sent the calls to that host; with the blanks,
  `apiKeySource=none`, the helper did not run, the fake host got no request, and the subscription login still
  answered. (`forceLoginMethod: "claudeai"` did not stop the helper.)
  <br>Suggested: `SettingsPath` wherever the completion path takes per-consumer options (Part 330's shape, keyed like
  `McpToolHostOptions.ToolsByConsumer`), emitted as `--settings <path>`; absent = today's argv, byte-identical. Tests:
  the argv carries `--settings` when set, nothing when unset. **When this ships, the adopter passes the settings file it
  already generates for its agent runs to its one-shot calls too, and deletes nothing** — it has no workaround for this
  half today.
- [ ] **Spawn one-shot calls from a directory the library owns, not the shared temp directory itself.** <!-- item: state=startable -->
  `CliProviderEngine.NeutralWorkingDirectory` is `Path.GetTempPath()` — the account's shared temp folder, writable by
  every process the user runs. The CLI loads its working directory's project scope on its own (a `-p` run reads a
  project `.claude/settings.json`, runs its hooks and its `apiKeyHelper`, and loads a `CLAUDE.md` there — measured in a
  data folder; not measured with temp itself as the cwd, but nothing in the CLI treats temp differently), so a `%TEMP%\.claude\settings.json` or `%TEMP%\CLAUDE.md` planted by
  any program is loaded into every library completion and judge call — the very skew the neutral cwd exists to avoid
  (design §6), and a way to run a command or supply an API key through a call the caller believed isolated. Whether
  the project scope also walks up from temp (a `CLAUDE.md` in a parent such as the profile directory) is unmeasured.
  <br>Suggested: a library-owned empty directory — created once per process under temp with an unguessable name
  (or `Directory.CreateTempSubdirectory`), re-created if it disappears, never reused across processes — or an
  `LyntaiOptions` override for adopters that want their own; and a test that the completion runner's working
  directory is not `Path.GetTempPath()` itself. The first item closes the settings half regardless; this one closes
  the instructions half (`CLAUDE.md`), which no settings file can.

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