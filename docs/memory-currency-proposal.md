# Memory a model-driven knowledge system keeps current — Lyntai's sealed proposal, round 1

**Status: a proposal (`docs/documents.md`), 2026-10-30, written from Daoris's brief alone before the other maker's is
read; nothing is built and nothing here is decided.** It builds on `docs/memory-currency-study.md` (D239, D240,
`TASKS.md` MC1 to MC5) and answers the brief's five questions, each with a choice, its reason and what it rejects.

## 1. The retirement channel: end for a replaced or false entry, decay for an unused one

**Choice.** Keep MC1's end and MC5's failed check. Decay stays the one channel for what nobody uses, as D40 and D41 ship
it. The owner's proposal folds two different facts into one: an entry nobody asks about (decay's) and an entry somebody
asks about that is no longer true (decay cannot know it).

**Why decay cannot retire the second kind.**
- *Use refreshes the stale fact.* A recall reinforces what it returns (D57), so the superseded fact still asked about is
  the one decay protects most.
- *Burial is relative.* The floor is a fraction of the top result (D41), so a superseded and a current fact that
  rank alike for one question are both returned, and nothing says which is current.
- *Burial by a writer's act is a stored state*, which D41 forbids (only the ranker buries) and which is a third state
  every reading must answer for (`docs/memory-currency-study.md` §6).
- *An authoritative entry cannot be buried at all* (D41, D56), and a replaced authoritative fact is the case that
  matters most.

**What recall and history return.** Under an end: `Current` returns the live entry only, and `History` returns both with
the reason and the successor (`docs/memory.md` §9). Under decay: both in one ranking, the stale one refreshed by each
use, and `History` shows two entries with nothing saying why one stopped.

**Where I take the owner's point.** Recoverability: an ended entry is kept whole, and a wrongly failed check is
corrected by writing the fact again (MC5). **Where I differ:** a passing re-check stays a stamp, not a use. A nightly
host loop would otherwise keep alive what no reader asks about, defeating decay's one job (D40). Re-open on a measured
confirmed, unasked fact buried wrongly; the answer is then an option on the reinforcement seam (D57), not a change of channel.

**Rejects:** retirement through decay; a flagged third state; ending by age.

## 2. What a model does alone: nothing that changes what a reader sees as current

The evidence (D90, D109, D215, D217, and the 66% to 14% amendment cost) is about a model or a similarity score *choosing
what replaces what*. It does not apply to a step that changes nothing a reader reads as current. So the classes follow
the effect on `Current`:

| Step | Class | Why |
|---|---|---|
| Find candidates (MC3) | model or engine alone | read-only, demotes nothing (D109's line) |
| Propose `Disputes` or `Amends` (MC4) | model alone | neither ends anything; a wrong mark costs a doubt, not a fact |
| Propose `Supersedes` | **confirmed**: a second party applies it | a wrong one hides the true fact from `Current` |
| Run a `Recheck` | host's process alone | the library runs nothing (D240) |
| `Held` from a check | check alone | an exit status, not a model's reading |
| `Failed`, so ending | **confirmed**: a check's result, repeated per host policy, or a person | one failed run on an offline machine is wrong |
| End with no successor | **confirmed** | "it stopped being true" is the unsupported judgement |
| Bury | **nobody** | there is no such step; only the ranker buries (D41) |

**The confirmer is the host's choice, and a second model judging relevance is not one**: D109 measured such a judge
endorsing the superseded fact 3.4 times as often as the current one. A confirmer is a person, a deterministic check, or a
model *given evidence a judge lacks* (the source record's own change). The library half is one seam, the shape of
`IToolApprover` (`src/Lyntai.Core/Agents/IToolApprover.cs`): `<prefix>_remember` with `supersedes`, and `<prefix>_end`,
hand a proposal to a registered approver and apply nothing without one; with none, the write lands live and unlinked. A
host opts an agent into applying alone per task.

**What moves a step between classes.** A rate on labelled pairs, the bar written before the run: for `Supersedes`, how
often a model-proposed end hides the true current fact (the wrong-end rate), beside MC3's true-predecessor rate per basis,
on `memory-longmemeval --haystack` pairs, per model. A step moves to "alone" only for a model and corpus that clear the
bar; a number from one model is not carried to another (D217's shape). A NO leaves the default as it is.

**Rejects:** a model alone for any step that ends; a second relevance judge as the confirmer; a "newest wins" default (D90).

## 3. Memory shared across repositories: a read grant, never a copy

**Choice.** The owner's entries live in the owner's task (`taskKey` is a real boundary, D92, and a scope is a namespace,
never authorization). A reader gets a **read-only member over the owner's task**, blended into its own recall as the
blend already routes members (`CompositeMemoryEngine`). Writes and ends exist only on the owner's registration. The
owner's change reaches readers by being read: a supersession or end is visible at the reader's next recall, with nothing
copied and nothing replicated.

| Library | Coordinator |
|---|---|
| a read-only view of one task key as a blend member; the end and `History` readings it already has; refusal of any write through it | who owns a fact; which reader holds a grant to which task; the identity of an agent; routing a reader's proposed change to the owner as a request, never an edit |

**A reader that relies on a shared fact stores a citation** (the owner's reference as text in its own entry's metadata,
with a `Recheck` that asks the owner), not a copy: a copy is a second entry that goes stale on its own clock. When the
owner ends the original, the reader's check fails and ends the citing entry through MC5. No cross-task edges, so D92
stands.

**One addition only if polling proves costly:** a read of entries written or ended since a position, so the coordinator
can notify a reader. A read, not a push, and not built until polling `DueAsync` is measured too slow.

**Rejects:** copying shared facts into readers; one coordinator-wide task every agent may write (scope is not
authorization); cross-task relations; the library holding owners or grants, which are a host's policy.

## 4. Records as memory: a separate index the memory cites

**Choice.** Records stay in Daoris's index, which owns their supersession marks. A memory entry that relies on a record
cites it (reference and the record's version as text) and carries a `Recheck` the host answers with an index lookup:
*is this record still current?* A superseded record fails the check and ends the entry with the record's replacement as
the reason, through MC5, with nothing new in the library.

**Why not authoritative entries.**
- *They take reserved slots inside every recall's limit* (D56, `AuthoritativeReserve`), so indexing every record spends
  the budget of the ordinary entries the memory exists for.
- *The curated engine, where authoritative material lives by role, cannot hold valid time* (`docs/memory.md` §9), so a
  record's supersession would need the graph engine and its slots.
- *A second copy drifts*: two statements of one supersession, the memory's the unreviewed one.

**Re-open** on a measured case: a task whose answers need a record and an experience ranked together and cannot get
both from two reads. The record then enters as ordinary entries superseded by the index's mark, never exempt.

**Rejects:** records as authoritative entries; supersession derived inside the memory from text or from the marks'
shape (the marks are the records' author's statement, so they are applied by whoever holds them, which is the index).

## 5. The order, and what each step measures

1. **MC1** — everything stands on it. *Measures:* the refusal rules identical on every shipped store, and an ended entry
   absent from `Current`, present in `History`, unburied, unpruned.
2. **MC3** — before the tools, because it sets Q2's classes. *Measures:* the true-predecessor rate per basis, reported and
   not gated, as MC3 says.
3. **MC2, with the approver seam (new, MC6)** — agents write only where a confirmer exists. *Measures:* tool schemas,
   refusals read as results, and that no approver means no applied supersession.
4. **MC4** — after reading whether an edge keeps its kind. *Measures:* a relation surviving co-activation, decay and
   prune.
5. **MC5** — *measures:* a held check changes only its stamp, a failed one ends; the library runs nothing.
6. **MC7: the read-only task view** — Wave 4's first half, whose trigger is a second host needing a shared scope
   (`docs/ROADMAP.md`); Daoris's brief states that need. *Measures:* a write through the view refused, an owner's end seen at the reader's next recall.
7. **MC8: the wrong-end instrument** — Q2's bar. Its YES lets a host opt an agent into applying alone; its NO changes nothing,
   which is stated before the run (`.claude/rules/task-lifecycle.md`).

**Numbering.** The study's D236, D237 and Part 416 collide with an earlier answer's on another unlanded branch;
whichever lands second renumbers, and this round's decisions take the next free numbers at the synthesis.
