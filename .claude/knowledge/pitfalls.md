---
name: pitfalls
applies_when: before extending or refactoring any area of Lyntai — tooling, LLM/router, provider lifetime, storage, DI, or tests
enforces: don't reintroduce the measured traps — each one passed the build (and usually the tests) while being wrong
---

# Pitfalls — don't reintroduce these

Concrete traps, most surfaced by a real bug or an independent audit. Each passed the build (and usually
the tests) while being wrong. Skim before touching the relevant area.

## Environment / tooling

- **This machine's console is GBK/CP936.** Writing UTF-8 through it (PowerShell `Set-Content`/`Out-File`
  without `-Encoding utf8`, `echo >`, a shell heredoc) **double-encodes and lossily corrupts** non-ASCII
  content — it once mangled every `灵台`/`—`/`§` in `TASKS.md` irreversibly. **Always write files with
  the Write/Edit tools** (they emit UTF-8 directly) or, in scripts, `fs.writeFileSync`/`-Encoding utf8`.
  Verify with an ASCII-safe check (codepoints), not by eyeballing console output (which re-mangles it).
- Sources are BOM-less UTF-8 + `<CodePage>65001</CodePage>` (in `Directory.Build.props`) — without it
  csc reads CJK string literals as ANSI mojibake on a CJK-locale machine.
- **A prose gate that matches line-by-line is blind to any claim that WRAPS**, and it reports the file
  clean while doing it. `check-docs` tested each `retiredTerms` pattern against one line at a time, so
  `CLAUDE.md`'s "`ReciprocalRankFusionPolicy`, available\nbut not the default" — a stale claim in the one
  file auto-loaded into every session — matched nothing for the entire window in which it was false. These
  documents wrap at ~110 columns and the claims worth fencing are shorter than a line, so **the sentence
  the gate most needs to catch is the one most likely to straddle a break.** Fixed 2026-08-11 by testing
  each line AND that line soft-joined to the next; `drift-ok` on either line silences the pair. The general
  shape is worth carrying to any future text gate: **the unit you match must be the unit the claim is
  written in.** Line-oriented matching is a property of the scanner, never of the prose.
- **A doc gate built on "does this identifier EXIST in the tree?" is the wrong shape, and the reason is
  worth knowing before someone builds it.** Tried 2026-08-14 as the prose counterpart to `check-samples`
  (which compiles fenced C# but says nothing about a type named in a SENTENCE): scan maintained docs for
  backticked PascalCase names absent from the whole tree's vocabulary. It produced **~45 hits and zero
  defects**, because naming something that does not exist is frequently the CORRECT thing for this
  repository's prose to do:
  · `pitfalls.md` cites `LlmRouterTests` precisely because that class does not exist — it is the example in
    the "a filter matching nothing passes vacuously" entry;
  · `generic-library.md` says `AgentStreamEvent.ToolCall` "deliberately has *no* `FilePath` property";
  · `storage.md`'s backend-pair table writes `TraceStore`/`KeyValueStore` as shorthand for the Sqlite+Postgres
    PAIR, neither of which is a type name;
  · `DECISIONS.md` names test fakes (`FixedAppraiser`, `EmptyAppraiser`) that were real on the day of that
    decision and are history now, and the design record's §1 names SIBLING projects' types on purpose.
  **The general shape: a gate whose false positives are legitimate authorial choices cannot be tightened
  into usefulness — it can only be given an exclusion list, and an exclusion list nobody can see rot is the
  hole `check-api-vocabulary`'s "an allowance that matches nothing FAILS" rule exists to avoid.** This is
  why `check-docs` is a curated `retiredTerms` registry (a decision retired this word, say that instead)
  rather than an existence check: the registry only ever contains claims someone deliberately settled, so a
  hit is a defect by construction. Reach for a registry, not a corpus scan, whenever "wrong" depends on
  intent rather than on the text.
- **Scope a doc gate by "is this maintained state?", not by directory.** The same gate omitted the two
  repo-root files, `CLAUDE.md` and `TASKS.md`, purely because the scope test was three `startsWith` calls.
  Both are maintained state and `CLAUDE.md` is the highest-leverage document in the repo — a stale claim
  there is read by the next session *before it reads anything else*. Adding them found real drift on the
  first run. `docs/task-archive.md` stays excluded on purpose: it is accurate BY using the vocabulary of its
  day. **`CHANGELOG.md` is only HALF that**, and the split cost real drift — see the next entry.
- **Two pieces of prose SHIP to consumers and no gate reads either: the packaged `README.md` and every
  csproj `<Description>`.** Found 2026-08-17, auditing 3.0 before the stamp. `PackageReadmeFile` in
  `src/Directory.Build.props` packs the repo README into **every** package, so it is what nuget.org renders
  on every package's page — and it named an untracked `local/superpowers/records/…` path three times,
  a link no consumer can follow, because `check-links` skips `local/**` by design (correctly: those records
  are untracked). The `<Description>`s are worse off still — `check-packages` asserts one EXISTS and never
  reads it, and `check-docs`' `CODE_IN_SCOPE` is `.cs`/`.mjs` only — so `Lyntai.Bundle`'s blurb was still
  justifying its exclusions with "an unverified surface", the reason **D69/D70 retired** for
  `Lyntai.Generation` a release earlier.
  <br>**The general shape, and it is the reason this is filed rather than just fixed:** a gate's scope is
  drawn around *what the repository maintains*, and these two live outside that line while being the most
  widely READ prose the project publishes. **Before a release, re-read the README as a consumer who has only
  the package** — no clone, no `local/`, no git history — and read every `<Description>` against the
  decisions since the last release. Both are cheap; neither is automatic.
  <br>**A third member of that family, measured 2026-08-21: a FORWARD version reference in an XML doc is a
  prediction, and it ships.** Two sentences said a fix arrives in "3.1" — reasonable, since the change was
  additive public surface; the release was cut as **3.0.1**, so the published package's IntelliSense named a
  version that does not exist. **The interesting half is that no gate can see it**: `check-docs` gates retired
  VOCABULARY, `check-counts` gates counted CLAIMS, and a version number is neither — nor can a corpus scan
  become one, because `net10.0`, `llama3.1` and every historical `## 3.0.0` heading are the same token shape.
  The rule is `repo-mechanics.md` §"never NAME a version that has not shipped"; what belongs *here* is that
  the correction repeated the mistake in a different costume an hour later, in the ROADMAP row recording the
  release — so treat "is this number a record or a prediction?" as a review question, not a thing you fix once.
- **"Historical record" can be true of part of a file and false of the rest. `## Unreleased` is not
  history.** `check-docs` exempted `CHANGELOG.md` wholesale on the rationale that a record is accurate by
  using the vocabulary of its day — true of a RELEASED section, false of `## Unreleased`, which describes
  behaviour that has not shipped and can still change under the words describing it. Measured 2026-08-09: an
  entry kept asserting a pre-fix behaviour *and* its retired justification after the code changed, while a
  paragraph five lines below was corrected in the same pass; the gate reported 39 docs clean and a human
  found it. Fixed 2026-08-11 by scanning the file down to its first released `## <version>` heading, sharing
  the boundary with `check-samples` so the two gates cannot answer it differently. Two hits on the first real
  run, both genuine. **The general shape: an exemption's rationale has a scope, and it is worth checking
  whether the FILE is that scope.**
- **Narrowing an exemption does nothing if an EARLIER filter already excluded the subject — and the gate
  reports the same green line either way.** The first version of that CHANGELOG fix rewrote the historical
  filter, ran clean, and had changed nothing at all: `IN_SCOPE` runs first and had never listed
  `CHANGELOG.md`, so the file was gone one step before the filter being narrowed. It was caught by PROBING
  the gate — asking it whether the file was in its list and how many lines it would scan — rather than by
  reading its exit code. **For any filter chain, a clean run proves nothing about the stage you edited;
  assert on the intermediate (the file list, the line count), or write the positive control that must fail.**
- **`git ls-files` (and `diff --name-only`) C-QUOTE any path containing a non-ASCII byte** — `docs/灵台.md` <!-- link-ok: a guard FIXTURE's name, never a file here -->
  comes back as the literal 8-character-escaped string `"docs/\347\201\265\345\217\260.md"`, which matches no
  file on disk. **Always pass `-z` and split on NUL.** Measured 2026-08-11 in `check-sensitive`, where the
  failure was silent AND permissive: the quoted name failed `readFileSync` with `ENOENT`, the ENOENT branch
  classifies that as "a tracked file deleted from the working tree" (a legitimate mid-refactor state), so the
  file was skipped, the leak inside it was never scanned, and the run printed `clean`. `check-docs` had the
  identical bug with an even quieter ending — a bare `catch { continue; }`. This repository is named 灵台, so
  a CJK-named document is not a hypothetical. The general shape: **a scanner that gets its file list from one
  tool and its bytes from another must agree with both about how a name is spelled**, and the disagreement
  shows up first in the paths nobody tests with.
- **A doc gate scoped by FILE TYPE leaves the identical defect alive in every other tier, and its own
  exclusion rationale is where to look for the hole.** `check-links` was added 2026-08-14 because six
  references to an archived document survived in maintained markdown. It scans `.md` and nothing else — so on
  the day it went green, **seven more of the same dead references were alive in `src/` and `tests/`**, two of
  them inside XML documentation that ships to consumers, plus a second archived document nobody had swept at
  all. The gate's own header defends excluding `src/` on the grounds that "the compiler already gates their
  crefs", and that is the tell: **the compiler resolves `<see cref>` and NOTHING else.** A type name in a
  `<c>` tag, a file path in a `<c>` tag, a test name in a `//` comment — all prose, all unchecked, and all
  places this repository's XML docs habitually put load-bearing references (`MemoryLanguageSweep` cited a test
  that had been renamed away, twice, while calling five corpus arms "the two arms").
  <br>Two things generalise. **(1) When a gate's scope is narrower than the defect, say which tiers are
  UNCOVERED in its header** — "maintained markdown" reads like "everywhere that matters" until someone counts.
  **(2) An exclusion justified by "another mechanism already covers it" is a claim about that other mechanism,
  and it should name exactly what it covers.** "The compiler gates crefs" was true and load-bearing in the
  half nobody checked.
- **`.gitignore` does not untrack an ALREADY-TRACKED file, so `git mv`-ing a document into an ignored
  directory leaves it tracked — and every downstream claim that it is untracked silently becomes false.**
  Measured 2026-08-14 while squashing: of **29** files under `local/`, exactly **one** is tracked —
  `local/superpowers/records/2026-08-09-memory-policy-measurement.md`, the record D43 archived out of `docs/`.
  It was moved rather than removed-and-recreated, and `.gitignore`'s `local/` has no effect on a path git
  already has in the index. Nothing failed, and no gate could notice: the file is out of `check-docs`' scope
  because `IN_SCOPE` never lists `local/`, not because it is untracked.
  <br>**The cost is the three claims it falsifies**, all in `docs/superpowers/INDEX.md` and all load-bearing
  for how people treat that directory: "a fresh clone does not carry these files" (it does), "`check-docs` no
  longer gates them (it scans tracked files only)" — the right conclusion via a mechanism that is not the one
  operating — and "nothing was destroyed, only untracked" (it was not untracked). A reader who believes
  `local/` is unpublished is one `git mv` away from publishing something they wrote there on that belief;
  `local/sensitive-patterns.txt` lives in the same directory, and its whole premise is that the directory
  does not ship.
  <br>**It stayed tracked for two days after this entry was written, and the entry said otherwise.** It had
  closed with "Verified clean today — that file is untracked" while `git ls-files local/` returned it. So the
  entry describing the trap had itself fallen into the half it warns about: the escape was recorded, the
  remedy was not applied, and the closing sentence asserted it had been. **A "verified" claim with no gate
  behind it decays exactly like any other counted claim** — which is the argument for `check-counts`, one
  tier up, applied to a boolean. That is the durable half of this incident, and it is why the fix below is
  reported as an assertion rather than as a claim.
  <br>**Resolved 2026-08-16** on the owner's call: `git rm --cached` (never a path change), and
  `git ls-files local/` is now empty — which is the assertion, not "verified clean". The three claims in
  `docs/superpowers/INDEX.md` are true again. The content is not destroyed, only untracked, and the INDEX
  records how to retrieve it from history; that it leaves every other clone is D43's intended trade-off
  ("a fresh clone does not carry these files"), which is exactly why it was the owner's call and not a
  tidy-up. The TRAP is permanent regardless — `.gitignore` still has no effect on a path already in the
  index, so the next `git mv` into an ignored directory repeats it.
  <br>The general shape, and it applies to any "move it out of the way" procedure: **untracking is an
  explicit `git rm --cached`, never a side effect of a path change.** Assert it (`git ls-files <dir>` should
  be empty) rather than inferring it from the ignore rule, because the ignore rule is not what decides.
