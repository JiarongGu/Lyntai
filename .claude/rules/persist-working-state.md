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
  **But route by KIND first — see below. This line is the one that gets over-applied.**
- **A finding or a trap that survives the task → the knowledge or conventions record**, so it is not
  re-derived next time.
- **In-progress plan state → the plan or backlog file**, updated as steps land rather than after all of
  them do.
- **Write it when it happens.** A note recorded at the moment of the decision costs a sentence; the same
  note reconstructed later costs an investigation, and is usually wrong.
- Durable means *in the repository*, tracked and reviewable — not a scratch directory and not a
  conversation.

## Route by KIND — and the decisions record has the highest bar, not the lowest

**The test for the decisions record: was there a CHOICE between real alternatives, and does it constrain
future work?** Both halves. Work you simply did is not a decision, however much reasoning it took; a fact
you discovered is not a decision, because nobody chose it.

| What you have | Where it goes |
|---|---|
| A choice between alternatives that constrains future work | The decisions record |
| What a pass/sweep/review DID — findings triaged, items closed | The task archive |
| A trap that costs something when forgotten — nobody chose it, you *found* it | The pitfalls record |
| A number you measured | A record under the untracked design-records directory; the CONCLUSION goes to whichever row above fits |
| A convention every task must follow | The rules or knowledge tier |

**Why the bar matters, measured.** The decisions record is the default destination in most people's heads,
and three separate rules point *into* it while nothing said what to keep out. By 2026-08-14 it held 72
entries, of which **23 were created on two consecutive days** — a session-note rate, not a decision rate —
and six announced in their own titles that they were a work session ("the post-1.0 ergonomics *batch*",
"the three calls that *closed the backlog*", "the 3.0 pre-freeze *sweep*"). One entry was a pure trap that
belonged in pitfalls. The cost is not size: it is that a reader looking for *what governs this code* has to
sift a work log to find it, and one subsystem's answer was spread across 22 numbers.

**When it is genuinely borderline, write the RULE and see if it survives the title.** "We decided to ship X"
is a work log. "X, because the alternative Y costs Z" is a decision. If you cannot name the alternative,
it probably was not one.
