# The vNext RFC, folded into the roadmap and the backlog

**Status: a study of an external proposal** (`docs/documents.md`). The RFC, *Lyntai vNext — modular memory
intelligence and agent capabilities*, was reviewed on 2026-10-25 against the public repository, without a clone or a
test run. It proposes; it does not decide. **Where it and this repository's records disagree, the records
win**, and §4 says where. What Lyntai adopts and refuses is **D218**; what is filed is `TASKS.md` Part 375
(VN2, its comparison, closed as **D224**), and `docs/ROADMAP.md` §Sequenced.

## 1. How each proposed item stands

`Exists`, `Partly`, `Refused` (a decision already answered it), `Owner` (only the owner can rule), `Decided` (ruled
since this table was written) or `Later` (sequenced behind a ruling). Evidence is the decision or the archive Part, read at this commit.

| RFC item | Stands | Evidence and disposition |
|---|---|---|
| §1.2 do not rebuild: router, graph, jobs, forget, vector migration, no UI in Core | Exists | `ITextClient` and the router; D39–D41, D215; the job runner and its redelivery (`docs/task-archive.md` Part 367); D198; D199 and D212 (`docs/task-archive.md` Parts 350 and 368); design §9 (a host is an application's concern). All seven stand as written |
| §1.2 another free-key LLM extraction | Refused | D217: a model cannot supply an attribute's identity. Re-opens only on its two triggers |
| NEXT-001 capability inventory | Exists | this table, plus D39, D175, D188, D200–D204, D209, D213–D216 |
| NEXT-002 profile ownership (Curated or a ledger) | Decided | RV26 is yes (**D220**); **VN2** compares the two on one scenario set, with a partial amendment and the second-authority question added |
| NEXT-003 package graph | Decided | **VN1** is yes within a bound (**D221**): D25 gains the whole promotion gate; bridge packages referencing several capability packages are not authorized |
| NEXT-004 consumer story, P-fixtures, no-model baseline | Later | RV27, behind VN2 and serving the profile alone; §3 reconciles the RFC's two fixture lists |
| NEXT-005 what D217 refused | Exists | D217 names it: a model-named key as identity. What stays open is its trigger, a consumer-supplied schema |
| NEXT-101 to 106 profiles package | Later | Wave 1: VN2, then a prototype (RV28) held to RV27's Track A, then a package only through D221's gate |
| NEXT-201 to 207 ingestion | Later | Wave 2, behind Wave 1 and D217's trigger. The RFC's constrained arm (the model picks from allowed attribute ids or abstains) is exactly what the trigger asks for |
| NEXT-301 does the composer already serve many sources | Partly | `IPromptComposer` and `EngineBackedPromptComposer` own the budget; D201's manifest already reports cuts. A context package is an adapter over them or nothing |
| NEXT-302 budgeted contributions with an omission manifest | Exists | D201 |
| NEXT-303 reflection proposals | Refused | D167, D168 and the Dream refusal in `TASKS.md` Part 375. A proposal-only, default-off form needs a NEW measurement before it is a task |
| NEXT-304 Agent Framework adapter | Refused | D216: a tested recipe (`docs/extensions-ai.md`); reconsidered when a second consumer needs the same mapping |
| NEXT-305 tool and MCP recipes | Exists | `ITool`, `Lyntai.Tools.Mcp`, D210's approval seam |
| NEXT-306 fault injection, compatibility | Partly | D211 (every contract on every backend, `docs/task-archive.md` Part 366) and the redelivery in Part 367. A package-absent run belongs to whichever wave ships a package |
| NEXT-401, 402 grants and multi-agent shared memory | Later | Wave 4. No consumer has asked; the RFC itself leaves orchestration to hosts, which D219 holds for agents |
| NEXT-403 revocation-aware import | Refused | D198 refused a tombstone fence and D204 says a file older than a forget brings the content back (`docs/memory.md`). Re-opens on a consumer that needs the fence |
| NEXT-404 to 406 | Later | Wave 4, on demand |
| ADR-A to D (appendix) | Decided / Later / Exists | package boundary is D221; ownership is VN2; subject, actor and scope is D92 plus the host authorizing before a read; change and forget propagation is D198 and D213 |
| §16 version and release | Partly | one `VersionPrefix` stamps every package (D19), so a per-capability preview train does not exist; adding a package does not force a major (D18 already) |

## 2. The principles

D218 records the verdicts. In short: **adopted** are core stable and capability optional, strict ownership
of raw, fact, projection and summary, model optional, no hidden side effects, security at the boundary and
measured exploration — each is a choice the library already made, now binding on anything optional.
**Adopted in part** is the package graph as a contract: the adapter rule stands whole, and a second axis for
a package is held for the owner. **Refused** is what the table above marks so, the RFC's own ids and ADR
template as tracked records, and a profile contract in Core before two heterogeneous consumers (the RFC's
own gate).

## 3. Two lists of fixtures

The RFC carries P01 to P16 in its appendix and P01 to P20 in its body, and they differ. RV27 keeps the
appendix list, which is the review's: negation, a third party's speech, reported speech, a one-off request
against a standing preference, a correction and a second one, a past value, scopes that conflict, a forget,
an older backup, cross-tenant reads, concurrent writes, a replayed event, a provider fault, and a read by an
agent in a later session. The body's extras (an agent's unauthorized proposal, a revoked block, shared
grants, a renamed attribute) need features of Waves 2 to 4, so they enter RV27 only with those waves.

## 4. Where the RFC and the records disagree

