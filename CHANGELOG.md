# Changelog

All packages version in lockstep from `src/Directory.Build.props` (`VersionPrefix`).
From 1.0, **SemVer 2.0** applies: breaking public-API changes require a major bump (gated by
`ApiSurfaceTests`; see `docs/DECISIONS.md` D22). Pre-1.0 (≤ 0.31.x) minor bumps could carry breaking
changes — each is called out below.

**Amended 2026-07-29 (`docs/DECISIONS.md` D24):** while every consumer is one of the owner's own
applications, a **documented** break may ship in a MINOR release. Every break is still gated by
`ApiSurfaceTests` and still called out under a **Breaking** heading here — only the version-number
consequence is relaxed. Strict SemVer resumes as soon as any third party depends on Lyntai.

## 2.5.0 — 2026-08-08

### Added

- **Named memory engines** (`IMemoryEngine`, `IMemoryEngineFactory`, `AddMemory()` /
  `AddMemoryEngine(name, …)` / `UseMemoryComposer(name)`) — several memory systems can now coexist in one
  application and are resolved **by name**, the way `IHttpClientFactory` resolves clients. Until now every
  memory surface was a single unnamed singleton, so an application wanting a *chat* memory and a *project*
  memory had to wrap all of it itself — the same wrapper in every consumer, and none of them able to share
  it. Registration is a DI collection keyed by `Name`, the same variation-point shape as `ILlmProvider`
  keyed by `Id` and picked by `ILlmRouter`, so a fourth kind of memory is a class plus a registration.
  Thin engines adapt the three existing stores (`IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore`),
  and a **blend is itself an engine** (`CompositeMemoryEngine`), so naming, blending, remembering and
  expanding stay one concept. Optional abilities are separate interfaces (`IExpandableMemory`,
  `ILinkableMemory`, `IForgettableMemory`) an engine may also implement, and the composite forwards them by
  routing on `MemoryRef.Engine` rather than guessing — the regression that once made every queue-backed
  render unroutable in the generation router is pinned here by a test.
- **Prompt composition now protects exact facts from being crowded out** (`MemoryComposition.ComposeAsync`,
  `MemoryCompositionOptions`, `MemoryGrade`). Material carries a grade: **authoritative** content never
  decays, is never truncated to a derived headline, is allocated from a **reserved** character budget
  *before* any associative content is admitted, and renders in its own labelled section so the model is
  told which material is exact rather than left to infer it. Today's flat 4000-character budget fills in
  rank order, so a burst of loosely-relevant recall can push a hard constraint out of the prompt entirely
  while the prompt still looks full. Authoritative material that genuinely cannot fit emits an explicit
  `… N further authoritative facts omitted (budget)` line rather than disappearing, and an authoritative
  write is **routed** to an engine that can hold it or throws — never silently downgraded.
- **A one-line path that needs nothing implemented**: `AddMemory()` registers a working engine and backs
  `ChatOrchestrator`'s `IPromptComposer` with it. The fluent builder exists for applications that want to
  *differ*. A duplicate engine name or member label fails at **configure** time, and a member whose backing
  store is absent fails at **startup** naming the store — rather than resolving to a permanently empty
  memory section that reads exactly like "nothing matched".

- **Graph memory: entries that decay, connect, and open as an index** (`GraphMemoryEngine`,
  `IMemoryGraphStore`, `UseGraph()`). Recall returns **headlines** and withholds the full text until
  expansion, so a session opens on a cheap index and pays for depth only along the direction it turns out
  to need. Entries **decay** unless reused — retrievability is computed at read time from stored
  counters, so there is no sweeper and no background job — and each successful recall lengthens an entry's half-life,
  making repeated context durable while one-off noise sinks beneath fresher material. Decay only ever
  **ranks**;
  deletion stays explicit via `PruneAsync`. Entries **connect** model-free: whatever is returned together
  gets linked, the link strengthens on recurrence, and a later recall spreads through those links to reach
  material the query never matched.
- **The decay curve is a seam** (`IRetrievabilityPolicy`, `HalfLifeRetrievability`, `HalfLifeOptions`).
  Exposing the constants alone would settle the values while freezing the formula, so an application can
  tune the numbers *or* replace the model of forgetting, and neither choice forecloses the other. The
  default is registered, so nothing has to be implemented. Storage never evaluates the curve — a policy
  supplies a conservative `CandidateCutoff` and the store bounds its candidate set with plain division,
  which keeps a custom curve possible and avoids depending on SQLite's optional `pow`.
  `HalfLifeOptions.MaxStability` caps reinforcement: unbounded compounding multiplies a half-life by more
  than three thousand in about twenty recalls, which would silently give a frequently-recalled *associative*
  entry the durability of an authoritative one without any of its guarantees.
- **Decay is measured in what has HAPPENED in a memory, not in elapsed time** (`IMemoryClock`,
  `PerWriteClock`, `ContentSizeClock`, `ElapsedClock`, `BurstDampenedClock`). Each engine keeps a position
  that advances when something is written to it; an entry's age is how far that position has moved since it
  was last used. **A rarely-used memory therefore decays slowly and a busy one decays fast** — automatic,
  and the reverse of what wall-clock time gives you. Reading never ages anything. What the position
  *counts* is a seam with four shipped implementations, because "how much has happened" is genuinely
  ambiguous — writes, volume, or real time — and a project memory can decay on the calendar while a chat
  memory decays by volume in the same application.
  **Bursts saturate**, which is a correctness matter rather than a refinement: advancing once per write is
  linear, so a 500-item bulk ingest would age every pre-existing entry by 500 and erase everything known
  before it. A damped burst advances by about `ln n`, and its own entries start with a proportionally
  shorter half-life — so a long document neither wipes your memory nor is itself remembered in full.
  Consequences: no date arithmetic appears in any query (removing the hazard where SQLite's `julianday`
  returning NULL on an unparseable timestamp would silently exclude every row), and the decay constants no
  longer carry `TimeSpan`, which asserted a dimension the application had not chosen.
- **Decay buries an entry rather than cutting it** (`GraphMemoryOptions.RelativeFloor`). Recall ranks
  everything and hides only what falls far below the *strongest* hit, so a faint memory alone in a quiet
  engine still surfaces while the same memory under fresher material does not — and either way it stays
  reachable by reference or through a neighbour, reporting how faint it has become. Seeding applies no
  faintness bound at all; `MinRetrievability` now governs `PruneAsync` alone, where removing a memory is
  the explicit intent.
- **Connectedness feeds decay, and edges decay too.** A memory woven into a dense, repeatedly-reinforced
  neighbourhood now resists forgetting, while an isolated one fades — connections make an entry more
  *durable*, not merely more reachable. Symmetrically, edge weight decays with disuse
  (`HalfLifeOptions.EdgeHalfLife`), so a link that stops recurring stops pulling its neighbour into recall
  and stops propping it up; without that, every pair that had ever co-occurred would stay linked at a rising
  weight until spreading reached everything from everything. Both are read-time, so there is still no
  sweeper.
- **Graph memory persists** — `IMemoryGraphStore` now has SQLite and Postgres backends alongside InMemory,
  all held to one contract. SQLite indexes headline and content through an FTS5 **trigram** mirror (so CJK
  substring recall works, which a word-boundary tokenizer would silently return nothing for); Postgres uses
  a `pg_trgm` GIN index. Neither evaluates the decay curve: candidates are bounded by plain division
  against the cutoff the policy supplies, which is what keeps a caller-supplied curve possible and avoids
  depending on SQLite's optional `pow`.
- **Graph memory is available to the model**, not just to the prompt composer —
  `AddMemoryTools(engine, taskKey)` registers `{engine}_recall` and `{engine}_expand` as ordinary tools, so
  they reach the tool loop and the MCP bridge alike. Recall returns headlines and a `ref`; expand takes
  that ref and returns the full text plus what the item is linked to. Names are prefixed per engine rather
  than one multiplexed tool taking an engine argument, because the multiplexed form lets a model consult
  the *wrong* memory. `MemoryToolScope.Use(taskKey)` overrides the registered default for the current async
  flow, which a chat application needs since its task is per-conversation.
- **Similarity enrichment** — when an `IEmbedder` and an `IVectorStore` are registered, a new graph entry is
  also linked to its nearest existing neighbours (`GraphMemoryOptions.SimilarityK`, `MinSimilarity`). Pure
  enrichment on top of the model-free floor: without it the graph still forms from co-activation and
  explicit links, and a failing embedder costs an entry some links, never the entry itself. Connectedness may only ever *raise* retrievability, and `MaxConnectionBoost` bounds how far —
  which is load-bearing rather than cosmetic, since `CandidateCutoff` widens by exactly that factor and an
  unbounded boost would leave well-connected entries outside any finite cutoff, silently losing the very
  memories connectedness was meant to protect.
  **Several constants are unmeasured and say so in their XML docs** — the MEM-TUNE task closes them, and
  the three governing connectedness have to be measured *together*, since edge decay erodes the strength
  that feeds the boost.

Purely additive: `IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore` and `MemoryPromptComposer` are
unchanged, and an application that never calls `AddMemory`/`AddMemoryEngine` observes no difference.
No new package. Designs: `local/superpowers/specs/2026-08-08-memory-engine-seam-design.md` (MEM1) and
`2026-08-08-graph-memory-engine-design.md` (MEM2a).

## 2.4.0 — 2026-08-05

### Added

- **An agent session can be given the host application's own MCP servers, on either CLI backend**
  (`AgentSessionOptions.McpServers`, `AgentMcpServer`, `McpTransport` — all neutral `Lyntai.Core`). Until now
  the only way to point an `IAgentSession` at app tools was `ClaudeAgentOptions.McpConfigPath`, so the two
  backends were interchangeable only for an agent that needed no app tools — and adopting `CodexAgentSession`
  meant spawning codex with **no MCP servers at all**, a silent capability loss rather than an error. The new
  type expresses **stdio** (command/args/env — how an app ships its own tools as a child process) as well as
  **HTTP**, and each adapter renders it in its own MEASURED vocabulary: claude an owner-only `--mcp-config`
  document deleted when the turn ends, codex repeated `-c mcp_servers.<name>.…` TOML overrides. A caller's
  existing `McpConfigPath` is kept **alongside** the rendered one (the flag takes a list), an `AuthToken`
  never reaches argv, naming a server does **not** pre-approve its tools, and an entry that cannot be
  rendered refuses the turn instead of being dropped. Additive — `McpServers` defaults to empty and no
  existing behaviour changes. Closes CLI14, filed by a consuming app; reasoning in `docs/DECISIONS.md`
  **D47**.

### Fixed — documentation

- **The README no longer says the codex agent session refuses `ResumeToken`.** It has been honoured since
  CLI13 (measured 2026-08-05 via the turn-free `codex exec resume --help`); the bullet describing the old
  refusal was left behind and now describes what actually happens, including the one token shape still
  refused and the unmeasured `SessionId`-on-resume caveat.

### Changed — behaviour

- **A CLI backend's own account of a failed turn now wins over the process exit code.**
  `CliProviderEngine.CompleteAsync` returned on a non-zero exit **before** parsing stdout, so a turn that
  reported its failure in band *and* also exited non-zero was classified from whatever happened to be on
  stderr. Measured on codex-cli 0.146.0 (2026-08-05, an account whose login had expired): the turn printed
  `{"type":"turn.failed","error":{"message":"… 401 …"}}`, exited non-zero, and carried nothing but ordinary
  startup chatter (`Reading prompt from stdin...`) on stderr — so the reply was `Failed` with the detail
  `exit 1: Reading prompt from stdin...`, which is neither the reason nor the right remedy.
  **Observable delta:** that call now reports the classified in-band verdict — here `AuthFailed`, which cools
  the host for the cooldown window rather than merely advancing — and the backend's own message as the
  detail, with the exit code kept as context (`exit 1: <message>`). A non-zero exit with **no** in-band
  failure is unchanged, as are the streaming path and both agent sessions (each of which already preferred
  the in-band terminal). This is the ordering `StatusAsync` has always used — parse the answer, then fall
  back to the exit code — which the completion path never had. Filed as CLI15 by a consuming app that hit
  the same shape in its own reader.

## 2.3.0 — 2026-08-05

_Everything below was finished before 2.2.0 was cut but landed **after** it: the release workflow runs against
the pushed branch, and this work had not been pushed, so 2.2.0 shipped without any of it (see
`docs/DECISIONS.md` **D46**). Nothing here is in the published 2.2.0 packages — it ships in the next release._

### Fixed — the pre-release whole-library review (2026-08-05)

_A review of all twelve packages and the test suite, verified adversarially. Everything in **this** section is
detectable at compile time or observable only in a log or a metric; every change that a consumer cannot detect
at compile time is in **Changed — behaviour fixes…** below, disclosed one by one as `docs/DECISIONS.md` D44
requires. (They were split because the behaviour half was originally deferred to a major; D44 removed that
constraint and both halves ship together.) The `Lyntai.Generation` package is EXPERIMENTAL and exempt, so its
behaviour fixes are called out here as well as there._

- **A throttled STREAMING call now reports itself.** `RateLimitedLlmClient.StreamAsync` hand-rolled its
  refusal chunk and called neither the logger nor the counter, so a streaming-only workload being throttled
  reported **zero** on `lyntai.ratelimit.refusals` — the metric the feature ships to be observed through. The
  chunk the caller receives is unchanged; only the log line and the counter that were always meant to fire
  now do.
- **`AddRateLimit` no longer reports "no effective limit" at a consumer it is actively blocking.** A
  per-consumer entry with a zero rate is a deliberate burst-then-block, but the startup predicate required a
  *positive* rate, so a configuration consisting only of those logged "it will not throttle" while throttling.
- **A budgeted call no longer reads the usage total when nothing caps it.** `BudgetedLlmClient` issued a
  pre-call global read on **every** request even with no global cap configured — for the SQLite and Postgres
  trackers, a whole-table `SUM` per request. Now read only when a cap needs it, matching the generation twin.
  Refusal outcomes are identical in every configuration.
- **Dead-host state is dropped on recovery** rather than zeroed, so a long-lived process no longer retains an
  entry per configuration that ever recovered. Every reader already treated missing and zeroed identically.
  This matters more since cooldown began keying on a `ProviderKey`, which changes with every credential
  rotation.
- **A CLI dialect now sees the same line on both paths.** The buffered path split on `\n` and handed dialects
  a trailing `\r` from a CRLF-emitting child, while the streamed path stripped it. No shipped dialect was
  affected (both tolerate it); a third-party dialect doing an exact match was broken on one path only.
- **`Lyntai.Generation` (EXPERIMENTAL), two behaviour fixes.** `FalQueueProvider` **refuses** an input that
  carries only bytes instead of dropping it — the drop submitted, and billed, a text-to-video render for a
  caller who asked for image→video, and the result looked plausible. And `ComfyUiProvider` now distinguishes a
  transport failure from a terminal one when polling: an unreachable server or a 5xx reports `Running` (the
  render is still going), while a 4xx or an unconfigured base URL stays `Failed`, so a poll no longer abandons
  a live render — nor polls a dead id forever.

### Breaking — two documented compile-time breaks (2026-08-05)

Both ship in a minor under `docs/DECISIONS.md` **D24**, which allows a documented, compile-time-**detectable**
break while every consumer is first-party. Neither can bind silently: each is a compile error that names the
fix.

- **`OpenAiCompatibleOptions.ContextSize` → `OllamaContextSize`.** The option only ever affected the
  Ollama-native payload (`options.num_ctx` on `/api/chat`) and was silently ignored by every other flavour —
  including Ollama's *own* OpenAI-compatible `/v1` surface — so the generic name invited exactly the
  configuration that does nothing. The type (`int?`) and behaviour are unchanged; only the name says which
  backend it is for. Verified against the code before renaming.
- **`ICuratedMemoryStore.UpdateAsync` takes `taskKey` and `scope`**, inserted before `metadata` so the
  identity fields read in the same order as `AddAsync`. Named-argument callers are unaffected; a positional
  call that reached `metadata` or `ct` must move to named arguments — always a compile error, never a silent
  re-bind, since the types are not interconvertible. **Every BYO `ICuratedMemoryStore` implementation must add
  the two parameters.**

### Changed — behaviour fixes that a consumer cannot detect at compile time (2026-08-05)

**Read this section before upgrading.** Everything here changes what the library *does*, with no compile-time
signal. It ships in a minor under `docs/DECISIONS.md` **D44**, which amends D24's third bullet while every
consumer is first-party — and D44's entire price is that each change names its observable delta, which is what
this section is. (Storage and migration breaks remain major-bump material, unconditionally. None here.)

- **A repeated generation candidate is attempted once, and no longer costs the sole-candidate exemption.**
  Before, a list naming one backend twice called it twice per request *and* made `capable.Count == 2`, so
  `ExemptSoleCandidate` did not fire: the only capable backend was benched after one rate limit, and the next
  call returned a synthesized "every capable media backend is on dead-host cooldown" instead of the backend's
  own actionable verdict. Now repeats collapse (first wins, order preserved). Deduping is on the *resolved*
  (backend, effective model) pair, so `["fal", "FAL"]` is one candidate — but two different models at one
  backend are still two candidates.
- **A rejected media submission is now answered by the routing policy instead of always being penalized.**
  Before, every rejected `SubmitAsync` advanced *and* took a dead-host strike regardless of why. Now the
  backend's own rejection text is classified and the policy decides: a rate-limit/auth rejection benches
  immediately rather than taking 1-of-N strikes; a blameless one (`NotConfigured`/`Unsupported`) advances with
  **no** dead-host bookkeeping, so an unconfigured queue stays in rotation and a key set later is picked up;
  an unclassifiable one behaves exactly as before. **The one delta that can stop a fallback you were getting:**
  a rejection classifying as `Refused` (a content-policy body) now *surfaces* instead of being re-submitted to
  the next queue — matching the inline path. Restore the old behaviour with
  `policy.On(GenerationVerdict.Refused, GenerationFallbackAction.Advance)`.
- **A failed submission now names the backend's reason.** The message keeps its `no capable media backend
  accepted a 'video' job among [...]` prefix (substring checks still hold) and gains
  `— 'fal' said: <the backend's own words>`. `GenerationSubmission.ProviderId` is deliberately still empty on
  every non-accepting path, because `IGenerationRouter` documents empty as "no candidate accepted".
- **`LlmRouter` matches candidate ids case-insensitively**, like every other id lookup in the library. Before,
  `new LlmCandidate("OpenAI")` against a provider reporting `openai` matched nothing and produced
  `"no live candidate"` even as the only registered backend — reachable, because the pool guard deliberately
  accepts a slot whose case differs from the instance's `Id`, so such a provider was built, pooled, and never
  selected. Candidates differing only in id case now dedup to one. Affected: anyone relying on case to keep
  two same-named providers apart.
- **A streamed CLI completion is now bounded by an absolute clock, not only by child inactivity.** Before, a
  child that kept printing but never terminated streamed forever, since every line re-armed the inactivity
  window. Now `LyntaiOptions.MaxProviderTimeout` (default 30 min) is also a ceiling, raised to match a larger
  app-configured `TimeoutByConsumer` budget rather than clamping it. On expiry the process tree is killed and
  the stream ends in a terminal `Timeout` chunk naming the window that fired. **Not** applied to
  `ClaudeAgentSession`/`CodexAgentSession` — those are long-running agent turns and a wall clock there would
  kill healthy hour-long sessions.
- **An empty content event from a CLI dialect is no longer "delivered content".** It is ignored rather than
  yielded, so a stream with no other output now ends `Error(Failed, "no output produced")` and the router can
  fall over — where before it ended `Final`, a fully successful *empty* answer, and a zero-content first chunk
  committed the router's stream and disabled fallback for the whole turn. It also no longer suppresses the
  answer reported on a terminal `result` line. Both shipped dialects already guarded, so `claude` and `codex`
  users see no change; this moves the invariant into the engine so it no longer depends on third-party code.
- **The Ollama-native flavour sends image attachments.** Before, `/api/chat` was posted with text only and the
  attachment vanished — nothing on the wire, nothing logged — while the README promised images were sent. Now
  inline bytes travel as Ollama's own `images` array on the user turn, and a URL-only attachment (which
  `/api/chat` has no form for) is logged as undeliverable instead of vanishing. **Two directions of surprise:**
  a consumer who adapted to the drop now really sends images, so a text-only model may answer worse or error;
  and request bodies get larger.
- **A usage token count that is not an integral number no longer throws.** A fractional or out-of-range count
  from a gateway raised `FormatException` — escaping `CompleteAsync`, escaping a stream *mid-enumeration* after
  content had reached the caller, and breaking the documented "never throws" contract of both claude readers.
  It now reads as 0 and the call completes. A budget or telemetry line under-counts by that field instead:
  deliberate, since a usage count is telemetry and losing one beats failing an answer already paid for.
- **`ClaudeAgentSession` ends a turn once.** A transcript with two terminal `result` lines ended the session
  twice, and `RunAsync`'s last-one-wins fold turned a completed `Ok` turn followed by a stray error into
  `Failed` with empty text. The first terminal now wins, matching the codex twin.
- **`IScorer.Applies` reaches an LLM judge's own predicate.** `((IScorer)judge).Applies(ctx)` returned true for
  every `LlmScorerBase` subclass regardless of its override, because the default interface implementation
  answered. It now returns the subclass's answer, and a *throwing* predicate propagates out of
  `EvaluateAsync` instead of being logged and skipped fail-open. **Persisted scores and token spend are
  unchanged** — `ScoreAsync` always re-checked the gate as its first line, so no judge ever spent a token it
  shouldn't have.
- **`InMemoryVectorStore` top-k is deterministic on tied scores.** Equal-scoring entries came back in
  hash-bucket order, which .NET randomizes per process — so the same store and query returned a different
  order run to run, and with more ties than `k` an arbitrary member was dropped. Ties now order by id.
  Strictly-better scores never move; the SQL-backed vector stores are unchanged, so the cross-backend contract
  remains "unspecified", now stated per backend.
- **A paused job can be cancelled.** `CancelAsync` returned false for a `Paused` job and left it paused, so the
  only route was resume-then-cancel — and a resumed job is Pending, i.e. claimable, so a runner polling that
  lane between the two calls could claim it and the cancel degraded to a cooperative flag on a now-running job.
  Paused → `Cancelled` now happens in one call on all three backends, never briefly claimable.
  `RequestCancelAsync` is unchanged and still refuses a paused job.
- **`Lyntai.Generation` (EXPERIMENTAL): a non-positive size hint falls back to the configured default.**
  `"0x0"`, `"512x0"` and friends were forwarded verbatim to the Automatic1111 WebUI, which errored — while the
  method's own documentation promised the fallback and the sibling local-engine parser already did it. A
  caller relying on a non-positive size to *fail* a render now gets a 512x512 image. A usable hint still
  reaches the WebUI unclamped.

### Changed — the rest of the backlog sweep (2026-08-05)

Same D44 disclosure rule: each names what a caller observes.

- **A media run where nothing substantive failed now reports what the backends actually said.** Before, if
  every candidate returned a blameless verdict, `GenerationRouter.GenerateAsync` returned a synthetic
  `NotConfigured` / "every capable backend reported it is not configured" — inaccurate for a run in which
  every candidate actually said `Unsupported`, and it discarded the backends' own words. Now the first
  blameless result carrying a reason is returned verbatim, with its own verdict. **A real failure still always
  wins** — that guard is untouched. Affects anyone switching on the router's verdict or displaying its detail,
  notably the generation agent tools and the durable-render job's failure message.
- **`ContextWindowExceeded` from a media backend no longer benches it.** It now translates to
  `GenerationVerdict.Unsupported` (Advance) rather than `Failed` (PenalizeAndAdvance), so a few oversized
  prompts in a row stop counting toward the dead-host threshold and stop routing unrelated later requests
  away from a perfectly healthy backend. Nothing is lost from the report: "your prompt is too long" is still
  the run's stated reason, now carried by the blameless slot above. **These two are one change in two halves**
  — the mapping is only safe because the reporting slot landed first.
- **A curated-memory update that would collide is refused.** Re-categorising an entry onto a
  `(kind, content, taskKey, scope)` another entry already holds used to succeed and silently mint the
  duplicate that `AddAsync(dedup: true)` promises cannot exist — after which dedup kept returning the *other*
  row's id. It now returns `false` and writes nothing. `false` already meant "no row updated"; it now also
  means "refused", and `GetAsync(id)` distinguishes them. Best-effort under concurrent writers, like
  `AddAsync`'s own dedup check.
- **`AsChatClient()` throws `LlmVerdictException`** (below) instead of a bare `InvalidOperationException`. It
  **derives from** `InvalidOperationException` and the message text is byte-identical, so `catch` clauses and
  message parsing both keep working. **One residual break, disclosed rather than discovered:** an exact-type
  check (`ex.GetType() == typeof(InvalidOperationException)`) no longer matches.
- **`CodexAgentSession` honours `ResumeToken`.** It previously refused with a single
  `SessionEnded(Unsupported)` and no spawn, because guessing codex's resume shape would have been read as a
  prompt and billed a turn. The shape is now measured against the real CLI
  (`codex exec resume <SESSION_ID> … -`), so a multi-turn agent conversation survives on both CLI backends.

### Added — small surface, from the same pass (2026-08-05)

- **`Lyntai.Llm.LlmVerdictException`** — carries the `LlmVerdict` on the seams that must throw rather than
  return an `LlmReply`. It lives in Core beside the taxonomy it carries, not in the bridge that first needed
  it, so the next seam that has to throw does not mint a second near-identical type. `LlmVerdict.NotConfigured`
  exists so a host can offer *setup* instead of reporting an error; through the `Microsoft.Extensions.AI`
  bridge that was previously recoverable only by string-parsing an exception message.



- **`ChatResult.Usage`** — `ChatOrchestrator` was discarding `ToolLoopResult.Usage` because the result type had
  nowhere to put it, so a two-gate chat could not be metered.
