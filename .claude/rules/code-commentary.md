---
name: code-commentary
description: Three tiers, three jobs — the XML doc is the CONTRACT, a // comment ANNOTATES the code beneath it, and the DESIGN argument lives in a record.
applies_when: writing or reviewing any comment — an XML doc on a public member, or a // note on a line of code
---

# Comments — three tiers, three jobs

**An XML doc states the contract a consumer reads. A `//` comment annotates the code it sits on. The design
argument behind either belongs in a record. A comment longer than what it explains has stopped being one.**

**A long comment is an unindexed, unreviewed document in the worst possible location.** The prose gates
read a code comment only for retired vocabulary and dead references, never for whether it is still true —
so the longest and least-read prose in the tree rotted exactly as you would expect. `check-comments` bounds
the LENGTH (`docs/GATES.md` §check-comments). **And the same argument in a comment AND in a record is duplication** —
two copies drift, and the comment is the copy nobody reviews.

## Pick the tier by asking who reads it

| The reader | Where it goes |
|---|---|
| A consumer who cannot see the source | the `///` XML doc |
| Someone already reading this line | a `//` comment |
| Someone asking why the code is shaped this way, where there was a real alternative | `docs/DECISIONS.md` |
| Someone about to repeat a mistake that already cost something | `.claude/knowledge/pitfalls.md` |
| Someone asking what a pass or sweep DID | `docs/task-archive.md` |
| Someone asking what a number measured | the record that owns the measurement |

## The XML doc is REFERENCE, not an essay

What it does, what each parameter means, what it guarantees, and how it fails — plus, on a BYO seam, what
an implementer must honour. It is what a stranger meets in IntelliSense.

- **Never cite something the reader cannot open**: a fix round, an internal review, an untracked plan. A
  design record stops being true when its version ships, so shipped documentation must not lean on one.
- **Past roughly 25 lines the design argument has leaked in.** Move it to the record that owns it and keep
  the RULE plus a pointer. *"Reversion is not optional: damping's factor is zero at `D = 10`, so dropping
  it leaves the ceiling absorbing"* is a rule. Three paragraphs on how that was discovered is a record.

**25 is a PROXY for the real rule — a comment must be smaller than what it explains — and no block in `src/`
holds an exception today**: the one that earned it, by stating seven distinct guarantees once each rather than
one guarantee at length, has since come under the line. **Be very slow to conclude your block is an
exception:** strike every sentence that could be deleted without weakening a promise. If what remains is one
guarantee explained at length, it is fat; if it is seven guarantees stated once each, it is contract. **And do
not trust the measurement of your own paydown** — re-run the gate and read what it says.

**Trim to the rule, then stop.** Record what is left as an allowance rather than reaching for `comment-ok`:
the allowance is a visible, ratcheted number that keeps the block from growing, while the escape removes it
from measurement entirely. Reserve the escape for a block no reader would want shorter — a table, a
wire-format capture — not for one you simply could not get under the line.

## A `//` comment is an ANNOTATION on the code beneath it

It needs code to annotate and should be **smaller than what it explains** — a twenty-line block over three
lines of code is inverted, because the prose has become the subject. Write one where the code alone would
**mislead**, not merely where it would be unfamiliar. What earns its keep, and note that every one is
short: a precedence that looks arbitrary and is not; a guard whose deletion looks safe (*"the `MAX()` is
load-bearing: a zero divides to NULL, and a NULL predicate excludes the row silently"*); a deliberate
omission that reads like an oversight; a unit, scale or lifetime the type does not carry.

What does not: restating the line, narrating history, or arguing with a reader who is not there.

## Three things that are always wrong

- **Meta-commentary** — prose about the document's own history (*"this paragraph used to say…"*). That is a
  changelog in the wrong file. If the correction matters, the record holds it; if not, nothing should.
- **Work-log provenance** — fix rounds, plan names, review item numbers, dates attached to nothing a reader
  can act on.
- **Restating a record** — carry the one-line rule and the pointer, never the argument twice.

## When you delete, RELOCATE first

Cutting a comment is only safe once its load-bearing half has a home. Move the rule, then cut — never the
other way round. An invariant that exists solely in prose nobody reads is already half-lost; deleting it
without relocating finishes the job.

Related: `persist-working-state.md` §Route by KIND · `dotnet-package-layout.md` §Naming (the same principle
for names: say what the thing IS) · `.claude/knowledge/pitfalls.md`.