- **A gate that enumerates a directory must tolerate the directory being absent.** `check-packages` threw a
  raw `ENOENT` stack trace from `readdirSync(Baselines/)` when the last baseline was deleted, instead of
  reporting the per-package "no API baseline" problems it had already collected. It failed CLOSED, so nothing
  shipped wrongly — but the operator is shown a stack trace naming no package, which is the report they
  needed. Found 2026-08-11 by the test that deletes the last baseline.
- **The release workflow builds from the REMOTE, so "finished" and "shipped" are different states and only
  one of them is pushed.** Measured 2026-08-05: **v2.2.0 was cut without the whole-library review that had
  been finished for it** — three commits were sitting locally, the workflow built what the remote had, and
  the release reported complete success. Nothing failed, nothing warned, and the missing work was invisible
  until someone compared the tag against the local branch. **Push before triggering a release**, and treat
  "the work is done" and "the work is on the remote" as two separate claims — the second is the only one a
  release can act on. (Recorded as a decision until 2026-08-14, which was the wrong home: nobody CHOSE this
  behaviour, it was discovered — so it lives here rather than in the decision record.)
- **NuGet never re-extracts a package version it already has in the global cache**, so packing under a FIXED
  throwaway version (`consumer-smoke`'s `9.9.9-smoke`) tests the packages only ONCE — every later run restores
  the first run's copies from `~/.nuget/packages/` and reports success about code it never compiled against.
  Found 2026-08-04: a newly-added public method was "missing" from a package that demonstrably contained it,
  and evicting the cached ids reported **9 stale packages**. `consumer-smoke` now deletes
  `<global-packages>/lyntai.*/<version>` after packing; keep that step if you touch the script, and remember the
  general shape — *a distinct version is not enough on its own if the version is a constant.* Evicting by
  id+version beats isolating `NUGET_PACKAGES`, which would re-download every third-party dependency per run.

- **Roslyn BINDS NOTHING in a compilation that carries a syntax error, so a per-file verdict taken from a
  failed batch certifies unbound code.** Parse errors in ONE file suppress semantic analysis for EVERY file
  in the compilation. Measured 2026-08-11 on `check-samples`' first run: the batch reported **132 errors,
  every one syntactic, and zero `CS0246`** — so eleven documented samples naming types that do not exist in
  this library were reported as compiling, and the gate announced 83/95 green over code whose names had
  never been resolved. The fix is the general one: **a batched checker may only accept a member from a run
  in which NOTHING failed** — iterate, drop or re-shape whatever errored, recompile, and take the verdict
  from the clean run. "No error was attributed to my file" is not evidence in a failed compilation, and
  this generalises past csc to any batch tool with a fail-fast phase (a linter that stops at a parse error,
  a migration runner that aborts the transaction).
- **A type declared in SOURCE outranks the same type from a referenced assembly for the whole compilation,
  and the diagnostic is only a warning (`CS0436`).** So one documented sample opening
  `namespace Lyntai.Generation;` silently redefines the real `GenerationRequest` for every other sample
  compiled alongside it, which then typecheck against the doc's copy instead of the shipped one — green,
  and meaningless. `check-samples` compiles any block declaring a namespace under the library root in its
  OWN compilation for this reason. The shape to remember: **whenever you compile untrusted-ish source
  beside real references, ask what that source is allowed to SHADOW.**
- **Splicing lines into a CRLF file with `lines.join('\n')` leaves the inserted lines lone-LF**, because
  splitting on `'\n'` leaves each original line's `\r` attached to its own end. `git` says only
  *"LF will be replaced by CRLF the next time Git touches it"* — a warning that reads like routine
  autocrlf noise — and the content diff looks perfect. Measured 2026-08-11 inserting 28 `compile-skip`
  markers. Detect and repair with a codepoint check (`s.replace(/(?<!\r)\n/g, '\r\n')`), never by eye.
- **`check-warnings` reports "build FAILED" for a build that SUCCEEDED once the build log outgrows Node's
  1 MiB `spawnSync` buffer.** Measured 2026-08-09 adding the memory policy sweep: a single
  `ProjectReference` from `bench/Lyntai.Benchmarks` to `Lyntai.Tests` dragged Postgres, Testcontainers, MCP,
  ExtensionsAi, Generation and xunit into the bench build, and the full-solution `-v normal` log hit
  **1,049,602 bytes** — just over the cap. Node returns `ENOBUFS`, the script sees no output, and the gate
  announces a failure that did not happen.
  **This is the worst class of defect this repo has** — not a gate that misses something, but a gate that
  LIES, in the direction that trains a reader to ignore it. It is the `windows-machine.md` §"exit codes that
  lie" trap wearing a different hat, and it will recur for *any* future change that grows the build log,
  with a message pointing nowhere near the cause.
  Two takeaways: **a `ProjectReference` to the test project pulls its ENTIRE dependency graph** into
  whatever references it — prefer `<Compile Include>` links for the handful of files you actually need; and
  when a gate reports a failure whose detail is empty or truncated, **suspect the harness before the build**.
  A `--verbosity` reduction or an explicit `maxBuffer` on the spawn would close it at the root.
- **A classifier written from a vocabulary list is blind to MODIFIERS on that vocabulary, and it fails
  silently because every item still lands somewhere.** Measured 2026-08-16, in the release workflow's
  notes generator. It matched `^feat(scope)?:` and `^fix(scope)?:` and dropped
  `^(chore|docs|refactor|…)(scope)?:` — a complete-looking vocabulary that omits conventional commits'
  one modifier, the BREAKING `!`. So `feat(memory)!:` matched no rule and fell to the catch-all bucket,
  titled "Other changes": **29 commits of history, and 11 of 11 in the v2.5.0..3.0 range.** A major release
  whose entire story is breaking changes was one run away from publishing "New features: 1".
  <br>**The tell is a catch-all that never looks wrong.** Nothing errored, nothing was dropped, no count
  disagreed — every commit appeared in the output, under a heading a reader would skim. Contrast a
  classifier that throws on an unrecognized token, where the same defect is a build failure. **When a
  classifier has an "everything else" branch, the question is not "does it run?" but "what is landing in
  the catch-all, and does that list look like the name on it?"**
  <br>Its sharpest form here: plain `refactor:` is *dropped* as non-user-facing, so a breaking refactor —
  the single most important line a consumer can read — was one regex away from being deleted rather than
  merely misfiled. The only reason it survived is that the drop pattern *also* failed to match the `!`.
  **Correctness rested on a second rule failing**, which is not a property anybody can maintain.
  <br>The fix is the shape this repo already uses for gates: a pure function in
  `devtools/scripts/release-notes.mjs`, tested by `test-devtools`, with the workflow a thin caller — and
  the rule pinned by a test over the REAL commit log, as a property ("no breaking commit lands in Other")
  rather than a count, since a count there fails on the next commit.

## LLM / router (details in `llm-and-router.md`)

- **A timeout as a single `CancelAfter` over a whole call** — a wall clock that kills a slow-but-alive
  child (a long tool loop, a big prompt) exactly like a dead one. Both the STREAMING path and the BUFFERED
  `ProcessRunner.RunAsync` must use a per-chunk inactivity clock (re-armed on each read); the buffered path
  adds an absolute `maxDuration` backstop and reports `ProcessResult.TimeoutKind`. (Streaming shipped the
  bug in two providers; the buffered path shipped it too — both fixed.)
- **A non-positive resolved budget means the OPPOSITE thing in the two domains — don't "unify" the idiom
  casually.** The LLM sites arm the clock unconditionally (`OpenAiCompatibleProvider.CompleteAsync`,
  `HttpEmbedder.EmbedBatchAsync`, `ExtensionsAiProvider` all `CancelAfter(timeout)`), and app-configured
  values are trusted rather than clamped (`LyntaiOptions.ResolveTimeout`), so a `TimeoutByConsumer` entry of
  `TimeSpan.Zero` **cancels the call instantly**. `GenerationDeadline.GuardAsync` reads the same value as
  **no deadline at all** — the documented escape hatch for a host that owns its own clocks. Both are
  deliberate; flipping either is a behaviour change no consumer can detect at compile time (major-bump
  material, `docs/DECISIONS.md` D18), and a shared helper that keeps both behaviours behind a flag has
  consolidated nothing but the line count.
- **Committing a stream on an empty content chunk** — disables fallback for a zero-content first chunk.
  Gate the commit on `Text.Length > 0`.
- **Hand-rolled verdict heuristics** in a provider — they drift. Always route through
  `LlmVerdictClassifier`. And keep it conservative: a bare word like "unauthorized" or a "429" in a
  stack frame must not trip a verdict that benches a healthy host.
- **Empty provider output as `Ok`** — must be `Failed` (and a terminal `Error` chunk when streaming) so
  the router can fall over.
- **Asking the `claude` CLI a question it doesn't recognize SPENDS A TURN.** An unrecognized token is
  treated as a PROMPT, not an error (`claude zzznotacommand` answers in prose), and `config`/`models` hang
  waiting on a session. So a turn-free question must be a **flag** or a **verified** subcommand — `--version`
  (`ProbeAsync`), and `update`/`install`/`auth {status,login,logout}`, each checked against the installed CLI's
  own `--help` before being wired. Never "just try" a plausible subcommand to see what it reports: the build
  stays green while every call quietly costs tokens.
  Corollary: the CLI has no turn-free model readout — `ProviderProbeResult.Model` is null there by design,
  and the resolved model comes from `AgentStreamEvent.UsageFinal.Model` after a turn.
- **…and the flip side, because "it costs a turn" was used to defer measurable work twice: a CLI's own
  READ-ONLY subcommands are a free measuring instrument.** `--help` is the obvious one, but a CLI that
  manages configuration usually has a command that PRINTS what it would use, and printing config spends no
  tokens. Measured 2026-08-05 while closing CLI14: `codex mcp list -c 'mcp_servers.x.command="node"' …` and
  `codex mcp get x …` echo back the server codex actually registered, which turned every `-c mcp_servers.*`
  key from documented-not-measured into measured; the claude side was settled by dropping a candidate
  document in as a project `.mcp.json` and reading back `claude mcp list`. **Before writing "unverified" into
  an XML doc, look for the read-only subcommand** — CLI13 waited on "an unmeasured CLI" when
  `codex exec resume --help` had been free all along.
- **A subcommand you rely on may not exist on an OLDER install — pin a FLAG so it fails loudly.**
  `auth status --json` sends `--json` explicitly even though it is the CLI's default (measured on v2.1.220),
  precisely because a build predating `auth` rejects the unknown *flag* (exit non-zero, no turn) instead of
  billing a turn to answer the sentence "auth status". Same reasoning as above, one step further out.
- **Trusting the exit code over the machine-readable answer.** A signed-out `auth status` may report its
  state AND exit non-zero — that's an ANSWER, not a broken backend. Parse first, then fall back to the exit
  code (`CliProviderEngine.StatusAsync`). And parse the WHOLE body: the `Tail()` helper keeps the LAST 500
  chars, which would decapitate a JSON document.
  **The COMPLETION path had the same defect and shipped with it** (fixed 2026-08-05, `docs/FIXES.md`):
  `CompleteAsync` returned on a non-zero exit *before* parsing stdout, so a turn that reported its failure in
  band AND exited non-zero was classified from whatever was on stderr — measured as `Failed` /
  "exit 1: Reading prompt from stdin..." for a 401 that should have been `AuthFailed` (which benches the
  host; `Failed` merely advances). **The generalisation, since one seam having this fixed did not stop the
  next one shipping it: whenever a backend can answer in TWO channels, decide the precedence explicitly and
  pin it with a test.** The backend's own words outrank the exit code every time; the exit code is context
  for the detail, not the reason.
- **Re-implementing the CLI rules for a new CLI backend.** Everything above lives in
  `CliProviderEngine` (Core, `Lyntai.Llm.Cli`); a new CLI is an `ICliProviderDialect`, never a fresh
  `ILlmProvider` (`docs/DECISIONS.md` D21). The reason these traps were fixable at all is that there is now
  ONE copy.
- **Assuming a non-zero exit means failure — a CLI can report failure IN BAND and exit 0.** Measured on
  codex-cli 0.146.0: a 401 turn prints `{"type":"turn.failed","error":{"message":"… 401 Unauthorized …"}}`
  and the process still exits cleanly. Map it to `CliOutputEvent.Failure` so the message is classified
  (`AuthFailed` cools the host; a bare `Failed` just advances). Ignoring it yields "no output produced" —
  right verdict class, no reason, wrong routing. **And it can do BOTH:** measured 2026-08-05, an expired
  login prints `turn.failed` AND exits non-zero, so "in band" and "non-zero exit" are not two disjoint
  cases to branch on — see the exit-code-precedence entry below.
- **…and the mirror image: treating every error-ish line as terminal.** Also measured on codex: a bare
  `{"type":"error","message":"Reconnecting... 2/5"}` and an `item.completed` whose item type is `error`
  ("Model metadata not found") both appeared in a run that went on to **succeed**. Only the terminal event
  (`turn.failed`) may fail a call, or healthy calls die on a retry they recovered from.
- **A neutral working directory can break a CLI that expects a repo.** The engine spawns from a temp dir on
  purpose (§6 hygiene). codex refuses to run outside a git repository, so its dialect MUST pass
  `--skip-git-repo-check` — every completion would fail on a perfectly good install otherwise. Check what
  your CLI assumes about its cwd. **And the flag is needed on the AGENT path too, where the cwd is the
  caller's project**: that reads as "obviously a repo" on a developer's machine and is very often not one in
  a shipped bundle — a passing test that hides a shipped failure. Both codex paths build argv from
  `CodexExecArgs` for exactly this reason; a second copy is a second chance to lose the flag.
- **Adding a SECOND seam over the same CLI without sharing the wire knowledge.** `CodexAgentSession` reads
  the same JSONL as `CodexCliProvider`, so the vocabulary and the non-terminal-`error` rule live once in
  `CodexEnvelope`. Two readers of one wire format drift, and the drift is invisible until the halves disagree
  about whether a turn failed.
- **Mapping a wire format you have not measured, name by name.** The codex agent session's tool-step half is
  INFERRED (the measured capture ran no tools). It is written **shape-driven**: any unknown item type becomes
  a tool step under the BACKEND's own name carrying the BACKEND's own payload, nothing renamed or normalised.
  **Be precise about what that buys, because the first draft of this entry was not:** it guarantees *no
  payload is invented or dropped* and *every uncertainty stays inside the tool-step half* — it does NOT
  guarantee the right KIND of event. `CodexAgentReader.ReadItem` reaches the tool arm by **elimination**
  against three names, one of which (`reasoning`) is itself a guess, so a renamed `reasoning` surfaces the
  model's thought as a *fabricated* `ToolCall`. A tool step's **kind is provisional, its payload is
  reliable** — tell consumers to switch on `ToolCall.Name`. Mark every inferred member as inferred in the
  XML docs — and where a guess would COST something (codex reads an unrecognized subcommand as a prompt and
  spends a turn), refuse instead of guessing, and refuse instead of silently ignoring. A safety claim that
  overreaches is itself a documented-not-measured surface (`docs/DECISIONS.md` D35).
- **Trusting an explicit command without checking it exists.** For a PORTABLE install (an app's own bundled
  CLI copy) `IsAvailable` must verify presence — `ProcessRunner.CommandExists`, which also accepts an
  extensionless launcher with a spawnable sibling. Returning true for a path that isn't there turns a
  skippable candidate into a failed turn.
- **Forwarding a free-form option value straight into argv.** `ProviderLoginRequest.Mode` /
  `ProviderInstallRequest.Version` are free-form so other backends fit the contract — but an adapter must
  REFUSE a value it doesn't recognize instead of synthesizing `--<whatever>`, and must refuse a flag-shaped
  value in a data slot (`Email: "--dangerously-x"`, `Version: "--force"`). `ArgumentList` prevents *shell*
  injection, not the backend's own argument parser reading your value as an option.
- **Spawning a Windows CLI by its EXTENSIONLESS npm/nvm shim** — a global npm install writes three
  launchers side by side (`claude`, `claude.cmd`, `claude.ps1`); the extensionless one is a POSIX `sh`
  script, and CreateProcess rejects it with *"The specified executable is not a valid application for this
  OS platform"*. Green on every dev box whose `where.exe` happens to list the `.cmd` first, broken on the
  one whose PATH/PATHEXT doesn't — and broken for any caller handed the bare shim path directly
  (`CLAUDE_CMD`, a BYO command). `ProcessRunner.ResolveLauncher` swaps in the spawnable sibling; keep shim
  handling THERE, never at a call site, or the paths that don't go through it (the turn-free probe/update
  seams did) silently regress on shimmed installs.