- **`StructureScorer.FormatKey`** — the `ScoreContext.Extra` key the scorer reads, published as a constant the
  way `OutcomeScorer.ErrorKey` already was, so a caller stops hardcoding `"format"`. Same value; no behaviour
  change.
- **`JobSpec.DefaultMaxAttempts`** — the retry default was the literal `3` hand-copied into all three job
  stores, free to drift.
- **`FalQueueOptions.CancelSegment`** (EXPERIMENTAL package) — the one URL segment that was a hardcoded literal
  while its siblings were settable. Default `"cancel"`, so every existing host calls the identical endpoint.
  It matters because cancel is the call that stops a render already costing money, on a backend whose wire
  format is documented-not-measured.

### Documentation (2026-08-05)

No API changed. These were all sentences a consumer or a maintainer would have acted on:

- **The MCP tool host is documented as what it is.** Four shipped XML docs and several README passages still
  said it runs on Kestrel/ASP.NET Core; it has run on `System.Net.HttpListener` since 2.0.1, and the README
  contradicted itself within one page.
- **Sibling application names are gone from the shipped XML docs** — `IPromptComposer` described "the Sonora
  pattern", and two other types named siblings, all of which shipped in the NuGet `.xml` and appeared in
  consumer IntelliSense. Each now states the pattern instead of naming a stranger's app.
- **`LlmVerdict.Unsupported` appears in the taxonomy lists a consumer reads.** It surfaces with no fallback
  and no host penalty, and was missing from `ILlmRouter`'s summary, `LlmVerdict`'s own list, and the internal
  routing table that is meant to be kept in sync with them.
- **The cross-backend memory-recall guarantee is stated correctly.** `IMemoryStore.RecallAsync` and
  `ICuratedMemoryStore.SearchAsync` asserted a guarantee their own next sentence contradicted; it holds for a
  **single-token** query, and a multi-token query is per-token on SQLite and contiguous-substring elsewhere.
- **`IGenerationStreamProvider` says that nothing implements it.** The seam is designed, not exercised: no
  backend implements it and no router path consumes it, so a backend advertising streaming delivery is
  unreachable. Its chunk shape is modelled on the LLM contract rather than measured against a real TTS wire
  format, and the first real backend may reshape it.
- Also corrected: `Automatic1111Provider` renders with whatever checkpoint the WebUI has loaded and does not
  honour a pinned model; the SQLite migration runner's description of its own tag dispatch; `GenerationRouter`
  documenting that the per-verdict policy governs the inline path only; the streamed CLI path having no
  absolute backstop; and a `Paused` job not being cancellable.

### Tooling (2026-08-05)

- **`new-migration` scaffolds the `[Tags(...)]` attribute** — with a placeholder that deliberately does not
  compile until the feature is named. An **untagged** migration is run by FluentMigrator under *every* feature
  set, so a domain the app disabled would still land its table and nothing would report it. A reflection test
  now asserts every migration carries its feature tag plus `StorageFeatures.AllTag`.
- **Nine live-integration tests stopped reporting PASS while asserting nothing** — their documented skip
  mechanism was never wired, so they ran and passed with no endpoint. They now skip honestly. A `SmokeTests`
  class whose entire body was `Assert.True(true)` under the name `SolutionBuilds` was removed; the build gate
  proves that claim.
- A process-wide catch-all error matcher registered by one test could answer for any concurrently running
  test that classified an error — a rare cross-collection flake, now narrowed to a per-test sentinel.

## 2.2.0 — 2026-08-05

### Added
- **`CodexAgentSession` — the agent-session shape is no longer claude-only.** `AddCodexCliAgentSession()`
  registers an `IAgentSession` that drives `codex exec --json` and streams the agent's **tool steps**, so an
  app that shows tool activity can offer both CLI backends through one shape instead of hand-parsing codex's
  JSONL. Both `Add*CliAgentSession` extensions now also register **keyed** by provider id
  (`GetRequiredKeyedService<IAgentSession>("codex-cli")` / `"claude-cli"`), so registering both no longer
  makes the unkeyed resolve depend on registration order.
  **Read the honesty note before adopting** (`docs/DECISIONS.md` **D42**): the message/usage/terminal half of
  the mapping is MEASURED against codex-cli 0.146.0, the **tool-step half is INFERRED** — the measured run
  used no tools. It is written shape-driven, which bounds the cost of a wrong guess to two things: **no
  payload is invented or dropped** (a tool step carries codex's own item-type name and its raw item object,
  nothing renamed or normalised) and **every uncertainty stays in the tool-step half**. It does NOT guarantee
  the right KIND of event — the tool arm is reached by elimination against three names, so a non-tool item we
  don't recognise arrives as a fabricated `ToolCall`. Treat a tool step's **kind as provisional, its payload
  as reliable**, and switch on `ToolCall.Name`. Not
  emitted, because codex has no analogue: `UsageLive`, `SessionEnded.Subtype`, `UsageFinal.Model`, and
  token-level text deltas (a `TextDelta` is one whole assistant message). `ResumeToken` is **refused** with
  `LlmVerdict.Unsupported` and no spawn rather than guessed — `codex [OPTIONS] [PROMPT]` reads an unrecognized
  subcommand as a prompt, so a wrong guess would silently spend a turn; `DisallowedTools` is logged as
  unhonoured (codex's gate is the sandbox); `SystemPrompt` travels as a leading block of the prompt.
  Internally both codex paths now build argv from one source, so `--skip-git-repo-check` — the flag whose
  absence works in a dev git repo and breaks in a shipped bundle — cannot go missing from one of them.
- **Provider lifetime as a library seam (`Lyntai.Lifecycle`)** — for the app whose backend configuration is
  owned **outside** the deployment (an end user, or a store the process polls), where several configurations
  of one backend are live at once and any of them can change mid-render. `IProviderPool<TProvider>` takes a
  `ProviderKey` and a factory and hands back the instance for that configuration; `BoundedProviderPool` (the
  default, LRU + idle bounds) reuses while the key is unchanged, `TransientProviderPool` never reuses, and the
  interface is the BYO seam for anything else. **Which one is registered is the only thing that decides
  reuse** — `b.UseProviderPool()` / `b.UseTransientProviders()` at startup, with no edit at any call site.
  A replaced entry is **retired, never disposed**: in-flight calls hold their own reference and finish
  normally, because without leases a pool cannot know when the last of them is done. See
  `docs/DECISIONS.md` **D37**.
- **`ProviderKey` and its builder** — the pool's connection string, and the unit reuse, cooldown and
  admission are all keyed on. Every contribution is **named** (`ProviderKey.For(id).With("baseUrl", …)
  .WithSecret("apiKey", …).Build()`), so a forgotten member is visible in review rather than inferred;
  `WithSecret` folds in a digest and never retains the value, and the key's string form carries no secret
  material.
- **Router factories — `IGenerationRouterFactory` and `ILlmRouterFactory`** — one injected thing that
  composes a fully-governed router over the provider set a **caller** chooses, resolving each registration
  through the pool. The long-lived bookkeeping (dead-host tracker, rate limiter, usage ledger, admission
  table) stays a shared singleton across every router they hand out, which is precisely what per-call
  hand-construction throws away: a consumer that rebuilds its tracker with its router can never bench a
  failing backend. Both are registered by `AddLyntai`; the container-composed path is unchanged.
- **Cooldown and admission keyed on the CONFIGURATION, not the provider id.** Both routers take an optional
  `Func<TProvider, ProviderKey?> configuration` delegate (the factories bind it to `pool.TryGetKey`). Without
  it, behaviour is exactly as before — `p => p.Id`. With it, one tenant exhausting its quota no longer benches
  every other tenant sharing that backend, while two consumers of the same downed self-hosted host do share a
  bench.
- **`IProviderAdmission` / `ProviderAdmission` / `b.ConfigureProviderAdmission(…)`** — bounds how many calls
  may run against one configuration at a time, for a locally-run engine where simultaneous renders contend
  for a CPU or GPU. The shipped table bounds one PROCESS; the interface is the seam for a host that has to
  bound a shared engine across several (a distributed lock or lease service behind the same
  `EnterAsync`) — the routers and both factories take the interface, so nothing above it changes.
  Limits are declared per **slot** and enforced per **key**. Applied by the routers rather than by a decorator
  around a provider, because a wrapper implementing only the base seam erases the optional capability
  interfaces the generation router type-tests — which would silently stop every queued render from routing.
  Completion paths only: streams are deliberately not gated, since a stream would hold its permit for the
  whole response.
- **`IProviderIdentity`** — the `string Id { get; }` both `ILlmProvider` and `IGenerationProvider` already
  declared, now a shared base interface so the pool can be one generic type over either seam. **Both
  interfaces keep their own `Id` declaration** (as `new`), so this is binary-compatible for CALLERS as well
  as for implementors: adding a base interface is safe, but removing the member from the derived interface
  would throw `MissingMethodException` in every pre-compiled consumer that reads `provider.Id`, since member
  resolution does not walk base interfaces. The only caveat is source-level and rare — a consumer that
  implemented `Id` *explicitly* (`string ILlmProvider.Id => …`) must now also implement
  `IProviderIdentity.Id`; implicit implementation is unaffected.
- **Call-site verdict predicates — `verdict.IsOk()` and `verdict.IsTransient()`** (`LlmVerdictExtensions`).
  They hang off `LlmVerdict` itself, not off `LlmReply`, so the five released types that carry a verdict
  (`LlmReply`, `LlmChunk`, `SessionEnded`, `AgentSessionResult`, `ToolLoopResult`) share one definition.
  Deliberately **categories, not one method per member**: the enum grows (`NotConfigured` was appended after
  the 1.0 freeze), so an `IsRateLimited`/`IsRefused`/… set would make every future verdict a public-surface
  addition and leave the newest one as the only member without a helper — while `verdict == LlmVerdict.RateLimited`
  already expresses a single member perfectly. `IsTransient()` answers "may the SAME request succeed later?"
  — true for `Failed`/`Timeout`/`RateLimited`, false for everything terminal as sent (and for an unknown
  value, so an unrecognized verdict can never provoke a retry loop). **Known over-report, documented on the
  method:** `Failed` is also the classifier's catch-all, so an unrecognized PERMANENT error (a 400 whose body
  matches no pattern) reads transient. Kept deliberately — `RoutingPolicy.Retry` already re-sends to the same
  candidate only for `Failed`/`Timeout`, and a call-site predicate contradicting the router's own retry rule
  would be worse. Treat it as "worth one BOUNDED attempt", not as a licence to loop. See
  `docs/DECISIONS.md` **D39**.
- **`CuratedMemory.MetadataValue(key)`** — the null-safe read of the curated-memory metadata map, which is
  `null` on every entry written without any, so `entry.Metadata!["source"]` both throws on a missing key and
  NREs on the common case. Generic on purpose: CMEM6 retired the purpose-built `Source`/`Title` columns into
  one arbitrary map so a new payload field needs no schema or API change, and a typed accessor per key would
  re-privilege the same handful of names one layer up.
- **`AddMcpTools(params ITool[])`** — an inline overload beside the existing sequence one, for a hand-picked
  subset of a server's toolset or a BYO `ITool` alongside them. The sequence overload remains the one
  `McpToolset.FromClientAsync`'s result flows into, and its documentation now states the two-step
  connect-then-adapt shape as intentional: connecting an MCP client is async and its lifetime outlives
  registration, so the app owns the client and hands Lyntai only the adapted tools.
- **Member and type documentation** on surface the 1.0 review found bare: `ExtensionsAiProvider`'s
  constructor parameters and its `IsAvailable`/`SupportsToolCalls` (why both are unconditionally true),
  `LyntaiChatClientExtensions` (which direction of the MEAI bridge it is), `McpBuilderExtensions`, and
  `ClaudeCliProvider.ProviderId`.
- **`MigrationRunnerService.MigrateUpAsync(…, CancellationToken)`** on both storage backends — the awaitable
  twin an app owning its schema (`SchemaMigration.None`) calls from an async startup path, instead of a
  `GetAwaiter().GetResult()`. **Read what it promises before reaching for it**, because FluentMigrator's
  runner is synchronous and takes no token: the migration runs **inline on the calling thread** (no
  `Task.Run` — that would occupy a pool thread for the whole migration *and* still be uncancellable), and
  the token is honoured at the only two points that exist — **before any work** (a cancelled token leaves
  the SQLite file uncreated / never dials the Postgres connection string) and **between feature passes**.
  Under the default `StorageFeature.All` there is exactly one pass, so there it means "before starting"
  only. A pass in flight cannot be cancelled. See `docs/DECISIONS.md` **D40**.
- **`b.AddSemanticMemory(…)`** — the wiring seam for semantic recall, so an app enabling it composes with
  builder calls instead of hand-constructing a vector store, a connection factory and an embedder. Overloads
  mirror `AddEmbeddings` (instance / factory / by type), plus a no-argument one for when the embedder arrives
  from elsewhere (`AddOpenAiCompatibleEmbedder`, or a host registration made before `AddLyntai`). Its real
  value is that it **states the intent**: semantic memory was previously enabled purely as a side effect of
  an `IEmbedder` being registered, so forgetting one registered no `ISemanticMemory` at all and every recall
  path skipped it in silence. `AddLyntai` now throws at composition instead. Everything stays substitutable —
  the vector store and `ISemanticMemory` are still `TryAdd`-registered, so a host's own registration wins,
  and the concrete stores stay public for the hand-wired path. Persist the vectors with the existing
  `UseSqliteVectorStore()` / `UsePostgresVectorStore()`; `UseSqliteVectorStore`'s documentation now names the
  non-obvious prerequisite that `lyntai_vector` ships under `StorageFeature.Governance`. See
  `docs/DECISIONS.md` **D41**.

### Fixed
- **A capability gap no longer benches a healthy media backend.** `GenerationVerdictClassifier` translated
  `LlmVerdict.Unsupported` — "this backend cannot do THIS request" — into `GenerationVerdict.Failed`, even
  though `GenerationVerdict.Unsupported` exists and means the same thing. Since `GenerationRoutingPolicy` maps
  `Failed` to `PenalizeAndAdvance` and `Unsupported` to `Advance`, a capability gap counted toward the
  dead-host threshold, and a few of them in a row put a perfectly healthy backend on cooldown. **Who is
  affected:** anyone whose media backend reports a capability gap through the shared corpus — a
  consumer-registered `LlmVerdictClassifier.AddErrorTextMatcher` returning `Unsupported`, or an exception that
  classifies into it. **What you observe:** such a result now carries `GenerationVerdict.Unsupported` instead
  of `GenerationVerdict.Failed`, so routing advances without blame — and, consistently with every other
  blameless verdict, `GenerationRouter` no longer reports it as the run's failure reason when a real failure
  also occurred. A `switch` on `GenerationVerdict.Failed` that was catching these needs an `Unsupported` arm.
  **Read this before choosing the version to release it in.** This is a `Lyntai.Core` type, so it carries the
  full SemVer promise rather than the `Lyntai.Generation` experimental carve-out — and it is precisely the shape
  `docs/DECISIONS.md` **D24** declines to license in a minor: *"Does NOT: silent behavior changes … or anything
  a consumer can't detect at compile time. Those stay major-bump material regardless."* No API member changed,
  so nothing here breaks a build; a consumer's `switch` on `GenerationVerdict.Failed` keeps compiling and simply
  stops matching these results. **Treat it as major-bump material** — it is recorded under `## Unreleased`,
  which fixes no version, so whoever cuts the release makes that call deliberately.
  `LlmVerdict.ContextWindowExceeded` still collapses to `Failed`, now deliberately and with its reason written
  down — **at a stated price**: `Failed` means `PenalizeAndAdvance`, so repeated oversized prompts can still
  bench a healthy backend, and the LLM domain maps that verdict to `Advance`, so the two now disagree about it.
  Keeping it reportable was judged worth that, because the alternative silently loses "your prompt is too long"
  — the one message a caller can act on. The remedy needs a router change and is filed as `TASKS.md` Part 40.
  The catch-all that hid all this is gone: every `LlmVerdict` member has its own arm, and a test fails until a
  newly added one has both a translation and an arm. See `docs/DECISIONS.md` **D43**.
- **The HTTP generation backends now have the per-call deadline their infinite `HttpClient` timeout was already
  resting on** (`TASKS.md` GEN11). 2.1.0's `Add*` shims register a client with `Timeout.InfiniteTimeSpan`
  because a render routinely outlives the 100-second default — but no deadline existed to take over:
  `GenerationRequest.TimeoutSeconds` was on the contract and **read by nothing**, so a backend that accepted the
  connection and then stalled hung until the caller's token fired, and a background render with no cancel waited
  forever. Unbounded and silent is worse than the cut-off it replaced. Each of `OpenAiImageOptions`,
  `Automatic1111Options`, `ComfyUiOptions` and `FalQueueOptions` now carries a **`Timeout`** — 10 minutes for the
  inline render backends, 2 minutes for the queue ones — overridden per call by `GenerationRequest.TimeoutSeconds`
  where a request exists, and opted out of with `Timeout.InfiniteTimeSpan`. A fired deadline is a
  **`GenerationVerdict.Timeout` result, not a throw** (these backends are contractually fail-safe), while the
  caller's own cancellation still propagates as `OperationCanceledException` — the two are told apart by the
  caller's token, the same discriminator `OpenAiCompatibleProvider` uses on the LLM side. A BYO client's own
  `HttpClient.Timeout` now also surfaces as that verdict instead of escaping as `TaskCanceledException`.
- **What a deadline means for the queue backends is now stated rather than assumed.** For `FalQueueProvider` and
  `ComfyUiProvider` it bounds **one HTTP call** — submit, status, fetch, cancel — never the render, which
  outlives every individual call and is polled across job re-dispatches and process restarts by
  `GenerationRenderJobHandler`; bounding the whole operation is the job's retry budget to do, not the provider's.
  Consequently a timed-out **status poll reports the operation as still `Running`**: no answer is not a failed
  render, and reading it as terminal would abandon a submitted (and billed) generation. A timed-out **submit**
  fails with a detail saying the request may still have been enqueued.
- Probes (`OpenAiImageProvider`, `Automatic1111Provider`, `ComfyUiProvider`) are bounded by the same option — with
  the shim's infinite client they could otherwise stall indefinitely against a host that accepts connections.
- **A submission that gets no answer no longer causes a second, paid submission elsewhere.** `GenerationOperation`
  gained an additive **`Inconclusive`** flag for the one case the `Failed` status cannot express: *the backend
  never answered, so nobody knows whether it took the work*. `GenerationRouter.SubmitAsync` now **surfaces** such a
  submission — carrying the provider id, so the caller learns who might hold it — instead of advancing to the next
  candidate, which would buy the same render twice; and it does not count it against dead-host cooldown, because
  no answer is no evidence of ill health. The status stays `Failed`, so every existing status check behaves
  exactly as before. `GenerationRenderJobHandler` fails such a job with the backend **named** and says it is
  deliberately not retried, since a duplicate submission is a duplicate charge. This is the same reasoning that
  makes a timed-out poll report `Running`, applied to the call that commits the money.
- **The agent tool no longer invites the retry the router just refused.** `generate_submit` reported a failure
  with the operation detail alone, dropping the `ProviderId` — and a model's default reaction to a tool error is
  to call the tool again, which re-submits work a backend may already be billing for. That path has no human in
  it, so it is the one that mattered most. An inconclusive submission now returns an observation that *instructs*:
  it names the backend, says plainly not to retry and why, points at `generate_status` as the alternative, and
  carries an `inconclusive: true` flag so a host can branch without parsing prose.
- Telemetry distinguishes the two: `RecordSubmission` tags an inconclusive submit `error.type = "Inconclusive"`
  (plus `lyntai.generation.inconclusive` on the span) rather than lumping it in with `"Failed"`. Same status, but
  not the same incident — an operator investigating a possible double charge needs something to search on.

### Changed
- **A Governance-backed `Use*` helper now fails at WIRING time when `StorageFeature.Governance` is toggled
  off**, instead of registering a store over a table that was never created. `lyntai_response_cache`,
  `lyntai_usage` and `lyntai_vector` all ship in the one Governance migration, so
  `UseSqliteResponseCache` / `UseSqliteUsageTracking` / `UseSqliteVectorStore` (and the two Postgres
  equivalents) were the only calls that could break `Use*Storage`'s own stated rule — a disabled domain is
  simply unresolvable, and that unresolvability *is* the startup signal. **What you observe:** if you call
  `UseSqliteStorage(path, StorageFeature.Memory)` and then `UseSqliteVectorStore()`, you now get an
  `InvalidOperationException` at `AddLyntai` naming the offending call and the feature to add, where you
  previously got a container that built cleanly and threw "no such table: lyntai_vector" at the first
  recall. The check is order-independent (either call may come first) and fires only on a configuration
  that was already broken — the default `StorageFeature.All` includes Governance, so existing wiring is
  untouched. It also applies **only where Lyntai owns the schema**: under `SchemaMigration.None` or an
  app-supplied `IDbConnectionFactory` no migration was going to run, the feature set never decided which
  tables exist, and adding `StorageFeature.Governance` would create nothing — so those wirings are left
  alone. `UsePostgresVectorStore` is deliberately exempt too: `PostgresVectorStore` creates its own schema
  lazily, so it works with or without the Governance migration.
  - **Two ordering consequences, now written down** — clarifications of what the guard already does, not
    behaviour changes. (1) The check is evaluated **eagerly**, at each `Use*` call, against the last feature
    selection registered *so far*, so `UseSqliteStorage(a, Memory)` → `UseSqliteVectorStore()` →
    `UseSqliteStorage(b, All)` throws at the middle call even though the FINAL selection is valid — state
    the feature set once, or widen it before the helper. (2) A **BYO-factory call made last stands the guard
    down for the whole wiring**: `UseSqliteStorage(path, …, SchemaMigration.OnStartup)` followed by
    `UseSqliteStorage(factory, …)` skips the check entirely, because the app supplied the factory last and
    therefore owns the schema. Both follow one rule — the guard follows the selection, and the last selection
    decides — but each is reachable by reordering two lines you thought were independent.
- **`AddClaudeCliAgentSession` now takes `environment`, so a portable install's `CLAUDE_CONFIG_DIR` reaches
  agent turns too.** `AddClaudeCliProvider`, `AddCodexCliProvider` and `AddCodexCliAgentSession` all already
  had it; the Claude agent session was the only one of the four without, and its sibling's docs instruct a
  host to "pass the same value to both". **What you observe:** a host that did exactly that had the variable
  honoured for completions and **silently dropped** for agent turns — so the portable CLI read and mutated
  the machine-wide install's state, which is the one thing a portable install exists to avoid. Added as a
  trailing optional parameter on both `AddClaudeCliAgentSession` and the `ClaudeAgentSession` constructor:
  source-compatible (existing calls compile and behave identically), but **not binary-compatible** for a
  pre-compiled caller of the old signature, exactly as with the router constructor parameters below.
- **`BoundedProviderPool`'s per-call idle sweep no longer allocates.** `ProviderPoolOptions.IdleTimeout` is
  set by default, so the sweep ran inside the pool's lock on every `GetOrAdd` and materialised a list whether
  or not anything was actually idle; it now scans over the dictionary's struct enumerator and allocates only
  when there is something to evict. LRU overflow eviction likewise finds its victim by a linear minimum scan
  rather than sorting every entry to take one of them. Behaviour — including which entry is evicted — is
  identical.
- **An unconfigured LLM backend is skipped, not benched — `LlmVerdict.NotConfigured`.** When an
  OpenAI-compatible endpoint answers 401/403 to a call that carried **no** credentials,
  `OpenAiCompatibleProvider` now reports the new `LlmVerdict.NotConfigured` instead of `AuthFailed`, and the
  default `RoutingPolicy` maps it to `FallbackAction.Advance`. **What you observe:** a candidate you listed
  but never configured is skipped with no cooldown and no dead-host penalty, where it previously benched that
  provider for the whole cooldown window on every first attempt; when everything is unconfigured, the
  surfaced verdict now says so, which a host can turn into a setup prompt. A key that WAS supplied and got
  rejected still reports `AuthFailed` and still cools the host — the two cases were indistinguishable before.
  It is deliberately not "a key is required": a locally-run OpenAI-compatible server (LM Studio, vLLM,
  Ollama) legitimately needs none, so only the server *demanding* one makes a missing key a configuration
  gap. This closes the asymmetry with the generation domain, which already drew the same distinction
  (`GenerationVerdict.NotConfigured`, 2.0.1); both domains now answer the same situation the same way.
  Also exposed as `LlmVerdictClassifier.FromHttpFailure(status, body, hasCredentials)` for a custom provider,
  and `GenerationVerdictClassifier` no longer flattens a `NotConfigured` from the shared corpus to `Failed`.
  - **If you switch on `LlmVerdict`:** the enum gained a member (appended last, so existing members keep
    their numeric values — binary-compatible, and a compiled consumer keeps working). A **non-exhaustive
    `switch` expression** over `LlmVerdict` will now raise **CS8509** in your build — a warning, not an
    error. Code that treated the old `AuthFailed` as "check your API key" should handle `NotConfigured` as
    "no API key is set" rather than falling into its default branch.
  - **A blameless verdict no longer masks a real failure in the reported reply.** Introducing a verdict that
    advances without blame exposed a second-order bug: `LlmRouter` remembered the last failure
    unconditionally, so for candidates `[a host that is down → Failed, one you never configured →
    NotConfigured]` you were told **"not configured"** and sent to set up a key while the backend you *had*
    configured was the actual problem. `NotConfigured` and `Unsupported` are now remembered separately on
    both the streaming and non-streaming paths and reported only when there was no real failure at all —
    the guard `GenerationRouter` already had. **Unchanged:** which substantive failure wins (still the last
    one attempted), and `ContextWindowExceeded` still surfaces normally — "your prompt is too big" is a real
    answer. When every candidate is unconfigured you still get `NotConfigured`, not a generic error.
  - **`HttpEmbedder` deliberately unchanged in behaviour:** an embedding call has no verdict and no
    fallback — it throws — so there is nothing for it to route around. Its 401 message now says
    `(not configured: no ApiKey)` when no key was supplied, so a host can tell setup from a rejected key.
    Message only; the exception type is still `HttpRequestException`.
