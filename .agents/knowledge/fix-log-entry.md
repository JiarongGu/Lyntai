---
name: fix-log-entry
applies_when: writing or correcting an entry in docs/FIXES.md — the shared fix-log skill names the fields, this names this repository's shape
enforces: newest at the top, a dated heading and five bold-lead paragraphs; a correction goes at the corrected entry's HEAD
---

# The fix log here is `docs/FIXES.md`, in its own entry shape

**The shared `fix-log` skill says what an entry records. This is the shape `docs/FIXES.md` holds, which
every existing entry already shows.**

## Why

A reader reaches an entry from a grep, mid-file, so every entry must read the same way and a superseded one
must still be readable: it is often the only home of its reusable half. `repo-mechanics.md` §Fix log is
the rule; this is its template.

## How to apply

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
