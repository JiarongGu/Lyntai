# LLM & router internals

The load-bearing correctness rules for `Lyntai.Core/Llm/**`. These are invariants — tests pass while
subtly violating them, so hold them in mind when touching the router or a provider. Reference:
design spec §6 (amended 2026-07-17).

## Verdict taxonomy (`LlmVerdict`)

One enum drives all router behavior. Classify through the **one** `LlmVerdictClassifier` (typed HTTP
status wins over text; text heuristics are deliberately conservative — "429" in a stack frame stays
`Failed`; bare "unauthorized" without auth context stays `Failed` because `AuthFailed` now cools the
host).

| Verdict | Router action |
|---|---|
| `Ok` | success — record host success, return / stream |
| `Failed`, `Timeout` | availability problem — record a failure, **advance** to the next candidate |
| `RateLimited`, `AuthFailed` | terminal for THIS host, transient for the fleet — **cool the host immediately (`MarkDead`) and advance** (a different candidate has a different quota/key) |
| `ContextWindowExceeded` | request too big for THIS model, not a host fault — advance with **no** dead-host penalty |
| `Refused` | content policy follows the prompt, not the host — **surface as-is, never fall back** |

The §6 amendment: `RateLimited` used to circuit-break the whole request; it now cools-and-advances.
`AuthFailed`/`ContextWindowExceeded` are the finer taxonomy added in v0.2.0. If you touch this table,
update the `ILlmRouter` XML doc too — it's the contract a consumer reads.

## Fallback (`LlmRouter.CompleteAsync`)

Dedup candidates by `(providerId, model)` (first wins — a mis-ordered list that re-prepends the primary
won't retry it), then try in order, skipping providers that are unregistered / `!IsAvailable` / in
dead-host cooldown. Log every attempt with provider + verdict + detail. Return the last reply if all
candidates are exhausted; a `Failed` "no live candidate" reply if none were even eligible.

## Routing recipes

- **Single-provider adopter who wants a 429 to hard-stop** (protect the quota window instead of
  cool-and-advance — e.g. Sonora): the default maps `RateLimited → CooldownAndAdvance`, and with a lone
  candidate `ExemptSoleCandidate=true` (the default) even *retries* the cooled sole host. To surface the
  429 to the caller immediately with no cooldown/retry:
  ```csharp
  builder.ConfigureRouting(p => p.On(LlmVerdict.RateLimited, FallbackAction.Surface));
  ```
  `Surface` returns the reply as-is (no host penalty, no fallback), so the app sees `RateLimited` and can
  back off on its own schedule. (Leave `ExemptSoleCandidate` alone — it only matters for cooldown/advance
  actions, which `Surface` no longer triggers.)
- **Caller-supplied refusal check on the reply text**: set `LlmRequest.RefusalPattern` (a case-insensitive
  regex, e.g. a per-language "I can't help with that"). An otherwise-`Ok` reply whose text matches is
  surfaced as `Refused` (no fallback). Applied by `RefusalScreeningLlmClient` — the always-on OUTERMOST
  front-door layer (above the response cache), so a cached hit is re-screened too. Completion-path only
  (streaming isn't screened); a malformed pattern is logged and ignored (fail-open).

## Streaming (`LlmRouter.StreamAsync`) — two invariants

1. **No fallback after the first content token.** Once a *real* content chunk is yielded (`committed`),
   every later chunk — including an `Error` — passes through unchanged and the stream ends; no second
   candidate is tried. Falling back mid-stream would duplicate tokens. Only a **pre-content** failure
   advances.
2. **Only real content commits.** The commit gate is `Kind == Content && Text.Length > 0`. An
   empty/role-only first chunk must NOT commit (the router is the trust boundary; a third-party provider
   might yield one). A `Refused` pre-content error surfaces without fallback, same as non-streaming.

## Provider streaming timeout = inactivity clock

A stream's timeout must measure **provider inactivity**, not wall-clock or consumer dwell. Arm
`CancelAfter(ProviderTimeout)` immediately before each read; `CancelAfter(Timeout.InfiniteTimeSpan)`
immediately after it returns (while you and the consumer process the chunk). A single `CancelAfter` over
the whole enumeration kills slow-but-healthy streams — this bug shipped in two providers and was fixed;
don't reintroduce it. Canonical shape: `ProcessRunner.StreamLinesAsync`.

## Dead-host tracker

Per **provider id** (not per model — that granularity is a roadmap item). N consecutive failures →
dead for a cooldown window; any success resets. Clock is injected (`Func<DateTimeOffset>`), never
`DateTime.Now`, so it's deterministically testable. All state access is under a lock. `MarkDead` benches
immediately (RateLimited/AuthFailed); `RecordFailure` counts toward the threshold (Failed/Timeout).

## CLI hygiene (`ProcessRunner`)

`UseShellExecute=false`; `ArgumentList` only (never a shell — prompts carry newlines/metacharacters);
dynamic content (the prompt) travels via **stdin**, never argv; BOM-less UTF-8 on stdin, stdout, stderr;
resolved-path cache (`where.exe`/`which`, prefer `.cmd`/`.exe`); `Kill(entireProcessTree:true)` on
cancel/timeout. **Both** paths measure child **inactivity**, never wall-clock: the buffered `RunAsync`
reads stdout in chunks and re-arms `timeout` on each (stdin written concurrently, its clock re-armed too),
so a slow-but-alive turn finishes while a child gone SILENT for the window is killed — matching
`StreamLinesAsync`. The buffered path also takes an absolute `maxDuration` backstop (a child that never
stalls but never finishes) and reports `ProcessResult.TimeoutKind` = `Inactivity` vs `MaxDuration` so the
two are distinguishable; `ClaudeCliProvider` passes the resolved timeout as the window and
`MaxProviderTimeout` as the backstop. Do NOT reintroduce a single wall-clock `CancelAfter` over the whole
buffered call — it kills healthy slow turns (the streaming-timeout trap, same failure mode). Tests stub the
CLI via `LYNTAI_PROVIDER_CMD`.

## Front door (`ILlmClient` / `LlmClient`)

To a consumer, Lyntai behaves like **one** provider: `ILlmClient` wraps the router with the default
candidate list so callers don't thread candidates through. New consumer-facing surface (structured
output, etc.) hangs off the front door, not the raw router. `AsChatClient()` is the reverse bridge
(Lyntai consumed *as* an MEAI `IChatClient`).