- `LlmRouter` and `GenerationRouter` gained two **optional** trailing constructor parameters (`configuration`,
  `admission`). Source-compatible — existing constructions compile and behave identically — but **not binary
  compatible**: a pre-compiled consumer calling the old constructor gets `MissingMethodException` until it
  recompiles against this version. Nothing else changes for an app that configures its backends at startup;
  the routers `AddLyntai` builds still pass neither.

## 2.1.0 — 2026-08-04

### Added
- **Named factories for `GenerationInput`** — `GenerationInput.Init(bytes, "image/png")`, `.FirstFrame(…)`,
  `.Reference(…)`, `.Voice(…)`, and `.From(role, …)` for a role a backend documents itself, each with a
  `System.Uri` overload. The positional constructor is a **silent-misbinding trap**: three of its four slots are
  strings and `Role` is last, so `new GenerationInput(GenerationInputRoles.Init, bytes, "image/png")` compiles,
  binds `"init"` to the media type and leaves `Role` null — an img2img request then degrades to text-to-image
  with **no error anywhere**, the source image simply ignored. The factories make the role impossible to omit.
  Purely additive; the constructor is unchanged. See `docs/DECISIONS.md` **D35**, which also records why
  `GenerationArtifact` was checked and deliberately left alone.
- **Per-backend `Add*` shims for `Lyntai.Generation`** — `AddOpenAiImageProvider`, `AddAutomatic1111Provider`,
  `AddComfyUiProvider`, `AddFalProvider` and `AddLocalDiffusionProvider`, the media counterpart of
  `AddOpenAiProvider()` / `AddOllamaProvider()`. Every backend previously had to be hand-constructed with its own
  `Func<HttpClient>`. BYO stays optional on all of them; omit it and Lyntai registers a named client with an
  **infinite** `HttpClient` timeout so the per-call deadline owns cancellation (a render routinely outlives the
  100-second default). `GenerationProviderBuilderExtensions.HttpClientName(id)` is public so a host can decorate
  the same client without abandoning the shim.

### Fixed
- **A BYO `HttpClient` handed to a generation backend is no longer disposed by Lyntai.** The backends did
  `using var http = httpFactory()`, so the natural BYO lambda `_ => _myClient` worked for the first render and
  threw `ObjectDisposedException` on the **second**. BYO now means what it means on the LLM side: the host owns
  the lifetime. See `docs/DECISIONS.md` **D36**.

### Changed — `Lyntai.Generation` only (EXPERIMENTAL; source-compatible)
- `OpenAiImageProvider`, `Automatic1111Provider`, `ComfyUiProvider` and `FalQueueProvider` take a third
  `bool disposeHttpClient = true` constructor parameter. Defaulted to the previous behaviour, so existing
  hand-constructions compile and behave identically; it is **binary**-breaking for a pre-compiled caller, which
  the `Lyntai.Generation.*` experimental carve-out permits.
- `Lyntai.Generation` now depends on `Microsoft.Extensions.Http` (managed, on the runtime's own version band).
  The package is not in the `Lyntai` bundle, so the one-line install's closure is unchanged.

### Tooling
- **`consumer-smoke` was testing STALE packages** and now isn't. It packs under a fixed throwaway version, and
  NuGet never re-extracts a version already in the global cache — so after the first run ever, every later run
  restored that run's copies and reported success about code it had never compiled against. It now evicts
  `<global-packages>/lyntai.*/<version>` after packing; the first eviction removed **9** stale packages. The
  smoke app also now references `Lyntai.Generation` — the package a consumer is most likely to meet on its own,
  and the only one carrying a dependency the bundle never resolves.

## 2.0.1 — the generation platform + a coherent package graph (2026-08-04)

### Breaking — package graph only; **no namespace, type or API changed**
Every move below is a one-line `PackageReference` edit. No `using`, type name or `Add*` extension changes,
because the restructure was designed around keeping namespaces fixed.

| Was | Now |
|---|---|
| `Lyntai.Providers.ClaudeCli` / `.CodexCli` / `.OpenAiCompatible` | `Lyntai.Providers.Default` |
| `Lyntai.Generation` | `Lyntai.Core` (drop the reference — you already have Core) |
| `Lyntai.Generation.Http` | `Lyntai.Providers.Default` |
| a typical app's whole set | **`Lyntai`** (new metapackage) |

- **`Lyntai.Generation` and `Lyntai.Generation.Http` are gone as packages** — folded into `Lyntai.Core` and
  `Lyntai.Providers.Default` respectively. Both had **zero** external dependencies, so their boundaries
  isolated nothing (`docs/DECISIONS.md` D31). Verified as an exact union: the merged API baselines gained
  precisely the lines the deleted ones held, with zero removals, and all tests pass unedited.
- **New `Lyntai` metapackage** — `dotnet add package Lyntai` gets Core + the dependency-free default backends
  + in-memory storage. It ships no assembly. Anything costly stays an explicit opt-in: `Storage.Sqlite` (native
  binary), `Providers.Local` (LLamaSharp), `Tools.Mcp*` (MEAI/ASP.NET Core), `Secrets.Dpapi` (Windows-only).
  This is deliberately how "most consumers use X" is served — **not** by adding X's dependencies to the
  mandatory package.
- **`Lyntai.Providers.ClaudeCli`, `Lyntai.Providers.CodexCli` and `Lyntai.Providers.OpenAiCompatible` are
  merged into `Lyntai.Providers.Default`.** Packages are now split by **dependency footprint, not by vendor**
  (`docs/DECISIONS.md` D31): those three need nothing beyond Core and managed `Microsoft.Extensions.Http`, so
  bundling them costs a consumer nothing and removes two ids plus their release ceremony. Everything that
  drags something stays separate — `Providers.Local` (LLamaSharp + native backend), `Storage.Sqlite` (native
  SQLite binary), `Tools.Mcp.Hosting` (ASP.NET Core), `Secrets.Dpapi` (Windows-only), `Providers.ExtensionsAi`
  (MEAI) — because a console app wanting the `claude` CLI must not acquire llama.cpp to get it.
  **Migration is one line:** replace those `PackageReference`s with `Lyntai.Providers.Default`. **No code
  changes** — the namespaces (`Lyntai.Providers.ClaudeCli`, `.CodexCli`, `.OpenAiCompatible`) are unchanged, so
  every `using`, type name and `Add*` extension still resolves. The three old ids stop receiving updates at
  1.2.2.

- **The `Lyntai` bundle now includes `Lyntai.Providers.ExtensionsAi`** — the MEAI bridge in both directions
  (`IChatClient` → provider, and `AsChatClient()` back) on the one-line install. It is free: MCP already pins
  `Microsoft.Extensions.AI.Abstractions`, so the bundle's third-party closure is unchanged at 15 packages (only a
  version unification), for 38 KB of managed code that trimming removes entirely.
- **NEW PACKAGE `Lyntai.Generation`** — the five media backends move out of `Lyntai.Providers.Default` into
  their own package, and their namespaces are corrected to `Lyntai.Generation.Providers`. The old names were
  wrong: `Lyntai.Generation.Http` contained `LocalDiffusionProvider`, a *subprocess* backend, whose own namespace
  `Lyntai.Generation.Local` read as the unrelated package `Lyntai.Providers.Local` (GGUF inference). The
  generation *contracts* stay in Core; this is the backend set, and it has **zero third-party dependencies**.
  `dotnet add package Lyntai.Generation` pulls Core with it. **It is deliberately NOT in the `Lyntai` bundle** —
  an experimental domain most consumers don't use should not arrive with the one-line install (D32). Justified by
  a new axis (`docs/DECISIONS.md` **D34**): a split for release CADENCE, since media is where the growth is and
  every new backend would otherwise churn the package every chat consumer installs — 10 of `Providers.Default`'s
  26 public types were media. Done at 2.0.1 precisely because generation has never shipped, so the namespace
  change had zero consumers to protect; after this release the same fix would cost a major bump.
- **`Lyntai.Generation.*` ships EXPERIMENTAL, exempt from the SemVer promise** until GEN-VERIFY closes. The
  platform is complete and tested, but two backends were written from vendor docs with no key to call, one's argv
  is ported rather than measured, and `IGenerationStreamProvider` has no implementation yet — so its shape is
  expected to change on contact with reality. Marking it costs nothing; freezing it would force either a major
  bump for a fix we already anticipate or a known-wrong API left in place. Every other domain carries the full
  promise.
- **`node devtools/dev.mjs consumer-smoke`** — a repeatable RELEASE gate that packs every package to a scratch
  feed and then restores, builds and runs a fresh console app against them. Everything else tests the repo via
  project references; this is the only check that exercises what actually ships (nuspecs, dependency groups,
  symbol packages, the bundle restore). Run by hand once before this release it found two defects nothing else
  could — an empty symbol package on the new bundle, and an unconfigured backend reporting the wrong verdict.
  Deliberately not in `verify`: it is minutes, not seconds.
- **`node devtools/dev.mjs new-package <Lyntai.X>`** — scaffolds an adapter package (csproj + the conventional
  `Add*` builder entry point, which doubles as the API-gate anchor type) and registers it in all seven mechanical
  registries; the API baseline seeds on the next test run. Bundle membership is deliberately not automatic — that
  is a budgeted decision under D32. Adding the next package is now one command plus writing the adapter.
- **Many small packages is now the settled shape, with tooling to match (`docs/DECISIONS.md` D33)** — a package
  as small as `Lyntai.Secrets.Dpapi` (8 KB) earns its id, because the cost of a package is its DEPENDENCY, not its
  size: merging a Windows-only 8 KB adapter into anything larger makes that larger thing unusable off Windows. So
  granularity stays, and the growth is paid for in tooling: `node devtools/dev.mjs check-packages` (now in
  `verify`) treats the filesystem as the source of truth and fails unless every packable project is registered in
  all nine places that must know about it — `packableProjects`, the solution, `ApiSurfaceTests` (both the list and
  the anchor map), the test project's references, a baseline file, the `docs/AOT.md` table and the README table —
  plus the reverse, so a deleted package leaves no orphan baseline or stale entry. The miss that matters most is
  silent: no `ApiSurfaceTests` entry means the package ships with **no public-API gate at all**.
- **A bundle membership POLICY, enforced (`docs/DECISIONS.md` D32)** — with the package count set to keep growing,
  what belongs in the one-line install is now a written rule plus a gate rather than a per-package argument. A
  package joins only if it adds no third-party dependency outside the `Microsoft.Extensions.*` band, or if it is
  near-universal and the cost is accepted explicitly and recorded; never if it carries a native payload or a
  platform-specific API. `node devtools/dev.mjs check-bundle` (now in `verify`) reads the bundle's resolved
  dependency closure and fails when anything unapproved appears — or when the allowlist goes stale. Also settled:
  ONE bundle, never a family of curated subsets (they multiply combinatorially and every one is an id that can
  never be unpublished — D29).
- **Renamed the project behind the bundle `Lyntai.Meta` → `Lyntai.Bundle`** (the published id is unchanged and
  stays `Lyntai`). "Metapackage" is NuGet's term, but a project name is read by people deciding where code goes.
- **An unconfigured image backend is skipped, not benched** — `OpenAiImageProvider` guarded `BaseUrl` but not
  `ApiKey`, so with no key it made a live call, got a 401 and reported `AuthFailed`, which (with the new GEN5
  cooldown) benched the backend for the cooldown window on every first attempt. It now reports `NotConfigured` —
  routing skips the candidate blamelessly and a host can offer setup. The distinction lives in Core as
  `GenerationVerdictClassifier.FromHttpFailure(status, body, hasCredentials)`: an auth failure with nothing to
  authenticate WITH is a configuration gap, while a REJECTED key stays `AuthFailed`. It is not simply "require a
  key", because an OpenAI-compatible endpoint run locally (LM Studio, vLLM, Ollama) legitimately has none — only
  the server *demanding* one makes a missing key a config problem.
- **Honest trim/AOT metadata in `Lyntai.Providers.Default`** — three generation backends (`OpenAiImageProvider`,
  `Automatic1111Provider`, `ComfyUiProvider`) built their request bodies by reflection-serializing anonymous
  types, in a package that stamps `IsTrimmable` into its assembly to tell a consumer's trimmer it is safe to
  trim. The claim was false: those calls break under trimming/AOT. They now build `JsonObject`s and serialize
  reflection-free, matching the chat payloads in the same package (`Payloads/OpenAiPayload`). **`verify` now
  fails on ANY warning in a published project** (`node devtools/dev.mjs check-warnings`), because a warning
  nobody fails on is how a shipped claim rots silently — this is what caught it.
- **`GuardedStream.ReadAll` honours `WithCancellation`** — the public async iterator's `CancellationToken`
  parameter lacked `[EnumeratorCancellation]`, so a consumer's `.WithCancellation(ct)` was silently dropped and
  a cancelled caller could receive a fabricated terminal instead of an `OperationCanceledException`. Affects an
  external provider author using the shared read-loop; Lyntai's own providers pass the token explicitly.
- **Governance + telemetry parity for generation (`AddGenerationUsageBudget()`, `AddGenerationRateLimit()`,
  cooldown by default)** — the generation domain now has the LLM front door's governance, REUSING that machinery
  rather than duplicating it: `DeadHostTracker` benches a backend that rate-limits or rejects a key (and counts
  repeated transient faults toward the threshold), `IUsageTracker` accounts spend, and the same token bucket
  throttles. Two boundaries are deliberate and tested. **Cooldown keys are domain-prefixed**, so a host whose
  chat provider and image backend share an id never has a chat outage bench its renders. **Throttling is
  configured separately** (`GenerationOptions.RateLimit`), because a render and a chat turn hit different
  vendors' limits and one shared bucket would let a render starve the chat that requested it. **Spend, by
  contrast, is shared on purpose** — renders record into the same tracker as chat, so "what has this app spent"
  stays one number; only COST caps bind a render (it spends no tokens and claims none). The cap is checked before
  a render *and* before a submission, since submitting is what commits the money for a hosted video whether or
  not anyone fetches it, and `GenerationRenderJobHandler` records what a finished durable render cost — the only
  place that still exists by then. New: `GenerationRequest.Consumer` (round-tripped through the durable-job
  payload, so a resumed render still bills to whoever asked), `GenerationRoutingPolicy.ExemptSoleCandidate` plus
  the `PenalizeAndAdvance`/`CooldownAndAdvance` actions, and a THIRD OTel source/meter `Lyntai.Generation`
  (per-attempt spans, `lyntai.generation.duration`/`.cost`/`.artifacts`) — kept out of the GenAI source because a
  render is not a `gen_ai.*` chat operation, while `gen_ai.system`/`gen_ai.request.model` carry over so spend can
  still be grouped by vendor across both domains. Agent-driven renders default to consumer `"agent"`, so
  `Budget.PerConsumer["agent"]` fences off the runaway-spend case without limiting what a user's own click may
  spend; a model cannot name its own consumer.
- **Generation as agent tools (`AddGenerationTools()`)** — the generation domain exposed as five `ITool`s, which
  is the **entire coupling** between the generation and LLM domains: neither references the other's concrete
  types, and because the LLM side already knows `ITool`, these work in the in-process tool loop *and* — with
  `AddMcpToolHost(...)` — for a CLI agent running its own loop over MCP. `generate_backends` lets a model
  discover what exists and what each backend supports before choosing; `generate` is the inline path; and
  `generate_submit` → `generate_status` → `generate_fetch` is the asynchronous path a video render actually
  needs. **Bytes never enter a tool observation** (a base64 image would blow the context window for nothing): if
  an `IGenerationArtifactSink` is registered the artifacts are delivered to it and the observation says so,
  otherwise it reports type/size/URI. Bad arguments come back as a readable error rather than a throw, so a model
  can correct itself, and unknown arguments pass through as backend options so a model can use a backend's own
  knobs without Lyntai enumerating them.
- **`FalQueueProvider`** (in `Lyntai.Providers.Default`) — the first remote video backend, over **fal.ai's
  queue API**: submit → poll → fetch, pairing with the durable render handler so one integration reaches the
  Wan/Kling/Veo-class models. The **operation id carries its model** (`"model#requestId"`), because the queue's
  status and result URLs need the model while a resumed job hands back only an operation id. A transport failure
  while polling reports **Running, not Failed** — a 500 says nothing about a render that is already paid for and
  probably still going — and an unknown status is likewise not terminal. Artifact reading is shape-tolerant
  (models return `video`/`images`/`audio`), and an unrecognised result is a failure rather than an empty success.
  Cost comes from the response, never inferred from a rate card. **Surface is documented, not measured** (no API
  key to call), so paths are options and the flag says so in the XML docs.
- **`LocalDiffusionProvider`** (in `Lyntai.Providers.Default`) — image generation on a locally-installed
  **stable-diffusion.cpp** (`sd-cli`): no key, no network, no content policy in the path. Inline delivery, since
  a local render blocks until the file exists. The engine and its weights stay the host's to provide (D26); the
  probe is free and exact (both files present). Two ported details that look incidental and are not: the spawn's
  working directory is the **binary's** directory, because the engine loads `ggml*.dll` from beside itself, and
  sizes are clamped to multiples of 64 within 256–768, which the engine requires and a CPU render makes
  advisable. Unlike the implementation this was ported from, the spawn goes through **`IProcessRunner`** — so it
  gains the BYO-runner seam, kill-the-tree cancellation, and an **inactivity clock with an absolute backstop**
  instead of one wall clock that would kill a healthy slow render. Argv and clamping are production-proven but
  **not measured here** (no engine on the dev machine), so they are pinned by exact-argv tests.
- **Durable renders (`GenerationRenderJobHandler`, `IGenerationArtifactSink`)** — an asynchronous generation
  run as a `Lyntai.Jobs` job, which is where the platform earns its keep over a thin HTTP client: the backend's
  operation id is **checkpointed before the first poll**, so a crash, deploy or restart resumes polling the
  render already in flight instead of submitting — and paying for — a second one. Progress is reported for a UI,
  each poll renews the lease, and a **lost lease stops the handler** (with the operation id in the error) rather
  than letting two workers drive one paid render. A submission no candidate accepts FAILS (a config problem a
  retry can't fix) while a still-working backend retries. Finished artifacts go to an app-implemented
  `IGenerationArtifactSink` — the platform routes and tracks the render; where bytes land stays the app's call
  (D26/D30). Composes with the existing job machinery (lanes, priorities, backoff, DLQ, cancellation) rather
  than reimplementing any of it, and the payload/checkpoint JSON is hand-written so Core keeps its AOT claim.

### Changed
- **`Lyntai.Tools.Mcp.Hosting` no longer requires ASP.NET Core.** The framework reference on
  `Microsoft.AspNetCore.App` is gone: the MCP protocol lives in `ModelContextProtocol.Core`
  (`StreamableHttpServerTransport` works on plain `Stream`s), and the ASP.NET package only supplied Kestrel
  routing glue. The per-call host now runs on `System.Net.HttpListener` — the right fit for a loopback-only
  ephemeral endpoint, needing no URL ACL or elevation for `127.0.0.1`. A console or desktop app no longer
  acquires the ASP.NET shared framework just to let a CLI agent call its tools. Public API unchanged; the
  existing MCP round-trip tests and the p3 e2e suite pass untouched (and the round-trip now completes in
  under a second).
- **MCP is in the `Lyntai` metapackage**, so a typical install gets both halves without Core acquiring the
  MCP SDK's pinned MEAI abstraction (`docs/DECISIONS.md` D31).

### Added
- **`Lyntai.Generation`** — a NEW package: the generation **platform** (image · video · audio · 3d, and any
  kind you define — `Kind` is an open string, so a non-media artifact uses the same machinery and no `Custom`
  constant is needed). Fallback is a **policy**: `GenerationRoutingPolicy` defaults to the LLM router's §6
  semantics (a content `Refused` surfaces) but a host pairing a hosted backend with a permissive locally-run
  one can set `On(Refused, Advance)` — that is the host's call, not the library's. One capability-aware provider seam
  spanning image, video and audio (and any medium next — `Kind` is an open string, as 3D already ships on real
  aggregators), with three delivery modes because real backends genuinely differ: inline (`IGenerationProvider`),
  async job (`IGenerationJobProvider` — submit → poll → fetch, universal for video and batch music) and streaming
  (`IGenerationStreamProvider` — TTS starts playback before generation ends). Async operations expose their
  **operation id**, so a render survives a restart, composes with `Lyntai.Jobs`, and works with a
  webhook-delivering backend (your app owns the endpoint and calls `FetchAsync`). Backends declare
  `GenerationCapabilities` and the router **pre-filters** on them — unlike chat models, a media backend often simply
  cannot serve a request, and that is a skip rather than a failure. Every backend answers "are you usable?"
  via `ProbeAsync` **without generating anything**, replacing the generate-and-discard test that pattern
  otherwise requires. Chaining is first-class (`artifact.ToInput(role)` → 3d → image → video). Media keeps its
  own `GenerationVerdict` vocabulary but **shares the failure corpus** (`GenerationVerdictClassifier` delegates to
  `LlmVerdictClassifier`), so there is one definition of what a 429 or a content refusal means. Lyntai
  generates nothing itself: no inference, no engine/weights provisioning, no webhook host, no artifact storage
  (`docs/DECISIONS.md` D26, D30). The LLM stack gains **zero** dependency on media — the bridge is `ITool`/MCP.
  Async-video/`Jobs` composition, governance parity, the tool bridge and pipelines follow in
  `docs/2026-08-04-generation-platform-plan.md` Plans 4–7.
- **`Lyntai.Generation.Http`** — a NEW package with the first three backends, each an independently registered
  `IGenerationProvider` over a BYO `HttpClient`:
  - **`OpenAiImageProvider`** (inline) — `/images/generations`, switching to `/images/edits` (multipart) when
    the request carries an input image. Both response variants are handled because both occur: inline
    `b64_json`, and a `url`, which is returned **as a URI artifact rather than downloaded** — the platform
    doesn't spend your bandwidth, or guess at auth for someone else's host, uninvited. A URI-only *input* is
    refused for the same reason. A content-policy rejection classifies as **`Refused`**, which is what the
    routing policy acts on.
  - **`Automatic1111Provider`** (inline) — a locally-run Stable Diffusion WebUI: `txt2img`, or `img2img` with
    `init_images` when source bytes are supplied; base64 decodes with or without a `data:` prefix. A WebUI
    that isn't running reports **`NotConfigured`**, not `Failed` — on a fresh machine that's the normal state,
    so routing skips it without penalising it. Its probe asks which **checkpoints are loaded**, because a
    WebUI with none is up but cannot generate.
  - **`ComfyUiProvider`** (**job** delivery) — local and **workflow-driven**: the caller supplies the graph in
    `Options["workflow"]` and optionally `Options["prompt-path"]` (e.g. `"6.inputs.text"`) to say where the
    prompt belongs, so `Prompt` may be null and no default graph is ever invented. Submit → poll history →
    fetch, with outputs returned as **view URIs** (a local video is easily 100 MB). An empty history reads as
    *running*, not failed. **Its surface is documented rather than measured** — no instance was available —
    so every endpoint path is a settable option and the parsing degrades to "not finished" rather than
    inventing an artifact.

  Shapes for the first two are ported from a sibling app's production implementation. 42 tests, all through a
  stubbed handler — nothing leaves the machine and no generation is billed.

## 1.2.2 — turn-free backend auth + pinned self-install (2026-08-03)

Completes the "ask and drive the backend about itself, without spending a turn" family that 1.2.0 started
(`ProbeAsync` / `UpdateAsync`): a host can now also find out **whether the backend is signed in, and as
whom**, drive sign-in/sign-out, and **pin a named version** of the backend. All generic Core capabilities —
the claude CLI is the first implementer, not the shape.

### Added
- **`IProviderAuth` + `ProviderAuthStatus` / `ProviderLoginRequest` / `ProviderAuthResult`** (Core,
  `Lyntai.Llm`) — an OPTIONAL provider capability answering the one thing every consumer needs settled
  before a turn can possibly succeed: `StatusAsync` reports `{ Authenticated, Method?, Account?, Detail? }`
  **without running a completion**. Previously a consumer either ran a completion and pattern-matched the
  failure string (a wasted, possibly billed turn) or shelled out to the backend's CLI itself — the bespoke
  provider handling Lyntai exists to remove. **"Not signed in" is a VALUE, not an exception.** `LoginAsync`
  / `LogoutAsync` drive the backend's own flows. Discovered by pattern-matching
  (`provider is IProviderAuth a`) like the other capabilities, so a backend with no login story (an
  API-key provider, a local GGUF runtime) simply doesn't implement it. `Method` and
  `ProviderLoginRequest.Mode` are free-form strings on purpose: another backend's account kinds must fit
  without an enum change. **Lyntai never stores credentials** — the backend owns its own.
- **`IProviderVersionInstaller` + `ProviderInstallRequest`** (Core, `Lyntai.Llm`) — drive the backend's own
  installer for a **named** version (`{ Version?, Force }` → the existing `ProviderUpdateResult`, so
  callers keep one result type for all self-maintenance). This is the difference between *pinning* a
  known-good version and merely taking whatever `UpdateAsync` gives you; `Updated` therefore also covers a
  deliberate **downgrade**. Lyntai still never downloads or stores a binary itself — see
  `docs/DECISIONS.md` D26 for where that line now sits.
- **`ClaudeCliProvider` implements both** — `claude auth status --json` / `auth login [--claudeai|--console]
  [--email <e>] [--sso]` / `auth logout`, and `claude install [stable|latest|<version>] [--force]`, through
  the same BYO `IProcessRunner` + command seams (and therefore the same Windows npm-shim handling) as a
  completion, from the neutral working directory, with no stdin. Notes on the contract a UI can rely on:
  `--json` is passed **explicitly** even though it is the CLI's default, so a build that predates `auth`
  rejects an unknown *flag* instead of treating the words as a **prompt** and spending a turn; the parsed
  state **wins over the exit code** (a signed-out CLI may report its state and still exit non-zero — that
  is an answer, not a broken backend); `LoginAsync` **blocks** until the browser flow finishes, fails, or a
  bounded 10-minute budget expires (never a hang), and `ct` abandons the wait; an unrecognized login `Mode`
  or a flag-shaped `Email`/`Version` is **refused without spawning anything**, so a free-form value can
  never become an invented backend flag.

- **`Lyntai.Providers.CodexCli`** — a NEW package: the authenticated OpenAI `codex` CLI as a provider
  (`AddCodexCliProvider()`, id `codex-cli`), so a fallback chain can span two independent CLI agents. It is
  the second implementer of the shared CLI seam below, which is what proves that seam generic rather than
  claude-shaped. Every command, flag and event shape was **measured against codex-cli 0.146.0** — `--help` for
  the argv, plus one real successful turn (through the `--oss` local-model path, so no tokens were spent) and
  one real failed turn for the JSONL. Three measured details that a guess would have gotten wrong:
  `--skip-git-repo-check` is **required** (codex refuses to run outside a git repo, and the engine spawns from
  a neutral temp dir); `codex login status` prints **prose** and rejects `--json`, so its auth parse is a
  conservative prose sniffer that returns "unknown" rather than guessing a signed-in state; and a bare `error`
  line — plus an `item.completed` whose item type is `error` — are **not** terminal (both appeared in the run
  that went on to succeed), so only `turn.failed` fails a call. It deliberately does **not** implement
  `IProviderVersionInstaller`: `codex update` takes no target, so this backend genuinely cannot pin a version.
  Completions run `--sandbox read-only` by default (a text completion shouldn't edit your disk); raise it with
  `new CodexCliDialect { SandboxMode = "workspace-write" }`.
- **Portable (non-global) CLI installs are a first-class wiring** — `AddClaudeCliProvider(command, environment)`,
  `AddClaudeCliAgentSession(command)` and `AddCodexCliProvider(command, environment, dialect)` now take the
  path to a CLI your app ships or unpacks itself, plus extra environment variables for that install (its own
  `CODEX_HOME` / `CLAUDE_CONFIG_DIR`), so a bundled backend needs no process-wide environment variable. The
  environment applies to the maintenance spawns too, so a probe/auth check reports the PORTABLE install's
  state rather than the machine-wide one's. `IsAvailable` now **verifies an explicit command exists** (via the
  new `ProcessRunner.CommandExists`, including an extensionless launcher rescued by its `.cmd` sibling)
  instead of trusting it — a missing portable copy makes the router skip that candidate rather than surface as
  a failed turn.
- **`CliProviderEngine` + `ICliProviderDialect` / `CliProviderDialectBase` / `CliOutputEvent` /
  `CliPromptDelivery` / `CliCommand`** (Core, `Lyntai.Llm.Cli`) — the generic engine behind ANY spawned-CLI
  backend, so adding one is a **dialect class plus a forwarding provider**, not a second copy of the rules.
  The engine owns everything that isn't backend-specific: command resolution (explicit override → the
  dialect's env vars → its default exe), spawn hygiene (no shell, neutral cwd, Windows launcher shims),
  timeouts as an inactivity clock with an absolute backstop, `LlmVerdictClassifier` verdicts, empty output as
  `Failed`, streaming order (content chunks then exactly one `Final`/`Error`), and probe → run → re-probe
  self-maintenance. A dialect supplies the vocabulary: argv, prompt delivery (stdin **or** a trailing
  argument), line parsing, and only those maintenance commands the backend verifiably has —
  `CliProviderDialectBase` claims **no** optional capability by default, so a backend is never credited with
  one that wasn't measured. `ClaudeCliProvider` is now this composition (`ClaudeCliDialect` + the engine);
  its members are unchanged and its ~90 existing tests pass untouched. A dialect can also report an **in-band
  turn failure** (`CliOutputEvent.Failure`) — needed because a CLI can print `turn.failed` and still exit 0,
  which would otherwise be flattened to a bare `Failed` with no reason; the message is classified, so a 401
  becomes `AuthFailed` (cools the host) rather than a generic retry.

### Fixed
- **The leak scan no longer fails closed on a pending deletion** (`devtools/scripts/check-sensitive.mjs
  --tree`) — a tracked file already removed from the working tree but whose deletion isn't staged yet (an
  ordinary mid-refactor state) read as "unscannable" and blocked `verify` with what looked like a leak report.
  A file that is *gone* has no content to leak, so it is now skipped and **reported** (never silently — a scan
  that skipped something must not print a bare "clean"). Fail-closed still applies to a file that exists but
  cannot be READ, which is the case that could actually hide something; both directions are covered by a
  reproduction of the original failure and an unreadable-path check.

