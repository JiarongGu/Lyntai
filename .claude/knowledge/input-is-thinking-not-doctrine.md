---
name: input-is-thinking-not-doctrine
applies_when: recording something the owner said into a spec, a decision record, a schema, or a commit
guides: default to recording it as an open working position with its reversal cost, rather than as a settled rule
---
<!-- local: never synced; not a daoris artifact -->

# The owner's input is usually thinking, not doctrine

**A note, not a rule — and itself revisable.** When something the owner says gets written down, the default
status is *working position*, not *decision*. Record the reasoning, the alternative, and what reversing would
cost, and let them promote it when they mean to.

## Why

Lyntai is now large enough that the owner contributes direction more often than rulings — they have said as
much: no one holds every subsystem's constraints at once any more. So what arrives in conversation is usually
thinking-in-progress that wants following through, not a decision to be enforced.

**Measured 2026-08-09, twice inside one conversation.** The remark *"salience does not mean it is always first
priority, it just means it does not decay that easily"* became, in a single turn, both a stated rule in a
design spec and the deletion of a planned database column. Neither was asked for; both were undone. The
correction was *"it not a rule it just my thinking rn"*.

The asymmetry is what makes this worth writing down. A thought recorded as a thought costs one sentence to
promote later. A thought recorded as doctrine costs an investigation to detect and a migration to reverse —
and it usually is *not* detected, because a spec stating a rule confidently reads exactly like a spec that
recorded a real one.

## How to apply

- **Default to `OPEN`.** Capture the reasoning, the road not taken, and the reversal cost. Promotion is
  cheap; demotion is not.
- **An explicit marker makes it a decision** — "this is a rule", "decided", "lock it in", or a selected
  option from a choice that was posed.
- **Prefer reversible acts on unmarked input.** A dropped column, a removed public API, a `DECISIONS.md`
  entry or a commit are worth confirming first; sketching, drafting and analysing are not.
- **Still engage fully.** Thinking is signal, not noise — follow it through and show what it implies. What
  this note governs is the *status* something is recorded with, not whether to act on it.
- **Separate a finding from its resolution.** A remark often exposes a genuine defect; the defect stands on
  its own, while which way it resolves stays open. (2026-08-09: the salience remark exposed a spec answering
  one question two contradictory ways — a real defect — while the resolution stayed the owner's call.)
- **Ask when the fork is material.** If either branch changes the work substantially, put the choice up
  rather than picking one and recording it as settled.

## Related

- `.claude/rules/persist-working-state.md` — checkpoint it when it happens; this note is about the *status*
  it gets checkpointed with.
- `.claude/rules/no-global-memory.md` — why this lives in the repository at all.
- `.claude/rules/repo-mechanics.md` § the sync warning — local, no `daoris.lock` entry, same deletion
  exposure as every other local document.