- **A preview train per capability.** The RFC proposes `-preview`, `-rc`, then stable for each package. One
  `VersionPrefix` stamps all of them (D19) and D70 withdrew the last exemption, so a new package is under the
  full promise from its first stable version. The freeze risk is handled by what ships first, not by a label.
- **Capability packages referencing each other.** The layout rule allows an adapter to reference Core or one
  domain package. The RFC's bridge packages reference two. **D221** does not authorize that: it adopts the
  promotion gate and leaves the adapter rule whole.
- **ADRs and a proposals folder.** The records here are `docs/DECISIONS.md` entries of at most 35 lines and
  untracked specs under `local/superpowers/` (D43); the RFC's nine-heading ADR and its `docs/proposals/` do
  not exist here. The headings it wants (problem, alternatives, rejected, reversal trigger) are what an entry
  already carries.
- **The RFC's recommendation on RV26** (a conditional yes to a prototype) was a recommendation. The owner ruled
  yes (**D220**), for their own reason: planned consumers, so "no consumer has asked yet" is sequencing.

## 5. Room left for the memory evidence

A memory-lane run of the engines on a corpus far larger than the library's own has landed
(`docs/memory-measurements.md` §5). Its amendment finding is why VN2's scenario set gains a partial amendment
(**D220**); VN2 and RV27 read it there.

## 6. VN2 compared: the profile is a projection over curated memory

**Standing.** The proposal of the session that ran VN2, now recorded as **D224** (`docs/DECISIONS.md`), which governs; this
section is the comparison it rests on and stays as written. The evidence is `docs/memory-measurements.md` §5 (the VN2
setup and `vn2-concurrent-corrections`, `vn2-partial-amendment-real-records`) and `ProfileScenarioTests`, test-only
code over shipped store interfaces. No public type, package or default changed.

**The profile is a projection over curated memory.** One claim per field of the schema, one curated entry per
claim (kind = the field id, task key = the subject, **D92**), appended and never edited: a correction ends the entry it
read behind `IRevisionedCuratedMemoryStore`'s revision check and then adds the new one, and a replay is found by a claim
id. **The authority is the entry**, which is the claim and the source it cites at once; the profile keeps no state and a
read returns the latest start, reporting any other claim that still holds. The three cases: a schema rename re-kinds each
entry in place (ids and creation times kept) and a file taken before it repopulates the old id until the caller renames
again, which merges by claim id; a partial correction writes one field and no other; an older export with a conflicting
current value is added beside the newer claim and read as a dispute, never as an overwrite (**D204**).

**Why this and not the ledger.** Both options passed every deterministic scenario and read the real records exactly
(869 of 869, 97% of amendments partial), so reads decide nothing. The one scenario they split on is two writers correcting
one claim: the revision check gave one winner in 20 of 20 trials on each store, while the ledger written through
`GraphMemoryEngine` alone left the refused writer's claim live and returned its value in 17 to 20 of 20 on SQLite. Closing
that takes a compare-and-supersede on `ITemporalMemoryGraphStore` across four backends, a frozen public capability
(**D70**) added for a prototype that **D220** says fixes no contract. What the projection lacks (valid time held in
metadata, one successor, a start no earlier than the predecessor's) is code the prototype owns and tests, in one place.

**Costs it accepts, to be proved by RV28 and RV27 Track A.** Valid time as entry metadata is a second vocabulary beside
**D215**'s, and the store checks none of it. A correction is two writes, so a crash between them leaves the field with no
current claim and fails closed; inject it. A forget removes rows one by one. The catalog's own edit path can change an
entry's content in place, so the prototype must detect a claim whose content no longer matches its id, or the profile
becomes a second authority edited by another tool. On disk it is 1.8 to 2.3 times smaller than the ledger.

**Rejected.** The ledger now (the race above; node ids change on a rename; a stored claim carries decay state). The ledger
with a caller-side compensation, which narrows the race to a window and does not close it. One claim per record: of two
writers the second is stale, so a partial amendment must be a field. Choosing from the real records' structure: only 46% of
partial amendments name their field by word overlap, so the field comes from the caller or the schema (**D217**).

**Re-opens** if a compare-and-supersede reaches `ITemporalMemoryGraphStore` for another consumer (the ledger's remaining
costs are the rename and the disk), if the profile needs intervals queried across fields, or if the two-write gap or the
in-place edit cannot be closed in the prototype.

## 7. RV28 built it: what the prototype proved of §6's costs

**Standing.** What the prototype did, with its evidence in `docs/memory-measurements.md` §5 (`profile-prototype-rv28`) and
`ProfilePrototypeTests`; it adds no public type, package or default, and §6 above stands as written. **D224**'s re-open
condition (the two-write gap or the in-place edit cannot be closed in the prototype) did not occur.

- **The two-write gap closes without a new store verb.** A correction that crashes between its writes leaves the field with
  no current claim, says which claim was never written, and is completed once by delivering the same event again. A peer
  that loses the revision gate to the same event finishes the second write itself.
- **The in-place edit is detected, not prevented.** Each claim carries a checksum over its content, start, source, authority
  and scope; an entry that fails it, or that the profile never wrote, is reported and never read. A kind change and the
  end a correction writes are outside the checksum.
- **Track A's two declared gaps are closed.** The engine's import is replaced by an import that finds a claim by id, and
  one event delivered to several writers converges on one entry. A reader folds copies of one claim by id meanwhile.
- **Attributes beyond VN2's.** A claim carries scope, authority, status and revision. A user's claim outranks a
  hypothesis and a hypothesis cannot end it; a narrative summary is a cache stamped with the snapshot's version and
  never answers a read. None of this is a published contract: the prototype stays under `tests/`.