### Note on binary compatibility
The optional parameters added to `ClaudeCliProvider`'s constructor and to the `AddClaudeCli*` extensions are
**source**-compatible but not **binary**-compatible: recompile against this version rather than dropping the
assembly in. Allowed in a minor by `docs/DECISIONS.md` D24 (all consumers are first-party and build from
source); no member or type was removed.

### Changed
- **`node devtools/dev.mjs doctor` also checks version AUTHORSHIP** — `VersionPrefix` must equal the newest
  `v*` release tag, which is true by construction between releases (the release workflow bumps it *as part
  of* releasing). A new `check-version-bump` pre-commit guard blocks a staged hand-edit of `<VersionPrefix>`
  or a hand-stamp/removal of the CHANGELOG's `## Unreleased` heading, with `LYNTAI_RELEASE=1` as the
  pipeline's (and a deliberate repair's) escape hatch. This closes a real failure mode measured in a
  sibling repo: the release workflow bumps *from* whatever `VersionPrefix` says, so a helpful-looking manual
  bump silently moves the baseline and the next release publishes the version **after** the intended one
  (a hand-edited `0.1.2 → 0.2.0` published `0.3.0`; `0.2.0` went from unreleased to skipped). The existing
  consistency checks all stayed green through it — consistency was never the property at risk, authorship
  was. See `docs/DECISIONS.md` D25.

## 1.2.1 — 2026-07-29

### Fixed
- **A Windows npm/nvm CLI shim now spawns** (`ProcessRunner`, Core) — an npm/nvm global install drops
  three launchers side by side: an EXTENSIONLESS POSIX script (`claude`), a `claude.cmd` and a
  `claude.ps1`. CreateProcess can't exec the extensionless one, so whenever the command resolved to it —
  a `where.exe` hit list without the `.cmd`, or a caller-supplied/`CLAUDE_CMD` path pointing straight at
  the shim — every spawn failed with *"The specified executable is not a valid application for this OS
  platform"* on an install that otherwise works. The runner now swaps a non-exec'able launcher for its
  spawnable **sibling** (`.cmd`/`.bat`/`.exe`/`.com`, then `.ps1` via the existing PowerShell host).
  Only paths with a directory component are probed, so a bare command name can never pick up a same-named
  file from the current directory. This is in the shared spawn path, so it fixes every caller at once —
  the reported symptom was `ClaudeCliProvider.ProbeAsync`/`UpdateAsync` reporting `Available: false` /
  `Succeeded: false` for a working shimmed CLI (`TASKS.md` CLI2, found consuming 1.2.0 on Windows).

## 1.2.0 — turn-free backend probe + self-update seam (2026-07-29)

A host can now show what its LLM backend actually IS — version, and where the backend can say so, model —
without spending a turn to find out, and can drive that backend's own updater. Generic: the claude CLI is
the first implementer of a Core capability, not the shape of it.

### Added
- **`IProviderInstallation` + `ProviderProbeResult`** (Core, `Lyntai.Llm`) — an OPTIONAL provider
  capability: `ProbeAsync` asks the backend what it is **without running a completion** (no tokens, no model
  call) and reports `{ Available, Version, Model?, Detail }`. Stronger than `ILlmProvider.IsAvailable`,
  which is a cheap guess that never contacts anything. Fails safe — an absent/stalled/erroring backend is
  `Available: false` with the reason in `Detail`, never a throw. A separate interface rather than members on
  `ILlmProvider`, so a provider that can't answer cheaply simply doesn't implement it and callers
  pattern-match (`provider is IProviderInstallation p`) over the registered provider collection.
- **`IProviderUpdater` + `ProviderUpdateResult`** (Core, `Lyntai.Llm`) — the sibling capability for a backend
  that ships its own updater: `UpdateAsync` runs it and reports `{ Succeeded, Updated, FromVersion,
  ToVersion, Detail }`. Backends have no check-only mode, so "was an update available?" is answered after the
  fact by `Updated` (the version moved). Lyntai drives the updater the backend already ships; it never
  provisions, downloads or pins a binary (that stays the host's concern).
- **`ClaudeCliProvider` implements both** — `claude --version` for the probe, `claude update` for the
  updater, through the same BYO `IProcessRunner` / `LYNTAI_PROVIDER_CMD` / `CLAUDE_CMD` seams as a
  completion, from the neutral working directory, with no stdin. `--version` is the ONLY turn-free question
  asked, deliberately: the CLI treats an unrecognized token as a **prompt** and spends a turn answering it,
  so probing by guessing subcommands would silently cost tokens. A probe stalls out after 30s (a version
  readout is sub-second work); an update gets the configured provider clocks, since it legitimately
  downloads.
- `ProviderProbeResult.Model` is **null against today's claude CLI** — it has no turn-free way to report its
  resolved model, and the probe never guesses one. Read the model actually used from
  `AgentStreamEvent.UsageFinal.Model` after a turn; the field fills in only if a build labels one on its
  version line (`model: x`), or for a backend that knows its loaded weights.
- The provider stub answers `--version` and `update|upgrade` before reading stdin, so both paths are
  covered by a real spawn in tests.

## 1.1.0 — generic CLI tool-hosting (2026-07-29)

CLI tool-hosting is no longer claude-shaped: the host is generic and each CLI contributes only its flags
and config-file shapes. Rationale in `docs/DECISIONS.md` **D23**; the minor-with-a-break versioning call
is **D24**.

### Breaking
- **`Lyntai.Providers.ClaudeCli.Mcp` is removed** (package and its `AddClaudeCliMcpTools()` extension).
  It had shrunk to one line of sugar, which does not justify a package id. **Migration — replace the
  package reference with `Lyntai.Tools.Mcp.Hosting` and the call with the dialect:**
  ```diff
  - .AddClaudeCliMcpTools()
  + .AddMcpToolHost(new ClaudeCliMcpDialect())     // Lyntai.Tools.Mcp.Hosting + Lyntai.Providers.ClaudeCli
  ```
  Host tweaks that would have gone to `AddClaudeCliMcpTools(configure)` go to the second parameter of
  `AddMcpToolHost(dialect, configure)`. Behavior is unchanged. Shipping in a minor per D24 (every consumer
  is currently first-party).

### Added
- **`Lyntai.Tools.Mcp.Hosting`** — the provider-neutral half of CLI tool-hosting, split out of the removed
  package (which was named for one consumer but referenced only `Lyntai.Core`). It owns the ephemeral
  loopback MCP server, the `ITool` bridge, token minting, temp-file handling and teardown;
  `AddMcpToolHost(dialect, configure?)` wires it. The OUTBOUND twin of `Lyntai.Tools.Mcp`.
- **`IMcpCliDialect` / `McpEndpoint` / `McpCliContext`** (Core, `Lyntai.Agents`) — the per-CLI half:
  flag names and config-file shapes, and nothing else. Supporting another CLI that runs its own agent
  loop is now one small class, no new package and no change to the host. In Core (not the host package)
  so a provider can ship a dialect without taking on the ASP.NET Core dependency — which would also cost
  the provider its AOT compatibility.
- **`ClaudeCliMcpDialect`** (`Lyntai.Providers.ClaudeCli`) — the `claude` flags/config shapes, now living
  with the claude provider. Costs that package no new dependencies.
- **`McpToolHostOptions`** — the MCP server name and bind address are configurable instead of hard-coded.

### Changed
- **`ICliToolProvisioner` is resolved KEYED by provider id**, first registration also taking the unkeyed
  slot as a fallback. Previously an unkeyed `TryAddSingleton`, so two CLI providers that each run their
  own agent loop collided — first registration won and the wrong dialect was injected into both.
- **`Lyntai.Providers.ClaudeCli` stays AOT-compatible and ASP.NET-free.** Only the dialect moved into it;
  the Kestrel host did not. Apps using the plain CLI provider gain no new runtime requirement.

### Internal (no public surface change)
- **OpenAI-compatible endpoint/auth rules deduped** into `OpenAiEndpoint`. `OpenAiCompatibleProvider` and
  `HttpEmbedder` each carried their own copy of the flavor resolution, the Azure `/openai/v1` rule, the
  `/v1`-suffix logic and the `api-key` header block — differing only in route name. A drift between the two
  copies would have been silent (chat keeps working while embeddings 404, or the reverse).
- **`LyntaiBuilder` qualification cleanup** — ~55 fully-qualified `Lyntai.Llm.Caching.…`-style references
  replaced with usings.

## 1.0.0 — API freeze (2026-07-28)

**Lyntai 1.0.** The adoption gate is met (three sibling apps on 0.31.1) and, before the permanent surface
freeze, a multi-agent adversarial API review + a read-only consumer-usage review settled the public
surface (`docs/DECISIONS.md` D21). **From 1.0, SemVer 2.0 is in force** — no breaking public-API change
without a major bump, gated by `ApiSurfaceTests` (D22). This release also collapses the accreted 0.x
migration ledger into clean per-domain baselines (D12 one-time exception).

### Breaking
- **Migration baseline reset (consumer action required).** The 0.x SQLite (16) and Postgres (14)
  migrations are collapsed into **9 per-`StorageFeature` baselines** each (`202607280001..0009`). The NET
  schema is UNCHANGED — a schema-equivalence gate (`MigrationSchemaSnapshotTests` byte-level for SQLite,
  `PgMigrationSchemaSnapshotTests` catalog-level for Postgres) proves it — but the ledger is renumbered.
  **Before the first 1.0 run, drop your `lyntai_*` tables (including `lyntai_version_info`) or delete the
  dev database**; Lyntai recreates them. One-time, pre-1.0 only — post-1.0 the ledger is append-only.
- **Public-surface shrink/settle** (pre-freeze): `Lyntai.Tools.Mcp.McpTool`,
  `Lyntai.Providers.OpenAiCompatible.ProviderDetect` (incl. `Detect`), and
  `Lyntai.Providers.ExtensionsAi.LyntaiChatClient` → `internal`; FluentMigrator migration classes removed
  from the public surface; `LocalModelOptions.AntiPrompts` → `StopSequences`;
  `OpenAiCompatibleOptions.Flavor`/`OpenAiCompatibleEmbedderOptions.Flavor` `string` → `OpenAiFlavor` enum;
  `IVectorStore` gains a required `DeleteAsync(collection, id)`.

### Added
- **`IScorer.Applies(ScoreContext)`** — an optional default-interface predicate to gate a scorer
  declaratively, checked by `IScoringService` before `ScoreAsync` (distinct from `ScoreAsync` returning
  null = "ran, no score"). Non-breaking to existing scorers.
- **`IVectorStore.DeleteAsync(collection, id)`** — single-vector removal across InMemory / SQLite /
  Postgres (pgvector), for incremental collection updates without a full rebuild.
- **`OpenAiFlavor` enum** (`Auto`/`OpenAi`/`Ollama`/`OpenRouter`/`AzureOpenAi`) — a typo-safe replacement
  for the old magic-string flavor; `Auto` = URL-detected.
- Member-level XML docs on non-obvious frozen surface: `PostgresVectorStore` search/upsert/remove,
  `HttpEmbedder.EmbedAsync` (throw contract), `DpapiSecretProtector` (all-input → `CryptographicException`),
  `ClaudeCliProvider.IsAvailable` (optimistic BYO-runner).
- **Built-in `IEmbedder` for OpenAI-compatible endpoints** (EMB1): `Lyntai.Providers.OpenAiCompatible` now
  ships `HttpEmbedder` + a `builder.AddOpenAiCompatibleEmbedder(id, o => { o.BaseUrl; o.Model; o.ApiKey; })`
  method, so an app already talking to an OpenAI-compatible chat endpoint can turn on semantic memory
  (`ISemanticMemory`) **without a BYO embedder**. It POSTs the batched `{model, input[]}` body and extracts
  vectors tolerantly from either the OpenAI/LM-Studio `data[].embedding` shape (re-ordered by the
  authoritative `index`) or Ollama's `embeddings[[…]]` shape — one impl covers OpenAI, LM Studio, OpenRouter,
  Azure, and local Ollama. Endpoint + flavor reuse the chat provider's `ProviderDetect` (Ollama → native
  `/api/embed`; a bare Azure resource → `/openai/v1/embeddings`; else `/v1/embeddings`, not double-prefixing a
  `/v1` base) and the same BYO-`HttpClient` seam; the per-call deadline is `LyntaiOptions.ProviderTimeout`.
  `OpenAiCompatibleEmbedderOptions.BatchSize` splits an over-cap input list into several requests. Pairs with
  the existing in-memory / `UseSqliteVectorStore` / pgvector vector stores. Additive — no breaking change.
  Live-gated Ollama coverage sits alongside the chat provider's (`LYNTAI_LIVE_OLLAMA`, embed model via
  `LYNTAI_OLLAMA_EMBED_MODEL`, default `nomic-embed-text`).

## 0.31.0 — 1.0-prep API sign-off + the curated metadata catalog (2026-07-27)

**1.0-prep: infrastructure + the final API sign-off pass**, plus the curated catalog's evolution into a
searchable, metadata-carrying store. The whole public surface was audited name-by-name against the design
contract and the .NET ecosystem's naming conventions; the resulting breaks are pre-1.0 (1.0 itself is
adoption-gated — see ROADMAP). Decisions recorded in `docs/DECISIONS.md` D19. **One released DB change:**
the CMEM6 migration `202607270003` folds the released `Source` and the same-batch `Title` into the new
`Metadata` field and drops both columns (data-preserving backfill); every other renamed C# member maps onto
a frozen column via SELECT aliases.

### Added
> A push/PR CI workflow was briefly added here and then removed by decision: verification stays MANUAL
> (`node devtools/dev.mjs verify` before any commit/release) and releases stay manual (`release.yml`,
> triggered by hand) — see `docs/DECISIONS.md` D20.
- **`ICuratedMemoryStore` metadata field + query index** (CMEM6): `CuratedMemory` gains an arbitrary
  app-owned `string→string` `Metadata` map (`title`, `source`, `author`, `category`, …) — stored as one
  opaque JSON field per backend (via `CuratedMetadataJson`) and made QUERYABLE by a `metadataMatch` argument
  on `ListAsync`/`SearchAsync` (matches every given key/value pair exactly — AND). A plain relational
  `lyntai_curated_meta(memory_id, key, value)` index backs the query, so metadata filtering is identical
  across SQLite/Postgres/InMemory with no `jsonb`/JSON-function divergence; migration `202607270003` adds the
  column + index. **Breaking (pre-1.0):** `CuratedMemory` drops `Source` and `Title`, and `AddAsync`/
  `UpdateAsync` drop those params — both fold into `Metadata`. The migration backfills the existing
  `source`/`title` values into it before dropping the columns (data-preserving); a titled prompt lead is now
  rendered app-side from a `title` metadata key, and `SearchAsync` narrows to content-only. See
  `local/superpowers/specs/2026-07-27-curated-metadata-design.md`.
- **`ICuratedMemoryStore.UpdateAsync(..., kind:)`** (CMEM5): re-categorise a curated entry in place — an
  optional `kind` (COALESCE; null = leave unchanged) moves a note between kinds keeping its id and
  `created_at` instead of forcing a remove+re-add. All three backends.
- **`ICuratedMemoryStore.SearchAsync`** (CMEM4): keyword search over the curated catalog — matches CONTENT,
  with the `ListAsync`-family strict filters (`kind`/`taskKey`/`scope`, `enabledOnly` default false, the
  `metadataMatch` map) and a `limit` cap; a whitespace query returns empty (`ListAsync` is the enumeration
  path). Reuses the lexical-memory index machinery per backend, with the same documented divergence and
  fail-open behavior as `IMemoryStore.RecallAsync`: SQLite matches any ≥3-char token via a `lyntai_curated_fts`
  FTS5-trigram table (bm25-ranked, LIKE fallback); Postgres matches a contiguous substring via
  pg_trgm-accelerated ILIKE (recency-ranked, GIN-indexed); InMemory substring/recency. The SQL curated stores
  gained the optional `ILogger` constructor parameter the memory stores already had.
- **SourceLink / deterministic release builds** (`src/Directory.Build.props`): `PublishRepositoryUrl` +
  `ContinuousIntegrationBuild` under GitHub Actions (the manual `release.yml` pipeline) — stepping into
  Lyntai from a consuming app resolves sources from the repo.
- **Azure OpenAI as a first-class flavor**: `AddAzureOpenAiProvider(...)` preset,
  `ProviderDetect.AzureOpenAi` (detects `*.openai.azure.com`), bare-resource endpoint completion to
  `/openai/v1/chat/completions`, and the `api-key` header sent alongside `Authorization: Bearer`.
- **`IKeyValueStore.ListKeysAsync(prefix?)`** — enumerate stored keys (ordinal order, case-sensitive
  starts-with) on all three backends; `LikePattern.StartsWith` helper for SQL prefix patterns.
- **`IResponseCache.RemoveAsync(key)`** — evict one poisoned/stale entry without waiting out its TTL,
  on all three backends.
- **`IJobQueue.GetAsync(id)` / `ListAsync(status?, lane?, limit)`** — the front door's read side; apps
  watch jobs without injecting the storage-layer `IJobStore`.
- **`AddPlaintextSecretVault()`** — the LOUD dev-only opt-in for an unencrypted vault (see the
  `AddSecretVault` break below).
- **`LyntaiBuilder.AddScorer(Func<IServiceProvider, IScorer>)`** and **`AddEmbeddings<TEmbedder>()`** —
  factory/generic registration parity with the other seams.
- **`IProcessRunner.StreamLinesAsync` `maxDuration`** — an optional wall-clock backstop alongside the
  inactivity window (a slowly-dripping stream can no longer run unbounded).
- **`Lyntai.Llm.Streaming.GuardedStream` + `InactivityClock`**: the provider streaming read-loop
  (arm-the-inactivity-clock → read → stop-the-clock, caller-cancel rethrow, per-provider fault→terminal
  mapping) now lives once in Core — the hand-rolled copies in all five streaming providers were exactly
  where the wall-clock timeout bug shipped twice. BYO `ILlmProvider` authors should iterate it instead
  of hand-rolling the loop.

### Fixed
- **Rate limiter honors live option retunes** (it documented them but froze rates at construction):
  bucket rate/burst now resolve from current options on every acquire, so a global limit enabled after
  startup (env override / admin) throttles, and a per-consumer rate change applies to the existing
  bucket immediately. An explicit zero-rate consumer now refuses after its burst instead of throwing.
  The verdict classifier's custom-matcher list is also read as a lock-free snapshot per classification.