- The retry in `CompleteJsonAsync` must **differ** from the first attempt (feed back the bad reply + a
  corrective instruction) — re-sending the identical request to a temperature-0 model just repeats it.

## Provider lifetime, cooldown & admission (`Lyntai.Lifecycle`; details in `docs/DECISIONS.md` D30)

Every one of these is a *silent* failure: the build is green, the tests are green, and the damage is a
benched tenant, an unbounded engine or a render nobody cancelled.

- **Hoisting a member into a new BASE interface, and deleting it from the derived one, breaks every
  pre-compiled caller.** Adding a base interface is binary-safe; removing the member from the interface that
  used to declare it is not, and the refactor that does both in one step reads as pure cleanup. A consumer
  compiled against the old surface emits `callvirt ILlmProvider::get_Id`, and member resolution **does not
  walk base interfaces** — so `provider.Id` throws `MissingMethodException` until that assembly is
  *recompiled*, which upgrading a package reference does not do. Nothing in this repository catches it:
  `check-warnings` is silent, the API baseline shows a line moving from one interface to another, and
  **`consumer-smoke` cannot see it at all** because it rebuilds its consumer from source every run. To test a
  binary-compatibility claim you must compile a probe against the OLD assembly and run it against the new one
  (a `callvirt` on a `null` argument is enough — `NullReferenceException` means the member resolved,
  `MissingMethodException` means it did not). `ILlmProvider` and `IGenerationProvider` therefore keep their
  own `new string Id { get; }` next to `IProviderIdentity`; the declarations are the compatibility, and a
  test pins them. Implementors are unaffected either way — one implicit `public string Id` satisfies both
  slots — so an implementor-only compatibility check proves nothing about callers.
  **The next two places this can happen, named so nobody has to rediscover them:** `IScorer`
  (`src/Lyntai.Core/Cortex/IScorer.cs`) and `ICliProviderDialect`
  (`src/Lyntai.Core/Llm/Cli/ICliProviderDialect.cs`) each declare their own `string Id { get; }` with exactly
  the shape `IProviderIdentity` supplies, so both look like leftovers a tidy-up should hoist. Neither derives
  from `IProviderIdentity` today, and neither should be *changed to derive from it by deleting its own
  declaration* — that is the same `MissingMethodException` for every pre-compiled caller of `scorer.Id` or
  `dialect.Id`. If either ever gains the base interface, it keeps its own `new string Id { get; }` too, and
  gets a line in `ProviderIdentityTests` alongside the two provider seams.
- **Disposing a replaced instance aborts in-flight work.** Retiring an entry looks like it should clean up
  after itself, and "clean up" reads as `Dispose`. It isn't: retirement removes the entry and drops the
  pool's reference, and the runtime reclaims the instance once the last caller finishes. **Without leases a
  pool cannot know when that is**, so disposing on retirement means disposing while callers may still be
  running — and with configuration arriving from a source that changes at any moment, a routine poll then
  kills a healthy render that had minutes to go. `IHttpClientFactory` is the model: an expired handler leaves
  the lookup so new callers get the fresh one, while existing users keep the old one until they're done.
  **Expiry is not disposal.** Pinned by a disposable fake whose flag must stay false through `Retire`,
  `RetireSlot`, LRU eviction and idle eviction.
- **Cooldown keyed on the provider id benches other tenants.** The key was `generation::{providerId}`. With
  two configurations of one backend live under different credentials, the one that exhausts its quota benches
  the other, whose key was fine — while two consumers of the same downed self-hosted host *should* share a
  bench and don't. Key on the `ProviderKey` (`configuration` delegate → `pool.TryGetKey`) and both are right;
  key on the id and exactly one is wrong, only in production, and only under multi-tenancy no test simulates.
- **A per-instance concurrency semaphore admits everyone under a no-reuse strategy.** A `SemaphoreSlim` field
  on a provider (or on a decorator over one) bounds anything only while the instance is shared. Register
  `TransientProviderPool` and every call builds its own limiter with its own fresh permits, so the local
  engine the limit exists to protect thrashes exactly as it would with no limit configured. Keyed and shared
  (`ProviderAdmission`) is the only shape that survives both strategies.
- **A key derived from the options object is right for some backends and silently wrong for others.** Four of
  the five generation options types are records with `init` members and compare structurally; but
  `LocalDiffusionOptions` is a plain class and compares by **reference**, and `ComfyUiOptions.Kinds` /
  `FalQueueOptions.Kinds` are `IReadOnlyList<string>` members that record equality also compares by reference
  (both defaults are collection expressions evaluated per instance, so even two default-constructed options
  compare unequal). So automatic derivation reuses correctly for **two** backends and rebuilds-or-reuses
  arbitrarily for **three**. (The measured basis, so the next audit can re-check the tally without re-deriving
  it: `Automatic1111Options` and `OpenAiImageOptions` are the two with no collection member at all — only
  `string`/`int`/`double`/`TimeSpan`.) Name every contribution
  (`ProviderKey.For(id).With("baseUrl", …).WithSecret("apiKey", …)`) so a forgotten member is visible in
  review — and include values the backend resolves at **runtime** (a downloaded engine's binary/model paths
  appear when the download completes; the saved configuration has not changed, yet a pooled instance holding
  empty paths keeps failing forever).
- **Decorating a provider erases its optional capability interfaces.** The generation seam expresses
  long-running and streaming delivery as *additional* interfaces the router type-tests:
  `if (provider is not IGenerationJobProvider job) continue;` (`GenerationRouter.SubmitAsync`). Any wrapper
  implementing only `IGenerationProvider` makes a queue backend invisible, so **every video render stops
  routing while every image render keeps working and every inline-only test stays green.** This is why
  admission is applied by the router rather than by a decorator — and the trap applies to *any* future
  wrapper (telemetry, retries, redaction), not just this one. If you must wrap, forward every optional
  interface the wrapped instance implements, and prove it with a test that submits a job.
- **A `using` that is one frame shallower than you think.** `GenerationRouter.GenerateAsync`'s `Surface` arm
  `return`s from inside the fallback switch, which is safe **only** because the admission permit is held in
  `AttemptAsync`, one frame deeper. Inlining `AttemptAsync` in the name of simplification turns that return
  into a permanent gate leak. More generally: every `ProviderAdmission.EnterAsync` result must reach a
  `using` on **every** path — fallback, refusal, cancellation — because a caller that abandons the `ValueTask`
  without disposing the handle pins its gate forever.

