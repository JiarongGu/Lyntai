# The agent-provider layer — a design for the owner

**Status: a proposal; built so far are the failure of §5.3 on the two CLI doors (AG1), the event additions of
§5.2 (AG2), the provider description and registry of §5.1 (AG3), the bounded retry of §5.3 (AG4), the account's
reach into a door (AG5, §5.4's first bullet), the pool and its restriction reducer of §5.4 (AG6) and the transcript, brief and hand-over of
§5.5 (AG7), all in `docs/guide.md` §CLI-agent session, whose names stand as built; the ACP door (AG8) and the usage
reading (AG9) are not.** The decision is **D219** (`docs/DECISIONS.md`); the open build rows are `TASKS.md` Part 376
and the closed ones `docs/task-archive.md` Parts 381, 382, 384 and 385. This is a study (`docs/documents.md`): it is
read for its reasoning, and what it settles
moves into the contract documents as each row lands. Every type named below is a PROPOSED name, to be
confirmed by the row that builds it against the `ApiSurface` baselines. The sketches are plain text because
none of them compiles yet.

## 1. The need, and why it is general

A .NET application that runs coding agents (Claude Code, Codex, others) through their CLIs or the Agent Client
Protocol needs five things beyond spawning a process: **one contract** every agent implements; **one event
stream** read the same way from every door; **accounts as a pool** with limits, resets and rotation; **one
error policy**; and **context that travels** when a conversation goes on with another agent or account.

Lyntai already holds the seam under all five (`IAgentSession`, D35, D38, D190), and a first-party application
other than the one that asked consumes it today. The asker built the rest inside a desktop driver. That is the
signal `.agents/knowledge/generic-library.md` asks for, and its first test is whether the sentence survives
with the asker's name struck: *an application that runs agents needs a pool, a failure policy and a hand-over
brief that do not depend on which agent or which UI it has.* It does. **Orchestration does not** — which
quest an agent works, how its work lands, who reviews it and what the person sees stay the host's (§8).

## 2. The evidence, and how far it can be trusted

Daoris is public, and its designs are the evidence. **Their status differs, and the difference matters here**:
a design that was built and run is evidence of what was hard; a design nobody has built is evidence of what
its authors expect to be hard, read by two independent makers.

| Need | Daoris's record | Status there | What was hard |
|---|---|---|---|
| One contract | `2026-10-26-agent-provider-design.md`, `2026-10-27-provider-infrastructure-design.md` (D213) | accepted over four read rounds; **nothing built** | 350 agent-specific branches in 234 files. A fact belongs to the agent, the door or the account, and a plugin that joins a provider made the account key ambiguous. Every fact needed its source and where it applies |
| Event stream | the ACP session and the stream-json readers; `2026-09-25-stream-json-evidence.md` | built, measured | The same end reads three ways: Claude's native result says `success` with `is_error: true`; Codex's ACP adapter ends a failed turn `end_turn` and says the error as agent text unless the client declares typed failures; a woken turn and a late tool update look alike |
| Accounts | D197 (one pool), D205 (ordered choices), D206 (limit resume); `2026-10-02-limit-signals-evidence.md` | built; the restriction model in the D213 amendment is designed only | A limit may state no reset (Daoris defaults 60 minutes, a policy and not a fact); "out of credits" waits for a person; several credentials may share one allowance; usage is per-turn or cumulative and absent is not zero |
| One error policy | D210, `2026-10-24-agent-errors-design.md` | rows 1a and 1a2 built; three read rounds | A 529 mid-turn read as a clean end; a model quoting "529 Overloaded" must not stop its own turn, so only the door's own failure fields classify; a word once on the wire is never sent again |
| Context that travels | D163, D189 | built in part | The agent's own conversation cannot be copied, so a move hands a brief; it is a pure function of Daoris's record, bounded, the person's words first, and no model writes it |

What Daoris did NOT settle, and this design therefore leaves open: any account pool across processes, a hosted
or HTTP agent, and the restriction reducer's behaviour on a real shared allowance.

## 3. What Lyntai has, against the five

| Need | Exists | Missing |
|---|---|---|
| Contract | `IAgentSession` with a neutral `AgentSessionOptions`; Claude and Codex sessions keyed by id; adapter-local options; `CliProviderEngine`'s shared rules (D21); the honest-subset rule (D35) | a description of a provider (its doors and what each can do, with a source for each claim); a registry; a conformance suite; an ACP door |
| Events | `AgentStreamEvent`: session start, text, thinking, tool call and result, live and final usage, one terminal event | a plan event; a usage reading that can say *unknown* (`UsageFinal` carries non-nullable counts, so a count a door never reports reads 0); a statement that the terminal event ends the TURN |
| Accounts | nothing. The router cools a downed host by provider or by model; `ProviderKey` changes when a credential rotates; D20 says Lyntai drives a backend's maintenance and never owns its credentials | the account, the pool, a restriction with its recovery, usage readings, rotation |
| Errors | `ProviderVerdict` and the one `ProviderVerdictClassifier`; commit `84a17f99` already took the Codex exhaustion sentence | transient apart from fatal (both are `Failed`); a reset or retry-not-before fact; a delivery disposition; a bounded retry for a session, which has no router to advance it |
| Context | an opaque, validated `ResumeToken`; a `SystemPrompt` whose position is the adapter's | a transcript, a brief and a hand-over decision |

## 4. Where it belongs

**In the existing packages, layered by what each part is, and no new package in the first slice.**

- **Contracts and the pure policy go in `Lyntai.Core`, namespace `Lyntai.Agents`**: the event additions, the
  provider description, the failure kinds and the restriction model, the pool's reducer, the transcript and
  the brief. They have no dependency, Core already owns `IAgentSession` and `ProviderVerdict`, and the failure
  reader must call the ONE classifier (`.agents/knowledge/llm-and-router.md`: classify through it).
- **The doors go in `Lyntai.Providers.Basic`**: the two CLI sessions that exist, and an ACP door written on
  `System.Text.Json` beside them. Nothing in them isolates a dependency, which is D25's only test for a
  package.
- **The pool's state is kept in the existing key-value store** under a documented key prefix, the way D192 kept
  schedules. A thirteenth storage domain would put a migration on every backend and make any later mistake a
  major-only break (D18).

**Why not a new package.** (1) D25 and D27 split by the dependency a boundary isolates, and none does here.
(2) The shelter a preview package would give is not available: D70 withdrew the only exemption, and one
`VersionPrefix` stamps every package, so a new package is under the full promise from its first stable
version. (3) A pool package would need the CLI doors that live in Basic; Core would then hold the contracts
anyway, or the package would reference an adapter, which `.claude/rules/dotnet-package-layout.md` forbids.
(4) The RFC's second axis for a package (an opt-in capability earning one) is ruled yes within a bound (**D221**):
it needs the whole promotion gate, and this design waits on none of it. Nothing moves now. A later `Lyntai.Agents`
package taking the pool, the brief and the ACP door would need two applications of different kinds reusing the
contract, and proof of binary compatibility, since a namespace that does not change does not guarantee it.

**The risk taken, stated.** A published member is frozen (D70). The pool and the restriction model come from a
design nobody has built, so they are the least certain surface. The rows are therefore ordered so the
certain parts ship first and are used by a consumer before the uncertain ones are published (§9), the records
are sealed with init members so a field is additive, and D18's relaxation (a disclosed break in a minor)
holds while every consumer is first-party.

## 5. The contract

### 5.1 One provider contract

A provider is described by one record and one port:

```text
AgentProviderDescriptor
  Id                      the id the session is keyed by today ("claude-cli", "codex-cli")
  Doors[]                 Id, Kind ("cli", "acp", or a host's own), Abilities[]
  Abilities[]             Name, Support (supported | unsupported | unknown),
                          Source (declared | negotiated | measured), Evidence, the version it applies to
  Accounts                the variable the door reads an account's configuration home from, if any
IAgentProvider            Describe() and the door's IAgentSession
IAgentFailureReader       a door's reading of its own failure (§5.3), required
IAgentUsageSource         OPTIONAL, discovered with `is`, as D203 does for a store capability
```

- **As built (AG3):** `AgentProviderDescriptor` holds the id and `AgentDoor`s; each door holds `AgentAbility`
  claims (`Name`, `Support`, `Source`, `Evidence`, `AppliesTo`). `IAgentFailureReader` is not built, since the two
  CLI doors read their failure in their own readers. `IAgentProvider` is `Describe()` and `Open(doorId)`;
  `AgentProvider` wraps a door's session with the refusal, and `AgentProviderRegistry` resolves by id.
- **As built (AG5):** `Accounts` is an `AgentAccountMapping` (`HomeVariable`) on the descriptor, set exactly when
  the doors state the `accounts` ability `Supported`: `CLAUDE_CONFIG_DIR` on claude, `CODEX_HOME` on codex. It is
  on the descriptor, as §5.1's sketch has it, and not on each door: both CLI doors of a provider are one agent
  reading one variable.
- **Registration is a DI collection** of `IAgentProvider`, resolved by id; an unknown id throws and names what
  exists. The existing `Add*CliAgentSession` extensions register the provider beside the keyed session they
  register now, so no consumer changes. Adding an agent is a new class and one registration, never an edit to
  a `switch` (`dotnet-package-layout.md`).
- **The host never branches on a provider id.** It asks the abilities: resume, read-only gate, MCP servers,
  disallowed tools, system prompt, steering, plan events, live usage, typed failures. An option a door cannot
  honour is answered `Unsupported` before anything is spawned, as an unrenderable MCP server already is, and
  never dropped.
- **Unknown is a value.** A door states an ability it has not measured as `unknown` with its source, and
  `.agents/knowledge/claims-need-checks.md` applies: a claim a door makes is held by a test that fails when
  the wire disagrees.

### 5.2 A turn's event stream

`AgentStreamEvent` stays, and **a turn is one `StreamAsync` call, ended by exactly one `SessionEnded`**; the
conversation is the `ResumeToken`. That is how both CLI doors already behave, and an ACP door is built the same
way first (a process per turn), so a long-lived connection with steering is a later optional capability and
not a v1 type. Additions, all additive:

- a **plan** event carrying the agent's plan entries and their statuses;
- a **usage reading** event whose counts are nullable, with a unit, a scope (this turn or cumulative) and the
  moment it was measured. The older live and final usage events stay as they are, and the docs say a count
  they never report is 0 only because the type cannot say otherwise;
- a **failure** on the terminal event, as a new init member (§5.3).

The rule for every door: **it emits what its wire carries and nothing it could only infer**, and the ability
table says which events it can produce. A tool step's kind stays provisional and its payload reliable (D35).
Fields one agent has and another does not are adapter-local, as `ClaudeAgentOptions` is.

### 5.3 One error policy

```text
AgentFailure
  Kind            limit | access | transient | fatal
  Restriction?    Cause (quota | rate | credit | concurrency | capacity | unknown)
                  Subject (account | pool | model | endpoint | unknown)
                  Recovery (reset-at T | retry-not-before T | owner-action | unknown)
  Delivery        never-sent | taken | uncertain
  By              the table entry or field that decided it
```

- **A door reads its own failure fields before any sentence** (a JSON-RPC code and its `data`, a result's
  status and error field, Codex's typed error) and a generic `success` label never outranks them. The
  sentences stay in `ProviderVerdictClassifier`, held both ways by a recorded sentence that must read as its
  kind and a near one that must not. Order: limit, then access, then transient; anything unread is fatal.
- **Only the door's own failure classifies.** The agent's prose, a tool's output and a line the agent printed
  never do. A harness's own stderr may name an access failure and nothing else.
- **It maps onto the existing taxonomy and adds no member to it**: limit to `RateLimited`, access to
  `AuthFailed` (or `NotConfigured` with no credentials), transient to `Failed`, fatal as the classifier says.
  A new enum member would move the frozen surface for a distinction a field carries (the `84a17f99`
  reasoning), and `Failed`'s routing action is already the right one for transient.
- **A recovery is a fact the sentence states or it is `unknown`.** Lyntai never invents a deadline. A default
  cool-off for an unstated reset is the HOST's option (`generic-library.md` rule 7: two honest applications
  would answer differently).
- **A transient failure is retried by a decorator, `RetryingAgentSession`, that is off unless registered**
  (built: `AddAgentRetry`, `docs/guide.md` §CLI-agent session):
  bounded, with waits taken from an injected `TimeProvider` and cut short by cancellation. **One layer owns the
  retry budget.** It resends the original prompt only when the delivery was `never-sent`; when it was `taken`
  it resumes the conversation with a continue word if the door can resume; when it was `uncertain` it does
  not retry and says so. A limit, an access failure or a fatal one is never retried.

### 5.4 Accounts as one pool

- **An account** is a host-named configuration home for one provider. A neutral `Account` on
  `AgentSessionOptions` is mapped by each door to its own variable. **Lyntai creates no profile, signs in to
  none and stores no credential** (D20); whether one is signed in is the door's existing `IProviderAuth` probe.
  **As built (AG5):** `AgentAccount` is a `Provider` (the registered id), a `Name` and a `Home`, all strings and none
  a secret. The door sets the home in its variable on the child for that turn, over the session's own entry for it;
  `AgentProvider` refuses an account of another provider, and each session refuses a blank name or a home with a
  control character, all before a spawn (`docs/guide.md` §CLI-agent session). The library never opens the home
  and does not check that it exists, since a runner may place the child on another machine.
- **The pool** takes an ordered list of choices (provider, model, effort) and the accounts of each, and
  answers the first choice and account that no active restriction forbids, **walking a choice's accounts
  before the next choice**. It is asked once per turn, so **a rotation happens at a turn's end and never
  inside a turn**.
- **Restrictions are a set, never one replaceable cool-off.** A turn is admitted only if every applicable
  restriction permits it. A reset or retry-not-before time permits ONE bounded attempt and is not evidence
  of credit. An owner-action restriction holds until the host clears it by id and revision, one attempt at a
  time. An unknown recovery holds until a host's bounded probe or release. A newer failure is never cleared
  by an older usage reading.
- **A shared allowance exists only where a door supplies the relationship with its evidence.** Another local
  profile name proves nothing, and an unknown pool stays unknown.
- **Usage readings** keep their unit, scope and time, and an absent reading is unknown and never zero. They
  come from the usage events and from the optional `IAgentUsageSource` a door may supply (AG9, bounded by **D222**:
host-enabled, validated per door and version, side effects declared, unknown on any failure).
- **State** is the reducer in process, persisted through the key-value store so a restart keeps its holds.
  Two processes sharing one pool would need an atomic compare-and-set the store does not have; that is out of
  the first slice and recorded as such (§7).
- **Which account serves a turn beyond the ordered walk** (spread, most work, least recently used) is an
  `IAgentAccountPolicy` seam. Only the ordered walk ships.
- **As built (AG6):** `AgentAccountPool` over `AgentPoolOptions.Choices` (`AgentChoice`: provider, model, effort and
  its accounts); `PickAsync` answers an `AgentSeat` once per turn, `ReportAsync` takes the turn's `SessionEnded`
  back, and the rotation therefore lands on the next pick. The reducer (`AgentPoolReducer`, internal and pure) holds
  a set of `HoldRow`s, one per scope and cause. **What a failure holds:** its own `Restriction` (a limit or an access
  failure holds something even when the door stated nothing; a transient or fatal one only when it carries one). The
  subject picks the scope: `Account` the account, `Endpoint` the provider, `Pool` the allowance an
  `IAgentAllowanceSource` showed (else only the account it was seen on, since an unknown extent is not guessed),
  and `Model` or an unstated subject the account and model that met it. **How a hold ends:** a stated moment
  passing, a host's `ClearAsync(id, revision)`, or for an unstated recovery `AgentPoolOptions.UnknownProbeAfter`
  (null by default, since a deadline is the host's); an owner action only by a clear. **Ending is not recovering:**
  a released hold permits one attempt at a time, leased for `AttemptLease`, and only that attempt's success removes
  it; its failure restates the hold one revision up. A success, a clear or a reading names the revision or the time
  it is about, so none can undo a newer failure. A reading (`ReportReadingAsync`, the input AG9 will feed) lifts
  only holds with a stated moment, and only if measured strictly after the hold was taken. State is one key per
  hold under `lyntai:agent-pool:<name>:hold:`, written through, and read back on first use. **Out of scope, and
  stated on the type: two processes sharing one pool**, since the store has no compare-and-set (§7, **D219**'s
  re-open condition). No door implements `IAgentAllowanceSource` yet, so every `Pool` restriction holds one account
  today.

### 5.5 Context that travels

- **A transcript** is what a host records of a conversation: its words with who said them, each turn's last
  agent message, a one-line summary of each turn's tool calls, the last plan. A recorder folds the event
  stream into one so a host need not.
- **A brief is a pure function of a transcript and a bound**, composed without a model: what it is and that it
  is not the agent's memory, the request as first written, every host word verbatim and another agent's as
  its claim, each turn's last message, the plan, host-supplied facts (the library does not read git), and a
  count of what was left out. The person's words come first and the turns newest first when the bound bites.
- **A hand-over** is a decision, not a feature of the pool: the same provider, account and resume ability
  continues with the `ResumeToken`; anything else starts a fresh session whose prompt carries the brief. The
  position of the brief in the prompt is the adapter's, as `SystemPrompt`'s is.
- **Not memory.** No `IMemoryEngine` is involved and no model summarises. A brief is a record's projection and
  expires with the move that used it.
- **As built (AG7):** `AgentTranscriptRecorder` folds a turn (`BeginTurn` with the options, then each event), and
  `RecordingAgentSession` does both around any `IAgentSession`; an `AgentTranscript` holds `TranscriptTurn`s, each
  with an `AgentConversationSource` (provider, door, account — the host's names). `AgentBriefComposer` composes the
  brief and `AgentHandover.Decide` decides the move; `AddAgentBrief` sets the bound. The bound is
  `AgentBriefOptions.MaxCharacters` in Unicode characters, not UTF-16 units, bytes or tokens. **Order of the
  brief:** preamble, the host's words verbatim (the first request, later prompts, standing instructions), host
  facts, one block per turn (outcome, the agent's last message quoted line by line behind `> `, a tool line), the
  last plan, the left-out count. **The bound cuts, first to go first:** tool lines oldest turn first; messages
  oldest turn first, the newest turn's excepted; the plan; the newest turn's message; facts last-listed first.
  Never cut: the preamble, the host's words, the per-turn headers, the count. A part is kept whole or dropped
  whole, and because the count is text a cut that would lengthen the brief is not made: the shortest reachable
  brief comes back with `WithinBound` false. A resume needs a recorded session id, the same provider, door and
  account, and the door stating resume `Supported` (`Unknown` is not promised); every other case is a fresh
  session, and `AgentHandoverDecision.Apply` leads its prompt with the brief, the host's new request after a marker.

## 6. Which agents and doors first

1. **Claude Code CLI and Codex CLI**, the doors that exist, brought to the contract with no new transport. They
   are measured (D35, `docs/task-archive.md` Part 260) and Lyntai already runs them.
2. **A generic ACP door** on JSON-RPC 2.0 over stdio, then the two agents Daoris measured through it
   (`claude-agent-acp`, `codex-acp`), each with its own reading of its `_meta`, error `data` and typed failures.
   Last in the order because it is the largest new surface and the one most dependent on installed adapters.
3. **Any other agent** by a host registering its own `IAgentProvider`. **Hosted or HTTP agents are not in
   scope**: they need an executor, a workspace and a cancellation story nobody has measured.

## 7. How it is tested without a paid API

Zero tokens, as the rest of the suite is (`repo-mechanics.md` §Dev loop).

- **Pure logic** with fakes and no I/O: the failure tables held both ways on recorded sentences; the
  reducer under an injected clock (concurrent restrictions, an owner action, a stale reading, an unknown
  recovery); the brief as goldens; the retry decorator's disposition table.
- **Recorded captures per door**, each marked source-derived or captured on a real door, and neither standing
  for the other.
- **Stand-ins**: the existing deterministic provider and Codex stubs, and one new ACP stand-in under
  `devtools/scripts` that speaks the protocol, including an error with `data`, a usage update, and a limit.
- **A conformance theory** over every registered provider, from one source (D211's shape): each states every
  ability, an unknown one is `unknown`, and an option it cannot honour is refused before a spawn.
- **Fault injection** for delivery: a process dying before any event (never sent), mid-stream (taken), after
  a tool call (uncertain).
- **Live suites** against an installed agent are gated and skip without it; each is one named new skip.

## 8. What it leaves to hosts

Which quest or task an agent works; session trees, branches and how work lands; roles, review and second
opinions; **when** to rotate, wait or hand over, and whether an agent may ask for one; which paid model to use;
sign-in and every credential; where a transcript is kept; the clock the waits run on (the library reports the
earliest moment an account is ready and the host sleeps); notifications and every screen. The layer is what an
orchestrator calls.

## 9. Order of the build

Each row is a lane quest (`daoris.lanes.json`), and the order is the risk order: the parts every consumer
needs and the data already measured first; the parts designed but never built last.

1. **AG1** — failure kinds, restriction facts and delivery on the existing two doors (`platform` + `inference`).
2. **AG2** — event additions and the nullable usage reading (`platform` + `inference`).
3. **AG3** — the provider description, registry and conformance theory (`platform` + `inference`).
4. **AG4** — the retry decorator (`platform`).
5. **AG5** — the account reaching a door (`inference`), then **AG6** the pool with its reducer (`platform`).
6. **AG7** — the transcript and the brief (`platform`).
7. **AG8** — the ACP door and its stand-in (`inference` + `tools`).
8. **AG9** — the optional usage reading (§11, **D222**), after AG1 to AG3; AG1 to AG8 stand without it.

## 10. Rejected

- **A new package now** (§4). **New `ProviderVerdict` members** (§5.3). **Classifying from the agent's own
  message**, which a model can write. **A pool inside the front door**: a session is not bound by it, and the
  remark on `IAgentSession` says why. **A model-written brief**: it adds a call, a cost and an unreviewable
  paraphrase to a move whose point is a faithful record. **A thirteenth storage domain** for the pool.
  **A default-on retry**: whether a deployment may spend a second try is its own to say. **A long-lived
  connection type in v1.** **Copying Daoris's code**: it is read for what was hard.

## 11. For the owner

Three questions; the first two are ruled.

1. **VN1 — a second reason for a package**: yes within a bound (**D221**, `docs/task-archive.md` Part 378). This
   design needed no answer and nothing moves; a later package takes the pool, the brief and the ACP door only
   through the whole promotion gate.
2. **AG9 — may a door read an account's usage by starting the vendor's own server under that account's home?**
   Yes within a bound (**D222**), as its own row after AG1 to AG3. It spawns the vendor binary with that
   account's credentials in its environment (Daoris does this for Codex and Claude), so it is an optional ability a
   host enables and invokes explicitly, supported per door and vendor version only once that version's real
   response is validated, with its side effects declared and unknown on any failure. That it is "the only
   source of a whole-account allowance" is to be tested there, not assumed.
3. **Whether Daoris adopts it** is Daoris's decision, taken in its own records. The layer must stand without
   it, and the first-party consumer of `IAgentSession` today must see no change: every addition is additive.
