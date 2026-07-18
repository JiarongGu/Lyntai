# 2026-07-19 — Agent-session primitive: a generic self-driving-agent loop

> Design for Part 6 of `tasks.md` ("Agentic CLI session"), reworked so the primitive is **generic**
> (interface + event model in Core; the `claude` CLI is adapter #1), not a claude-CLI-shaped surface.
> Read `docs/2026-07-17-lyntai-design.md` (§6 CLI hygiene, §9 platform kit) first — this extends it.

## 1. Context — the gap, and why it is a new primitive

An adopter's **interactive two-gate chat** — plan (read-only) → human approve → execute (write,
scope-guarded) → human review diff → commit — lets **the agent drive its OWN tool loop** (many tool
turns inside one `claude -p`) against the app's out-of-process MCP server + a scope-guard hooks file,
and needs a **rich streamed event surface + session resume across the human gate**.

Lyntai already drives an LLM two ways, and **neither fits**:

- `ILlmProvider` / `ClaudeCliProvider` — one-shot text reply from a *neutral* cwd. No tool transcript,
  no session id, no resume, no gate.
- `IToolLoop` / `ChatOrchestrator` — **Lyntai** orchestrates a ReAct loop over registered `ITool`s
  (in-proc; the tool calls come back to *us*). The opposite control model from "the agent runs its own
  loop out-of-process and we observe it."

So Part 6 is a genuinely new capability. The question this doc settles is **where its abstraction line
sits** so it serves the driving consumer *and* generalizes.

## 2. Decision — a Core primitive, `claude` as adapter #1

Part 6 as first written put the *interface* (`IClaudeAgentSession`), the event model, the options, and
the arg-builder all **inside** `Lyntai.Providers.ClaudeCli`, named after `claude -p`. That inverts the
repo's load-bearing convention — **interface in Core, impl in adapter** (`ILlmProvider`, the storage
domains, `IScorer` all follow it) — and, by its own words, misplaces "the CLI-shaped counterpart to
`Agents.ToolStep`" (which lives in **Core** `Lyntai.Agents`).

**Decision:** the neutral, reusable parts go to **Core (`Lyntai.Agents`)**; only genuinely
provider-specific parts stay in the adapter.

| Concern | Neutral? | Home |
|---|---|---|
| Event vocabulary (`AgentStreamEvent`) | yes | Core `Lyntai.Agents` |
| Tool policy (read-only vs write) | yes | Core (`AgentToolPolicy`) |
| Resume token | yes (opaque string) | Core (on `AgentSessionOptions`) |
| Session interface + result | yes | Core (`IAgentSession`, `AgentSessionResult`) |
| Model / timeout / prompt / disallowed-tools | yes | Core (`AgentSessionOptions`) |
| `--settings` hooks file, `--mcp-config`, `--allowedTools`, `acceptEdits`, `--include-partial-messages` | **no** | Adapter (`ClaudeAgentOptions`, `ClaudeAgentArgs`) |
| stream-json parsing | **no** | Adapter (`StreamJsonParser`) |

This **dominates "neutral events only"**: the same code is written either way; the delta is *where the
interface + records live and what they're named*. Putting them in Core costs ~nothing now and saves a
breaking rename when adapter #2 lands, while the adopter codes against a neutral `IAgentSession` today.

It **avoids "fully generic"**: Core commits only to what is provably neutral. Every claude flag stays in
the adapter, where it **cannot be wrong for provider #2**. And it does **not** fold in `IToolLoop`
(see §3).

## 3. The boundary — `IAgentSession` is not `IToolLoop`

Two Core abstractions, deliberately separate; they answer different questions:

- **`IToolLoop`** — *Lyntai drives* the loop over registered `ITool`s. Tool calls are handed back to us
  in-proc; we execute them and feed observations back. Provider-agnostic via native-or-prompt protocol.
- **`IAgentSession`** — *the external agent drives its own loop* out-of-process. We spawn it, stream its
  transcript, gate it read-only vs write, and resume it across a human gate. It executes its own tools
  (built-ins + an app-hosted MCP server); they never come back to us as calls-to-execute.