## Storage (details in `storage.md`)

- **An admission guarantee must survive the LIMIT, not just the WHERE.** "Admitted unconditionally" that
  is implemented only as a predicate is still excluded by `ORDER BY … LIMIT` whenever the ordering key is
  the axis the protected row is weakest on. All three `IMemoryGraphStore` backends filtered authoritative
  material into every seed candidate set correctly, then ordered by recency alone and took a capped
  count — so a long-quiet exact fact, which by definition has the lowest recency in its scope, sorted last
  and was cut before ranking ever saw it. Grade now leads the seed ordering ahead of recency on every
  backend. Check both halves whenever a query claims to exempt something.
- **A bound configured on one SCOPE and enforced on another is not a bound — and the tests will only ever
  exercise the regime where the gap is invisible.** The sequel to the entry above, and it shipped inside the
  fix for it. `GraphMemoryOptions.AuthoritativeReserve` is a per-ENGINE option that reserves recall slots;
  `MemoryQuery.Limit` arrives per QUERY. Only the `null` DEFAULT was capped by the limit (`?? limit`), so an
  explicit value larger than a caller's limit overran it outright — measured at reserve `5` / `Limit: 2`,
  **three items came back for a limit of two, and not one ordinary hit**, against a promise written down in
  three maintained documents. `5` is a sensible bound against the default limit of `10`; the trigger is
  simply a caller passing something smaller, which is what a prompt budget does.
  <br>**Both existing tests used a reserve BELOW the limit** (`1` against `3` and `4`) — the only regime where
  the missing cap cannot be observed — and nothing anywhere asserted the flat property that a recall returns
  at most `Limit` items. **Whenever two limits come from different scopes, write the fact that the narrower
  one still binds**, and pick fixture values from the regime where they DISAGREE; a fixture in which the
  configured bound is always the smaller one tests the reconciliation you did not write.
- **Missing FTS `'delete'` trigger row on delete/update** — silent index corruption. Three triggers,
  always.
- **A double column read without `CAST(x AS REAL)`** — the SQLite integer-affinity trap.
- **Opening a connection outside the factory** — loses per-connection `foreign_keys=ON`, so cascades
  silently stop.
- **Reusing a migration number** — silently skipped, so the migration never runs. Use
  `dev.mjs new-migration`.
- **`ORDER BY` on a non-unique column with no tiebreaker** — nondeterministic on ties.
- **A `Relevance` normalized by RANK POSITION, not by score margin, makes a bounded rank boost's effect
  CANDIDATE-COUNT DEPENDENT.** `SqliteMemoryGraphStore.SeedAsync` (and the other two backends' own
  orderings) reports `Relevance = 1 - i / rows.Count` — with only two candidates that is *exactly* 1.0 and
  0.5, a fixed 2× gap no matter how close the underlying `bm25` scores actually are; with ten candidates the
  same 2nd-place gap shrinks to 10%. Found 2026-08-09 closing the memory-retention-model Plan 2
  (`docs/DECISIONS.md` D45, `GraphMemoryRankingTests`): a first pass shipped a LOGARITHMIC rank boost
  (then `GraphMemoryOptions.SalienceRankWeight`; the property moved to
  `Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight` when the ranking-policy seam landed,
  same name, same default) defaulting ON, and the default value could not clear the 2× worst-case gap even at
  the maximum salience a real appraiser can report — which is *why* the owner then ruled the boost OFF by
  default in `docs/DECISIONS.md` **D45** (salience means "does not fade away", not "first priority"; store
  admission, which is unconditional, already delivers the former). The reusable trap either way: **a test (or
  a consumer's own expectation) for any rank-lifting signal over this store needs a result set large enough
  that the position-based gap it is fighting is smaller than the boost it is proving** — two candidates is
  the WORST case, not a representative one. `docs/task-archive.md` Part 53 item 1 (the ranking-policy seam
  itself, shipped 2026-08-09) is where this was found; the salience-measurement residue is `TASKS.md`
  Part 65.
- **One stored value read at N sites grows N coercion rules, and the divergence is silent.** Found
  2026-08-09 in the same work: `salience` was read at FOUR sites with THREE rules — both SQL stores coerced
  `IsFinite ? Math.Max(1, x) : 1` into their promoted column, the in-process store ordered the raw bag value,
  and the engine's rank boost used a bare `Math.Max(1, x)`. So `{salience: 0.5}` admitted level with
  unappraised rows on SQL and BELOW them in-process — same data, same query, different backend — and a `NaN`
  (reachable through the public appraiser seam) crashed the write on one backend, sorted to the TOP on
  another, and made every candidate's rank `NaN` in the engine, which empties the recall entirely because
  `NaN >= floor` is false. **Put the coercion in ONE public function on the value's own type and make every
  reader call it** (`MemorySignals.Salience`), the same discipline
  `ModulatedRetrievability.Declared` already applies within one file — and remember `Math.Max` PROPAGATES
  `NaN` per IEEE 754, so a clamp is not a finiteness guard.
- **A clamp is not a finiteness guard — `Math.Max`/`Math.Min`/`Math.Clamp` PROPAGATE `NaN` (IEEE 754).**
  Promoted to its own entry because it landed **twice in four days in the same subsystem**, the second time
  *in a file whose neighbour documents the identical fact*: `ModulatedRetrievability` already warns about
  `Math.Max(1, NaN)`, and `DsrRetrievability.Reinforce` still shipped `Math.Max(0, increase)` as its safety
  floor. Both authors read the clamp as "this can only make it safe". Being written down was not enough —
  **route an implementer to this document in the dispatch**, because nothing surfaces it otherwise
  (`.claude/rules/skills-workflow.md`).
  Write `double.IsFinite(x) ? Math.Max(0, x) : fallback` wherever the input is derived arithmetic rather
  than a literal. The consequence is never local: `GraphMemoryEngine` feeds `Reinforce` straight into
  `TouchAsync`, so on the in-process store one `NaN` stability is **persisted permanently** — and `NaN`
  compares false against every threshold, so the entry neither ranks, prunes, nor reports as broken.
  <br>**Third occurrence, 2026-08-14, and it is the one that changes the prescription.** The two above were
  authors reading a clamp as a safety guard. This one is different and worse: `DsrRetrievability.Reinforce`
  had the guard, in that exact form, with a comment explaining that `Age` arrives per-call and therefore
  cannot be validated at construction — and then the difficulty law was added as a **second reader of the
  same `Retrievability(state)` value**, re-derived the finiteness question independently, and concluded the
  opposite in writing ("every term feeding `D''` is provably finite"). A `NaN` age produced a `NaN` grade and
  a persisted `NaN` difficulty, through `ExpandAsync`, which is the act this library's own docs recommend
  reinforcing on. **So the rule is not "remember to guard" — it is that a guard belongs to the VALUE, not to
  the call site.** When a second consumer of a already-guarded expression appears, it does not inherit the
  reasoning, and the only two shapes that survive are pushing the guard down to where the value is produced
  (here, `DerivedGrade` screening the retrievability once for both readers) or the `MemorySignals.Salience`
  treatment — one public coercion function every reader calls. Two call sites reasoning separately about the
  same double is the same defect as two call sites coercing it separately, which is the entry above this one.
  <br>**Fourth occurrence, 2026-08-16, on the WRITE path — and the prescription two lines up is what missed
  it.** `GraphMemoryEngine` shipped `_policy.InitialStability * Math.Max(0, tick.Encoding)` and
  `Math.Max(0, tick.Position)`, both persisted, both fed from the public `IMemoryAgePolicy.Advance` seam. The
  rule was already written HERE, in this entry, naming the exact replacement — and the search that would
  have found it is not "where do we clamp a derived double" but "**what does this seam RETURN, and is any of
  it stored**". `IMemoryAgePolicy.Age` carries a long paragraph promising a non-finite return "cannot corrupt
  the store, the review log, or anyone else's recall" because everything downstream that would persist the
  poison is defended. That was true of `Age`, which is never persisted, and false of `Advance` on the very
  next member — so the doc's own confidence was what made the gap invisible. **A contract paragraph that
  reasons about one member of a seam does not cover its siblings**, and the more thorough it is, the more it
  reads as though it does.
  <br>The two backends then disagreed about the poisoned write, which is worse than either answer: Postgres
  added `NaN` into the engine's running position permanently (every LATER entry reports a non-finite age, so
  nothing ranks and nothing prunes), while SQLite refused the bind and threw. Fixed by coercing to `1` —
  taken from `MemoryTick.One`'s existing definition of an ordinary write, rather than inventing a neutral.
- **A ceiling written as a bare `Math.Min(grown, max)` is a CUT, not a cap, for anything already above it —
  and the value it cuts is usually persisted.** `DsrRetrievability.Reinforce` shipped that shape through
  2.5.x, so recalling an entry whose STORED stability already exceeded `MaxStability` wrote the ceiling back:
  a stored `100000` returned `2000`, a **50× shortening**, violating `IMemoryRetrievabilityPolicy.Reinforce`'s
  own written "never smaller than the current one" guarantee — which the interface then *disclosed* for a
  release rather than closing. Reachable by lowering the ceiling under an existing corpus, or by any value
  written outside the policy. **The correct shape was already in the same file, one method away**:
  `EffectiveStability` has always written `Math.Max(current, Math.Min(current * factor, max))`, where the
  outer floor makes the bound cap GROWTH without ever acting as a CUT — an over-ceiling entry is *frozen*,
  which is all a growth ceiling ever claims to do. Two things generalise. **(1) Ask what a bound means for an
  input already past it**: "cannot grow beyond X" and "is never above X" are different promises, and `Math.Min`
  alone silently implements the second. **(2) A monotonicity guarantee needs a fixture that can actually
  violate it** — the contract fact that should have caught this (`Reinforcement_never_shortens_a_memory`) only
  ever ran on a state at `InitialStability`, structurally far below any ceiling, so it passed for two years
  while the guarantee was false. Fixed 2026-08-11 (`docs/task-archive.md` Part 54, DSR2).
- **A record where ONE field is domain-guarded and its neighbours are not is more dangerous than one with no
  guards at all** — the guard reads as evidence the record was audited. `DsrOptions.Decay` got a validating
  `init` in 2026-08-09's Task 1 (after a review found `Decay = 0` made everything permanently unforgettable
  while pruning still deleted); `InitialStability`, declared three lines away and equally load-bearing, kept
  none, and `InitialStability = 0` then reached `Math.Pow(0, -0.4)` = `+∞` → `0 × (1+∞)` = `NaN`. When a
  review forces a guard onto one option, **audit every other field of that record in the same fix**, and
  prefer a test that walks the whole options surface over one that pins the field just fixed.
- **A test whose recall silently returns nothing exercises only the write path — and stays green.** Hit
  twice, four days apart, both times while reaching for `InMemoryMemoryGraphStore` "for speed": 2026-08-09 a
  recall-quality guard measured a **~93% miss rate** and switched to `SqliteMemoryGraphStore`; 2026-08-10 an
  age-primitive identity test instrumented at **writes 25, queries 69, `TouchAsync` calls 0** — every recall
  returned `MemoryRecall.Empty` *before* `ReinforceAsync`, so deleting the store's entire touch stamping left
  the test green.
  **The CAUSE was fixed in 3.0 and the LESSON was not.** The cause was that only SQLite's FTS path split a
  query into terms; every other path matched the whole query as one contiguous substring, so
  `"item topic0 repeat0"` could never match `"item topic0 covers <filler> ordinary material…"`.
  `SearchTerms` now gives every backend the same split (`docs/DECISIONS.md` D55), so the old prescription —
  *any test whose subject is the RECALL or TOUCH path must run on SQLite* — no longer holds **for that
  reason**. Prefer SQLite anyway when the subject is RANKING, which the in-process store still has none of.
  What survives unchanged is how both were caught, and neither was caught by reading: **mutate the behaviour
  the test claims to cover and watch it stay green.** Note also that the second one shipped a guard
  *intended* to catch this — `Assert.True(comparisons > ids.Count)` — which 25 writes alone satisfy at 325
  comparisons, so it could never detect zero touches. **A guard that cannot observe the thing it guards is
  worse than none: it reads as coverage.** And the wider one, which is why this entry keeps its history:
  a cross-backend difference filed as *by design* had been quietly failing recall on two of three backends
  for a year, defended by a test that asserted the divergence rather than questioning it.
