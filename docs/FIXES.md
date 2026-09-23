# Fix log

Per-incident records: **symptom → root cause → fix → verification**, plus the commit that introduced the
bug. Newest first. The other two homes, so nothing has to be guessed (`.claude/rules/repo-mechanics.md`
§Fix log): a decision and its reasoning goes to `docs/DECISIONS.md`; the reusable trap a fix revealed goes
to `.claude/knowledge/pitfalls.md`; the release-facing line goes to `CHANGELOG.md`.

---

## 2026-09-24 — the file graph store reported a durable write as failed when its compaction was refused

**Symptom.** None shipped; found by the whole-branch review of `docs/task-archive.md` Part 282. When the
journal's snapshot rewrite or the review log's trim could not write (a rename held by an indexer or antivirus),
`UpsertAsync`, `TouchAsync`, `LinkManyAsync`, `WriteBackAsync` and `RecordSubjectsAsync` threw after their
change was already appended and applied — a stored memory reported as failed, a write-back's reviews skipped, a
retried touch counted twice — and every later write threw the same way, because nothing backed the retry off.

**Root cause.** `GraphJournal.Rewrite` let the file system's exception escape, and both of its callers run it
after the change is durable. Separately, `FileSystemRoot.Append` left whatever a failed write had put down, so
the next append joined onto it and the merged line became unreadable.

**Fix.** A rewrite the file system refuses (`IOException`, `UnauthorizedAccessException`) is logged and returns
false, backing off as the unreadable-line refusal does — retried once the journal doubles again. An append
that throws cuts the file back to its length before the write, best-effort, and rethrows the original failure.

**Verify.** Two `FileSystemGraphRestartTests` facts put a directory where the rewrite's temporary file goes —
one for compaction, one for the trim; both threw `UnauthorizedAccessException` before the fix and pass after,
and removing the back-off fails the first. The append's cut has no test: a partial write needs the file system
to accept some bytes and then fail, which no in-process obstacle produces deterministically.

**Introduced by.** `618d9198` (the journal), `e3c7b116` (the store).

## 2026-09-23 — the pre-release review: a person's broken file written over, and a line break D166 missed

**Symptom.** None shipped; found by reading `v3.2.0..HEAD -- src/` before the release (`docs/task-archive.md` Part 279).
In the file-system package: a hand-edited prompt revision, thread file or key file that failed to parse was
rewritten by the next save of the same name; a deleted key came back after a restart when a hand-made copy
also held it; a hand-renamed curated file was duplicated on update and resurrected after a remove; and a
recall whose access time could not be written returned no memories at all. In Core: `MemoryLine` let `\v`
and U+001C–U+001E through, and the judge's `ContentChars` rendered an EMPTY content as an empty note.

**Root cause.** `FileSystemRoot`'s promise — a skipped file is never written over — was honoured only where a
name is a NUMBER (`MaxId`); every name derived from a key had no equivalent, and the curated store addressed
files by id rather than by where it loaded them. The access-time write sat inside the recall's fail-open
`catch`, so a failure to record a touch discarded the hits. `MemoryLine`'s doc asserted `ReplaceLineEndings`
folds `\v`, which it does not; the judge's fallback tested `Content` for null, not for nothing.

**Fix.** Prompt revisions number past every `v####.md` on disk (`MaxId` takes a prefix). A new thread or key
whose own name is already on disk is REFUSED with the path named (**D171**'s new paragraph says why). A key
remembers every file that held it and a delete removes them all; the curated store writes back to the file it
loaded. A failed touch is logged and skipped. `MemoryLine` folds the four extra separators and
`MemoryHeadline.Derive` calls it; the judge falls back on null-or-whitespace content.

**Verify.** Six `FileSystemRestartTests` facts (one per door), `MemoryLineTests` (eleven characters plus the
forged heading), and a two-case judge theory — all 13 failed before the fix and pass after; 190 tests across
the touched areas pass.

**Introduced by.** `500c4a74` (the package), `a7305589` (D166's doc claim), `0ce6d8ea` (D170's fallback).

## 2026-09-23 — the next release's notes led with a Breaking section the CHANGELOG did not have

**Symptom.** Previewing the notes for the release after 3.2.0 (`release-notes --tag`): *"Breaking changes —
This release requires changes to consuming code. See the **Breaking** section of `CHANGELOG.md`"* above
`fix(memory)!:`, while the CHANGELOG files that change under `### Security` with "nothing at a call site"
and has no Breaking section to see. A gate repair, `fix(gates):`, was listed under Fixes.

**Root cause.** Two. The generator read breaking-ness from the subject's `!` alone, and that commit is
pushed, so the marker can never be corrected — while the entry is what **D161** classifies and review
reads. And `NON_SHIPPING_SCOPES` was a list from 2026-08-23 that `gates` post-dates; a census of every
`feat`/`fix` scope in history found `measure`, `archive`, `e2e` and `playground` in the same position.

**Fix.** `changelogDeclaresBreaking` reads the version's stamped section (or `## Unreleased` for an untagged
preview, which the stamp renames); where it declares nothing Breaking, a `!` commit is listed by its kind
and printed to stderr as a disagreement, and where it declares a break no subject marked, the section is
rendered from the CHANGELOG alone. The five scopes joined the list, and a compound scope is dropped only
when every part ships nothing. The ruling itself is recorded in **D161**.

**Verify.** Seven new cases in `release-notes.test.mjs` (27 pass), including the real subject shape both
ways and a Breaking heading in an OLDER section that must not bleed into Unreleased. The preview now lists
the D166 commit under Fixes, drops the gate repair, and names the demotion on stderr.

**Introduced by.** Not one commit: the `!` rule is from 2026-08-16's extraction, and it was right while no
`!` commit had ever been misfiled. `a7305589` was the first.

## 2026-09-23 — a Claude agent session announced "session started" once per thinking tick

**Symptom.** An adopter persisting `IAgentSession`'s event stream stored one `SessionStarted` per turn plus
one per thinking update — invisible in any UI, and a row per tick in its chat history. It shipped its own
one-announcement guard around the stream to cope.

**Root cause.** `StreamJsonAgentReader.ReadSystem` yielded `SessionStarted` for ANY `system` line carrying a
`session_id`. Measured against `claude` 2.1.280 (`-p --output-format stream-json --verbose`): a turn emits
`system/init`, then `system/thinking_tokens` PROGRESS events (`estimated_tokens`, `estimated_tokens_delta`,
`session_id`), and a `rate_limit_event` before `result`. Every progress event was read as a session start.

**Fix.** The reader remembers the id it announced and yields `SessionStarted` only for a new or changed one.
Keyed on the id rather than on `subtype == "init"`, because not every emitter sets the subtype — two
existing session tests feed a bare `system` line — and an id-keyed rule still reports a real change. The
`rate_limit_event` arm stays "yield nothing", now with a comment saying that surfacing it is a public-surface
decision rather than an oversight.

**Verify.** `StreamJsonAgentReaderTests.Progress_system_events_do_not_restart_the_session` feeds `init` plus
two `thinking_tokens` lines and asserts ONE `SessionStarted`; it failed with three before the fix.
`A_changed_session_id_is_announced_again` pins the other half. The adopter's own end-to-end check counts
announcements through a stub emitting that exact sequence, so its guard becomes deletable once this ships.

**Introduced by.** `3b89a17d` (2026-07-19), which wrote the reader against streams whose only `system` line
was `init` — every fixture its tests fed was one.

## 2026-09-21 — every sweep's single-text embed crashed on a cast the D153 refactor left stale

**Symptom.** The first sweep run since 2026-09-18 dies immediately:
`InvalidCastException: Unable to cast object of type 'CachingVectorProvider' to type
'Lyntai.Inference.IVectorProvider'` — from `BenchVectors.EmbedAsync`, reached by `memory-longmemeval`'s
new `--corroborate` mode, and equally reachable from every existing single-text embed site in
`memory-locomo`, `memory-decision` and the longmemeval cosine controls. The bench BUILDS clean.

**Root cause.** **D153** step 4 (`5c45c84c`) moved `EmbedAsync` off `IModelProvider`; the bench's bridge
extension was rewritten to cast to the new `IVectorProvider` — but the two bench doubles it is always
called on (`SweepDoubles.OpenAiCompatibleVectorProvider`, `SweepDoubles.CachingVectorProvider`) still
declared bare `IModelProvider`. An extension-method cast defers the break to runtime, the compiler stayed
green, and no sweep is in `verify` because sweeps need a live model — so the break sat invisible until the
next measurement run, three days later.

**Fix.** Both doubles now implement `IVectorProvider`: `CallAsync(VectorRequest)` wraps the existing batch
`EmbedAsync`, throwing on a dead server rather than mapping a verdict — a sweep wants a crashed run, not a
silently degraded figure, the same posture `TryRealVectorProviderAsync` takes about fakes. The reusable
half is in `.claude/knowledge/pitfalls.md` §Refactoring: **when a refactor touches a seam the instruments
double, run ONE sweep before recording it done.**

**Verify.** The failing call is the proof both ways: `memory-longmemeval --corroborate` crashed with the
cast before the fix and completed on both corpus variants after it (oracle 70/70, haystack 70/70 at
34,242 turns per arm), through the same `BenchVectors.EmbedAsync` path the other sweeps use.

**Introduced by.** `5c45c84c` (D153 step 4, 2026-09-18) — the refactor updated the bridge's cast but not
the doubles the bridge is cast ON, and every recorded sweep figure predates it.

## 2026-09-20 — `verify` reported the guards themselves failing on a tree where all 871 passed

**Symptom.** `verify` dies at its FIRST gate: *"test-devtools: ✗ the scripts that GATE this repository are
themselves failing"* — while the runner output printed directly above it reads `tests 871 / pass 871 /
fail 0`. Reproducible on unmodified `master`, and the spawned runner exits `0` when run by hand, so nothing
about the tree or the tests was wrong. The message names no failing test because there is none.

**Root cause.** `test-devtools` reads the runner's SUMMARY rather than trusting its exit code alone —
deliberately, since these scripts fail permissively. It parsed the counts with `^ℹ <name> (\d+)`,
anchored at line start. Node's spec reporter writes a colour escape BEFORE that `ℹ`, and it honours
`FORCE_COLOR` through a PIPE, which is exactly how the gate spawns it. So under any environment that sets
`FORCE_COLOR` — a terminal that exports it, and Claude Code sets `FORCE_COLOR=3` — every count parsed as
`NaN`, `tests > 0` was false for `NaN`, and the gate reported red. Not a flake and not the tests: a
false RED, which is the safe direction to fail in and still blocks the only gate that says "am I done".

**Fix.** The parse is an exported `testSummaryCounts(out)` — ANSI stripped before matching — so it can be
driven by a test, the same reason the doctors left this switch (`docs/task-archive.md` Part 62). Missing
counts stay `NaN` rather than collapsing to a plausible `0`, and the failure message now says when the
summary did not PARSE, naming the exit code and the `--test-reporter=spec` re-run, instead of implying a
counted test failure. The reusable half: **a guard that parses a tool's human-readable output is hostage to
that tool's colour decisions, and a pipe does not turn colour off.**

**Verify.** Driven red first — the new test imports `testSummaryCounts` before it exists, then asserts the
counts survive a coloured summary (the measured defect) and that an unparseable one yields `NaN` rather
than zeroes. Then measured green the way the defect actually arrives: `node devtools/dev.mjs test-devtools`
**with `FORCE_COLOR=3` still set** now reports `873/873` and exits 0, where it previously exited 1.
Baseline moves 871 → 873 for the two tests added; `check-counts` caught that itself.

**Introduced by.** The gate as originally written — the anchored parse has been there since `test-devtools`
gained its quiet-when-green shape. It was invisible for as long as nobody ran `verify` from a
colour-forcing environment, which is why it surfaced now rather than at the change that caused it.

## 2026-09-19 — the release pipeline failed `verify` on a tree that was green minutes earlier

**Symptom.** Every gate green locally at `d1219c2d`, `consumer-smoke` green, `doctor` green — and the
release run failed at `check-samples`: *"CLAUDE.md says 'doc samples 61/61' — this run compiled 60"*. Every
other gate in that run reported numbers identical to the local one (12 decision claims, 61 commands, 174
options, 3983 baseline lines, 840 prose files), so it was the same tree, same code, one sample short.

**Root cause.** The release workflow stamps `## Unreleased` with a version BEFORE it runs `verify`.
`CHANGELOG.md` is historical except for that prefix, so the D157 entry's fenced `AddOnnxProvider` sample
was compiled on every ordinary run and historical the instant the stamp landed — census 61 → 60, while the
CLAUDE.md baseline the gate checks itself against still said 61. **A live PREFIX is transient by
construction**, which the D164 mask work (same day) wired check-samples onto without noticing that one
region it admits is scheduled to disappear. Not a flake and not environmental: reproducible by replaying
the stamp through the gate's own `read` seam.

**Fix.** `check-samples` now REFUSES a compiled sample in any `LIVE_PREFIX` region, naming the stamp and
saying what to do instead — keyed on the registry rather than on a filename, because an amendment region
(the design record) is permanent and unaffected. The changelog entry keeps its prose and points at
`.claude/knowledge/extending-lyntai.md`, where the identical recipe was already compiled — so nothing went
unchecked; the duplicate went. Census is now 60 before and after the stamp. A doubled `compile-given`
annotation found in that section is removed with it.

**Verify.** Driven red first: a compiled fence under `## Unreleased` fails with the stamp named; a
`compile-skip` one there still passes (only the compiled census moves); and — the regression that matters —
**the real tree's census is invariant under the exact stamp the workflow applies**, replayed through the
`read` seam. One existing test asserted the opposite contract ("the live prefix IS compiled") and was
INVERTED rather than deleted, carrying why.

**Introduced by.** **D164** (2026-09-19), which pointed check-samples at the live mask; the fence it tripped
over arrived with D157 the day before. Found by the release run itself — no gate could have caught it,
because the failure only exists in the state the pipeline creates.

## 2026-09-19 — the default img2img argv passed a mode value the current engine rejects

**Symptom.** An img2img render through `LocalDiffusionProvider` against a current stable-diffusion.cpp
build fails immediately: `invalid mode img2img, must be one of [img_gen, adetailer, vid_gen, convert,
upscale, metadata]`. txt2img was unaffected (it never passed a mode), and no test could see it — the argv
was ported, and the unit tests pin what the backend BUILDS, not what an engine ACCEPTS.

**Root cause.** Upstream folded txt2img/img2img into one `img_gen` mode and retired the `img2img` value;
the engine now selects img2img by the PRESENCE of `-i` alone. The ported default (`Img2ImgMode =
"img2img"`, always appended after `-M`) was correct for the build it was ported from and is an argv error
on `master-874-656a135`. D69's escape hatch could already absorb it (`Img2ImgMode = "img_gen"` is
accepted), but the shipped DEFAULT was broken against the engine a host downloads today.

**Fix.** `Img2ImgMode` is nullable and defaults to null — the mode pair is omitted entirely, matching the
measured engine; a host on an older build that still requires the pair sets `"img2img"` back. Found by
GEN-VERIFY-SD's measurement (`docs/task-archive.md` Part 261), from the engine's own argv parse.

**Verify.** Driven red first: the two exact-argv tests in `LocalDiffusionProviderTests` (no `-M` on the
img2img path; byte-identical unconfigured argv without the pair). Then measured green:
`LocalDiffusionLiveTests` ran txt2img and img2img end to end against the real engine, and the requested
250×250 came back as the clamped 256×256 on both legs.

**Introduced by.** The 2026-08-04 generation platform (archive Part 33), which ported the argv with its
own header saying "confirm against a live engine" — this was that confirmation, thirty-one engine
releases later.

## 2026-09-19 — `ConfigureRouting` reached chat alone; vector and score routed on the defaults, silently

**Symptom.** An operator's routing configuration — `ConfigureRouting(p => p.Retry(Failed, 2))`, the
`LYNTAI_RETRY_*` variables, a per-verdict `Surface` override, `ExemptSoleCandidate = false` — applied to the
text front door and was silently ignored by every factory-built router: vector, score, and any kind an
application closes `IProviderCall<,>` over. A vector backend that failed transiently was never retried,
whatever the operator had configured. Nothing failed and nothing logged; the calls simply routed on
`RoutingPolicy`'s defaults.

**Root cause.** `TextRouter` reads its policy from `LyntaiOptions.Routing` live. `ProviderRouter<,>` takes a
`policy` parameter defaulting to `new RoutingPolicy()`, and `ProviderRouterFactory` never passed one — it
was built (D155) without `LyntaiOptions` in its constructor, so it had nothing to pass. The rule existed on
the text side and was never carried across, the same one-sided shape D155's own entry records for the
bookkeeping.

**Fix.** The factory takes `LyntaiOptions` (optional, trailing) and defaults `For(...)`'s `policy` to
`options?.Routing`; an explicit `policy:` argument still wins, and DI supplies the options. Found by the
2026-09-19 design-closure review's call-shape symmetry pass (`docs/task-archive.md` Part 256).

**Verify.** Driven red first: `The_CONFIGURED_routing_policy_reaches_a_factory_built_router` (a flaky
vector backend heals on the configured retry) and `An_EXPLICIT_policy_still_wins_over_the_configured_one`,
both in `ProviderRouterFactoryTests`. Full suite green with the change.

**Introduced by.** D155's factory (2026-09-18) — one day old; found by review, not by a gate.

## 2026-09-19 — two backends named "onnx" shared one bench, so a failing reranker silenced recalls