The G3 README section ("CLI-agent session **vs** `IToolLoop`") therefore documents a real boundary, not
an overlap. §6's stress-test confirms the line is correct: the moment a provider *hands a tool call back
to the caller to execute*, that is `IToolLoop`'s job, not `IAgentSession`'s.

## 4. Core surface — additions to `Lyntai.Agents`

### 4.1 `AgentStreamEvent` — the event model (G1, Core half)

A sealed hierarchy (richer than a nullable-bag record; consumers `switch` on type):

```csharp
namespace Lyntai.Agents;

public abstract record AgentStreamEvent;

public sealed record SessionStarted(string SessionId) : AgentStreamEvent;
public sealed record TextDelta(string Text)           : AgentStreamEvent;
public sealed record Thinking(string Text)            : AgentStreamEvent;
public sealed record ToolCall(string Name, string ArgumentsJson, string? CallId = null) : AgentStreamEvent;
public sealed record ToolResult(string? CallId, string Content, bool IsError) : AgentStreamEvent;

// RAW token counts (not LlmUsage) — the app prices from its OWN table, so it needs cacheCreate + the
// ACTUAL model id, neither of which LlmUsage carries (it is the priced/cost path). Deliberate divergence.
public sealed record UsageLive(long Input, long Output, long CacheRead) : AgentStreamEvent;
public sealed record UsageFinal(long Input, long Output, long CacheRead, long CacheCreate, string? Model)
    : AgentStreamEvent;

// ONE terminal event (folds the task's separate Done + Error): Verdict != Ok ⇒ errored. A no-output run
// is diagnosable (auth / rate-limit / turn-limit), never silent — Diagnostic carries the provider detail
// (the CLI adapter packs its stderr tail here; an API adapter packs its error body — see §6).
public sealed record SessionEnded(
    LlmVerdict Verdict, bool IsError, string? Subtype, string? SessionId,
    string? FinalText, string? Diagnostic) : AgentStreamEvent;
```

Notes that fell out of the §6 stress-test (each keeps Core neutral):

- **No `filePath` on `ToolCall`.** `file_path` is claude's Edit/Write/NotebookEdit *tool schema*, not a
  universal concept. Core carries `ArgumentsJson`; the adapter ships a convenience
  `ClaudeToolCalls.FilePathOf(evt)` and the app's `EditTracker` uses it (still app-owned, as the task's
  "Not gaps" says). `CallId` (claude `tool_use.id` / OpenAI call id) correlates `ToolResult` to its
  `ToolCall` — more robust than positional pairing.
- **`SessionEnded.Diagnostic`, not `stderrTail`.** stderr is process-specific; `Diagnostic` is neutral.
- **`UsageFinal.CacheCreate` tolerates 0.** Not every provider reports a cache-create count.

### 4.2 `AgentToolPolicy`, resume token

```csharp
public enum AgentToolPolicy { ReadOnly, Write } // the two gates; extensible
```

The **resume token is an opaque `string?`** on the options — claude's session id (→ `--resume`), OpenAI's
`previous_response_id`. This is the single strongest neutrality win: one field, every backend's
resume-across-a-gate mechanism.

### 4.3 `AgentSessionOptions` — the neutral per-call inputs (G2, Core half)

```csharp
public record AgentSessionOptions
{
    public required string Prompt { get; init; }          // the user turn; travels over stdin, never argv
    public string? SystemPrompt { get; init; }
    public AgentToolPolicy ToolPolicy { get; init; } = AgentToolPolicy.ReadOnly;
    public string? ResumeToken { get; init; }             // opaque; null = fresh session
    public string? Model { get; init; }
    public int? TimeoutSeconds { get; init; }             // null = the global (reuse C1 per-request timeout)
    public IReadOnlyList<string> DisallowedTools { get; init; } = [];
    public string? WorkingDirectory { get; init; }        // CLI-oriented; see §11 decision 1
}
```

The adapter subtypes this (§5). The interface method takes the base; the claude session pattern-matches
for its extras. A rarely-used third-adapter flag can ride a `ProviderOptions` bag rather than force a new
Core field — but that escape hatch is not built until needed.

