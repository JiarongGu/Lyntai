# The agent-provider layer against Daoris's accepted provider design — a study

**Status: a study (`docs/documents.md`), 2026-10-29; AG20 is built, the rest is not.** It reads Lyntai's layer
(`docs/agent-provider-layer.md`, **D219**, rows AG1 to AG9 closed in `docs/task-archive.md`) against two designs
Daoris has since accepted, and files what a host that runs coding agents still needs from the layer as rows AG10 to
AG20 in `TASKS.md` (Part 412 there) and **D233**. AG20 was ruled and built as **D234**, and §9 states how a host
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
is; what Lyntai **lacks** that is general (a row in `TASKS.md`) or why it files nothing.

### 3.1 Rows a to u

| Row | Lyntai answers | Differs, and whose | Lacks |
|---|---|---|---|
| **a** the contract named, the registry, the census | `AgentProviderDescriptor`, `AgentDoor`, `AgentAbility` (a name, a support, a source: declared, negotiated or measured, evidence, `AppliesTo`), `AgentProviderRegistry` by id, an unknown id throws and names what exists | Daoris's `Known<T>` applies per **eight facts** (door, versions, posture, writable roots, reach, OS, settings digest); `AgentAbility.AppliesTo` is one version. Daoris needs it to ADMIT a measured confinement; Lyntai states `Unknown` and never reads it as a promise, the safe direction. **Not filed**: no consumer measures confinement per posture; it re-opens when one does. The census (no vendor name outside the providers) is a Daoris maintenance gate: Lyntai's vendor vocabulary in Core is the one classifier's sentence tables by **D219**, and `check-api-vocabulary` holds the surface | nothing |
| **b** the shim, the one registration list, the conformance suite, stand-ins discovered | `AddAgentProvider` is a DI collection, so a provider is a class and a registration; `AgentProviderConformanceTests` is a theory over the registry; `provider-stub.mjs`, `codex-stub.mjs`, `acp-stub.mjs` | the theory holds the DESCRIPTOR and the spawn rules; it never drives a stream, so "exactly one terminal", "a failed terminal carries its failure" and "a limit a tool printed never classifies" are held door by door in each door's own tests and by no theory | **AG16** |
| **c** a third stand-in through the contract alone | the suite's own `StandInProvider` (a host-own door, kind `host-own`) registers with no edit to Core | it emits one terminal and nothing a wire carries, so it proves the REGISTRATION and not the vocabulary: a stand-in with a dialect of its own, whose extensions no built-in door reads, is what shows a door is not reading another agent's keys | **AG15**'s proof, **AG16** |
| **d** the owner, once: an account key above the doors | `AgentAccount.Provider` names one provider; an ACP agent is its own provider id, never a second door on a CLI id (**D225**) | Daoris keeps one provider with two doors and resolves the owner once; Lyntai chose two providers so an account, a seat and a hand-over each rest on one door's facts. **D225 named its own re-open condition: a host that needs one account across a CLI door and an ACP door of the same agent**; Daoris's design is that need, designed and not used. **Answered by AG20 as D234**: the doors stay providers and name one `Agent`, which is the account key Daoris's §2.2 describes ("a key resolves to its pool, never to one provider"); doors of one key read the home through one variable, so a plugin door that declares its own variable is a key of its own, as Daoris's §4.1 already has it | built, **D234** |
| **e** the accounts' readers (usage question, floors, windows, limit grammar, sign-in words) | `IAgentUsageSource` with a per-vendor internal dialect (`ClaudeUsageDialect`, `CodexUsageDialect`) and `AgentAllowanceReading` (**D222**); the limit grammar is `ProviderVerdictClassifier` and `StatedRecovery`, one classifier | Daoris splits words into a provider's and a door's tables; Lyntai keeps one classifier a BYO door may call (`ReadAgentFailure`) and builds its own `AgentFailure` where its words differ, so a third provider's reset grammar needs no registry. The **floor** (how often a read may run) is the host's: a read starts the vendor's process, which the ability's `SideEffects` says, and how often to pay it is a deployment's call | nothing |
| **f** the program: install, update, pin, release sources | `ICliBackend` drives the backend's own installer at a version the host names (**D20**) | Daoris owns release sources and a signed download; **D20 refuses exactly that** (Lyntai never fetches, chooses a version or picks a location), so this is the host's | nothing |
| **g** the doors as abilities | `AgentAbilities` (eleven names), `AgentDoor.Unhonourable`, refusal before a spawn | Daoris's `interactive` is a conversation the person types into: the host's, Lyntai has no chat in the layer | nothing |
| **h** the spawn: a recipe, one shell completing it, the launcher | `IProcessRunner` (`StreamLinesAsync`, `ExchangeLinesAsync`): the runner resolves shims and forces UTF-8; a turn's `environment` applies over the inherited one and a null value REMOVES a variable | Daoris's own variables (role, repository, service, git identity) are orchestration: the host's. The seam exists in both directions | **AG17** (an account that is a key) |
| **i** the protocol transport and its dialects | `AcpAgentSession` and `AcpAgentReader` (internal) over the spec's frames | the reader and `AcpFailure` read **both** vendors' extensions inline (`_claude/rateLimit`, `errorKind`, `codexErrorInfo`) by presence, for every agent; there is no place for a third agent's extensions. Daoris's first step is the same composite, then a seam | **AG15** |
| **j** each protocol door its own dialect (a narrowing) | the composite reads for every door | narrowing is a CHANGE of what a door reads; Lyntai keeps the composite as the default and lets the host name the set (AG15), so the narrowing is a host's choice and not a rule | with AG15 |
| **k** the native transport and Claude's native dialect | `CliAgentLoop` is the one per-turn process loop; `StreamJsonAgentReader` and `CodexAgentReader` are the dialects (**D21**); `RetryingAgentSession` is the retry | Daoris's retry is "a structured resume's alone"; Lyntai's is a decorator keyed on the DELIVERY of the prompt: both bounded, one layer owning the budget | nothing |
| **l** the readings and the sink | the event stream IS the sink: `AgentStreamEvent` handed to the consumer; `SessionStarted`, `TextDelta`, `Thinking`, `ToolCall` and `ToolResult` (a `CallId`), `PlanUpdated`, `UsageLive`, `UsageFinal`, `UsageReading` (nullable, unit, scope, `MeasuredAt`), one `SessionEnded` with `Failure` | provenance (reported, observed, computed, inferred) and "replayed" on every reading is not carried: a protocol load's replay is dropped (`AcpAgentSession`), unread frames yield no event. A consumer that must tell inferred from reported has not appeared; **not filed**, the trigger is one that does. Daoris's `WindowsSaid` (windows in the stream) is covered between turns by AG9; surfacing a live window frame waits for a host that rotates on utilization and not on a limit | **a child's and a task's readings** (`ChildSeen`, `ChildSaid`, `ChildEnded`, `TaskStarted`, `TaskEnded`): **AG10**, **AG18**. A held permission request: **AG19**. A model refused: **AG12** |
| **m** the session's life: `AgentRun`, steer, stop-turn, close, the conversation's home | a turn is one `StreamAsync`; the conversation is `AgentSessionOptions.ResumeToken`; a stop is the cancellation token; `steering` is stated `Unsupported` on every door; the hand-over decision is `AgentHandover.Decide` | Daoris keeps a live run with steering for the ACP door (`promptQueueing`, built and measured there). **D219 §10 rejected a long-lived connection in v1 and left it a later optional capability**; the need survives the generic test, but it is a design and not one sitting, so it is an OPTION in **D233** with its trigger and not a row | an option in D233 |
| **n** models | `AgentSessionOptions.Model`, carried on both CLI doors by flag; `AgentChoice.Model`, set on the options by `AgentPick.Apply`; the reported model on `UsageFinal.Model`; effort is the host's word on `AgentChoice` and never interpreted | **the ACP door never reads `AgentSessionOptions.Model`** (no reference to it, to `configOptions` or to `session/set_config_option` under `Acp/`), so a pool pick that names a model on an ACP provider drops it silently, which is the one thing the layer's own rule forbids (an option a door cannot honour is refused, never dropped). And there is no `model` ability to ask. A `model_not_found` reads `Fatal` with no restriction, so the pool re-picks the same seat | **AG11**, **AG12**. The offered LIST (a model picker) waits for a host that renders one; Daoris's two scopes (a session's offers and an account's default) are a settings page's |
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