- **`EdgeHalfLife` exists on TWO options records, with the same name and the same default `100`, governing
  DIFFERENT things.** `GraphMemoryOptions.EdgeHalfLife` decays an **edge's weight** during traversal — it is
  what stops the graph saturating, and it is read by the engine for every arm regardless of which
  retrievability policy is installed. `DsrOptions.EdgeHalfLife` decays **connection strength** inside the
  curve, feeding `EffectiveStability` and therefore retrievability. Found 2026-08-11 writing the 3.0
  migration guide; a review three days earlier had already been wrong about this once in the other
  direction, generalising "the shared one governs edge weight for every arm" into "nothing reads the
  policy's own copy" — which was false and reached a decision record before it was caught.
  **Both default to 100, so they agree by coincidence and nothing surfaces the confusion until someone tunes
  one and expects the other's behaviour.** When you touch either, name which one in the same sentence;
  "the edge half-life" is ambiguous in this subsystem and always has been.
- **A PERMANENT change driven by the system's own retrieval decisions is the dangerous shape — not merely an
  unbounded one.** Five studies on 2026-08-12 across `GraphMemoryEngine`'s three retrievability-raising
  mechanisms, including two that refuted the investigator's own earlier framings, which is why the
  qualifications below are part of the entry rather than trimmed out of it.
  <br>**Measured:** salience (keyed on write-time content novelty) HELPS; the age reset a recall performs
  (age → 0) HELPS; stability growth HURTS badly, and **in every form tested** — the shipped compounding rule,
  a capped one, and one computed purely from the entry's recall COUNT so that it cannot compound by
  construction. Not growing at all beat all three on every corpus shape.
  <br>**Two framings this refuted, both plausible and both wrong.** "Reinforcement is net-harmful" was wrong
  because every arm still had the age reset on — the harm is growth alone. "A bounded effect is safe, a
  compounding one is not" was wrong because the non-compounding form still lost to not growing.
  <br>**What fits all of it**: the age reset EXPIRES (the entry decays again at the same rate), while growth
  PERSISTS (it decays slower forever), and salience persists but is keyed on content rather than on
  retrieval. So the combination that does damage is *permanent* × *retrieval-conditioned* — the system banks
  its own ranking error instead of letting it wash out. **The question to ask of any such mechanism: does its
  effect expire, and if not, is it driven by something other than the system's own output?** Offered as the
  hypothesis that survives, not as a proven law — it is the third framing in one day and has not been tested
  on its own terms.
- **A feedback loop conditioned on the system's OWN output is not learning, and it reads like learning at
  every call site.** Measured 2026-08-12 across three studies (`docs/task-archive.md` Part 64,
  `docs/DECISIONS.md` D53):
  `GraphMemoryEngine` reinforces every node `RecallAsync` returned, which sounds like the documented promise
  ("material you keep coming back to becomes durable") and is not it. What the ranker RETURNED is the
  ranker's own opinion; reinforcing it compounds that opinion rather than the entry's usefulness, so recall
  quality gets measurably WORSE the more reinforcement happens — monotonically, concentrated in exactly the
  class where the same entries recur. **The tell to generalise: when you import "use strengthens X" from a
  domain, check whether that domain's "use" was VERIFIED.** FSRS's learner knows whether they recalled
  correctly; a query-driven engine does not, so the same rule becomes positive feedback on its own mistakes.
  The fix is not to weaken the loop but to condition it on an act that carries evidence — here `ExpandAsync`,
  a caller choosing to pay for full content, which this engine already produces and then weights identically
  to a guess.
- **An English, space-separated test corpus measures the FRIENDLIEST tokenization a trigram-FTS store
  supports, and every number it produces is a best case.** Measured 2026-08-12: the same cluster-recall
  question scores ~**0.47** miss with a cue whose only live token reaches the target, and ~**0.82** with a cue
  carrying ordinary words that also appear elsewhere — a gap far larger than any policy effect measured that
  day. **Cue quality dominated every knob in the subsystem.**
  <br>The trap is the reflex, not the number: the overlapping cue was first dismissed as a stopword
  "mistake" and deleted. That is wrong here, because this store tokenizes FTS as `trigram` precisely so
  non-Latin text works, and **under trigram matching almost any two texts share trigrams — for CJK there is
  no stopword to strip and the contention is unavoidable.** So the "contaminated" case was the representative
  one for a large part of the audience, and the "clean" one was the artificial best case.
  <br>**Before calling incidental match contamination, ask which tokenizer the store actually uses and which
  languages it claims to support.** A repository named 灵台 with CJK storage tests does not get to measure
  only English.
- **A best-effort catch turns a BUG into "nothing matched", and you will debug the wrong layer for hours.**
  Measured 2026-08-13 adding query-time semantic seeding: `GraphMemoryEngine.RecallAsync` wraps
  `GatherAsync` in `catch (Exception ex) { _logger.LogWarning(...); return MemoryRecall.Empty; }` — a
  deliberate best-effort promise, so a storage outage degrades to "no memory" rather than a throw. The cost
  is that **any** defect in the gather path produces an empty recall, which is indistinguishable from a
  query that legitimately matched nothing. With no logger attached — the default in a hand-built engine —
  there is no signal at all. Two implementations were debugged blind against a feature that was, on the
  third attempt, correct all along.
  <br>**Before concluding "the feature does not work", attach a logger and assert it stayed silent.** The
  fix is not to remove the catch (the promise is right) but to make the silence assertable:
  `SemanticSeedProbeTests` asserts an empty warning list, so a swallowed failure fails the test rather than
  looking like a null result. The general rule: **wherever a catch converts an exception into a plausible
  empty value, a test must be able to tell the two apart** — and that test is the one to write FIRST when a
  new code path in that method appears to do nothing.
- **A measurement that cannot observe a change reports "nothing moved", which reads exactly like "no
  regression".** Measured 2026-08-12 reconciling `GraphNode.Relevance` across the three backends: both
  `MemoryDefaultRecallQualityTests`'s fixed-corpus pin and the 28-minute `memory-sweep` came back
  bit-identical, which looks like strong evidence the change is safe. It is not evidence at all — `MemoryCorpus`
  contains **zero `MemoryGrade` references**, so it holds no authoritative material, and the changed
  behaviour (what an authoritative node the query did NOT match reports) is structurally unreachable on that
  instrument. The right claim is "not exercised", and the change is carried by contract facts on all three
  backends instead. This is the same shape as the guard-that-cannot-observe entry under Testing below, one
  level up: **before citing an unchanged measurement as reassurance, ask whether that instrument can express
  the thing you changed** — and prefer a corpus/fixture audit (`grep` for the feature) over inferring it from
  a stable number.
  <br>**Sequel, 2026-08-13, and it is the reason this entry is worth re-reading rather than filing away:**
  the gap above was correctly identified, correctly written down here AND in `CLAUDE.md` — and then left
  open, because documenting a blind instrument feels like handling it. Teaching the corpus to express the
  promise (`CorpusShape.AuthoritativeCount`, opt-in and byte-identical when unset) took one afternoon and
  immediately found the library's **highest-priority** promise broken in all five languages — objective (1),
  the one with no acceptable failure rate, cut by `Take(limit)` for the whole life of the feature
  (`DECISIONS.md` D56). **A documented blind spot is still blind.** If the instrument cannot express the
  promise, the fix is the instrument, and the cost of not fixing it is bounded only by how important the
  unmeasured promise happens to be.
  <br>**Sequel, 2026-08-21, and it is the trap on the OTHER side of that advice.** "Build the instrument" was
  about to be applied to `TASKS.md` Part 65's `many-candidates` item by sweeping
  `ReciprocalRankFusionOptions.SalienceWeight` — a knob that already exists, over a corpus that already
  exists, mirroring `MemorySpacingSweep` almost line for line. It would have returned a **perfectly flat
  curve**, and the flatness would have been an artifact end to end. Two facts compose, and neither is visible
  from the sweep: `GraphMemoryEngine.Probe` returns `(Novelty 0, Comparables 0)` when there is no vector
  search, so **without an embedder `StructuralSaliencePolicy` declines on every single write** and salience is
  uniformly absent; and `ReciprocalRankFusionPolicy` ranks by COMPETITION (**D82**), so a signal on which
  every candidate ties contributes the same constant term and **cannot move the ordering at any weight**. Arm
  0 and arm 4 are then the same engine.
  <br>The output would have read as a clean exoneration — flat, tight CIs, every control green, because every
  control that exists checks that the arms carry their own *weights*, which they would have. **Ask not "does
  each arm carry a different knob value?" but "does the SIGNAL that knob scales actually VARY on this
  corpus?"** — and assert the second one in the study, as a distinct-value count, the same way
  `MemoryCorpusGoldenTests` pins that a language arm genuinely differs. A knob that scales a constant is
  unmeasurable, and nothing about the shape of the experiment says so.
- **The guard that stops a contract fact being wired to one backend only counts the CONTRACT's declarations,
  not what each backend CALLS — so it is blind in exactly one direction.** Measured 2026-08-14: all 68
  `MemoryGraphStoreContract` facts are wired on all three backends today (68/68/68), so this is latent, not
  live. But `PostgresStorageTests`' `Assert.Equal(declared, covered)` compares a reflected count of the
  contract's public statics against a hand-bumped literal — it catches a fact added and wired NOWHERE, and a
  fact added and wired to InMemory/Sqlite but not Postgres, and it does **not** catch a fact added and wired
  to **Postgres alone**: the author bumps `covered`, the assertion passes, and the invariant the entry above
  exists to protect is enforced on one backend. Closing it properly means driving all three through a
  reflection-fed `[Theory]` so exhaustiveness is structural rather than counted — deliberately not done days
  before a freeze, because it restructures ~200 tests to close a gap with no live instance. **Re-measure with
  a per-backend call census (parse each test file for `MemoryGraphStoreContract.<name>`) rather than trusting
  the count**, which is how the 68/68/68 above was established.
- **A cross-backend invariant enforced on ONE backend's test class is not enforced.** The non-finite-salience
  guard above was pinned only in `SqliteMemoryGraphStoreTests`, so the in-process store's own divergence
  survived a full review. If a fact is about the CONTRACT, it belongs in `MemoryGraphStoreContract` and must
  be wired to all three backends (`InMemory`, `Sqlite`, `Postgres` — the last also bumps the `covered` count
  that guards against a silent skip). Keep a backend-specific assertion only where it genuinely cannot be
  portable — reading a raw column, for instance.

## DI / config

- **Calling `AddLyntai` twice** — registers a second `LyntaiOptions` (shadows the first) while both
  calls' providers pile into the collections. It now throws; compose everything in one callback.
- **A singleton capturing a scoped/transient dependency** (captive dependency). Providers/stores are
  singletons; resolve per-call transients (like an `HttpClient` from `IHttpClientFactory`) inside the
  method, not the constructor.
- **A documented option/env-var that isn't wired** — the `LYNTAI_MODEL_<CONSUMER>` override and the OTel
  cost attribute were both documented but silently dropped, and no test caught it. When you add a
  documented knob, add the test that exercises the documented path.