### 4.4 `IAgentSession` + `AgentSessionResult` — both consumption doors

Two consumption APIs, mirroring `ILlmProvider.StreamAsync`/`CompleteAsync`, so the primitive serves *both*
a live-transcript UI and a headless/batch caller:

```csharp
public interface IAgentSession
{
    // Streaming door — live transcript; cancels cleanly (disposal kills the process tree, §5).
    // Adapters implement ONLY this.
    IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentSessionOptions options, CancellationToken ct = default);
}

public static class AgentSessionExtensions
{
    // Result door — headless/batch callers; optional per-event callback for logging/tracing. Iterates
    // StreamAsync once and folds it (SessionId ← SessionStarted; Verdict/FinalText/Subtype/Diagnostic ←
    // SessionEnded; Usage ← last UsageFinal). The fold lives here ONCE — DRY across all adapters.
    public static async Task<AgentSessionResult> RunAsync(
        this IAgentSession session, AgentSessionOptions options,
        Action<AgentStreamEvent>? onEvent = null, CancellationToken ct = default) { /* fold */ }
}

public sealed record AgentSessionResult(
    string? SessionId, string FinalText, LlmVerdict Verdict, bool IsError,
    string? Subtype, string? Diagnostic, UsageFinal? Usage);
```

`StreamAsync` is the one primitive adapters implement — idiomatic, composable, natural for a live
transcript. `RunAsync` is an **extension** that folds the stream to `AgentSessionResult` (with an optional
`onEvent` callback), so both doors are first-class while the fold is written once, not per adapter.

