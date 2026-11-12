# The agent-provider layer against Daoris's accepted provider design — a study

**Status: a study (`docs/documents.md`), 2026-10-29; every row it files is built and archived: AG10 to AG20; §10 answers a later request, A-LQ3 to A-LQ17, by rows AG25 to AG45 (A-LQ10 is declined and has none); §11 designs the run kept across turns (AG39, **D251**) and files AG46 to AG48.** It reads Lyntai's layer
(`docs/agent-provider-layer.md`, **D219**, rows AG1 to AG9 closed in `docs/task-archive.md`) against two designs
Daoris has since accepted, and files what a host that runs coding agents still needs from the layer as rows AG10 to
AG20 in `TASKS.md` (all closed in `docs/task-archive.md` since) and **D233**. AG20 was ruled and built as **D234**, and §9 states how a host
consumes the layer before a release (**D235**). Its reasoning is read here; the other rows are built in later
sessions. A type named below is a shipped one unless the row that proposes it says otherwise, and the `ApiSurface`
baselines hold what shipped.

## 1. What was read, and how far it can be trusted

| Input | Standing there | Read for |
|---|---|---|
| Daoris `2026-10-26-agent-provider-design.md` | accepted 2026-10-26 after four read rounds; its §11 rows are built in order, **row a on a branch and not yet on its main line**, none of the rest | the contract (§2), the readings (§3), what each part answers (§4), the rows (§11), OPENROUTER1 (§12) |
| Daoris `2026-10-27-provider-infrastructure-design.md` | accepted amendment, 2026-10-27, "no new runtime behavior is shipped here" | providers across local and remote infrastructure: restrictions, delivery, execution without a process |
| Daoris's measured evidence the two cite | built and run | `2026-09-30-console3-native-streams-evidence.md` (a subagent's lines on `stream-json`), D173's notes (a child's lineage on both agents) |

**A design nobody has built is evidence of what its authors expect to be hard**, which is `docs/agent-provider-layer.md`
§2's rule and applies unchanged: where this study files a row because a Daoris design says so, the row says
that, and where it files one because Daoris MEASURED something (the subagent lines, AG10) the row says that too.
The sections are cited as "Daoris §n"; Daoris is public, and nothing here reads a private repository.

**The row names.** The brief for this study spoke of rows a to w. Daoris's §11 lists **a to u**, the corrections
**s-i to s-ix** and **t**; the letters **g, j, q, r, w-s, w and x** that §2.8 names are FOUNDATION1's (D212's)
rows, which the design reads and does not build. §3 answers all of them, so no row is skipped under either count.

**The generic-library test** (`.agents/knowledge/generic-library.md`): each gap below is stated as the sentence
with the asker struck. A need that reads as an application's own policy is judged the host's (§5), and rule 7
decides it: *could two honest applications answer this differently?*

## 2. The two designs agree, and Lyntai is further on

The restriction model Daoris's amendment writes (cause *quota | rate | credit | concurrency | capacity | unknown*;
subject *account | declared-shared-pool | model | endpoint | unknown*; recovery *reset-at | retry-not-before |
owner-action | unknown*) is `AgentRestriction` with `RestrictionCause`, `RestrictionSubject` and `RestrictionRecovery`, member
for member; its delivery disposition (*never sent | taken | uncertain*) is `AgentDelivery`; its "a set of independent
restrictions, admission requires every one to permit, a stated moment permits ONE attempt, an owner action waits
for a person's release, an old reading never clears a newer failure" is `AgentPoolReducer` as built in AG6 (D227);
its "one layer owns the retry budget" is `RetryingAgentSession`; its hand-over is a brief and not the agent's memory
(`AgentBriefComposer`, D228); its `Known<T>` is `AgentAbility` with a source and evidence. **Lyntai has built what
Daoris has designed in each of those**, so the gaps below are the part of Daoris's design the layer does not
have, not a re-derivation of what it does.

Two places where Daoris's design is ahead of what the layer states, and which this study therefore reads as input
and not as agreement: its **child and task readings** (§3.2 there: a child seen, said and ended, a background task
started and ended), which Daoris built against measured streams, and its **model parts** (§4.2: a model asked,
carried, acknowledged and reported apart, a refusal read before the prompt).

## 3. Every Daoris row, answered

Columns: what the row moves; what Lyntai answers (the type); where the answer **differs** and whose the difference
is; what Lyntai **lacks** that is general (a row in `TASKS.md`) or why it files nothing. **The first three columns read the code as it stood when the
study was written**; where the Lacks column names a row, that row is built since (§7).

### 3.1 Rows a to u

