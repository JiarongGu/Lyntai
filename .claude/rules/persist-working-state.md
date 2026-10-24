---
name: persist-working-state
description: Checkpoint a decision, finding, or milestone to its durable home in the repository WHEN it happens, not at the end.
applies_when: any multi-step task — at each decision, finding, or milestone
---

# Persist working state — where each kind goes here

**The rule is the shared `persist-working-state` (`AGENTS.md`): write it when it happens. This is where
each kind of thing goes in this repository.**

## Route by KIND — and the decisions record has the highest bar, not the lowest

**The test for the decisions record: was there a CHOICE between real alternatives, and does it constrain
future work?** Both halves. Work you simply did is not a decision, however much reasoning it took; a fact
you discovered is not a decision, because nobody chose it.

| What you have | Where it goes |
|---|---|
| A choice between alternatives that constrains future work | `docs/DECISIONS.md` |
| What a pass/sweep/review DID — findings triaged, items closed | `docs/task-archive.md` |
| A trap that costs something when forgotten — nobody chose it, you *found* it | `.agents/knowledge/pitfalls.md` |
| A number you measured | the record that owns the measurement (`docs/memory-measurements.md` §5 for the memory engine); the CONCLUSION goes to whichever row above fits |
| A per-incident bug fix | `docs/FIXES.md` |
| What a gate is for and what it holds | `docs/GATES.md` |
| A convention every task must follow | the rules or knowledge tier |

**Why the bar matters:** the decisions record is the default destination in most people's heads, and three
separate rules point *into* it while nothing said what to keep out. The cost is not size — it is that a
reader looking for *what governs this code* has to sift a work log to find it. `check-decisions` now
ratchets entry LENGTH, but nothing gates what goes in.

**When it is genuinely borderline, write the RULE and see if it survives the title.** "We decided to ship
X" is a work log. "X, because the alternative Y costs Z" is a decision. If you cannot name the alternative,
it probably was not one.