- **TWO construction sites for one type, each with its own copy of a long optional-argument list, is how a
  documented knob stops being wired — and the compiler is structurally unable to notice.** Measured
  2026-08-14 (`docs/FIXES.md`): `MemoryEngineBuilder` built `GraphMemoryEngine` in both `UseGraph` (the
  configured path) and `UseBestAvailable` (what the one-line `AddMemory()` resolves to). `annotation:` and
  `verification:` were added to the first when those seams shipped and never to the second, so
  `AddMemory().AddMemoryVerification()` registered a policy that never ran, while the identical registration
  behind `AddMemoryEngine(…, e => e.UseGraph())` worked.
  **Three things had to line up, and they always will for this shape:** the parameters are OPTIONAL, so
  omitting them compiles; the fallback is a real, documented behaviour ("the model-free floor"), so nothing
  throws and no result is missing; and the only symptom is QUALITY, which a consumer enabling a feature for
  the first time has no baseline for. `check-warnings` cannot see it, the API baseline is unchanged, and
  every existing wiring test happened to exercise the other path.
  **The fix is one call site, never a second copy kept in step by review** — an override-taking private
  factory, with the zero-configuration caller passing nothing (null already means "take the container
  registration"). And the test that catches it must assert the policy was **CONSULTED**, not that recall
  still worked: a recording fake whose `Seen` list must be non-empty. This is the same family as the
  documented-but-unwired entry below, arrived at from a different direction — there the knob was never read,
  here it was read on one path of two.
- **A `TryAddSingleton` reached during `configure(builder)` BEATS `AddLyntai`'s own options-built
  registration.** All of this is in `src/Lyntai.Core/DependencyInjection/ServiceCollectionExtensions.cs`:
  `AddLyntai` invokes `configure(builder)` well before it calls `RegisterLlmFrontDoor`, and that method is
  where the `DeadHostTracker` built from `LyntaiOptions` is registered. **Names first, lines second** — these
  numbers rot, and this very entry was made stale by a change inside the branch that added it; follow the
  method names if a line disagrees. **It has now rotted TWICE** (a 2026-08-05 audit found all four numbers
  pointing at a blank line, a doc comment and unrelated calls), so the numbers are gone: cite `AddLyntai`,
  `RegisterLlmFrontDoor` and `RegisterProviderLifetime` by name, which is the only form that cannot rot a
  third time. So a `TryAddSingleton<DeadHostTracker>()` added inside a `Use*`/`Add*` extension reaches the
  collection FIRST, and `TryAdd` keeps the first — silently swapping the configured `DeadHostThreshold`,
  `DeadHostCooldown` and logger for the parameterless defaults, **for both domains**. Nothing in 1427 tests
  noticed; it was found by mutation (adding the line failed exactly one new guard test and nothing else) and
  confirmed twice. The rule:
  inside a builder callback, resolve what `AddLyntai` registers later with `GetRequiredService<T>()` — never
  seed it with a `TryAdd`, and if you need a service that must exist regardless, register it in the
  Register* block that owns it. `RegisterProviderLifetime` is all `TryAdd` deliberately for the mirror-image
  reason: everything it seeds is meant to lose to a host or a `Use*` call.

## Copying a rule copies its assumptions

- **A rule moved from where it was true to where it is not — the shape behind three of the four regressions
  a single review round introduced (2026-08-15, `docs/FIXES.md`).** Each looked like careful reuse of an
  existing, correct decision, and each carried an unstated premise that held only where it came from:
  · `ComfyUiProvider`'s poll rule — *"a 4xx is terminal, a 5xx is transport"* — copied onto
    `FalQueueProvider`. True for a loopback server that never rate-limits; on a hosted, paid, rate-limiting
    API one `429` permanently dead-lettered a render that was still running and already billed. **The
    premise "this backend does not rate-limit" was never written down, because where the rule was written
    it was always true.**
  · `IMemoryGraphStore.SeedAsync`'s portable guarantee — *"a node whose content contains a query token is
    found on every backend"* — read as a CEILING and used to justify narrowing the one backend that matched
    more. It is a FLOOR. **A minimum and a maximum are the same sentence in English**, and the difference
    only shows when you ask what the sentence FORBIDS.
  · `CompositeMemoryEngine`'s fan-out justification — *"removing one member and returning success leaves the
    blend holding the data"* — applied to the members that could remove and silently not to the ones that
    could not, which is the case it was written for.
  <br>**The fourth is the same shape aimed inward**: the same round taught `FalQueueProvider` to distinguish
  "never reached the backend" from "may have been delivered", and then wrote a `catch` in another file that
  treated every throw as ambiguous — turning a connection-refused blip into a dead-lettered job.
  <br>**Fifth instance, 2026-08-17, found by review rather than by a consumer:** `GenerationRouter.StreamAsync`
  carried the SUBMIT door's `NeverReachedTheBackend` filter on its catch — a filter whose premise is "the
  act of asking may have been billed", true of a queue submission and false of a stream open — so the one
  failure class fallback most exists for (a refused connection before the first byte) was the one that
  skipped fallback and escaped raw. The filter's own doc said "used ONLY on the submit path" the whole time.
  <br>**What to actually do**, since "review harder" is not a technique: when you reuse a rule, write down
  the premise that makes it true THERE and check it holds HERE. If you cannot name the premise, you are
  copying a conclusion rather than reasoning. And when a rule you just wrote is about a distinction (this
  versus that), grep your own diff for the other places that distinction applies — the round that
  articulates a rule is the round most likely to violate it elsewhere, because attention is on the sentence
  rather than on the code.

## Second doors

- **Inserting a member ABOVE an existing one strands that one's doc onto your new member.** Measured FOUR
  times on 2026-08-16/17, by the same hand, in one session — adding a private helper before its neighbour
  left the neighbour's `<summary>` (or, twice, only its `<param>` tags) attached to the newcomer, so the
  newcomer carried two docs and the member below carried none or half. It is a JSDoc problem too: the same
  edit in `check-counts.mjs` separated `countGuardTests`' block from its function.
  <br>**The insertion anchor is a declaration; the thing above it is somebody else's doc.** So the check is
  not "did I write my doc correctly" but **"what was already sitting above the line I anchored to?"** Anchor
  below the neighbour's doc, or move the doc with the declaration.
  <br>Every one of the four was caught by `check-comments`' stacked-`<summary>` rule and none by the compiler
  — duplicate `<summary>` is not a warning, the XML stays well-formed, and it SHIPS. The two `<param>`-only
  cases did warn (CS1571 duplicate param tag), which is why they were found in seconds and the summary cases
  were not found for a release.
- **An invariant DOCUMENTED in one implementation is not a contract, and the backend that wrote it down is
  usually the only one that keeps it.** Measured 2026-08-16, three times in one pass, all in storage:
  `InMemoryVectorStore` carried a top-k tiebreak and a doc calling it *"load-bearing, not tidiness"* while
  neither persistent backend had one; `MemoryGraphSql.MinimumStability` was hoisted with a doc saying *"two
  literals is how that happens"* while the in-process store was the second literal — and spelled it as a
  SUBSTITUTE where SQL FLOORS, which is identical at the value the guard was written for and different for
  everything between it and zero, on a DELETE path.
  <br>**The tell is a doc that argues for a rule in the first person.** Prose explaining why THIS class does
  something careful is evidence the author knew it mattered and no evidence that anyone else does — so the
  question is not "is this implementation correct?" but **"is this a promise, and if so what holds the
  others to it?"** In this repository the answer is a shared contract class every backend runs
  (`VectorStoreContract`, `MemoryGraphStoreContract`), never a per-backend test file.
  <br>A single-backend test file with a rule's name on it is the same smell as the doc:
  `InMemoryVectorStoreTiebreakTests` existed, was thorough, and proved the property for the one backend that
  already had it. **Moving a fact into the contract is the fix; adding a second per-backend test is the
  defect repeated.**
  <br>**And a green cross-backend run does not mean the backends AGREE for the same reason.** SQLite passed
  the tiebreak facts before the fix, because `PRIMARY KEY (collection, vec_id)` gives it an autoindex the
  plan happens to walk in `vec_id` order — an accident that a rewrite, an `ANALYZE`, or a different plan
  changes silently. When a contract fact passes on a backend you expected to fail, find out WHY before
  believing it.
- **A shared helper whose doc says "EVERY read site calls this" is making a claim nothing checks — and the
  site that does not call it is usually the newest one.** `MemorySignals.Salience` was extracted for exactly
  this reason and says so, naming the three readers that once "normalized the same value three different
  ways, which made identical data admit differently on different backends". Measured 2026-08-17:
  `SalienceRetentionPolicy` was a FOURTH reader, added later, that spelled the read out itself — and got it
  wrong in the one way the helper exists to prevent (`Math.Clamp` propagates `NaN` where the helper coerces
  it), returning a factor outside the range its own interface promises.
  <br>**The cheap detection, worth running whenever you extract a coercion:** grep for the raw accessor the
  helper wraps (here `Get(WellKnown.Salience`) and check every hit is inside the helper. A helper's
  usefulness is entirely in being universal, so the enumeration in its doc is load-bearing — and it is prose,
  which means it was true when written and nothing has held it true since. Prefer routing the new caller
  through the helper over adding a second correct copy of the guard: two correct copies is how the three
  incorrect ones started.
- **A SPEND cap is a capability too, and its second door is the one that moves the money.** Measured
  2026-08-16. `GenerationFetchTool` and `GenerationRenderJobHandler` both fetch a finished render; only the
  handler recorded its cost. Because a queue backend prices at FETCH — the only point the total is known —
  the unbilled door was the one carrying the entire cost, and `GenerationSubmitTool` then re-checked its cap
  on every submit against a total that could not grow. A configured cap never fired.
  <br>**The tell is a check and a record living on different paths.** A limit is two halves — something
  reads the total, something else adds to it — and reviewing either half alone reads as correct. So the
  question is not "is this path budgeted?" but **"where is the total INCREMENTED, and does every path that
  spends reach it?"** A cap whose total never moves fails silently and permissively, and looks like a
  generous limit.
  <br>Coverage was no help: a test already drove submit → status → fetch end to end and asserted nothing
  about usage. **Exercising a path is not testing its accounting.**
- **A capability enforced at one entry point is not enforced if a second entry point reaches the same
  objects.** Measured 2026-08-15. `ToolLoop.GatedInvokeAsync` runs every model-driven tool call through
  `IGuardRail` — that gap was found and closed for the tool loop in 2026-07 and recorded as a security item.
  The hosted MCP endpoint, added later and in a different package, executes **the same `ITool` instances**
  (`sp.GetServices<ITool>()`) through `ToolFunction`, which called `tool.InvokeAsync` directly. So an app
  that registered a guard had it applied on one door and silently skipped on the other — and neither
  `ChatOrchestrator` gate could see it, because gate 1 sees the user message and gate 2 sees only the final
  answer. A `grep` for `guard` across both MCP packages returned one unrelated comment.
  <br>**The tell is shared STATE, not shared code.** The two paths have nothing in common to review side by
  side; what they share is the collection they both resolve. So the question that finds this is not "is this
  code guarded?" but **"what else can reach these objects, and does it apply the same rules?"** — asked
  whenever a new surface is given the app's tools, stores, or provider collections.
  <br>The same shape is worth checking for any capability that reads as a global promise: guards, budgets,
  rate limits, redaction. Each is enforced by a specific wrapper at a specific door, and adding a door is
  the cheapest way to lose one.
  <br>**Third instance, 2026-08-16 — and this time the compiler found it, which is the point.** Adding
  `IGenerationRouter.StreamAsync` as a required member with NO default body turned "a decorator silently
  fails to govern the new door" into a build error naming both offenders — and there were **two**,
  `BudgetedGenerationRouter` and `RateLimitedGenerationRouter`, where only the first had been anticipated. A
  default interface implementation would have compiled clean and shipped an ungoverned path. **When adding a
  member to an interface the library itself decorates, no default body is the cheap gate**; it costs BYO
  implementers a compile error in a major, which is exactly when that is affordable. Note the limit, though:
  the compiler forces a decorator to HAVE the member and cannot tell a governed implementation from a
  pass-through, so the behaviour still needs a test per door (`GenerationGovernanceTests`).
- **A constant does not live in one place just because it was written once — and the copy nothing ENFORCES
  is the one that rots silently.** Measured 2026-08-16 making the local-diffusion size ceiling configurable
  (`docs/DECISIONS.md` **D68**). The hard-coded 768 lived in **three** places, and each was found a different
  way. The **scale** was obvious. The **rounding** re-clamped through a second copy, so raising the cap would
  have moved the scale and left every result pinned at 768 by the rounder — a knob that appears to work and
  cannot exceed its old value; found only by writing the test at 1024 *because* that is above the old
  constant. The **advertisement** — `GenerationCapabilities.Limits["max-width"]` — was found by a human
  reading the diff and asking where the number came from.
  <br>**The third is the general lesson.** `Limits` is documented as informational: *"the platform does not
  enforce them (only the backend knows the real rule)"*. So a stale value there fails no test, trips no gate,
  and breaks nothing — the backend simply **lies to consumers who plan against a published ceiling**. An
  enforced constant is corrected by its own failure; an advertised one has no such feedback, and the only
  thing that reads it is a person. **When a value becomes configurable, grep the number itself, not the
  identifier** — the identifier finds the code paths, and the literal finds the documentation of them.
  <br>Related: with no ceiling the keys are now OMITTED rather than set to something large. An absent key
  already reads as "not enumerated"; a number would have been a limit nobody measured, which is the same
  defect wearing a bigger value.
- **A rule that is right for the only implementation exercising it is a coincidence, and the second
  implementation is where that shows.** `CliProviderEngine` appended an `ICliToolProvisioner`'s args after
  the dialect's argv. Correct for `claude`, whose argv ends in options; wrong for `codex`, whose argv ends
  in the `-` stdin positional, where everything after it is read as PROMPT text and **a swallowed flag is a
  spent turn rather than an error**. `CodexExecArgs` had documented that hazard and taken an `extraOptions`
  parameter for it; the agent path used it and the completion path structurally could not
  (`docs/DECISIONS.md` **D65**). **When a shared engine composes something whose correctness depends on a
  backend's own grammar, the backend places it** — the engine may supply the pieces, not choose the order.

- **A member of a variation-point collection that the ROUTING RULE can never select is inert, and the
  registration is what makes it look wired.** Measured 2026-08-21, reported by two adopters on 3.0.0
  (`docs/task-archive.md` Parts 89 and 90; the answers are `docs/DECISIONS.md` **D85**).
  `CompositeMemoryEngine.RememberAsync` routed a write to the FIRST member supporting its grade, which is
  correct and documented — but in `UseGraph().UseSemantic()` the graph supports both grades, so it took every
  write and the semantic member's store stayed permanently empty. Nothing threw, `Supported` widened, the
  builder read as if both were live, and the only symptom was recall quality. The same shape one layer out:
  `AddMemoryVerification()` registers a policy only a graph member consults, and its absence renders as
  `Answered = null` — indistinguishable from a judge that ran and abstained.
  <br>**This repository's own README carried an instance of the reported defect**
  (`e.UseLexical().UseSemantic()`), which is the part worth carrying: the documentation is written by whoever
  understands the routing best, and it is exactly as blind to this as a consumer is, because *adding* the
  member is the whole visible act.
  <br>**The question that finds it is not "is this registered?" but "which member does the selection rule
  actually PICK, and what happens to the others?"** — asked of any DI collection whose consumer picks ONE
  (a first-match write route, a keyed lookup, a type-test) rather than iterating. And when you add the check:
  **a rule that also fires on correct wiring is worse than no rule.** The narrow version here reports a member
  only when EVERY grade it supports is already claimed, which keeps the documented
  `UseCurated("glossary").UseGraph()` blend silent; the naive "shares a grade with an earlier member" version
  fires on it, and a warning people learn to skip is the `check-warnings` ENOBUFS failure in a different hat.

## Refactoring & namespace moves

- **A compiler error list is not the authoritative site-list for a rename or move — it misses silently in
  three distinct ways.** Found closing the memory-domain restructure (moving the graph-retention types into
  `.Interference`/`.Forgetting`/`.Modulation`/`.Salience` sub-namespaces): **(1) it cannot see warnings.**
  `CS1574` (an unresolved doc `<see cref>`) is a *warning*, so a file that references a moved type only
  inside an XML comment produces zero errors and never shows up — three such files were missed this way,
  one of them in a different package from the one being edited. **(2) it truncates across projects.**
  MSBuild stops building a dependent project once its own dependency fails, so one build surfaced 4 of 78
  true sites and the rest arrived only after those 4 were fixed and the build ran a second time. **(3) it
  truncates within a single line.** A line with three unresolved names produced only one reported error, so
  **five whole test files** with genuine `CS0246` errors produced **no reported error at all**. The reliable
  method instead: **search for the moved type NAMES across the tree** — a plain text search over every
  file, code and prose alike, which does not care whether the reference is a compiled statement or a
  comment — rather than trusting whatever the compiler chose to surface. **A second-order trap sits inside
  that fix, too:** an exclusion filter added to such a search to cut noise (for instance, excluding a
  directory believed to be covered by some other step) is a bet that the excluded area really is covered
  elsewhere. On this branch that bet was three-quarters right — the worst outcome, because a mostly-correct
  exclusion looks exactly as safe as a fully-correct one and hides precisely the quarter that wasn't.
- **A member name reached by REFLECTION is a rename site the compiler cannot see** — and here the one gate
  that would catch it is deliberately outside `verify`. `bench/Lyntai.Benchmarks/MemoryPolicySweep.cs` reads
  engine internals with `GetField("_agePolicies", …)!`: rename that field and the code still compiles, `verify`
  still goes green over every one of its gates, and the sweep throws `NullReferenceException` on the `!` the next
  time somebody runs `node devtools/dev.mjs memory-sweep` — which is slow by design and therefore not in
  `verify`. `docs/DECISIONS.md` D47's own amendment justifies that reflection as a trip-wire making "a Core
  rename fail LOUDLY", and it does — **but only when the sweep is run, which nothing forces.** Before any
  rename, grep for the identifier as a STRING (`GetField("`, `GetProperty("`, `GetMethod("`, `nameof` is
  safe, serializer attribute names, SQL column names, reflection in tests) and treat each hit as a site. The
  general shape: **a green build proves nothing about names the build never resolved**, and "the compiler
  will find the call sites" is exactly the reasoning that misses them.
- **A rename that stops at the types leaves the retired word alive in PARAMETER NAMES and PROSE, and the
  prose is what puts it back.** D47 renamed four memory seams (`IMemoryClock` → `IMemoryAgePolicy`,
  `ISalienceAppraiser` → `IMemorySaliencePolicy`, `IRetentionModulator` → `IMemoryRetentionPolicy`) and
  explicitly deferred the parameter names as "purely cosmetic". Three of them survived into 3.0's frozen
  public surface — `ageClocks`, `appraisers`, `modulators` — beside roughly a hundred XML-doc sentences
  still calling those policies appraisers and modulators. **The parameters were named after the prose**,
  which is exactly what `repo-mechanics.md` §Naming warns about for `Dto`, in a domain nobody connected to
  that rule. Two things to carry: **named arguments are public API** (source-compatible surface, recorded in
  the baseline, and renaming one after a freeze costs a major), and **a deferral whose justification depends
  on a window staying open needs re-checking when that window closes** — by then it reads as a settled
  choice rather than a deferral, and nobody re-opens it.
  It also escaped every gate by construction: `check-docs` excluded `src/` entirely at the time, and the
  API-surface baseline **records parameter names without judging them**, so a stale name round-trips
  through the one gate that exists to notice API changes. A human review caught all three.
  <br>Half of that has since been closed — `check-docs` scans code COMMENTS in `src`, `tests` and `bench`
  as of 2026-08-17 — but only half, and the halves are different in kind: a comment is PROSE and a parameter
  name is SURFACE, which is why `check-api-vocabulary` is a separate gate with a separate registry rather
  than a wider glob on this one.

- **A doc comment asserting "this is NOT duplication waiting to be extracted" is unfalsifiable, and it is
  the one claim nobody re-reads.** Measured 2026-08-16 (`docs/DECISIONS.md` **D77**).
  `PostgresMemoryGraphStore`'s class doc said exactly that, and justified it with three real dialect
  differences — `GREATEST` versus `MAX`, an `ILIKE` over a GIN index versus an FTS5 virtual table, the table
  reference Postgres requires in `DO UPDATE SET`. Every clause was TRUE and every clause was about the SQL.
  The file also held four materialization row types and a projection that were **byte-identical** to the
  SQLite twin's, and the sentence covered them by adjacency alone.
  <br>The cost is the class of defect it protected: those types alias 25 columns explicitly *because a
  column↔property mismatch is a SILENT null rather than an error*, so two copies is two places for that
  silence to appear, and nothing can see either — the compiler binds nothing there, and a test only ever
  exercises whichever backend it runs on. The repository's own counter-example was one directory away
  (`JobStoreSql`, which hoists the job state machine for the same pair and says why in its header).
  <br>**What generalises: a "these must stay separate" claim needs the same treatment as a bound — ask what
  it FORBIDS, and check that the answer covers the whole file it is written on.** Here it forbade sharing
  the SQL, and the SQL was maybe half the file. A defence written against the part someone was looking at
  reads, forever after, as a defence of everything beside it. When you write one, name the part it does NOT
  cover — the same rule `check-links`' own header states for a gate whose scope is narrower than its defect.

- **A "tidy up the formatting" regex in a scripted sweep is a rewrite of every line it can match, and the
  build stays green while it happens.** Measured 2026-08-16 stripping work-log citations out of `src/`
  comments. The substitutions were narrow and correct; three cleanup rules appended to "fix the punctuation
  a removed parenthetical strands" were not. `'  +([a-z(<])' → ' \1'` collapsed the INDENTATION of **all
  349 tracked files in `src/`** — the script reported "349 files swept" and the number read like success —
  and `body.replace('()', '')` turned `AddScorer&lt;T&gt;()` into `AddScorer&lt;T&gt;` in a file the sweep
  had no business editing at all. **C# does not care about indentation, so `dotnet build` succeeded both
  times.** Recovered by `git checkout -- src/` and re-running the intentional passes, which only worked
  because every one of them was already a script.
  <br>Three things generalise, and the third is the one worth carrying past this incident.
  **(1) A sweep must only touch a line one of its REAL rules changed** — compare before/after per line and
  keep the original otherwise; an unconditional tidy applied after the substitutions cannot tell the lines
  it was written for from the rest of the file. **(2) Assert the invariant the sweep must not break**, in
  the script: line count unchanged, and per-line leading whitespace unchanged for every line. That check
  costs six lines and turns this class of accident into a crash. **(3) The file COUNT is the tell.** A
  targeted sweep that reports touching nearly every file in the tree has stopped being targeted, and that
  is visible in the output before any diff is read — the corrected run touched 15.
  <br>Related but distinct from the CRLF entry above, which the same session also hit: splicing `\n`-joined
  lines into CRLF files left three of them mixed, and `git diff` says only *"LF will be replaced by CRLF"*,
  which reads like routine autocrlf noise. Detect by codepoint and normalise deliberately; `core.autocrlf`
  is `true` here, so the working-tree convention is CRLF and a uniformly-LF file is not "fine because git
  normalises" — it is a file the next editor may re-save as mixed.

## Testing

- **A finding that is WRITTEN DOWN is not a finding that was VERIFIED, and this repository keeps treating
  the two as the same.** Twice in two days (2026-08-17): `TASKS.md` §Startable recorded a divergence as
  "ComfyUI hardcodes `Failed` while `FalQueueProvider` routes the same class through
  `GenerationVerdictClassifier`" — so fal was believed correct, and the item was filed as coverage work
  rather than a bug fix. Measured with the first contract test that reached it: **fal reports `Failed` too**,
  because it classifies the failure TEXT and a `"401: …"` status line is not vocabulary `FromErrorText`
  matches on. The other was a `CHANGELOG.md` entry asserting "neither shipped curve sets anything beyond
  `Stability`" that the same release had already falsified.
  <br>Both were written by someone who had read the code, which is precisely why they were persuasive. **The
  half that gets recorded is the half that was READ; the half that was RUN is what a claim needs.** When a
  record names two implementations and says one of them is fine, the fine one is the claim to check first —
  it is the only one nobody is planning to touch.
- **A contract fact can pass or fail for reasons that have nothing to do with its subject, and the failing
  direction is the dangerous one because it looks like evidence.** Both shapes hit within one hour writing
  the 3.0 seam contracts:
  - **Testing the FAKE.** A cancellation fact asserted that a pre-cancelled token propagates — against a
    stub that ignores its token entirely (`FakeLlmClient`, `StubHttpHandler`). Nothing cancelled, so the fact
    passed vacuously for some subjects and failed for others by measuring the stub. What it was FOR is the
    subject's catch ORDERING (`catch (OperationCanceledException) { throw; }` ahead of a fail-safe catch),
    which needs a double that honours the token.
  - **Failing before reaching the subject.** A fetch fact passed a bare operation id to every job backend;
    fal encodes the model into its ids and rejects a malformed one BEFORE calling out, so the fact failed on
    id validation while reporting the classification defect it was hunting. It would have been "confirmed"
    without ever exercising the code under test — and then "fixed", and still red.
  - **Timing what you could OBSERVE.** A concurrency fact asserted three stalled probes finish "under a
    second" against a 400ms deadline — green alone, failed inside a full-suite run. Same shape as the
    `ElapsedAgePolicy` entry above, written down here long before and re-read the same session, which is the
    honest lesson: **knowing a pitfall is not the same as not falling into it.**
    <br>**The fix generalizes: assert the PROPERTY, not the clock.** Concurrency is observable — have the
    fakes record their peak overlap, which serial execution cannot push above 1 at any speed. That is both
    deterministic and a stronger claim than any elapsed bound. And where a bound's failure mode is a HANG
    rather than a slow pass (a probe stalling on `Task.Delay(Timeout.Infinite)`), drop the clock assertion
    entirely: the runner reports a hang far more loudly, so the assertion was buying nothing and costing
    load-dependence.
  <br>**When a new fact fails, confirm it failed where you think.** Read the failure message against the
  code path, not against the expectation.

- **Adding a VARIANT to a measurement instrument moves the templates and the READERS, and forgetting the
  readers produces flattering numbers rather than a failure.** Measured 2026-08-12 adding a Chinese arm to
  `MemoryCorpus` (D55). The generator had ten template sites and they were easy; what nearly broke the
  measurement was everything that parses generated text back — the corpus's own invariants read the attribute
  value as "the word after `is`" and found a cue by `EndsWith(" recallcue")`. Those are English grammar in a
  helper's costume. On Chinese input they do not crash; they return something plausible and wrong, so the
  variant that most needs its guarantees checked is the one running unchecked. **Put the readers beside the
  templates** (`CorpusLexicon`) so the compiler names every hole when a language is added.
  Two bugs got through anyway, and BOTH made the new arm look better, which is the direction that does not
  announce itself:
  - **A corpus token below the store's term floor silently stops competing.** The Chinese shared leading
    token was `条目` — two characters, under `SearchTerms.MinimumTermLength`, so it yielded no trigram and was
    dropped from every query. That deleted the corpus's central property (every queried entry shares a token,
    so a broad recall makes them COMPETE) and the sweep reported `topical` miss AND pollution of exactly
    `0.0000` on almost every shape. An implausibly *good* number is a bug report; treat a clean sweep with the
    suspicion of a failing one.
  - **A classifier that matches wording silently empties a whole class out of the report.**
    `ClassifyQuery` used `EndsWith(" recallcue")`, so every Chinese attribute cue landed in `other` and the
    class the study existed to measure was simply absent. **Absence reads as "not exercised", never as
    "mislabelled"** — so a report missing a row is a louder signal than a row with a bad number.
  - **Every invariant pinned the cue's UPPER bound and none pinned the LOWER one, so a cue matching NOTHING
    passed them all.** The Chinese discriminative cue reached zero of three cluster members where English
    reaches exactly one; the arm reported miss ≈ 0.889 with pollution 0.000 against English's 0.299. That is
    a cue returning almost nothing, and it reads as a language finding. Caught only by disbelieving a number,
    not by any gate. **When a fixture is constrained to avoid matching the wrong thing, assert separately
    that it still matches the RIGHT thing** — "shares no term with material outside the cluster" and "reaches
    exactly one member inside it" are two facts, and the second is the one that keeps the first honest.
  The general rule, and the reason this sits under Testing rather than in the memory section: **an instrument
  is code that nothing else validates.** Its output is a number, and a number is never obviously wrong. Guard
  a variant with (a) goldens on the ORIGINAL captured before the axis exists, (b) an assertion that the
  variant genuinely DIFFERS, and (c) an assertion that the property the instrument depends on still holds in
  the variant — all three, because each catches a different way of quietly measuring nothing.