**Symptom.** With an embedder and a reranker registered through `AddOnnxProvider` — both defaulting
`Id = "onnx"`, which the registration's own doc suggests distinguishing but nothing enforces — a rate-limited
or failing SCORE backend benched the VECTOR backend for the cooldown window: recalls quietly lost their
semantic channel because a different kind's backend had a bad minute. Two shipped docs
(`ProviderRouterFactory`'s parameter doc and `llm-and-router.md` §Dead-host tracker) claimed the tracker's
keys were domain-prefixed, so a reader checking the claim would have been reassured by prose the code did
not implement.

**Root cause.** `MediaRouter` prefixes its cooldown keys (`generation::`) and `TextRouter`'s bare format is
the historical one — but `ProviderRouter<,>` keyed on the bare configuration/id while sharing the ONE
`DeadHostTracker`, so every kind routed through the generic router shared one bench namespace with every
other. The prefixing rule existed, was documented as universal, and was applied in two of three routers.

**Fix.** `ProviderRouter<,>` takes an optional `cooldownScope`; the factory derives it per closed shape
(`vector::`, `score::`, an app kind's own request-type name), so an application-defined kind gets its own
namespace without telling the factory anything. The two doc claims now describe code that exists. A
duplicate-id guard in `AddProvider` was considered and refused — the TryAdd/BYO-wins pattern makes duplicate
ids transiently legitimate (**D162** records the refusal).

**Verify.** Driven red first: `A_score_failure_never_benches_a_vector_backend_sharing_the_same_id`
(`ProviderRouterFactoryTests`) — a benched `score::onnx` leaves `vector::onnx` asked and answering. Full
suite green with the change.

**Introduced by.** D155's factory (2026-09-18), inheriting D153's bare keys; found by the same review pass.

---

## 2026-09-17 — removing the embedder seam made a BYO backend invisible, and the error told you to use it

**Symptom.** After **D151**, `AddSemanticMemory()` threw for a consumer who had registered their own
embedding backend as an `IModelProvider` — either `services.AddSingleton<IModelProvider>(backend)` before
`AddLyntai`, or `builder.AddProvider(_ => backend)`. The thrown message named that exact route as the
remedy: *"A backend of your own is an IModelProvider declaring that it produces vectors"*. So the library
refused a wiring, and told the consumer to do the thing it had just refused. `AddSemanticMemory`'s own XML
doc made the same promise, and so did D151.

**Root cause.** The pre-D151 guard had TWO conditions — a builder flag (`EmbeddingProviderRegistered`, set
by `AddEmbeddingProvider`) **and** a descriptor scan for a registered `IEmbedder`. Deleting the interface <!-- drift-ok: the fix entry records the pre-D152 spelling -->
removed the type the scan looked for, and the scan went with it instead of being repointed. Only the flag
survived, and nothing but `AddEmbeddingProvider` sets it. Every SHIPPED backend was fine — Model2Vec, Onnx <!-- drift-ok: the fix entry records the pre-D152 spelling -->
and the HTTP provider all funnel through that method — so only bring-your-own broke, which is the half no
shipped test covered.

**Why the test suite was green.** The one test for the route, `An_embedder_registered_by_any_route_...`,
had both its arms rewritten to the same call during the refactor. Two identical arms cannot fail
independently: the second `Assert.NotNull` was guaranteed by the first. The test name still said "by any
route" and its doc still listed three.

**Fix.** `EmbeddingIsWired` restores the second route on the only footing that survives the deletion. A
FACTORY registration cannot be inspected before a provider is built, so the flag remains the statement for
it; an INSTANCE registration needs no statement, because the object is in the descriptor and declares its
own `ProviderCapabilities`. Reading the capability rather than counting providers is what keeps a chat-only
deployment failing fast — a second fact now pins that direction.

**Verify.** Two facts, both driven RED first: the host-instance route threw before the fix and wires after
it, and a host-registered chat-only backend still throws. The two arms of the repaired test are now
genuinely different code paths, and its doc says that if they ever read alike again, one of them is
testing nothing. Full `verify` green with Docker up, 3821 / 3854 / 33.

**Introduced by.** `355221d8` (D151), the same day — found by a review pass, not by a gate.

---

## 2026-09-17 — the release gate had not compiled since the rename campaign, and nothing said so

**Symptom.** `node devtools/dev.mjs consumer-smoke` — the gate whose whole job is proving the PACKAGES work
for a fresh consumer — exited 1 with four compile errors. It had last passed before the D125–D147 renames,
so it had been broken across the largest breaking change in the project's history while `verify` stayed
green on all 24 gates, twice a day.

**Root cause.** The gate writes a consumer app in the library's own public API and compiles it against the
packed nuspecs. That fixture is a CONSUMER, so every sweep that updates call sites has to update it — and
none did: it still constructed `GenerationCandidate` (**D125** replaced it with `ProviderCandidate`), passed <!-- drift-ok: the retired name the fixture held is the defect -->
`AddOllamaProvider(defaultModel:)` (**D132**/**D133** reshaped the registrations to `model:`), and lacked
the `using` for `ProviderKinds` and `ProviderVerdict` after both moved to `Lyntai.Lifecycle` <!-- drift-ok: the record names where the type went AT THE TIME; D154 renamed it after -->
(**D127**/**D136**/**D140**).

**Why nothing caught it.** Three mechanisms all miss `devtools/`: no prose gate scans it, the solution build
never compiles a fixture that exists only as a template string, and `verify` does not run this gate at all —
deliberately, because it is minutes. So the only thing that could have reported it was somebody running it,
which is what "run it before a release" actually means.

**Fix.** The template updated against the shipped API baselines rather than by guessing: `ProviderCandidate`,
`model:`, and `using Lyntai.Lifecycle`. The gate then passes end to end — pack, symbol-package check, <!-- drift-ok: the record names where the type went AT THE TIME; D154 renamed it after -->
restore, build, run.

**Verify.** `consumer-smoke` green, exit 0: 11 packages, 10 symbol packages each carrying a PDB, and the app
restores, compiles and runs. Re-run from a cold scratch feed, so the eviction step proves it compiled
against today's packages rather than a cached copy.

**Introduced by.** The D125–D147 sweeps, 2026-09-15 — the fixture was never in any of their call-site
updates. The reusable half is in `.claude/knowledge/pitfalls.md`: a gate outside the routine run rots like
an unrun test, and reports it at release time.

---

## 2026-09-16 — the retirement roster named a NAMESPACE, so the package it retired stayed listed

**Symptom.** None from the tool, which is the defect. `node devtools/nuget-unlist.mjs` printed
`Lyntai.ExtensionsAi` / `- not published, skipping` and ended `Planned: 0 version(s)… Done.` On nuget.org,
`Lyntai.Providers.ExtensionsAi` holds **30 versions, ten of them still LISTED** (2.0.1–3.1.0) — the package <!-- drift-ok: the PUBLISHED id this entry is about; it is data on a feed, not a name in the tree -->
**D123** folded away, which this array exists to unlist. The id in the array was never published at all.

**Root cause.** **D145** renamed the NAMESPACE `Lyntai.Providers.ExtensionsAi` → `Lyntai.ExtensionsAi` and <!-- drift-ok: as above -->
the sweep rewrote the `RETIRED` entry with everything else, turning a published package id into a namespace
name. The commit message states the opposite in as many words — *"The PACKAGE does not move… only the
namespace moves"* — so the sweep contradicted its own author. This is the THIRD instance: the commit
immediately before it (**D144**) had repaired two of these and written the warning into the file's header.

**Why nothing caught it.** The array is the one part of the roster with no on-disk counterpart, by design
(*"NOTHING on disk remembers them"*), so there is nothing for a rename to disagree with. No prose gate reads
it either: `check-docs` excludes `devtools/` structurally and `check-links`' code tier is `.cs` only.
Measured before concluding that: running every `retiredTerms` pattern over `devtools/**/*.mjs` comments
gives **28 hits, all legitimate** (a registry has to quote what it retires), so widening that gate is
refused — as is widening it to `*.csproj`, which scores **0 hits over 18 files**.

**Fix.** The published id restored. `mustBePublished(id, listed)` makes a `RETIRED` id the feed has never
published an ERROR rather than a skip — true by construction, since the array holds only ids that WERE
published. The tool's body moved into `main()` behind an `import.meta.url` check so the predicate is
importable without the network, which is the pure seam `.claude/rules/repo-mechanics.md` §Dev loop asks for.

**Verify.** Driven RED against the roster as it stood at HEAD: `mustBePublished('Lyntai.ExtensionsAi', null)`
returns `true` under the old array, and the roster fact fails on the missing published id. Five facts in
`devtools/scripts/__tests__/nuget-unlist.test.mjs`, including a positive control so an emptied array cannot
pass. A live dry run then resolves all eight retired ids and reports `Planned: 0` with no error line.

**Introduced by.** `888e0fde` (D145), 2026-09-15 — one commit after the header warning it broke.

---

## 2026-09-16 — a rename sweep collapsed CONTRASTS, in prose, in a sample, in shipped code and in a test

**Symptom.** None visible, which is what let it spread. `docs/DECISIONS.md` **D36** read *"translating
between `ProviderVerdict` and `ProviderVerdict`"* — a decision about a translation, describing a type <!-- tautology-ok: quotes the defect this entry is about -->
translated to itself. Six sentences across `DECISIONS.md`, `CHANGELOG.md`, `pitfalls.md` and a shipped
`Lyntai.Core` comment had the same shape. The README's capability-probe sample shipped
`if (provider is not IModelProvider installation) continue;` against a variable already typed
`IModelProvider`, and `check-samples` compiled it happily. `MediaRouter.StreamAsync` carried the same
test as a live branch with an error message naming a case that could no longer occur, and
`GenerationProviderContract` asserted `Assert.True(provider is IModelProvider)` on an `IModelProvider`
parameter — a contract fact that could not fail.

**Root cause.** **D127** collapsed `ILlmProvider` / `IGenerationProvider` / `IGenerationStreamProvider` <!-- drift-ok: names the three seams D127 retired, which is this entry's subject -->
into one `IModelProvider`, and the sweep replaced every occurrence of the retired names. <!-- drift-ok: continues the sentence above --> Where a sentence
MENTIONED one, the result is correct; where it CONTRASTED two, both sides became the survivor. The type
tests went the same way: a real question became a tautology the compiler cannot object to, because
`x is not T` on a `T` is still meaningful for null.

**Every gate was satisfied by construction.** `check-docs` hunts vocabulary a decision RETIRED and the
retired name was gone; `check-links` resolves member names and the survivor resolves;
`check-api-vocabulary` reads the frozen surface, which never held prose; `check-samples` compiles a sample
and a tautological type test compiles. **`check-docs.mjs`'s own `HISTORICAL` list records this happening in
`docs/2026-07-17-lyntai-design.md` and fixes it by exempting that file** — the incident was filed as *this
file was damaged* rather than *this sweep was damaging*, so the other ten sites were never looked for.

**Fix.** All ten prose and sample sites corrected. The router's dead branch deleted — post-D127 such a
backend inherits `IModelProvider`'s default `StreamAsync(GenerationRequest, …)` and lands in the ordinary <!-- drift-ok: the record names the type AS IT WAS; D154 renamed it after -->
pre-commit failure path carrying its own `NotServed` detail, which is a better message than the branch
gave. `GenerationProviderContract.ServesMediaStream` replaces the vacuous type test: it reads the INTERFACE
MAP and asks whether the concrete type OVERRIDES the default member, which is the question that survived
the collapse. `check-tautology` is the new gate for the prose half.

**The orphaned fixture is the part worth carrying.** `LyingStreamProvider` was built to drive that router
branch. D127 made the branch unreachable, so the fake was used by NOTHING for a release — build green,
suite green, coverage reported. A fixture whose only caller is a branch that stopped existing is invisible
in exactly the way the branch is.

**Verify.** `check-tautology` was driven RED against `git show HEAD:` copies of the six prose files and
reported all six, one line each — synthesized fixtures prove the patterns, the real pre-fix tree proves the
gate. `DeclaredDeliveryIsBackedTests` drives the contract fact both ways: the two streaming fakes pass,
`LyingStreamProvider` fails with the new message, and a backend that declares no Stream is not asked. The
router deletion was checked against the full Generation suite (374 passed, 0 failed) and `check-warnings`.

**Introduced by.** The D127 sweep on 2026-09-15 — one day before, so every site had been wrong since it
landed and none of it had ever been right under the new names.

---

## 2026-09-15 — a cross-encoder refused a multi-label export where nothing was listening

**Symptom.** None visible, which IS the defect. `AddOnnxCrossEncoder` pointed at an NLI-shaped export — <!-- drift-ok: the record names the registration of its day; D157 folded it into AddOnnxProvider -->
three labels per pair rather than one — loads cleanly, and every recall through
`AddMemoryScoringVerification` comes back exactly as the engine ranked it. That is indistinguishable from
having registered no scoring backend at all, and nothing above debug level says otherwise.

**Root cause.** `CrossEncoderLogits.Read` refuses such a head rather than reading column 0, and its own
comment calls that *"safe and LOUD"*. It is loud only for a DIRECT caller. The seam it was built to serve
(**D139**) is `ScoringVerificationPolicy`, which is fail-open by contract: it catches every exception, logs
at `LogDebug` and returns `NoOpinion`. **The refusal was raised into the one consumer designed to swallow
it.** The asymmetry sat inside a single method — `OnnxCrossEncoder` already ran one shape check at <!-- drift-ok: the record names the class of its day; D157 folded it into OnnxProvider -->
composition (an output it cannot name) and left the sibling label-count check to run time.

**Fix.** `CrossEncoderLogits.ShapeProblem` states the rule once, and both `CrossEncoderLogits.Read` and the
new `CrossEncoderLogits.ScoreOutput` ask it — so a graph cannot be refused at one time and accepted at the
other. `OnnxCrossEncoder.FromDirectory` now resolves its output through `ScoreOutput`, against the graph's <!-- drift-ok: the record names the class of its day; D157 folded it into OnnxProvider -->
DECLARED `OutputMetadata`, so a multi-label head throws at composition. A graph declaring a dynamic label
axis (`-1`) states too little to refuse on and is deferred to `Read` and the tensor it really returns.
`ScoringVerificationPolicy` keeps failing open — that is its contract and is pinned separately — but a
`NotSupportedException` now logs at **Warning**, because a backend declaring a kind it does not serve is a
permanent wiring defect and not a transient failure.

**`ScoreOutput` takes the declaration, not the session, and that is the load-bearing half of the fix.** A
check welded to a native `InferenceSession` is one no test can reach, so deleting it leaves the suite green
— the defect this repository records against `OnnxRegistrationTests` (`docs/task-archive.md` Part 226).
Passing the output names plus a shape lookup is the same separation `EmbeddingPooling` and
`CrossEncoderLogits` already make for the same reason.

**Verify.** `CrossEncoderShapeDeclarationTests` — the declared happy path, the multi-label refusal naming
what it read, a dynamic axis DEFERRED rather than rejected, and a theory asserting the declaration check and
`Read` judge four shapes identically. **The composition refusal was mutation-checked**: disabling the
`ShapeProblem` call in `ScoreOutput` failed exactly one test, the multi-label one, and nothing else. The log
level is pinned by a pair — a `NotSupportedException` reaching Warning, with a transient failure asserted
NOT to, so an implementation that warned about everything cannot pass.

**Introduced by.** `c1a62871`, the commit that added the cross-encoder — the refusal and the fail-open
consumer arrived together, so no window existed in which it behaved differently.

---

## 2026-09-15 — a graph engine's vector collections could be forgotten ACROSS a task boundary <!-- keeps: the collision, the U+001F fix and the three-spellings lesson all stand; "one spelling" was too strong, and the MECHANISM below is superseded -->

> **SUPERSEDED the same day, MECHANISM only: the bench reaches that one spelling by a compile-LINK, not by
> `InternalsVisibleTo`.** The correction below is right that a harness asserting against an internal address
> is a spelling of it, and right that the locomo control had to call the owner. What it reached for was too
> big: `Lyntai.Core` is published and NOT strong-named, so an `InternalsVisibleTo` is matched on assembly
> NAME alone, ships inside the nupkg, and hands every internal in Core to anything built under that name —
> bought for two static string methods.
> <br>**The bench now links `MemoryVectorCollection.cs` as a source file** (`<Compile Include=… Link=…>`),
> which the same csproj already did three times over for exactly this reason. It is the SAME source, so the
> one-spelling guarantee is unchanged; only the door is gone. `Lyntai.Tests` keeps its grant — it has to
> reach what it gates, and that trade is worth making once. Found by the Part 221 review
> (`docs/task-archive.md` Part 225).

> **CORRECTED the same day: there was a FOURTH spelling, and it was outside `src/`.** The
> `memory-locomo --retrieval` semantic CONTROL composed `locomo|{conv}|session` by hand and prefix-swept
> `locomo|{conv}|`. After the fix it matched nothing and printed
> *"collections=0 [] vectors=0 of 369 turns; semantic top-20 returned 0"* over a store holding all 369 —
> **a false zero, and one that reads as a dead retrieval channel rather than as a stale probe.** It is the
> more dangerous direction than the bug it was watching for: the control exists to prove semantic seeding
> runs, and it now disproved it. Caught only because a cross-encoder measurement needed that arm as its
> base (`docs/task-archive.md` Part 215).
> <br>**Why the tests could not catch it.** `MemoryVectorCollectionTests` asserts write-side/read-side
> agreement inside `Lyntai.Core`, and the bench is neither side — it is a third party asserting against an
> internal address, and nothing gated it because `bench/` has no API surface to gate.
> <br>**Fix.** `Lyntai.Core` now grants `InternalsVisibleTo` to `Lyntai.Benchmarks`, and the control calls
> `MemoryVectorCollection.For` / `PrefixFor` like everything else — so the owner really is the only
> spelling. The control now reads `collections=1 … vectors=369 of 369 turns; semantic top-20 returned 20`.
> <br>**The lesson below gets stronger, not weaker.** Grepping for the SHAPE rather than the helper's name
> is right and still would not have found this one, because the shape search was run over `src/`. **A
> harness that asserts against an internal address is a spelling of it**, and the search has to cover
> every tree that can compose one.

**Symptom.** `GraphMemoryEngine` addressed a similarity-index collection as `{engine}|{taskKey}|{scope}`.
Because `|` is an ordinary character a caller may put in either component, two different triples composed to
ONE address: engine `E` + task `a` + scope `b|c` and engine `E` + task `a|b` + scope `c` both give
`E|a|b|c`. Forgetting either task erased the other's enrichment vectors, and `SemanticSeedSource`'s
unscoped path — which prefix-swept `{engine}|{taskKey}|` — could read a neighbouring task's collections. A
task boundary is the one thing `docs/memory.md` §7 holds absolute (**D92**), and nothing validated either
component for the separator.

**Root cause.** Two independent mistakes that only bite together. The separator was PRINTABLE, so composed
addresses are ambiguous; and the address was spelled in THREE places — the engine's `VectorCollection`
helper, the seed source's own copy, and an inline interpolation on the engine's similarity-SEARCH path —
so no side could be fixed without another silently disagreeing. The engine's own doc named that hazard
(*"two spellings of one address is how a removal quietly misses the collection a write created"*) while
carrying a second spelling next door and a third inside itself.

**The third spelling was missed on the first pass and found by the tests**, which is the detail worth
carrying: grepping for calls to the helper (`VectorCollection(`) finds every site that USES the owner and
none that bypasses it, and the bypassing one is the only kind that can drift. The search that finds it is
for the SHAPE — an interpolation composing the address — not for the helper's name.

**The repository had already solved this TWICE elsewhere.** `MemoryContentId.For` length-frames its
components (`{len}:{value}`) with a comment naming the same collision, and `SemanticMemory` has used U+001F since it shipped, with
the reason in a comment: *"so ("ab","c") and ("a","bc") can't collide onto one collection."* The graph
engine, written later, did not inherit it. A convention that lives in one implementation's comment is not a
convention.

**Fix.** `MemoryVectorCollection` (internal, `Lyntai.Core/Memory/`) now owns the address for both sides:
`For(engine, taskKey, scope)` and `PrefixFor(engine, taskKey)`, separated by U+001F. The engine's
`VectorCollection` and the seed source's `Collection`/prefix all delegate to it, so there is one spelling
and an unambiguous one. Validating keys was considered and rejected: it moves a silent corruption to a
runtime throw on data a caller may already hold, where an unambiguous separator makes the collision
unrepresentable instead.

**Verify.** `MemoryVectorCollectionTests` — a theory over three ambiguous triples, the prefix's
non-reach into a neighbouring task, write-side/read-side agreement, and a behavioural test that remembers
into both colliding triples, forgets one, and asserts the other keeps its vectors. **All three of the
collision tests were confirmed to FAIL with the separator temporarily set back to `|`**, which is the only
evidence that they test the defect rather than the fix.

**Introduced by.** The graph engine's enrichment write path, at its introduction — the address has had this
shape since the vector collections existed. Persisted vectors written under the old address are orphaned by
the change rather than migrated, so a deployment re-indexes; enrichment rebuilds them on the next write.

---

## 2026-09-14 — the static embedder's tokenizer silently DELETED text: newlines, `$ ^ + = | < >`, and every emoji
 <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
**Symptom.** `StaticEmbedder` tokenized through `Microsoft.ML.Tokenizers`' `BertTokenizer`, which departs <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
from the reference BERT pipeline in four ways — and every one of them LOSES text rather than mis-splitting
it, so the vector is finite, plausible and wrong:

1. **`\t`, `\n`, `\r` do not separate words.** `"alpha\nbeta"` is one unmatchable token, so it embeds as a
   single `[UNK]`. Any multi-line document, memory entry or conversation turn is affected at every break.
2. **ASCII symbols are dropped entirely.** `$ ^ + = | < >` each have their own vocabulary row and each
   vanished — so a price, a comparison, and a URL's query string lose characters the model was trained on.
3. **An unmatchable symbol is dropped rather than `[UNK]`.** An emoji produced no token at all, silently
   shortening the mean rather than contributing the row the table has an opinion about.
4. **Accents were not stripped**, though every BERT `tokenizer_config.json` declares `strip_accents: null`,
   which the reference reads as *follow `do_lower_case`* — on. `"café"` was `[UNK]` instead of `cafe`.

**Root cause.** (1)–(3) are one mistake: the reference's `_is_punctuation` tests the ASCII ranges 33-47,
58-64, 91-96 and 123-126 **and then** the Unicode `P*` categories, precisely because `$ ^ + = | < >` are
Unicode *symbols* (Sc/Sk/Sm) rather than punctuation. Testing only the categories fails to split on them,
and the characters are then neither matched nor emitted. (4) is a plain default disagreement — the library
leaves accents on; the model's own config asks for them off.

**A fifth hazard that was not a defect, but was one edit away.** `BertTokenizer` shadows `EncodeToIds` with <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
a `new` method that adds `[CLS]`/`[SEP]`; the base does not. `StaticEmbedder` held the field as `Tokenizer`, <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
so it got the base one — correct for a `model2vec` table, and correct **by the declared type of a private
field**. Narrowing that field to `BertTokenizer`, which reads as a safe tidy-up, would have folded two rows
into every vector (`.claude/knowledge/pitfalls.md`).

**Fix.** The tokenizer is owned: `WordPieceTokenizer` implements the reference pipeline, and
`TokenizerRules` reads `do_lower_case` / `strip_accents` / `tokenize_chinese_chars` / `unk_token` from the
model's own `tokenizer_config.json` rather than defaulting them — the same stance `NormalizeFromConfig`
already took for `normalize`. This was reached while pricing the dependency for **D122**, not while
hunting a bug.

**Verify.** 15 new tests. The load-bearing one asserts id-for-id equality against
`Microsoft.ML.Tokenizers` — now a TEST-only reference — over a corpus built to hit the rules
implementations disagree on, and again over a **real 29,528-row pruned vocabulary** behind
`LYNTAI_STATIC_MODEL_DIR`; both are exact. The four corrections are pinned by a test that asserts the OLD
answer as well as the new, so if the library is ever fixed upstream the exclusion fails rather than rots.
`StaticEmbedderLiveTests` still passes against a real `potion-base-8M`.

**Introduced by.** **D121**, the commit that shipped the static embedder (2026-09-13) — the defect shipped
with the feature and was never released.

---

## 2026-09-12 — three fail-open contracts were false for the BYO implementations they were written to protect

**Symptom.** `ScoringService`, `MemoryPromptComposer` and `PromptRegistry` each promise fail-open in their
shipped XML docs — *"skips null/faulted results"*, *"Never throws — an outage in either source yields
whatever the other returned"*, *"a store outage → the default"* — and each guarded its handler with a bare
`catch (OperationCanceledException) { throw; }`. A BYO scorer, embedder, memory store or key-value store
that imposes its OWN deadline raises that exact type, so its timeout propagated out instead of degrading.
Worst of the five sites is `ScoringService`'s per-scorer loop: `results` is a method-local, so one scorer's
deadline discarded every score already computed.

**Root cause.** A component's own timeout and the caller's cancellation are the same exception type and can
only be told apart by asking whose token is cancelled. `Lyntai.Memory` was swept for exactly this on
2026-09-09 (`docs/task-archive.md` Parts 173–174) and the pattern it settled on —
`catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` above the fail-open arm —
is used at a dozen sites there. Cortex and Prompts were never swept; that sweep's own closing note names
only the SWALLOWING (`{ return; }`) shape as surviving outside memory, which is true and is a different
shape from this one.

**Fix.** The memory pattern applied verbatim at all five sites. `Winner`-style behaviour is unchanged for a
caller who never cancels; what changes is that a component's own deadline now degrades as documented.

**Scope, measured rather than assumed.** The tree holds **27** bare rethrows of this shape outside
`Lyntai.Memory`; **5** are fixed here and the other 22 are deliberately left, because reachability is the
test rather than the text. Those sit in `Lyntai.Generation` (which reports a verdict rather than promising
fail-open) and in the SQLite/Postgres stores, where the wrapped component is the database driver: SQLite
sets `DefaultTimeout = 30` and surfaces it as `SqliteException`, Npgsql as `NpgsqlException` /
`TimeoutException` — **neither is an `OperationCanceledException`**, so the fail-open arm already catches
them and the rethrow is unreachable. The five fixed are precisely the ones wrapping a **BYO seam**, where a
consumer's own `CancellationTokenSource` genuinely produces this type.

**Verify.** Nine new tests, 33/33 across the three suites. Each class gets the same pair — a component's
own timeout degrades, AND the caller's cancellation still propagates — because without the second half
"swallow every `OperationCanceledException`" would pass the first. Full `verify` green with Docker up.

**Introduced by.** Each site as written; found by the generic-solution audit, not by a failure. Nothing
regressed — the promise was never true for a BYO implementation.

---

## 2026-09-12 — a malformed embedding element became a silent ZERO, and a zero is a legitimate component
 <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
**Symptom.** `HttpEmbedder.ToFloats` <!-- link-ok: the PRE-RENAME name, which this entry exists to describe; the fix renamed it to TryToFloats --> read every element as <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
`n.ValueKind == JsonValueKind.Number ? (float)n.GetDouble() : 0f`. A JSON `null`, a number sent as a
string, or an object in an `embedding` array therefore became `0f` — indistinguishable from a real zero
component — and the result was a plausible, wrong vector that got stored, indexed and compared by cosine
with nothing anywhere reporting it. The only upstream guard checked the CONTAINER
(`emb.ValueKind == Array`) and the batch guard checked the COUNT of vectors, never their contents.

**Root cause.** A tolerant parse in the one place tolerance cannot be detected. Everywhere else this file
fails loudly — a non-2xx throws, a malformed body throws, a vector-count mismatch throws, and the type's
own XML doc promises `InvalidOperationException` for "the response was malformed" — so the element loop was
the single silent path in an otherwise strict reader. A wrong vector has no symptom at the seam: it is the
right length, the right dimension, and it ranks.

**Fix.** `TryToFloats` returns null when any element is not a number, or is not FINITE — a large enough
magnitude overflows float32 to Infinity and poisons every cosine it touches. All three shapes route that
null into the failure the caller already had (`?? throw new InvalidOperationException("malformed or empty
embeddings response")`), so this is not new behaviour but the documented behaviour finally reached. A bad
element fails the WHOLE response rather than dropping one vector, because dropping one would trip the count
check instead and report an arity problem pointing away from the real defect.

**Verify.** Five new tests, 22/22 in `HttpEmbedderTests`. The load-bearing one is the positive control —
*a legitimate ZERO is still a perfectly good component* — because the obvious over-correction is to reject
`0f`, which is common in a real embedding and was exactly what the old coercion produced. Full `verify`
green, 22/22 gates, with Docker up.

**Introduced by.** Present since the embedder was written; found by the generic-solution audit rather than
by a failure, which is the point — this class of defect produces no symptom to notice.

---

## 2026-09-12 — a judge that never answered reported a substantive TIE, and nothing typed said otherwise

**Symptom.** `LlmPairwiseComparer` returned `PairwiseWinner.Tie` in three unrelated situations: the model
answered *"tie"*, the model did not answer at all (`Verdict != Ok`, a refusal, an unparseable reply), and a
two-pass position-bias run whose passes contradicted each other. `PairwiseResult.Winner` was the only typed
channel, so a consumer could not tell a judge being DOWN from a judge saying *neither is better* — a model
outage read as an evaluation result. The only difference was the prose `Reason`, which is `string?` with no
contract and nothing a caller can branch on.

**Root cause.** The seam was written with one output channel where it needed two, and the library already
knew that: `MemoryVerification.Judged` exists one subsystem over for exactly this, and its own doc says
collapsing *"the verifier could not decide"* into *"nothing was relevant"* is the defect to avoid. The
pairwise seam simply never got the same treatment. Found by an audit for places the library behaves like a
wrapper for one model rather than a generic solution — a seam that cannot report that its model failed is
one of those, because the failure becomes indistinguishable from a result.

**Fix.** `PairwiseResult` gains `Judged` (default `true`) and a `NoOpinion(reason)` factory, mirroring
`MemoryVerification`. `Judged` means **the model produced a usable answer** — not that the answer was
decisive — so a genuine *"tie"* and a self-contradicting two-pass run are both `true`, and only the absence
of a usable answer is `false`. A pass that produced nothing now POISONS the combined two-pass result, which
the original could not express: it would otherwise report a confident verdict built on one real answer and
one failure. `Winner` is unchanged in every case, so a caller that ignores `Judged` behaves exactly as
before.

It is an init-only property rather than a positional parameter deliberately: a positional one changes the
record's constructor AND `Deconstruct` arity, which the frozen surface (**D70**) does not allow. Both API
baseline diffs are purely additive.

**Verify.** Six new tests, 10/10 in `PairwiseComparerTests`, and the one that matters is the positive
control — *a judge that genuinely answers TIE is still judged* — without which an implementation reporting
`Judged = false` for every `Tie` would pass everything else. Full `verify` green, 22/22 gates.

**Introduced by.** `456047f` (judge calibration helpers, v0.4), which shipped the seam with a single output
channel. Nothing regressed since; the defect is as old as the type.

---

## 2026-09-11 — a bench printed a HARDCODED caveat about its own output, and it was wrong in both directions

**Symptom.** `memory-locomo --verdict` printed *"'judge-graded' is generous by roughly 12 points"* under its
own results table, on every run, regardless of what that table said. At `--n 300` the measured gap was
**26.7 / 27.8 / 26.5 points** across the three arms — about 15 points off. On the bring-up run the gap ran
the OTHER way, judge-graded BELOW token-F1, so the sentence was not merely stale but directionally wrong.

**Root cause.** The number was a string literal in `PrintVerdictBound`, written from one early observation
and never connected to the arms printed three lines above it. A literal in an explanatory note reads exactly
like a derived one, and a caveat is the last line anybody re-checks against the table it explains — so it
rots invisibly while the table beside it moves. Nothing could catch it: `check-counts` gates counted claims
in maintained PROSE and this is a bench's stdout, `check-measurements` reads the record rather than the
harness, and the run's own exit code is 0 either way.

**Fix.** The caveat now DERIVES everything it asserts from the rows it has just printed: the per-arm gap,
its spread across arms, and the largest paired arm difference it is judged against. The load-bearing half of
the sentence is kept — the bias is COMMON-MODE, which is the only reason the column is reported beside
token-F1 rather than instead of it — but it is now stated as a computed comparison (*"the gap VARIES x
points against a largest arm difference of y"*) with the note that a spread comparable to those differences
would mean the opposite. A derived number cannot be wrong in either direction.

**Verify.** `build` clean. Not re-run: the n = 300 sweep is a ~50-minute contended-model run and this
changes only a report line, so the arithmetic was checked against the recorded table instead — the three
gaps and the 1.3-point spread follow from the arms in
`devtools/_verdict/run-verdict-n300-2026-09-11.txt` <!-- link-ok: gitignored raw sweep output, named as provenance -->,
and the 3.5-point reference is that file's own `partition − base` mean of −0.0346. **That file predates this
fix and still shows the literal**: every other number in it stands, and only that one caveat line does not,
which `docs/memory-measurements.md` §5 states where the figures are published.

**Introduced by.** `01b9889` (2026-09-11, the paired bound for the verdict study), the same commit that
added the table the caveat describes.

---

## 2026-09-11 — a burst-1 rate limiter test on the REAL clock, flaking only under `verify`'s own load

**Symptom.** Two `GenerationGovernanceTests` (the hosted-image and TTS-stream rate-limit cases) failed
intermittently under a full `verify` run and passed every time in isolation — the same "invisible to a
standalone run that starts clean" shape as Part 99's watched flake below, but a DIFFERENT cause: this is two
tests, not nine, and neither spawns a process or resolves a PATH entry. **This is NOT Part 99's watched
flake** (`TASKS.md` Part 99) — that one is nine `ProcessRunner`/CLI-provider tests failing together from one
cached-failed-lookup cause. Conflating the two would dilute the only refutable claim Part 99's item has:
recurrence of exactly those nine names, from that one mechanism.

**Root cause.** Both tests built a `TokenBucketRateLimiter` with `RateLimitOptions { PermitsPerSecond = 1,
Burst = 1 }` on the REAL clock, then asserted the second of two immediately-awaited calls was refused. A
burst-1 bucket refills at 1 permit/second, so the assertion only holds if the gap between the two `await`s
stays under a second — true in isolation, not guaranteed once `verify` has run `test-devtools`, `build` and
nine gates ahead of the test step and the machine is under load. A stall of a second or more between the two
calls refills the bucket and wrongly admits the second one.

**Fix.** Both sites now inject a fixed clock instead of the real one — `TokenBucketRateLimiter` already
accepts an optional `Func<DateTimeOffset>? clock`, so this is a one-line change per site, matching
`RateLimitTests`' own pattern (`FrozenNow`, a constant `DateTimeOffset`).

**Verify.** The subsequent full `verify` ran clean.

**Introduced by.** `86ee606` (2026-08-04, GEN5 governance/telemetry), which built both tests on the real
clock; fixed the same day `verify` first surfaced the flake, in `93689ca`.

---

## 2026-09-09 — the fail-open chain: 16 more sites, and the contracts that stated the false premise

**Symptom.** None observed, and that is the entry's own point. This closes the item the entry below opened:
16 bare `catch (OperationCanceledException) { throw; }` sites remained in `Lyntai.Core/Memory`, filed as
needing individual answers because "cancellation semantics differ" between them.

**Root cause.** They did not differ. Every one of the 16 sits above a handler making the SAME promise —
"must not sink the blend", "returning nothing", "the entry is stored unlinked", "a broken custom store must
not sink the caller's prompt" — and every one wraps a **BYO interface** (`IMemoryGraphStore`, `IMemoryStore`,
`ICuratedMemoryStore`, `ISemanticMemory`, `IEmbedder`, `IVectorStore`, `IMemoryEngine`), any of which a
consumer may implement over HTTP. The driver question that framing invited — what Npgsql and
Microsoft.Data.Sqlite throw on a command timeout — turned out to be **irrelevant here**: `Lyntai.Core`
references neither, and both are reachable only from the adapter packages.

**And the handlers NEST, which is why a per-site answer was the wrong shape.** One timeout from a BYO
embedder passes through `SemanticSeedSource` → `GraphMemoryEngine.GatherAsync` (no catch of its own) →
`GraphMemoryEngine.RecallAsync` → `CompositeMemoryEngine` → `MemoryWalk`/`MemoryComposition`. A bare rethrow
at any link breaks the promise of every link above it, so fixing a subset buys nothing.

**Fix.** The filter at 15 sites. The sixteenth — `CollectSignals` — has its catch **deleted** instead:
`IMemorySaliencePolicy.Signals` is synchronous and takes no token, the method has no `ct` to test, and
nothing there can be relaying a caller's cancel.
<br>**Four seam CONTRACTS said the false premise and were corrected with the code**, because fixing one
without the other ships a doc that contradicts the behaviour and no gate can see it. `IMemoryEngine` said
*"Only `OperationCanceledException` propagates, because cancellation belongs to the caller"*;
`IMemorySeedSource` said *"Cancellation … is always propagated"* one sentence after *"it must not throw for
a transient fault"* — which an `HttpClient` timeout is both of. Each now states the TEST
(`ct.IsCancellationRequested`) rather than the type.

**Verify.** `MemoryFailOpenCancellationTests` — 17 facts, one per fixed path plus an end-to-end chain test
and five caller-cancel controls. **Mutation-tested twice**: against the unfixed tree 12 of 17 fail and only
the 5 controls pass. The first probe caught a **vacuous** test of its own — the subject-index fact passed
either way, because with no annotator registered the engine has no subjects and never calls
`RecordSubjectsAsync`. `verify` green. `Lyntai.Core/Memory` now holds 21 of these catches, 20 guarded and
**0** bare.

**Out of scope and filed, not swept:** `JobRunner`'s heartbeat loop has the same shape
(`catch (OperationCanceledException) { return; }` over `_store.HeartbeatSlotsAsync`), where per **D73** a
lost heartbeat is a lost cross-process job slot. Different subsystem, different promise, its own answer.

---

## 2026-09-09 — the same defect in three more places, and a control that could not fail <!-- keeps: the three sites, the fix, and the caller-cancel control that PASSED under the wrong repair; NOT the census or the "two remaining candidates" filing -->

> **PARTLY SUPERSEDED — the census and the filing below were closed by the entry above, the same day.**
> <br>**KEEPS — and it is the reusable half:** both seams' caller-cancel twins, the controls that exist to
> stop the wrong repair, **passed under the wrong repair**. A control that cannot fail is worth nothing,
> and neither one could until it was made to MARK the exception the seam throws and assert on the marker.
> <br>**DOES NOT KEEP — the census** (`5` guarded, `16` bare) and **"two remaining candidates are recorded
> in `TASKS.md` rather than swept"**. The entry above swept all 16, having found the premise that filed
> them — that their cancellation semantics differ — was false. The census is now 21 catches, 20 guarded,
> **0** bare.

**Symptom.** None observed. This is the entry below's own open question, answered by asking rather than
waiting: it closed by fixing `GraphMemoryEngine.VerifyAsync` and filing whether the ANNOTATION seam — the
other opt-in model-in-the-loop policy, and the other one documented fail-open — had the same defect.

**Root cause.** It did, and so did two more. Three sites carried `catch (OperationCanceledException)
{ throw; }` ahead of a fail-open handler: `LlmMemoryAnnotationPolicy.AnnotateAsync`
("**Fail-open, always**" in its own type doc), `GraphMemoryEngine.AnnotateAsync` (BEST-EFFORT: "the write
proceeds exactly as it would have"), and — not named in the filing — **`LlmMemoryVerificationPolicy.VerifyAsync`
itself**, which the entry below did not touch because the engine's outer catch masks it on the shipped
path. It is reachable for a BYO engine or a direct caller, and it is a written promise the type does not
keep. The write path is the worse half: annotation runs BEFORE `store.UpsertAsync`, so a slow annotator
lost the FACT, not merely its subject edges.

**Fix.** `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` at all three,
and the promise moved to where every implementation is held to it: `MemoryAnnotationPolicyContract` and
`MemoryVerificationPolicyContract` each gained "a policy timing out on its own yields no opinion", driven
by a `TimingOutClient` that throws what `HttpClient` throws while the caller's token stays uncancelled.

**The second defect was in the first fix's own test, and it is the reusable half.** Both seams' caller-cancel
twins — the controls that stop the wrong repair, "swallow every cancellation" — **passed under the wrong
repair**. `RecallAsync` checks the token before anything else, so a pre-cancelled token never reaches the
verifier; on the write path the annotator's exception was swallowed, the write continued, and
`store.UpsertAsync` (outside every `try`) threw its own `OperationCanceledException`. Both now MARK the
exception the seam throws and assert on the marker, and the verification twin cancels MID-CALL because a
pre-cancelled token cannot reach it at all.

**Verify.** Four new tests fail on the old catches and pass on the new ones; both rewritten caller-cancel
twins were **mutation-tested** — with the guard deleted they go red, which the versions they replace did
not. Census, run rather than assumed: `Lyntai.Core/Memory` holds 21 of these catches, **5** guarded
and **16** bare, <!-- count-ok: the census AS OF THIS FIX; the entry above closed the 16 --> the bare ones wrapping store work, an embedder, or a whole recall. Two remaining
candidates are recorded in `TASKS.md` rather than swept, because a sweeping edit is what the filing warned
against. **The guarded idiom was not new**: `SemanticMemory.cs:50` has carried it over an embedder since
2026-07-18 (`8a2cde6`), so what this fixed was unevenness, not a missing technique.

---

## 2026-09-09 — a fail-open seam failed CLOSED on the one failure a model-backed policy actually has <!-- keeps: the symptom, root cause and fix — the only statement of them anywhere; NOT the "both tests fail" claim, the "20 other sites" census, or the open question at the foot -->

> **PARTLY SUPERSEDED — two claims below are wrong, and this entry is still not skippable.** Corrected the
> same day by the two entries above rather than left to be inherited.
> <br>**KEEPS — and it lives only here:** the symptom, the root cause and the fix. An `HttpClient` timeout
> arrives as `TaskCanceledException`, which **is** an `OperationCanceledException`, so a fail-open seam
> that rethrows the base type fails closed on exactly the failure a model-backed policy has.
> <br>**DOES NOT KEEP — *"Both tests fail on the old catch"*, which is FALSE of the second.**
> `A_CALLER_cancelling_still_propagates` passed under the old catch and under the wrong fix alike, because
> `RecallAsync` checks the token before the verifier is ever reached. It has since been rewritten to
> discriminate, and mutation-tested.
> <br>**DOES NOT KEEP — *"20 other sites"*, which is 19.** The census counts 21 in all, and one of the 20
> (`SemanticMemory.cs:50`) already carried the filter, having done so since 2026-07-18 (`8a2cde6`).
> <br>**The open question at the foot is ANSWERED:** yes, the annotation seam had the same defect, in two
> more places — and the remaining bare sites were swept by the newest entry, which found their "cancellation
> semantics differ" premise was false.

**Symptom.** A `memory-longmemeval --haystack --trigger` run died ~40 minutes in, after ingesting 34,242
turns, with `TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 300
seconds elapsing`, thrown from a judge call and propagating all the way out of
`GraphMemoryEngine.RecallAsync`. The recall did not degrade — it failed, and took the run with it.

**Root cause.** `GraphMemoryEngine.VerifyAsync` is documented fail-open and catches everything, returning
`MemoryVerification.NoOpinion` and logging: *"memory verification failed; reinforcing what was returned"*.
But it rethrows `OperationCanceledException` FIRST, which is correct for a caller's cancellation — and an
`HttpClient` timeout surfaces as `TaskCanceledException`, **which is an `OperationCanceledException`**. So
the two are indistinguishable at the catch site, and the seam failed closed on precisely the failure a
model-backed policy is most likely to have: a slow model. A verifier is opt-in and defaults to none, which
is why this survived — the shipped path never registers one.

**Fix.** `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` — rethrow only
when the CALLER actually cancelled; anything else falls through to the fail-open handler. Both halves are
pinned, because the wrong fix here is to swallow every cancellation, which would make a cancelled recall
look like a successful one: `MemoryVerificationTimeoutTests` asserts that a timing-out verifier still
returns items AND that a cancelled caller still gets an `OperationCanceledException`.

**Verify.** Both tests fail on the old catch and pass on the new one; <!-- both this sentence and the count below are corrected at this entry's HEAD --> `verify` green. **The same idiom
appears at 20 other sites in `Lyntai.Core/Memory`** <!-- count-ok: the figure AS CLAIMED here; it is 19, corrected at this entry's HEAD --> and was deliberately NOT changed in this fix: the
evidence is for the verification seam, and the store-facing sites wrap work whose cancellation semantics
are different. Whether the annotation seam — the other opt-in model-in-the-loop policy, and the other one
documented fail-open — has the same defect is an open question, filed rather than assumed.

---

## 2026-09-02 — the LoCoMo oracle arm could not run at full sample, and a 200-question ceiling hid it

**Symptom.** `node devtools/dev.mjs memory-locomo --retrieval --n 1540` dies partway through the eighth
conversation with `System.ArgumentException: An item with the same key has already been added. Key: What are
the names of Jolene's snakes?`, thrown from `Ladder()` while building the `+forget0+oracle` arm. Nothing was
wrong with the engine; the harness could not construct one of its own arms. The wrapper reported `EXIT=0`
because the shell line ended in an `echo` — the lying-exit-code shape `.claude/rules/windows-machine.md`
records — so the crash had to be read out of the output file rather than the status.

**Root cause.** `EvidenceOracleVerifier` was constructed as
`mine.ToDictionary(q => q.Text, q => q.Evidence.ToHashSet())` (introduced with the verdict arms in
`47de408`), which assumes question text is unique within a conversation. It is not: **LoCoMo repeats 11 QA
rows verbatim inside `conv-48`** — measured, not inferred — every one category 4, and every pair
byte-identical in both evidence and gold. Text is nevertheless the only key available, because
`MemoryVerificationRequest` carries the query and nothing else.

**The reason it survived four months of use is the interesting half.** Every retrieval ladder on record ran
`--n 200`, and the stratified sample never drew both copies of any duplicate. So the arm whose 77.5% ceiling
is quoted in the backlog, `docs/memory-measurements.md` §5 and `docs/task-archive.md` Part 235's framing had
**never been run at full sample and could not be** — a defect reachable only at a size nobody had used. The
QA path carries the same assumption and is silent about it: `recalled[(arm, q.Text)] = …` OVERWRITES where
`ToDictionary` throws, which is why the n = 1,540 QA run earlier the same day completed normally. That
silence is benign only because the duplicates are exact.

**Fix.** `EvidenceByQuery(questions, convId)` groups by text and unions the evidence. The union is the
honest resolution rather than first-wins: two questions sharing one text are indistinguishable to a judge
shown only that text, so their combined evidence is the ceiling a perfect one could reach. Because the
duplicates are exact here it changes no score — and rather than leave that as a measured assumption that can
rot, the helper WARNS when duplicated texts declare different evidence, naming the conversation and calling
the oracle generous to those questions.

**Verify.** The full run completes: 1,540 of 1,540 questions, all ten conversations, `conv-48`'s 191
questions probed, 9,611.4s, and **the divergence warning did not fire**, which is the fix's own control
reporting that the duplicates are identical. The oracle is demonstrably firing rather than silently
inert — 65.6% multi-hop against `+forget0`'s 51.4% — so the arm was not repaired into a no-op. Table:
`docs/memory-measurements.md` §5.

## 2026-08-30 — `memory-salience`'s OFF arm was never off, so its whole table measured one consumer

**Symptom.** `node devtools/dev.mjs memory-salience` (and both its ladders) reports a paired difference
against an arm labelled `SalienceOff`. Nothing failed, every control was green, and the tables have been
quoted in `docs/memory-measurements.md` §5 and cited by `docs/task-archive.md` Part 216 since 2026-08-28. The defect surfaced only
when the `--novelty` ladder was widened from two corpus shapes to six on 2026-08-30: the `NW0` arm — which
provably emits no salience at all, `0/51330` writes judged and 0 distinct values — came back
**significantly worse** than `SalienceOff` on `high-reuse` (+0.0401 [0.0145, 0.0656]) and `high-noise`
(+0.0493 [0.0280, 0.0707]). Two arms that both emit nothing cannot differ by more than the entire spread of
the arms they were being used to rank.

**Root cause.** The off arm passed `saliencePolicies: null`, and
`GraphMemoryEngine.NormalizeSaliencePolicies` substitutes a fresh shipped `StructuralSaliencePolicy` for a
null OR empty collection — the deliberate "empty does NOT mean off" contract, which `docs/task-archive.md` Part 216
states in as many words ("registering an empty collection does NOT — that takes the shipped default"). So
the off arm judged every write at the shipped `NoveltyWeight = 1.5` and wrote the salience signal that store
admission reads. What actually differed between the arms was the **retention policy alone**, with salience's
admission consumer ON in both — while the sweep's own preamble, its class doc and `docs/memory.md` all
describe it as measuring "retention and store admission, the two consumers that actually ship ON".
Wrong from this sweep's first run; never a regression.

**This is the second time the trap bit, and the first time is why `NeutralSaliencePolicy` exists.**
`MemorySalienceInversionTests` records it: *"The first three-arm run here asserted the control judged nothing
salient and got 255 — the 'control' was a second copy of the treatment"*, and concludes that a trap costing a
measurement its control belongs fixed in the library. By 2026-08-30 the rule was written in four places —
that type, the test `An_empty_policy_collection_leaves_salience_ON_and_only_the_neutral_policy_turns_it_off`,
`docs/task-archive.md` Part 216 verbatim, and two sibling sweeps that build their off arm correctly
(`MemoryEnrichmentSweep`, `MemoryImportanceSweep`) — and a harness written after all of them still got it
wrong. **What differed was the control, not the knowledge:** the test tier reports `SalientWrites` per arm
and asserts the control's is zero, so it caught its version in one run; the sweep counted salient writes on
treatment arms only, so its off arm contributed no row and nothing could be non-zero.

**Fix.** The off arm registers `NeutralSaliencePolicy` explicitly, and EVERY arm — off included — is wrapped
in the counting double, so "salient" and "distinct" mean the same thing in every row of the controls
(`bench/Lyntai.Benchmarks/MemorySalienceSweep.cs`). The ladder additionally re-pairs each rung against its
TIED rung rather than against off, because holding registration constant is the contrast a ladder claims to
draw; both contrasts print, since the vs-off table is where a confound of this kind is visible at all.

**Verify.** A positive control that throws: the off arm must have been **consulted** (`Judged > 0`) AND have
declined every write (`Salient == 0`). Asserting `Salient == 0` alone would pass on an arm that was never
asked — which is precisely the shape that shipped, since the old off arm was not wrapped and contributed no
row at all. The figures in `docs/memory-measurements.md` §5 taken through the old arm are marked rather than deleted;
they measure retention, which is a real quantity, just not the one they are labelled with.

## 2026-08-30 — ComfyUI promised the router it took inputs, then dropped them

**Symptom.** Hand `ComfyUiProvider` a `GenerationRequest` carrying a `GenerationInput` — a chained first <!-- drift-ok: the record names the types AS THEY WERE; D154 renamed them after -->
frame, an init image — and the render runs as if you had passed none. No error, no warning, nothing in the
result saying the image was discarded. The graph executes exactly as authored, so the output comes back
plausible and is simply not what was asked for. Found while surveying whether a 3D stage could feed the
video backends (`docs/task-archive.md` Part 124), not from a report.

**Root cause.** The backend declared `SupportsInputs = true` and never read `request.Inputs` — the
identifier occurred exactly once in the file, in the declaration. **The flag is not advisory:**
`GenerationCapabilities.Supports` uses it as an ADMISSION filter <!-- drift-ok: a dated entry naming the type AS IT WAS; D125 renamed it afterwards -->
(`if (request.Inputs.Count > 0 && !SupportsInputs) return false;`), so declaring it is a promise to the
router that this backend consumes inputs, and the router acts on it by SELECTING this backend for
input-carrying work. The submit path substitutes the prompt into the graph and posts it; there is no
branch that could have consumed an input, because a ComfyUI init image is a node the caller authored and
the platform cannot know which node that is. Introduced by `a0efbe6` (2026-08-04), the commit that added
the backend — never a regression, wrong from the first line.

`FalQueueProvider` had the same defect on its own side and fixed it, leaving the reasoning in a comment:
dropping a bytes-only input "submitted — and billed — a text-to-video render against a caller who asked
for image→video, and the result looked plausible". ComfyUI kept it for 26 days after fal's cure was in the
tree, because nothing generalised the cure.

**Fix.** Stop declaring the capability, and refuse rather than drop. `SupportsInputs` is no longer set
(`src/Lyntai.Generation/ComfyUiProvider.cs`), so `Supports` filters the backend out of input-carrying
requests and the router routes elsewhere; and `SubmitCoreAsync` now returns a `Failed` operation naming the
workflow-graph route if an input arrives anyway, which is reachable by a caller holding the provider
directly and bypassing the router. Nothing is posted, so nothing is billed. The flag bought the backend
nothing even charitably: a caller who bakes the image into the graph sends no `Inputs` at all, and
`Supports` only filters when there are some.

**Verify.** A new backend-agnostic contract fact —
`GenerationProviderContract.A_handed_input_is_consumed_or_refused`, wired into
`HttpGenerationProviderContractFacts` so every HTTP backend takes it. It hands the provider an input whose
bytes carry a marker and asserts that either nothing was sent (a refusal) or the marker appears in what was
sent, in raw or base64 form. On the unfixed tree it failed for ComfyUI alone — **1 failed, 3 passed**, so
OpenAI, Automatic1111 and fal already honoured it and the fact isolates the defect rather than describing
it. Plus two per-backend tests pinning the honest capability and the refusal. 35 passed after the fix.

## 2026-08-26 — a corrected `Metadata` bag was silently ignored on a re-remember

**Symptom.** Write a fact with `Metadata`, then write the same content again with a corrected bag — a fixed
`source_ref`, a field learned later — and the store kept the original. No error, no signal. The only route
to a correction was delete-and-rewrite, which discards the entry's id, its edges, its decay state and its
subject links.

**Root cause.** `metadata` was in every backend's INSERT column list and absent from `DO UPDATE SET`. Not a
decision: the neighbours that are *deliberately* absent from that clause (`stability`,
`provenance_retrievability`) each carry a comment saying so, and this one carried nothing. It was pinned as
"write-once" earlier the same day (**D90** recorded whether that was right as an open question) precisely
because the omission was indistinguishable from an intent.

**Fix.** `metadata = COALESCE(@metadata, …)` in both SQL backends and the equivalent in the in-process one —
**the rule its sibling `Signals` already had, written one line above it in the same statement.** An absent
(null or empty) bag is "no opinion" and keeps what is stored; a supplied bag replaces it. Replace and not
merge, exactly as signals does: a supplied bag is the caller's whole opinion, and merging would make
removing a key impossible, which is this defect's mirror image.

No API changed — `MemoryWrite.Metadata` is nullable and `CuratedMetadataJson.Serialize` returns null for
null-or-empty, so the distinction was already on the wire. Unlike the grade fix an hour earlier, nothing new
had to reach the store.

**Verification.** `MemoryGraphStoreContract.Metadata_keeps_the_stored_bag_when_none_is_supplied_and_replaces_it_when_one_is`
on all three backends, asserting both directions and that an unrestated key is GONE rather than merged, plus
two engine facts. The contract fact that previously pinned write-once was inverted and is the RED test.
`verify` 15/15.

**What it invalidated, recorded rather than quietly dropped.** The prototype resolver reported deriving an
assertion's `ValidTo` as FORCED by write-once — "the store's constraint and an append-only ledger are the
same thing". That convergence is gone. The derivation stays because it is right on its own terms, and the
record now says chosen rather than forced.

**Introduced by.** The graph store's first upsert; `metadata` has been absent from the update since the
column existed. Found by asking what a re-remember overwrites, not by a consumer.

---

## 2026-08-26 — re-remembering a fact without restating its grade silently demoted it

**Symptom.** `RememberAsync` an entry as `MemoryGrade.Authoritative`, then write the same content again
without naming a grade — the ordinary way an application refreshes a fact — and it came back
`Associative`. It then decayed like anything else, lost its reserved recall slot, could be truncated to a
headline, and became eligible for `PruneAsync`. No error, no warning, and the entry still there.

**Root cause.** `MemoryWrite.Grade` defaults to `MemoryGrade.Inherit`, meaning "take the engine's role".
`GraphMemoryEngine.RememberAsync` resolved that to `Associative` (absent an annotator suggestion) *before*
the store saw it, and every backend's upsert did `grade = @grade` unconditionally on conflict. **So "the
caller said nothing" and "the caller said Associative" arrived as the same value**, and the store had no
way to tell them apart.

Not a store bug and not an engine bug on its own — a distinction that was destroyed in between. Design
§5.7.0's objective (1) and **D90**'s invariant 2 are about exactly the entry it destroyed.

**Fix.** `GraphNodeWrite` gains `bool GradeStated = true`; the engine passes
`write.Grade != MemoryGrade.Inherit`; all three backends overwrite the stored grade only when it is true —
the same "only when the caller meant it" conditional `Signals`, salience and difficulty already had, which
is why the shape was already in the SQL beside it.

`Inherit` now inherits from the ENTRY on a re-remember and from the engine's role on a genuine first write.
An annotator's **suggested** grade counts as unstated, extending `RememberAsync`'s existing "a model may
advise what matters, never overrule the application" across time rather than only within one call.

**Verification.** `MemoryReRememberTests` (the fact that pinned the demotion was inverted, and is the RED
test) plus
`MemoryGraphStoreContract.An_unstated_grade_keeps_the_stored_one_and_a_stated_one_overwrites`, wired to all
three backends and asserting **both directions in one fact** — a store that simply never updated the grade
would pass the first half and break promotion, which is the capability the overwrite exists for.
`verify` 15/15.

**Introduced by.** The graph engine's first write path; `grade = @grade` has been unconditional since the
column existed. Found 2026-08-26 by asking what a re-remember overwrites, not by a consumer.

---

## 2026-08-26 — one transient `where.exe` failure made an installed CLI look absent for the life of the process

**Symptom.** Chased from the other end: `verify`'s test step intermittently failed **exactly 9** tests — 3
occurrences in 14 runs — while a standalone `dotnet test` passed every time, including immediately before
and after a failing run. The nine were `ProcessRunnerTests.Resolve_command_path_finds_node_and_caches`,
three `RouterEndToEndTests`, two `CortexIntegrationTests` and three CLI-provider facts. **Every one spawns
a process or resolves a command on PATH**, and they all reach the deterministic provider-stub through
`node`.

**Root cause.** `ProcessRunner.ResolveCommandPath` memoized `Locate(cmd) ?? cmd` into a **process-wide
static** `ConcurrentDictionary` — the fallback included. So a single transient locator failure (a spawn that
could not start under load, an AV hook, a dead PATH entry; `where.exe` is itself a process spawn) cached the
**unresolved bare name permanently**. `CommandExists` reads a resolved name with no directory part as NOT
FOUND, so from that moment every provider's `IsAvailable` reported an installed CLI as absent, for the
lifetime of the process.

That is why it looked like a test flake and is not one: it is reachable in a shipped application, and the
first probe of a provider's availability is exactly when a machine is busiest. The failure is silent,
permanent, and indistinguishable from "the CLI is not installed".

**Fix.** `src/Lyntai.Core/Processes/ProcessRunner.cs` — look the cache up first, resolve on a miss, and
cache **only a successful** resolution. A genuinely missing command now costs one locator spawn per call,
which is the right trade: a command that is absent is absent once, while a command wrongly believed absent
stays wrong until the process restarts.

**Verification.** `ProcessRunnerTests.A_FAILED_path_lookup_is_not_cached_so_one_transient_locator_failure_is_not_permanent`,
mutation-checked — restoring the old one-line body fails it with its own message. It carries a **positive
control** asserting a SUCCESSFUL lookup is still cached, because otherwise the fact passes on an
implementation that caches nothing, which would "fix" the bug by deleting the optimization the cache exists
for. `verify` 15/15.

**Not proven to be the flake's cause.** The mechanism explains every observation — intermittent,
all-or-nothing, constant count, only under `verify`'s process churn — but the flake was never reproduced on
demand (11 of 14 runs green, twice consecutively while trying). If it recurs, this was not it; `TASKS.md`
Part 99 keeps the entry open on those terms.

**Introduced by.** The commit that added the resolved-path cache; the `?? cmd` fallback has been inside the
`GetOrAdd` factory since.

---

## 2026-08-26 — a failing similarity index threw out of `PruneAsync` after the nodes were already deleted

**Symptom.** With a vector store that refuses writes, `GraphMemoryEngine.PruneAsync` removed the entries
from the graph store and then threw — so the caller got an exception instead of the count, and no way to
tell that the prune had in fact succeeded. `IPrunableMemory.PruneAsync` documents itself as best-effort
capacity management, where "removing fewer entries than hoped is a deferred cost rather than a defect".

**Root cause.** Introduced by the fix directly above it, on the same day. That change ordered the two
removal verbs deliberately opposite ways — a forget clears the index FIRST so a failure is retryable, a
prune clears it AFTER because there the cheap failure is an orphan — and then gave both the same uncaught
propagation. The ORDER was asymmetric and the ERROR HANDLING was not. The doc comment had already argued
the asymmetry in as many words; the `try`/`catch` simply did not implement it.

**Fix.** `RemoveVectorsAsync` (the prune-side cleanup, and its only two callers are prune paths) is now
best-effort: it logs a warning naming how many entries were removed and left as orphans, and rethrows only
`OperationCanceledException`. <!-- drift-ok: what this fix DID on its own day; that rule was itself retired 2026-09-10, and this very site is one of the 21 the entries above repaired --> `ForgetVectorsAsync` is unchanged and still fails loudly, which is the whole
point of the split.

**Verification.** `MemoryRemovalCompletenessTests`, two facts written RED-first against a vector store whose
every removal throws: a forget FAILS and leaves the nodes intact so the call is retryable (already correct),
and a prune REPORTS its count rather than throwing (was red). `verify` 15/15, 3293 passed / 3314 total.

**Introduced by.** `c458ace`, the entry below — found by reviewing what that commit documented and never
tested, rather than by a consumer.

---

## 2026-08-26 — graph memory's removal verbs never reached the similarity index, so `ForgetAsync` left the content readable

**Symptom.** With an `IEmbedder` and an `IVectorStore` wired, `GraphMemoryEngine.ForgetAsync("t", "s")` —
the path `IForgettableMemory`'s own doc calls *"the deletion path an application uses when a user withdraws
consent"* — removed the nodes and left each entry's **full content, verbatim**, retrievable from the vector
store. `PruneAsync` left the same payloads as orphans. Observed directly: the first RED run printed the
leaked text back (`Collection: ["the recovery key is written on the blue card"]`).

**Root cause.** `EnrichAsync` indexes every write as
`vectors.UpsertAsync(collection, id, vector, write.Content)` — the payload is the content, not a hash —
into a collection the engine addresses itself (`{Name}|{taskKey}|{scope}`). Neither removal verb touched
that store: `ForgetAsync` was a single `store.ForgetAsync(...)` and `PruneAsync` ended at
`store.PruneAsync`/`store.DeleteAsync`. **The vector store is not `IMemoryGraphStore`**, so no
`MemoryGraphStoreContract` fact could see it — which is exactly why the identical defect was caught for
SUBJECT rows (2026-08-14) and missed here: subjects live inside the graph store's own contract, and this
projection lives outside it. `pitfalls.md` §Second doors, and the tell is shared STATE rather than shared
code.

Two things kept it invisible. `IVectorStore` has always had `DeleteAsync` and `RemoveCollectionAsync`, so
nothing was missing to notice; and an orphaned vector cannot surface as a recall ITEM — `GatherAsync` drops
an id `store.GetAsync` no longer resolves — so the only symptoms were data at rest plus a silently wasted
`SemanticSeedOptions.K` slot.

**Fix.** `src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs`.

- `ForgetAsync` drops the collections first, then the nodes. **That order is the promise**: a failure
  leaves the nodes intact and the call retryable, where the reverse reports success over surviving content.
- Addresses are **derived from the nodes**, never prefix-matched. A collection name embeds task and scope
  with a separator either may contain, so a prefix sweep for task `"t"` also matches task `"t|x"` — and
  over-deleting is the one direction a removal must never err in. Deriving is also why no
  `IListableVectorStore` is required: that member is optional, and a consent withdrawal must not degrade to
  what a BYO index can enumerate.
- `PruneAsync` orders the other way (store, then vectors), because there the cheap failure is an orphan
  rather than a residue.
- Prune's **cheap path** reports only a COUNT, so the removed ids are recovered by a before/after census,
  paid only when a vector store is wired. Re-deriving the store's criterion in C# was rejected: it would be
  a second copy of one rule, and `CandidateCutoff` is deliberately CONSERVATIVE (widened by
  `MaxConnectionBoost`), so an engine-side evaluation would delete strictly **more** than that path does
  today — a behaviour change smuggled inside a residue fix.
- The collection address is now one private `VectorCollection(taskKey, scope)`; it was spelled inline at
  three sites, and two spellings of one address is how a removal misses the collection a write created.

**Verification.** `MemoryRemovalCompletenessTests`, eight facts — RED first, with five of the six original
facts failing and the payload printed. Both later facts were mutation-checked: disabling the census kills
`Pruning_removes_the_payloads_on_the_store_s_OWN_prune_path_too` and nothing else; making it unconditional
kills `Pruning_with_no_vector_store_makes_no_extra_store_reads` and nothing else. Full suite with the
Postgres leg live (Docker up, 21 skipped): **3272 passed / 3293 total**. `verify` 15/15.

**Introduced by.** `e98e44c` (the graph engine, 2026-08-08) wrote the one-line `ForgetAsync`; `6216c22`
(similarity enrichment, same day) gave it something to leave behind. Neither commit was wrong on its own —
the second added a projection the first's removal path could not know about, which is the shape.

---

## 2026-08-17 — a pre-commit connection failure escaped the generation stream door raw

**Symptom.** With two stream candidates where the first is unreachable, `IMediaRouter.StreamAsync`
threw a raw `SocketException` out of the enumerable instead of falling over: the healthy candidate was
never tried, no cooldown was recorded, and the caller got an exception where the contract promises exactly
one terminal chunk.

**Root cause.** The per-chunk catch carried `when (!NeverReachedTheBackend(ex))` — the SUBMIT door's
billing-ambiguity filter, whose own XML doc says it is used only on the submit path. On the stream door
nothing is charged by the act of asking, so the filter's one job (let a provably-undelivered submit
propagate to the durable-job retry) inverted into "let a provably-undelivered stream skip fallback". The
same round that articulated the never-reached-the-backend distinction copied it onto the door it does not
belong to — `pitfalls.md` §"Copying a rule copies its assumptions", fourth instance.

**Fix.** The stream door's catch is unconditional, with a comment saying why the submit door's filter must
not come back.

**Verification.**
`GenerationRouterStreamTests.A_connection_class_THROW_before_any_data_falls_over_like_any_other_pre_commit_failure`
— red with the raw `SocketException` before the fix. The existing post-commit throw fact still holds (a
throw after data stays final).

**Introduced by.** The commit that opened the stream door (D67); the filter arrived with the door.

---

## 2026-08-17 — the local diffusion ceiling could violate the engine's own multiple-of-64 rule, and the advertised ceiling went stale

**Symptom.** Two corners of the same D68 configurability pass. `MaxDimension = 1000` with a large request
put `1000` into `sd-cli`'s argv — 1000 % 64 = 40, against a requirement the class documents as the
engine's own. And a host following the registration doc's late-provisioning pattern (flip `Accelerator`
after a runtime probe) moved the ENFORCED ceiling while `Capabilities.Limits` kept advertising the old one
forever.

**Root cause.** `Round64` clamped to the host's cap verbatim — nothing validates the cap, so the clamp
outranked the rounding for any non-64-multiple value; every shipped and tested value happened to be a
multiple of 64. `Capabilities` was a `{ get; }` property initialized once at construction while
`GenerateAsync` reads `options.EffectiveMaxDimension` live — the D68 stale-advertisement class
reintroduced through the mutable-options path its own entry warns about.

**Fix.** The cap is floored to a multiple of 64 before it can win (never below the 256 floor), and
`Capabilities` is derived per access.

**Verification.** `LocalDiffusionProviderTests.A_cap_that_is_not_a_multiple_of_64_cannot_defeat_the_engines_rounding`
and `The_ADVERTISED_ceiling_follows_a_LATE_provisioned_option_change` — both red before.

**Introduced by.** The D68 commit that made the ceiling configurable; both corners shipped with it.

---

## 2026-08-17 — a global job slot leaked forever when the claim that followed it threw

**Symptom.** With `GlobalMaxConcurrency` configured, a shutdown cancellation or one transient store fault
landing between `TryAcquireSlotAsync` and `ClaimNextAsync` shrank the deployment's effective cap by one —
permanently, because `HeartbeatSlotsAsync` renews EVERY slot its worker id holds, so the orphan was renewed
for the life of the process and lease expiry never reclaimed it.

**Root cause.** No `try`/`finally` covered the acquire→claim window (or the previously-claimed slots in the
same pass): `ReleaseSlotAsync` was reached only on the clean `job is null` branch. `RunAndReleaseAsync`,
four lines below, already guaranteed release on every path — the guarantee was owed one window earlier and
not written there. D73 records the acquire-then-claim ordering and covers only the clean branch.

**Fix.** The claim loop hands back the in-flight slot when the claim throws and every already-claimed slot
on the pass's throw paths, through the same never-cancelled quiet release `RunAndReleaseAsync` uses. The
abandoned claims need no help — a claim's own lease expires unrenewed.

**Verification.** `JobRunnerTests.A_slot_is_released_when_the_claim_that_followed_it_THROWS` — red before
(the follow-up pass could never acquire the leaked slot).

**Introduced by.** The D73 commit that added the cross-process cap.

---

## 2026-08-17 — a hung locator froze the first CLI call forever

**Symptom.** The first `ProcessRunner` call for a bare command name (any CLI provider's first completion or
`IsAvailable` probe) could hang indefinitely when `where.exe`/`which` hangs — a dead network drive on PATH
or an AV hook is enough — ahead of every inactivity clock `RunAsync` arms, with no token in the chain to
break out.

**Root cause.** `Locate` read the locator's stdout synchronously and unbounded BEFORE its
`WaitForExit(5000)`, so the only bound sat behind the unbounded read — and the code's own comment already
said so while explaining a different problem (stderr drain).

**Fix.** Both pipes drain asynchronously; `WaitForExit(5000)` bounds the whole call, the tree is killed on
expiry, and both drains are observed under a short bound so a grandchild holding a pipe open cannot re-open
the hang.

**Verification.** `ProcessRunnerTests.A_locator_that_hangs_is_killed_at_the_bound_rather_than_hanging_the_caller`,
against a real hanging child — red before (30s bound hit). The RED run also demonstrated a second trap: the
orphaned child inherited the test host's console handles and wedged `dotnet test` itself past the failure,
which is why the fixture self-exits at 60s.

**Introduced by.** The commit that added the resolved-path cache; the ordering was original.

---

## 2026-08-17 — prose + an unassemblable tool call still reported a clean `Ok`

**Symptom.** An OpenAI-compatible stream carrying content deltas AND `finish_reason: "tool_calls"` whose
tool-call deltas never produced an assemblable call (a `function.name` that never arrives) ended in a
benign `Final` — the model asked for a tool and the caller was handed the prose as the answer. The exact
silent discard D71 was written to eliminate, surviving in one branch.

**Root cause.** The D71 guard ("finished for tool calls and assembled none is a real failure") was gated
`&& !sawContent`, closing only the no-content cell of the 2×2. The adjacent comment's own reasoning holds
regardless of content; the tests covered content+well-formed and no-content+unassemblable, and the fourth
cell was the untested one.

**Fix.** The guard fires whenever the stream stopped FOR tool calls and none could be assembled; the prose
already streamed and stays, and the `Error` is the terminal chunk (which the router passes through
unchanged post-commit).

**Verification.** `OpenAiCompatibleProviderTests.Content_AND_a_finish_for_tool_calls_that_assembles_NONE_is_still_a_failure`
— red before (clean `Final`).

**Introduced by.** The D71 fix commit — it repaired the no-content branch and left this one.

---

## 2026-08-17 — the engine factory's "only one registered" fallback was unreachable

**Symptom.** `services.AddLyntai(b => b.AddMemoryEngine("project", e => e.UseLexical()))` — exactly one
engine — then `factory.Get()` threw `KeyNotFoundException` "No engine named 'default', and 2 are
registered", against the interface doc "the one named 'default', or the only one when exactly one is
registered".

**Root cause.** The fallback checked `_byName.Count == 1`, and the index deliberately holds a composite's
MEMBERS alongside it (so `"project"` and `"project/lexical"` are two entries for one engine). Every
standard registration wraps in a composite with at least one member, so the only way to reach the fallback
was to name the sole engine `"default"` — where the first branch already answers.

**Fix.** The factory tracks top-level registrations apart from the index; the fallback (and the refusal's
count) reads engines, not index entries.

**Verification.** `MemoryEngineRegistrationTests.A_single_engine_under_any_name_is_the_default_the_parameterless_Get_returns`
(red with exactly the reported message) and `Two_engines_and_no_default_still_refuse_the_parameterless_Get_by_the_TOP_LEVEL_count`.

**Introduced by.** The 2.5.0 commit that added named engines; member indexing and the fallback arrived
together, never able to coexist.

---

## 2026-08-17 — concurrent prompt-version saves raced `MAX(version)+1` on Postgres

**Symptom.** Two concurrent `SaveAsync` for one prompt name could both compute the same next version; the
loser threw a raw `PostgresException` 23505 at the caller.

**Root cause.** Under READ COMMITTED, two transactions can read the same `MAX(version)` before either
commits; `UNIQUE(name, version)` then rejects the loser, and nothing retried. The identical race shape is
retried three files away (`PostgresConversationStore.AppendMessageAsync`, bounded 23505 retry) — the
precedent existed and was not applied here. The SQLite half of the reviewed finding was a FALSE POSITIVE,
measured: `BeginTransaction()` issues an immediate transaction, so SQLite serializes the whole save —
which is precisely the pair divergence `storage.md`'s table records for the conversation store.

**Fix.** `PostgresPromptVersionStore.SaveAsync` retries a 23505 in a fresh transaction, bounded, recomputing
against what the winner committed — mirroring the conversation store. SQLite unchanged.

**Verification.** `PromptVersionStoreContract.Concurrent_saves_of_one_name_get_distinct_consecutive_versions`,
wired on all three backends — red on Postgres only (raw 23505), green on SQLite/InMemory before AND after,
which is what measured the false-positive half.

**Introduced by.** The commit that added the prompt version store; the conversation-store retry postdates it
and was never back-ported.

---

## 2026-08-17 — a tied subject list truncated differently on Postgres

**Symptom.** `KnownSubjectsAsync` — the reuse list the annotation model consumes — orders by `COUNT(*)`
with the subject as tie-break. On a locale-collated Postgres (the Docker default), ties sorted under the
locale while SQLite (BINARY) and InMemory (`StringComparer.Ordinal`) sort byte-wise — so with more tied
subjects than the limit, WHICH handles survived differed by backend, silently, worst for CJK.

**Root cause.** The tie-break lacked `COLLATE "C"` — the one text ordering in the graph-store family
without it. The sibling stores (`PostgresVectorStore`, `PostgresCuratedMemoryStore`) carry it with comments
stating the rule; the existing subject test asserted only survivor COUNT, so the divergence was
unobservable.

**Fix.** `COLLATE "C"` on the tie-break, with the family's standard comment.

**Verification.** `MemoryGraphStoreContract.Tied_subjects_order_and_truncate_byte_ordinally`, driven on all
three backends with a pair a locale reverses (`zeta` / `état`) — red on Postgres only.

**Introduced by.** The commit that added the subject pair; the sibling precedent predates it.

---

## 2026-08-17 — one store-wide gate serialized every job's step reporting

**Symptom.** Not a crash: `ReportStepAsync` on both relational job stores held a single `SemaphoreSlim`
across two database round-trips, so concurrent jobs' step reports queued behind whichever got there first —
real contention on Postgres under parallel agentic handlers, against `JobRunner`'s own promise that
parallel lanes truly run in parallel.

**Root cause.** The read-modify-write on the capped step log needs serialization per JOB (cross-process
interleaving is already safe via the fenced write); the gate was per store because that was the smallest
thing that was correct, and nothing measured the collateral serialization.

**Fix.** `Lyntai.Storage.KeyedLock<TKey>` (new, beside `JobStoreSql`, reusable by BYO relational backends
the same way): an async lock per key with the table bounded by keys in flight — the same shape
`ProviderAdmission`'s gate table carries. Both stores key it by job id.

**Verification.** `KeyedLockTests` (peak-overlap property for same-key, deterministic overlap for
different-key, bounded table, cancelled-wait hygiene) and
`SqliteJobStoreTests.Step_reports_for_DIFFERENT_jobs_do_not_serialize_behind_one_gate`, whose clock is
called inside the gate so overlap is observable — red before, and mutation-checked (pinning the key back to
a single value re-reddens it). The mutation check itself tripped the `windows-machine.md` revert trap: the
`git checkout --` that removed the mutation also removed the fix, which had to be re-applied.

**Introduced by.** The commit that added `ReportStepAsync`.

---

## 2026-08-17 — both job backends reported a rejected credential as a failed RENDER

**Symptom.** A host running ComfyUI or fal behind an authenticating proxy, or with a bad key, was told the
render FAILED. That is wrong, unactionable, and expensive in a way the verdict taxonomy exists to prevent:
`Failed` makes the router advance AND take a dead-host strike against a backend whose only problem is a
missing credential, so a deployment one config line from working benches itself instead.

**Root cause.** The typed status was thrown away before anyone could classify it. Both
`ComfyUiProvider.HistoryAsync` and `FalQueueProvider.GetAsync` collapse a failed response into
`$"{(int)StatusCode}: {body}"` and return it as a string, so their fetch paths could not call <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
`GenerationVerdictClassifier.FromHttpFailure` — the entry point the classifier's own doc names as better <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
("typed status wins over body text"). ComfyUI then hardcoded `GenerationVerdict.Failed`; fal called <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
`FromErrorText`, which reads a `"401: …"` status line as ordinary prose and also answers `Failed`.

**Why nothing caught it, and the interesting half.** The divergence was already WRITTEN DOWN, in `TASKS.md`'s
then-`Startable` section (closed as `docs/task-archive.md` Part 87) — and its description was wrong. It recorded ComfyUI as hardcoding `Failed` "while <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
`FalQueueProvider` routes the same class of failure through `GenerationVerdictClassifier`", i.e. fal was <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
believed correct. Nobody had run it. This is the second time in two days that a claim recorded in a
maintained file, by someone who had read the code, turned out false when measured.

**Fix.** Both readers carry `HttpStatusCode?` back, and both fetch paths classify with
`FromHttpFailure(code, body, hasCredentials)`. fal passes `hasCredentials: true` (it has a key that was
rejected → `AuthFailed`); ComfyUI passes `false`, because it has no credential surface at all, so a 401 can
only mean something in front of it wants one → `NotConfigured`, the verdict that tells a host to go and set
it up. The poll paths are untouched: their transport/terminal split is a separate, deliberately different
rule.

**Verification.** `GenerationProviderContract.An_authentication_failure_is_classified_rather_than_flattened`,
driven on every HTTP backend's verdict-bearing doors. It failed for BOTH job backends before the fix.
A first draft of the fetch fact passed a bare operation id, which fal rejects before calling out — so it
failed for the wrong reason and would have "confirmed" the defect without reaching it.

**Introduced by.** The commits that added each backend; neither ever classified this door.

---

## 2026-08-17 — `generate_backends` could block an agent for ~20 minutes, and one bad backend took the listing

**Symptom.** The tool an agent is told to call FIRST hangs. With two HTTP backends that accept a connection
and then stall, it returns nothing for about twenty minutes; with one BYO backend that throws from
`ProbeAsync`, it returns nothing at all — the exception reaches the tool loop, and the listing of every
healthy backend is lost with it.

**Root cause.** `GenerationBackendsTool.InvokeAsync` awaited `ProbeAsync` on every registered provider in
sequence, with no aggregate bound and no `try`. Each probe was capped only by that backend's own `Timeout`,
and the same option governs a RENDER — `Automatic1111Options` and `OpenAiImageOptions` both default it to ten
minutes, correctly, since a render routinely outlives `HttpClient`'s own default. Every backend disclosed its
timeout honestly; the COMPOSITION disclosed nothing, and serial execution multiplied it.

**Why nothing caught it.** No test registered a backend that stalls, and the per-backend suites each assert
their own probe in isolation — the failure is a property of the composition, which nothing owned.
`MediaRouter` names itself the trust boundary for a backend that throws instead of returning a verdict
(**D64**); this is a SECOND reader of the same registered collection and applied none of it. `pitfalls.md`
§"Second doors" again.

**Fix.** Probes run concurrently under one `MediaOptions.ProbeDeadline` (new; 20s, non-positive means
none), each wrapped so a throw or an overrun becomes `usable: false` WITH the reason rather than an
exception or an omission — omitting the backend would tell the model it does not exist, which is a different
and worse answer than "it is not answering". The caller's own cancellation still propagates, told apart from
the deadline the way `GenerationDeadline` does it.

**Verification.** `GenerationBackendsToolTests` — seven facts covering the deadline binding, the stalled
backend still being listed, a throwing probe becoming an observation, concurrency (asserted on the clock),
caller cancellation, and the no-deadline escape.

**Introduced by.** The commit that added `AddGenerationTools`.

---

## 2026-08-17 — `SalienceRetentionPolicy` could return `NaN` from a method that promises a bounded multiplier

**Symptom.** None observable, and that is the whole entry: `ModulatedRetrievability` coerces a non-finite
factor to 1, so no ranking and no prune ever saw it.

**Root cause — and it is a DUPLICATED READ, not a missing check.** `MemorySignals.Salience(in MemorySignals)`
exists precisely to be the one coercion every reader shares, and it already handled non-finite values
(`double.IsFinite(raw) ? Math.Max(1, raw) : 1`). Its own summary says **"every read site calls this"**, and
names the three that do — the two SQL stores' promoted `salience` column, the in-process store's admission
ordering, and the engine's rank boost — because they "once normalized the same value three different ways,
which made identical data admit differently on different backends".
`SalienceRetentionPolicy` was a FOURTH read site that spelled it out itself, as
`Math.Clamp(signals.Get(Salience, fallback: 1), 1, MaxSalience)` — and `Math.Clamp` PROPAGATES `NaN` rather
than clamping it. A `NaN` salience is reachable: the bag is written by `IMemorySaliencePolicy`, a PUBLIC and
PLURAL seam, and a row stored before `SalienceOptions.NoveltyWeight` was guarded may already hold one.

**Why it is worth fixing anyway.** The decorator's clamp is documented as defence against a BYO policy being
dishonest — "soundness must not depend on an implementation being honest about itself" — and a SHIPPED policy
must not be the thing it is defending against. `NoveltyWeight`'s own doc had already stated the rule this
violates: **the guard belongs to the VALUE, not to whoever reads it last.**

**Fix.** Read through `MemorySignals.Salience` and apply only this policy's own ceiling on top
(`Math.Min(MemorySignals.Salience(state.Signals), MaxSalience)`), which restores the helper's "every read
site" claim to being true.

**Verification.** `MemoryRetentionPolicyContract.The_factor_never_exceeds_the_declared_maximum`, whose state
set includes a `NaN` salience signal. It failed before the fix, naming the value and the declared bound.

**Introduced by.** The commit that added the retention seam; the read side never had the guard.

---

## 2026-08-16 — a configured generation budget could never fire, because the second door never billed

**Symptom.** None visible. `IUsageTracker.TotalAsync()` under-reported by the full cost of every
tool-driven async render, and a `Budget.PerConsumer` cap on those renders never triggered — so the
observable behaviour was "the cap is generous", which looks like working software.

**Root cause.** `GenerationFetchTool.InvokeAsync` called `backend.FetchAsync` directly — correct in itself,
since nothing remains to route once an operation id exists — and passed `result.Usage` to the artifact sink
without recording it. `GenerationRenderJobHandler`, the OTHER consumer of the same fetch, bills it. A queue
backend prices at fetch because that is the only point the total is known, so the unbilled path was the one
carrying the money.

The compounding half: `GenerationSubmitTool` re-checks the cap on every submit, against a total the fetch
never grew. So an agent looping submit → status → fetch spent without bound underneath a cap that was
configured, enforced on paper, and read a number that could not move. Within ONE `AddGenerationTools()`
registration, the inline `generate` tool billed correctly and the async pair did not.

**Why nothing caught it.** `GenerationToolsTests` already exercised submit → status → fetch end to end and
asserted nothing about usage — the path was covered, the accounting was not. `BudgetedMediaRouter`
names the job handler as *the* recorder for the submit path and never mentions that `AddGenerationTools`
ships a second fetch door. Textbook `pitfalls.md` §"Second doors".

**Fix.** `GenerationFetchTool` takes an optional `IUsageTracker` and a `consumer` (defaulting to the
`"agent"` tag its sibling tools already use) and records before delivery, matching the job handler's own
documented ordering — the money is spent either way, and a sink that throws would lose the record.

**Verification.** A new test configures `AddMediaUsageBudget` and drives submit → fetch against a
backend that prices the render at $0.50. It failed with `Expected: 0.5, Actual: 0` before the fix.

**Introduced by.** The commit that added `AddGenerationTools`; the asymmetry has existed for as long as both
doors have.

---

## 2026-08-16 — three storage invariants were written down for ONE backend and held by no contract

**Symptom.** None observable, and two of the three could not be observed by any test in the suite. The
third was observable only as "the same call destroyed a memory on one backend and kept it on the other two".

**Root cause.** One shape, three times: an invariant was DOCUMENTED, in prose, on the backend that first
needed it — and never promoted to the cross-backend contract, so the other implementations were free to
disagree and nothing asked them.

1. **Vector top-k had no tiebreaker on either persistent backend.** `InMemoryVectorStore` carries
   `.ThenBy(m => m.Id, StringComparer.Ordinal)` and a doc calling that tiebreak *"load-bearing, not
   tidiness … the same defect `storage.md` records for an `ORDER BY` on a non-unique column"* — and it was
   the only backend that had one. Its test lived in `InMemoryVectorStoreTiebreakTests`, a single-backend
   file, rather than in `VectorStoreContract` beside the seven facts all three backends run. Ties are not
   hypothetical: `VectorMath.Cosine` returns exactly `0` for a zero vector or a dimension mismatch, and
   identical embeddings score exactly equal. The blast radius is permanent rather than cosmetic — those
   top-k hits become similarity EDGES and a novelty→salience score, so an arbitrary tie-break is written
   into the graph rather than merely displayed.
2. **The zero-stability floor had a third copy with different SEMANTICS.** `MemoryGraphSql.MinimumStability`
   was hoisted for exactly this reason, its own doc saying *"a floor that differed between them would make
   the same corpus prune differently on each, and two literals is how that happens"* — and
   `InMemoryMemoryGraphStore` was the second literal, spelling `stability > 0 ? stability : 1e-6`
   (SUBSTITUTE) where SQL spells `MAX`/`GREATEST` (FLOOR). Identical at stability `0`, which is the value
   the guard was written for, and different for every value strictly between zero and the floor — on the
   DELETE path. Stability `1e-7`, age 1, cutoff `5e6`: kept on both relational backends, deleted in process.
3. **`Math.Max(0, x)` was standing in for a finiteness guard on the WRITE path.** `Math.Max` PROPAGATES
   `NaN` per IEEE 754. `IMemoryAgePolicy.Age` documents at length that a non-finite return cannot corrupt
   the store because *"everything downstream that would otherwise persist the poison"* is defended — true
   of `Age`, which is never persisted, and false of `Advance`, whose result IS. A BYO policy returning
   `NaN` poisoned the Postgres position column permanently (every later entry then reports a non-finite
   age, so nothing ranks and nothing prunes) while SQLite refused the bind and failed loudly — the two
   backends disagreeing about a poisoned write, which is worse than either answer alone.

**Fix.** Promote the invariant to the contract in each case, rather than patch the backend that lacked it.
The three tiebreak facts moved into `VectorStoreContract` (and the single-backend file was deleted, not
left as a second door); `MemoryGraphSql.MinimumStabilityValue` derives the number from the one literal and
the in-process store floors through it; `GraphMemoryEngine.Ordinary` coerces a non-finite tick component to
`1`, which `MemoryTick.One` already defines as an ordinary write rather than being a value invented here.

**Verification.** The tiebreak facts fail on Postgres before the fix (`["id-15","id-14","id-16"]` for a
query whose answer must be `["id-01","id-02","id-03"]`) and pass on all three after. SQLite PASSED the
tiebreak facts before the fix — worth recording, because it passes for a reason nobody chose: `PRIMARY KEY
(collection, vec_id)` gives it an autoindex the plan happens to walk in `vec_id` order, which a rewrite,
an `ANALYZE` or a different plan changes silently. A new contract fact,
`A_stability_under_the_floor_is_floored_not_substituted`, was confirmed to fail on the in-process backend
with the fix reverted and to pass on all three with it restored; because `MemoryGraphStoreCoverageTests`
makes contract coverage structural, it enrolled on all three backends without being wired anywhere.

**Introduced by.** Each at the commit that added its backend; none is a regression from a working state.

---

## 2026-08-16 — a comment sweep stranded punctuation inside XML docs that ship to consumers

**Symptom.** Two public members' IntelliSense read *"…reads as the neutral 5 instead ( was the floor 1 —
see…)"* and *"…field instead . This exists so…"*. Confirmed present in the generated
`Lyntai.Core.xml`, so it reached consumers rather than stopping at the source.

**Root cause.** A sweep deleted work-log parentheticals — `corrected 2026-08-11;` from inside a `(`, and
`(2026-08-10, plan Task 2)` — and left their surrounding punctuation behind. The deletions were correct;
the edits were not complete.

**Why nothing caught it.** Every gate was green and none of them could have been otherwise:
`check-comments` measures block LENGTH, `check-docs` scans prose files and deliberately excludes `src/`,
`check-encoding` looks for mojibake, and the compiler has no opinion about a sentence. A person reading the
diff found them. That is the same shape as `check-encoding`'s own origin — a rule that is written down and
still violated is a missing gate, not a knowledge problem.

**Fix.** Repaired both sentences, and added a stranded-punctuation rule to `check-comments`.

**Verification.** Both rules are pinned by the ACTUAL shipped text as fixtures, and mutation-checked
against the repaired version so they match the defect rather than its neighbourhood. The narrow forms were
chosen by measurement: across `src`, `tests`, `devtools` and `bench` the obvious broad rules ("a comment
line starting with punctuation", "a line ending in `(`") give 5 hits and 0 defects — a `.cmd` extension, a
wrapped `: 1e-6</c>)`, a wrapped `services.AddHttpClient(`, two ellipsis continuations — while the shipped
rules give 0 hits and still fire on both real defects. The distinguishing feature is the SPACE: a deleted
parenthetical leaves `instead (`, a wrapped call leaves `AddHttpClient(`. There is deliberately no escape
token, because nothing in the tree legitimately matches and an allowance would be a hole opened for a case
that does not exist.

**Introduced by.** `0c1e703`, the comment/cref sweep, this session.

---

## 2026-08-16 — the release notes could not see a breaking change, on the eve of a major release

**Symptom.** None observable, which is the point: the generator ran green every release and published a
plausible document. Found by asking a question the pre-release checklist did not contain — *can this
workflow actually cut a 3.0, and what will it say?*

**Root cause.** `.github/workflows/release.yml` categorized commits with `^feat(\([^)]+\))?:` and
`^fix(\([^)]+\))?:`, dropping `^(chore|docs|refactor|style|perf|test|ci|build)(\([^)]+\))?:` as
non-user-facing. A complete-looking vocabulary that omits conventional commits' one modifier — the
BREAKING `!`. `feat(memory)!:` matches none of the three (each expects `:` where the `!` sits), so every
breaking change fell to the catch-all headed **"Other changes"**: 29 commits of history, and **11 of 11**
in the `v2.5.0..HEAD` range. The 3.0 notes would have read *"New features: 1"* above a bucket holding
every change that breaks a consumer's build.

Its sharpest form: plain `refactor:` is *dropped*, so a breaking refactor — the most important line a
consumer can read — was one regex away from deletion rather than misfiling. **The only reason it survived
is that the drop pattern also failed to match the `!`.** Correctness rested on a second rule failing.

Two things kept it alive. It lived as ~40 lines of inline `pwsh` in a YAML step, so `test-devtools` — which
runs FIRST in `verify` precisely because a component whose failure mode is a false PASS cannot be validated
by running it — had no seam to reach. And its failure mode is *plausible output*: nothing errors, nothing is
dropped, no count disagrees, every commit appears somewhere.

**Fix.** Extracted to `devtools/scripts/release-notes.mjs` behind pure functions (`categorize`,
`previousTag`, `renderNotes`), with the workflow step reduced to one `node` call — the shape this
repository already uses for every guard. Rules: the `!` **outranks the kind**, so a breaking `refactor!:`
is kept while `refactor:` is dropped; breaking changes lead the body and carry their scope, because "which
package breaks" is the first thing a reader needs; `bench` and `tasks` joined the non-user-facing list from
a census of the real history. `node devtools/dev.mjs release-notes --tag vX.Y.Z` previews it — the notes
are the one release output a consumer reads, and nothing could look at them before publication.

**The honest limit, stated rather than discovered.** Breaking-ness is read from the `!` in the SUBJECT.
Conventional Commits also allows a `BREAKING CHANGE:` body footer, and this reads `%s`; a commit using only
the footer is categorized by its kind. Every one of this repository's 29 breaking commits uses `!`.

**Verification.** 16 tests, mutation-checked — neutering the `!` branch fails 6 of them, including the
regression case. The last is pinned against the REAL log as a property ("no breaking commit lands in Other
or is dropped") rather than a count, since a count fails on the next commit. `test-devtools` 329 → 345,
`verify` 14/14. Previewed against the actual range: all 11 breaking changes now lead the notes.

**Commit that introduced it.** The generator has had this shape since the workflow was written; the `!`
convention arrived later and nothing revisited the categorizer, which is the ordinary way a vocabulary list
goes stale.

---

## 2026-08-16 — two round-2 findings that were recorded rather than fixed, closed

Round 2 raised both and the pass logged them as observations. Both are the same species as the defects the
review existed to find, so leaving them written down would have been the failure `check-encoding` and
`check-links` were each created after: **a rule that is written down and still violated is a missing gate,
not a knowledge problem.**

**1. D63's constraint was a sentence in a document.** Its remedy for the composite silently dropping
`IForgettableMemory` read *"when a capability interface is added anywhere in this library, the wrapper over
it gets a line in the same change"* — while its own diagnosis of the original defect was that **a comment
asserting an invariant is not the invariant**. The class docblock had claimed "It never guesses about
capabilities" while implementing two of three, and nothing but that sentence stopped the fourth capability
going the same way.

*Fix.* `The_composite_implements_every_capability_interface_its_richest_member_does` derives the expected
set by reflection — every interface `GraphMemoryEngine` implements, `CompositeMemoryEngine` must implement
— so it cannot go stale the way a hand-listed set would. The graph engine is the right yardstick because it
is the richest member and the one every `UseGraph` blend wraps. Mutation-checked by removing
`IForgettableMemory` from the composite's declaration while keeping its members, which is exactly the shape
that shipped: the fact fails, and no other test does.

**2. `check-links`' escape unit did not match its match unit.** The Part half was given a soft-joined
two-line window earlier in this same review, and the `link-ok` check was left reading one line. So an
annotation on line *i+1* — the line where a reader actually SEES `Part 53` — was invisible, and the gate
would fire on prose somebody had deliberately annotated. The fix a maintainer reaches for in that situation
is duplicating the token onto both lines, which is how an escape quietly becomes noise.

*Fix.* `link-ok` on either line silences the pair, which is the rule `check-docs` carries in the same breath
as its own window — `pitfalls.md` states it verbatim, *"drift-ok on either line silences the pair"*, and
this gate copied the window without it. **Copying a rule copies its assumptions; here it copied the
mechanism and left the half that makes it usable.** Three guard tests: an annotation on the second line, on
the first, and — the control that stops the widening becoming a hole — an unannotated wrapped reference
still failing.

**Verification.** `test-devtools` 326 → 329, `verify` 14/14, 2,932 passed / 2,953. Latent when found, both
of them: no defect was live in the tree. That is the point at which a gate is cheapest to fix and the point
at which it is easiest to leave alone.

---

## 2026-08-15 — round 2 of the pre-3.0 review: four regressions round 1 introduced, and the reasoning error under one of them

**Symptom.** All fourteen gates green, 2,923 tests passing, e2e 3/3 — over four behaviour regressions and one
false claim. Round 1's own author verified every finding it acted on and still shipped these, which is the
case for having a round 2 at all: this repository's last comparable pass found five the same way.

---

**1. A rate limit dead-lettered an already-paid render.** `FalQueueProvider.GetAsync` classified
`transport = status >= 500`, copied from `ComfyUiProvider.HistoryAsync`. `GenerationRenderJobHandler` turns a
`Failed` poll into `JobOutcome.Fail`, so ONE `429` from fal permanently dead-lettered a render that was still
running and already billed — as would a `401` mid key-rotation, a `403` WAF challenge, or a `408`.

*Root cause is the copy, not the code.* ComfyUI is a loopback server that never rate-limits; fal is a hosted,
paid, rate-limiting API. **A rule copied between two backends inherits the assumptions of the one it came
from**, and those were never stated because on ComfyUI they are always true. Terminal now means a `404` (the
queue does not know this id) or the unconfigured pre-check, and nothing else. The new tests covered `404` and
unconfigured only — the two regimes where terminal IS right, which is `pitfalls.md`'s "pick fixture values
from the regime where they DISAGREE" exactly.

**2. A transport blip dead-lettered a durable job.** `MediaRouter.SubmitAsync`'s new catch mapped EVERY
throw to `Inconclusive`, which surfaces, which the handler fails. Before round 1 the throw propagated and
`JobRunner` retried it. A connection-refused during a deploy went from "retries and succeeds" to "dead-
lettered, permanently". The duplicate-charge reasoning was right for an ambiguous failure and wrong for one
that provably never left the process — **the same distinction round 1 taught `FalQueueProvider` one file
over, and did not apply to its own catch.** `NeverReachedTheBackend` now decides it, and such a throw
propagates untouched.

**3. Narrowing removed a capability, on a misreading of the contract.** Round 1 confined SQLite's FTS
expression to `content` so the three backends would agree, reading `IMemoryGraphStore.SeedAsync`'s portable
guarantee as content-only. **That guarantee states a MINIMUM** — *"a node whose content contains … is found
on every backend"* — not a ceiling. SQLite was not exceeding a contract; it was the only backend that could
match an authored `MemoryWrite.Headline`, which is a summary a caller writes precisely so the entry can be
found by it. After the fix, a headline-only match was found by no path on any backend, and `memory-sweep`
could not have seen it because no corpus entry authors a headline disjoint from its content.

Converged by WIDENING instead: `SearchTerms.LikeClause` gained a multi-column overload (per-column
disjuncts, so each trigram index stays usable), Postgres gained `M202608152310_MemoryHeadlineSearch`, the
in-process store reads both, and SQLite's expression is unconfined again. The contract fact is inverted and
still runs on all three backends. **The lesson is about the reading, not the code: a floor and a ceiling look
identical in a sentence that starts "is found".**

**4. The composite removed SOME members and reported success.** `PruneAsync`/`ForgetAsync` fanned out to
whichever members implemented `IForgettableMemory` and silently skipped the rest — the exact outcome the
fan-out exists to prevent, written one paragraph above it. Only `GraphMemoryEngine` implements it, so
`UseCurated("glossary").UseGraph()` — a blend from this library's own README — cleared the graph half, kept
the AUTHORITATIVE half, and returned normally. For the call an application makes when a user withdraws
consent, that is the worst available answer.

**And round 1's own test pinned it**: it put a non-forgettable member in the blend and asserted success. The
composite now refuses unless every member can remove, **checked before anything is removed** — a mid-fan-out
refusal would be a partial remove AND an exception. This takes nothing away: through 2.5.x the interface was
unreachable through a composite at all.

---

**Verification.** Nine new tests across the four, each pinning the regime the original fixture missed: four
retryable fal statuses; both directions of the submit-throw distinction; the headline fact inverted on three
backends; and two composite facts asserting that the capable member is NOT removed when a sibling cannot be.
`verify` green at 14/14, 2,930 passed / 2,952, e2e 3/3. The Postgres schema golden was regenerated and its
diff reviewed line by line: exactly one index added.

**What generalises, and it is not "review harder".** Three of the four are one shape — **a rule moved from
where it was true to where it was not**: ComfyUI's status policy onto fal, a contract floor read as a
ceiling, and a fan-out justification that stopped applying the moment a member could not serve it. The
fourth is the same shape aimed inward: round 1 wrote the transport-versus-ambiguous distinction and then did
not apply it to the code it was writing at the time. **Copying a rule copies its assumptions, and the
assumptions are the part nobody writes down** — because where the rule came from, they were always true.

---

## 2026-08-15 — `check-links` was green over a stale reference in the design CONTRACT, because two blind spots cancelled

**Symptom.** `check-links` reported *"every in-repo reference resolves ✓"* while
`docs/2026-07-17-lyntai-design.md` — the document this repository tells a reader to open first — said a
shipped router rule was *"the open call — `TASKS.md` **Part 40**"* and *"must not be revisited on its own <!-- link-ok: QUOTING the defect this entry is about -->
before it lands"*. Part 40 landed and lives in the archive. This is precisely the defect the gate's Part
half was built for.

**Root cause — two of them, with opposite signs.**

1. **The Part half matched one line at a time.** `check-docs` builds a soft-joined two-line window and its
   comment generalises the rule — *"the unit you match must be the unit the claim is written in… worth
   carrying to any future text gate."* `check-links` was written **three days later** and did not carry it.
   These documents wrap at ~110 columns and a Part reference spans a backtick, a filename and a bold
   marker, so it is among the likeliest claims to straddle a break. This one did: line 804 ended
   `` `TASKS.md` `` and line 805 opened `**Part 40**`.
2. **`declaredParts` read `##`/`###` headings only.** Parts 40 and 42 are filed in the archive as
   `- [x] **Part N — …**` list items, so both were invisible to the record scan.

**The cancellation is the interesting part.** (1) is a false NEGATIVE and (2) a false POSITIVE. Had the
reference not wrapped, (2) would have reported it as *"in NEITHER record"* — wrong, but loud. Had the Parts
been headings, (1) would still have hidden it. **Fixing either alone surfaces the defect; having both kept
the gate quiet for a release.** A measured instance of the shape `pitfalls.md` records for filter chains: a
clean run proves nothing about the stage you did not check.

**Fix.** The Part half matches a soft-joined window, keeping only matches that BEGIN in the current line so
a reference is never double-counted; `declaredParts` recognises the list-item shape alongside both heading
levels. The path half deliberately keeps `line` as its unit — a target is a single token and cannot wrap
mid-name.

**Verification.** Three guard tests — a wrapped reference is caught, a bullet-declared Part counts as
present, and a Part declared in NEITHER shape is still reported (the control that stops the widening making
the check vacuous). On the real tree the fixed gate immediately found three references: the design
contract's, now corrected, and two in `CLAUDE.md`/`CHANGELOG.md` that quote `` `TASKS.md` Part 53 `` as an <!-- link-ok: quoting the illustration, same reason those two carry it -->
ILLUSTRATION of what the gate matches — prose about data, now annotated `link-ok`, which is the case the
gate's own header describes.

**Introduced by.** `check-links` itself (2026-08-14), which was built from the lesson of one incident and
not from the lesson written down for the gate before it.

---

## 2026-08-15 — two of the fourteen `verify` gates had no CLI-entry test, in the file whose premise is that all of them do

**Symptom.** Latent. `cli-entry.test.mjs` exists because a guard whose entry wrapper stops matching
*"starts, scans nothing, prints nothing, and exits 0"* — a green line over an unscanned tree. It drove
eleven of the thirteen scripts that carry such a wrapper; `check-counts` and `check-encoding` were absent.

**Root cause.** The roster is hand-written, one `it(...)` per script, with nothing deriving it from the
scripts on disk. `check-counts` was added by `273d4e0` — the sweep that fixed seven other gates for
reporting success without having checked — and was not entered into it.

**Why these two matter most.** Both are `verify` steps, and `check-encoding`'s own header states that
mojibake *"is silent by construction, so no other gate can see it"*: a dead entry point there means nothing
is scanned for it in `verify` **or** in the pre-commit hook, with a tick printed either way. The exported
`checkCounts`/`checkEncoding` functions are unit-tested and would keep passing throughout.

**Fix.** Both added to the roster. **Not** derived from disk, deliberately: several scripts need arguments
or a hostile environment to produce observable output (`--tree`, `--list`, `LYNTAI_RELEASE=1`), so a
derived roster would either invoke them wrongly or need a per-script exception table — which is the same
hand-maintained list with an extra step. Recorded so the next reader does not re-derive the tradeoff.

**Verification.** `test-devtools` 321 → 326.

---

## 2026-08-15 — a headline-only query found the fact on SQLite and nowhere else

**Symptom.** A node written with an authored headline carrying a word its content does not contain was
returned by `SeedAsync` on SQLite and not on Postgres or the in-process store. Same call, same data, three
backends, two answers.

**Root cause.** `lyntai_memory_node_fts` declares `fts5(headline, content, …)`, and an unconfined FTS5
expression matches ANY indexed column — so SQLite's FTS branch matched headlines. Postgres's GIN index is
`gin (content gin_trgm_ops)` and `InMemoryMemoryGraphStore.Matches` reads `node.Content`; both are
content-only, as is SQLite's own LIKE fallback. So SQLite also disagreed with ITSELF depending on whether
the trigram index happened to hit.

**Why this is a defect and not a divergence.** `storage.md` states the rule: *an ORDERING difference between
backends is a divergence; a different answer to "is the fact found" is a defect.* And
`IMemoryGraphStore.SeedAsync`'s own portable guarantee is written content-only — *"a node whose CONTENT
contains a single ≥3-character query token as a substring is found on every backend"* — so SQLite was the
outlier EXCEEDING the contract rather than the other two falling short of it.

**Fix.** `FtsQuery.Build` takes an optional column and emits `{content} : (…)`; the graph store passes
`content`. No migration, no index, no released schema touched. **The parentheses are load-bearing**: an
FTS5 column filter binds tighter than `OR`, so `content : "a" OR "b"` would confine only the first term —
correct-looking in a single-term test and wrong for every real multi-token query.

**Deliberately NOT done: widening the other two to match headlines.** That is a legitimate, additive change
and a different decision — it needs a Postgres index on `headline` and a recall-quality measurement, and a
divergence fix should not smuggle in a behaviour change nobody measured. If it lands, the contract fact
below is what has to be rewritten on purpose.

**Verification.** Two facts on `MemoryGraphStoreContract`, so they run on all three backends by
construction: a word only in the headline matches nowhere, and — the control that keeps the first honest —
a word in the content matches everywhere. Mutation-checked by removing the confinement: **exactly one of
the 157 cases fails, SQLite's**, which is the proof that the divergence was one-sided and the fix is
targeted.

**Introduced by.** The 2.5.0 memory-graph migration, which indexed both columns while the contract promised
one.

---

## 2026-08-15 — the in-process stores ranked by recency where their own interface docs promise match count

**Symptom.** `RecallAsync(task, "deploy pipeline", limit: 1)` returned a different ENTRY on
`InMemoryMemoryStore` than on SQLite or Postgres: an entry matching one term displaced one matching both,
simply by being newer.

**Root cause.** Both SQL stores order by `{kw.MatchCount} DESC, created_at DESC, id DESC`. The two
in-process stores ordered by `CreatedAt`, `Id` only — while `IMemoryStore.RecallAsync`,
`ICuratedMemoryStore.SearchAsync` and `storage.md` all state that Postgres **and InMemory** rank by matched
terms then recency. With a `limit`, an ordering difference becomes a different answer.

**Fix.** `SearchTerms.MatchCount` — the in-process twin of the `MatchCount` expression `LikeClause` already
builds for SQL — used by both in-process stores. Put beside the tokenization that produced the terms on
purpose: a store that splits a query with `SubstringTerms` and then counts matches its own way is two rules
for one question, which is the shape `MemorySignals.Salience` exists to prevent. With no query every count
is 0, so the documented "most recent first" is unchanged.

**Introduced by.** D55's tokenization convergence, which made admission agree across backends and declared
ranking backend-specific — but declared InMemory's ranking to be term-count, which it never was.

---

## 2026-08-15 — the Postgres graph store opened every connection synchronously, alone among twelve

**Symptom.** No wrong data; a liveness defect. Under concurrent recalls each call blocked a thread-pool
thread for a whole TCP connect plus authentication.

**Root cause.** `PostgresMemoryGraphStore` used `factory.Open()` at all **fourteen** sites, plus a
synchronous `BeginTransaction`/`Commit`. Every other Postgres store in the package uses
`await factory.OpenAsync(ct)`, and `PostgresUsageTracker`'s own docblock states the reason: a network
round-trip inside the async front door must not block a pool thread. On a cold pool, or one at
`MaxPoolSize`, this is thread-pool starvation rather than a slow query. `SeedAsync` runs on every recall.
The cancellation token also could not reach the connect, since `Open()` takes none.

**Fix.** All fourteen converted to `await using … await factory.OpenAsync(ct)`, with the transaction pair
converted alongside; the class docblock now states the rule and why the SQLite twin may open synchronously
(its "connect" is a file handle).

**Verification.** The full Postgres leg, 172/172 green against a real container, and a clean build with zero
warnings. No behavioural assertion is possible for the liveness property itself — stated rather than papered
over.

**Introduced by.** The 2.5.0 graph-store work, which was written from the SQLite store's shape rather than
from its Postgres siblings'.

---

## 2026-08-15 — the hosted MCP endpoint ran the app's tools with no guard gating, reopening a hole closed for the tool loop

**Symptom.** An application that registered an `IGuard` had it enforced when the model drove a tool through
`IToolLoop`, and silently not enforced when a CLI's own agent invoked the same tool through the hosted MCP
endpoint. No error, no log: the tool ran and its output went straight back to the model.

**Root cause.** `ToolFunction` — the `ITool` → `AIFunction` bridge the host exposes — took only the tool and
called `tool.InvokeAsync(...)` directly. `ToolLoop.GatedInvokeAsync` wraps the identical call in
`InspectToolCallAsync` / `InspectToolResultAsync`, and its own docblock states why: *"the tool-loop guard
hook (guards otherwise only cover the chat boundary, not model-driven tool calls)"*. Both paths receive the
same instances from `sp.GetServices<ITool>()`. Neither `ChatOrchestrator` gate covers it either — gate 1 saw
the user message, gate 2 sees only the final answer.

**Why it survived.** `docs/task-archive.md` records item R2, *"Guards don't cover the agent tool loop
(security)"*, closed 2026-07-20 — for `ToolLoop`. MCP hosting shipped later, as a separate package, and
nothing asked the same question of it. A grep for `guard` across both MCP packages returned one unrelated
comment.

**Fix.** The rail is resolved optionally in `AddMcpToolHost` and travels with the tools into
`McpToolHost.StartAsync` and on into `ToolFunction`, which now gates both ways. A Block cannot abort the
CLI's own agent loop the way it aborts this library's, so it is reported to the model as a refusal
observation — the tool does not run and its payload is never produced, which is the enforcement available
from this side. With no rail registered, nothing changes.

**Verification.** Three facts in `McpToolHostTests` driven through a real in-process host and Lyntai's own
MCP client: a blocking rail leaves the tool unexecuted and the payload absent; a replacing rail redacts the
observation; and a host with no rail behaves exactly as before. Mutation-checked — disabling either gate
fails the corresponding fact. **The first mutation attempt reported "applied" and the tests still passed,
because the replacement string never matched**; asserting on the intermediate (the count of sites replaced)
is what caught that, and it is the same lesson `pitfalls.md` records for filter chains.

**Introduced by.** The MCP tool-hosting work, which added a second door onto the tools without asking what
already guarded the first.

---

## 2026-08-15 — MCP tool-host args landed in codex's PROMPT, where a swallowed flag is a spent turn
 <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
**Symptom.** An app registering `AddCodexCliProvider()` together with `AddMcpToolHost(...)` spawned <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
`codex exec … - -c mcp_servers.…`, with the config overrides AFTER the `-` stdin positional. Everything
after `-` is read as prompt text, so the tools the provisioner exists to expose were absent and the turn was
spent on a prompt made of config strings.

**Root cause.** `ICliProviderDialect.BuildCompletionArgs` took only the request, so `CliProviderEngine` <!-- drift-ok: the record names the seam of its day; D159 renamed it after -->
appended the provisioner's args after the dialect's argv. That is correct for a CLI whose argv ends in
options and wrong for one that ends in a positional. `CodexExecArgs` already had the right shape — an
`extraOptions` parameter, with a comment explaining that appending afterwards is exactly what must not
happen — and the agent path used it. The completion path had no way to.

**Why it never bit.** `claude` is the only CLI that has driven the tool-hosting path, and its argv ends in
options, so appending happens to be correct there. The second implementation is where a rule that was
really a coincidence shows up.

**Fix.** The seam carries the args (`docs/DECISIONS.md` **D65**); `CodexCliDialect` passes them to <!-- drift-ok: the record names the seam of its day; D159 renamed it after -->
`CodexExecArgs` as `extraOptions`, `ClaudeCliDialect` appends them and says why it may. <!-- drift-ok: the record names the seam of its day; D159 renamed it after -->

**Verification.** `Tool_host_args_land_before_the_stdin_positional_not_after_it` asserts the `-c` index is
below the `-` index, mutation-checked by making the codex dialect append; plus a control that a dialect
given no tool args builds exactly what it always did.

**Introduced by.** The CLI tool-hosting generalization (1.1), which added the provisioner without giving the
dialect a say in where its args go.

---

## 2026-08-15 — an OpenAI-compatible backend that reported its failure at HTTP 200 was re-sent the same request, then blamed for a malformed body

**Symptom.** A gateway answering `200` with `{"error":{"code":429,"message":"Rate limit exceeded"}}` — the <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
shape aggregators and proxies use — produced `LlmVerdict.Failed` with the detail *"malformed or empty <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
response after retry"*, after the identical request had been sent a second time to the host that had just
said it was rate-limited. The operator was pointed at the response shape; the actual problem was quota.

**Root cause.** `OpenAiCompatibleProvider` read the failure channel only when the STATUS was non-2xx <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
(`if (!response.IsSuccessStatusCode) return MapHttpFailure(...)`). On a 2xx the body went to `TryExtract`,
which returns false for a body carrying no `choices`/`message`/`finish_reason` — so an error-only body fell
into the malformed-body path, which retries once and then reports `Failed`. Streaming had the same shape:
`ParseStreamLine` yields nothing for an error-only SSE line, so the stream ended `Failed: no output
produced` — the right verdict CLASS with no reason.

**Why it matters beyond the message.** `Failed` and `RateLimited` route differently: `Failed` ADVANCES and
takes a dead-host strike, `RateLimited` COOLS the host. So a backend answering honestly in band was
penalised toward being benched, and a second billable request was sent first.

**This is the THIRD instance of one class.** The same "two answer channels, precedence pinned on one" defect
shipped in `CliProviderEngine.CompleteAsync` (2026-08-05, this log) and again on the claude agent session
(`8dac87f`). Both sweeps checked the CLI seams; neither checked the HTTP provider.
 <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
**Fix.** `OpenAiHttp.InBandError(body)` — one reader of the in-band channel for every OpenAI-compatible <!-- drift-ok: the PRE-RENAME name this incident was recorded under -->
surface in the package — consulted BEFORE the retry on the buffered path, and on the zero-content path when <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
streaming. The verdict comes from the shared corpus (`LlmVerdictClassifier.FromErrorText`), never a local <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
heuristic, and carries the same `AuthFailed → NotConfigured` promotion `FromHttpFailure` makes, restated
because that overload needs a failed status to key on and this path has none.
<br>Deliberately narrow: it reports only that an `error` member is present and what it says. It does not
read a `code` as an HTTP status — that mapping is not measured across the aggregators this provider serves.
<br>And on the streaming path it is consulted ONLY when no content arrived, because the mirror-image trap is
real and measured: a bare `{"error":"Reconnecting... 2/5"}` appeared in codex runs that went on to succeed,
so treating every error-ish line as terminal kills healthy calls that recovered.

**Verification.** Five facts in `OpenAiCompatibleProviderTests`: the 429 classifies AND `handler.Requests`
holds exactly one entry (no resend); an in-band 401 with no key is `NotConfigured`; the same WITH a key
stays `AuthFailed` (the control that stops the promotion swallowing every auth error); the streamed twin;
and an error line arriving AFTER content leaves a delivering stream alone. The last was green before the fix
and is what keeps the mirror trap closed.

**Introduced by.** The provider as originally written — the status check has gated the failure channel since
it shipped.

---

## 2026-08-15 — a durable render polled a backend that could never answer, forever

**Symptom.** A `GenerationRenderJob` against fal whose key was rotated out of configuration, or whose
request id fal no longer knew, polled every 15 seconds for the life of the process. The job was never
dead-lettered, never failed and never completed; the reason — *"not configured: BaseUrl and ApiKey are both
required"* — sat in `QueuedOperation.Detail`, where nothing acts on it.

**Root cause.** `FalQueueProvider.PollCoreAsync` mapped EVERY `GetAsync` failure to
`QueuedOperationStatus.Running`, and `GetAsync` produces a failure for three unrelated things: an
unconfigured backend, any non-2xx, and any exception. The method's own `<remarks>` justified only the narrow
case — *"the same treatment a transport failure already gets here"* — so the code was broader than the
reasoning written above it. `GenerationRenderJobHandler` reads `Running` as "re-checkpoint and poll again",
which is correct behaviour on an incorrect input.

**Why no test caught it.** The suite pinned the 500 case (`A_transport_failure_while_polling_keeps_the_render
_alive`), which is the regime where the bug is invisible — that case SHOULD report `Running`. No fixture
exercised a 4xx or an unconfigured backend on the poll path.

**Fix.** `GetAsync` now returns a `Transport` flag and `PollCoreAsync` branches on it: a failure that never
reached the queue (5xx, `HttpRequestException`) leaves the render alive; one the queue ANSWERED — a 4xx for
an id it does not know — or a backend with no key at all is terminal. This is `ComfyUiProvider.HistoryAsync`'s
shape verbatim, whose docblock had already reasoned the case through: *"A 4xx or an unconfigured BaseUrl IS
terminal — that id will never resolve, and polling it forever strands the job."* Two sibling backends in one
package answered the same question opposite ways, and only one had written down why.
<br>The fetch path takes the flag and discards it deliberately: a fetch that cannot be completed is a result
the caller acts on now, where a poll is a question that can be asked again.

**Verification.** `A_4xx_while_polling_is_TERMINAL_rather_than_polled_forever` and
`An_unconfigured_backend_polling_is_TERMINAL_rather_than_polled_forever`, with the pre-existing 500 fact
still green — it is the control that stops the fix over-firing into "abandon a render that is still running".

**Introduced by.** `FalQueueProvider` as first written (the 2026-08-04 durable-renders work); the surface is
one of the two GEN-VERIFY names as documented-not-measured, though this half is control flow rather than
wire format and needed no key to settle.

---

## 2026-08-15 — a CLI turn that finished cleanly was reported as a stall, and paid for twice

**Symptom.** A buffered `ProcessRunner.RunAsync` whose child exited `0` at the instant its inactivity (or
max-duration) clock fired returned `ProcessResult(-1, <the complete stdout>, …, Inactivity)`. Because
`CliProviderEngine.CompleteAsync` branches on `result.TimedOut` BEFORE it parses stdout, a complete, <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
successful, already-billed CLI turn was discarded as `LlmVerdict.Timeout` — *"stalled, no output for …"* — <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
and the router fell over to the next candidate and paid for a second turn.

**Root cause.** The buffered path decided on the cancellation flag alone
(`if (killCts.IsCancellationRequested)`). `KillTree` is a no-op against a process that has already gone, so
the flag being set does not mean the kill BEAT the child. `StreamLinesAsync`, forty lines away, asked the
fuller question and named the race in a comment — *"unless the child actually finished cleanly first: the
kill can race a clean exit"* — so the guard existed on one path and was absent from the other for a whole
release. The window widens for any child that closes stdout and then lingers (a flush, an atexit hook, a
telemetry upload) while still exiting `0`.

**Fix.** The decision is now one internal function, `ProcessRunner.TimedOut(killRequested, exitCode)`, called
by both paths — the `MemorySignals.Salience` prescription (`pitfalls.md`: one thing read at N sites grows N
rules) applied to a decision rather than a coercion. Two copies kept in step by review is what produced the
divergence; one function makes it unrepresentable.

**Verification.** A four-case `[Theory]` over the truth table, including the case that was wrong
(`killRequested: true, exitCode: 0 → false`). **The race itself cannot be driven deterministically from
outside the class**, which is exactly why the copy that DID carry the guard never had a test either —
extracting the decision is what made it observable at all. Stated rather than papered over: a timing test
here would be flaky, and a flaky test is worse than none.

**Introduced by.** The buffered path's inactivity-clock work; it has never had the exit-code half.

---

## 2026-08-14 — a recall returned more entries than its own `Limit`, because a per-ENGINE bound was never reconciled with a per-QUERY one

**Symptom.** `RecallAsync` returned three items for `Limit: 2`, and none of them was an ordinary hit. No
error and no warning: the caller simply got a larger result than it asked for, which downstream shows up as
a prompt that overruns its budget rather than as anything identifiable as a memory defect.

**Root cause.** `GraphMemoryEngine.RecallAsync` computed
`reserve = Math.Min(authoritative.Count, Math.Max(0, AuthoritativeReserve ?? limit))`. The `?? limit` caps
the DEFAULT; an explicitly configured value was never compared against the limit at all. The line that
follows, `ordinary.Take(Math.Max(0, limit - reserve))`, floors at zero — so once `reserve > limit` the
ordinary half contributes nothing and `reserved` is concatenated whole.

The two numbers live on different scopes and nothing else reconciled them: `AuthoritativeReserve` is an
engine option, sized against `GraphMemoryOptions.DefaultLimit`; `Limit` arrives per query. So the trigger is
the ordinary case — a reserve of `5` against a default limit of `10`, and any caller that passes something
smaller for a tight prompt budget.

**Why no test caught it.** Both existing reserve facts use a reserve BELOW the limit (`1` against `3` and
`4`), which is the only regime where the missing cap is unobservable. Nothing exercised reserve > limit, and
nothing anywhere asserted the plain property that a recall returns at most `Limit` items.

**Fix.** `Math.Min(limit, …)` around the whole expression, so the option can only ever REDUCE displacement —
the only direction its own documentation describes. Objective (1) is unaffected: the default was already
unbounded-within-the-limit and still is. `AuthoritativeReserve`'s XML doc was corrected in the same change; its
last paragraph still described the rejected first version of the mechanism ("applies only to entries
re-admitted BY GRADE"), three lines from the engine comment that says the opposite in capitals.

**Verification.** `GraphMemoryRankingGoldenTests.A_reserve_larger_than_the_query_limit_still_returns_at_most_the_limit`
— red before the fix (3 items), green after, with the eight pre-existing golden facts unchanged.

**Introduced by.** The 3.0 authoritative-reserve work (D56); the option was born with the gap.

---

## 2026-08-14 — a non-finite age poisoned an entry's persisted `Difficulty`, on the reinforcement path the docs recommend

**Symptom.** With a `IMemoryAgePolicy` reporting a non-finite age, `ExpandAsync` persisted
`MemoryDecayState.Difficulty` as `NaN` on the in-process store; on a SQL backend the write threw and
`ReinforceAsync`'s deliberate catch-all swallowed it, so the reinforcement was silently lost instead. Either
way the review log — the artifact that exists to make FSRS parameter fitting possible — recorded a `NaN` row.

**Root cause.** `DsrRetrievability.Reinforce` guards the STABILITY half (`double.IsFinite(increase)`) and its
own comment explains why it must: the increase term depends on `Age`/`Strength`/`StrengthAge`, which arrive
per-call in the caller's state and so cannot be validated at construction the way `DsrOptions` validates its
constants. The DIFFICULTY half, added later by the fsrs-properly work, asserted the opposite in
`NextDifficulty`'s own doc — "every term feeding `D''` is provably finite before the clamp runs".

Both cannot be true. The derived grade is `2 + 2 × Retrievability(state)`, and `Retrievability` is a function
of exactly the age the stability half declines to trust. `Math.Clamp` PROPAGATES `NaN` (IEEE-754), so
`Retrievability` returned `NaN`, `DerivedGrade` returned `NaN`, `NextDifficulty`'s final
`Math.Clamp(_, 1, 10)` returned `NaN`, and `GraphMemoryEngine` wrote it straight to `TouchAsync`.
`DerivedGrade(double)`'s own summary carried the same false premise — "already clamped to `[0,1]` by
`Retrievability` itself".

**Why the recall path looked fine.** It is covered by accident: `MemoryRankingContract.Rankable` drops a
candidate whose retrievability is non-finite, so the poisoned entry never reaches reinforcement.
`ExpandAsync` reinforces a node the caller named with no ranking in between — and
`ReinforceOn = MemoryReinforcementActs.Expansion` is what `docs/memory.md` and this release's changelog
recommend as the measurably better setting, so the unguarded route is the one the documentation points at.

**Fix.** `DerivedGrade(in MemoryDecayState)` reports NO JUDGEMENT for a non-finite retrievability, reusing the
meaning `null` already carries for the Δt=0 bypass: nothing computable happened, so nothing should move.
`Reinforce`'s existing `grade is null` branch then leaves difficulty at its already-coerced, finite value.
Both over-claiming doc paragraphs corrected in the same change.

**A distinction the first attempt got wrong.** Only `NaN` is uncomputable. `+Infinity` age gives
`Math.Pow(+Infinity, decay)` with a negative exponent — exactly `0`, "fully forgotten" — and derives a real
Hard grade; `-Infinity` was always caught by `Age <= 0`. A first test asserted `null` for all three and failed
on `+Infinity`, which was the TEST being wrong rather than the fix. The pinning fact now says which is which.

**Verification.** `DsrPathologyTests.A_non_finite_age_never_produces_a_non_finite_grade_or_difficulty` and
`Expanding_under_a_non_finite_age_policy_never_persists_a_non_finite_difficulty` — both red before, green
after. The second uses `InMemoryMemoryGraphStore` deliberately: on a SQL backend the poisoned write throws and
is swallowed, so the stored value stays finite and the fact would pass while the defect was live.

**Introduced by.** The fsrs-properly difficulty work (D49's follow-up), which added a second consumer of
`Retrievability` without carrying the finiteness reasoning the first one already had.

---

## 2026-08-14 — a multi-word CJK query produced NO search terms, on every backend, because one short-circuit skipped the short grams

**Symptom.** A keyword recall whose tokens are all short CJK words — `"配偶 客户"` ("spouse", "client"), two
ordinary two-character Chinese words — matched nothing on any storage backend. No error, no exception: the
query simply returned no keyword hits, which is indistinguishable from "nothing was stored". Found
2026-08-14 by a code-review agent reading `SearchTerms`, and confirmed by executing the method rather than
by reasoning about it.

**Root cause.** `SearchTerms.SubstringTerms` short-circuited on the index pass:

<!-- compile-skip: the defective body AS IT WAS, quoted from inside SearchTerms — `Extract` and
     `ShortGrams` are private statics of that class, so the fragment cannot stand alone, and supplying a
     context for it would mean re-declaring the very method this entry is about. -->
```csharp
var terms = Extract(raw);
return terms.Count == 0 ? terms : [.. terms, .. ShortGrams(raw)];
```

`Extract` emits 3-grams (the trigram-index floor), so a token of exactly two Han characters yields nothing.
With EVERY token below that floor, `Extract` returns empty and the ternary returns immediately — never
calling `ShortGrams`, which exists precisely to carry those two-character words and would have returned
`["配偶", "客户"]`. `LikeClause`'s own `terms.Count == 0` fallback then substituted the whole trimmed query
as a single pattern, `%配偶 客户%`, demanding that exact phrase *including the space* — which ordinary prose
never contains.

**Why it hid for so long.** Three coincidences.
1. **The single-token case works by accident.** `"配偶"` alone also yields no terms, and the whole-query
   fallback is then `%配偶%` — exactly what the term-based clause would have produced. Every existing test
   used one token.
2. **A long neighbour masks it.** `"配偶 叫什么名字"` makes `Extract` non-empty, so the short grams ARE
   appended and `配偶` survives. The same token was kept or dropped according to its neighbours — which is
   the tell, and no design would choose it.
3. **The rescue built for this case defeated itself.** Postgres runs a narrow pass, then consults
   `HasShortSpacelessTerms` — which calls `ShortGrams` DIRECTLY and correctly answered `true` — and paid a
   second round trip to widen. That widening called `SubstringTerms`, hit the same short-circuit, and
   returned the same empty list. The pass that exists to rescue short CJK words rescued nothing while
   costing the sequential scan it was budgeted for.

**Fix.** Union the two sources unconditionally, de-duplicated across the union (every shipped
`ScriptProfile` uses index 3 / substring 2 so no gram can repeat today, but `ScriptProfile` is public and
consumer-constructible, and equal lengths would otherwise double-count a gram in `LikeTermClause.MatchCount`
and distort the ordering that expression exists to provide). Single-token and all-ASCII behaviour is
byte-identical: `Spaced` does not expand, so an English query gains nothing.

**Verification.** `SearchTermsTests.Short_grams_survive_when_every_token_is_below_the_index_floor` pins the
multi-token case and the neighbour asymmetry; two sibling facts pin the single-token and ASCII paths that
must NOT change. Executed before and after against the real method:
`"配偶 客户"` → `[]` before, `[配偶, 客户]` after. Full affected suites green (1386 passed), including the
corpus goldens, the CJK recall tests and the Postgres leg.

**Introduced by** the D55 tokenization work that added `ShortGrams` — the short-circuit was carried over
from when `SubstringTerms` had only one source and empty meant empty. Unreleased, so no shipped version
carries it.

---

## 2026-08-14 — the one-line memory path ignored two registered policies, because one engine had two constructors' worth of call site

**Symptom.** None that surfaces as an error. `services.AddLyntai(cfg => cfg.UseSqliteStorage(…).AddMemory()
.AddMemoryVerification())` builds, resolves, remembers and recalls — and the verification policy is never
called. The only observable is recall QUALITY, which a consumer has no baseline for: they enabled the
feature, saw results, and had no way to learn it was inert. The identical registration behind
`AddMemoryEngine("m", e => e.UseGraph())` worked, so the difference was invisible unless someone compared
the two paths. Found 2026-08-14 during the 3.0 pre-freeze whole-repo review, by reading the two call sites
side by side rather than by any failing test.

**Root cause.** `MemoryEngineBuilder` constructed `GraphMemoryEngine` in TWO places — `UseGraph` (the
configured path) and `UseBestAvailable` (what `AddMemory()` resolves to) — each with its own copy of a
fourteen-argument list. `annotation:` and `verification:` were added to the first when those seams shipped
and never to the second, so the zero-configuration path passed neither and the engine took its documented
model-free floor for both. Nothing could catch it: the parameters are optional, so omitting them compiles;
the engine's fallback is a real, supported behaviour, so it does not throw; and every existing wiring test
exercised `UseGraph`. This is `.claude/knowledge/pitfalls.md` §DI/config's *"a documented option that isn't
wired"* for the third recorded time, and the first where the unwired option is the one its own registration
doc calls **the single largest recall-quality lever the subsystem has**.

**Fix.** One construction site — a private `BuildGraph(sp, full, store, …)` carrying the DI reads and the
five per-engine overrides. `UseGraph` passes its overrides; `UseBestAvailable`, which has no configuration
surface to name one with, passes none, and null already means "take the container registration" for every
one of them. The behaviour change is exactly that the one-line path now honours those two registrations;
nothing else moves. Fixing the SHAPE rather than adding the two missing arguments is the point: a second
copy kept in step by memory is how this happened, and the next engine parameter would have done it again.

**Verification.**
`GraphMemoryWiringTests.The_one_line_AddMemory_path_honours_a_registered_annotation_and_verification_policy`
registers recording policies through the container, drives `AddMemory("m")`, and asserts BOTH were consulted
— red against the old wiring (`Expected: ["the spouse is called Alice"]  Actual: []`), green after. Full
`verify` green: 13 gates, 2727 passed / 2748.

**Introduced by** the commits that added each seam — `annotation:` with the subject-linking work (D55) and
`verification:` with D59 — neither of which touched `UseBestAvailable`. Both are unreleased, so no shipped
version carries the defect.

---

## 2026-08-11 — the version-authorship guard reported "no problems" whenever git could not answer

**Symptom.** None, again by construction: `check-version-bump` exits 0 and prints nothing on a clean index,
which is byte-for-byte the output it produced when it could not read the index at all. A corrupt
`.git/index`, a missing `git`, or a cwd with no repository anywhere above it each yielded a passing
pre-commit guard. The pre-commit hook only masked it because `check-sensitive` runs first and fails closed
on the same condition, so the hook path usually stopped anyway — `node devtools/dev.mjs check-version` did
not. Found 2026-08-11 while writing that guard's tests (`docs/task-archive.md` Part 60), filed as Part 62, fixed here.

**Root cause.** `stagedDiff` wrapped `git diff --cached` in `try { … } catch { return '' }`. The empty
string is exactly what a file with no staged changes produces, so every failure was laundered into the
benign case and the three rules above it saw "nothing staged". The comment above it named the benign case
it was written for and the catch was never narrowed to it — this is fail-OPEN on the one check standing
between a hand-authored `<VersionPrefix>` and D19's silently-skipped release.

**Fix.** `stagedDiff` throws `StagedDiffFailure` instead; `checkVersionBump` catches it, reports it as a
problem (so every caller blocks by default) and also returns it as `gitError`, because the remediation is
the opposite one — "stop hand-editing the version" is nonsense advice for a corrupt index, and the CLI now
prints a different block for it. The benign cases were MEASURED before the catch was narrowed, not assumed:
`git diff --cached -- <file>` exits 0 with empty output for a file with no staged change, for a file that
does not exist, **and for a repository with no HEAD** (a fresh clone's first commit). All three still pass.
Not a git failure either, and worth knowing: a cwd that is not itself a repository, because git walks up.

**Verification.** `check-version-bump.test.mjs` → "when GIT ITSELF fails, the guard fails CLOSED" (6 tests:
a really corrupt index, an injected `ENOENT` for the spawn-failure branch, git's own stderr surfaced, one
problem rather than one per file, the `LYNTAI_RELEASE` hatch still short-circuiting before git is asked, and
the CLI's own exit-1 branch driven end to end through a bogus `GIT_DIR`), plus two new benign-case tests
pinned FIRST. Mutation-checked: restoring the `return ''` reddens exactly those 6 and leaves the 16 benign
ones green.

**Introduced.** `check-version-bump.mjs` at its creation; latent in every run since.

---

## 2026-08-11 — check-docs exempted the whole CHANGELOG, and the first fix for it was inert

**Symptom.** A `## Unreleased` entry asserted a behaviour the code had since changed, plus the retired
justification for it, and `check-docs` reported 39 docs clean with it present (measured 2026-08-09). A human
reviewer caught it — the exact failure `docs/DECISIONS.md` D42 created the gate to prevent. A paragraph five
lines below in the same bullet was corrected in the same editing pass, so the text was read and the stale
claim still survived.

**Root cause.** Two, one behind the other. (1) `HISTORICAL` exempted `CHANGELOG.md` wholesale on the
rationale that a record is "accurate BY using the vocabulary of its day" — true of a released section and
false of `## Unreleased`, which describes behaviour that has not shipped and can still change under the
words describing it. (2) Narrowing that exemption changed NOTHING, because `IN_SCOPE` runs first and had
never listed `CHANGELOG.md` at all: the file was excluded one stage earlier, and the gate printed the same
clean line either way. The second cause was found by probing the gate (asking what was in its file list and
how many lines it would scan) rather than by reading its exit code, which is the only way a filter-chain
edit can be verified.

**Fix.** `CHANGELOG.md` added to `IN_SCOPE`, plus a new `LIVE_PREFIX` rule that scans it down to the first
released `## <version>` heading and no further; the two-line wrap window is clipped at the same boundary so
it can never join a live line to a historical one. `check-samples` imports the same boundary rather than
keeping a second copy of the answer. One `retiredTerms` pattern was widened in the same pass, because the
first real run showed the same claim caught in one phrasing and missed, four hundred lines earlier, in
another: "only reads … never writes it back" versus "ours reads … never writes it back". <!-- drift-ok -->

**Verification.** `check-docs.test.mjs` → "CHANGELOG.md is historical only BELOW its first released heading"
(7 tests, including `IN_SCOPE('CHANGELOG.md')` itself — the assertion that fails against the inert first
attempt — a released section staying exempt, and the window not reaching across the boundary), plus
`check-samples.test.mjs` → the same split for a fenced block. Mutation-checked 4 ways (drop it from
`IN_SCOPE`; drop the `LIVE_PREFIX` rule; remove the prefix clip; move the boundary three lines) — each
reddens 1–4 tests. On the real repository the first run reported 3 stale passages: two fixed, one
`drift-ok`-annotated (it names the policy as a mutation's value, never as a default).

**Introduced.** The exemption in `6837ba3` (2026-08-08), with `check-docs` itself.

---

## 2026-08-11 — the leak guard never scanned a file whose NAME is non-ASCII, and called it a deletion

**Symptom.** None, which is the whole problem: `check-sensitive --tree` reported `clean` over a tree
containing a file with a dev-machine path in it. Reproduced by the first fixture that named a document in
CJK — `docs/灵台.md`, holding a synthesized Windows user-home path — while `docs/plain.md` with the identical <!-- link-ok: guard FIXTURE names, never files here -->
content was flagged. Found while writing this guard's first tests (`docs/task-archive.md` Part 60), not by any run.

**Root cause.** `git ls-files` C-QUOTES a path containing any non-ASCII byte: `docs/灵台.md` comes back as <!-- link-ok: guard FIXTURE names, never files here -->
the literal string `"docs/\347\201\265\345\217\260.md"`. That name matches no file on disk, so `readFileSync`
threw `ENOENT` — and the ENOENT branch of the scan classifies ENOENT as *a tracked file deleted from the
working tree*, a real and legitimate mid-refactor state that is deliberately skipped rather than
fail-closed (added earlier so a refactor did not look like a leak report). The two behaviours compose into a
false PASS: the file is silently not scanned, and the only output is a line saying one tracked file was
deleted. Staged mode had the same root cause with the opposite ending — `git show :"docs\347…"` fails, which
lands in `unreadable` and correctly fails closed, so the pre-commit hook blocked with a confusing git error
while `verify` sailed through. `check-docs` had the identical defect via `git ls-files`, ending in its bare
`catch { continue; }`: an in-scope document with a CJK name was never scanned and never reported as unscanned.

**Fix.** `-z` on every git path listing, split on NUL instead of newline — `git ls-files -z` in
`check-sensitive.mjs`'s `sources()` and in `check-docs.mjs`'s `trackedFiles()`, and
`git diff --cached --name-only --diff-filter=ACM -z` for the staged list. `-z` disables quoting entirely, so
paths arrive as raw bytes and both readers agree with git about how a name is spelled. No pattern, scope or
classification logic changed.

**Verification.** `devtools/scripts/__tests__/check-sensitive.test.mjs` → "sees a leak in a file whose NAME
is non-ASCII": a fixture repository with `docs/灵台.md`, asserting `sources()` returns the path raw, that <!-- link-ok: guard FIXTURE names, never files here -->
`checkSensitive` returns 1 naming `docs/灵台.md:1`, and that the run does NOT report it as a deletion (the <!-- link-ok: guard FIXTURE names, never files here -->
last assertion is the one that fails against the old behaviour). Twin in
`check-docs.test.mjs` → "returns a NON-ASCII path unquoted". Both fail against the pre-fix scripts.
Adjacent regression cover added at the same time: the deletion branch itself still passes (a genuinely
deleted tracked file is reported and does not block), so the fix did not simply disarm it.

**Introduced.** `check-sensitive.mjs` in `55c079b` (2026-07-17, the original scaffolding);
`check-docs.mjs` in `6837ba3` (2026-08-08). Latent in every run of both since.

---

## 2026-08-11 — check-packages crashed instead of reporting when the Baselines directory was absent

**Symptom.** `readdirSync` `ENOENT` stack trace out of `check-packages`, naming no package, when the last
file in `tests/Lyntai.Tests/Api/Baselines/` is deleted (a directory with no files does not exist in git, so a
fresh branch that deletes a package's baseline can reach this).

**Root cause.** The orphan-baseline sweep enumerated the baseline directory unconditionally, after the
per-package loop had already collected the correct `no API baseline` problems. The exception discarded them.

**Fix.** `existsSync` first; an absent directory yields an empty list, so the per-package problems are
reported instead. Fails closed either way — this only changes a stack trace into the report the operator
needed.

**Verification.** `check-packages.test.mjs` → "the API baseline file" and "returns 1 and prints every problem
with its fix", both of which delete the only baseline and assert the *reported* problem.

**Introduced.** `d9ab870` (2026-08-04), with the gate.

---

## 2026-08-09 — all three backends cut a long-quiet exact fact at the candidate LIMIT, not just the WHERE

**Symptom.** `IMemoryGraphStore.SeedAsync` documents, in prose, that authoritative material is admitted
unconditionally. The predicate half of that promise was fixed the same day (the entry directly below this
one), but a second route to the same exclusion survived it: every backend's `SeedAsync` ordered candidates by
`last_recalled_position DESC` and took a capped number of rows. A long-quiet exact fact — one nobody had
touched since it was written — has the *lowest* `last_recalled_position` in its scope, so it sorts last, and
the `LIMIT` cuts it before the engine ever ranks anything. This is D41's rejected behaviour ("faintness never
excludes") reached by ordering instead of by predicate — the row was never excluded by the `WHERE`, only by
running out of `LIMIT` before reaching it.

**Root cause.** `SqliteMemoryGraphStore.SeedAsync`'s LIKE and no-query branches, `PostgresMemoryGraphStore
.SeedAsync`'s single query, and `InMemoryMemoryGraphStore.SeedAsync`'s LINQ pipeline all ordered candidates by
recency alone (`ORDER BY n.last_recalled_position DESC, n.id DESC` / `.OrderByDescending(n =>
n.LastRecalledPosition)`) before applying the `limit`. The grade carve-out in the `WHERE`/`.Where(...)`
predicate (fixed in the entry below) guarantees an authoritative row is never *filtered out* — it says
nothing about whether the row survives the `LIMIT` that runs after the filter and before the caller ever sees
a rank.

**Fix.** Grade now leads every seed ordering, ahead of recency: `ORDER BY (n.grade = @authoritative) DESC,
n.last_recalled_position DESC, n.id DESC` on both relational backends (`SqliteMemoryGraphStore.cs`'s LIKE and
no-query branches; `PostgresMemoryGraphStore.cs`'s single query, both call sites), and
`.OrderByDescending(n => n.Grade == MemoryGrade.Authoritative).ThenByDescending(n =>
n.LastRecalledPosition).ThenByDescending(n => n.Id)` on `InMemoryMemoryGraphStore.cs`. Exact facts now occupy
the head of the candidate set regardless of how stale they are, so the `LIMIT` cuts fresher associative
material first.

**Deliberately NOT changed: `SqliteMemoryGraphStore.Merge`.** Task 1's reserved-capacity `Merge` (which
combines the FTS branch's matches with a separately-fetched exact-facts query) already guarantees survival
past ITS limit by construction — it reserves capacity for exact facts rather than relying on ordering. An
earlier draft of this fix flipped `Merge` to put exact facts first too; that was wrong, because `Merge`
renormalizes `Relevance` over the merged order, and ordering exact facts first would hand them the *top* of
the gradient and outrank every genuine match — the opposite of the intent (see `Merge`'s own XML doc and
`Exact_facts_survive_a_full_page_of_matches`, which pins the current order).

**Amended 2026-08-09 (final branch review): `Merge` DID need one change, in the other direction.** It sent
*all* of `exact` to the tail, including rows that were also a top bm25 match — the `exactIds` filter stripped
those out of the matched portion and re-appended them last. An exact fact that directly ANSWERS the query
therefore took the bottom of the gradient (`≈ 1/merged.Count` where pre-branch it kept `≈ 1.0`), and because
`GraphMemoryEngine.RecallAsync` ranks by `Relevance × Retrievability × HopAttenuation^Hop` and then
`.Take(limit)`s, it could be dropped from recall outright — strictly worse than pre-branch. The partition is
now on `matched`'s ids, so only genuine non-matches are tail-ordered; the paragraph above stands for the
*ordering of non-matches*, which is unchanged. Pinned by
`SqliteMemoryGraphStoreTests.An_exact_fact_that_matches_the_query_keeps_its_earned_position` — red at
`Relevance 0.0999…` before the change, green after — which is the case both tests named above structurally
cannot see, because each uses an exact fact matching nothing.

**Verification.** `MemoryGraphStoreContract.Seeding_admits_a_long_quiet_exact_fact_over_fresher_material`:
one authoritative fact, then 60 fresher associative writes in the same scope, `SeedAsync(..., limit: 10)`
must still return the authoritative fact. Wired against all three backends
(`SqliteMemoryGraphStoreTests.Exact_survives_the_limit`, `InMemoryMemoryGraphStoreTests
.Exact_survives_the_limit`, inline in `PostgresStorageTests.Graph_store_satisfies_the_contract`, coverage
guard bumped 19→20). Red before the fix on all three
(`node devtools/dev.mjs test --filter "Exact_survives_the_limit|Graph_store_satisfies_the_contract"`:
`Assert.Contains() Failure` — the count assertion passed (10 returned), the membership one did not — on
SQLite, InMemory, and a real Postgres container), green after (3/3). Full `--filter "MemoryGraphStore"`
regression: 43/43, including `Exact_facts_survive_a_full_page_of_matches` and
`Upsert_then_seed_by_single_token_substring` (`Assert.Single`, unaffected by the reordering). `node
devtools/dev.mjs verify` green: 1910 passed, e2e 3/3.

---

## 2026-08-09 — SQLite's FTS seed path silently dropped an authoritative fact the query didn't match

**Symptom.** `IMemoryGraphStore.SeedAsync` documents, in prose, that AUTHORITATIVE material is admitted
unconditionally — "the query does not exclude them." On SQLite this was false whenever the query's trigram
index matched *something else* in scope: an exact fact sharing no trigram with the query was silently not
seeded. Ask about "restaurant" and a stored dietary constraint never reaches the prompt.

**Root cause.** `SqliteMemoryGraphStore.SeedAsync`'s FTS branch (`src/Lyntai.Storage.Sqlite/SqliteMemoryGraphStore.cs`,
introduced in `5756755` *feat(storage): persist graph memory on SQLite*) filtered its `MATCH` query on
`engine`/`task_key`/`scope` only. The `authoritative` parameter was bound into the query's parameter object
but never referenced in the SQL. Worse, `if (hits.Count > 0) return hits;` returned as soon as the trigram
index matched anything, before ever reaching the LIKE branch further down — which *does* carry the grade
carve-out (`n.grade = @authoritative OR n.content LIKE @pattern`). So the carve-out existed in the code, just
on a branch that a matching query never reached. Postgres (`PostgresMemoryGraphStore`) and InMemory
(`InMemoryMemoryGraphStore`) apply the carve-out directly in their query/filter and were already correct.

**Fix.** When the FTS branch gets a hit, fetch the scope's authoritative facts in a second query (ordered by
`last_recalled_position DESC, id DESC`, same shape as the LIKE branch) and merge them with the matched set
via a new `Merge` helper. `Merge` gives exact facts RESERVED capacity rather than appending them after a full
page of matches and truncating the tail — `matched` is itself `LIMIT`-bound, so an append-then-truncate drops
every exact fact whenever the query alone fills `limit`, which is the same defect wearing a hit-count
condition (caught in review before it shipped). The merged list is then renormalized as one `Relevance`
gradient — matches lead, exact non-matches take the low end — because `Relevance` is a per-query rank
position, and splicing two independently-1.0-topped batches would report a fact that matched nothing as the
best hit for the query. Only runs when the trigram index matched something at all: with no match the
existing fallback to LIKE (which already carries the carve-out) still applies.

**Verification.** Two tests, at two different scopes. The backend-agnostic contract fact,
`MemoryGraphStoreContract.Seeding_admits_authoritative_material_the_query_does_not_match` (one exact fact,
one non-matching associative note — both must come back), is wired against all three backends
(`SqliteMemoryGraphStoreTests.Admits_exact_on_query_path`, `InMemoryMemoryGraphStoreTests.Admits_exact_on_query_path`,
and inline in `PostgresStorageTests.Graph_store_satisfies_the_contract`) and pins the ORIGINAL defect —
red before the fix (SQLite only; InMemory and Postgres already passed), green after on all three
(`node devtools/dev.mjs test --filter "Admits_exact_on_query_path"`: 2/2 SQLite+InMemory;
`--filter "Graph_store_satisfies_the_contract"`: 1/1 against a real Postgres container).

A second, SQLite-only test, `SqliteMemoryGraphStoreTests.Exact_facts_survive_a_full_page_of_matches`, pins
the `Merge` mechanism itself — reserved capacity and renormalized relevance — by writing 12 matching notes
against a `limit` of 10, so the query branch alone fills the page and an append-then-truncate `Merge` would
silently drop the exact fact again. It stays SQLite-only rather than joining the shared contract fact: no
other backend merges two independent queries (InMemory and Postgres admit an exact fact in the SAME
query/filter as the match, so there is nothing to merge), and `Relevance < 1` is not a portable assertion:
only SQLite computes a non-trivial relevance gradient at all. Red against the pre-fix
append-then-truncate `Merge` (`Sequence contains no matching element` — the exact fact is entirely absent),
green after
(`node devtools/dev.mjs test --filter "Exact_facts_survive_a_full_page_of_matches"`: 1/1). Full
`--filter "MemoryGraphStore"` regression run: 41/41, including `Fts_stays_in_sync_after_a_delete` and
`Cjk_substring_recall`, the two other tests exercising the same branch.

*(Whether the same reserved-capacity truncation defect exists in InMemory/Postgres when their own match count
exceeds `limit` is a separate question — tracked and fixed as part of the grade-priority ordering work, not
this fix.)*

---

## 2026-08-05 — a non-zero exit code masked a CLI backend's own account of a failed turn

**Symptom (reported by a consuming app, filed as `TASKS.md` CLI15).** A `codex` turn run against an account <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
whose login had expired came back as a bare `LlmVerdict.Failed` whose detail was <!-- drift-ok: the PRE-MERGE name this incident was recorded under -->
`exit 1: Reading prompt from stdin...`. The actual failure — a 401 — appeared nowhere, so the app told its
user the CLI was missing or not on PATH. The right remedy was "log in again".

**Root cause.** `CliProviderEngine.CompleteAsync` returned on `result.ExitCode != 0` *before* it parsed
stdout, classifying the stderr tail. The dialect's in-band `CliOutputEventKind.Failure` — which carries the
backend's own message and is what makes the verdict actionable — was therefore never read on that path.

The two halves arrived in the **same** commit, `58a5657` (*feat(cli): generic CLI provider engine + codex
backend…*), which is why nothing looked wrong: the in-band failure vocabulary was added because codex reports
a failed turn **at exit 0**, and that measured case works correctly. Nobody asked what should happen when a
backend does *both*, and no test paired them. A consumer's measurement supplied the missing case.

Two consequences, not one. The detail was wrong (chatter instead of the reason), and so was the **verdict**:
`AuthFailed` benches the host for the cooldown window, `Failed` merely advances — so a fleet-wide credential
problem kept being retried against the same backend.

**Fix.** `src/Lyntai.Core/Llm/Cli/CliProviderEngine.cs` — parse stdout first; a reported in-band failure wins <!-- link-ok: the entry names the path AS IT WAS; D154 NS-4 moved the directory to src/Lyntai.Core/Inference/Cli · drift-ok: same reason, for the slashed-path rule -->
and is classified, with the exit code kept in the detail as context (`exit {code}: {message}`). The exit-code
reply is unchanged when the backend reported nothing in band. This is the ordering `StatusAsync` already
used ("parse the answer, then fall back to the exit code"); the completion path simply never had it.

**Verification.**
- Three failing tests first, all red before the change and green after: `CliProviderEngineTests`
  `A_reported_failure_wins_over_a_NONZERO_exit_and_its_stderr_chatter` (generic seam, `FakeCliDialect`),
  `CodexCliProviderTests.An_in_band_failure_is_classified_even_when_the_process_ALSO_exits_nonzero`
  (the measured JSONL pair), and `…_over_a_real_spawn` (the same pair through `codex-stub.mjs`, so the
  ordering is pinned against a real exit code and a real stderr rather than a hand-built `ProcessResult`).
- A fourth test, `A_nonzero_exit_with_NO_reported_failure_still_reports_the_exit_and_its_stderr`, pins the
  half that must **not** change — it passed before and after.
- `devtools/scripts/codex-stub.mjs` grew the measured `AUTH_ERROR_EXIT` shape (a bare `error` line whose
  `message` is a **string**, a `turn.failed` whose `error` is an **object**, stderr chatter, non-zero exit).
  While there, its three prompt-marker branches stopped calling `process.exit()` mid-write and set
  `process.exitCode` instead — exiting with queued stdout writes can drop the very lines a test asserts on
  (`.claude/rules/windows-machine.md` §Scripts and exit codes).
- `node devtools/dev.mjs verify` green.

**Not affected, checked rather than assumed.** `CliProviderEngine.StreamAsync` already ends the stream on the
in-band `Failure` event before the runner's non-zero-exit `ProcessRunException` can surface, and both
`ClaudeAgentSession` and `CodexAgentSession` suppress a second terminal via their `sawTerminal` guard — so
the "one failure reported twice" half of the consumer's report was already handled everywhere except here.
