# Memory a model-driven knowledge system keeps current — Lyntai's synthesis, round 1

**Status: a synthesis (`docs/documents.md`), 2026-10-30; nothing is built.** It settles `docs/memory-currency-proposal.md`
against the other maker's sealed proposal, written from the same brief, and the accepted knowledge-freshness design, as
a decision of this repository (**D241**), in the shape of Daoris's D191: each of the brief's five questions answered once,
each difference chosen with its reason, and what each choice rejects. The rows are `TASKS.md` Part 417, MC1 to MC9; the
study's rulings it builds on are **D239** and **D240** (`docs/memory-currency-study.md`).

## 0. Where the two proposals already agree, and the owner's proposal

Both keep: retirement by an explicit end (MC1, and MC5's confirmed failure) rather than burial; decay for what nobody uses;
a passing re-check a stamp and never a use; a model alone only for a step that changes nothing a reader reads as current;
the records a separate index that memory cites; and MC1 then MC3 first. Both keep one owner per shared fact and refuse
cross-task links (`docs/memory.md` §7, **D92**).

**The answer to the owner's proposal of retiring through decay.** Decay stays, for what nobody uses, and the owner's point
is kept: **an ended entry is kept whole and recoverable**, never deleted, read back under `History` with its reason and
successor, and a wrong end is corrected by writing the fact again. What decay cannot do is retire the entry somebody still
asks about, which is the case that matters. A use reinforces the stale fact, burial is relative to the top result so a
replaced and a current fact that rank alike are both returned, an authoritative entry is never buried (**D41**, **D56**),
and a guaranteed exclusion would be a third stored state every reading must answer for. So an end, which is the writer's
statement, retires; decay, which is the ranker's, only quiets. The exclusion an end gives is of *eligibility*; bounded
retrieval can still miss an eligible entry, and ended entries stay in the walk, so accumulated ends can shorten a page
(`docs/memory.md` §9) — MC1 measures that instead of promising it away.

## 1. Shared memory: a read-only view of the owner's task, enforced on every operation

**Chosen.** One owner per shared fact, in the owner's task, and the owner alone writes or ends it. A reader gets a
**read-only view** of that task, as a member of its own blend, so a supersession or an end the owner makes is read at the
reader's next recall with nothing copied. A reader that relies on a shared fact stores a citation (the owner's reference and
the revision it read, as text) with a `Recheck` that asks the owner; the citing entry ends through MC5 when the check
fails. A reader's own observation is a proposal to the owner, routed by the coordinator as a request, never a competing
copy that can end the original.

**Taken from the other proposal.** The coordinator authenticates callers and enforces read, propose, write and end grants
on **every operation, expansion by reference included**; and the owner's change reaches readers through a durable log with
subscriptions and a cursor. Both are the **coordinator's**, since it owns grants, delivery and scheduling; the library owns
reference identity, validity and the view's contract below.

**What the code shows, and why the view is not just a blend member.**
- An expansion is addressed by engine and id only (`MemoryRef(Engine, Id)`; `GraphMemoryEngine.ExpandAsync` reads
  `store.GetAsync(Name, id)` and takes the task from the node), so a reference from *any* task of an engine expands there,
  and ids are sequential. `taskKey` isolates the neighbours and every recall, not a held reference. The view therefore
  **refuses a reference outside its task** and MC9 closes the same gap in the shipped expand tool.
- A recall and an expansion reinforce, and a write advances the engine's interference tick (`ReinforceAsync`,
  `_age.Advance(write, Name)`). A view that reinforced would write to the owner's graph and age its entries from a
  reader's questions. The view **reads without reinforcing, linking or advancing anything**, which is also why a shared
  *writable* task is refused: every reader's write would age the owner's entries.
- Interference advances per engine, so which entries share an engine is a deliberate choice of the host.

**Library and coordinator.**