| Row | Lyntai answers | Differs, and whose | Lacks |
|---|---|---|---|
| **a** the contract named, the registry, the census | `AgentProviderDescriptor`, `AgentDoor`, `AgentAbility` (a name, a support, a source: declared, negotiated or measured, evidence, `AppliesTo`), `AgentProviderRegistry` by id, an unknown id throws and names what exists | Daoris's `Known<T>` applies per **eight facts** (door, versions, posture, writable roots, reach, OS, settings digest); `AgentAbility.AppliesTo` is one version. Daoris needs it to ADMIT a measured confinement; Lyntai states `Unknown` and never reads it as a promise, the safe direction. **Not filed**: no consumer measures confinement per posture; it re-opens when one does. The census (no vendor name outside the providers) is a Daoris maintenance gate: Lyntai's vendor vocabulary in Core is the one classifier's sentence tables by **D219**, and `check-api-vocabulary` holds the surface | nothing |
| **b** the shim, the one registration list, the conformance suite, stand-ins discovered | `AddAgentProvider` is a DI collection, so a provider is a class and a registration; `AgentProviderConformanceTests` is a theory over the registry; `provider-stub.mjs`, `codex-stub.mjs`, `acp-stub.mjs` | the theory holds the DESCRIPTOR and the spawn rules; it never drove a stream, so "exactly one terminal", "a failed terminal carries its failure" and "a limit a tool printed never classifies" were held door by door in each door's own tests and by no theory | built, **AG16**: `AgentStreamConformanceTests` is the second theory, over the same registry |
| **c** a third stand-in through the contract alone | the suite's own stand-in (a host-own door, kind `host-own`; `DialectStandIn` in `tests/Lyntai.Tests/Agents/`) registers with no edit to Core | as filed it emitted one terminal and nothing a wire carries, so it proved the REGISTRATION and not the vocabulary: a stand-in with a dialect of its own, whose extensions no built-in door reads, is what shows a door is not reading another agent's keys | **AG15**'s proof, **AG16** (built: the stand-in has a wire of its own and is held to the stream rules) |
| **d** the owner, once: an account key above the doors | `AgentAccount.Provider` names one provider; an ACP agent is its own provider id, never a second door on a CLI id (**D225**) | Daoris keeps one provider with two doors and resolves the owner once; Lyntai chose two providers so an account, a seat and a hand-over each rest on one door's facts. **D225 named its own re-open condition: a host that needs one account across a CLI door and an ACP door of the same agent**; Daoris's design is that need, designed and not used. **Answered by AG20 as D234**: the doors stay providers and name one `Agent`, which is the account key Daoris's §2.2 describes ("a key resolves to its pool, never to one provider"); doors of one key read the home through one variable, so a plugin door that declares its own variable is a key of its own, as Daoris's §4.1 already has it | built, **D234** |
| **e** the accounts' readers (usage question, floors, windows, limit grammar, sign-in words) | `IAgentUsageSource` with a per-vendor internal dialect (`ClaudeUsageDialect`, `CodexUsageDialect`) and `AgentAllowanceReading` (**D222**); the limit grammar is `ProviderVerdictClassifier` and `StatedRecovery`, one classifier | Daoris splits words into a provider's and a door's tables; Lyntai keeps one classifier a BYO door may call (`ReadAgentFailure`) and builds its own `AgentFailure` where its words differ, so a third provider's reset grammar needs no registry. The **floor** (how often a read may run) is the host's: a read starts the vendor's process, which the ability's `SideEffects` says, and how often to pay it is a deployment's call | nothing |
| **f** the program: install, update, pin, release sources | `ICliBackend` drives the backend's own installer at a version the host names (**D20**) | Daoris owns release sources and a signed download; **D20 refuses exactly that** (Lyntai never fetches, chooses a version or picks a location), so this is the host's | nothing |
| **g** the doors as abilities | `AgentAbilities.All` (the names every provider states), `AgentDoor.Unhonourable`, refusal before a spawn | Daoris's `interactive` is a conversation the person types into: the host's, Lyntai has no chat in the layer | nothing |
| **h** the spawn: a recipe, one shell completing it, the launcher | `IProcessRunner` (`StreamLinesAsync`, `ExchangeLinesAsync`): the runner resolves shims and forces UTF-8; a turn's `environment` applies over the inherited one and a null value REMOVES a variable | Daoris's own variables (role, repository, service, git identity) are orchestration: the host's. The seam exists in both directions | **AG17** (an account that is a key) |
| **i** the protocol transport and its dialects | `AcpAgentSession` and `AcpAgentReader` (internal) over the spec's frames | as filed, the reader and `AcpFailure` read **both** vendors' extensions inline (`_claude/rateLimit`, `errorKind`, `codexErrorInfo`) by presence, for every agent, with no place for a third agent's extensions. Daoris's first step is the same composite, then a seam. **Built, AG15**: `IAcpExtensionReader`, with the shipped `AcpExtensionReaders.Claude`, `.Codex` and `.AirSessionFailure`, and `.Default` the composite | **AG15** (built) |
| **j** each protocol door its own dialect (a narrowing) | the composite reads for every door | narrowing is a CHANGE of what a door reads; Lyntai keeps the composite as the default and lets the host name the set (AG15), so the narrowing is a host's choice and not a rule | with AG15 |
| **k** the native transport and Claude's native dialect | `CliAgentLoop` is the one per-turn process loop; `StreamJsonAgentReader` and `CodexAgentReader` are the dialects (**D21**); `RetryingAgentSession` is the retry | Daoris's retry is "a structured resume's alone"; Lyntai's is a decorator keyed on the DELIVERY of the prompt: both bounded, one layer owning the budget | nothing |
| **l** the readings and the sink | the event stream IS the sink: `AgentStreamEvent` handed to the consumer; `SessionStarted`, `TextDelta`, `Thinking`, `ToolCall` and `ToolResult` (a `CallId`), `PlanUpdated`, `UsageLive`, `UsageFinal`, `UsageReading` (nullable, unit, scope, `MeasuredAt`), one `SessionEnded` with `Failure` | provenance (reported, observed, computed, inferred) and "replayed" on every reading is not carried: a protocol load's replay is dropped (`AcpAgentSession`), and an unread frame yields no event, only a bounded count and sample on the terminal (`SessionEnded.Unread`, AG32). A consumer that must tell inferred from reported has not appeared; **not filed**, the trigger is one that does. Daoris's `WindowsSaid` (windows in the stream) is covered between turns by AG9, and a window frame in the stream is `AgentAllowanceReport` (AG42, **D246**) | **a child's and a task's readings** (`ChildSeen`, `ChildSaid`, `ChildEnded`, `TaskStarted`, `TaskEnded`): **AG10**, **AG18**. A held permission request: **AG19**. A model refused: **AG12** |
| **m** the session's life: `AgentRun`, steer, stop-turn, close, the conversation's home | a turn is one `StreamAsync`; the conversation is `AgentSessionOptions.ResumeToken`; a stop is the cancellation token; `steering` is stated `Unsupported` on every door; the hand-over decision is `AgentHandover.Decide` | Daoris keeps a live run with steering for the ACP door (`promptQueueing`, built and measured there). **D219 §10 rejected a long-lived connection in v1 and left it a later optional capability**; the need survives the generic test, but it was a design and not one sitting, so it was an OPTION in **D233** with its trigger and not a row | an option in D233, then designed in §11 (AG39, **D251**) and built as `IAgentRunProvider` and `IAgentRun` (AG46 to AG48) |
| **n** models | `AgentSessionOptions.Model`, carried on both CLI doors by flag; `AgentChoice.Model`, set on the options by `AgentPick.Apply`; the reported model on `UsageFinal.Model`; effort is the host's word on `AgentChoice`, never interpreted by the library, which `AgentPick.Apply` sets on `AgentSessionOptions.Effort` for a door to carry or refuse (**D244**) | **as filed, the ACP door never read `AgentSessionOptions.Model`** (no reference to it, to `configOptions` or to `session/set_config_option` under `Acp/`), so a pool pick that names a model on an ACP provider dropped it silently, which is the one thing the layer's own rule forbids (an option a door cannot honour is refused, never dropped). And there was no `model` ability to ask. A `model_not_found` read `Fatal` with no restriction, so the pool re-picked the same seat. **Built** (**D243**): `AgentAbilities.Model`, and `AcpAgentReader` sets the model before the prompt or ends the turn `option-unsupported` | **AG11**, **AG12** (built). The offered LIST (a model picker) waits for a host that renders one; Daoris's two scopes (a session's offers and an account's default) are a settings page's |
| **o** permissions, skills, hooks | `AgentToolPolicy` (read-only or write), `DisallowedTools` by name, a read-only gate on the Claude door; an ACP read-only turn rejects every permission request and a write turn allows each once, never for good | Daoris's rule language is Claude's grammar and the product's (D72 there): the host's. Skills, plugins and the lineage hook are Daoris's plugin catalogue: the host's. **A request held for the person is general** (Daoris's `RequestHeld`, built) and the layer has no way to hand one to the host | **AG19** |
| **p** connectors | `AgentMcpServer` (stdio or http, an environment, an `AuthToken` kept out of argv), rendered per backend; `mcp-servers` and `disallowed-tools` abilities; the Claude door's `StrictMcpConfig` leaves the agent's own servers out | Daoris's `ConnectorPlan` carries the session's identity in every entry and states "its servers inherit its environment": the identity is the host's, and the inclusion rule is adapter-local on Claude (a Codex door states nothing about it). **Not filed**: one agent with the option and no consumer asking for it on another | nothing |
| **q** plugins' declared doors | `AddAcpAgentSession(id, command, arguments, environment, accountHomeVariable)` is the declared door: its claims are fixed `Declared`, resume is `Unknown` with the source `Negotiated`, and nothing a host passes can state steering or a limit grammar | Daoris's manifest is the host's; the effect (a declaration has no field for what it may not claim) is what the registration already gives | nothing |
| **r** the page, the service, the tools | `AgentProviderDescriptor` is the sheet: a host reads abilities and never an id | the measurements table, the page and the service's call-lineage readers are a desktop's | nothing |
| **s-i** the machine's door | not a Lyntai concept | the host's | nothing |
| **s-ii** a resume goes on the record's own door | `AgentHandover.Decide`: the same provider, door and account, and `resume` stated `Supported` (`Unknown` is not promised), else a fresh session with the brief | the same rule | nothing |
| **s-iii** the read floor per provider | not carried (see **e**) | the host's | nothing |
| **s-iv** the efforts a door offers | effort is the host's word on `AgentChoice` | the host's | nothing |
| **s-v** the Claude seam on every protocol door | the ACP door sends no vendor variable but the one the host names as the account's home | Daoris-specific | nothing |
| **s-vi** other providers' credentials stripped from a spawn | the removal seam exists (a null value in `environment`) | which variable to strip is a deployment's policy; **the DECLARED list of variables each provider reads** is a fact about the provider, which a host needs to compute the strip and which the descriptor does not state | **AG17** |
| **s-vii** counted by agent, a pick carried across one agent's two doors | an account filed under the agent is one account in the pool whichever door's choice it sits in (**D234**); a pick names the account and model, and the host chooses the door | the conversation is not carried: `AgentHandover.Decide` resumes only on the same provider and door, else a brief | built with row **d**, **D234** |
| **s-viii** a child's card kind | not a Lyntai concept | the host's | nothing |
| **s-ix on** each cell where today's copies disagree | not a Lyntai concept (one registry) | Daoris-internal | nothing |
| **t** the old seam's end, records held | the ids and event shapes are frozen (**D70**) | Lyntai keeps its strings by the freeze | nothing |
| **u** `Reach`: which repositories a provider may be sent | `AgentSessionOptions.WorkingDirectory` and the tool policy | where the person opens a repository to a provider is **admission, the host's**: two applications answer it differently (rule 7). That a door's turn sends the working content to a remote service is a FACT about the door and is stated with the door (AG14's evidence) | nothing |

### 3.2 FOUNDATION1's rows the design reads (§2.8 there)

| Row | Answer |
|---|---|
| **g** (failures read once on every path), **j** (one start plan, one walk) | the first is `ProviderVerdictClassifier`'s one place and `AgentFailure` on every failed terminal (AG1); the second is the host's start plan, which Lyntai's `AgentAccountPool.PickAsync` serves with a seat per turn |
| **q**, **r** (one policy shape, the connector by work) | the host's: which work a call belongs to |
| **w-s**, **w**, **x** (the roles' staffing, the catalogue of working roles, starts take their role) | the host's orchestration; Lyntai's §8 names it |

### 3.3 OPENROUTER1 (Daoris §12)

Daoris's first reading is **Claude Code's program pointed at OpenRouter** (a base address, the key in one variable,
another left empty), not an HTTP door; its provider keeps a key account, declared offers, limit words from recorded
sentences and a daily cap read as a limit, and "its gateway is no proof of another maker" (a review it serves
reads its independence as unknown). Against Lyntai:

- **The same composition runs in Lyntai today for one account**: `AddClaudeCliAgentSession(environment: …)` takes the
  base address and the token as standing variables. What it cannot do is **an account that is a key** and varies per
  turn, because `AgentAccount` carries a home and nothing else: **AG17**.
- **The HTTP door**, the brief's first candidate, is a different thing from Daoris's reading and is the one Lyntai
  can build without a vendor program: `HttpModelProvider` already speaks an OpenAI-style chat completion with tool
  calls, and `ToolLoop` already yields `AgentStreamEvent`s with one terminal. What is missing is the door: a
  descriptor, abilities, a typed failure and a delivery disposition: **AG13**, **AG14**, **D233**.
- **A daily cap read as a limit with a stated or absent reset** is `RestrictionRecovery`; the HTTP half (a
  `Retry-After` header Lyntai never reads, a 402 as a credit hold needing a person) is **AG13**.
- **Maker independence** (D155 there) and **where the person opens a repository** are the host's.

## 4. The gaps, and the test each passed

Each sentence is what a library user would say with the asker's name struck. The Evidence column is the code as it read
when the row was filed; every row is built.

| Row | The general need | Evidence | Why it is not the host's |
|---|---|---|---|
| **AG10** | *An application that runs Claude Code and shows its tool calls and its plan needs a subagent's lines kept apart from the session's.* | Daoris measured it (`2026-09-30-console3-native-streams-evidence.md`): every line a subagent produced arrived on the session's stdout with `parent_tool_use_id` set, `assistant` lines with its tool calls and `user` lines with their results, none of them partial. `StreamJsonAgentReader` has no reference to the field, so a subagent's `TodoWrite` replaces the session's plan, its tool calls read as the session's, its usage ticks as the turn's, and its last text and error fields can become the turn's fallback text and failure reading | no application wants a subagent's plan to overwrite its own |
| **AG11** | *An application that picks a model for an ACP agent needs the door to carry it, or to refuse it.* | the code read in §3.1 row n | the layer's own rule: an option is never dropped |
| **AG12** | *An application whose pool picks a seat needs a refused model to stop being picked for that seat.* | `model_not_found` is `Fatal` with no restriction; the reducer holds a fatal failure only when it carries one | a hold is a fact; its length is the host's (`ClearAsync`) |
| **AG13** | *An application calling a chat-completions endpoint needs a 429's `Retry-After`, a 402, an overloaded 5xx and the error envelope read as a limit with a stated or absent recovery.* | `FromHttpFailure` returns a verdict from the status and the body and never reads a header; the infrastructure design's §9 cites OpenRouter's documented budget metadata and a reset that can be absent | a default for an unstated reset stays the host's (rule 7, D219) |
| **AG14** | *An application that runs agents wants the model reached over HTTP to be one more provider: described, refusing what it cannot do, ending a turn with a typed failure.* | `ToolLoop.StreamAsync` already yields the events and one `SessionEnded`; no `IAgentProvider` wraps it | an executor owning the tool loop is Lyntai's; a workspace is not (D233) |
| **AG15** | *An application running a third ACP agent needs to teach the door that agent's `_meta`, error data and limit frames without a fork.* | Daoris's `IProtocolDialect`, built against two agents; Lyntai's reader names both inline | the extensions are the agent's, and the host knows which agent it registered |
| **AG16** | *An application that registers its own provider needs the stream rules every provider keeps held by one theory, as the descriptor rules already are.* | the conformance theory covers the descriptor and the spawn only | one source for the rules is what D211's shape asks |
| **AG17** | *An application that runs an agent on an API key, or through a gateway, needs an account that is a set of variables and not only a home, and the provider's own list of what it reads.* | `AgentAccount` is `Provider`, `Name`, `Home`; Daoris's key account and its strip rule (s-vi) | the VALUE is the host's and is never stored (D20); the declared NAMES are the provider's |
| **AG18** | *An application that runs an agent that starts background work needs to know it started and how it ended, because the binary ends it when the turn ends.* | Daoris measured it (the same evidence: `task_started`, `task_updated`, `task_notification`; background work killed at the turn's end) | the fact is the agent's; what to show is the host's |
| **AG19** | *An application that lets a person decide a tool call needs an ACP permission request handed to it, not decided by the library.* | Daoris's `RequestHeld`, built; Lyntai's ACP door rejects every request in a read-only turn and allows each in a write turn | who decides is the host's, and today it cannot |
| **AG20** | *An application that runs one agent through both its CLI and its ACP adapter needs a limit on one to hold the other.* | D225's own re-open condition; Daoris §4.1 ("one account on two doors is one account") | built as **D234**; no persisted key moved, because the key of an account filed under its provider's id is unchanged |

## 5. What the study judges the host's

Which quest or task an agent works, how its work lands, review and maker independence, the person's screens,
the roles and their staffing; **when** to rotate, wait or hand over (the layer reports the earliest moment, the host
sleeps); where a transcript is kept and any durable request record across a restart (Daoris's execution-request
records and their reconciliation are Course's there); a default cool-off for an unstated reset; the floor on how
often a usage read runs; which variables to strip from an inherited environment; where the person opens a repository
to a provider; the rule language and skills; the plugin catalogue and its manifest. These are `docs/agent-provider-layer.md`
§8's list with the Daoris cases added; each fails rule 7 (two honest applications answer it differently).

## 6. What the study does not file, and why

- **A long-lived run with steering and a stop that keeps the conversation** (Daoris's `AgentRun`, the infrastructure
  design's "the run is an asynchronous resource"). Real and general, but a design round and not a sitting; recorded
  in **D233** as an option with its trigger and its first measurement, as `.claude/rules/repo-mechanics.md` says a
  conditional item goes. The trigger was later met, and §11 designs it and AG46 to AG48 build it.
- **A hosted agent** (a remote job with a workspace snapshot, a broker grant and a cancellation not confirmed by a
  closed socket; the infrastructure design's §3 and §6). **D219 §6 left it out and D233 keeps it out**: Lyntai
  owns no workspace and has no cancellation story for a remote run that a closed connection does not give.
- **The eight-fact applicability, the census, per-event provenance, a live window frame, an offered-model list, a
  per-door `leaves its own servers out` ability.** Each has an application that would answer it differently or has
  no consumer yet; §3 names the trigger.
- **Durable request records across a restart.** The pool persists its holds; a consumer's words and their
  delivery are the host's (§5), and `RetryingAgentSession` is in process.

## 7. Order of the rows

AG20, AG11, AG12, AG10, AG13, AG14, AG15, AG16, AG17, AG18 and AG19 are built (**D234**, **D243**); this is the order they were built in, one sitting each:

1. **AG10**: a defect against measured evidence in a shipped reader, small, and it needs nothing.
2. **AG11**, then **AG12**: the second reads the refusal the first makes, and a pool pick must not drop a model
   silently (Daoris §4.2's models).
3. **AG17**: an account that is a key and the variables a provider reads; it builds on the account key AG20 fixed, and
   Daoris's first new provider (§12 there) needs it.
4. **AG16**, then **AG13**, then **AG14**: the stream rules are held by one theory before the HTTP door exists, so the
   door is born under them, and the failure facts come before the door that needs them (**D233**).
5. **AG18** (after AG10: it reads the same lines), then **AG15** and **AG19**, which no Daoris row waits on: Daoris
   built its own dialect seam and held permission request.

The order is Lyntai's reading of the dependencies in the rows and of Daoris's §11 order; a host that reports a blocking
need moves a row up, and a row that is not built says so in `TASKS.md` rather than here.

Each row is sized for one sitting, names its lanes (`daoris.lanes.json`), and carries its proof in `TASKS.md`.
Every capture a row's tests use is labelled SOURCE-DERIVED or REAL as `tests/Lyntai.Tests/Providers/AcpAgentSessionTests.cs`
labels them: this study captured nothing, and the evidence it cites is Daoris's.

## 8. What this study does not establish

It read Daoris's files and Lyntai's code and ran nothing: AG10 rests on Daoris's capture at one binary version
(an asynchronous subagent; a synchronous one, a nested one and a failed one were not seen there), and the row's
first step is a fixture of that shape. The Daoris rows were read as accepted designs, with row a alone built, on a
branch. Whether a Codex or ACP agent emits any child frame the reader should take is unmeasured on both sides.

## 9. How a host consumes the layer before a release (**D235**)

A host that builds on the layer takes it as a package (`Lyntai.Core` for the contracts and the pool,
`Lyntai.Providers.Basic` for the doors) and composes it through `AddLyntai`. **Every entry for the layer is under
`## Unreleased` in `CHANGELOG.md`, so no version a host can pin carries any of it.**

**Building against what is not released.** Pack the packages from a named commit of this repository into a folder the
host keeps untracked (`dotnet pack Lyntai.slnx -c Release -p:Version=<a version of the host's own> -o <folder>`, as
`consumer-smoke` does), add the folder as a package source in the host's untracked NuGet configuration, and use a version
that differs on each pack and evict the global cache's copy of it (`docs/GATES.md` §consumer-smoke says why). The host's
tracked files name no folder and no unreleased version; when a release carries what it needs it pins that release. Lyntai
publishes no preview version.

**What is frozen.** A release freezes the surface it carries (**D70**; **D18**'s disclosed break in a minor). Nothing of
the layer is yet carried, and every row is built, so the tiers below record what the rows touched, additively, before a
release carried it:

| Tier | Members | A host may |
|---|---|---|
| The first slice's members, which later rows only added to | `IAgentProvider`, `AgentProvider`, `AgentProviderRegistry`, `AgentProviderDescriptor` with `Agent` and `AccountKey` (**D234**), `AgentDoor`, `AgentAbility`, `AgentAccount`, `AgentSessionOptions`, `IAgentSession` and the event records but `ToolCall` and `ToolResult`, `SessionEnded` with `AgentFailure`, `AgentRestriction` and `AgentDelivery`, `AgentAccountPool` with `AgentChoice`, `AgentPick`, `AgentSeat`, `AgentHold` and `IAgentAccountPolicy`, `AgentHandover`, the brief and transcript types, the retry decorator, `AddAgentProvider` in both forms, the pool, retry and brief registrations, and `agent:` on the three session registrations | build on them as they stand |
| Grown by a row, additively | `ToolCall` and `ToolResult` (a `ParentCallId`, AG10); `AgentAbilities.All` (a `model` ability, AG11, and `permission-requests`, AG19, both built; a provider that states fewer reads `Unknown` for the rest); `AgentAccount` and `AgentAccountMapping` (environment entries and the names a provider reads, AG17, built; it replaced `AgentAccount`'s test that it holds only a name and a home); the new event records `AgentTaskStarted` and `AgentTaskEnded` (AG18, built); `AddAcpAgentSession` (a set of extension readers, AG15, built, and a callback, AG19, built); `AddHttpAgentSession` and `AgentDoorKinds.Http` (AG14, built); the HTTP failure reader (AG13, built); the usage reading: `AgentAllowanceWindow` and `AgentAllowanceReading` reshaped to state what the server did (AG23, **D237**), `AgentUsageReadingOptions` with a version policy, a working directory and a client identity (AG24, AG22, **D236**, **D238**) | build on the members that exist, and treat a member a row adds or reshapes as absent from a pack taken before its row landed |
| Not a promise | `internal` types, XML-doc wording, log text, the SOURCE-DERIVED capture shapes (`TASKS.md` AG rows say which), an ability stated `Unknown`, and what `docs/guide.md` says an agent's wire carries beyond the neutral events | read them, never depend on them |

Every row's change to a type in the first two tiers is made with its `ApiSurface` baseline moved from an emitted
`.actual` and a `CHANGELOG.md` entry, and a change that is not additive is disclosed under `### Breaking` (**D161**), so
a host that rebuilds against a newer pack reads the difference in one place.

**What the study's rows missed.** Reading Daoris's §2.2 to §4.1 and §11 again for what it needs to replace its own
contract found one thing the study had filed as AG20 and no further general gap: **row d** (the account key, one pool
for a key) is **D234**, **s-vii** follows it, and **s-vi**'s declared variables and OPENROUTER1's key account are **AG17**.
What stays Daoris's, each failing the generic-library test (two honest applications answer it differently): the door's
trust file and whether a start with no account runs in its own home, `Reach` and the opening of a repository to a
provider, the rule language, skills and hooks, the plugin catalogue and its manifest, the census, the eight-fact
applicability of a measurement, and a plugin door's `Lent` part. **Not filed, with a trigger**: a long-lived run
(**D233**), provenance on every reading, an offered-model list (§3.1 row n).

## 10. Daoris's second request: what its moves still wait on (A-LQ3 to A-LQ17)

Daoris takes the layer from a pack of a named commit (§9, **D235**) and moves a door onto it only when the layer carries everything
Daoris does on that door today, case for case. It asked, read at `master` `8aacfb8f`, for twelve needs numbered **A-LQ** (the LQ
numbers are Daoris's memory requests). Each was read against the code as it stands and put to the generic-library test with the
asker struck; **every need taken is built**, and a need declined keeps Daoris's own transport on that door. The rulings are **D244** to
**D249**; the rows were AG25 to AG45, each archived in `docs/task-archive.md` in the order it landed: Part 447 (AG43), Part 448 (AG25 to
AG27), then Parts 449 to 465 (AG37, AG35, AG34, AG30, AG31, AG32, AG33, AG28, AG29, AG36, AG42 in two Parts, AG40, AG41, AG44, AG45, AG38), and
AG39's design, Part 466, is §11.

| Need | Ruling | Row | Decision |
|---|---|---|---|
| **A-LQ4** a permission mode as the agent's own | **taken**: one option carrying the agent's word; a door that cannot carry it states so and is refused before a spawn | AG25 (CLI doors), AG28 (ACP door) | **D244** |
| **A-LQ5** effort carried or refused | **taken**, the same shape as the model (**D243**) | AG26, AG28 | **D244** |
| **A-LQ6** plugins handed or refused | **taken** as a neutral option and ability; only the Claude door carries it | AG27 | **D244** |
| **A-LQ9** a turn's own variables | **taken**, last, as a turn's environment | AG29 | **D244** |
| **A-LQ7** a child's and a task's life | **taken in part**: a child's words, a protocol child, an unread frame; **answered another way** for a child's start and end; **routed to the kept run** for a woken turn and a replay | AG30, AG31, AG32; AG39 | **D245**, **D248** |
| **A-LQ17** a turn's own report, a session start as announced | **taken**: denials, usage by model, the context figure, a start told from a frame that carries the id | AG33, AG34, AG35 | **D245** |
| **A-LQ8** what the open offered | **taken** for the protocol door, as an event before the prompt | AG36 | **D245** |
| **A-LQ3** a run kept across turns | **taken in three steps**: the prompt's own delivery, a request that waits for the person, then the kept run as a design first | AG37, AG38, AG39 | **D248** |
| **A-LQ11** why a hold needs a person, what access failed | **taken**: a reason on the owner action, and two readings of an access failure kept apart | AG40 | **D246** |
| **A-LQ16** a limit's window, the reset as stated, the reports | **taken**: the window on the restriction, the stated parts on the recovery, a report event | AG41, AG42 | **D246** |
| **A-LQ12** the restriction rules without the walk | **taken**: two defects first, then the rules public over a snapshot the host stores | AG43, AG44 | **D247** |
| **A-LQ13** the CLI turn's rules over a host's reader | **taken** | AG45 | **D249** |
| **A-LQ10** a built-in door built without a container | **declined**: the registration is how the door's claims and usage source are wired | none | **D249** |

### 10.1 What each reading rests on

Each bullet states the code at the commit the request was read at; the row it names is built since.

- **A-LQ4.** `ClaudeAgentArgs.Build` writes `--permission-mode acceptEdits` for a write turn and nothing else, and
  `ClaudeAgentOptions.SkipAllPermissions` replaces it; no option carries a mode, and the ACP reader never sets one. A mode is
  vendor vocabulary, so the option holds the agent's own word and a door refuses a word it was not measured with: Daoris's
  "refuse before a spawn", and **D243**'s model shape without its list check, since a CLI has no list to ask. **The second half is
  not filed**: the always-denied flow tools (`AlwaysDenied`) are a fact about a door, and when Daoris's measurement says whether a
  driven start should keep them, the door states the set it denies in its claim's evidence, which needs no option.
- **A-LQ5.** `AgentChoice.Effort` is carried to the host on the pick and no door reads it. The model's shape makes a pool pick drop
  nothing: `AgentPick.Apply` sets it, and a door that cannot carry it states `Unsupported`. A protocol agent offers effort as a
  config option of a category, which the door finds as it finds the model.
- **A-LQ6.** A plugin directory is the agent's own extension folder, so what is in it is never read; a door that does not carry it
  refuses. Only `claude` has the flag measured by anyone here, so every other door states `Unsupported` until a row measures one.
- **A-LQ7.** `StreamJsonAgentReader.ReadAssistant` reads a subagent's calls and drops its `text` blocks (the `own` guard), and reads
  no `thinking` block at all. A subagent's lines are not partial (§4, AG10), so its words arrive whole in the complete message and
  one `TextDelta` with a parent is the honest shape. **A child's start and end are not new events**: its first event with a
  `ParentCallId` is its start, and its end is the `AgentTaskEnded` of the task `AgentTaskStarted` opened for that call, else the
  `ToolResult` of the call, because a background child's call returns at once while it runs on and a synchronous child has no
  task. Two sources that can disagree are a worse contract than one rule written down and tested, which is AG30's documentation
  half. The protocol door drops a child's session by id and reads none of the `parentToolUseId` stamp that a child not declared as a
  session carries on the root session (Daoris's record of `claude-agent-acp`), which is AG10's defect on the other door (AG31). The
  turn a task's notification wakes exists only when the agent's stdin stays open; today a turn is one process whose end ends its
  background work, so that and a replayed frame are the kept run's (AG39). Readers are tolerant by design and an unread frame
  leaves no trace; a bounded count and sample on the terminal is the cheapest trace that cannot flood a stream (AG32).
- **A-LQ17.** The Claude `result` frame carries `permission_denials`, `modelUsage` (by model, with `contextWindow`) and the usage;
  the layer reads none of the first two, and `UsageFinal.Model` is the last model the reader remembered. `SessionStarted` is
  yielded for any `system` frame with a new id, which the reader's own comment says is deliberate; the distinction Daoris needs is a
  flag on the event, and the host keeps the first one announced. A cost the CLI states is not carried: Lyntai reports a backend's
  cost only where it is the backend's own figure (`.agents/knowledge/lyntai-computations.md`), and nothing here asks for it.
- **A-LQ8.** `AcpModelOption` already reads the offers from the open's answer and keeps them for a diagnostic's words. An event
  before the prompt carries every option the agent offered, its current value and its values, so a refused turn still shows the host
  what it could have chosen. It must not count as delivery, which is AG37's rule.
- **A-LQ3.** One `StreamAsync` is one turn and one process, by **D219** §10, and **D233** left a kept run an option with a trigger,
  met now by a host that needs a word taken mid-turn. Three things in the request do not need a kept run. **Delivery:**
  `AcpAgentReader.StartPrompt` yields `SessionStarted` and writes the prompt in the same call, and `CliAgentLoop.Stamp` counts any
  event but the terminal, so a protocol turn whose agent dies right after the open reads `Taken` with the prompt never read. **A
  request held for the person:** `IAcpPermissionApprover` refuses at half the inactivity window, and the runner's clock ends a
  silent agent, so waiting as long as the host holds needs the clock told. **A repeatable open** has no meaning before a connection
  is kept. The kept run itself is a design, and AG39 is the design.
- **A-LQ11.** `AgentFailureKind.Access` and `RecoveryKind.OwnerAction` say that a person must act and not which action. The door's
  fields (`error` categories, a status) say an access failure in a word that does not tell a sign-in from a refused credential, and
  the sentence does; **D219** says only a field classifies, and a sentence may fill a detail the field left open
  (`StreamJsonFailure.Merge`). Daoris's "never one winner" is kept by carrying both readings and a rule for neither.
- **A-LQ16.** `RejectedLimit` is internal and keeps a window, a reset and a scope that `AgentRestriction` then cannot state; an
  `allowed` frame is dropped by `NoteRateLimit`; the session builds its reader with the system clock; and `StatedRecovery.Read`
  returns null for a zone it cannot find, which drops a stated time whole. **What stays Daoris's**, as it asks: the names it gives a
  window and its policy for a time that has passed today, because the library states the parts and its own resolution under its one
  rule (the next occurrence, a minute of skew allowed) and a host re-resolves from the parts.
- **A-LQ12.** `AgentPoolReducer` is pure and internal, and `AgentAccountPool` is single-process by **D227**, whose re-open condition
  is this need; the host's store is the host's, so the answer is the rules public over a snapshot and not an atomic store
  capability. **Two defects came out of the read, both from the source and not run:** `Hold` computes revision 1 for an id no longer
  held, so a clear that names an old revision 1 matches a hold recreated at revision 1; and `Succeed` removes the hold whose
  revision matches without asking whether the reporting attempt's lease token still owns it.
- **A-LQ13.** `CliAgentLoop` is internal. The runner raises `ProcessRunException` with its exit code on a non-zero end and ends the
  stream silently on zero, so a host's reader needs the end told to it with that code, and a stream that ends with no terminal is
  fatal only when the reader yielded none.
- **A-LQ10.** Registration is DI-first by the repository's own rule, and a built-in door's claims, usage source and account mapping
  are wired by `AddClaudeCliAgentSession` and its siblings; a constructor outside it would be a second place the claims drift from
  the wire. The cost Daoris names, a private container per start, is a cost and not a blocker.

### 10.2 What this section does not establish

It read the code and Daoris's checked-in records and ran nothing. A wire fact a row needs is labelled SOURCE-DERIVED where it rests
on Daoris's capture, and the row says so in its proof. Whether a Codex or protocol agent emits a child frame the layer should read
stays as §8 left it. What the installed `claude` lists for modes, effort levels and `--plugin-dir` is the first step of AG25, AG26
and AG27: a `--help` read, no spend.

## 11. A run kept across turns: the design (AG39, **D251**)

**What this reads.** Daoris's recorded measurements of the agents it keeps a process open on, as its second request (§10) points
to them: its design's §5.1, its steer, stream-json, console-streams and model-effort evidence notes, and the fix log's BGWAIT2 entry.
It read them and ran nothing. **REAL** below is a note's own "measured" (a run Daoris took), **SOURCE-DERIVED** its "read" (an
agent's source or schema, no run), and **Unknown** is no record at all. Daoris's line citations no longer resolve at its head (its
protocol door was split into several files since), so the evidence is cited by note, not by line.

### 11.1 Which doors can carry a kept run

| Capability | Claude protocol door | Claude CLI, stdin held open | Codex protocol door | Codex CLI (`exec`) | dsh over the protocol |
|---|---|---|---|---|---|
| **Turns in one process** | Supported, REAL (a second prompt on one process, steer note) | Supported, REAL (two turns, one process, stream-json note) | Unknown | **Unsupported**, SOURCE-DERIVED: the prompt is taken once; a resume is a new process | Unknown (one scripted prompt then close, REAL) |
| **A word taken mid-turn** | Supported, REAL: queued and folded at the next step; the earlier prompt answers `end_turn` with every usage count 0 | Unknown: bundle-derived only, never run | **Unsupported by a second prompt**, SOURCE-DERIVED (it replaces the tracked one); its `_session/steering` is advertised and never run, so Unknown | Unsupported (no input after the start) | Unknown |
| **A stop that keeps the conversation** | Unknown: a cancelled turn's zero counts are REAL, a usable next turn is not recorded, and cancel with a word on its way loses its answer (SOURCE-DERIVED) | Supported, REAL (an interrupt, then an ordinary turn) | Unknown | Unknown | Unknown (SOURCE-DERIVED mapping only) |
| **The model re-asked between turns** | Unknown: REAL only after `session/new` and before the first prompt | Unknown, not offered | Unknown: the same, before the first prompt | Unknown | Not offered |
| **Background work read until it ends** | Supported, REAL (the task frames, and a subagent as a session of its own) | Unknown: on `-p` the binary ends it with the turn (REAL); held stdin never probed | Unknown | Unknown | Unknown |
| **A turn a task's end wakes** | Supported, REAL (the agent spoke again 1.6 and 2.4 s after the response); a prompt sent into the woken turn was **never answered** (REAL, BGWAIT2) | Unknown | Unknown | Unknown | Unknown |
| **A replayed frame told from a live one** | Unknown: `session/load` replays and `session/resume` does not (SOURCE-DERIVED) | Unknown (a flag is read from `--help`) | Unknown (SOURCE-DERIVED, as the left) | Unknown | Unknown |
| **A repeatable open** | Unknown: the raw open repeats in Daoris's stand-in only, and the handshake refuses a second | **Unsupported**: no open exists, a new open is a new process | Unknown | Unsupported (a new process) | Unknown |
| **A request held for the person, process kept** | Unknown: a request arrives (REAL) and is answered, held for minutes never run | Unknown (`-p` refuses what it does not allow, SOURCE-DERIVED) | Unknown | Unknown | Unknown |

**The library's own HTTP door** carries no kept run: a turn is one tool loop and the conversation is the host's messages.
**Not established** (Daoris's own list, and this study's): whether any CLI door accepts input mid-turn; whether Codex's adapter
takes a word mid-turn by steering; a stop against a session with background work running; a request held for minutes; and any dsh
row beyond one scripted prompt. **Two records contradict their text** and the table follows the record: Daoris's design says the
native door "takes a framed message" mid-turn, and its stream-json note measured messages between turns and an interrupt, with the
mid-turn line stated as not measured; and a conversation's background work "may outlive a turn" is stated where the native note says
it was not probed.

### 11.2 What a YES does to the library and what a NO does

**A YES** adds one optional capability beside `IAgentSession` and leaves that interface and its one-turn model as they are: a run
type and an optional provider interface (§11.3), a turn event or two, and the abilities of §11.1 stated on each door, all frozen
from release (**D70**). It rebuilds `AcpAgentReader`'s per-turn state as per-run state for the one door with a REAL record, the Claude
protocol door, over the live-stdin runner member that exists. It costs three rows (AG46 to AG48), no account and no model call. It is
what lets Daoris move its conversations (its moves 1e and 1f-i) off its own transport for that door; every other door keeps the
one-turn model, stating `Unknown` or `Unsupported`.
**A NO** adds nothing and spends nothing: the one-turn model serves a host that never takes a word mid-turn, and Daoris keeps its
own transport on the doors where it keeps a process across turns, as §10 said a declined need does. It also leaves **D233**'s
re-open condition (a host that needs a word taken mid-turn) met and unserved, which is the cost this study weighs.

### 11.3 The shape

- **A separate pair, found by type test.** An optional `IAgentRunProvider` on a provider (as `IAgentAllowanceSource` is) opens an
  `IAgentRun`; no member is added to `IAgentSession`, whose BYO implementations would all break (**D70**). A provider without it
  states `kept-run` `Unsupported` or `Unknown` as §11.1 says.
- **A turn is the stream a host reads today.** `IAgentRun.SendAsync(prompt)` returns an `AgentRunTurn` whose events are the same
  events with exactly one `SessionEnded`, so delivery, failure and usage read as they do per turn. The run is one account for its
  life: a host takes one pick for it and gives it back at the end (**D227**).
- **A word mid-turn** (`SendAsync` while a turn runs) is taken only on a door stating `queued-words` Supported. The earlier turn then
  ends with a `SessionEnded` of subtype `folded`, success, every usage count 0, as the record shows the agent answers it; the later
  turn owns the rest. The run never sends `session/cancel` while a word is on its way.
- **A stop that keeps the conversation** is `StopAsync`: a cancel as the one-turn door sends it, the turn ending as a cancelled one
  ends today. The door states `keeping-stop` `Unknown` until a record shows a usable turn after it, and the run states no more.
- **A held permission request** is the same approver seam and the same wait (**D248**, AG38). The run refuses `SendAsync` while one
  is held, because no record shows a folded word and a held request together: an `Unknown` is not offered.
- **A repeatable open** is not offered, and **the library never repeats an open itself** (**D248**). An open that fails ends the run
  with a typed failure and the host opens another run, a new process; a raw `session/new` re-sent on the live process is Daoris's
  own and rests on a stand-in.
- **A replayed frame** is not an event. The updates a `session/load` streams before it answers are the conversation's history, dropped
  as the one-turn door drops them; `session/resume` sends none. The door states `replay-told` `Unknown` because no run showed the load.
- **The turn a task's end wakes** is surfaced as a woken `AgentRunTurn` on the run's `WokenTurns`, ended by silence only after a
  host-set `WokenTurnQuiet` (no default: the library invents no deadline) with subtype `quiet`. `SendAsync` is refused during one, as
  the record shows a prompt sent into it is never answered. Background work's end is read as `AgentTaskEnded` events that may come
  after the turn that started it.

### 11.4 The rows

AG46 (the type, the open, a turn, a stop and the abilities of §11.1, on the Claude protocol door), then AG47 (a word taken mid-turn
and the refusals above) and AG48 (the woken turn and background work read until it ends), archived as `docs/task-archive.md` Parts 469 to 471. Their captures are
SOURCE-DERIVED from Daoris's notes, which a real one replaces when it arrives, and none spends an account.

## 12. Daoris's third request: A-LQ18 to A-LQ24, Part 481's frames and a release (**D340** to **D343**)

Daoris read the layer at `master` `fa54381f` and asked for seven needs and a release. Each was read against the code and the records it names
in Daoris's checkout, and put to the generic-library test with the asker struck. Four are built, three are planned as rows (`TASKS.md`
Part 518), and the release is prepared and stays the owner's (**D19**). The rows' captures are read from Daoris's notes and none was run:
no signed-in agent and no installed `codex` was used.

| Need | Answer | Where |
|---|---|---|
| **A-LQ18** words that cut into the running turn, on a kept run | **planned**: an optional member beside `IAgentRun`, answering `injected`, `startedNewTurn` or left to the host; `claude-agent-acp` 0.84.0 is the only record | AG49 |
| **A-LQ19** a kept run on `codex-acp` and dsh | **planned, blocked on the records Daoris will send**; the §11.1 cells stay `Unknown` until then | AG50 |
| **A-LQ20** another writer told apart | **built**: `AgentFailure.Conflict` `ConversationHeld`, read from `data.reason` and never the sentence | **D340** |
| **A-LQ21** a Claude CLI kept run with stdin held | **planned**, design first: its measured frames are two turns on one process and an interrupt, and the rest was never run | AG51 |
| **A-LQ22** Codex's sandbox, approval and effort on the exec door | **built in part**: the sandbox as `PermissionMode` and the effort; **the approval half is declined** because `exec` asks no one | **D341** |
| **A-LQ23** sign-in state and sign-in per account home | **built**: `IAgentAuthSource`, a login handing its lines over while it waits; **ports declined** as the host's; a device-code flag is a row | **D342**; `TASKS.md` Part 518's last row |
| **A-LQ24** child sessions declared, or their absence stated | **built as a statement**: `child-sessions` `Unsupported` on every door, naming the reading to rely on; declaring them as an option is a row | **D343**, AG52 |
| **Part 481** rejected limit never cleared inside a turn | **waiting**: the ruling needs the frames in turn order, and none had arrived; Daoris's checkout holds a single `allowed` frame per turn on the native door and a rejection followed by an error, never both in one turn | `TASKS.md` Part 481 |
| **A-LQ1** a release that carries the agent layer | **prepared** (below) | **D19** |

### 12.1 What each reading rests on

- **A-LQ20.** The protocol door's Codex reader read `codexErrorInfo` alone, so `{"reason":"thread_active_writer"}` fell to the code's reading, `Fatal`
  from `-32600`. Daoris's record is `codex-acp`'s refusal of a resume another client holds (`-32600`, `data.reason`, `invalidRequest`, a sentence naming the
  Codex app, the CLI or an IDE), never run here. The sentence is not read, and the same words without the field state no conflict (a test).
- **A-LQ22.** The door's argv already carried `--sandbox` from the tool policy and `CodexAgentOptions.SandboxMode`; a host composing through the registry with
  neutral options could not name `danger-full-access`. The sandbox words are the three `codex exec --help` lists, measured in this repository. **Approval** is not carried: a host's
  audit of codex-cli 0.160.0 read an MCP call refused with "approval policy is never", so `exec` fixes it and `-c approval_policy` would be a claim no
  record shows. **Effort** is `-c model_reasoning_effort`, a key the host's own tests write into a config and the maker's reference lists; its five levels
  were not run here, so the claim is `Declared` and a level a model does not take is the agent's own failure.
- **A-LQ23.** The inference providers had `StatusAsync` and `LoginAsync` with both parsers, fixed to one environment. The agent door needs the same under an
  account's home, which is the account mapping the turn already uses, so the answer is the same engine under a merged environment. The inference `LoginAsync` returns its output at
  the end, which is too late for a device code, so the agent one reads the program's lines as they come. **Not carried**: the ports (where a browser runs relative to the
  process is the deployment's), a logout, and a device-code flag (not measured).
- **A-LQ24 and which reading to rely on.** With neither declared, `claude-agent-acp` keeps the spawning `Agent` call in the parent's stream and stamps a child's calls and
  words `_meta.claudeCode.parentToolUseId`; the Claude CLI door's `parent_tool_use_id` is the same fact, and both read as `ParentCallId`, a child's start being its first stamped
  event and its end the task's end else the call's result (§10.1). With `nativeSubagentSessions` declared the child is a session of its own: `subagent_spawned` and
  `subagent_state_update` on the parent, the child's updates routed by `params.sessionId`, **and the spawning call removed from the parent's stream** (Daoris's console2 record of
  0.79.0). **A host should rely on the stamp reading** unless it keeps a surface per child, which is a host's choice the library offers as AG52 and does not make for it.
- **Part 481's frames.** The row asks whether an `allowed` frame can follow a `rejected` one in a turn. Daoris's notes hold 117 `allowed` frames on ordinary turns and a rejection read from
  source, so neither order is shown yet; the row waits for Daoris's recorded frames.

### 12.2 The release (A-LQ1)

Every entry for the layer is under `## Unreleased`, and the tree is ready for the owner to cut it: `doctor` is green against 3.5.3, the changelog's `### Breaking` entries disclose their actions (**D161**), and a
disclosed break ships in a minor (**D18**), so the owner starts the release workflow with `bump` set to `minor` and the pipeline decides the number. **Nothing here names it or stamps it** (**D19**): the
`## Unreleased` heading, `VersionPrefix` and the notes are the workflow's. After it publishes, §9 of this study stops being true (no version carries the layer, the host's local pack and its condition go), and
the steward then rewrites that section to say which release carries what. Daoris's own pin and its `DaorisLyntaiLayer` condition move when it reads that release; that is its repository's change.
