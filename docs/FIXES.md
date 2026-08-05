# Fix log

Per-incident records: **symptom → root cause → fix → verification**, plus the commit that introduced the
bug. Newest first. The other two homes, so nothing has to be guessed (`.claude/rules/repo-mechanics.md`
§Fix log): a decision and its reasoning goes to `docs/DECISIONS.md`; the reusable trap a fix revealed goes
to `.claude/knowledge/pitfalls.md`; the release-facing line goes to `CHANGELOG.md`.

---

## 2026-08-05 — a non-zero exit code masked a CLI backend's own account of a failed turn

**Symptom (reported by a consuming app, filed as `TASKS.md` CLI15).** A `codex` turn run against an account
whose login had expired came back as a bare `LlmVerdict.Failed` whose detail was
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

**Fix.** `src/Lyntai.Core/Llm/Cli/CliProviderEngine.cs` — parse stdout first; a reported in-band failure wins
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
