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
- **NuGet never re-extracts a package version it already has in the global cache**, so packing under a FIXED
  throwaway version (`consumer-smoke`'s `9.9.9-smoke`) tests the packages only ONCE — every later run restores
  the first run's copies from `~/.nuget/packages/` and reports success about code it never compiled against.
  Found 2026-08-04: a newly-added public method was "missing" from a package that demonstrably contained it,
  and evicting the cached ids reported **9 stale packages**. `consumer-smoke` now deletes
  `<global-packages>/lyntai.*/<version>` after packing; keep that step if you touch the script, and remember the
  general shape — *a distinct version is not enough on its own if the version is a constant.* Evicting by
  id+version beats isolating `NUGET_PACKAGES`, which would re-download every third-party dependency per run.

## LLM / router (details in `llm-and-router.md`)

- **A timeout as a single `CancelAfter` over a whole call** — a wall clock that kills a slow-but-alive
  child (a long tool loop, a big prompt) exactly like a dead one. Both the STREAMING path and the BUFFERED
  `ProcessRunner.RunAsync` must use a per-chunk inactivity clock (re-armed on each read); the buffered path
  adds an absolute `maxDuration` backstop and reports `ProcessResult.TimeoutKind`. (Streaming shipped the
  bug in two providers; the buffered path shipped it too — both fixed.)
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
- **A subcommand you rely on may not exist on an OLDER install — pin a FLAG so it fails loudly.**
  `auth status --json` sends `--json` explicitly even though it is the CLI's default (measured on v2.1.220),
  precisely because a build predating `auth` rejects the unknown *flag* (exit non-zero, no turn) instead of
  billing a turn to answer the sentence "auth status". Same reasoning as above, one step further out.
- **Trusting the exit code over the machine-readable answer.** A signed-out `auth status` may report its
  state AND exit non-zero — that's an ANSWER, not a broken backend. Parse first, then fall back to the exit
  code (`ClaudeCliProvider.StatusAsync`). And parse the WHOLE body: the `Tail()` helper keeps the LAST 500
  chars, which would decapitate a JSON document.
- **Re-implementing the CLI rules for a new CLI backend.** Everything above lives in
  `CliProviderEngine` (Core, `Lyntai.Llm.Cli`); a new CLI is an `ICliProviderDialect`, never a fresh
  `ILlmProvider` (`docs/DECISIONS.md` D27). The reason these traps were fixable at all is that there is now
  ONE copy.
- **Assuming a non-zero exit means failure — a CLI can report failure IN BAND and exit 0.** Measured on
  codex-cli 0.146.0: a 401 turn prints `{"type":"turn.failed","error":{"message":"… 401 Unauthorized …"}}`
  and the process still exits cleanly. Map it to `CliOutputEvent.Failure` so the message is classified
  (`AuthFailed` cools the host; a bare `Failed` just advances). Ignoring it yields "no output produced" —
  right verdict class, no reason, wrong routing.
- **…and the mirror image: treating every error-ish line as terminal.** Also measured on codex: a bare
  `{"type":"error","message":"Reconnecting... 2/5"}` and an `item.completed` whose item type is `error`
  ("Model metadata not found") both appeared in a run that went on to **succeed**. Only the terminal event
  (`turn.failed`) may fail a call, or healthy calls die on a retry they recovered from.
- **A neutral working directory can break a CLI that expects a repo.** The engine spawns from a temp dir on
  purpose (§6 hygiene). codex refuses to run outside a git repository, so its dialect MUST pass
  `--skip-git-repo-check` — every completion would fail on a perfectly good install otherwise. Check what
  your CLI assumes about its cwd.
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

## Provider lifetime, cooldown & admission (`Lyntai.Lifecycle`; details in `docs/DECISIONS.md` D37)

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
  `LocalDiffusionOptions` is a plain class and compares by **reference**, and `ComfyUiOptions.Kinds` is an
  `IReadOnlyList<string>` that record equality also compares by reference. So automatic derivation reuses
  correctly for three backends and rebuilds-or-reuses arbitrarily for two. Name every contribution
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

- **Missing FTS `'delete'` trigger row on delete/update** — silent index corruption. Three triggers,
  always.
- **A double column read without `CAST(x AS REAL)`** — the SQLite integer-affinity trap.
- **Opening a connection outside the factory** — loses per-connection `foreign_keys=ON`, so cascades
  silently stop.
- **Reusing a migration number** — silently skipped, so the migration never runs. Use
  `dev.mjs new-migration`.
- **`ORDER BY` on a non-unique column with no tiebreaker** — nondeterministic on ties.

## DI / config

- **Calling `AddLyntai` twice** — registers a second `LyntaiOptions` (shadows the first) while both
  calls' providers pile into the collections. It now throws; compose everything in one callback.
- **A singleton capturing a scoped/transient dependency** (captive dependency). Providers/stores are
  singletons; resolve per-call transients (like an `HttpClient` from `IHttpClientFactory`) inside the
  method, not the constructor.
- **A documented option/env-var that isn't wired** — the `LYNTAI_MODEL_<CONSUMER>` override and the OTel
  cost attribute were both documented but silently dropped, and no test caught it. When you add a
  documented knob, add the test that exercises the documented path.
- **A `TryAddSingleton` reached during `configure(builder)` BEATS `AddLyntai`'s own options-built
  registration.** `AddLyntai` invokes `configure(builder)` (`ServiceCollectionExtensions.cs:39`, in
  `AddLyntai`) well before it calls `RegisterLlmFrontDoor` (`:60`, in `AddLyntai`), and that method
  (`:98`, `RegisterLlmFrontDoor`'s signature) is where the `DeadHostTracker` built from `LyntaiOptions` is
  registered (`:101`, in `RegisterLlmFrontDoor`). **Names first, lines second** — these numbers rot, and this very
  entry was made stale by a change inside the branch that added it; follow the method names if a line
  disagrees. So a `TryAddSingleton<DeadHostTracker>()` added inside a `Use*`/`Add*` extension reaches the
  collection FIRST, and `TryAdd` keeps the first — silently swapping the configured `DeadHostThreshold`,
  `DeadHostCooldown` and logger for the parameterless defaults, **for both domains**. Nothing in 1427 tests
  noticed; it was found by mutation (adding the line failed exactly one new guard test and nothing else) and
  confirmed twice. The rule:
  inside a builder callback, resolve what `AddLyntai` registers later with `GetRequiredService<T>()` — never
  seed it with a `TryAdd`, and if you need a service that must exist regardless, register it in the
  Register* block that owns it. `RegisterProviderLifetime` is all `TryAdd` deliberately for the mirror-image
  reason: everything it seeds is meant to lose to a host or a `Use*` call.

## Testing

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
  looked like a clean regression run. (2) `node devtools/dev.mjs test -- --filter "X"` **does not forward the
  filter at all** — it runs the whole suite, which is slow but honest, and is easy to mistake for the
  filtered run you asked for. **Always read the matched/total count**, and prefer a broad filter over an
  exact class name (`~Router|~Routing|~DeadHost`) so a renamed class degrades to running too much rather than
  to running nothing.
- **A test that HANGS on the failure it detects is worse than no test.** A permit-leak test that blocks a
  second caller on a gate proves the leak by never completing — which turns `verify` into an opaque hang
  instead of a red test, and the next person bisects the harness rather than reading the failure. Bound every
  await that can block on a permit (a timeout on the wait, asserting the timeout is what fails), so the
  regression the test exists for arrives as an assertion.