- **Microsoft.Data.Sqlite completes its "async" methods SYNCHRONOUSLY, so two awaited store calls issued
  from one thread are sequential by construction — and a concurrency test over them measures nothing.**
  Measured 2026-08-17: an overlap test called `Task.WhenAll(store.ReportStepAsync(a…), store.ReportStepAsync(b…))`
  and failed even AFTER the per-job locking it was written for landed, because the first call ran to
  completion on the test thread (blocking inside the instrumented clock) before the second expression was
  even evaluated — the store's gate never entered into it. Wrap each call in `Task.Run` when the subject is
  concurrency and the backend is SQLite (or anything whose async is sync), and treat "still red after the
  fix" as a cue to ask WHERE the serialization actually is before touching the fix.
- **An orphaned child process inherits the test host's console handles, so a test that STRANDS one wedges
  the whole runner — past its own failure, past every later test.** Measured 2026-08-17 by the RED run of
  the hanging-locator test: the test itself failed correctly at its 30s bound, and `dotnet test` then hung
  indefinitely anyway, because the stranded node child held the inherited stdout the harness reads to EOF;
  it unblocked the moment the child was killed by hand. A bounded in-test wait is NOT enough when the
  failure mode leaves a child alive — give the fixture child a self-exit (`setTimeout(() => process.exit(0), …)`)
  so a regression costs one slow red run rather than a wedged pipeline. Same family as `windows-machine.md`
  §Processes (kill your own tree), from the harness's side.