- **Streamed runs no longer buffer unbounded stderr**: `ProcessRunner.StreamLinesAsync` drains stderr
  as a rolling 500-char tail (all it ever reported) instead of `ReadToEndAsync` — a child spewing MBs
  of diagnostics while streaming can't grow the parent's memory. `RunAsync` still returns full stderr
  (that's its contract).

### Breaking (pre-1.0, unreleased batch)
- **`LyntaiBuilder.DefaultCandidates` → `UseDefaultCandidates`** — hard rename, no shim: the method SETS
  (replaces) the fallback chain, and "Use" is the set-semantics builder family.
- **`UseSqliteStorage` / `UsePostgresStorage` bool pair → `SchemaMigration` enum**
  (`OnStartup` / `OnFirstUse` / `None`) — `UseSqliteStorage(path, migrate: false, deferMigration: true)`
  ambiguity is gone; the enum names the lifecycle.
- **`AddSecretVault` now REQUIRES the encryption key** (throws `ArgumentException` on null/empty): a
  "secret vault" must never silently store plaintext. Dev/testing opt in loudly via
  `AddPlaintextSecretVault()`.
- **`IResponseCache.TryGetAsync` → `GetAsync`** (null-on-miss, matching `IKeyValueStore`/`ISecretVault`
  naming); interface also gained `RemoveAsync` (BYO impls must add it).
- **`IUsageTracker` is fully async**: `Record/Total/Reset` → `RecordAsync/TotalAsync/ResetAsync`
  (`ValueTask`); per-consumer totals aggregate case-insensitively across all three backends.
- **`IProcessRunner`**: `timeout` parameters renamed `inactivityTimeout` on both methods (they always
  were inactivity clocks — the name now says so); `StreamLinesAsync` gained the `maxDuration` parameter
  before `workingDirectory`. `ProcessResult` replaces `TimedOut : bool` (ctor position) with
  `TimeoutKind : ProcessTimeoutKind` (`None`/`Inactivity`/`MaxDuration`); `TimedOut` remains as a
  computed property and `Success` still excludes timeouts.
- **Renames for what things ARE:** `CuratedMemory.Task` → `TaskKey` (a scoping key, not a
  `System.Threading.Tasks.Task` — DB column unchanged, aliased in SELECTs);
  `OpenAiCompatibleOptions.NumCtx` → `ContextSize` (`int?`; `LocalModelOptions.ContextSize`
  `uint?` → `int?` to match); `UsageLive`/`UsageFinal` members gained the `*Tokens` suffix
  (`InputTokens`, `OutputTokens`, `CacheReadTokens`, + `CacheCreateTokens` on final).
- **Wire-format internals are now `internal`:** `ClaudeArgs`, `ClaudeAgentArgs`, `StreamJsonParser`,
  `StreamJsonEvent(+Kind)` (ClaudeCli); `OpenAiPayload`, `OllamaPayload` (OpenAiCompatible). They were
  implementation detail of the providers, never a consumer seam.
- **SQLite `ListKeysAsync`** (new API, noted for completeness): prefix matching is case-SENSITIVE
  (BINARY `substr`, not `LIKE` — SQLite's `LIKE` is ASCII case-insensitive and would have broken the
  cross-backend contract).
- `IScorer.ScoreAsync`'s `CancellationToken` gained `= default` (source-compatible; binary-identical).

`ApiSurface` baselines regenerated deliberately for all 11 assemblies — the baseline renderer itself now
also emits `sealed`/`abstract`/`static`/`required` modifiers, so a future accidental modifier change
fails the surface test too.

## 0.30.0 — 2026-07-26

Two bodies of work: (1) consumer-driven generic ergonomics from an agent-manager desktop adopter
(CLI1/TL1/TL2/PR1) plus the source-study curated-memory papercuts (CM1/CM2) — all additive; and (2) a
**whole-library foundation-hardening pass** (six parallel reviews → ~80 findings → correctness fixes,
structural dedup, and test-suite hygiene across every package, then a second adversarial review of the
pass's own diff). No new migration. **Breaking changes below (pre-1.0 minor-bump policy)** — see the
"Breaking" paragraph in the hardening section.

### Changed / Fixed — foundation-hardening pass
**Correctness (behavior fixes):**
- **Router:** thrown provider errors now classify through `LlmVerdictClassifier` (a thrown 429 cools the
  host instead of hammering it); an EMPTY provider stream (zero chunks, or Final with no content) is a
  failure that falls over / ends with a terminal Error chunk — never a silent end.
- **Rate limiter:** per-consumer buckets are case-insensitive like their options map ("Chat"/"chat" no
  longer each get a full rate); a cancelled wait refunds its permit and PROPAGATES the cancellation
  (was: a fabricated `RateLimited` reply + a polluted refusal metric). **Behavior note** for BYO
  `IRateLimiter` impls: cancellation should now throw, not return false.
- **Env config:** every numeric `LYNTAI_*` knob parses invariant — on a comma-decimal locale (de-DE)
  `"1.5"` used to parse as **15** (a silently 10x-wrong timeout).
- **ProcessRunner:** the stdin observe after stdout EOF is bounded by the inactivity clock (a child that
  closed stdout but never drained stdin could hang the call forever without a `maxDuration`).
- **Guards:** chained arg-rewriting guards now COMPOSE on the tool-call gate (`GuardRail` overrides
  `InspectToolCallAsync` with an args-aware re-thread; the interface default couldn't compose).
- **Chat orchestration:** the input gate runs on the RAW user message BEFORE memory composition — an
  input-gate Replace used to persist the whole redacted composed prompt, re-storing recalled facts as a
  new record every turn (compounding memory growth).
- **Prompts:** placeholder substitution is single-pass — a var value containing `{otherKey}` stays
  literal (was order-dependent injection over dictionary order).
- **Bridges:** prose alongside native tool calls survives transcript replay (OpenAI payload + MEAI
  forward bridge); the reverse bridge maps declaration-only tool schemas (`AIFunctionDeclaration`);
  streamed OpenAI-flavor requests send `stream_options.include_usage` so streams stop bypassing
  budget/telemetry accounting; the ephemeral MCP config (bearer token) is owner-only on Unix.
- **Jobs/scheduler:** a corrupt persisted next-run self-heals (re-anchor + overwrite) instead of
  silently freezing the schedule forever; the impossible-cron error names the expression.
- **DI:** `AddLyntai` now also throws when an `ILlmClient` is pre-registered and an `IRefusalMatcher`
  was added (it would have been silently ignored); conversation-enrichment wrapping preserves a BYO
  store's lifetime; `AddEnvelopeSecretVault` + a BYO `ISecretVault` no longer throws `InvalidCastException`.
- **Storage:** all async store methods open connections via `OpenAsync` (Postgres no longer blocks a
  threadpool thread per call); FTS recall has a deterministic id tiebreak; `InMemoryConversationStore`
  throws on a duplicate thread id like the SQL backends; Postgres `AppendMessageAsync` retries a
  transient seq race instead of surfacing a raw unique-violation.

**Structural dedup (drift-prevention):**
- **`JobStoreSql` + `JobRow` (Core):** the relational job stores' state machine (fence predicate, every
  transition, insert, reads) and row materialization live once — executed by both backends with booleans
  bound as parameters; only the claim statement stays per-dialect.
- **`DelegatingLlmClient` (Core, public):** the decorator base all five front-door decorators now derive
  from — BYO decorators can too.
- **`LazyMigratingConnectionFactory` (Core, public):** the once-only/retry-on-transient-failure lazy
  migration gate (now also covering `OpenAsync`), wrapped by both packages' migrating factories.
- **`VectorMath.Cosine` (Core, public)**, **`StorageFeatures.TagPasses`**, `StreamJsonFields.ReadUsage`,
  `GuardRail` shared gate loop + `Redact`, router `LiveCandidates` preamble, shared `JsonArgs` in the
  MCP toolset, one `LyntaiOptions` env-parse helper set, one non-convergence tail in `ToolLoop`.

**Breaking (pre-1.0):** `ChatResult.BlockReason` → `Detail` (the old name lied for non-blocked failures);
`GuardOutcome.IsAllow` removed (unused); `CandidateDedup` is now internal; `IRateLimiter` cancellation
semantics (above). `ApiSurface` baselines updated deliberately for all of the above.

**Tests:** +~30 targeted regression tests (TDD per fix); shared `Fakes/` helpers (`TestPaths`,
`TempDbPath`, one `FakeProcessRunner`, `MutableClock` relocated); the six process-global
`ClearAllPools()` re-introductions replaced with per-db pool clears; duplicated contract tests removed;
`RecallAsync(limit:)`/scoped `ForgetAsync` and the `SkipAllPermissions`+ReadOnly matrix now pinned.

**Round 2 (adversarial review of the pass itself)** — a workflow-backed review of this branch's own diff
confirmed and fixed five regressions the pass had introduced: the chat orchestrator now gates the
COMPOSED prompt too (recalled memory can't bypass input guards; the memory-compounding fix stands);
prompt placeholder keys are unrestricted again (hyphen/dot/CJK keys substitute — the single-pass rewrite
had narrowed them to ASCII identifiers); the stdin inactivity clock counts a child's active DRAINING as
liveness (pipe-granular slice writes re-arm it) and an observe/reap kill classifies as Timeout; a THROWN
transport error can no longer surface as a terminal Refused on a keyword match; and streamed OpenAI usage
is actually read (the SSE loop runs to `[DONE]` and parses the trailing empty-choices usage chunk).
Plus: consumer identity is now case-insensitive END-TO-END (usage-tracker totals aggregate across casings
— closes a 2× per-consumer budget overspend; **behavior note** for the SQL trackers' `Total(consumer)`/
`Reset(consumer)`), the dialect-neutral job-claim predicate is shared (`JobStoreSql.ClaimCandidateWhere`),
the one remaining sync connection open is async, and the `.ps1`-hosting exception to the spawn-hygiene
rule is documented where the rule lives.

### Added
- **Headless "skip all permissions" for the Claude agent session (CLI1)** — `ClaudeAgentOptions` gains an
  opt-in `SkipAllPermissions` bool. When set, `ClaudeAgentArgs.Build` emits `--dangerously-skip-permissions`
  and SUPPRESSES the conflicting `--permission-mode` / `--allowedTools` (the CLI rejects combining them), so a
  headless `-p` run against the user's OWN machine/resources no longer hangs waiting for a permission
  responder. The always-denied flow tools (AskUserQuestion/ExitPlanMode/EnterPlanMode) and the caller's
  `DisallowedTools` (+ the ReadOnly write-tool denial) are still honored. Documented as opt-in/dangerous.
- **Per-run token usage on `ToolLoopResult` (TL1)** — `ToolLoopResult.Usage` (nullable `LlmUsage`) aggregates
  every front-door call the loop made (summed input/output/cache-read tokens; cost summed when any call
  reported one, else null; null overall when no provider surfaced usage). A tool-loop consumer gets a per-run
  token/cost figure without wrapping `ILlmClient` in its own front-door decorator.
- **Live progress from `IToolLoop` (TL2)** — `IToolLoop.StreamAsync(req, maxIterations?, ct)` yields
  `AgentStreamEvent`s as the loop runs: a `ToolCall` then `ToolResult` per tool round-trip (so an interactive
  UI shows tool chips live), assistant `TextDelta`(s), a `UsageFinal` when usage was reported, and one terminal
  `SessionEnded`. Mirrors `IAgentSession.StreamAsync` (no `SessionStarted` — a Lyntai-driven loop has no
  external session id); events are TURN-granular (the native path needs the whole reply to read its structured
  tool calls). `RunAsync` now folds the same shared core, so both doors stay in lockstep. `StreamAsync` is a
  DEFAULT interface method (a BYO `IToolLoop` that only implements `RunAsync` gets a functional post-hoc stream
  for free; the built-in `ToolLoop` overrides it with a live one) — so this is additive, not a break.
- **Curated-memory dedup-on-add (CM1)** — `ICuratedMemoryStore.AddAsync` gains an opt-in `dedup` bool: when
  true the add is idempotent on the (kind, content, task, scope) identity (returns the existing id, writes no
  second row — mirroring `IMemoryStore.RememberAsync`), so a consumer can write a fact idempotently without a
  pre-`ListAsync`+compare. Default (false) keeps the deliberate-catalog always-insert behavior. Across
  InMemory/SQLite/Postgres (null-safe identity match via `IS` / `IS NOT DISTINCT FROM`).
- **Curated-memory `scope` filter on `ListAsync` (CM2)** — `ICuratedMemoryStore.ListAsync` gains a strict-
  equality `scope` filter (the optimize/admin pass: "all notes for ONE scope, incl. disabled"); null = no
  filter (unchanged). Across all three backends.

### Fixed
- **Default `ProcessRunner` hosts `.ps1` launcher shims on Windows (PR1)** — a `.ps1` launcher (some Windows
  CLIs ship one) can't be exec'd directly by CreateProcess; the runner now resolves it (preference
  `.cmd` → `.exe` → `.ps1`) and hosts it via `powershell -NoProfile -ExecutionPolicy Bypass -File`, rather than
  failing with a Win32Exception. The `.cmd`/`.exe` resolution and BOM-less UTF-8 streams were already in place
  — so the common Windows CLI shims now work out of the box, and a BYO `IProcessRunner` is only needed for a
  genuinely different launch policy (sandbox, remote, custom encoding).

### Docs
- **New `.claude/knowledge/generic-library.md`** — the discipline for turning a consumer-specific request
  ("app X needs Y") into app-agnostic library surface (Core interface / adapter option / BYO seam), with red
  flags and the current backlog as worked examples. Registered in `RULES_INDEX.md`.

## 0.29.3 — 2026-07-23

The 0.29.1–0.29.3 patch series: consumer-driven generic gaps (TASKS.md Part 11 · Part 12) plus CLI-runner
hardening (the `StreamLinesAsync` large-prompt deadlock, then buffered inactivity/dead detection). All
additive; public surface grew (`ApiSurface` baselines updated) — no removals, existing calls
source-compatible. One seam signature grew: the buffered `IProcessRunner.RunAsync` gained an optional
`maxDuration` parameter (calls stay source-compatible; a BYO `IProcessRunner` implementation must add the
parameter to its override) — see Fixed, buffered dead detection.

### Added
- **Opt-in memory-prune cron job (Part 15)** — `builder.AddMemoryPruneJob(cron, olderThan?, taskKey?)`
  registers a durable-job handler that calls `IMemoryStore.PruneAsync` on a cron schedule: background GC
  that reclaims storage from cold/expired `(taskKey, scope)`s that on-write eviction never revisits. Lyntai
  owns the prune work; the app owns the pump (drives `IJobScheduler`/`IJobRunner` — no self-run timer, per
  the "no host" boundary). Idempotent; call it more than once for several schedules.
- **App-configurable memory retention (Part 14)** — `IMemoryStore` size management is now a
  `MemoryRetentionPolicy` (`LyntaiOptions.MemoryRetention` / `ConfigureMemory(...)` / `LYNTAI_MEMORY_*`),
  mirroring the configurable `RoutingPolicy`: a per-scope count cap with **FIFO or LRU** eviction, a default
  TTL, a per-scope size (character) budget, and presets (`CountCap` / `TimeToLive` / `SizeBudget` /
  `Composite` / `Manual`). Eviction is a single pure `MemoryEviction.Survivors` helper shared by all three
  backends (InMemory / SQLite / Postgres). LRU adds a `last_accessed_at` column (migration `202607220002`,
  `ADD COLUMN` + backfill — the SQLite FTS update-trigger is scoped to `content` to avoid churn). The default
  reproduces the historical 500-entry FIFO cap; `MemoryCapPerScope` now proxies it (source-compatible).
- **Curated memory `task` + `scope` (CM1)** — `CuratedMemory` gains optional nullable `Task`/`Scope`;
  `ICuratedMemoryStore` gains `ForCompositionAsync(task, scopes, enabledOnly)` (enabled entries whose task
  matches or is null, and whose scope is null/empty or ∈ scopes; an EMPTY `scopes` disables scope filtering)
  plus a `task` strict-equality filter on `ListAsync`/`AddAsync`. `CuratedMemorySections` gains the shared
  `AppliesTo` predicate and a `(task, scopes)` filter on `Compose`. New migration **`202607220001`** adds
  nullable `task`/`scope` to `lyntai_curated_memory` on SQLite + Postgres (a separate `ADD COLUMN` migration,
  not a fold, since the table shipped in 0.28; no backfill — null = "applies everywhere", so existing rows
  are unchanged). Across InMemory/SQLite/Postgres.
- **`IConversationStore` count + keyset paging (G3)** — `CountThreadsAsync()` and `ListThreadsPageAsync(limit,
  after)` (cursor is the last thread of the previous page; same `created_at DESC, id DESC` order, stable across
  same-timestamp ties). Default interface methods (BYO impls keep working) with efficient `COUNT(*)` / keyset
  overrides on all three backends.

### Fixed
- **Buffered CLI completion uses INACTIVITY-based dead detection, not a wall clock (Part 1)** — the buffered
  path (`ProcessRunner.RunAsync`, driving `ClaudeCliProvider.CompleteAsync`) applied a single wall-clock
  timeout over the whole call, so a slow-but-ALIVE turn (a big prompt, a long tool loop) was killed exactly
  like a dead/stalled one — a consumer couldn't tell "working" from "hung," and raising the wall-clock just
  delayed the false kill. `RunAsync` now treats `timeout` as an **inactivity window**: it reads stdout in
  chunks and re-arms the clock on each, so the child is killed only after the window elapses in **true
  silence** (a streaming/tool-looping child keeps resetting it) — the same discipline `StreamLinesAsync`
  already used. A new **absolute `maxDuration`** backstop bounds a child that never stalls but never finishes;
  `ProcessResult.TimeoutKind` (`Inactivity` vs `MaxDuration`) says which fired, and `CompleteAsync` surfaces
  the distinction in the timeout `Detail`. `ClaudeCliProvider` passes the resolved timeout as the window and
  `MaxProviderTimeout` (raised to the window if a consumer budget exceeds the ceiling) as the backstop.
  **Seam note:** `IProcessRunner.RunAsync` gained an optional `maxDuration` parameter — callers are
  source-compatible, but a BYO `IProcessRunner` must add the parameter to its override.
- **`StreamLinesAsync` no longer deadlocks on a prompt larger than the OS pipe buffer** — the streamed CLI
  runner (`ProcessRunner.StreamLinesAsync`) used to `await` the FULL stdin write and close stdin **before**
  starting the stdout read loop. A child that emits stdout before draining stdin (e.g. `claude
  --output-format stream-json`, which prints its startup / MCP handshake first) then deadlocked on a large
  prompt: the parent blocked filling the stdin pipe while the child blocked filling the stdout pipe the
  parent hadn't begun draining — the turn never started and the call hung to its timeout (a consumer's agent
  loop would silently time out or fall back to a non-LLM path). The stdin write now runs **concurrently**
  with the read loop (its outcome observed after the loop), so stdout drains as stdin is fed — matching
  `RunAsync`'s read-first ordering. Internal behavior only; public surface unchanged.
- **`ClaudeToolCalls.FilePathOf` now reads `notebook_path`/`path` (G1)** — checks `file_path`, then
  `notebook_path` (NotebookEdit), then `path`, so an edit-tracker built from the agent stream no longer
  silently misses NotebookEdit (or any `path`-arg tool) writes.
- **Agent-session `FinalText` falls back to assistant text (G2)** — when a run ends with assistant text but an
  empty/absent terminal `result` (truncation / older CLI / provider variant), both the claude adapter's
  `SessionEnded.FinalText` and the generic `RunAsync` fold (accumulated `TextDelta`s) fall back to the
  assistant text instead of `""`, so consumers that treat empty as failure don't spuriously fail. The fold
  backfills only for SUCCESSFUL terminals — a timed-out/failed run keeps an empty `FinalText` (it is not
  dressed up as a partial success; `Verdict`/`IsError` still report the truth).

## 0.29.0 — 2026-07-20

Part 7 (app-owned storage adoption) + Part 8 (generic/sustainable review sweep) + Part 9 (storage feature
toggles) + Part 10 (actor/mailbox durable jobs).

### Changed
- **Generic typed-event conversation store (Part 7 · P2)** — a conversation is now modelled as a typed
  multi-kind event stream (text / tool-call / tool-result / usage / thinking / phase / error), not only
  role/text chat turns — so an agent transcript or a tool-loop run persists through the same surface, and a
  complex external event log fits without a bespoke schema. The enriched `ChatMessage` is
  `(Id, ThreadId, Seq, Kind, Payload, Metadata, CreatedAt)`: `Id` is a store-generated **GUID** handle;
  `Seq` is the **1-based per-thread** sequence (external event-stream schemas key on `(thread_id, seq)`);
  `Kind` is the event/message type (a role for a plain chat turn; `Role`/`Content` kept as read-only chat
  aliases); `Payload` the body (text or JSON); `Metadata` optional **per-message** JSON. `ChatThread` gains
  optional opaque `Metadata` (thread-level JSON state) with `IConversationStore.SetThreadMetadataAsync` +
  a `metadata` arg on `CreateThreadAsync`. **Add your own additional info without forking the store** via a
  new `IConversationEnricher` DI collection (`AddConversationEnricher<T>` / factory) — Lyntai owns the LLM
  storage, and each registered enricher is invoked after a thread/message write to persist the app's own
  info in its own store (auto-wired by `EnrichingConversationStore` only when an enricher is registered).
  **Breaking (pre-1.0):** `ChatMessage.Id` changes `long`→`string` (GUID); message columns become
  `id(TEXT)/thread_id/seq/kind/payload/metadata/created_at` (was `id(INTEGER)/thread_id/role/content/
  created_at`); threads gain a `metadata` column; the message index is now `UNIQUE(thread_id, seq)`
  (migrations edited in-place, pre-release — no data migration). `AppendMessageAsync` is
  `(threadId, kind, payload, metadata=null, ct)` (the store assigns Id + Seq). All three backends
  (SQLite / InMemory / Postgres).
- **App-owned storage direction (Part 7 · P3)** — settled the design: **Lyntai owns and manages the LLM
  storage schema** (its `lyntai_*` tables + migrations); an app adds its ADDITIONAL INFO via the record
  `metadata` fields and the `IConversationEnricher` seam (above), rather than pointing Lyntai at its own
  tables (which would force the app to manage schema versions). An app that genuinely needs its own backend
  still registers its own domain-store impl (a full BYO — see the `TryAdd` registration change). The
  earlier "configurable table names" direction was dropped as contrary to this.
- **App-owned cortex KV (Part 7 · P1)** — the cortex KV key namespaces are now configurable, so an app can
  point Lyntai's prompt/model overrides straight at its OWN existing keys — no prefix-translating shim, no
  duplicated rows. `PromptRegistry` and `KeyValueModelRoutingStore` take an optional `keyPrefix` ctor
  argument, surfaced on the builder as `LyntaiOptions.PromptKeyPrefix` / `LyntaiOptions.ModelKeyPrefix`.
  Defaults are UNCHANGED (`lyntai.prompt.` / `lyntai.model.`), so existing consumers are unaffected.
  **Breaking (pre-1.0):** the public `KeyPrefix` const on both stores is renamed to `DefaultKeyPrefix`; the
  effective prefix is now the instance `KeyPrefix` property.
- **KV backing table renamed `lyntai_app_config` → `lyntai_kv`** — it is Lyntai's own key→value store
  (prompt/model overrides, scheduler next-run, secret vault), never the *application's* config; the old name
  was a carry-over that read backwards for a library table. Applied in-place to the existing migration
  (pre-release — no data migration). **Breaking (pre-1.0)** for anyone reading the raw table.

### Tests
- **Dapper DateTimeOffset-handler parity pinned (Part 8 · R15)** — the SQLite + Postgres factories each
  register a `DateTimeOffsetHandler` into Dapper's PROCESS-GLOBAL registry (whichever static ctor runs last
  wins process-wide), documented as "must stay identical." Added a Docker-free test asserting the two
  handlers `Parse`/`SetValue` identically across a battery of inputs, so a silent drift is caught the moment
  one is edited (the handlers are now `internal` + `InternalsVisibleTo(Lyntai.Tests)`).
- **Cross-backend storage parity (Part 8 · R5)** — Postgres integration tests no longer false-green: they
  were `if (!pg.Available) return;` (PASS when Docker is absent), now `[SkippableFact]` + `Skip.IfNot` (via
  `Xunit.SkippableFact`) so a Docker-less run reports them SKIPPED, not passed. Extracted backend-agnostic
  shared contracts for `IKeyValueStore` / `IConversationStore` / `IMemoryStore` / `ITraceStore` /
  `IPromptVersionStore` (joining the existing Score/Job/CuratedMemory contracts) and run InMemory + SQLite +
  Postgres through them, replacing Postgres's ad-hoc re-implementations — so the in-memory test double can't
  green-light semantics the SQL stores don't reproduce. Genuinely backend-divergent behavior (memory recall
  ordering / multi-word matching — bm25 vs recency/substring) is deliberately kept OUT of the shared
  contracts as backend-specific tests (tracked as R19).

### Docs
- **Scheduler is single-process (Part 8 · R20)** — documented on `IJobScheduler` that exactly one scheduler
  process must drive the pump: unlike the job runner (N instances via atomic claim), the scheduler's
  read-due → enqueue → persist-next-run sequence is not a compare-and-swap, so two scheduler processes on a
  shared KV store would each fire every schedule once. The runner fleet can still be N; the idempotent-handler
  guidance is the backstop. (A CAS `SetNextAsync` was deferred — it needs an `IKeyValueStore` interface
  change across all backends.)
- **Env-override reference completed (Part 8 · R18)** — the durable-jobs family (`LYNTAI_JOBS_LEASE_SECONDS`
  / `_POLL_SECONDS` / `_MAX_ATTEMPTS` / `_BACKOFF_SECONDS` / `_DEFAULT_CONCURRENCY` / `_MAX_STEP_LOG`) and the
  `LYNTAI_DEFAULT_MODEL` alias were read by `ApplyEnvOverrides` but missing from the `LyntaiOptions` XML-doc
  list + README; added them (plus the cache/budget/rate-limit/tool-loop vars the README had omitted).
- **Semantic `RememberAsync` throw contract + model-swap behavior documented (Part 8 · R16)** — made
  explicit that `ISemanticMemory.RememberAsync` SURFACES failures (deliberately asymmetric with the
  fail-open `RecallAsync` — a silently-lost write is worse than a throw; the orchestrator already guards its
  own call), and that after an embedding-model swap the vector stores degrade gracefully (in-memory/SQLite
  rank a dimension-mismatched row last; pgvector rejects it) so a reindex is required. (A persistent
  per-collection dimension stamp was deliberately not added — it would contradict the intentional
  graceful-degradation design and needs an `IVectorStore` change; the stores already handle the mismatch.)
- **Version-drift reconciliation + guard (Part 8 · R7)** — refreshed the README `## Status` (was stuck at
  v0.15, now v0.28.5 with the full feature arc); relocated the agent-session (Part 6) entries from
  "Unreleased" into a `## 0.28.5` section (with 0.28.2–0.28.4 consolidated) so Unreleased reflects only the
  not-yet-released work; and added `node devtools/dev.mjs doctor` (a `pack` pre-check) that fails if the
  README Status version ≠ `VersionPrefix`, so a shipped nupkg can't advertise a stale version.
- **`ITraceService` is the BYO / app-driven persisted-trace API (Part 8 · R4)** — clarified (XML-doc +
  README) that Lyntai's batteries-included flows do NOT auto-populate a `RunTrace`; the automatic
  observability path is the OpenTelemetry `Activity` spans on the `Lyntai.Llm` / `Lyntai.Agents` sources.
  Use `ITraceService.Begin`/`Record` when you want your own durable, step-shaped run history.

### Added
- **`AddRateLimit` warns when it resolves to no effective limit (Part 8 · R21b)** — calling `AddRateLimit()`
  with all-default options (global `PermitsPerSecond=0` and no per-consumer rate) silently throttled nothing.
  It now logs a warning at front-door resolution (after env overrides are applied) pointing at the setting to
  fix, mirroring the intent of the pre-registered-client guard — while still serving (a no-op passthrough, so
  a limit raised later via env still takes effect). Only the built-in token bucket is checked; a BYO
  `IRateLimiter` owns its own effectiveness.