| Library (MC7) | Coordinator (Daoris's, not here) |
|---|---|
| a view of one task key as a blend member; no write, reinforcement, link or tick through it; a foreign reference refused; `History` and the end reading it already has | who owns a fact, who holds which grant, caller identity, the request that carries a reader's proposal, the change log, subscriptions and cursors |

**The change log and the cursor** are the coordinator's. Missed notifications do not need one: the reader's `DueAsync`
sweep (MC5) re-checks every citation, which is the reconciliation. The one library addition, if the coordinator measures
the sweep too slow, is a read of entries written or ended since a position; it is not built before that (a new row then).

**Rejects:** a writable shared task (scope is not authorization, **D92**); copying a shared fact into readers; a
cross-task relation; the library holding owners, grants or subscriptions; a notification that rewrites another
repository.

## 2. A failed check: yes, `Failed` means the host confirmed the named claim no longer holds

**Chosen.** MC5's `Failed` is that, and nothing weaker. A timeout, a missing access or an ambiguous answer is
**inconclusive**: the host calls nothing, the entry stays exactly as it was, and `DueAsync` keeps returning it, so the
retry the other proposal asks of the coordinator falls out of the query. The library records no "inconclusive" state, since
a recorded state with no effect on a reading is the third state D240 refuses; the host's attempt log is the host's.

**What `Failed` ends at.** The instant of the confirmation, by the engine's clock unless the host states one: the library
never infers an earlier cessation from a later failure. The reason is the host's note, required.

**Who confirms a failure** is §3's ladder: a registered deterministic predicate the host executed, or a person for a
semantic check, which is where every semantic check starts. **Rejects:** ending on a run that did not complete; an age
that ends entries (**D240**); a flagged-but-live state.

## 3. The confirmer, and what moves a step between classes

**Chosen.** The classes follow the effect on what a reader reads as current, as the proposal had them, with the other
maker's evidence rule added:

| Step | Class | Applied by |
|---|---|---|
| find candidates (MC3) | none | a model or the engine alone; it changes nothing |
| `Disputes`, `Amends` (MC4) | **alone, attributed** | the writer, stamped with the host's metadata; neither ends anything |
| `Supersedes`, an end, a `Failed` (MC1, MC5) | **confirmed** | the host's approver (MC6): a person, a registered deterministic check on source evidence, or a model reviewing bounded claim pairs **only for an (action class, model, corpus) that cleared MC8's bar** |
| `Held` | none | the host's check; an exit status, never a model's reading |
| bury | nobody | the ranker only (**D41**) |

**The seam (MC6).** An approver in the shape of `IToolApprover` that `<prefix>_remember` with `supersedes` and `<prefix>_end`
call with the proposal: the exact references the host returned, the claim and the evidence. With no approver registered the
write lands live and unlinked and the end is refused as a result the model can read. **The library ships no approver that
approves by itself**; a host that wants an agent to apply alone writes that approver, and so owns the decision (**D218**).
This replaces the study's and the proposal's opt-in per task: an option would be a shipped setting a model's measured
error does not justify.

**Taken from the other proposal.** Agreement between two models earns no promotion; **disagreement abstains**; a
model's identity for an entry is only a host-issued reference, never one it invented (**D217**); and blanket human review
and unmeasured model consensus are both refused. A "second model" as confirmer is a judge only when given evidence a
relevance judge lacks (the source record's own change), because **D109** measured a plain judge endorsing the replaced fact
3.4 times as often as the current one.

**What moves a step.** Only MC8: the host's cases, independently adjudicated, with the error bounds **written before the run**,
per (action class, model, corpus), never carried from one model to another. A step is reported on the rates separately
(a wrong end hiding the true fact; a missed replacement; an unaffected claim lost; a wrongly kept stale one), never as one
accuracy. A bar met moves that class for that pair in `docs/memory.md`'s guidance and `docs/memory-measurements.md` §5; a bar
missed changes nothing, which is stated here so the next session does not re-run for it.

## 4. Conditional mutation: yes, an optional precondition on the end

**Chosen.** Codex's point holds against the code: a write that supersedes resolves its predecessors, upserts the
successor, then ends them as three calls (`GraphMemoryEngine` write path, `SupersedeAsync`), and a confirmation that a person
made an hour ago ends whatever the entry has become since (for example a `Held` stamped after the failure it was
reviewed on). So MC1's `MemoryEnding` gains an optional **expected state**: the validity and confirmation as the confirmer
read them. A store that holds the capability applies the end **only if** the state still matches, in one conditional
statement on the SQL stores and under the lock on the in-memory and file-system stores; a mismatch changes nothing and
returns `Changed` with the state it found, never an exception. An unconditional end is the default, as today.

**A store without the capability throws `NotSupportedException` when an expected state is given**, never ends unconditionally
in silence. MC5's `ConfirmAsync(Failed)` and MC6's proposal carry the state the due read returned, so the three use one
guard.

**Not chosen:** a transaction over the successor's insert and the predecessor's end. That widens a store contract the
shipped stores implement; the order today (insert, then end) fails toward both entries live, and a repeat of the same write
passes, so no fact is lost and the repair is the retry. MC1 measures races, retries and a failure between the two, and
reports whether that is enough.

## 5. The order and the instruments

**Chosen order**, with what each measures: **MC1** (the refusal rules identical on every shipped store; an ended entry
absent from `Current`, present in `History`, unburied, unpruned; the conditional end under a race; accumulated ends against
page length). **MC3** (the true-predecessor rate per basis; unchanged recall counters). **MC4** *before* the tools
(the other maker's order: an agent given only a whole-entry end would take the 66% to 14% amendment loss, `docs/memory.md`
§7, so `Amends` and `Disputes` exist before an agent can write; a relation surviving co-activation, decay and prune;
unaffected-claim recall). **MC6**, the approver seam, then **MC2**, the tools on it (schemas, refusals read as results,
no approver meaning no applied supersession, attribution). **MC5** (a held check changes only its stamp; a failed one ends
only as §2 says; the library runs nothing). **MC7**, the view, whose trigger is Daoris's stated second host
(`docs/ROADMAP.md` Wave 4). **MC8**, the wrong-end instrument, built from MC3's pairs and independent of MC2, so it can run as
soon as MC3 lands. **MC9**, a small isolation fix that waits for nothing and is listed last only because nothing stands on it.

**Recall is measured by repeated paired walks.** A recall reinforces and links what it returns (`docs/memory.md` §7's
trap), so every measured step above runs its arms back to back on each query with the order alternating, repeats the
walk, and reports **stale answers, lost current claims and lost historical evidence separately**. The first step of MC3
and MC8 is to read whether the harness does this (`bench/Lyntai.Benchmarks/MemoryLongMemEvalBench.cs`) and to add it
where it does not; it is a step inside those rows, not a row, because no outcome of an instrument alone changes the
library (`.claude/rules/task-lifecycle.md`). **Rejects:** one aggregate accuracy as permission to trade an invariant away.

## 6. Records: a separate index that memory cites

Settled as the proposal had it and the other maker had it. Records stay in the coordinator's index, which owns their
standing and their supersession and amendment marks (the accepted freshness design's one-home mark); a memory entry that
relies on one cites its reference and the revision it read, and its `Recheck` asks the index. A whole-record replacement
ends the citing entry through MC5; an amendment mark returns the original with its amendment context and ends nothing,
since whole-entry ending cut an amendment's claim from 66% to 14%. No record becomes an authoritative entry: the grade
protects against decay and truncation and establishes neither currency nor complete retrieval (**D56**), it takes reserved
slots inside every recall's limit, and a second copy of a supersession is the unreviewed one. A claim projected later is
rebuildable from the record, with stable claim identity the owner supplies; a model's summary never becomes the record.
**Re-opens** on a measured task that needs a record and an experience ranked together and cannot get both from two reads.

## 7. What differs between the two proposals, in one table

| Point | Chosen | Source | Rejected |
|---|---|---|---|
| shared read path | read-only view as a blend member | this repository's | references only, with no view |
| enforcement | coordinator, on every operation; the view refuses a foreign reference | the other's, with the code finding | a scope string as authorization |
| change log, cursors | the coordinator's; `DueAsync` reconciles | the other's, placed | a library-held log |
| `Failed` | the host confirmed the claim; else inconclusive | the other's | a recorded inconclusive state |
| confirmer | person, deterministic check, or a model past MC8's bar | both | an opt-in "apply alone" setting |
| `Disputes`, `Amends` | alone and attributed | this repository's | a proposed-versus-accepted state |
| conditional end | optional expected state | the other's | a transaction over insert and end |
| order | MC4 before the tools | the other's | tools first |
| instruments | MC8 plus paired walks | both | one aggregate score |