- Provider/e2e tests must not hit a live endpoint or spend real tokens — HTTP providers get a stubbed
  `HttpMessageHandler`; CLI providers and the Playground get `provider-stub.mjs` via
  `LYNTAI_PROVIDER_CMD`. Extend the stub's prompt-marker behavior when a test needs a new deterministic
  output.
- 221 passing tests didn't catch any of the LLM/contract bugs above — **tests passing ≠ correct.** For
  load-bearing semantics (fallback, streaming, verdicts, FTS sync) reason about the code, then add the
  test that would have caught the bug.
- **This repo has TWO independent ways for a targeted test run to verify nothing.** (1) `dotnet test
  --filter` **reports success when it matches zero tests** — a filter naming `LlmRouterTests`, a class that
  does not exist (the real ones are `LlmRouterCompleteTests` / `LlmRouterStreamTests`), passed vacuously and
  looked like a clean regression run. (2) Through the wrapper it depends on `--`, which is not obvious.
  `node devtools/dev.mjs test --filter X` forwards straight into `dotnet test` (`devtools/dev.mjs` spreads
  `...args` into the argv, and always has) and inherits trap (1) whole — a name matching nothing passes
  vacuously, exit 0. `node devtools/dev.mjs test -- --filter X` runs the **WHOLE suite** instead: VSTest reads
  post-`--` tokens as RunSettings arguments and the filter is dropped (measured 2026-08-05: 1573 passed /
  1582 total). Slow but honest — and easy to mistake for the filtered run you asked for, in the other
  direction from (1). **Always read the matched/total count**, and prefer a broad filter over an
  exact class name (`~Router|~Routing|~DeadHost`) so a renamed class degrades to running too much rather than
  to running nothing.
- **A test that is already RED for an unrelated reason cannot be mutation-killed, and a mutation check
  against it proves nothing.** Found 2026-08-09 running the two required mutation checks for the salience
  rank boost's FIRST round (`docs/DECISIONS.md` D45, before the owner's same-day correction that ranking
  defaults off): deleting the boost factor entirely and hard-coding the weight both left the "salient entry outranks a
  better match" fact failing (it was already failing at the round-1 default weight — see the
  `Relevance`-normalization entry above) and left the "weight leaves ranking unchanged" fact passing EITHER
  way, because the relevance gap it measures against dominated regardless of the weight. Neither mutation was
  actually caught. **Ask "which specific wrong implementation does this test reject?" of the MUTATED run, not
  just the correct one** — a fact that is red (or green) independent of the mutation is not exercising what
  its name claims, however correct the underlying implementation is. Round 2, with ranking defaulted OFF and
  the opt-in fact (`An_explicitly_configured_rank_weight_can_outrank_a_better_textual_match`, weight 1.0)
  chosen to clear the measured gap, both mutations DO now kill the opt-in fact — the fix was not to accept a
  test with no discriminating power, but to pick a scenario the mechanism could actually win.
- **A test that HANGS on the failure it detects is worse than no test.** A permit-leak test that blocks a
  second caller on a gate proves the leak by never completing — which turns `verify` into an opaque hang
  instead of a red test, and the next person bisects the harness rather than reading the failure. Bound every
  await that can block on a permit (a timeout on the wait, asserting the timeout is what fails), so the
  regression the test exists for arrives as an assertion.
- **A counter can return the RIGHT number from two errors that cancel, and only comparing the ITEMS finds
  it.** Measured 2026-08-15 writing `check-counts`' `verify`-gate counter. It parsed `dev.mjs`'s `steps`
  array with `\['[a-z-]+'`, which cannot match `e2e` (the class excludes digits) and DOES match the inner
  argument array in `['check-sensitive', ['--tree']]`. Twelve real steps plus one phantom is thirteen — the
  documented number, on a clean tree, agreeing with the prose it was gating. A test asserting only the COUNT
  passes forever. The test that caught it compared the parsed NAMES and asserted two specific properties:
  `e2e` is present, `--tree` is not. **When a counter's output is a number, pin the SET it derived the number
  from** — the number alone cannot distinguish a correct parse from a wrong one that happens to tie, and the
  tie is likelier than it sounds because both errors are off-by-one in opposite directions.
- **A fail-closed guard belongs on the SOURCE list, not on the post-filter count — the filtered count has a
  legitimate zero.** Measured 2026-08-15 giving `check-docs` / `check-encoding` / `check-links` /
  `check-samples` the fail-closed rule `check-api-vocabulary` already carried (a gate that scanned nothing
  must never print a tick). Written the obvious way — `if (candidates.length === 0) return 1` — it
  immediately failed check-encoding's own `an unscanned extension is ignored` fact, and that test was RIGHT:
  a call whose only input is a binary has legitimately nothing to scan. The distinction that makes the guard
  correct is **who chose the scope**. An empty SOURCE list is a broken listing whoever supplied it (the exact
  shape that let `check-sensitive` skip every renamed file), so it indicts any caller; zero SURVIVORS of the
  filters is an indictment only on the full-tree path, where this repository cannot legitimately produce one
  — README/CLAUDE.md/TASKS.md guarantee three docs, 761 files pass the text filter, 74 samples exist. Hence
  `source.length === 0 || (files === null && filtered.length === 0)`. **Write the both-directions test at the
  same time**: a fail-closed guard that over-fires is a broken build on legitimate input, which is the
  failure people fix by deleting the guard.
