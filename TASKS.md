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

## Open items — 84 across 19 Parts: 1 startable, 3 blocked, 1 watch, 79 decision-only

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 185 | 33 | GEN-VERIFY-FAL, the last line — the Hugging Face route | blocked · env | an hf_ token for the router route |
| 226 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 253 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 325 | 417 | MC8 — run the wrong-end instrument on an adjudicated corpus, with a bar wri… | blocked · decision+data | two reviewers' independent labels on a sample of the pairs, and a bar a per… |
| 347 | 475 | Kept-run edges that need a ruling before a change | decision-only | a ruling for each: refuse a word after a stop or document the order, `Dispo… |
| 359 | 479 | The write tools let a model end, revive or demote an entry the approver nev… | decision-only | a ruling: whether the tool may state validity and a grade at all, or the re… |
| 365 | 479 | A supersession whose successor is an entry that already ended leaves nothin… | decision-only | a ruling: may a restated claim revive an ended entry, or does the supersess… |
| 369 | 479 | What the approver is shown is not all that is applied | decision-only | a ruling: which of by, at, validity and scope the request must carry, or a … |
| 372 | 479 | A keyed end or confirmation's file is written before its receipt, so a cras… | decision-only | a ruling: accept and document the window (a guarded repeat then reads Chang… |
| 376 | 479 | A host's `IMemoryViewCheck` is asked about an expansion's root only, not th… | decision-only | a ruling: whether the check also filters each neighbour an expansion return… |
| 379 | 479 | A future `at` on an end or a confirmation sets `EndedReason` while recall s… | decision-only | a ruling: may an end or a failed check state an instant after now, or does … |
| 381 | 479 | A receipt expires by the caller's clock and the purge runs on it | decision-only | a ruling: whether a receipt expires by the store's clock or the caller's (D… |
| 392 | 481 | An HTTP agent turn can spend a second billed request the retry budget never… | decision-only | a ruling: whether the agent door turns the engine's resend off (the budget … |
| 395 | 481 | A rejected limit is never cleared inside a turn | decision-only | a ruling: whether an allowed report clears an earlier rejection, which need… |
| 398 | 481 | A wrapped agent session registration is never disposed | decision-only | a ruling: whether RetryingAgentSession becomes IAsyncDisposable and forward… |
| 401 | 481 | A `Taken` resume repeats the system prompt | decision-only | a ruling: whether a resume drops SystemPrompt, which needs a measurement of… |
| 404 | 481 | Codex's `FinalText` joins every agent message with no separator, and the re… | decision-only | a ruling: whether FinalText is the last message or all of them, since a tes… |
| 413 | 483 | A retried or polled job leaves its partition's FIFO | decision-only | a ruling: whether a retrying or polling head blocks its partition, or the d… |
| 416 | 483 | A missing handler fails the job for good | decision-only | a ruling: a terminal Failed (pinned by Unknown_type_fails_the_job), or a re… |
| 418 | 483 | An interval schedule drifts by its own lateness | decision-only | a ruling: anchor to the previous slot, or since the last fire (the class do… |
| 420 | 483 | A cancel the handler honoured with a different exception ends in a retry or… | decision-only | a ruling: whether a cancel request seen plus any thrown exception finalises… |
| 422 | 483 | A cron that never occurs is accepted at registration | decision-only | a ruling: whether Validate refuses an expression with no occurrence, which … |
| 424 | 483 | Removing a stored schedule resets the next run of a build-time schedule of … | decision-only | a ruling: which side owns the next-run and trigger keys when a build-time s… |
| 433 | 484 | The file backend reissues a removed curated or memory id after a restart; S… | decision-only | a ruling: a persisted high-water mark for these two domains as D174 gave th… |
| 435 | 484 | A value holding a lone surrogate is three things | decision-only | a ruling: refuse a lone surrogate on every backend or replace it on every b… |
| 437 | 484 | `ListKeysAsync` sorts keys past U+FFFF differently | decision-only | a ruling: which order the contract's 'ordinal' means, then a fact with a su… |
| 439 | 484 | A vector with a NaN or infinite component is accepted by one backend and re… | decision-only | a ruling: refuse a non-finite vector, or score it 0 and rank it last, on ev… |
| 441 | 484 | A response cache's `MaxEntries` of 0 or less caches nothing in memory and k… | decision-only | a ruling: whether a non-positive cap is refused, means no cache, or means o… |
| 449 | 485 | A `.cmd` launcher re-parses the argv through `cmd.exe` | decision-only | a ruling: refuse unsafe argv for a .bat or .cmd, escape it for cmd.exe, or … |
| 452 | 485 | A stdio MCP server's `env` goes into Codex's argv | decision-only | a ruling: a pass-through of named variables (codex's env_vars plus the chil… |
| 454 | 485 | The MCP bearer's variable name may not be one Codex strips from a shell too… | decision-only | a ruling: rename to carry TOKEN or SECRET, or document it, after reading co… |
| 456 | 485 | A rate limit named only on stderr carries two verdicts | decision-only | a ruling: whether a limit or transient reading of stderr stays Fatal as Fai… |
| 458 | 485 | A parent that exited while a grandchild holds its stdout may hang `RunAsync` | decision-only | a ruling: a bounded drain after the child exits, and a node parent-and-deta… |
| 460 | 485 | A stdin producer that throws leaves the child's stdin open, and the fault i… | decision-only | a ruling: whether a producer's fault ends the exchange with that fault or o… |
| 462 | 485 | The two-minute inactivity window kills a long silent Codex step | decision-only | a ruling: a longer default window for the agent doors, or a documented warn… |
| 470 | 486 | A cache hit replays the original `Usage` | decision-only | a ruling: zero or strip Usage on a hit, or keep it and mark the reply as a … |
| 472 | 486 | A stream's billed usage is dropped when the router turns an empty `Final` i… | decision-only | a ruling: carry the dropped usage onto the failure, or state that a failed … |
| 480 | 487 | A blob resource and an image or audio block in an MCP tool result are descr… | decision-only | a ruling: pass the block's media type and size, or its bytes, or a link a h… |
| 482 | 487 | The hosted MCP accept loop ends on any exception that is not a shutdown | decision-only | a ruling: log and keep accepting on a transient listener fault, or stop and… |
| 484 | 487 | A judge answering on the wrong scale reads as a perfect score | decision-only | a ruling: clamp (today), refuse the dimension as unscored, or rescale; and … |
| 486 | 487 | `JsonExtract.ExtractObject` commits to the first `{` | decision-only | a ruling: retry later braces, with a bound on how many, or document the fir… |
| 488 | 487 | A secret's ciphertext is not bound to its name | decision-only | a ruling on the threat model: bind the name as associated data (a stored-fo… |
| 490 | 487 | Two processes over one store can each generate a master key | decision-only | a ruling: a compare-and-set on IKeyValueStore (an interface change) or a do… |
| 498 | 488 | A trace step's `seq` is `INTEGER` on Postgres and 64-bit on SQLite | decision-only | a ruling: widen the column with a migration, which rewrites and locks the t… |
| 500 | 488 | SQLite's fallback `LIKE` folds ASCII case only | decision-only | a ruling: a Unicode-aware LIKE or lower on the connection, an in-process sc… |
| 502 | 488 | Postgres retries a conversation append four times, so eight concurrent appe… | decision-only | a ruling: a per-thread advisory lock, or an unbounded retry bounded by the … |
| 503 | 488 | An empty vector is refused on Postgres and accepted elsewhere, and a NUL in… | decision-only | a ruling for each: refuse an empty vector on every backend or score it 0, a… |
| 511 | 489 | An OpenAI `refusal` is retried and reported `Failed` | decision-only | a ruling: map a non-empty refusal to Refused (no fallback, no dead-host str… |
| 513 | 489 | A stream that errors after content, or never reaches its terminator, is a c… | decision-only | a ruling: end Error(classified) when content streamed, an error line was se… |
| 515 | 489 | `max_tokens` and a non-default `temperature` are sent to every OpenAI-shape… | decision-only | a ruling: a MaxTokensField option, a per-model rule, or documented as the h… |
| 517 | 489 | A negative usage count is accepted | decision-only | a ruling: clamp a negative count to 0, or read it as absent, on every wire … |
| 518 | 489 | A non-finite request number escapes the provider as an exception | decision-only | a ruling: validate a request's numbers up front (throw to the caller) or an… |
| 520 | 489 | A server that ignores `stream:true` and answers one JSON body is dropped | decision-only | a ruling: fall back to buffered extraction when the first payload carries m… |
| 522 | 489 | A base URL ending in a version other than `/v1` gets `/v1` appended | decision-only | a ruling: a flag to use the base verbatim, a pattern for any trailing versi… |
| 529 | 490 | An export whose pooling or module list this reader does not support is mean… | decision-only | a ruling: refuse (InvalidDataException) a pooling_mode other than cls or me… |
| 532 | 490 | `Model2VecProvider` embeds and counts an unknown token | decision-only | a ruling: skip the unknown id, which changes every vector already stored fr… |
| 534 | 490 | A `vocab.txt` longer than the table counts tokens that add nothing | decision-only | a ruling: refuse a vocabulary longer than the table at load, log it, or cou… |
| 535 | 490 | A RoBERTa-family export that declares no narrower limit keeps 514 | decision-only | a ruling: subtract the two padding positions when config.json's model_type … |
| 537 | 490 | A non-segmented call is one forward pass whatever its size | decision-only | a ruling: keep An_unsegmented_call_is_ONE_pass_however_many_inputs_it_holds… |
| 538 | 490 | A segmented text's vector is always unit length, a one-window text's is not… | decision-only | a ruling: document it as direction-only (cosine is unaffected) or return th… |
| 540 | 490 | `LlamaSharpProvider` leaks the weights when the executor fails to build, an… | startable |  |
| 549 | 491 | A transient fetch failure fails a durable job for good | decision-only | a ruling: which verdicts are transient and whether a transient fetch polls … |
| 551 | 491 | A ComfyUI run lost to a server restart polls for ever | decision-only | a ruling: consult /queue and fail or call it inconclusive when the id is in… |
| 553 | 491 | Cancelling a durable generation job does not cancel the backend render | decision-only | a ruling: a best-effort CancelAsync when a cancel is seen with an operation… |
| 554 | 491 | `generate_fetch` bills and delivers again on every call, and the job engine… | decision-only | a ruling: whether the spend ledger or the tool owns de-duplication per (bac… |
| 556 | 491 | `generate_backends` lists and probes every registered `IModelProvider` | decision-only | a ruling: filter to providers that produce image, video, audio or 3d, or ke… |
| 558 | 491 | A ComfyUI output walk includes `type: "temp"` previews | decision-only | a ruling: drop temp outputs by default with an option to include them, or k… |
| 565 | 492 | A blend swallows the caller-mistake exceptions the graph engine throws on p… | decision-only | a ruling: which exception types a blend lets through as a caller's mistake … |
| 567 | 492 | `PruneAsync` with a floor above 1 removes everything or nothing, by age pol… | decision-only | a ruling: refuse a floor above 1, clamp it to 1, or define the floor as inc… |
| 569 | 492 | Entries cut by `CharBudget` are still reinforced and logged | decision-only | a ruling: reinforce after the cut, or document that learning follows the pa… |
| 571 | 492 | `PruneAsync(olderThan:)` has no range handling | decision-only | a ruling: refuse a non-positive span, treat it as no age criterion, or clam… |
| 572 | 492 | `RememberAsync` on the graph engine does not validate content | decision-only | a ruling: refuse or skip a blank write, the same on the graph, semantic and… |
| 574 | 492 | A character budget of 0 means unbounded for a recall and the root alone for… | decision-only | a ruling: one meaning for 0 on both, and the validation message for ExpandC… |
| 576 | 492 | Portable import validates `stability` and no other number | decision-only | a ruling: refuse a negative age and weight, or keep them because elapsedAge… |
| 578 | 492 | A `History` recall writes co-activation edges that include ended entries | decision-only | a ruling: filter ended entries before the pairing, or state that history li… |
| 580 | 492 | A large co-activation set computes its pair count as an `int` | decision-only | a ruling: bound CoActivationCap, or compute in long and cap the pairs, sinc… |
| 588 | 493 | `SearchTerms` counts UTF-16 units and the trigram index counts code points | decision-only | a ruling: measure terms in code points so every backend drops the sub-trigr… |
| 590 | 493 | A vector collection that was emptied stays stamped with its old space | decision-only | a ruling: treat a stamped but empty collection as unclaimed, or release the… |
| 592 | 493 | A signal bag of only non-finite values replaces the stored one in-process a… | decision-only | a ruling: whether an all-non-finite bag counts as no opinion, which is what… |
| 594 | 493 | A keyed link across engines writes the edge and a receipt in the SQL stores | decision-only | a ruling: add the engine predicate to StrengthenEdge and PlanLink, which ch… |
| 602 | 494 | Streamed text is never output-gated | decision-only | a ruling: buffer and gate before emitting, expose a stream gate the host su… |
| 604 | 494 | A guard block inside the tool loop reads `Blocked: false` on the chat result | decision-only | a ruling: set Blocked for a block inside the loop, or narrow the property's… |
| 606 | 494 | `AddVectorToolSelector` with no embedding backend fails quietly on every run | decision-only | a ruling: whether AddLyntai refuses the registration as AddSemanticMemory d… |
| 608 | 494 | `ToolLoop` bounds its iterations and not the calls in one turn | decision-only | a ruling: a per-turn call cap on the options, and whether a turn that throw… |

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

## Part 475 — what the 2026-11-07 review of the agent layer found and did not fix yet

_Opened by `docs/task-archive.md` **Part 474**: three read-only reviews of the agent layer the 2026-11-07 sessions built (the pool and
its failure core, the kept run, the ACP frame readers). The startable rows closed in `docs/task-archive.md` **Part 477**; what
stays is the one that needs a ruling. Lane `inference`._

- [ ] **Kept-run edges that need a ruling before a change**: a word sent after `StopAsync` is written behind the `session/cancel` <!-- item: state=decision-only needs="a ruling for each: refuse a word after a stop or document the order, `Disposed` or `run-killed` after a forced dispose, and whether a reading may lift a stated retry-not-before" -->
  (`AcpAgentReader.cs` `TryPrompt`); a dispose that had to kill the agent reports `run-killed`, where `IAgentRun.Ended` says
  `Disposed`; a quiet timer can be left unarmed by a stale reading in `ScheduleQuiet`; `ReportReadingAsync` lifts a service-stated
  `RetryNotBefore` hold. Each is arguable from the documents as written.

## Part 479 — what the 2026-11-07 review of the memory-currency layer found and did not fix yet

_Opened by `docs/task-archive.md` **Part 479**: three read-only reviews of what MC1 to MC12 built (the relational receipt and
supersession runners, the file and in-process stores, and the engine, the gate, the write tools and the view), each finding read
against the code before it was filed. The certain and small ones closed with a test in that Part. What stays needs a design
choice, or is a crash window to close with a store test of its own. Lanes `memory` and `storage`._

- [ ] **The write tools let a model end, revive or demote an entry the approver never sees.** `<prefix>_remember` states `validTo` and <!-- item: state=decision-only needs="a ruling: whether the tool may state validity and a grade at all, or the refresh must keep a stated end and a stronger grade (D241)" -->
  `validFrom` from the model and always writes `Associative`; a write of a live entry's exact text refreshes it, a stated bound
  replaces the stored one (`MemoryGraphState.Refresh` and the SQL upserts) and a stated grade replaces the stored one. So a
  `validTo` in the past ends an entry with no approver and no revision, a later one reopens an entry the approver ended (recall
  reads only the bounds and `SupersededAt`, not `EndedReason`), and the host's `Authoritative` entry is demoted and made prunable.
  Each is arguable from **D241** ("an end a confirmer applies"), so none is a code change before a ruling.
- [ ] **A supersession whose successor is an entry that already ended leaves nothing current**, on every store: the successor is <!-- item: state=decision-only needs="a ruling: may a restated claim revive an ended entry, or does the supersession refuse an ended successor (D242, D241)" -->
  matched by its content, refreshed and keeps its end, and the predecessor is ended by it, so `Applied` is true with both ended
  (a status that flips open, closed, open does this). The gate's store-first path ends the same way. Whether a restated claim may
  revive an ended one is an identity question.
- [ ] **What the approver is shown is not all that is applied.** `MemoryChangeRequest` carries the kind, task, targets, claim text, <!-- item: state=decision-only needs="a ruling: which of by, at, validity and scope the request must carry, or a refusal of a model-stated instant before the target's start" -->
  reason and evidence, while the model's `by`, `at` and the successor's validity, scope and headline are applied after it says yes.
  An `at` before the target began, or a successor whose `validTo` has passed, is approved unseen.
- [ ] **A keyed end or confirmation's file is written before its receipt, so a crash between leaves the effect with no receipt.** A <!-- item: state=decision-only needs="a ruling: accept and document the window (a guarded repeat then reads Changed, an unguarded one AlreadyEnded), or journal the effect in a line a restart catches up, as a supersession's is (D242)" -->
  guarded retry of the call the crash cut gets `Changed`. Reversing the order would leave a receipt for an effect that never landed, and
  the end's fields (`ValidTo`, no `EndedLine`) do not fit the line a supersession uses. `FileSystemMemoryGraphStore.ApplyEffectThenReceipt`
  states the window; closing it is a design choice about the journal.
- [ ] **A host's `IMemoryViewCheck` is asked about an expansion's root only, not the neighbours it returns.** `IMemoryView.ExpandAsync` <!-- item: state=decision-only needs="a ruling: whether the check also filters each neighbour an expansion returns, or the host's refusal of the root is all it promises (D242)" -->
  documents that its neighbours are not asked about, so a reference the host refuses can come back as a neighbour of one it permits.
  Filtering them costs one call per neighbour and changes what an expansion returns.
- [ ] **A future `at` on an end or a confirmation sets `EndedReason` while recall still shows the entry until then.** <!-- item: state=decision-only needs="a ruling: may an end or a failed check state an instant after now, or does the engine refuse it (D241, D242)" -->
  A scheduled end is a use the engine allows, and the end tool lets a model state the instant; a refusal narrows both.
- [ ] **A receipt expires by the caller's clock and the purge runs on it.** A host whose clock runs ahead purges receipts another <!-- item: state=decision-only needs="a ruling: whether a receipt expires by the store's clock or the caller's (D242), which shortens or lengthens retention by the skew" -->
  host's clock still counts live, and the file store's `Restore` judges expiry by its own clock where every live check uses the
  caller's. Retention is shortened or lengthened by the skew in either case.

## Part 481 — what the 2026-11-08 review of the Claude door and the agent core found and did not fix yet

_Opened by `docs/task-archive.md` **Part 481**; its five startable rows closed in `docs/task-archive.md` **Part 482**, leaving the five that need a ruling. Two read-only reviews of what the earlier agent-layer review did not read — the Claude
stream-json door (`StreamJsonAgentReader`, `StreamJsonFailure`, `StreamJsonFields`) and the shared core with the Codex and HTTP
doors (`RetryingAgentSession`, the HTTP door and its chat engine, the transcript recorder) — each finding read against the code
before it was filed. Lanes `inference` and `platform`._

- [ ] **An HTTP agent turn can spend a second billed request the retry budget never sees.** `HttpChatEngine.CompleteAsync` resends once on an <!-- item: state=decision-only needs="a ruling: whether the agent door turns the engine's resend off (the budget is RetryingAgentSession's, D233), or states it as inside the door" -->
  empty or malformed 200 body, for every caller, and `HttpAgentSession` documents that nothing falls over so the budget stays the retry
  layer's. The resend is harmless to side effects (no tool re-runs) and is a second paid call.
- [ ] **A rejected limit is never cleared inside a turn.** `StreamJsonAgentReader.NoteRateLimit` keeps `_rejectedLimit` once seen, so a later <!-- item: state=decision-only needs="a ruling: whether an allowed report clears an earlier rejection, which needs a capture of the CLI sending one after the other" -->
  `allowed` frame does not clear it and an unrelated `is_error` result is classified `Limit`. Whether the CLI sends `allowed` after `rejected`
  in one turn is not shown by any capture here.
- [ ] **A wrapped agent session registration is never disposed.** `AgentRetryRegistration` builds the inner by hand and `RetryingAgentSession` <!-- item: state=decision-only needs="a ruling: whether RetryingAgentSession becomes IAsyncDisposable and forwards, given that a registered instance is the host's to dispose" -->
  is not disposable, so a factory- or type-registered inner that holds a process or socket is no longer disposed with the provider; an
  instance registration was never the container's to dispose, so forwarding would dispose what the host owns.
- [ ] **A `Taken` resume repeats the system prompt.** `RetryingAgentSession` resumes with `attempt with { Prompt, ResumeToken }`, keeping <!-- item: state=decision-only needs="a ruling: whether a resume drops SystemPrompt, which needs a measurement of whether each door applies it once per thread or per turn" -->
  `SystemPrompt`, and the Codex door prepends it to the prompt block, so the resumed thread carries the standing instruction twice. Whether the
  Claude door applies it per turn harmlessly is not measured.
- [ ] **Codex's `FinalText` joins every agent message with no separator, and the recorder takes it as the last message.** <!-- item: state=decision-only needs="a ruling: whether FinalText is the last message or all of them, since a test pins the concatenation and the recorder's doc says last" -->
  `AgentTranscriptRecorder` prefers `FinalText`, so interim narration reaches a brief on Codex that Claude's last message does not carry.

## Part 483 — what the 2026-11-08 review of the durable jobs found and did not fix yet

_Opened by `docs/task-archive.md` **Part 483**: a read-only review of `Lyntai.Core/Jobs` and the job stores of every backend, each finding read
against the code before it was filed. Its certain and small findings closed in that Part; what stays is a choice the documents leave open.
Lane `platform`._

- [ ] **A retried or polled job leaves its partition's FIFO.** `JobStoreSql`'s FIFO test counts only earlier Pending jobs already <!-- item: state=decision-only needs="a ruling: whether a retrying or polling head blocks its partition, or the doc says a retry reorders (JobSpec promises strict FIFO)" -->
  available, and a retry or a poll moves the head's `available_at` into the future, so a later job of the partition runs first.
  No contract fact pins a retry inside a partition.
- [ ] **A missing handler fails the job for good.** `JobRunner` claims by lane, not by type, and marks a job whose type it cannot find <!-- item: state=decision-only needs="a ruling: a terminal Failed (pinned by Unknown_type_fails_the_job), or a retry that spends no attempt, for a rolling deploy" -->
  `Failed`, which nothing retries, so an old worker in a mixed fleet can lose a job the new one would run.
- [ ] **An interval schedule drifts by its own lateness.** `JobScheduler.NextAfter` adds the interval to the tick that fired, not to the due <!-- item: state=decision-only needs="a ruling: anchor to the previous slot, or since the last fire (the class doc says advanced past from)" -->
  slot, so a 60 s interval on a 2 s poll fires every 60 to 62 s and the lateness accumulates.
- [ ] **A cancel the handler honoured with a different exception ends in a retry or a dead letter.** After the poll cancels the job's <!-- item: state=decision-only needs="a ruling: whether a cancel request seen plus any thrown exception finalises as Cancelled" -->
  token, any exception that is not an `OperationCanceledException` is a transient retry, and on the last attempt a job the person cancelled is dead-lettered.
- [ ] **A cron that never occurs is accepted at registration.** `JobScheduleRules.Validate` only parses, so `AddCronSchedule` and a stored <!-- item: state=decision-only needs="a ruling: whether Validate refuses an expression with no occurrence, which JobSchedulerTests pins as quarantined at the tick" -->
  schedule take `0 0 30 2 *`; the scheduler now says so once per name, and refusing it earlier changes what a registration accepts.
- [ ] **Removing a stored schedule resets the next run of a build-time schedule of the same name.** `KeyValueJobScheduleStore.RemoveAsync` <!-- item: state=decision-only needs="a ruling: which side owns the next-run and trigger keys when a build-time schedule shadows a stored one (D192)" -->
  deletes the next-run and trigger keys by name, and the two share them, so the live build-time schedule re-anchors and skips the fire that was due. Read from the
  code, not reproduced.

## Part 484 — what the 2026-11-08 review of the file and in-memory storage found and did not fix yet

_Opened by `docs/task-archive.md` **Part 483**: a read-only review of every `Lyntai.Storage.Basic` store outside the memory graph against the
SQLite contract. The certain and small findings closed in that Part. Lane `storage`._

- [ ] **The file backend reissues a removed curated or memory id after a restart; SQLite never does.** The next id is the highest live or <!-- item: state=decision-only needs="a ruling: a persisted high-water mark for these two domains as D174 gave the graph store, or the file backend's reuse stated" -->
  numbered file plus one, so removing the highest frees it, and a stale `UpdateAsync(id, expectedRevision)` then lands on another entry. SQLite's `AUTOINCREMENT` never reissues.
- [ ] **A value holding a lone surrogate is three things.** The file backend refuses it (a body at encoding, a header at the JSON writer), <!-- item: state=decision-only needs="a ruling: refuse a lone surrogate on every backend or replace it on every backend, then one contract fact" -->
  the in-memory one returns it verbatim, SQLite is expected to store a replacement character. A reply cut inside a pair is a plausible source. The file backend no longer leaves its temporary file.
- [ ] **`ListKeysAsync` sorts keys past U+FFFF differently.** The in-memory and file stores order by UTF-16 unit, SQLite by UTF-8 byte, and <!-- item: state=decision-only needs="a ruling: which order the contract's 'ordinal' means, then a fact with a supplementary-plane key (ChatThreads.Page's tiebreak shares it)" -->
  the contract says ordinal (byte).
- [ ] **A vector with a NaN or infinite component is accepted by one backend and refused by another.** `InMemoryVectorStore` scores it NaN, <!-- item: state=decision-only needs="a ruling: refuse a non-finite vector, or score it 0 and rank it last, on every backend" -->
  which also breaks its top-k tiebreak; SQLite is expected to throw at the upsert (not run).
- [ ] **A response cache's `MaxEntries` of 0 or less caches nothing in memory and keeps one on SQLite.** The environment variable rejects <!-- item: state=decision-only needs="a ruling: whether a non-positive cap is refused, means no cache, or means one entry" -->
  it, the property accepts it, and the shared contract leaves the cap out on purpose.

## Part 485 — what the 2026-11-08 review of the Codex door and the process runner found and did not fix yet

_Opened by `docs/task-archive.md` **Part 483**: a read-only review of the Codex exec door and `ProcessRunner`, each finding read against the code before it
was filed. Three closed in that Part. What stays needs a ruling or a measurement against a real `codex`. Lane `inference`._

- [ ] **A `.cmd` launcher re-parses the argv through `cmd.exe`.** `where.exe` prefers `codex.cmd`, the default npm install on Windows, and <!-- item: state=decision-only needs="a ruling: refuse unsafe argv for a .bat or .cmd, escape it for cmd.exe, or resolve the shim to its node target; then a test through a .cmd that prints its argv" -->
  an `ArgumentList` is quoted for `CommandLineToArgvW`, so a `&` or `|` in a `-c mcp_servers.x.url="..."` value or in a resume token runs a second command or truncates the value.
  The spawn-hygiene knowledge states the caveat for `.ps1` only, and `Arguments_with_metacharacters_survive_argumentlist` runs `node.exe`.
- [ ] **A stdio MCP server's `env` goes into Codex's argv.** `CodexMcpConfig` renders it as `-c mcp_servers.x.env={...}`, where **D38** says a <!-- item: state=decision-only needs="a ruling: a pass-through of named variables (codex's env_vars plus the child's environment, as the bearer does) in place of values in argv" -->
  credential never reaches argv and the Claude door writes an owner-only file for that reason.
- [ ] **The MCP bearer's variable name may not be one Codex strips from a shell tool's environment.** `LYNTAI_MCP_BEARER_<NAME>` holds no <!-- item: state=decision-only needs="a ruling: rename to carry TOKEN or SECRET, or document it, after reading codex's shell_environment_policy default (not confirmed here)" -->
  `KEY`, `SECRET` or `TOKEN`, which Codex's default policy is believed to filter on; a model running `env` could read the bearer. The name is pinned in two tests.
- [ ] **A rate limit named only on stderr carries two verdicts.** For a non-zero exit with no `turn.failed`, `CliAgentLoop` takes the verdict from the <!-- item: state=decision-only needs="a ruling: whether a limit or transient reading of stderr stays Fatal as Failure.Kind, or the kind follows the verdict (the pool keys on the kind)" -->
  stderr (`RateLimited`) and `CliFault` maps everything but auth to `Fatal`, so the pool does not bench the account.
- [ ] **A parent that exited while a grandchild holds its stdout may hang `RunAsync`.** The main read loops read with `CancellationToken.None`, <!-- item: state=decision-only needs="a ruling: a bounded drain after the child exits, and a node parent-and-detached-child capture of what KillTree does to an exited parent" -->
  so only a kill unblocks them, and the kill is guarded by `!HasExited`. `RunLocator` has the bound; the run paths do not. Not reproduced.
- [ ] **A stdin producer that throws leaves the child's stdin open, and the fault is dropped.** `WriteStdinLinesAsync` closes stdin on <!-- item: state=decision-only needs="a ruling: whether a producer's fault ends the exchange with that fault or only closes stdin" -->
  `IOException` and cancellation only, and `ObserveStdinAndReapAsync` discards the rest, so the app-server never sees EOF and the exchange reads as a timeout.
- [ ] **The two-minute inactivity window kills a long silent Codex step.** `--json` carries no heartbeat between `item.started` and <!-- item: state=decision-only needs="a ruling: a longer default window for the agent doors, or a documented warning, and a measurement of how long a quiet step runs" -->
  `item.completed`, so a `dotnet test` over two minutes is a `Timeout`, `Transient`, `Delivery Uncertain`, and its step is lost.

## Part 486 — what the 2026-11-08 review of the front door, the router, the cache and the budget found and did not fix yet

_Opened by `docs/task-archive.md` **Part 484**: a read-only review of `Lyntai.Core/Inference` outside the agent layer, each finding read against the code before it was
filed and none reproduced. The certain and small findings closed in that Part. Lane `inference`._

- [ ] **A cache hit replays the original `Usage`.** The reply is stored whole, so `ToolLoop`, `ChatOrchestrator` and a trace step for the hit each count the first call's tokens and cost again, <!-- item: state=decision-only needs="a ruling: zero or strip Usage on a hit, or keep it and mark the reply as a hit so a caller can tell" -->
  while the budget, which sits inside the cache, is right. `A_cache_hit_is_traced` counts steps only.
- [ ] **A stream's billed usage is dropped when the router turns an empty `Final` into a failure.** The `Final` becomes an error before `usage = chunk.Usage` is read, so the next candidate is billed <!-- item: state=decision-only needs="a ruling: carry the dropped usage onto the failure, or state that a failed stream is unmetered as the non-streaming path's failed reply is not" -->
  and the budget sees one of the two. `TextRouterStreamTests` pins the fall-over, not the accounting.

## Part 487 — what the 2026-11-08 review of the secrets, the MCP bridge and the scorers found and did not fix yet

_Opened by `docs/task-archive.md` **Part 484**: a read-only review of `Lyntai.Core/Secrets`, `Lyntai.Secrets.Dpapi`, `Lyntai.Tools.Mcp`, `Lyntai.Core/Cortex`, `Prompts`,
`Diagnostics` and `Text`. The certain and small findings closed in that Part. Lane `tools`._

- [ ] **A blob resource and an image or audio block in an MCP tool result are described, never read.** `McpToolset.ToText` renders them as `[image content]` or `[resource content]`, so a tool <!-- item: state=decision-only needs="a ruling: pass the block's media type and size, or its bytes, or a link a host resolves, to the model; and whether a text-only model is told it cannot be shown one" -->
  whose answer is a picture or a binary file gives the model a placeholder. The text-resource and link half closed in `docs/task-archive.md` Part 486.
- [ ] **The hosted MCP accept loop ends on any exception that is not a shutdown.** `McpToolHost.AcceptLoopAsync`'s catch is filtered on cancellation and a closed listener, so an <!-- item: state=decision-only needs="a ruling: log and keep accepting on a transient listener fault, or stop and surface it to the session" -->
  `HttpListenerException` from a client reset faults the task unseen and the CLI's client waits on connect. Environmental; not reproduced.
- [ ] **A judge answering on the wrong scale reads as a perfect score.** `LlmScorerBase` clamps `score` to `[0, 1]`, so `{"score": 7}` from a 0 to 10 judge is `1.0`, and `1e999` is a <!-- item: state=decision-only needs="a ruling: clamp (today), refuse the dimension as unscored, or rescale; and whether an overflowing number is caught inside ScoreAsync" -->
  `FormatException` whose handling was not read.
- [ ] **`JsonExtract.ExtractObject` commits to the first `{`.** `see {score}: {"score":0.8}` extracts `{score}`, fails to parse and returns no object, so a scorer reads no verdict although <!-- item: state=decision-only needs="a ruling: retry later braces, with a bound on how many, or document the first-brace rule" -->
  a valid one follows.
- [ ] **A secret's ciphertext is not bound to its name.** `KeyValueSecretVault` and `AesGcmSecretProtector` pass no associated data, so anyone who can write the key-value store can copy the blob at <!-- item: state=decision-only needs="a ruling on the threat model: bind the name as associated data (a stored-format change that needs a migration), or state that store write access is trusted" -->
  one name over another and it decrypts under the second, where the access policy gates by name.
- [ ] **Two processes over one store can each generate a master key.** `GenerateMasterKeyAsync` checks for an envelope and then sets one under an in-process lock; <!-- item: state=decision-only needs="a ruling: a compare-and-set on IKeyValueStore (an interface change) or a documented single-process rule" -->
  the second envelope overwrites the first and strands the first process's secrets.

## Part 488 — what the 2026-11-08 review of the SQLite and Postgres adapters found and did not fix yet

_Opened by `docs/task-archive.md` **Part 484**: a read-only review of the key-value, conversation, score, trace, prompt, job, vector, cache and usage stores of both SQL adapters against each other and
the in-memory reference, outside the memory graph. None was run: where a row rests on an engine's behaviour it says so. Lane `storage`._

- [ ] **A trace step's `seq` is `INTEGER` on Postgres and 64-bit on SQLite.** `Sequence` is a `long`; a value past 2^31 is stored by one and refused by the other. <!-- item: state=decision-only needs="a ruling: widen the column with a migration, which rewrites and locks the trace-step table for every deployment to serve a value nobody has written, or refuse a sequence past int.MaxValue on every backend, which narrows what SQLite stores today" -->
  A shipped migration is not edited, so the first is a new Postgres-only one (which moves the per-backend migration counts `.agents/knowledge/storage.md` §Migrations keeps); low impact.
- [ ] **SQLite's fallback `LIKE` folds ASCII case only.** A recall of `Да` or `ÉC` under three characters skips FTS and finds nothing for `да` or `école`, where the in-memory stores and <!-- item: state=decision-only needs="a ruling: a Unicode-aware LIKE or lower on the connection, an in-process scan, or ASCII-only stated; then a MemoryStoreContract fact with a non-ASCII query" -->
  Postgres find it.
- [ ] **Postgres retries a conversation append four times, so eight concurrent appenders to one thread can throw `23505`.** SQLite and the in-memory store never throw. <!-- item: state=decision-only needs="a ruling: a per-thread advisory lock, or an unbounded retry bounded by the token; then a concurrent-append fact on both backends" -->
- [ ] **An empty vector is refused on Postgres and accepted elsewhere, and a NUL in any text is refused on Postgres and stored elsewhere.** pgvector is believed to need one dimension at least <!-- item: state=decision-only needs="a ruling for each: refuse an empty vector on every backend or score it 0, and strip, replace or refuse U+0000 (the same family as the open lone-surrogate row)" -->
  and `text` cannot hold `\0`, so `RecallAsync` silently returns `[]` for a query holding one. Neither was run.

## Part 489 — what the 2026-11-08 review of the HTTP and Ollama providers found and did not fix yet

_Opened by `docs/task-archive.md` **Part 488**: a read-only review of `Lyntai.Providers.Basic/Http` and `Ollama`, each finding read against the code and none run against a server.
The certain and small findings closed in that Part; a row that rests on a vendor's behaviour says so. Lane `inference`._

- [ ] **An OpenAI `refusal` is retried and reported `Failed`.** `OpenAiChatWire` never reads `message.refusal` or `delta.refusal`, so a structured-output refusal (`content` null, `refusal` set) is an empty <!-- item: state=decision-only needs="a ruling: map a non-empty refusal to Refused (no fallback, no dead-host strike, as content_filter is), or leave it Failed; and whether the streaming wire does the same" -->
  reply, the engine resends the identical request once (a second billed call), then answers `Failed`, which advances and counts toward the dead-host threshold. Rests on OpenAI's documented field; not run.
- [ ] **A stream that errors after content, or never reaches its terminator, is a clean truncated reply.** The in-band error is read only when no content streamed and no terminator (`[DONE]`, `done:true`, a finish <!-- item: state=decision-only needs="a ruling: end Error(classified) when content streamed, an error line was seen and no terminator arrived, or keep the recovered-error rule as written" -->
  reason) is tracked, so Ollama's `{"error":…}` line after content, then EOF, yields `Final`. The recovered "Reconnecting… 2/5" case (pinned) ends in `[DONE]` and would be kept.
- [ ] **`max_tokens` and a non-default `temperature` are sent to every OpenAI-shaped host.** OpenAI's reasoning models are believed to refuse both (`max_completion_tokens` instead), a 400 read as `Failed`; <!-- item: state=decision-only needs="a ruling: a MaxTokensField option, a per-model rule, or documented as the host's to configure; the vendor's refusal is believed from its documentation, not run" -->
  `SuppressReasoningFields` refuses these two keys, so there is no workaround today.
- [ ] **A negative usage count is accepted.** `WireJson.Long` rejects a non-number or an out-of-range one, not `-1`, so `InMemoryUsageTracker` adds it and a recorded spend goes down. <!-- item: state=decision-only needs="a ruling: clamp a negative count to 0, or read it as absent, on every wire (HTTP, Ollama, the agent doors)" -->
- [ ] **A non-finite request number escapes the provider as an exception.** `Temperature = double.NaN` makes `payload.ToJsonString()` throw `ArgumentException` out of `CompleteAsync`, although `HttpJsonCall` states <!-- item: state=decision-only needs="a ruling: validate a request's numbers up front (throw to the caller) or answer a Failed or Unsupported verdict; a scheme-only BaseUrl such as host:port is the same shape and its exception type was not run" -->
  that a failure is a verdict, never a throw.
- [ ] **A server that ignores `stream:true` and answers one JSON body is dropped.** The stream path reads `choices[0].delta` per line and never `message`, so a proxy's single `chat.completion` ends <!-- item: state=decision-only needs="a ruling: fall back to buffered extraction when the first payload carries message and no delta, or leave a non-streaming proxy as unsupported" -->
  `Failed` "no output produced".
- [ ] **A base URL ending in a version other than `/v1` gets `/v1` appended.** `…/v1beta/openai` and `…/api/paas/v4` compose to a 404. <!-- item: state=decision-only needs="a ruling: a flag to use the base verbatim, a pattern for any trailing version segment, or documented as /v1-only" -->

## Part 490 — what the 2026-11-08 review of the local inference backends found and did not fix yet

_Opened by `docs/task-archive.md` **Part 488**: a read-only review of `Lyntai.Providers.Onnx`, `LlamaSharp`, `Model2Vec`, `Text`, `Diagnostics` and `Prompts`. The certain and small findings closed in
that Part (the `JsonExtract` first-brace row was already filed in Part 487). A row resting on a reference implementation says so. Lane `inference`._

- [ ] **An export whose pooling or module list this reader does not support is mean-pooled silently.** `SentenceTransformerConfig.PoolingFrom` takes CLS only when it is set alone and everything else, <!-- item: state=decision-only needs="a ruling: refuse (InvalidDataException) a pooling_mode other than cls or mean and a modules.json entry other than Transformer, Pooling or Normalize, log it and carry on, or implement last-token and the Dense module; an explicit OnnxProviderOptions.Pooling would bypass the check" -->
  including `pooling_mode_lasttoken`, `max_tokens` and a following `Dense` module, reads `Mean` — finite vectors that rank wrongly. Its class comment says guessing is the failure it exists to avoid, and
  it also says a hand-assembled directory is not worth refusing to load, which the ruling has to reconcile.
- [ ] **`Model2VecProvider` embeds and counts an unknown token.** `EncodeToIds` emits `[UNK]`, which `Embed` averages in; the reference `model2vec` is believed to drop it before pooling (read <!-- item: state=decision-only needs="a ruling: skip the unknown id, which changes every vector already stored from this provider, or keep it as the table was built against; the upstream behaviour is to be read, not assumed" -->
  from the upstream source by the review, not here). An all-unknown text would then be the zero vector.
- [ ] **A `vocab.txt` longer than the table counts tokens that add nothing.** `AccumulateInto` ignores an id past `Rows` by design but `Embed` still divides by it; nothing compares the two at load. <!-- item: state=decision-only needs="a ruling: refuse a vocabulary longer than the table at load, log it, or count only the rows read" -->
- [ ] **A RoBERTa-family export that declares no narrower limit keeps 514.** `WindowFrom` narrows only to a declared `model_max_length` or `max_seq_length`, so an XLM-R, MPNet or <!-- item: state=decision-only needs="a ruling: subtract the two padding positions when config.json's model_type is roberta, xlm-roberta, camembert or mpnet, or require the limit to be declared" -->
  CamemBERT export with neither file takes `max_position_embeddings` (514) as its token limit and a 514-token row indexes past the position table.
- [ ] **A non-segmented call is one forward pass whatever its size.** 6,000 rows of 512 tokens at hidden size 768 is a 9.4 GB output copied by `ToArray()`, and `checked(i * rowSize)` overflows at row 5,461. <!-- item: state=decision-only needs="a ruling: keep An_unsegmented_call_is_ONE_pass_however_many_inputs_it_holds and document a caller cap, or cap the pass by a count or a memory budget; reading the tensor's buffer without ToArray is not in question" -->
- [ ] **A segmented text's vector is always unit length, a one-window text's is not when `Normalize` is false.** `VectorMath.WeightedMeanDirection` normalises, so a long and a short text differ <!-- item: state=decision-only needs="a ruling: document it as direction-only (cosine is unaffected) or return the weighted mean of the raw rows when normalize is false, for dot-product and L2 consumers" -->
  in scale under a model with no `Normalize` module.
- [ ] **`LlamaSharpProvider` leaks the weights when the executor fails to build, and `Dispose` ignores a generation in flight.** `EnsureLoaded` assigns `_weights` before `new StatelessExecutor`, so a throw (a `ContextSize` <!-- item: state=startable -->
  past memory) leaves them loaded and the next call loads a second copy over the field; `Dispose` frees the executor under a running `InferAsync` and the stream's `finally` releases a disposed semaphore.
  Fix: build into locals and dispose on a throw; `Dispose` waits on the gate and `StreamAsync` checks `_disposed` once it holds it. **The test needs a GGUF model and a CPU backend package the test project does not reference** (a named, obtainable download, as `docs/task-archive.md` Part 478 added one for a run), so it was not written here.

## Part 491 — what the 2026-11-08 review of the generation platform found and did not fix yet

_Opened by `docs/task-archive.md` **Part 488**: a read-only review of `Lyntai.Generation` and `Lyntai.Core/Generation`, each finding read against the code, one (the deadline's range) run. The certain and small findings closed in that
Part. Lane `generation`._

- [ ] **A transient fetch failure fails a durable job for good.** `GenerationJobEngine` turns any non-Ok `FetchAsync`, a `Timeout` or a 429 included, into `JobOutcome.Fail`, while `QueueCalls.FetchGuard` says the <!-- item: state=decision-only needs="a ruling: which verdicts are transient and whether a transient fetch polls again (bounded or not) or retries a set number of times, against the failing it today (only the 422 case is pinned)" -->
  operation can be fetched again and the checkpoint already holds its id: a paid render is lost to a retryable read.
- [ ] **A ComfyUI run lost to a server restart polls for ever.** Its queue and history are in memory, so `/history/{id}` answers `{}` after a restart, which reads as not landed yet, and a `Poll` outcome has <!-- item: state=decision-only needs="a ruling: consult /queue and fail or call it inconclusive when the id is in neither, or a maximum poll duration on GenerationPipelineJobOptions, and how many confirming polls before a run is declared lost" -->
  no deadline, so a durable job meant to survive restarts stays `Running`.
- [ ] **Cancelling a durable generation job does not cancel the backend render.** The engine never calls `IMediaJobProvider.CancelAsync`, so a paid fal render or a ComfyUI GPU run goes on unattended. <!-- item: state=decision-only needs="a ruling: a best-effort CancelAsync when a cancel is seen with an operation id checkpointed, or leave it to the host and say so; ComfyUI's interrupt is server-wide and can kill another tenant's run" -->
- [ ] **`generate_fetch` bills and delivers again on every call, and the job engine bills before it checkpoints.** A model that calls the tool twice records the cost and sends the artifacts twice, and a lease lost between <!-- item: state=decision-only needs="a ruling: whether the spend ledger or the tool owns de-duplication per (backend, operation id); both over-count, so they fail closed" -->
  the bill and the checkpoint is billed again by the worker that reclaims it.
- [ ] **`generate_backends` lists and probes every registered `IModelProvider`.** Text, score and vector providers appear to the model as generation backends with `usable: true`, and each is probed, which can spawn <!-- item: state=decision-only needs="a ruling: filter to providers that produce image, video, audio or 3d, or keep the whole roster as written; the 'lists every backend' test does not tell the two apart" -->
  a CLI or reach a network.
- [ ] **A ComfyUI output walk includes `type: "temp"` previews.** A workflow with both SaveImage and PreviewImage returns two artifacts, so a pipeline stage's "exactly one chains forward" rule refuses it. <!-- item: state=decision-only needs="a ruling: drop temp outputs by default with an option to include them, or keep every output the history lists" -->

## Part 492 — what the 2026-11-08 review of the memory engine core found and did not fix yet

_Opened by `docs/task-archive.md` **Part 489**: a read-only review of `Lyntai.Core/Memory` outside the memory-currency layer, each finding read against the code and none run. The certain and small findings closed in that
Part (an abstention signal lost when a judge emptied the page, a seed source's failing fetch, `Buried` counting history). Lane `memory`._

- [ ] **A blend swallows the caller-mistake exceptions the graph engine throws on purpose.** `GraphMemoryEngine` throws `KeyNotFoundException` for an unknown `RankingPolicyName` and `ArgumentException` for an <!-- item: state=decision-only needs="a ruling: which exception types a blend lets through as a caller's mistake (docs/memory.md says an unknown name throws), then a composite fact for each" -->
  incoherent `Validity`, "never fail-open"; `CompositeMemoryEngine.RecallAsync` catches every non-cancel exception from a member and logs it, so through the blend (the shape every DI-registered engine has) a typo'd ranking name is an empty recall. `MemoryWalk` does the same.
- [ ] **`PruneAsync` with a floor above 1 removes everything or nothing, by age policy.** The exact path (any derivable policy) removes every non-authoritative, non-ended entry; the in-store path asks for a cutoff <!-- item: state=decision-only needs="a ruling: refuse a floor above 1, clamp it to 1, or define the floor as inclusive; then the same fact on both paths" -->
  that `DsrRetrievability` makes `+Infinity` and removes none. `GraphMemoryOptions.MinRetrievability` accepts any finite number.
- [ ] **Entries cut by `CharBudget` are still reinforced and logged.** `RecallCoreAsync` reinforces `scored` before `ToItems` applies the budget, where the contract says "what a recall actually returned"; a blend's members <!-- item: state=decision-only needs="a ruling: reinforce after the cut, or document that learning follows the page and not the budget" -->
  each reinforce their own page before the blend's limit and budget cut.
- [ ] **`PruneAsync(olderThan:)` has no range handling.** A negative span puts the cutoff in the future and removes every non-authoritative entry; `TimeSpan.MaxValue` throws. <!-- item: state=decision-only needs="a ruling: refuse a non-positive span, treat it as no age criterion, or clamp, as Keyed does for receipts" -->
- [ ] **`RememberAsync` on the graph engine does not validate content.** A null content is a `NullReferenceException` in `MemoryLine.Flatten`, a blank one stores an entry with an empty headline, <!-- item: state=decision-only needs="a ruling: refuse or skip a blank write, the same on the graph, semantic and write-tool paths" -->
  where `SemanticMemory` skips it with `Ran = None` and `MemoryWriteTools` rejects it.
- [ ] **A character budget of 0 means unbounded for a recall and the root alone for an expansion.** `GraphMemoryEngine` ignores `budget > 0` in `ToItems` and passes 0 to `MemoryBudget.Cut` in <!-- item: state=decision-only needs="a ruling: one meaning for 0 on both, and the validation message for ExpandCharBudget to match" -->
  an expansion; the option's validation message reads "zero means none".
- [ ] **Portable import validates `stability` and no other number.** `age`, `ordinalAge`, `volumeAge`, `elapsedAge` and a link `weight` are read finite-only, so a hand-edited negative age gives <!-- item: state=decision-only needs="a ruling: refuse a negative age and weight, or keep them because elapsedAge can legitimately go negative under clock skew" -->
  retrievability 1 and the entry never decays.
- [ ] **A `History` recall writes co-activation edges that include ended entries.** `GraphMemoryEngine` takes `CoActivationCap` nodes before the `StatesEnd` filter, which guards only the touches, so the edge <!-- item: state=decision-only needs="a ruling: filter ended entries before the pairing, or state that history links what it read" -->
  adds `Strength` to both ends where `docs/memory.md` says a history recall does not reinforce an ended entry.
- [ ] **A large co-activation set computes its pair count as an `int`.** `top.Count * (top.Count - 1) / 2` overflows past 46,341 nodes, a negative list capacity throws, and the surrounding catch drops that recall's touches and <!-- item: state=decision-only needs="a ruling: bound CoActivationCap, or compute in long and cap the pairs, since the pair count is quadratic either way" -->
  review log with a warning.

## Part 493 — what the 2026-11-08 review of the memory graph and vector stores found and did not fix yet

_Opened by `docs/task-archive.md` **Part 489**: a read-only review of the graph stores of `Lyntai.Storage.Sqlite`, `.Postgres` and `.Basic`, read against each other and against the contract, none run. The certain and small findings
closed in that Part (SQLite's delete and neighbour reads past the variable limit, the subject replace's transaction, a portable age past the range). Lane `storage`._

- [ ] **`SearchTerms` counts UTF-16 units and the trigram index counts code points.** A spaced term of two emoji passes the 3-unit floor, SQLite's FTS finds nothing for a phrase under a trigram, and Postgres and the <!-- item: state=decision-only needs="a ruling: measure terms in code points so every backend drops the sub-trigram term, which makes Postgres and the in-memory stores stop matching it" -->
  in-memory stores find it.
- [ ] **A vector collection that was emptied stays stamped with its old space.** `VectorSpaceStore.DeleteAsync` never releases the stamp, so after a prune empties a scope and the declared space changes, the next write <!-- item: state=decision-only needs="a ruling: treat a stamped but empty collection as unclaimed, or release the stamp when the last vector goes" -->
  throws the mismatch, and the engine's index step swallows it, so every entry of that scope is stored with no vector.
- [ ] **A signal bag of only non-finite values replaces the stored one in-process and keeps it in SQL.** `MemorySignalsJson.Serialize` drops non-finite values and returns null, which the SQL stores read as no opinion; <!-- item: state=decision-only needs="a ruling: whether an all-non-finite bag counts as no opinion, which is what SQL does, then the contract fact on a re-upsert" -->
  `MemoryGraphState.Refresh` tests only `Count == 0`.
- [ ] **A keyed link across engines writes the edge and a receipt in the SQL stores.** `LinkKeyedAsync` says an endpoint the engine does not hold writes nothing and leaves no receipt; `MemoryGraphSql.StrengthenEdge` <!-- item: state=decision-only needs="a ruling: add the engine predicate to StrengthenEdge and PlanLink, which changes the unkeyed LinkAsync too" -->
  checks the ids exist in any engine, so SQLite and Postgres write the edge and an `Applied` receipt of revision 0, and the in-memory store refuses.

## Part 494 — what the 2026-11-08 review of the tool loop, the guards and the composition root found and did not fix yet

_Opened by `docs/task-archive.md` **Part 489**: a read-only review of the chat and tool side of `Lyntai.Core/Agents`, the guards, the cortex and `AddLyntai`'s wiring, each finding read against the code. The certain and small findings
closed in that Part (an approver seeing the model's spelling of a name, a null tool result, `UseVectorSpace`'s keyed and scoped stores, a JSON retry's usage, a curated kind in a heading). Lane `platform`._

- [ ] **Streamed text is never output-gated.** `GuardedTextClient.StreamAsync` gates the request only, as its class comment says, and `ToolLoop.StreamAsync` emits `TextDelta` as it arrives, so a streaming host <!-- item: state=decision-only needs="a ruling: buffer and gate before emitting, expose a stream gate the host supplies, or keep the documented limit and say so in docs/guide.md" -->
  with a `DenylistGuard` has no output gate at all.
- [ ] **A guard block inside the tool loop reads `Blocked: false` on the chat result.** `ChatOrchestrator` answers `Verdict: Refused` with the guard's reason in `Detail` and `Blocked` false, where `ChatResult.Blocked` is <!-- item: state=decision-only needs="a ruling: set Blocked for a block inside the loop, or narrow the property's documentation to the input and output gates" -->
  documented as a guard having blocked it.
- [ ] **`AddVectorToolSelector` with no embedding backend fails quietly on every run.** `AddSemanticMemory` throws at composition for the same case; the selector throws `NothingEmbeds` once the roster passes its limit, <!-- item: state=decision-only needs="a ruling: whether AddLyntai refuses the registration as AddSemanticMemory does, or keeps the warning and the full roster" -->
  `NarrowAsync` logs a warning and shows every tool, and nothing says so at startup.
- [ ] **`ToolLoop` bounds its iterations and not the calls in one turn.** A turn returning N calls runs all N and their observations grow the message list with no cap; an exception out of a turn leaves the `tool_loop` <!-- item: state=decision-only needs="a ruling: a per-turn call cap on the options, and whether a turn that throws still closes its span with a status" -->
  span without a status, because `Finish` is the only place that ends it.

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