**Front-door note (memory: "Lyntai behaves like a provider").** `IAgentSession` is a *second* front door,
sanctioned and distinct from the `ILlmClient` completion front door — a self-driving gated loop is a
different shape from a completion. It correctly sits **outside** the router: there is no cross-provider
fallback *mid* agent-loop (you can't fail a half-written plan over to a different backend), so routing /
fallback do not — and must not — apply here. That is consistent with the front-door principle, not a
breach of it.

## 5. Adapter surface — `Lyntai.Providers.ClaudeCli` (G1/G2, adapter half)

```csharp
public sealed record ClaudeAgentOptions : AgentSessionOptions
{
    public string? SettingsPath { get; init; }             // --settings: the scope-guard hooks file (PreToolUse jail)
    public string? McpConfigPath { get; init; }            // --mcp-config: the APP's out-of-process MCP server
    public IReadOnlyList<string> AllowedTools { get; init; } = []; // --allowedTools for that server
}
```

- **`ClaudeAgentArgs.Build(ClaudeAgentOptions)`** — generalizes the reference `ClaudeCliRunner.BuildArgs`:
  `-p --output-format stream-json --verbose --include-partial-messages`; `ToolPolicy.ReadOnly` ⇒
  `--disallowed-tools Edit,Write,NotebookEdit` (+ the caller set, default also `AskUserQuestion`
  /`ExitPlanMode`/`EnterPlanMode` — flow tools that hang a headless run); `ToolPolicy.Write` ⇒
  `--permission-mode acceptEdits`; `--settings`, `--mcp-config`/`--allowedTools`, `--resume`, `--model`
  forwarded verbatim. Prompt over **stdin only**, never argv.
- **`ClaudeAgentSession : IAgentSession`** — runs ONE `claude -p` turn over `IProcessRunner` (kill-tree on
  cancel; the `CLAUDE_CMD`/`LYNTAI_PROVIDER_CMD` stub seam keeps tests token-free), stream-json → §4.1
  events, prompt written on a **background task** so a large prompt can't deadlock the stdout drain,
  bounded stderr tail into `SessionEnded.Diagnostic`, final result classified via `LlmVerdictClassifier`.
- **`WorkingDirectory` is required here and per-call** — the deliberate inverse of
  `ClaudeCliProvider.NeutralWorkingDirectory`: the interactive gate *is* the product, so the CLI must run
  in the app's project root to load its `CLAUDE.md`/knowledge. Never the hardcoded neutral cwd.
- **Composes with, does not replace, `ICliToolProvisioner`.** `--mcp-config` points at the app's own
  out-of-process server; the in-proc `AddClaudeCliMcpTools` provisioner path is a separate, orthogonal way
  to expose Lyntai-hosted `ITool`s. Both can be present.
- **`StreamJsonParser` extension** — emit the §4.1 events from the fuller stream-json (`system`/init →
  `SessionStarted`; `stream_event` `content_block_delta` → `TextDelta`/`Thinking`; `assistant` tool_use →
  `ToolCall`, `message.model` + per-turn usage → `UsageLive`; `user` tool_result → `ToolResult`; `result`
  `subtype`/`is_error` → `SessionEnded`). With `--include-partial-messages` on, text comes from the
  partial deltas; the consolidated `assistant` block is **not** re-emitted as text (drives `UsageLive`
  only) to avoid double-counting. **Leave the existing `StreamJsonEvent` → text path and `LlmChunk`
  mapping unchanged** — no `ILlmProvider` regression; factor the extraction so the provider still
  collapses to text.
- **`AddClaudeCliAgentSession()`** on `LyntaiBuilder` (resolve `IProcessRunner` + `LyntaiOptions` +
  logger; honor the stub env), mirroring `AddClaudeCliProvider`.

## 6. Neutrality stress-test — mapping OpenAI Responses API onto `IAgentSession`

The point of this section: prove the Core surface is neutral, not accidentally claude-shaped. A
hypothetical `Lyntai.Providers.OpenAiResponses` adapter driving the **Responses API** with **hosted
tools** (hosted MCP, `web_search`, `code_interpreter` — the server runs the loop and streams it back,
the true analog of claude CLI's own loop):

| Core concept | claude CLI | OpenAI Responses | Verdict |
|---|---|---|---|
| `SessionStarted` | `system`/init `session_id` | first `response.created` id | ✅ |
| `TextDelta` | `content_block_delta` text | `response.output_text.delta` | ✅ |
| `Thinking` | `thinking_delta` | reasoning-summary delta | ✅ |
| `ToolCall` | `assistant` tool_use | `response.*_call` (web_search / mcp) | ✅ |
| `ToolResult` | `user` tool_result | hosted-tool result event | ✅ |
| `UsageFinal` (raw + model) | `result.usage` + `message.model` | `response.completed.usage` + `model` | ✅ (`CacheCreate`→0) |
| `ResumeToken` (opaque) | session id → `--resume` | `previous_response_id` | ✅ |
| `ToolPolicy` Read/Write | disallow Edit/Write vs `acceptEdits` | expose read-only vs write hosted tools | ✅ |
| `SessionEnded.Diagnostic` | stderr tail | error body / `incomplete` reason | ✅ |
| `WorkingDirectory` | cwd = project root | **n/a** (no filesystem) | ⚠ §11 |

**What the stress-test *found* (and already fixed above):** drop `ToolCall.filePath`; rename
`Done.stderrTail` → `SessionEnded.Diagnostic`; keep `UsageFinal` raw with `CacheCreate` tolerating 0; add
`CallId`. **What it confirmed:** a Responses adapter fits **only** when tools are hosted server-side.
The instant it uses a *function tool handed back to the caller to execute*, that is `IToolLoop`, not
`IAgentSession` — exactly the §3 boundary. The one non-fit is `WorkingDirectory` (§11).

## 7. What stays app-owned (unchanged from the task)

The two-gate **orchestration** state machine (plan→approve→execute→review→commit), the scope-guard hook
**content** (jail policy/script + `GUARD_VERSION`), the app's **MCP server + tool registry**,
**edit-tracking → git stage/diff/commit** (`EditTracker` consumes `ToolCall` args), the **SSE bridge** +
the app's event wire shape, and **model-pricing tables** all stay in the adopting app. Lyntai ships the
**primitive** — spawn + gate flags + rich streamed events + resume + diagnosable termination.

## 8. Testing (extends G1/G2/G3 tests)

- **Core (fakes, no I/O):** `AgentToolPolicy` → arg mapping via `ClaudeAgentArgs`; event-model pattern
  exhaustiveness; `AgentSessionResult` folded from a synthetic event stream.
- **`StreamJsonParser` (captured fixture lines):** `system`/init → `SessionStarted` with the id; tool_use
  block → `ToolCall` with name (+ args); partial `text_delta`/`thinking_delta` → `TextDelta`/`Thinking`;
  `result` `is_error:true, subtype:"error_max_turns"` → `SessionEnded` carrying both; raw counts + model
  on `UsageFinal`; a malformed line still ignored (no throw); **the existing provider text path still
  collapses correctly** (regression guard).
- **`ClaudeAgentSession` (against the stub):** read-only argv denies write tools + omits `acceptEdits`;
  write argv adds `--permission-mode acceptEdits`; `SettingsPath`/`McpConfigPath`/`AllowedTools`
  /`ResumeToken` all land in argv; the prompt never appears in argv; stubbed init → the result
  `SessionId`; a tool_use transcript → ordered `ToolCall`/`ToolResult` then `SessionEnded`; an empty run →
  `SessionEnded` with subtype + `Diagnostic`; cancel mid-stream kills the process tree.
- **Stub + e2e (G3):** extend `provider-stub.mjs` with a marker emitting a deterministic multi-tool
  agentic transcript (init → assistant text + tool_use → user tool_result → result); a `pN.mjs` that runs
  a read-only then a **resumed write** session and asserts the event sequence + the resume.
- **API baseline (memory: "Lyntai API baseline"):** new public surface in Core *and* the adapter — update
  both checked-in baselines deliberately.

## 9. Revised task shape (supersedes Part 6's G1/G2 split)

- **G1a (Core):** `AgentStreamEvent` hierarchy + `AgentToolPolicy` in `Lyntai.Agents`.
- **G1b (Adapter):** extend `StreamJsonParser` to emit them; provider text path unchanged.
- **G2a (Core):** `IAgentSession`, `AgentSessionOptions`, `AgentSessionResult`.
- **G2b (Adapter):** `ClaudeAgentOptions`, `ClaudeAgentArgs.Build`, `ClaudeAgentSession`,
  `AddClaudeCliAgentSession`.
- **G3:** DI + stub transcript + `pN.mjs` e2e + README ("CLI-agent session vs `IToolLoop`", + the
  Core/adapter split). Should-have, unchanged.

Blockers stay G1+G2 (now a+b each); G3 should-have.

## 10. Non-goals / YAGNI

No second adapter is built now (§6 is a paper stress-test, not code). No `CliAgentSessionOptions` mid-layer
and no `ProviderOptions` bag until a real adapter #2 needs them. `IToolLoop` is not touched. `LlmChunk` and
the `ILlmProvider` text mapping are not touched.

## 11. Resolved decisions

Reviewed and settled (2026-07-19); recorded here so they are not re-litigated during implementation.

1. **`WorkingDirectory` lives on the base `AgentSessionOptions`** — documented as "CLI-agent adapters run
   the loop here; adapters without a filesystem context ignore it." It is provably neutral only across
   *CLI* agents (an API agent has no cwd), but every realistic near-term adapter (claude/Codex/Gemini CLI)
   is a CLI agent that needs it, it is the single most load-bearing option in the task, and demoting it to
   a subtype for a speculative API adapter is the tail wagging the dog. **Revisit** (extract a
   `CliAgentSessionOptions` mid-layer) only if/when CLI-agent #2 shows `WorkingDirectory` + `SettingsPath`
   recurring together.
2. **Ship the adapter helper `ClaudeToolCalls.FilePathOf(evt)`** — Core stays clean (`ToolCall` carries
   only `ArgumentsJson`, §4.1); the adapter provides the convenience because the driving consumer's
   `EditTracker` wants it. Cheap, and it keeps claude's `file_path` tool-schema knowledge in the adapter
   where it belongs.
3. **One terminal `SessionEnded` keyed on `Verdict`** (folding the task's separate `Done` + `Error`) —
   simpler, neutral, and a no-output run is still fully diagnosable via `Verdict`/`Subtype`/`Diagnostic`.
