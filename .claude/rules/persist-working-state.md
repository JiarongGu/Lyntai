<!-- daoris: core/core/persist-working-state.md @ 0.1.0 — canonical; edit via `daoris upstream` -->
---
name: persist-working-state
applies_when: any multi-step task — at each decision, finding, or milestone
enforces: checkpoint in-progress state to its durable home in the repository as you go, not at the end
---

# Persist working state as you go — context is not durable

**Working context is ephemeral. The moment a decision is made, a finding is confirmed, or a milestone is
reached, write it to its durable home in the repository — not at the end of the task, and never only to a
scratch area.**

## Why

A long task's context does not survive intact: it gets summarized, truncated, or simply ends. Anything
held only in working memory — the reason a approach was rejected, the measurement that justified a
constant, the half-finished plan — is gone at exactly the moment the next person or session needs it, and
what remains is code whose motivation nobody can reconstruct.

Writing at the end is the same failure with extra steps. The end is where interruptions land.

## How to apply

- **A decision with a reason → the decisions record.** Especially a decision that was *considered and
  rejected*: without the reason, someone will reverse it later and rediscover the problem.
- **A finding or a trap that survives the task → the knowledge or conventions record**, so it is not
  re-derived next time.
- **In-progress plan state → the plan or backlog file**, updated as steps land rather than after all of
  them do.
- **Write it when it happens.** A note recorded at the moment of the decision costs a sentence; the same
  note reconstructed later costs an investigation, and is usually wrong.
- Durable means *in the repository*, tracked and reviewable — not a scratch directory and not a
  conversation.