- **Typed `IRefusalMatcher` seam (Part 8 · R21b)** — a structured alternative to the stringly-typed
  per-request `LlmRequest.RefusalPattern` regex. Register matchers with `AddRefusalMatcher<T>()` / instance /
  factory; the refusal-screening front door runs every one on an Ok reply's text (after the central patterns
  + the request pattern) and surfaces the reply as `Refused` (no fallback) if any returns true. A matcher
  keys off the whole request (consumer/model/language) as well as the text, and a throwing matcher fails open
  (logged + ignored). **Breaking (pre-1.0):** `RefusalScreeningLlmClient`'s constructor gains an optional
  `IEnumerable<IRefusalMatcher>` before the logger (only affects direct positional construction — it's a
  front-door internal, normally wired by `AddLyntai`).
- **Reverse MEAI bridge parity — tools / json-schema / multimodal / tool turns (Part 8 · R21b)** — consuming
  Lyntai `AsChatClient()` (the reverse `IChatClient` bridge) now maps the full request surface the forward
  bridge already did: `ChatOptions.Tools` → `LlmRequest.Tools`, a JSON-schema `ResponseFormat` →
  `LlmRequest.JsonSchema`, image `DataContent`/`UriContent` → `LlmMessage.Attachments`, and assistant
  `FunctionCallContent` / `FunctionResultContent` turns → tool-call / tool-result messages. The response
  completes the round-trip: a reply's native `ToolCalls` surface back as `FunctionCallContent` (with a
  `ToolCalls` finish reason), so an MEAI app can drive tool-calling *through* Lyntai. `JsonArgs` gains a
  `Parse` companion (JSON string → arg dict); the forward bridge's private copy now delegates to it too.
- **`Lyntai.Text.JsonArgs` — shared reflection-free tool-arg serializer (Part 8 · R21b)** — the
  boxed-primitive/`JsonElement`/`JsonNode` → JSON switch that the MCP tool-host (`ToolFunction`) and the MEAI
  provider bridge (`ExtensionsAiProvider`) each carried a private copy of is now one public helper in Core
  (`JsonArgs.ToNode` / `JsonArgs.Serialize`), so the two adapters can't drift on how a `3` vs `"3"` is
  serialized. Public (not `InternalsVisibleTo`-shared) — it's the primitive any custom tool bridge wants.
- **`TraceStep.Sequence` + `TraceStep.OffsetMs` (Part 8 · R21b)** — a run-trace step now carries an explicit
  0-based timeline ordinal (`Sequence`) and its wall-clock offset from the run start in ms (`OffsetMs`),
  stamped by the recorder at `Record` time (using its injectable clock) instead of the timeline relying on
  the store's insertion order. Persisted + ordered-by on all three backends (`offset_ms` column folded into
  the existing trace migration, pre-release; `seq` already existed). Additive — both default to 0 on a
  hand-built step.
- **Durable-job partition keys — actor mailboxes (Part 10 · A1)** — `JobSpec`/`EnqueueAsync` gain an optional
  `partitionKey`: jobs sharing a `(lane, partitionKey)` run **one-at-a-time in FIFO (enqueue) order** (an
  actor mailbox), while different keys run in parallel up to the lane's concurrency. Enforced in the atomic
  claim on all three backends (a candidate with a partition is claimable only if no live-leased Running
  sibling exists; a Pending one additionally requires no Running sibling at all — a stale/crashed Running is
  *reclaimed* first, keeping its slot — and must be the earliest available Pending of the partition; priority
  still orders across partitions). `partitionKey = null` is unchanged behavior. Verified on InMemory, SQLite,
  and Postgres (live container). Builds on the existing durable persistence + atomic claim + crash-resume.
- **Storage feature toggles (Part 9 · F1)** — every storage domain is individually enable/disable-able via a
  `[Flags] StorageFeature` (KeyValue / Conversation / Memory / Score / Trace / PromptVersion / Jobs /
  Governance / CuratedMemory / All): `UseSqliteStorage(path, StorageFeature.Score | …)` /
  `UsePostgresStorage(conn, …)` register only the selected domains' stores AND migrate only their tables — a
  disabled feature lands **no `lyntai_*` table** and registers no store (its null-tolerant consumers skip it;
  a direct `GetRequiredService` throws — the startup signal that a disabled feature is being used). Selective
  migration is tag-driven: each migration carries `[Tags(nameof(StorageFeature.X), StorageFeatures.AllTag)]`;
  `All` runs one pass, a subset one pass per feature. Default (`All`) is the historical behavior. The Postgres
  monolithic `InitialSchema` was split into per-feature migrations for parity (pre-release). Verified on both
  backends against a real Postgres container.
- **Scoring read/aggregate/export on `IScoringService` (Part 8 · R17)** — `AggregateAsync` / `ExportAsync`
  (and per-session `GetAsync`) lived only on `IScoreStore`, forcing a dashboard to inject the storage
  interface and reach past the service seam. They're now on `IScoringService` too (delegating to the store,
  empty when none) — inject the service, not the store, mirroring how `ITraceService.GetAsync` wraps its store.
- **`IDbConnectionFactory.OpenAsync` (Part 8 · R12)** — an async open added NOW (as a default-interface
  method delegating to `Open()`, so it's non-breaking for existing implementers) because adding it to the
  interface after 1.0 would break every implementer. The built-in SQLite + Postgres factories override it
  with a genuinely async open (over the driver's `OpenAsync` + pragmas), so a store can stop blocking a
  threadpool thread on connect — matters most for the networked/pooled Postgres backend.
- **Public front-door decorator seam (Part 8 · R11)** — `LyntaiBuilder.AddFrontDoorDecorator(order, factory)`
  is now public, so an app can fold its OWN cross-cutting `ILlmClient` decorator (PII redaction, request
  logging, a bespoke cache) over the base client along the SAME ordered chain as the built-in governance
  decorators — instead of pre-registering a whole `ILlmClient` (which trips the governance guard). The
  built-in fold orders are exposed as public consts (`RateLimitDecoratorOrder`=5, `BudgetDecoratorOrder`=10,
  `CacheDecoratorOrder`=20) so a custom decorator can position relative to them.
- **`LlmVerdict.Unsupported` (Part 8 · R9)** — a distinct verdict for a capability/transport gap (e.g. a
  native tool call that streaming can't carry — use `CompleteAsync`), previously overloaded onto `Refused`.
  It surfaces like `Refused` (no fallback/cooldown — another candidate has the same limitation; mapped to
  `FallbackAction.Surface` in the default `RoutingPolicy`), but is distinct so telemetry/scorers don't
  conflate a capability gap with a content-policy refusal. The OpenAI-compatible + MEAI streaming providers
  now emit it for the deferred stream-native-tool-calls case.

### Fixed
- **More low-priority nits (Part 8 · R21b)** — the agentic tool loop's native path now preserves any prose
  the model emitted alongside its tool calls (`LlmMessage.AssistantToolCalls` carries content) instead of
  dropping it; added a reflection guard test asserting every `LlmRequest` field is either hashed into the
  response-cache key or consciously excluded (catches a future field that would cause silent cache
  collisions); documented that `ISecretAccessPolicy` gates reads only (writes/enumeration are the
  admin/provisioning path — wrap the vault to gate them).
- **Low-priority nits batch (Part 8 · R21, partial)** — `InMemoryJobStore.ListAsync` now uses an ordinal
  `Id.ToString()` tiebreak to match the SQL stores' TEXT ordering (was `Guid.CompareTo`); `OutcomeScorer`'s
  magic `Extra["error"]` key is exposed as `OutcomeScorer.ErrorKey`; the `LlmScorerBase` judge SYSTEM
  preamble is now an overridable `JudgeSystemPrompt` (was a hardcoded English literal); and doc gaps closed
  (`CompleteJsonAsync` retry double-charges/never cache-hits; `ClaudeCommand.Tokenize` is double-quote-only).
  The more involved nits are tracked as a follow-up (R21b).
- **Curated-list ordering parity across backends (Part 8 · R19)** — the Postgres curated `ListAsync` sorted
  `kind` under the DB locale collation while SQLite uses its default BINARY (byte-ordinal), so the two could
  order differently. Postgres now `ORDER BY kind COLLATE "C"` (byte-ordinal) to match. The memory-recall
  ordering (SQLite bm25 relevance vs Postgres/InMemory recency) is an inherent semantic divergence — kept
  documented + asserted-divergent via the R5 backend-specific tests rather than forced to converge.
- **ClaudeCli warns instead of silently dropping `LlmRequest.Tools` (Part 8 · R14)** — the CLI provider
  doesn't consume request-level tool declarations (`SupportsToolCalls`=false; tool-calling goes through the
  MCP provisioner), so a caller that put tools on the request + routed to `claude-cli` had them dropped with
  no diagnostic. It now logs a warning naming the count + the correct path (the ClaudeCli.Mcp provisioner).
- **Envelope vault zeroizes the unwrapped DEK (Part 8 · R13, crypto)** — `EnvelopeSecretVault` unwrapped the
  master DEK and handed it to `AesGcmSecretProtector` (which clones it) but never scrubbed its own copy, so
  the plaintext key lingered on the managed heap until GC (the transient recovery KEK was already zeroed —
  this closes the same window for the longer-lived DEK). It now `CryptographicOperations.ZeroMemory`s the
  DEK at the single `BuildInner` choke point every unwrap path funnels through.
- **Verdict classifier: extensible + reaches `ContextWindowExceeded` on typed exceptions (Part 8 · R8)** —
  `LlmVerdictClassifier.FromException` now scans the full inner-exception chain, so a typed provider
  exception (e.g. an MEAI "prompt too long") that wraps the real detail in an inner exception classifies as
  `ContextWindowExceeded` (was flattened to `Failed`, defeating the big-context fallback). Added a
  consumer-extensibility seam `AddErrorTextMatcher(Func<string, LlmVerdict?>)` (returns a disposable
  registration) consulted before the built-in English patterns — so an app can teach the classifier a
  non-English provider's phrasing or a bespoke error code without editing Core.
- **SQLite memory dedup is now atomic (Part 8 · R6)** — `SqliteMemoryStore.RememberAsync` did
  UPDATE-then-INSERT with no unique constraint, so two concurrent `RememberAsync` of the same
  `(task, scope, content)` could both fall through the UPDATE and INSERT duplicate rows. Added a
  `UNIQUE(task_key, scope, content)` index (`ux_lyntai_memory_dedup`, replacing the non-unique
  `(task_key, scope)` index whose prefix it subsumes) and switched to `INSERT … ON CONFLICT DO UPDATE` —
  matching `PostgresMemoryStore`'s atomic upsert. The AFTER UPDATE trigger keeps the FTS index in sync.
- **Response-gate `Replace` redacts the whole reply (Part 8 · R3, security)** — the output gate scans a
  reply's `Text` + `Detail` + `ToolCalls`, but a `Replace` outcome only rewrote `Text`, leaving denied
  content in `ToolCalls`/`Detail` to pass through un-redacted (`GuardedLlmClient` and the rail's re-threading
  to later guards). A response `Replace` now also clears `ToolCalls` and `Detail` — the replacement text is
  the whole sanitized reply.
- **Guards now cover the agent tool loop (Part 8 · R2, security)** — `ToolLoop` gated nothing: only the
  chat orchestrator's initial user message + final answer passed the rail, so with `UseTools` on, a denied
  term in a model-emitted tool call's `ArgumentsJson`, or an exfil through a tool observation, bypassed the
  jail. `IGuardRail` gains `InspectToolCallAsync` / `InspectToolResultAsync` (default methods that reuse the
  existing request/response guards — no new per-guard surface), and `ToolLoop` gates each tool call's args
  (before execute) + observation (before feeding it back). A guard Block aborts the loop with a `Refused`
  verdict; a Replace rewrites the args / redacts the observation. Wired via DI; no guards registered → no-op.
- **BYO storage impl now actually wins (Part 8 · R1)** — the SQLite and Postgres storage packages registered
  every domain store with plain `AddSingleton`, so an app that registered its OWN impl BEFORE
  `UseSqliteStorage`/`UsePostgresStorage` was silently clobbered — contradicting the README's "anything you
  register wins (defaults use `TryAdd`)". The domain-store registrations now use `TryAddSingleton` (matching
  `Lyntai.Storage.InMemory`), so a pre-registered app impl wins regardless of call order — the BYO-backend
  escape hatch the app-owned-storage design (P3) relies on.

## 0.28.5 — 2026-07-19

Part 6 — the agentic **self-driving-agent session** primitive (0.28.2–0.28.4 were the cortex/scoring
adoption tail + patch re-releases; consolidated here).

### Added
- **Agentic self-driving-agent session** — a generic primitive for gating an agent that drives its OWN
  tool loop out-of-process (the `claude` CLI now; a future Codex/Gemini-CLI/OpenAI-Responses adapter
  reuses the surface unchanged), distinct from `IToolLoop` (where Lyntai drives the loop). Neutral surface
  in Core `Lyntai.Agents`: `IAgentSession.StreamAsync` yielding an `AgentStreamEvent` transcript
  (`SessionStarted`/`TextDelta`/`Thinking`/`ToolCall`/`ToolResult`/`UsageLive`/`UsageFinal`/`SessionEnded`),
  an `AgentToolPolicy` (ReadOnly plan gate vs Write execute gate), an opaque `ResumeToken` (resume across a
  human gate), and `AgentSessionOptions`/`AgentSessionResult`. **Both consumption doors:**
  `StreamAsync` (live transcript) and the `RunAsync(onEvent)` extension (folds to `AgentSessionResult`),
  mirroring `ILlmProvider.StreamAsync`/`CompleteAsync`. The `claude` adapter
  (`Lyntai.Providers.ClaudeCli`): `ClaudeAgentSession` + `ClaudeAgentOptions` (`--settings` scope-guard
  hooks, `--mcp-config`/`--allowedTools` for an app-hosted MCP server, read-only/write tool policy,
  `--resume`), `ClaudeAgentArgs`, `ClaudeToolCalls.FilePathOf`, and `AddClaudeCliAgentSession()`. Prompt
  over stdin only; diagnosable termination (a no-output run is never silent). Design:
  `local/superpowers/specs/2026-07-19-agent-session-design.md`.
- **`LyntaiOptions.ResolveTimeout(int?)`** — a per-call-seconds timeout overload (value clamped to
  `MaxProviderTimeout`, else the global `ProviderTimeout`), shared by the request path and the agent
  session.
- **`LlmScorerBase` applicability skip hook** (0.28.3) — a scorer can opt out of a given context.

## 0.28.1 — 2026-07-18

Consumer-driven adoption gaps — makes Lyntai's **cortex + scoring** genuinely adoptable (a real app can
retire its own scoring framework + model tuning with no regression) and adds a **per-request timeout**.
Additive only (new overloads / opt-in / default-interface members) — no breaking change.

### Added
- **Per-request timeout override** — `LlmRequest.TimeoutSeconds` (+ a per-consumer
  `LyntaiOptions.TimeoutByConsumer` map) let one long call — e.g. a CLI-agent run driving many steps —
  carry a bigger budget without inflating every short call. `LyntaiOptions.ResolveTimeout` (request >
  consumer-map > "default" > global `ProviderTimeout`), clamped to `MaxProviderTimeout`
  (env `LYNTAI_MAX_TIMEOUT_SECONDS`); honored by all four providers.
- **Score store: upsert + aggregate + export** (`IScoreStore`) — `SaveAsync` now UPSERTs on
  `(session, scorer)` (re-scoring replaces, not accumulates; new `UNIQUE` merged into the score
  migration), plus `AggregateAsync` (per-scorer AVG+COUNT → `ScorerAggregate`) and `ExportAsync`
  (flat `(session, scorer, score)` dump → `ScoreExportRow`) for the eval dashboard + tuning datasets.
- **Dry-run scoring** — `IScoringService.EvaluateAsync(ctx, persist: false)` scores without writing rows
  even when a store is wired (a preview/tuning path).
- **Per-scorer judge model** — `LlmScorerBase` exposes overridable `Model` + `Consumer`, so a cheap judge
  can route to a cheap model per scorer (was hardcoded to the default + `"scoring"`).
- **Applicability skip on `LlmScorerBase`** — a `protected virtual bool Applies(ScoreContext)` (default
  true) checked before the judge call, so a conditional judge (e.g. "faithfulness" applies to a plan, not a
  code-edit turn) returns null WITHOUT spending tokens instead of scoring every context.
- **Live per-consumer model routing** — opt-in `AddLiveModelRouting()` registers an `IModelRoutingStore`
  (KV-backed) the router + cache read each call, so an admin model retune takes effect WITHOUT a restart
  (`lyntai.model.<consumer>`; precedence: explicit → live → configured default). `ResolveModel` gains a
  `liveOverride` overload.
- **Prompt-override validation** — `IPromptRegistry.ValidateOverride(default, candidate)` returns the
  `{placeholders}` a candidate would drop (empty = valid), so an admin save-flow rejects a bad override up
  front instead of relying on `RenderAsync`'s silent runtime fall-back.
- **`IScorer.Description`** (optional default-interface member) for an admin "list scorers" view; documented
  the `ScoreContext.Extra` domain-dimension pattern.

## 0.28.0 — 2026-07-18

Adds a **portable, recoverable secret vault** and **job admission control / pause**, and lands the
review-follow-up backlog (a second multi-agent code review). New package **`Lyntai.Secrets.Dpapi`**;
public-surface additions to `Lyntai.Core` (envelope vault, `IJobAdmissionController`, `JobStatus.Paused`,
live job progress, curated memory) and the three storage backends (`PauseAsync`/`ResumeAsync`, progress
reporting, `ICuratedMemoryStore`) — see below.

### Added
- **DEK-envelope secret vault** (`Lyntai.Core`) — a Lyntai-managed data-encryption key instead of a BYO
  key. `SecretKeyEnvelope` generates a random 256-bit DEK that all secrets are AES-256-GCM encrypted
  under, and wraps the DEK **two ways**: a *machine wrap* (sealed by an injected `ISecretProtector` — the
  fast path, no passphrase on the same host) and a *recovery wrap* (a KEK derived PBKDF2-SHA256 from a
  one-time recovery key — the portability path). `EnvelopeSecretVault` drives the lifecycle:
  `GenerateMasterKeyAsync()` (once, returns the recovery key to record out-of-band), auto-init via the
  machine wrap, and `RecoverAsync(recoveryKey)` on a new host (re-binds the DEK, so later reads take the
  fast path). A machine that can't unseal the machine wrap throws `SecretRecoveryRequiredException` until
  recovered. Wire with `builder.AddEnvelopeSecretVault(machineProtector)`.
- **`Lyntai.Secrets.Dpapi`** (new package) — `DpapiSecretProtector` (Windows DPAPI via
  `System.Security.Cryptography.ProtectedData`, user- or machine-scoped, optional entropy) and
  `builder.AddDpapiSecretVault(...)`, which wires the envelope vault with DPAPI as the machine-binding
  protector: secrets sealed to this Windows host at rest, recoverable off-machine via the recovery key.
  Windows-only at runtime (guarded with a clear `PlatformNotSupportedException`); the envelope crypto
  stays portable in Core, so non-Windows hosts use an AES-GCM protector via `AddEnvelopeSecretVault`.
- **Job admission control + `Paused` state** — `IJobAdmissionController` (default admit-all) is consulted
  by the runner per lane *before* it claims, so an app can throttle lanes by external signals (GPU/CPU
  load, a maintenance window) without Lyntai knowing about them; a held lane's jobs stay Pending (a throw
  is treated as "hold"). Register with `AddJobAdmissionController`. Separately, `JobStatus.Paused` with
  `IJobQueue`/`IJobStore` `PauseAsync`/`ResumeAsync` administratively holds a single Pending job out of the
  claimable set (no schema change — status is TEXT) across all three backends.
- **Live job progress + step reporting** — `JobContext.ReportProgressAsync(done, total, stage)` and
  `ReportStepAsync(message)` let a handler surface live status a UI can read WHILE the job runs (new
  `JobRecord.Progress`/`Total`/`Stage`/`StepLog` + `IJobStore.ReportProgressAsync`/`ReportStepAsync`,
  fenced by the worker id, not lease renewals). The step log is a capped JSON array (`JobStepLog.Parse`/
  `Append` → `JobStep`); `JobContext` also exposes the prior snapshot (`Progress`/`Steps`/…) so a resumed
  handler sees what it already reported. New `lyntai_job` columns folded into the jobs migration
  (pre-release; SQLite + Postgres). InMemory mirrors it.
- **Per-request refusal pattern** — `LlmRequest.RefusalPattern` (a case-insensitive regex) surfaces an
  otherwise-`Ok` reply whose text matches as `Refused` (no fallback) — a caller-supplied check (e.g. a
  per-language "I can't help") on top of the central patterns. Applied by `RefusalScreeningLlmClient`, the
  always-on OUTERMOST front-door layer, so a cached hit is re-screened too; malformed patterns fail open.
- **Curated memory catalog** — `ICuratedMemoryStore` (across InMemory/SQLite/Postgres): a hand-managed
  catalog of `CuratedMemory` entries grouped by `Kind`, each individually enable/disable-able (`Enabled`)
  and editable (`UpdateAsync` with COALESCE semantics) with a `Source` note — distinct from the automatic,
  bounded, dedup/TTL remember/recall *log* (`IMemoryStore`). `CuratedMemorySections.Compose` renders the
  enabled entries into per-kind prompt sections. New `lyntai_curated_memory` table (migration
  `202607180003`, SQLite + Postgres).

### Documented (Sonora-adoption recipes)
- The **"rate-limit → surface"** recipe for single-provider adopters
  (`ConfigureRouting(p => p.On(RateLimited, Surface))` + the `ExemptSoleCandidate` note) — knowledge doc +
  README.

### Fixed
- **Security — denylist guard bypassed via tool calls/attachments** — `DenylistGuard` scanned only message
  text, so a jailed term in a tool-call name/arguments or an attachment URI slipped through. It now scans
  tool-call segments and attachment URIs on the request, and `reply.ToolCalls` on the response.
- **Durable-job poison-pill unbounded on crash-before-run** — a job whose attempts already exceeded
  `MaxAttempts` (e.g. repeatedly claimed then crashed) is now dead-lettered at claim time instead of
  running again.
- **Response cache collided across models** — `ResponseCacheKey` now folds the *effective* (resolved)
  model, so the same request routed to different models no longer serves a cross-model cached reply.
- **Usage-tracker consumer key was case-insensitive** — `InMemoryUsageTracker` now keys consumers
  ordinally (case-sensitive), matching the SQL trackers, so `"App"` and `"app"` bill separately.
- **Semantic recall now fails open** — if the vector backend throws mid-recall, `SemanticMemory.RecallAsync`
  logs and returns no hits (caller cancellation still propagates) rather than failing the whole request.
- **Router misclassified a provider's own cancellation** — a provider that throws
  `OperationCanceledException` for its *own* reasons mid-stream now falls over to the next candidate
  instead of aborting the request; only the caller's cancellation still propagates.
- **In-memory job-claim tiebreaker diverged from SQL** — `InMemoryJobStore` breaks a same-priority,
  same-`available_at` tie by the id string (ordinal), matching the SQL stores' `ORDER BY …, id`.

### Documented
- The soft budget cap's **concurrency overshoot bound** (in-flight calls, not "one past"), the job
  scheduler's **at-least-once** enqueue-then-advance window, the required **constant-time compare** for
  secret/token equality in an `ISecretAccessPolicy`, and the cross-backend memory-recall divergence.
  Corrected the 0.27.1 dimension-mismatch note (in-memory scores 0 since 0.27.2, only Postgres throws).

### Hardening (round-2 review of the new surface)
- **Concurrent step-log reports could lose a step on the SQL job stores** — `ReportStepAsync` is a
  read-modify-write on `step_log`; two concurrent reports from one handler could interleave (InMemory was
  already safe under its lock). Serialized with a per-store lock on both SQL backends.
- **Secret-envelope KDF downgrade / non-crypto exception** — the recovery PBKDF2 iteration count was honored
  from the (possibly tampered) envelope unbounded, and `0` threw `ArgumentOutOfRangeException`. Added a hard
  `MinRecoveryIterations = 100_000` floor enforced at load and at the KDF, as a `CryptographicException`.
- **Envelope `version` was written but never enforced** — a future format opened by this build now throws
  instead of silently misparsing; the transient recovery KEK is zeroed after use.
- **Config + polish** — `JobOptions.MaxStepLog` (env `LYNTAI_JOBS_MAX_STEP_LOG`) makes the step-log cap
  configurable; `CompleteJsonAsync` reuses `JsonExtract.IsValid`; documented that `ICuratedMemoryStore.ListAsync`
  kind-ordering isn't ordinal-stable across backends (the composed prompt re-sorts, so it's stable).

### Refactor (behavior-preserving)
- Consolidated the duplicated "extract a JSON object from an LLM reply, then parse it" scaffolding into
  `JsonExtract.TryParseObject`/`IsValid`; split the ~100-line `AddLyntai` composition root into one focused
  helper per feature area. No behavior change (only the two new `JsonExtract` methods touch the surface).

## 0.27.2 — 2026-07-18

Follow-up hardening from a full-codebase multi-agent code review (45 agents; 35 candidates, 19 refuted by
the review's own verifier). No public API change.

### Fixed
- **Streamed native tool call misclassified as a host failure** — a `StreamAsync` response that was a tool
  call (no text) ended as `Failed` in both the OpenAI-compatible provider (`finish_reason=tool_calls`) and
  the MEAI bridge (`FunctionCallContent`), which cooled down a perfectly healthy host. It now surfaces
  `Refused` (no fallback/cooldown) pointing the caller at `CompleteAsync`. (Full streaming tool-call
  *delivery* stays deferred — the `LlmChunk` streaming contract carries no tool-call payload.)
- **Secret vault: a corrupt/truncated at-rest blob now fails as `CryptographicException`** — `Unprotect`
  used to leak a `FormatException`/`ArgumentOutOfRangeException` from base64 parsing or span slicing;
  callers can now catch one exception type for all at-rest corruption (base64, too-short, or tampered).

### Changed
- **`InMemoryVectorStore` tolerates a dimension mismatch (scores it 0) instead of throwing** — so
  `IVectorStore` behaves consistently across the in-memory / SQLite backends (a stray wrong-dimension row,
  e.g. from a prior embedding model, ranks last rather than sinking the whole search).
- **`DenylistGuard` scans each message directly** (short-circuiting on the first hit) instead of
  allocating a whole-transcript join per request — cheaper on long tool-loop transcripts.

### Reviewed, kept by design
- Incrementing a job's attempt count on a stale-lease reclaim is deliberate poison-pill protection (a
  crash-looping job is bounded by `MaxAttempts`); long handlers renew the lease by checkpointing. The
  per-job cancel poll, the tool-loop's defensive message snapshot, and a few small duplicated helpers were
  judged not worth the churn/risk.

