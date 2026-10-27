# The vNext RFC, folded into the roadmap and the backlog

**Status: a study of an external proposal** (`docs/documents.md`). The RFC, *Lyntai vNext — modular memory
intelligence and agent capabilities*, was reviewed on 2026-10-25 against the public repository, without a clone or a
test run. It proposes; it does not decide. **Where it and this repository's records disagree, the records
win**, and §4 says where. What Lyntai adopts and refuses is **D218**; what is filed is `TASKS.md` Part 377
and Part 375, and `docs/ROADMAP.md` §Sequenced.

## 1. How each proposed item stands

`Exists`, `Partly`, `Refused` (a decision already answered it), `Owner` (only the owner can rule) or `Later`
(sequenced behind a ruling). Evidence is the decision or the archive Part, read at this commit.

| RFC item | Stands | Evidence and disposition |
|---|---|---|
| §1.2 do not rebuild: router, graph, jobs, forget, vector migration, no UI in Core | Exists | `ITextClient` and the router; D39–D41, D215; the job runner and its redelivery (`docs/task-archive.md` Part 367); D198; D199 and D212 (`docs/task-archive.md` Parts 350 and 368); design §9 (a host is an application's concern). All seven stand as written |
| §1.2 another free-key LLM extraction | Refused | D217: a model cannot supply an attribute's identity. Re-opens only on its two triggers |
| NEXT-001 capability inventory | Exists | this table, plus D39, D175, D188, D200–D204, D209, D213–D216 |
| NEXT-002 profile ownership (Curated or a ledger) | Owner | RV26 decides whether a profile ships at all; on yes, **VN2** compares the two on one scenario set |
| NEXT-003 package graph | Owner | **VN1**: whether D25 gains a second reason for a package |
| NEXT-004 consumer story, P-fixtures, no-model baseline | Owner | RV27, which serves RV26 alone; §3 reconciles the RFC's two fixture lists |
| NEXT-005 what D217 refused | Exists | D217 names it: a model-named key as identity. What stays open is its trigger, a consumer-supplied schema |
| NEXT-101 to 106 profiles package | Later | Wave 1, behind RV26, VN1 and VN2 |
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
| ADR-A to D (appendix) | Owner / Exists | package boundary is VN1; ownership is VN2; subject, actor and scope is D92 plus the host authorizing before a read; change and forget propagation is D198 and D213 |
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
  domain package. The RFC's bridge packages reference two. That is VN1's question, not a given.
- **ADRs and a proposals folder.** The records here are `docs/DECISIONS.md` entries of at most 35 lines and
  untracked specs under `local/superpowers/` (D43); the RFC's nine-heading ADR and its `docs/proposals/` do
  not exist here. The headings it wants (problem, alternatives, rejected, reversal trigger) are what an entry
  already carries.
- **The RFC's recommendation on RV26** (a conditional yes to a prototype) is a recommendation. RV26 stays
  decision-only until the owner rules, and what each answer does is written on the row.

## 5. Room left for the memory evidence

A memory-lane run of the engines on a corpus far larger than the library's own is in flight. Wave 0's
evidence is not guessed here: its findings are written to `docs/memory-measurements.md` §5 when it lands, and
VN2 and RV27 read them there before they are built. Nothing in this fold depends on their direction.
