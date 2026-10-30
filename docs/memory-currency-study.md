# Keeping an agent's memory current — a study

**Status: a study (`docs/documents.md`), 2026-10-30; nothing is built.** Daoris asked five things of the memory engine
(its requests LQ16 to LQ20): what an agent or a host still cannot do to keep what it remembers current by replacement.
This reads each against what the engine ships (`docs/memory.md` §9, *Say when a fact held, and what it replaced*;
`MemoryValidity.cs`, `MemoryRecords.cs`, `MemoryTools.cs`), answers each by the generic-library test, and files rows
MC1 to MC5 in `TASKS.md` (Part 417 there). The rulings are **D239** and **D240**. A type named below is a shipped one
unless a row proposes it, and every name a row proposes is settled against `docs/glossary.md` when it is built.

## 1. What the requests rest on, and what binds any answer

Daoris's request names three constraints and asks that nothing replace an entry without a writer's say: **D90** (no
"newest wins"), **D109** (detection belongs at write time: a relevance judge endorsed the superseded fact 3.4 times as
often as the current one) and **D217** (a model-chosen key matched its predecessor in a quarter of questions). All three
hold here. **D218** adds the one that decides the shape of the agent half: *a model proposes without owning identity,
scope or write authority*, so an agent writes only through tools the host registered, in a task and scope the host
pinned.

**The generic-library test** (`.agents/knowledge/generic-library.md`): each need is stated with the asker struck.

| Request | The need with Daoris struck | Passes? | Answered by |
|---|---|---|---|
| LQ16 | a model-driven application lets an agent correct what it remembers | yes: any host that exposes memory to an agent has the read half already | MC2 |
| LQ17 | a writer that does not hold the predecessor's id is shown the live entries it may update | yes: `Supersedes` is unusable without an id the writer rarely has | MC3 |
| LQ18 | a reader tells two entries that disagree from two that are compatible | yes, but only as **the writer's statement**, never the engine's inference (§5) | MC4 |
| LQ19 | a fact about a changing world is re-checked, and a failed check stops it reading as current | yes: the check is the host's, the library stores the outcome | MC5 |
| LQ20 | an entry that stopped being true leaves with a reason and no successor | yes, and it is the primitive the rest stand on | MC1 |

## 2. One primitive: an entry is ended by reference (LQ20, MC1)

Today an entry leaves by `Supersedes` (a successor in the same write), `ForgetAsync` (a scope), `PruneAsync` (a
capacity bound), or by writing identical text again with `ValidTo`. The store already has the one call every other
way of ending needs, `ITemporalMemoryGraphStore.SupersedeAsync`, and the engine reaches it only from a write or an import.

