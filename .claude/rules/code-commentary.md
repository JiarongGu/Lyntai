---
name: code-commentary
applies_when: writing or reviewing any comment — an XML doc on a public member, or a `//` note on a line of code
enforces: three tiers, three jobs — the XML doc is the CONTRACT, a `//` comment ANNOTATES the code beneath it, and the DESIGN argument lives in a record; prose that outgrows the code it explains has stopped being a comment
---

# Comments — three tiers, three jobs

**An XML doc states the contract a consumer reads. A `//` comment annotates the code it sits on. The design
argument behind either belongs in a record. A comment longer than what it explains has stopped being one.**

## Why

Measured in this repository on 2026-08-16: **0.86 comment lines per line of real code** (14,927 against
17,259, blanks and brace-only lines excluded), and `src/` carrying **1.6× more prose than the decision log,
the pitfalls record, the task archive and the design contract COMBINED**. A third of it sat in blocks of
16+ lines — including a 120-line `<remarks>` on one method — which is past what anybody reads in place.

Two costs, and the second is the one that is easy to miss.

**A long comment is an unindexed, ungated, unreviewed document in the worst possible location.** This
repository runs three gates to stop its maintained prose from rotting — `check-docs`, `check-links`,
`check-counts`. None of them can see a code comment. So the longest and least-read prose in the tree is
also the only prose nothing checks, and `pitfalls.md` records it rotting exactly as you would expect: a doc
citing a test renamed away twice, a paragraph describing pre-fix behaviour in the present tense for a whole
release, five corpus arms called "the two arms".

**And the same argument in a comment AND in the decision log is duplication** — the defect this repository
removes from code without hesitation. Two copies drift, and the comment is the copy nobody reviews.

## How to apply

### Pick the tier by asking who reads it

| The reader | Where it goes |
|---|---|
| A consumer who cannot see the source | the `///` XML doc |
| Someone already reading this line | a `//` comment |
| Someone asking why the code is shaped this way, where there was a real alternative | `docs/DECISIONS.md` |
| Someone about to repeat a mistake that already cost something | `.claude/knowledge/pitfalls.md` |
| Someone asking what a pass or sweep DID | `docs/task-archive.md` |
| Someone asking what a number measured | a record under the untracked design-records directory |

### The XML doc is REFERENCE, not an essay

What it does, what each parameter means, what it guarantees, and how it fails — plus, on a BYO seam, what
an implementer must honour. It is what a stranger meets in IntelliSense.

- **Never cite something the reader cannot open**: a fix round, an internal review, an untracked plan. A
  design record stops being true when its version ships, so shipped documentation must not lean on one.
- **Past roughly 25 lines the design argument has leaked in.** Move it to the record that owns it and keep
  the RULE plus a pointer. "Reversion is not optional: damping's factor is zero at `D = 10`, so dropping it
  leaves the ceiling absorbing" is a rule. Three paragraphs on how that was discovered is a record.

**25 is a PROXY for the real rule, and exactly ONE block in `src/` has earned an exception to it.**
`IMemoryGraphStore.SeedAsync` documents *several distinct guarantees at once* — seven, at 2–11 lines each:
ordering, faintness never excludes, null query + unconditional admission, the bound on that admission,
salience admission being backend-specific, relevance-0, and the portable substring guarantee. Each is a
promise a BYO store must honour. At 31 lines that is *smaller than what it explains*, which is the actual
rule, so cutting further would delete contract rather than prose.

**Be very slow to conclude your block is the second one.** The sweep that introduced this rule declared 19
files irreducible — "contract, not fat" — and an adversarial re-check the same day found that honest for
**two** of them. Fourteen came under 25 by relocating exactly what the always-wrong list above names, and
the most common single offender was a paragraph that *points at a record and then restates it anyway*. The
tell that you are looking at fat rather than contract: strike every sentence that could be deleted without
weakening a promise, and see what is left. If what remains is one guarantee explained at length, it is fat.
If it is seven guarantees stated once each, it is contract.

**And do not trust the measurement of your own paydown.** That same claim was made from having done the
work rather than from counting what was left, and it cited a number taken from the LEDGER rather than the
tree — which is how 279 lines of debt stayed invisible. Re-run the gate and read what it says.

**So: trim to the rule, then stop.** Record what is left as an allowance rather than reaching for
`comment-ok`; the allowance is a visible, ratcheted number that keeps the block from growing, while the
escape removes it from measurement entirely. Reserve the escape for a block no reader would want shorter —
a table, a wire-format capture — not for one you simply could not get under the line.

### A `//` comment is an ANNOTATION on the code beneath it

It needs code to annotate. A twenty-line block over three lines of code is inverted — the prose has become
the subject.

Write one where the code alone would **mislead**, not merely where it would be unfamiliar. What earns its
keep, and note that every one of these is short:

- a precedence or ordering that looks arbitrary and is not (*"parse the body first, then fall back to the
  exit code — the backend's own words outrank it"*);
- a guard whose deletion looks safe (*"the `MAX()` is load-bearing: a zero divides to NULL, and a NULL
  predicate excludes the row silently"*);
- a deliberate omission that reads like an oversight;
- a unit, scale or lifetime the type does not carry.

What does not: restating the line, narrating history, or arguing with a reader who is not there.

### The length test

**A comment should be smaller than what it explains.** Longer than the code beneath it means either the
code needs the work or the prose needs a different home — and both are better than leaving it where it is.

### Three things that are always wrong

- **Meta-commentary** — prose about the document's own history (*"this paragraph used to say…"*,
  *"recorded so nobody 'fixes' it later"*). That is a changelog in the wrong file. If the correction matters,
  the record holds it; if it does not, nothing should.
- **Work-log provenance** — fix rounds, plan names, review item numbers, dates attached to nothing a reader
  can act on.
- **Restating a record** — carry the one-line rule and the pointer, never the argument twice.

### When you delete, RELOCATE first

Cutting a comment is only safe once its load-bearing half has a home. Move the rule, then cut — never the
other way round. An invariant that exists solely in prose nobody reads is already half-lost; deleting it
without relocating finishes the job.

## Related

- `persist-working-state.md` — route by KIND, and the decisions record has the highest bar, not the lowest.
- `dotnet-package-layout.md` §Naming — the same principle for names: say what the thing IS.
- `.claude/knowledge/pitfalls.md` — the measured prose-rot incidents behind the Why above.
- `repo-mechanics.md` — which record is which here, and the local-file exposure this rule shares.