## 0.27.1 — 2026-07-18

Consolidation / hardening pass over v0.16–v0.27 (a three-way adversarial review). No public API change —
all fixes are internal/behavioral.

### Fixed
- **Rate limiter: a cancelled wait now refunds its reserved permit** — a caller that bailed during
  `await Task.Delay(wait)` used to leave the bucket decremented, so a burst of cancellations throttled
  legitimate callers for slots no request ever used.
- **Postgres vector store: a faulted lazy schema no longer bricks the store** — a transient failure on the
  first `CREATE EXTENSION`/`CREATE TABLE` was cached forever (every later call re-threw). It now retries on
  the next call.
- **Cron: inverted/empty ranges are rejected** — `5-3`, `70/5`, `10-40` (out-of-range) parsed cleanly and
  produced a schedule that silently never fired (slipping past `AddCronSchedule`'s eager validation). They
  now throw `FormatException`.
- **Scheduler: an impossible-but-parseable cron (e.g. Feb 30) is quarantined per-schedule** — its
  `Next()` throw no longer aborts the whole tick (skipping later schedules) or spins every poll.
- **Front-door decorators are idempotent** — calling `AddResponseCache`/`AddUsageBudget`/`AddRateLimit`
  twice no longer stacks a second decorator (two rate limiters sharing the singleton would double-charge
  permits).
- **A pre-registered `ILlmClient` + a decorator now throws at composition** instead of silently dropping
  cache/budget/rate-limit governance.
- Postgres response-cache eviction gained a `cache_key` tiebreaker (deterministic trim, matching SQLite);
  `SemanticMemory`'s task+scope separator is now a plain-ASCII `(char)0x1f` constant (was a raw control
  byte in the source); scheduler caches are `ConcurrentDictionary`.

### Known edge cases (documented, not fixed)
- The **pgvector** store can surface a DB error for a **zero-magnitude or non-finite embedding vector**
  (pgvector's `<=>`/parser reject them), where the brute-force in-memory/SQLite stores return a 0 score.
  Real embedders don't emit these for non-empty text. A **dimension mismatch** (e.g. after changing
  embedding models without reindexing) throws in the Postgres store; the in-memory and SQLite stores
  tolerate it (score 0 — in-memory was changed to tolerate in 0.27.2, see above). Either way, reindex on
  a model change.

## 0.27.0 — 2026-07-18

Running-job cancellation — a job that's currently executing can now be stopped (before, only Pending jobs
cancelled). Cooperative: a cancel request sets a flag the runner polls; it cancels the handler's token, and
a handler that honors the token stops. Across all three backends.

### Added
- **`IJobQueue.CancelAsync(id)`** — the single front-door cancel: a Pending job is cancelled outright, a
  Running one has cancellation *requested*.
- **`IJobStore.RequestCancelAsync`** (flag a Running job) + **`CancelRunningAsync`** (the runner marks it
  Cancelled, fenced by worker); **`JobRecord.CancelRequested`**. The runner links a per-job token, polls the
  store (`Jobs.PollInterval`) for the flag, and on seeing it cancels the handler's token → the handler
  stops → the job becomes Cancelled. A cancel already set on a reclaimed (stale-lease) job is honored
  without re-running it. `Replay` clears the flag.

### Breaking (pre-1.0)
- `JobRecord` gains a trailing optional `CancelRequested`; `IJobStore` gains `RequestCancelAsync` /
  `CancelRunningAsync` (custom implementers must add them). The `cancel_requested` column was folded into
  the Jobs migration (pre-release consolidation) — no new migration.

### Notes
- Cancellation is cooperative — a handler must honor its `CancellationToken` to actually stop. Latency is
  up to one `Jobs.PollInterval`.

## 0.26.0 — 2026-07-18

Cron expressions for job schedules — recurring jobs can now run on a real cron schedule, not just a fixed
interval. Dependency-free (a hand-rolled 5-field parser; no cron NuGet pulled into Core).

### Added
- **`AddCronSchedule(name, lane, type, payload, cron, priority)`** — schedule a job on a cron expression.
  The expression is validated at composition (a bad one throws in `AddLyntai`, not silently at tick time).
- **`CronExpression`** (`Parse` / `Next`) — a 5-field UTC cron: `*`, values, ranges `a-b`, steps `*/n` /
  `a-b/n` / `n/step`, comma lists, day-of-week 0–6 (Sunday=0 or 7), the standard day-of-month/day-of-week
  OR rule, and the macros `@hourly @daily @midnight @weekly @monthly @yearly/@annually`.