Each sentence is what a library user would say with the asker's name struck.

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
  conditional item goes.
- **A hosted agent** (a remote job with a workspace snapshot, a broker grant and a cancellation not confirmed by a
  closed socket; the infrastructure design's §3 and §6). **D219 §6 left it out and D233 keeps it out**: Lyntai
  owns no workspace and has no cancellation story for a remote run that a closed connection does not give.
- **The eight-fact applicability, the census, per-event provenance, a live window frame, an offered-model list, a
  per-door `leaves its own servers out` ability.** Each has an application that would answer it differently or has
  no consumer yet; §3 names the trigger.
- **Durable request records across a restart.** The pool persists its holds; a consumer's words and their
  delivery are the host's (§5), and `RetryingAgentSession` is in process.

## 7. Order of the rows

AG20 is built (**D234**); AG10 to AG19 are startable, and this is the order they are built in, one sitting each:

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
the layer is yet carried, so the tiers below are about what a row can still touch:

| Tier | Members | A host may |
|---|---|---|
| Not touched by any open row | `IAgentProvider`, `AgentProvider`, `AgentProviderRegistry`, `AgentProviderDescriptor` with `Agent` and `AccountKey` (**D234**), `AgentDoor`, `AgentAbility`, `AgentAccount`, `AgentSessionOptions`, `IAgentSession` and the event records but `ToolCall` and `ToolResult`, `SessionEnded` with `AgentFailure`, `AgentRestriction` and `AgentDelivery`, `AgentAccountPool` with `AgentChoice`, `AgentPick`, `AgentSeat`, `AgentHold` and `IAgentAccountPolicy`, `AgentHandover`, the brief and transcript types, the retry decorator, `AddAgentProvider` in both forms, the pool, retry, brief and usage-reading registrations, and `agent:` on the three session registrations | build on them as they stand |
| Grown by a row, additively | `ToolCall` and `ToolResult` (a `ParentCallId`, AG10); `AgentAbilities.All` (a `model` ability, AG11, and `permission-requests`, AG19; a provider that states fewer reads `Unknown` for the rest); `AgentAccount` and `AgentAccountMapping` (environment entries and the names a provider reads, AG17, which replaces `AgentAccount`'s test that it holds only a name and a home); new event records (AG18); `AddAcpAgentSession` (a dialect and a callback, AG15 and AG19); `AddHttpAgentSession` (AG14); the HTTP failure reader (AG13) | build on the members that exist, and treat a member a row adds as absent until its row lands |
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
