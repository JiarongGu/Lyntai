---
name: skills-workflow
applies_when: starting any non-trivial task, and whenever a follow-up changes its scope
enforces: run the discovery skills before exploring code, actually read what they route you to, and re-run them when the scope moves
---

# Start a non-trivial task through the discovery skills — and read what they return

**Before exploring code for a non-trivial task, invoke the discovery skills the generated index lists.
Then read every document they route you to.** Skip the gate only for genuinely trivial edits.

## Why

Every repository that adopted this rule wrote it after the same failure: work was done cold, it looked
correct, and it violated an invariant that was already written down. The rule is not about ceremony —
it is that the knowledge tier is deliberately not auto-loaded, so *nothing* surfaces a matched document
unless the discovery step runs.

The second half is where it actually breaks. Invoking a skill that returns a list of documents and then
not reading them produces the *appearance* of having checked, which is worse than skipping the step
openly: the transcript shows diligence and the code shows none. Repositories that hit this added an
explicit blocking step for it, which is the strongest evidence that it happens to everyone.

## How to apply

- **Consult the generated index for the roster.** It is built from what is on disk, so it is right about
  which discovery skills this repository actually has — a memorized list is not, and repositories differ.
- **Run them before any code exploration**, not after forming a hypothesis. A hypothesis formed cold is
  what the step exists to correct, and it is much harder to abandon once written down.
- **Reading is the point.** If a skill routes you to documents, read them and say which. An unread match
  is a missing contract.
- **Re-run when the scope moves.** A follow-up that names a different area is a new task, however
  continuous the conversation feels. Same files and same scope is a continuation; anything else is not.
- **Skip only for genuinely trivial edits** — a typo, a one-line fix, a comment. "It is a simple change"
  and "I already read those documents" are the two reasons that are wrong most often.
- **Finished something reusable? Evolve the system.** Add the rule, the knowledge document, or the skill
  so the next task starts ahead of where this one did.
