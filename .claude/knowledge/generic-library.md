# Generic-library discipline — generalize the consumer request, never ship its shape

**Lyntai is a reusable, publishable library. Every change must be an app-agnostic improvement behind the
`ILlmClient` front door or a BYO seam — NEVER app-specific code, and never a consumer's domain concept
leaked into the surface.** Most tasks arrive as "app X needs Y"; your job is to ship the *general Y*, not
X's Y.

## Why

The backlog is fed by sibling apps and adopters (a source-study tool, a WinForms AI-manager, …). Those
requests are the best signal we have for real gaps — but each is phrased in the consumer's terms. If we
ship the consumer's shape (its tool names, its file-path argument, its domain vocabulary, its one-off
posture) the library rots into a private fork with public packaging: the next adopter can't use the
feature, the `ApiSurface` baseline accumulates app-specific noise, and Core stops being neutral. The whole
value proposition — "a new project gets this without rebuilding it" — depends on refusing that. This is the
standing rule stated in `CLAUDE.md` and `TASKS.md`; this doc is *how* to satisfy it.

## How to apply

When a task says "app X wants Y," run it through this before writing code:

1. **Name the universal need behind X's request.** "The desktop app had to wrap `ILlmClient` to sum
   `reply.Usage`" → *every* tool-loop consumer wants per-run token accounting → put `Usage` on
   `ToolLoopResult`. Strip the app's name; if the generalized sentence still reads as a real need, it
   belongs in the library. If it only makes sense for that one app, it does **not** — help them do it in
   their code (a BYO seam, a decorator) instead.
2. **Interface in Core, provider-specifics in the adapter.** A neutral need goes on a Core type
   (`ToolLoopResult.Usage`, `ICuratedMemoryStore` params). A need that only means something for one
   backend goes on that adapter's option type, never Core — e.g. `--dangerously-skip-permissions` is a
   `claude` CLI concept, so `SkipAllPermissions` lives on `ClaudeAgentOptions` (adapter), NOT on the
   neutral `AgentSessionOptions`/`AgentToolPolicy` in Core. Ask "would a second provider have this exact
   flag?" — if no, it's adapter-local.
3. **Keep the surface neutral of the consumer's vocabulary.** No app names, no domain nouns, no
   hard-coded scopes/kinds/paths. `AgentStreamEvent.ToolCall` carries `ArgumentsJson` and deliberately
   has *no* `FilePath` property — a file path is one tool's argument schema, not a universal event field;
   the Claude adapter exposes `ClaudeToolCalls.FilePathOf(call)` as an adapter helper instead. Stringly-typed
   escape hatches (a `string→string` `Extra` map, an opaque `ArgumentsJson`) keep Core domain-agnostic —
   prefer them over baking the consumer's fields in.
4. **Additive + defaulted, so nobody else is disturbed.** New parameters take defaults that reproduce
   today's behavior (`dedup: bool = false`, `scope: string? = null`, an optional callback, a nullable
   `Usage`). A "dangerous"/opt-in posture is off by default and documented as such in the XML-doc.
5. **Vary via seams, never `if (appName)`.** The extension model is DI collections + BYO interfaces
   (`IProcessRunner`, `IEmbedder`, `IDbConnectionFactory`, `IToolLoop`). If a consumer needs different
   behavior, the answer is "register your own implementation," not a branch in Core. (See
   `dev-conventions.md` "variation points = DI collections".)
6. **Pin the generality with tests + baseline.** The contract test (e.g. `CuratedMemoryStoreContract`)
   runs across *all* backends so a new param behaves identically on InMemory/SQLite/Postgres. Update the
   `ApiSurface` baseline deliberately — it's the review gate that makes an app-specific leak visible.

### Red flags (stop and re-generalize)

- A public type, member, parameter, enum value, or string constant named after a consumer app, its
  domain, or one of its specific tools/files.
- A Core type gaining a field that only one provider/backend could ever populate (push it to the adapter).
- A `switch`/`if` over a consumer id, tool name, or app mode to pick behavior (make it a seam/collection).
- "It's easier to just take the app's shape" — that's the fork forming. The generalization *is* the work.
- Copying the consumer's whole feature verbatim when the reusable core is a small slice of it.

### Worked examples (the current backlog, all consumer-requested, all generalized)

| Consumer ask (app-specific) | Shipped as (generic) | Where it lives |
|---|---|---|
| WinForms app hangs in headless `claude -p` because every tool prompts | opt-in `SkipAllPermissions` bypass posture | `ClaudeAgentOptions` (adapter) |
| App wrapped `ILlmClient` to sum tokens per tool-loop run | `Usage` on the run result | `ToolLoopResult` (Core) |
| App's "typing" UI needs live tool-loop progress | `IToolLoop.StreamAsync` yielding neutral `AgentStreamEvent`s | Core, mirrors `IAgentSession` |
| App needed a full BYO runner for Windows `.cmd`/CJK | default `ProcessRunner` resolves shims + forces UTF-8 for everyone | Core |
| Source-study tool re-`List`s to dedup a note | `dedup`/`scope` params, defaulted off | `ICuratedMemoryStore` (Core) |

## Related

- `CLAUDE.md` / `TASKS.md` (the standing "this is a generic library" rule this doc expands).
- `.claude/rules/dev-conventions.md` — package layout (interface in Core, impl in adapter, never
  adapter→adapter), variation points = DI collections.
- `.claude/knowledge/extending-lyntai.md` — the four extension seams a generalization usually rides on.
- `docs/2026-07-17-lyntai-design.md` — the `ILlmClient` "behaves like one provider" front door.
- `ApiSurfaceTests` — the baseline gate that surfaces an app-specific leak in review.
