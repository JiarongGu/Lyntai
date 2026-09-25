---
name: fix-log
description: After landing a non-trivial bug or regression fix, record its root cause, fix, and verification in the repository's fix log. Also use to review past fixes or trace when a behaviour regressed. Use as part of "done", before moving on — not later.
---

# fix-log

Keep a durable, greppable history of **why** things broke and how they were fixed, so a future
regression's origin is traceable and the same bug is not reintroduced.

## When to log

- **A regression** — something that worked and stopped working. Always log it, and trace the commit that
  introduced it by searching history for the distinctive token that changed.
- **A non-obvious bug** whose root cause would be easy to reintroduce: an encoding trap, an ordering
  assumption, a silently-ignored field, a lifecycle or threading subtlety, a packaging trap.
- **Skip** trivial typos, pure refactors, and still-unfinished work. A log everything policy produces a
  file nobody reads.

## How

The fix log is `docs/FIXES.md`. Add the entry at the TOP, below the file's preamble — newest first — as a
dated heading and five bold-lead paragraphs:

```
## <YYYY-MM-DD> — <the symptom, in one line a reader would recognise>

**Symptom.** What was actually observed, and where it was found.

**Root cause.** The real mechanism — the line or rule that let it happen.

**Fix.** What changed, and where; the decision it follows, if one governs it.

**Verify.** The tests that pin it (named), and whether each failed before the change.

**Introduced by.** `<short-sha>` (<date>), the commit that introduced it and what it did — or why it is
not a regression.
```

**A later fix that corrects an earlier entry writes the correction at that entry's HEAD, never its foot**:
a blockquote under the heading, plus a `<!-- keeps: … -->` on the heading saying what still holds. A reader
arrives INSIDE an entry from a grep, and a superseded entry is often still the only home of its reusable
half, so it must stay readable rather than skippable.

## Rules

- **Capture the root cause, not the symptom.** If you cannot name the mechanism — or the commit that
  introduced a regression — the entry is not finished. "Fixed a null reference" records nothing; the
  mechanism that allowed the null is the whole value.
- **Log it as part of "done", before moving on.** Reconstructed later, the entry costs an investigation
  and is usually wrong about the cause.
- **A fix spanning several repositories is logged in each, and duplicated in none.** Record this
  repository's half here and refer to the other neutrally — cross-repository specifics, and any private
  name, stay out of tracked files.
- **If the root cause is a reusable invariant, also write the rule.** The log is history; the rule is
  prevention. A trap recorded only in the log will be rediscovered by whoever does not think to search it.

## Why

The value is not the record of the fix — version control already has that. It is the **mechanism**, which
version control does not: a diff shows a comparison changed from `<` to `<=` and never shows that the
boundary was off because the timestamp was inclusive. That sentence is what stops the next person
reintroducing it, and it exists nowhere unless someone writes it down while it is still fresh.