**MC1 proposes `IEndableMemory.EndAsync(taskKey, entry, MemoryEnding)`**, where `MemoryEnding` carries a required
`Reason`, an optional `At` (default: now by the engine's clock) and an optional `By`, an entry of this engine that took
over. With `By` it records the supersession exactly as a write does, at `At` (default: the successor's start); without
it, it states the entry's own `ValidTo`. Either way the entry is **kept**, not buried, not reinforced, not pruned, and
read back under `Validity = History` through an `EndedReason` on `MemoryValidity`. Refusals are the ones supersession already
has: a reference outside this engine or task throws `ArgumentException`, an end before the entry began throws, and
a different successor than the one an entry has throws `InvalidOperationException` (one successor, §6). Ending an
entry already ended changes nothing and says so in the result.

The surface is **optional, as valid time was**: `IEndableMemoryGraphStore : ITemporalMemoryGraphStore` (a required
member would break every store an application wrote), one nullable `ended_reason` column in a new migration on the two SQL
backends, the file-system and in-memory stores, the `v: 2` line of the portable format, and the blend declaring and
routing the capability by `MemoryRef.Engine` as it does `IForgettableMemory`. An engine that cannot hold it throws
`NotSupportedException`, as one that cannot hold valid time does.

**Why one primitive and not a method per request.** LQ16's `end`, LQ17's "the writer confirms the candidate", LQ19's
failed check and LQ20 are one act with different reasons. Four methods would be four places a refusal rule could
drift.

## 3. An agent writes, supersedes and ends (LQ16, MC2)

`AddMemoryTools` stays read-only, and a host that wants an agent to write calls a second registration, so an agent
that could only read still can only read (**D218**). **MC2 proposes `AddMemoryWriteTools(name, taskKey, scope?, prefix?,
options?)`**, registering `<prefix>_remember` (content, optional headline, `supersedes` refs, `validFrom`, `validTo`) and
`<prefix>_end` (a ref, a reason, optionally the `by` ref and `at`), from MC1.

- **The task is never an argument**: it is `MemoryToolScope.Resolve`'s, as the read tools' is. The scope is the host's
  unless its options let the model name one, because a scope is a namespace and never authorization (**D92**).
- **There is no grade argument.** A tool write is `Associative`; a host that wants more sets it in the options.
  Nothing a model supplies can make an entry exempt from decay.
- **A refusal reads as a result the model can act on**, not an exception: *"entry 7 was already superseded by entry 9;
  supersede entry 9 instead"* is the engine's own message, rendered as `{"error": …}`.
- The host's `Metadata` stamp (for example which agent wrote it) rides on every write, so a later reader sees where an
  entry came from without a reserved key in the app-owned bag.

## 4. A write is shown the live entries it may update (LQ17, MC3)

**MC3 proposes `IMemoryCandidates.FindUpdatesAsync(MemoryWrite write, limit)`**, answering the live entries (the
`Current` reading) in the write's own task and scope, other than the write itself, that it may update. Each carries a
basis: **lexical** (the full-text seed over the write's content) or **similarity** (the vector search, when an embedder
is wired). It reads: no reinforcement, no link, no store write, no model call beyond the embed a similarity basis
declares in the result's `Ran`. It is callable before the write (a host shows a person) or after it (the write is
excluded by identity).

**It is shown, never applied.** The list is the writer's to confirm: `Supersedes` on a write, or `EndAsync` with `By`
after one. The `<prefix>_remember` tool returns it with the new ref, as `"mayUpdate"`, when MC2 and MC3 are both
built; that is the shape the other memory server's writers saw and could not act on, and here `<prefix>_end` is the
act. **Nothing about the list decides anything**, which is what keeps it clear of **D109**: that refusal was of a rule
that DEMOTES by a relevance judgement, and a candidate list that a writer reads and answers demotes nothing.

**What is not known**, and MC3's proof measures rather than assumes: how often the true predecessor is among the first
few. `memory-longmemeval --haystack` already holds revised-against-superseded pairs (the profile run of **D217**); the
measurement reports the rate per basis and is not gated. **A low rate changes the default limit and the guidance in
`docs/memory.md`, and ships nothing less**, since nothing is applied unconfirmed. The default of four is the other
server's habit, not a finding.

**The subject basis is left out of the default.** An entry's subjects exist only after an annotator, which is a model
call, has read the write; a read that made one would break **D175**. A host that annotates first can pass the subjects
it knows; whether that earns a basis is a result of the measurement above, not a promise here.

## 5. Two live entries that may disagree are marked (LQ18, MC4)

**The mark is the writer's, not the engine's.** Overlapping valid entries are returned unmarked by design
(`MemoryValidity.cs`: validity "never infers that they conflict from their text"), and the request asks for a mark
"at write time per your D109 or on read" that names entries standing together on one subject. Two readings were
weighed:

- **A mark the engine derives** from shared subject or similar text. A subject is shared by every compatible fact about
  it (every entry about "the client" would carry it), so the mark would not separate a pair to doubt from a pair that
  is compatible, which is the thing asked for; and a mark that says *may disagree* from text is the inference **D90**
  refuses, in a form **D109** measured inverting as a read-time rule. It is not built.
- **A relation the writer states**, which is the information only the writer has (**D106**). **MC4 proposes** two kinds,
  `Disputes` and `Amends`, stated on a write beside `Supersedes` (so a candidate the writer declines to replace but
  doubts is marked in the same call) and by `RelateAsync` after it. A recall returns them on the item
  (`Relations` on `MemoryItem`), only for the items it returns and only where the other end is itself in the reading, so a
  dispute with an entry that has since ended is no longer shown. **Neither kind ends anything.** `Amends` is also the
  answer to the related question in §7.

The row's first step is to read how an edge keeps a kind through co-activation, decay and traversal. If it does not
survive them unchanged, the relation gets its own column and the row says so.

## 6. A stored fact can be re-checked (LQ19, MC5)

**The library runs nothing and infers nothing.** Whether a code base or a machine still matches a note is a check only
the host can run, and *when* a fact is stale is a policy two honest applications answer differently, so the library
stores what the host says (the test of `generic-library.md` rule 7).

**MC5 proposes**, on top of MC1: a `Recheck` on `MemoryWrite`, an opaque text of the host's (a command line, a query, a path)
the library never reads, bounded in length; `IConfirmableMemory.ConfirmAsync(taskKey, entry, outcome, at?, note?)` where
**`Held`** stamps `ConfirmedAt` (never backwards, and no other field moves: it is not a recall and not a reinforcement)
and **`Failed`** ends the entry through `EndAsync` with the note as its reason; and `DueAsync(taskKey, scope,
notConfirmedSince, limit)`, the live entries that carry a `Recheck` and were last confirmed before an instant or never,
which is how a host's loop finds what to run. a `Confirmation` on `MemoryItem` carries both on read, so a renderer can show *last
confirmed* beside an entry and nothing else about it changes.

**A failed check ends; it does not flag.** The request allows "ends or flags", and a flagged-but-live entry that a recall
does not return as current is an ended entry under another name, with a third state every reading would have to answer
for. A host unwilling to end an entry on one failed run (a machine that was offline) records nothing, and the entry
stays as it was; a check that was wrongly failed is corrected by writing the fact again, a new entry, with the old one
kept in history. An age limit that ends entries by itself is refused for the reason above.

Storage: nullable `recheck` and `confirmed_at` columns, a second optional store interface and its own migration, as
MC1.

## 7. The related question: a record amended in one claim of several

Superseding ends a whole entry, and a second `Supersedes` naming the same entry throws. **That stays**, and the reason
is measured: ending each amended whole document cut the recall of an amendment's own claim from 66% to 14%
(`docs/memory.md` §7 on a decision that amends another; `corpus-amendments-supersession`). What ends is a *claim*, which only the writer
knows. So a record whose claims change at different times is written as the claims (one entry each) and each is
superseded by name, and where a claim is changed but the rest of the text still stands, `Amends` (§5) says so without
ending anything. **Rejected:** more than one successor per entry (a reader of the entry could not say which end is the
current one), and a partial supersession that carries a span (the engine would have to read the text to apply it).

## 8. What is refused, in one place

A model-chosen key as identity (**D217**); an engine-derived conflict mark (§5); an end by age or by a failed read
the library ran itself (§6); a flagged third state (§6); more than one successor (§7); reserved keys in `Metadata`
(it is replaced wholesale on a re-remember, so a key the library depended on would vanish); a tool that names a task;
and registering write tools from `AddMemoryTools`, which would give every existing host's agent a write it did not
choose.

## 9. Build order, and what Daoris may rely on

MC1 first, since four rows stand on it; then MC2 and MC3, which are independent of each other (the tool shows
candidates only when both exist); then MC4; then MC5. None is released: every member added is frozen only from the
release that first carries it (**D70**), and a host builds against the unreleased rows as **D235** says. What Daoris
may rely on today is the shipped base the requests cite (`Supersedes`, `SupersedeAsync` on every shipped store, the
`Current`, `AsOf` and `History` readings), which does not move.