- `JobSchedule` now carries `Cron` (alongside `Interval`); the scheduler uses the cron's next occurrence
  when set — missed slots still coalesce (the cron's next-after-now skips them).

### Breaking (pre-1.0)
- `JobSchedule.Interval` is now `TimeSpan?` (was `TimeSpan`) and a trailing `Cron` field was added — set
  exactly one of interval/cron. Source-compatible for the existing interval overloads; the positional
  ctor/deconstruct arity changed. Both are normally built via the builder methods.

## 0.25.0 — 2026-07-18

Recurring job scheduling — the last big v0.14-deferred job feature. Register an interval schedule and the
scheduler enqueues a job every interval, durably.

### Added
- **`AddJobSchedule(name, lane, type, payload, every, priority)`** (and a `JobSchedule` overload) — a
  recurring job. **`IJobScheduler`** drives it: `TickAsync` enqueues the due schedules and advances them;
  `RunAsync` loops on `Jobs.PollInterval`. The app owns the pump (host-free), same as the runner.
- Next-run time is **persisted via the key-value store** (keyed by schedule name), so a restart resumes the
  cadence instead of re-anchoring; with no `IKeyValueStore` wired it falls back to in-memory. No new storage
  domain / migration — it reuses `IKeyValueStore`.
- **Missed slots coalesce** into a single enqueue (a ticker that was down doesn't replay a burst); the first
  run waits one interval (no fire-on-startup); a non-positive interval is skipped, not spun.

### Notes
- Interval-based for now (cron expressions are a future enhancement — they'd need a cron parser). Scheduling
  requires durable jobs (the queue throws without a storage backend).

## 0.24.0 — 2026-07-18

Durable-job priorities + a dead-letter queue — two of the deferred v0.14 job features. Across all three
backends (InMemory / SQLite / Postgres), pinned by the shared store contract.

### Added
- **Priorities** — `JobSpec.Priority` (and `IJobQueue.EnqueueAsync(lane, type, payload, priority)`). The
  claim now picks by `priority DESC, available_at, id` — higher runs first within a lane. The claim index
  is recreated to lead with priority (migration `M202607180003`).
- **Dead-letter queue** — exhausted transient retries now go to a new terminal `JobStatus.Dead` (instead of
  a silent `Failed`), which is **inspectable and replayable**: `IJobStore.DeadLetterAsync` /
  `ReplayAsync`, surfaced on the front door as `IJobQueue.ListDeadAsync` / `ReplayAsync`. `Replay` requeues
  a Dead (or Failed) job — Pending, attempts reset, error cleared, available now. The runner dead-letters
  on exhaustion (telemetry outcome `dead`, Error span status); an explicit `JobOutcome.Fail` still → `Failed`.

### Breaking (pre-1.0)
- `JobStatus` gains `Dead`; retries-exhausted jobs are now `Dead`, not `Failed` (an explicit `Fail`
  outcome and the no-handler path stay `Failed`). `JobSpec`/`JobRecord` gain a trailing optional
  `Priority` (source-compatible; positional deconstruct/ctor arity changed). `IJobStore` gains
  `DeadLetterAsync`/`ReplayAsync` (custom implementers must add them).

## 0.23.0 — 2026-07-18

Postgres backends for the governance + semantic-memory seams, mirroring v0.22's SQLite ones — and the
vector store uses **pgvector** so similarity search runs in the database, not brute-force in the app.

### Added (`Lyntai.Storage.Postgres`)
- **`UsePostgresResponseCache()`** — `PostgresResponseCache` (`IResponseCache`): reply JSON + `timestamptz`
  expiry, eviction on write. Persistent and shareable across processes on the same db.
- **`UsePostgresUsageTracking()`** — `PostgresUsageTracker` (`IUsageTracker`): per-consumer rows,
  incremented in place; global total is a `SUM`.
- **`UsePostgresVectorStore()`** — `PostgresVectorStore` (`IVectorStore`) over **pgvector**: the cosine
  `<=>` operator + SQL `ORDER BY … LIMIT k` do the top-k in the database (only the k nearest rows come
  back, vs. loading a whole collection into the app). `ISemanticMemory` persists unchanged.
- Migration `M202607180002_Governance` adds `lyntai_response_cache` / `lyntai_usage`.

### Notes
- **`UsePostgresStorage` does NOT require pgvector.** The vector store creates its `vector` extension +
  `lyntai_vector` table LAZILY on first use (needs rights to `CREATE EXTENSION vector`, or a DBA enabling
  it once) — so only `UsePostgresVectorStore` pulls in pgvector, not the whole storage layer.
- The pgvector column is an unbounded `vector` (dimension-agnostic) and unindexed — the search is exact (a
  sequential scan with pgvector's operator, SQL-side top-k). An ANN index (hnsw/ivfflat, needs a fixed
  embedding dimension) is a future enhancement.
- The Postgres test container image is now `pgvector/pgvector:pg16` (a superset of postgres:16) so the
  vector store's live tests run; all other Postgres tests are unchanged. Tests skip without Docker.

## 0.22.0 — 2026-07-18

Persistent SQLite backends for the governance + semantic-memory seams. The cache, usage tracker, and
vector store shipped with in-memory defaults (in Core); this backs them with SQLite so they survive a
restart — all behind the same interfaces, opt-in, no change to the decorators or `ISemanticMemory`.

### Added (`Lyntai.Storage.Sqlite`)
- **`UseSqliteResponseCache()`** — `SqliteResponseCache` (`IResponseCache`): reply JSON + expiry, eviction
  on write (prune expired, then trim oldest beyond `MaxEntries`). The cache survives restarts.
- **`UseSqliteUsageTracking()`** — `SqliteUsageTracker` (`IUsageTracker`): one row per consumer,
  incremented in place; the global total is a `SUM` across rows — so a usage budget isn't reset every
  deploy.
- **`UseSqliteVectorStore()`** — `SqliteVectorStore` (`IVectorStore`): persistent semantic-memory vectors
  (JSON float arrays), brute-force exact cosine loaded per collection. Plug it in and `ISemanticMemory`
  persists unchanged.
- Migration `M202607180002_Governance` adds `lyntai_response_cache` / `lyntai_usage` / `lyntai_vector`.

### Notes
- These `AddSingleton` over the Core in-memory `TryAdd` defaults (win regardless of call order). Each needs
  the connection factory + schema from `UseSqliteStorage`, so call that first.
- The SQLite vector store is brute-force (not indexed) — persistent and fine to some thousands of vectors
  per collection; a dedicated vector backend (pgvector) is the path for larger corpora. Rate limiting stays
  in-memory by design (a shared limiter is a distributed-cache concern, not SQLite) — its `IRateLimiter`
  seam remains the extension point.

## 0.21.0 — 2026-07-18

Client-side rate limiting — the third front-door governance decorator, completing the trio with response
caching (cost/latency) and usage budgeting (spend): cache · budget · **rate limit** (throughput). All
compose on the same ordered decorator chain.

### Added
- **`AddRateLimit([configure])`** — throttles front-door calls with a token bucket. Over the rate a call
  waits up to `MaxWait`, then is refused (a `RateLimited` reply / an Error stream chunk) without hitting a
  provider. Global rate via `RateLimitOptions` (`PermitsPerSecond` / `Burst` / `MaxWait`) with optional
  per-consumer rates (`ConsumerRate`); also `LYNTAI_RATELIMIT_PERMITS_PER_SECOND` / `_BURST` /
  `_MAX_WAIT_SECONDS`.
- **`IRateLimiter`** (the seam) with the built-in **`TokenBucketRateLimiter`** (continuous refill,
  reservation-based waits, injectable clock). Register your own before `AddRateLimit` for a
  distributed/shared limiter.
- A `lyntai.ratelimit.refusals` counter (tagged by consumer) on the `Lyntai.Agents` meter.

### Composition
- Fold order is now **cache (outer) → budget → rate-limit (inner) → client**, so a **cached hit spends
  nothing** — no budget accounting and no rate-limit permit; the rate limiter throttles only real provider
  calls. Order is deterministic regardless of the order the decorators were added.

## 0.20.0 — 2026-07-18

Semantic memory is now wired into the chat path — the composer and orchestrator use it automatically when
embeddings are registered, closing the "opt-in only" gap from v0.19.

### Changed
- **Hybrid memory recall in `MemoryPromptComposer`** — when an `ISemanticMemory` is present (embeddings
  registered), the composer leads the "Learned facts" section with meaning-based hits, then fills in
  lexical `IMemoryStore` entries, deduped by content and bounded by the same char budget. Fail-open across
  both sources: an outage in either yields whatever the other returned. Lexical-only behavior is unchanged
  when no embedder is wired.
- **`ChatOrchestrator` dual-writes memory** — a remembered exchange is written to both the lexical store
  and semantic memory (when wired), so the next turn's hybrid recall can find it by meaning. Both writes
  are fail-open.
- **Semantic memory is registered only when an embedder is** (`AddEmbeddings`) — absent one, `ISemanticMemory`
  isn't in the container, so the composer/orchestrator resolve null and skip it cleanly (no per-turn throws).

### Breaking (pre-1.0)
- `MemoryPromptComposer` and `ChatOrchestrator` constructors take an added optional `ISemanticMemory?`
  parameter (source-compatible for named/DI use; binary signature changed). Both are normally DI-resolved.

## 0.19.0 — 2026-07-18

Semantic memory — meaning-based recall to complement the lexical memory store. Facts are remembered by
their embedding and recalled by cosine similarity to a query, so retrieval finds relevant memories even
without keyword overlap. Consistent with Lyntai's shape: the app brings the embedding model, Lyntai owns
the recall machinery, and the vector backend is a swappable seam.

### Added
- **`IEmbedder`** (`Lyntai.Embeddings`) — the app-provided embedding model (BYO: an OpenAI/Ollama
  embeddings endpoint, a local model, …), a batch `EmbedAsync` primitive + a single-text convenience.
  Registered with **`builder.AddEmbeddings(...)`**.
- **`ISemanticMemory`** (`Lyntai.Memory`) — `RememberAsync` / `RecallAsync(…, k, minScore)` / `ForgetAsync`,
  scoped by (taskKey, scope) like the lexical store; re-remembering identical content dedups. Auto-wired
  when an embedder is registered; a call throws a clear error if none is.
- **`IVectorStore`** (the vector-persistence seam) with the built-in brute-force **`InMemoryVectorStore`**
  (exact cosine, zero-dependency). Register your own before `AddLyntai` to back recall with pgvector /
  sqlite-vec / a vector DB — the recall logic is unchanged.

### Notes
- The in-memory vector store is exact (brute-force) — fine for up to some thousands of entries per scope;
  for larger corpora or persistence across restarts, plug in a real vector backend via `IVectorStore`.
- First cut: no per-entry TTL on semantic memory (the lexical store keeps that); `ForgetAsync` clears a
  whole scope. Composer/orchestrator integration stays opt-in (call `ISemanticMemory` directly) for now.

## 0.18.0 — 2026-07-18

Usage budgeting — cost/token governance on the front door, the natural companion to the response cache.
Meters spend and refuses further calls once a cap is reached. Same shape as caching: a decorator over the
single `ILlmClient` front door with a swappable accounting seam.

### Added
- **`AddUsageBudget([configure])`** — registers the built-in `InMemoryUsageTracker` and decorates the
  front door with a `BudgetedLlmClient` that records each call's usage and refuses (a `Refused` reply / an
  Error stream chunk, **without** hitting a provider) once the applicable total reaches a cap. Global caps
  via `BudgetOptions` (`MaxCostUsd` / `MaxTokens`) with optional per-consumer overrides
  (`ConsumerBudget`); also `LYNTAI_BUDGET_MAX_COST_USD` / `LYNTAI_BUDGET_MAX_TOKENS`.
- **`IUsageTracker`** (the seam) — accumulates token/cost totals per consumer + globally; query spend
  (`Total`) or reset at a billing-window boundary (`Reset`) at runtime. Register your own before
  `AddUsageBudget` for persistent/shared accounting. `UsageTotals` is the snapshot record.
- A `lyntai.budget.refusals` counter (tagged by the cap hit) on the `Lyntai.Agents` meter.

### Changed
- **Front-door decorators now compose.** The cache/budget decorators are folded over the base client in a
  deterministic order (cache **outermost**), so enabling both works correctly regardless of the order they
  were added — in particular a **cached hit is free and never counts toward the budget**. (Previously each
  decorator wrapped a fresh base client, so a second one would have clobbered the first.)

### Semantics
- The cap is a **soft ceiling**: the applicable total is checked *before* each call, so the call that
  crosses a cap still runs (its cost isn't known until it returns) and the *next* one is refused.

## 0.17.0 — 2026-07-18

Read-through response caching on the front door — an opt-in decorator that turns identical repeated
completions into a stored hit, cutting cost + latency and making repeated runs deterministic. On-brand:
it wraps the single `ILlmClient` front door, so the whole library (tool loop, orchestrator, scorers,
pairwise judge) reads through it once enabled, and the cache backend is a swappable seam.

### Added
- **`AddResponseCache([configure])`** — enables caching. Registers the built-in
  `InMemoryResponseCache` (size-bounded, per-entry TTL) and decorates the front door with a
  `CachingLlmClient`. Tunable via `CacheOptions` (`Ttl` default 1h, `MaxEntries` default 1000) or
  `LYNTAI_CACHE_TTL_SECONDS` / `LYNTAI_CACHE_MAX_ENTRIES`.
- **`IResponseCache`** (the seam) — register your own before `AddResponseCache` for a persistent or
  shared backend (Redis, a distributed KV); the front door caches through it transparently.
- **`ResponseCacheKey.For(req)`** — a stable, length-framed SHA-256 over the output-determining request
  fields (messages incl. tool calls / tool-result ids / attachments, model, max tokens, temperature, JSON
  schema). Deliberately excludes `Consumer` (a routing/telemetry tag) so two consumers share a hit.
- A `lyntai.cache.requests` counter (result `hit`/`miss`) on the `Lyntai.Agents` meter.

### Semantics
- Cached: only clean `Ok` non-streaming completions. **Never** cached: streaming (delivered live),
  requests carrying native `Tools` (the tool loop is stateful and its tools can side-effect), and non-Ok
  replies (a transient failure must not stick). A non-positive `Ttl` disables storing entirely.

## 0.16.0 — 2026-07-18

Observability for the agentic subsystems. The GenAI telemetry (v0.2) covered the LLM call path; this
extends the same OpenTelemetry-native surface to the tool loop, durable jobs, and guards, so an agent run
shows up end-to-end in one trace/metrics backend alongside the `chat` spans.

### Added
- **Agentic telemetry** — a second source/meter, `Lyntai.Agents` (constants
  `LyntaiDiagnostics.AgentActivitySourceName` / `AgentMeterName`), separate from the `Lyntai.Llm` GenAI
  one because these aren't `gen_ai.*` operations. Subscribe with `AddSource("Lyntai.Agents")` /
  `AddMeter("Lyntai.Agents")`. Emits:
  - `tool_loop` spans (tags: consumer, mode = `none`/`native`/`prompt`, step count; Error status on a
    non-Ok verdict) with a child `execute_tool <name>` span per tool call, plus a
    `lyntai.tool.invocations` counter tagged by tool name + error flag.
  - `run_job <type>` spans (tags: lane, type, id, attempt, outcome; Error status on `failed`/`lost_lease`)
    with a `lyntai.jobs.processed` counter (lane + outcome) and a `lyntai.job.duration` histogram (lane).
  - a `lyntai.guard.decisions` counter (gate `input`/`output`, guard name, result `block`/`replace`).
  Nothing is emitted unless a listener is attached — the overhead without observability wiring is a few
  null/`Enabled` checks, matching the GenAI surface.

### Samples / tests
- **Playground now exercises the tool loop** (registers an inline `echo` tool and runs `IToolLoop` over
  the deterministic stub's new `TOOL_DEMO` protocol path) and **subscribes both telemetry surfaces**
  in-process, printing what fired. The `p1` e2e asserts the loop converges via a tool call and that every
  GenAI + agentic span/metric emitted — so the instrumentation is covered end-to-end, not just in units.

## 0.15.1 — 2026-07-18

Correctness + security fixes from a three-pass adversarial review of the v0.14–v0.15 code (the AES-GCM
crypto was reviewed and confirmed correct — fresh per-call nonce, proper layout, tamper detection).

### Fixed
- **Secret vault index collision** — a secret named `__names__` mapped onto the vault's internal index
  key and could corrupt/poison the name list. The index now lives outside the secret-name namespace.
- **Denylist guard bypass** — it scanned only `user` messages, so a denied term in a system/assistant/
  tool message (e.g. a tool result fed back mid-loop) slipped through. It now scans every message and the
  reply's error `Detail` too.
- **Guard rail Replace didn't compose** — a later guard saw the original text, not an earlier guard's
  rewrite. Replacements are now re-threaded so each guard inspects the current effective text.
- **Guarded client skipped non-Ok replies** — an error reply's `Detail` (stderr/HTTP body, which can echo
  content) bypassed the output gate. Every reply is now gated.
- **Job runner lane starvation** — under a global `MaxConcurrency` smaller than the sum of lane limits,
  the first lane monopolized the cap and others starved. Claiming is now round-robin across lanes (with a
  rotating start), and `ActiveLanes` is ordered deterministically.
- **Vision edge cases** — an attachment with neither inline data nor a URL now throws instead of sending
  an empty image URL; attachments on a non-`user` role are dropped (OpenAI rejects them) rather than sent.
- **Orchestrator re-persisted redacted input** — when the input gate rewrote (redacted) the message, the
  memory write stored the *raw* original, re-injecting it on the next recall. It now stores the redacted
  text.

### Docs
- Clarified the deliberate boundaries: the chat orchestrator's two gates cover the turn's entry + final
  answer (tool-loop intermediate turns aren't individually gated — use `GuardedLlmClient` for that);
  `ISecretAccessPolicy` gates reads only; `MaxConcurrency` bounds the per-pass batch; streaming applies
  only the input gate.

## 0.15.0 — 2026-07-18

The rest of the design §9 platform kit, in one release: guards, two-gate chat orchestration, a secret
vault, and vision/multimodal. All additive, all in `Lyntai.Core`.

### Added
- **Scope-guard / jail hooks** (`Lyntai.Guards`) — `IGuard` inspects an outbound request and/or inbound
  reply and can Allow / Block / Replace; `IGuardRail` runs the registered guards (first-non-Allow wins).
  A `DenylistGuard` jails named terms; `GuardedLlmClient` wraps the front door to gate every completion.
  Register via `builder.AddGuard<T>()`.
- **Two-gate chat orchestration** (`IChatOrchestrator` in `Lyntai.Agents`) — one guarded chat turn:
  **input gate** (guards) → memory recall into the prompt → the model *via the tool loop* → **output gate**
  (guards) → remember the exchange. Composes the guard rail, `IPromptComposer`, the tool loop, and memory;
  fail-open around the two gates. Injectable as a batteries-included entry point.
- **Secret vault + access gate** (`Lyntai.Secrets`) — `ISecretVault` (get/set/delete/list), encrypted at
  rest by an `ISecretProtector` (`AesGcmSecretProtector` = AES-256-GCM with a BYO 32-byte key; tamper-
  detecting), backed by the registered `IKeyValueStore` (persistent) or in-memory. An optional
  `ISecretAccessPolicy` gates reads (denied → `UnauthorizedAccessException`). Wire via
  `builder.AddSecretVault(key, policy)`.
- **Vision / multimodal** — `LlmAttachment` (inline bytes or a URL + MIME type) on `LlmMessage.Attachments`,
  with `LlmMessage.UserWithImage(...)` / `UserWithImageUrl(...)`. The OpenAI-compatible provider renders
  them as `image_url` content parts and the MEAI bridge maps them to `DataContent`/`UriContent`; text-only
  providers ignore them.

## 0.14.0 — 2026-07-18

Durable jobs (design §9): lanes + checkpoint/resume, built for running many agents in parallel with
proper concurrency control. New storage domain across all three backends + a runner. Additive.

### Added
- **Durable job store** (`IJobStore` in `Lyntai.Storage`, over a `lyntai_job` table) — enqueue a job
  (lane, type, JSON payload), a runner claims and runs it, the handler checkpoints, and a job whose
  worker crashed is reclaimed and **resumed from its last checkpoint**. The claim is a single atomic
  statement per backend (`UPDATE … RETURNING` on SQLite under the WAL single-writer; `… FOR UPDATE SKIP
  LOCKED …` on Postgres), so multiple workers coordinate without double-claiming. Writes are fenced by
  worker id (a lost lease is abandoned, not clobbered). Backends: SQLite, Postgres, InMemory (the
  Postgres claim proven under real-container concurrency).
- **Runner + handler seam** (`Lyntai.Jobs`) — `IJobHandler` (app work, keyed by type via
  `builder.AddJobHandler<T>()`; the **at-least-once / idempotent-from-checkpoint** contract is in its
  doc), `JobContext` (payload + checkpoint + `SaveCheckpointAsync`, which renews the lease), `JobOutcome`
  (Complete / Retry(delay) / Fail), `IJobQueue`, `IJobRunner`. Retries with backoff up to max attempts; a
  thrown handler is a transient retry.
- **Parallelism + control logic** — one `IJobRunner.RunOnceAsync` claims a bounded set across *all* lanes
  and runs them **concurrently** (true multi-lane parallelism), governed by per-lane limits
  (`JobOptions.LaneConcurrency`) and a global `MaxConcurrency` cap. Scale further by running several
  runner instances (one process or many) — the atomic claim hands each job to exactly one. **The app owns
  the pump** (`RunAsync` from your own `IHostedService`/loop) — Lyntai starts no background threads, so it
  stays host-free. Tuning on `LyntaiOptions.Jobs` + `LYNTAI_JOBS_*` env.
- Demonstrated end-to-end in the Playground (a checkpointing 2-step job over the same SQLite db).

## 0.13.1 — 2026-07-18

Correctness + resource-lifecycle fixes from an adversarial review of the v0.9–v0.13 tool-calling code
(two independent review passes). One small API refinement (`SupportsToolCalls` now takes the request).

### Fixed
- **Kestrel host / `WebApplication` leaks** — the CLI MCP host (`McpToolHost.StartAsync`) and provisioner
  (`McpCliToolProvisioner`) didn't dispose the started host if a later step threw (port-bind failure,
  temp-file write failure, cancellation). Both now dispose on any failure path.
- **`req.Tools` leaked into the prompt-fallback tool loop** — a caller-supplied `LlmRequest.Tools` was
  sent as native declarations *alongside* the JSON protocol prompt, so a partially-tool-aware model could
  emit a native tool-call turn the prompt path never parses. The prompt path now clears `Tools`.
- **Typed tool arguments were stringified** — the MEAI bridge and CLI tool-host serialized boxed CLR
  primitives (`3`, `true`) as JSON strings (`"3"`, `"true"`), so a tool with an `integer`/`boolean`
  schema got the wrong type. Primitives now keep their JSON type (reflection-free).
- **MCP tool results with only non-text blocks** (image/audio/resource) fed an *empty* observation back
  to the model. `McpToolset.ToText` now describes non-text blocks instead of returning "".
- **Capability-vs-routing mismatch** — `SupportsToolCalls` probed a candidate using its raw model while
  `CompleteAsync` resolved a per-consumer/request model, so under `ProviderAndModel` cooldown scope the
  loop could pick the native path while the router served a different (non-native) candidate, silently
  dropping tools. The probe now takes the `LlmRequest` and resolves the identical model/cooldown key
  (minor API change: `ILlmClient.SupportsToolCalls(req)` / `ILlmRouter.SupportsToolCalls(candidates, req)`).

### Security
- **The CLI tool-host now requires a per-call bearer token.** The localhost MCP endpoint *executes* the
  app's tools; a random token is generated per host, passed to the CLI via the `--mcp-config` headers,
  and required on every request (401 otherwise) — so another local process can't invoke the tools during
  the call window. (Loopback-only binding remains the primary mitigation.)

## 0.13.0 — 2026-07-18

Proper tool-calling for the **claude CLI** provider, plus a test-stability fix. Additive.

### Added
- **`Lyntai.Providers.ClaudeCli.Mcp`** — gives the claude CLI provider real tool-calling. The CLI runs
  its own agent loop and reaches custom tools only over MCP, so this hosts the app's registered
  `ITool`s as an **in-process, localhost-only HTTP MCP server** (Kestrel, ephemeral port, started and
  torn down per CLI call) and points `claude -p` at it via a temp `--mcp-config` + a `--settings`
  allow-list (`mcp__lyntai__*`, so only our tools run, non-interactively). Opt-in:
  `builder.AddClaudeCliProvider().AddTool(...).AddClaudeCliMcpTools()`; a completion routed to the CLI
  then lets its agent call the app's tools and returns the tool-informed answer.
  - A small Core seam (`ICliToolProvisioner` / `CliToolSession` in `Lyntai.Agents`) keeps the
    host/ASP.NET dependency out of the base `ClaudeCli` provider — the provider gains an optional
    provisioner and behaves exactly as before when the add-on isn't registered.
  - Each `ITool` is exposed via an invocable `AIFunction` (its own JSON schema, not delegate-inferred).
    Proven end-to-end by hosting the server and connecting with Lyntai's *own* MCP client (the exact
    thing the CLI does) — no real CLI needed for the core test; a gated `LYNTAI_LIVE_CLI_TOOLS` test
    covers the real binary.
  - **Note:** this is a deliberate, scoped exception to the library's "no server/no host" principle —
    an ephemeral localhost listener that exists only during a CLI call, isolated in this opt-in package.

### Fixed
- **Flaky router cooldown test** — the dead-host cooldown integration test depended on wall-clock timing
  (under a saturated parallel runner, call 1's subprocess spawn could outlast the cooldown before call 2
  ran). Rewritten to use `DeadHostTracker`'s injectable clock — fully deterministic. (No library change.)

## 0.12.0 — 2026-07-18

New package **`Lyntai.Tools.Mcp`** — expose a Model Context Protocol (MCP) server's tools to the tool
loop. Additive; new package only.

### Added
- **`Lyntai.Tools.Mcp`** (references `Lyntai.Core` + `ModelContextProtocol.Core`) — adapts each tool on
  a connected MCP server into a Lyntai `ITool`, so the whole MCP tool ecosystem becomes callable from
  `IToolLoop` (native or prompt path, same as any other tool).
  - `McpToolset.FromClientAsync(mcpClient)` lists the server's tools and wraps each as an `McpTool`
    (`ITool`); `builder.AddMcpTools(tools)` registers them into the tool collection. The **app owns the
    `McpClient`** (transport, connection, lifecycle — BYO, consistent with Lyntai's IoC seams); Lyntai
    only adapts. `McpTool` delegates the call through a `Func` seam so the SDK's concrete client stays
    out of the contract and the adapter is unit-testable.
  - Tool results flatten to the observation string the loop feeds back (text blocks joined, or
    structured content as JSON; `error:`-prefixed when the server flags an error).
  - Proven end-to-end against a real `@modelcontextprotocol/server-everything` over stdio (opt-in
    `McpLiveTests`, gated on `LYNTAI_LIVE_MCP`).

## 0.11.0 — 2026-07-18

Native tool-calling through the **MEAI bridge** — the follow-up deferred from v0.10. Now every
`Microsoft.Extensions.AI` `IChatClient` (OpenAI, Azure, Anthropic API, Ollama-via-MEAI, …) gets native
function-calling too, not just the OpenAI-compatible HTTP provider. Additive.

### Added
- **`ExtensionsAiProvider` bridges tools both directions.** `LlmRequest.Tools` map to declaration-only
  `AIFunctionDeclaration`s on `ChatOptions.Tools` (a `LyntaiToolDeclaration` — Lyntai's tool loop drives
  execution, so no invocable `AIFunction`/`FunctionInvokingChatClient` is used); the model's
  `FunctionCallContent` surfaces on `LlmReply.ToolCalls` (empty-content tool-call turn → `Ok` before the
  empty→Failed branch); tool-call/result turns map to `FunctionCallContent`/`FunctionResultContent`.
  `SupportsToolCalls => true`. Proven end-to-end through the tool loop with a scripted `IChatClient`.
- The bridge stays **trim/AOT-clean** (✅): the tool-argument round-trip uses `System.Text.Json.Nodes`
  (no reflection-based `JsonSerializer`).

## 0.10.0 — 2026-07-18

Native (structured) tool-calling — makes the v0.9 tool loop *actually work* over real provider
function-calling instead of only the prompt-based protocol. Additive; the contract additions keep every
existing `new LlmReply`/`LlmMessage` call site source-compatible.

### Added
- **Native tool-calling round-trip.** The model's tool calls now come back as structured data and tool
  results feed back through the contract:
  - `LlmToolCall(Id, Name, ArgumentsJson)`; `LlmReply.ToolCalls`; `LlmMessage.ToolCalls` +
    `LlmMessage.ToolCallId` with factories `AssistantToolCalls(calls)` / `ToolResult(id, content)`.
  - `ILlmProvider.SupportsToolCalls` (default-interface-method, default false),
    `ILlmClient.SupportsToolCalls` / `ILlmRouter.SupportsToolCalls(candidates)` — the loop asks the
    front door whether native tool-calling is available for the default routing (first live candidate)
    without ever seeing the candidate list.
  - **`OpenAiCompatibleProvider`** parses `tool_calls` from the response into `LlmReply.ToolCalls`
    (handling OpenAI's string arguments *and* Ollama's object arguments; synthesizing an id when Ollama
    omits one) and serializes assistant-tool-call turns + `role:"tool"` result turns in both the OpenAI
    and Ollama payloads. `SupportsToolCalls => true`.
- **`IToolLoop` now prefers native, falls back to prompt.** When the routing supports native tool-calling
  the loop sends tool declarations and acts on structured `ToolCalls` (parallel calls in one turn
  supported); otherwise it uses the v0.9 prompt protocol. Both paths execute the same app-registered
  `ITool`s, and unknown/throwing tools stay recoverable. Proven end-to-end against a real local Ollama
  (opt-in `OllamaToolCallLiveTests`, gated on `LYNTAI_LIVE_OLLAMA`).

### Deferred
- Native tool-calling through the **MEAI bridge** (`ExtensionsAiProvider`) — its `SupportsToolCalls`
  stays `false` for now (an argument dict↔JSON serialization spike + a `MapMessages` rewrite); a
  follow-up. Streaming tool-calls and ClaudeCli/Local native tools remain out of scope (they use the
  prompt fallback).

## 0.9.0 — 2026-07-17

First "platform kit" (design §9) capability: agentic tool-calling. Additive, all in `Lyntai.Core`.

### Added
- **Tool-calling loop** (`Lyntai.Agents`) — a provider-agnostic ReAct-style loop over the `ILlmClient`
  front door. `IToolLoop.RunAsync(req)` renders the registered tools into the prompt, asks the model to
  either call a tool or finish (a small JSON protocol: `{"tool":…,"arguments":…}` / `{"final":…}`),
  executes the chosen tool, feeds the observation back, and repeats until it finishes or the iteration
  budget is hit. Because it runs over the text contract (through `CompleteJsonAsync`), it works with
  **any** provider — CLI, HTTP, MEAI bridge, local — with no native tool-calling support required.
  - **`ITool`** — the executable-tool seam (`Name`/`Description`/`ParametersJsonSchema` mirror the
    existing `LlmTool`, plus `InvokeAsync`), registered into a DI collection via `builder.AddTool<T>()`
    / `AddTool(factory)` (the variation point — a tool is a new class + one registration).
  - **`FunctionTool`** — define a tool inline from a delegate, no class needed.
  - **`IToolRegistry`** — name-keyed (case-insensitive, first-wins) resolution over the tool collection.
  - Robust by construction: an unknown tool or a throwing tool becomes an `error: …` observation fed
    back to the model (it can recover) rather than an exception; a non-Ok LLM verdict (refusal, all
    candidates down) is surfaced as-is; a run that doesn't converge returns `Failed` with a reason.
  - Wired by default in `AddLyntai` (resolves with zero tools — it degenerates to one plain
    completion). Budget via `LyntaiOptions.ToolLoopMaxIterations` (default 8) /
    `LYNTAI_TOOL_LOOP_MAX_ITERATIONS`, or a per-call `RunAsync(req, maxIterations)` override.

## 0.8.0 — 2026-07-17

New provider package for in-process local inference. Additive — no changes to existing packages.

### Added
- **`Lyntai.Providers.Local`** — runs a local GGUF model in-process via LLamaSharp (llama.cpp), wired
  with `builder.AddLocalProvider(modelPath, …)`. No network, no API key, no external process; the
  model loads lazily and is reused, and generations are serialized (one local model, one at a time).
  It classifies to the same verdicts the router expects (produced answer → `Ok`; empty generation or
  a load/inference fault → `Failed` so the router falls over; inactivity → `Timeout`).
  - **Managed-only on purpose:** the package references `LLamaSharp` but *not* a backend, so it isn't
    nailed to one runtime — the consuming app adds the `LLamaSharp.Backend.*` (Cpu/Cuda/Vulkan/Metal)
    that matches its hardware. A missing backend surfaces as a `Failed` verdict on the first call
    (the router then falls over), not a startup crash.
  - Applies each model's own chat template (from its GGUF metadata) so instruct-tuned models get the
    prompt format they were trained on. Opt-in live tests gate on `LYNTAI_LIVE_LLAMA` +
    `LYNTAI_LLAMA_MODEL` (like the Ollama live tests), so the default run stays native-dependency-free.

## 0.7.1 — 2026-07-17

Correctness fixes from a high-effort multi-agent code review of the v0.3–v0.7 code. No API break
(one additive optional ctor param).

### Fixed
- **Critical: BYO HttpClient was disposed every call** — an app-supplied shared client threw
  `ObjectDisposedException` on the second request. Lyntai now disposes only clients it created.
- **Memory cap counted expired entries**, evicting live facts while keeping dead ones — the cap now
  evicts expired entries first (all three backends).
- **Memory dedup ranked by a stale id**, so a re-remembered fact recalled as old — recall and the cap
  now order by `created_at` (the refreshed value), honoring the "refreshes recency" contract.
- **Router recorded a dead-host failure per retry attempt** — `Retry(Failed, 2)` benched a host in one
  request; now one failure per request. Router streaming no longer leaks empty content chunks.
- **Dapper `DateTimeOffset` handler collision** between the SQLite and Postgres backends (process-global
  registry) — both handlers are now identical, so loading both is safe.
- **Postgres dedup race** (concurrent Remembers → duplicates) — now an atomic `INSERT … ON CONFLICT`.
- **`MigratingConnectionFactory` cached a transient migration failure forever** — now retries.
- **`ClaudeCliProvider.IsAvailable` bypassed a BYO `IProcessRunner`** — a custom (sandbox/remote) runner
  is now optimistically available so the router reaches it.

## 0.7.0 — 2026-07-17

Bring-your-own resources — inversion of control for the resource-lifecycle concerns. The app owns the
implementation; Lyntai provides the interface. All additive; the `ClaudeCliProvider` ctor now takes
`IProcessRunner` (source-compatible — `ProcessRunner` implements it).

### Added
- **`IProcessRunner`** — the process-spawning seam (default `ProcessRunner`). Register your own to own
  how the `claude` CLI is spawned (sandbox, custom shell, remote/audited execution).
- **BYO HttpClient** — `AddOpenAiCompatibleProvider` (and the presets) accept an optional
  `Func<IServiceProvider, HttpClient>`, so you supply your configured client (Polly, auth handlers,
  proxy, a named `IHttpClientFactory` client) and own its lifecycle.
- **BYO DB connection + schema** — `UseSqliteStorage`/`UsePostgresStorage` gain an
  `IDbConnectionFactory` overload (you own connection creation/pooling/lifecycle) and a `migrate: false`
  flag (you own the schema; Lyntai runs no migrations).
- **Provider presets** — `AddOpenAiProvider`, `AddOllamaProvider`, `AddOpenRouterProvider`,
  `AddAzureOpenAiProvider` — pre-configured defaults over the generic method. The BYO `ILlmProvider`
  path (`AddProvider`) stays open for anything bespoke.

## 0.6.0 — 2026-07-17

Second heavyweight backend + a real-endpoint provider test — the two items previously blocked on
infrastructure, now that a Postgres-capable Docker and a local Ollama are available. Additive.

### Added
- **`Lyntai.Storage.Postgres`** — a full PostgreSQL backend for every storage domain (Npgsql + Dapper
  + FluentMigrator). `lyntai_`-prefixed; memory recall uses `pg_trgm` (GIN trigram index) for ILIKE
  substring search including CJK substrings; `timestamptz` ↔ `DateTimeOffset`; dedup/TTL/cap/prune;
  `UsePostgresStorage(conn, migrateOnFirstUse)`. Integration-tested against a real container via
  Testcontainers (skips when Docker is unavailable). Proves the domain-interface seam holds for a
  heavyweight server DB — three backends now (SQLite, in-memory, Postgres).
- **Opt-in live Ollama test** — validates the OpenAI-compatible provider (Ollama flavor) against a
  real endpoint (completion with real usage, streaming, through the router). Gated on
  `LYNTAI_LIVE_OLLAMA`; the default run stays fast and dependency-free.

## 0.5.0 — 2026-07-17

Ecosystem & backends (roadmap v0.5) + v1.0 API-freeze groundwork. Additive; no behavioral change.

### Added
- **`Lyntai.Storage.InMemory`** — a zero-dependency in-memory backend for every storage domain
  (KV, conversation, memory with dedup/TTL/cap, score, trace, prompt-version). Useful standalone
  (tests, ephemeral/serverless, no file) and as the second real backend proving the domain-interface
  seam. Wired via `builder.UseInMemoryStorage()`.
- **Composite storage** — the mastra "one interface per domain, many backends" pattern expressed
  through DI: mix backends per domain (SQLite for most, in-memory for one) via a per-domain override
  (last registration wins); `UseInMemoryStorage()` uses `TryAdd` so it stands alone or backfills gaps.
- **Public-API baseline** (`ApiSurfaceTests`) — snapshots every packable assembly's public/protected
  surface against a checked-in baseline so API changes are deliberate (pre-1.0 visible in review;
  post-1.0 gate a major bump).
- **`docs/AOT.md`** — per-package trim/AOT status and the Dapper.AOT path for `Lyntai.Storage.Sqlite`.

## 0.4.0 — 2026-07-17

LLM-ops depth (the roadmap's v0.4). No behavioral change to existing paths; all additive except the
`IMemoryStore` signature (a `ttl` param + `PruneAsync`, pre-1.0).

### Added
- **Versioned prompt overrides** — `IPromptVersionStore` + SQLite impl: an audit trail for
  `lyntai.prompt.*` edits (author, monotonic versions, exactly one active) with history and rollback
  that re-activates an earlier revision without rewriting history. The registry renders the active
  versioned override (winning over the plain KV key), placeholder guard still applied.
- **Judge calibration** — `JudgeAgreement` (exact-agreement rate, mean absolute error, Pearson over
  two aligned score series) and `IPairwiseComparer` / `LlmPairwiseComparer` (which-is-better, with
  position-bias mitigation on by default — runs both orders, ties on disagreement).
- **Memory lifecycle** — dedup (remembering an identical fact refreshes rather than duplicates),
  per-entry TTL (expired entries excluded from every recall path), and `PruneAsync(taskKey?, olderThan?)`.
  `SqliteMemoryStore` takes an injectable clock for deterministic TTL tests.
- **Trace ↔ span bridging** — `RunTrace.TraceId` captures the ambient OpenTelemetry W3C trace id at
  `Begin`, persisted and round-tripped, so a stored run trace cross-references the distributed trace.

### Changed
- `IMemoryStore.RememberAsync` gains an optional `ttl`; adds `PruneAsync` (pre-1.0 interface change).
- Migrations 202607170006 (prompt versions), 202607170007 (memory TTL), 202607170008 (trace id).

## 0.3.0 — 2026-07-17

Routing & resilience depth (the roadmap's v0.3), plus a second independent audit pass (four
adversarial reviewers over the router, providers, storage, and cortex) — findings the 0.2.0 review +
221 tests missed.

### Added
- **Configurable `RoutingPolicy`** (`LyntaiOptions.Routing`, `LyntaiBuilder.ConfigureRouting`,
  `LYNTAI_RETRY_*` / `LYNTAI_COOLDOWN_SCOPE` env). The hard-coded §6 router switch becomes the
  *default* policy — every prior router test passes unchanged. Four routing items land at once:
  - **Per-verdict action** (`FallbackAction`: Advance / PenalizeAndAdvance / CooldownAndAdvance /
    Surface), overridable with `.On(verdict, action)`.
  - **Retry-then-advance** — `.Retry(verdict, n)` retries the *same* candidate on a transient fault
    (Failed/Timeout) before falling over; cooled/surfaced/context-window verdicts never retry the
    same host. Optional `RetryBackoff`. Applies to complete + streaming pre-content.
  - **Per-(provider, model) cooldown granularity** (`CooldownScope`) — a rate-limited model no longer
    benches its siblings on the same host. Default `Provider` (unchanged).
  - **Sole-candidate exemption** (default on, LiteLLM parity) — never bench the only candidate.
- **Deferred migrations** — `UseSqliteStorage(path, migrateOnFirstUse: true)` migrates lazily on the
  first store access (thread-safe) so DI composition does no I/O.
- **`bench/Lyntai.Benchmarks`** (BenchmarkDotNet, `dev.mjs bench`) — router overhead per attempt,
  FTS5 recall latency at 1k/10k/100k rows.

### Fixed (audit pass — no API breaks)
- **Streaming timeout is now an inactivity clock in every provider.** The ExtensionsAi and
  OpenAiCompatible streaming paths still armed a single wall-clock `CancelAfter` over the whole stream
  (0.2.0 fixed only the CLI/ProcessRunner path), so a slow-but-healthy stream or a slow consumer got
  killed. Both now re-arm per read and stop the clock while the consumer works.
- **Router won't commit a stream on an empty content chunk** — the commit gate requires non-empty text,
  so a third-party provider yielding an empty/role-only first chunk can't disable fallback.
- **`LYNTAI_MODEL_<CONSUMER>` env override is implemented** (was documented but silently ignored).
- **OTel cost + cache-read tokens are recorded** on the client span (were dropped despite the 0.2.0
  telemetry claim).
- **`CompleteJsonAsync`'s retry now differs from the first attempt** (feeds back the bad reply + a
  JSON-only instruction) instead of re-sending the identical request.
- **`AddLyntai` throws on a second call** instead of shadowing `LyntaiOptions` + duplicating providers.
- **`LlmVerdictClassifier` no longer treats a bare "unauthorized" as `AuthFailed`** (which cools the
  host) — it needs auth context, mirroring the 429 guard.
- **`MigrationRunnerService`** builds its connection string via `SqliteConnectionStringBuilder` (was raw
  interpolation) and sets WAL + `busy_timeout` before migrating.
- **`ConversationStore.ListThreadsAsync`** gets an `id DESC` tiebreaker (deterministic on `created_at`
  ties).
- **`MemoryPromptComposer`** bounds the appended section by a character budget, not just entry count.
- `ILlmRouter` XML doc corrected to the amended fallback semantics.

## 0.2.0 — 2026-07-17

Production-hardening release: everything surfaced by the multi-agent code review and the
2025–2026 best-practices research pass, plus the provider-shaped consumer API.

### Added
- **`ILlmClient`** — the front door: to a consuming app, Lyntai behaves like ONE LLM provider
  (complete/stream over a request; candidates, fallback, and cooldowns stay internal).
- **`AsChatClient()`** — the reverse MEAI bridge: consume a whole Lyntai composition as a
  `Microsoft.Extensions.AI.IChatClient`.
- **`CompleteJsonAsync`** — structured output per design §6: schema-constrained call, tolerant JSON
  extraction from prose/fences, one retry, else `Failed`. `LlmScorerBase` now builds on it.
- **`LlmVerdictClassifier`** — the one shared failure classifier (typed HTTP status first, then
  conservative text heuristics); replaces three drifting per-adapter copies.
- **`LlmVerdict.ContextWindowExceeded`** (advance without penalizing the host — the remedy is a
  larger-context candidate) and **`LlmVerdict.AuthFailed`** (immediate host cooldown + advance).
- **OpenTelemetry GenAI telemetry** — `ActivitySource`/`Meter` "Lyntai.Llm": `chat {model}` client
  spans with `gen_ai.*` attributes, `gen_ai.client.operation.duration`, `gen_ai.client.token.usage`,
  and `gen_ai.client.operation.time_to_first_chunk` (the streaming fallback point of no return).
- Packaging: XML docs, snupkg symbols, embedded sources, package tags, trim/AOT analyzers
  (`IsAotCompatible` everywhere except `Lyntai.Storage.Sqlite`, which opts out honestly over Dapper).
- Playground/e2e exercise streaming end-to-end.

### Changed — breaking
- **`RateLimited` semantics (design §6 amendment):** was circuit-break-hard-stop; now cools the
  host immediately (`DeadHostTracker.MarkDead`) and advances to the next candidate — a 429 is
  terminal for the host's window, not for the fleet. Surfaced only when every candidate is exhausted.
- `LlmScorerBase`/`RelevancyScorer` constructors take `ILlmClient` (was `ILlmRouter` + options).
- SQLite objects are prefixed `lyntai_` (tables, FTS, triggers, indexes, and the FluentMigrator
  version table) so `UseSqliteStorage` can safely target an existing application database.

### Fixed
- `ProcessRunner`: stdin written before the timeout was armed (unbounded hang); streaming timeout
  counted consumer dwell time (healthy streams killed); abandoned enumerators orphaned live CLI
  processes (now killed via try/finally).
- Content-filter verdicts: HTTP-200-with-empty-content and streamed `content_filter` both surfaced
  as `Refused` (never retried, never fallen back) instead of `Failed`/silent-`Final`.
- Zero-content HTTP/MEAI streams now yield `Error(Failed)` so the router can fall over, matching
  the non-streaming path and the CLI provider.
- Claude CLI: content without a terminal result event ends `Final`, not a spurious error; spawns
  from a neutral cwd (no host-project CLAUDE.md/hooks loaded into library calls).
- `http://localhost:11434/v1` (Ollama's OpenAI-compatible surface) detects the OpenAI flavor.
- SQLite `CommandTimeout` set deliberately (the driver's busy-retry loop is independent of
  `PRAGMA busy_timeout`).

## 0.1.0 — 2026-07-17

Initial implementation: the full `TASKS.md` sequence (phases 0–7). Core abstractions + fallback
router, SQLite storage with FTS5-trigram memory, claude-CLI / OpenAI-compatible / MEAI providers,
cortex layer (prompt registry, scorers incl. LLM judge, traces, memory composition), Playground,
devtools e2e harness, NuGet packaging.
