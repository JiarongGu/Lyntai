# Gates — what each one is for, what it measured, and what it actually holds

> **Not auto-loaded.** `CLAUDE.md` carries the command table and the baseline to compare a run against;
> this file carries the ARGUMENT — why each gate exists, the incident that produced it, which numbers it
> holds and which it does not. Read the section for a gate before changing it, adding one, or deciding a
> failure is spurious.

`node devtools/dev.mjs` with no argument prints the authoritative command list; it is derived from the
roster in `devtools/commands.mjs`, so it cannot be a subset. `verify` runs 23 checks, stopping at the first
failure. **Digits, not a number word** — `parseCount` has no hyphenated compounds, so `twenty-one` would be
skipped rather than compared and the claim it anchors would match nothing.

## Why this file exists

The repository gates its CODE from every side and, until 2026-08-08, gated its PROSE from none. A spec
paragraph that quietly stopped being true survived everything, and the next session read it and implemented
the wrong thing — twice in one day, caught both times only by a person reading it. Each gate below was
built after a defect of that shape, and each section records the measured cost of not having had it.

**The argument that produced most of them: a rule that is written down and still violated is a missing
gate, not a knowledge problem.** `check-encoding`, `check-links`, `check-archive`, `check-backlog` and
`check-tautology` were all built after their rule had already been written, published, and broken anyway.
**And when a gate's SCOPE rests on a measurement, that measurement expires**: `check-links` skipped the code
tier on "found none in code" and re-measured at 150, 88 of them dead (§check-links).

**And a guard whose failure mode is a false PASS cannot be validated by running it.** That is why every
guard script has its own test (`test-devtools`, and it runs FIRST in `verify`), why every counted claim's
counter is pinned against the real tree, and why every registered decision predicate is driven RED by a
synthesized tree before it is trusted.

### The cold-start measurement, and the four gates it bought (2026-09-10)

Five instrumented probes read this repository the way a fresh session would — **3,459 lines pulled, 1,838
wasted (53%), and two of five answers were not current.** It is the measurement `check-backlog`,
`check-pitfalls`, `check-dev-loop` and `check-measurements` were all built from, so it lives here rather
than in a backlog banner that closed with them.

| probe | read | wasted | current? |
|---|---|---|---|
| best memory config | ~595 | 55% | **no — unverifiable by construction** |
| why `SalienceWeight = 0` | ~730 | 27% | direction only |
| add a storage backend | ~970 | **61%** | **no — the docs were wrong** |
| what is open | ~625 | **74%** | yes, ungated |
| find a trap | ~539 | 47% | yes |

**Three findings, and each has since been paid for twice.** (1) **Generated and gated, or do not build
it** — four hand-written indexes had drifted against ZERO from `decisions-index`. (2) **Heading-level
indexing is the fix that looks sufficient and is not**, because the measurement record retracts INLINE, so
a heading-grep is blind by construction and the index must be at RESULT-ROW granularity. (3) An index sized
for READING is over-building: `decisions-index` barely helped LOCATING cost (~66 lines against ~117), its
wins being precision and a free currency scan — so `check-pitfalls`' index is line numbers only.

**A fourth arrived from building them, and it is the one that governs the next gate:** *measure a candidate
signal before shipping a scan over it.* Two were refused that way — a retraction vocabulary over prose
bodies (63 hits, **zero** defects, because the record's subject matter IS supersession) and "content after
the `**Verify.**` paragraph" in the fix log (present in **42 of 50** entries). A scan nobody counted is
indistinguishable from a strict one until it teaches a maintainer to reach for the escape token.

## `verify` — the roster, and what it does not hold

`VERIFY_STEPS` in `devtools/commands.mjs` IS the roster; `verify`'s own summary line is derived from it, so
running the command prints the current list and no prose copy can be more current than that. There is no
separate build step: `check-warnings` is the one solution build (`--no-incremental`, which it needs to see
every warning, and it prints the compiler's errors when the build fails), and `check-samples` compiles
incrementally on top of it.

**It also fails if the TREE CHANGED while it ran** — content-hashed before and after
(`devtools/scripts/_tree-fingerprint.mjs`), every moved file named, the green summary suppressed and a
non-zero exit. A verdict is only about the bytes the gates read, so an edit mid-run makes the whole report
describe a tree that no longer exists, green line included. Added 2026-08-30 after that produced two false
greens in one session. The habit the gate does not replace is in `.claude/knowledge/pitfalls.md`
§Environment / tooling, along with the two cheaper checks that were tried and refused.

**The guard tests run FIRST on purpose**: nothing below that gate can be trusted if the gates themselves
are broken.

## Which numbers a gate holds — and which it does not

Not all of them, and the difference decides what has to be re-measured by hand.

| number | held by |
|---|---|
| guard-script tests | `test-devtools`, against its own run |
| documented C# samples | `check-samples`, against its own run |
| e2e suites, the migration count | `check-counts` (derivable from the tree) |
| the xUnit trio (passed / total / skipped) | **NOTHING** |

**A run-derived number is checked by the gate that PRODUCES it**, never by a static counter that would have
to reimplement the filtering: both baselines `CLAUDE.md` quotes are compared by `_baseline.mjs` on a green
run, and an ABSENT claim fails, because a check that passes when its sentence is reworded away disarms
silently.

Only `dotnet test` knows the xUnit trio, and capturing that output is the shape `check-warnings` already
hit ENOBUFS on. **So those three are the ones to re-measure by hand after `verify`** — do not extrapolate
them from a diff, which is exactly how `3266/3287` was once written against a tree that ran `3264/3285`. A
stale baseline is worse than none: the whole point is that a reader can compare, and a count that no longer
matches a green run teaches them to stop comparing.

**A skip count in the low HUNDREDS means Docker is down and the whole Postgres leg is silently
unexercised** — start it and re-run before believing a green suite. `docs/task-archive.md` Part 58 caught a
missing table exactly that way, and it has recurred since, green on every gate each time. **Derive the leg
from the pair of runs, never carry its size forward**: skipped(down) − skipped(up) = passed(up) −
passed(down), the same quantity from both sides, and it grows with the tree. A figure that reconciles by
that arithmetic is still not a measurement; attest only the Docker-up run.

**Every other skip is a live-backend gate**, so a skip count BELOW the baseline is the live suites running,
never a defect. The suites gated on something a download or a local install provides, and what un-skips
each:

| suite | un-skipped by |
|---|---|
| `OnnxProviderLiveTests` | `LYNTAI_ONNX_MODEL_DIR` (a sentence-transformers ONNX export) |
| `OnnxCrossEncoderLiveTests` | `LYNTAI_ONNX_RERANK_MODEL_DIR` (a cross-encoder export) |
| `Model2VecProviderLiveTests`, `WordPieceTokenizerLiveTests` | `LYNTAI_STATIC_MODEL_DIR` (a model2vec directory) |
| `LocalDiffusionLiveTests` | `LYNTAI_SD_CLI` + `LYNTAI_SD_MODEL` |
| `ComfyUiLiveTests` | `LYNTAI_COMFYUI_URL` + `LYNTAI_COMFYUI_CHECKPOINT` |
| `PiperLiveTests` | `LYNTAI_PIPER_CLI` + `LYNTAI_PIPER_MODEL` |

The rest ride a running server, CLI or MCP endpoint (`LYNTAI_LIVE_*`, the Ollama and MCP variables); each
suite's `Skip` message names what it needs.

## Escape tokens and allowances

**Each gate owns its OWN escape token**, deliberately — one token silencing two unrelated gates is a hole
nobody can see opening.

| gate | token | notes |
|---|---|---|
| `check-docs` | `drift-ok` | a line that deliberately names the retired thing |
| `check-links` | `link-ok` | a line naming a path as DATA — a guard fixture's name, say |
| `check-counts` | `count-ok` | a sentence quoting a HISTORICAL count ("the list said seven and had eleven") |
| `check-comments` | `comment-ok` | on a block's first line; reserve it for a block no reader would want shorter — a table, a wire-format capture |
| `check-measurements` | `measure-ok` | a line discussing ANOTHER result's retraction; naming what this row supersedes is the better fix, and it is also the bookkeeping the index needs |
| `check-decisions`, `check-archive`, `check-backlog`, `check-pitfalls`' length ratchet | **none** | an allowance is a visible ratcheted number and is the only way out |

**An allowance that stops matching FAILS, and one looser than the subject needs FAILS**, so exclusions
cannot rot and the numbers only ever come down. That rule applies to `retiredApiNames`' allow-list,
`commentBlockAllowances`, `decisionLengthAllowances`, `archiveEntryLengthAllowances`,
`pitfallLengthAllowances`, `optionDocAllowances` and `backlogPreambleAllowance` alike.

**Paying a ratchet down means RELOCATING, not deleting** — several long entries are the only maintained
home for a trap. Move the rule to the record that owns it, keep a pointer, then cut.

## Writing a new gate

- **Where the registry lives is decided by what an entry IS.** Data — a term, a name, an allowance, a
  closed vocabulary — goes in `devtools/project.config.mjs`. A predicate or a regex-plus-function over the
  tree goes in the gate script itself; that is why `COUNTED_CLAIMS` and `DECISION_CLAIMS` are not in the
  config file.
- **Extract a pure function seam** (`checkDocs(repo, config, log)` and its siblings) with the CLI entry
  point a thin wrapper, and test THAT — never by spawning a process.
- **A dead registry entry must FAIL.** A claim whose pattern matches nothing, an allowance that no longer
  applies, a facet value no trap uses: each is an entry that cannot expire, and without that rule a
  registry silently stops covering what it was written for.
- **A counter that computes nothing is reported as a BROKEN GATE, never as stale prose** — "fix the number"
  is wrong advice when the counter is what failed.
- **Pin every counter with a test against the real tree.** The first `verify`-gate counter returned the
  RIGHT total from two cancelling errors: its character class could not match `e2e`, and it counted the
  inner `['--tree']` argument array as a step. Only comparing the parsed NAMES caught it. A subtly-wrong
  counter is worse than none.
- **Fail closed on an empty scan.** A gate that scanned nothing must never print a tick.
- **`_repo-files.mjs` is the ONE file list every full-tree gate scans** — tracked files PLUS new files that
  are not ignored. Five gates carried their own copy of that rule and the blind spot was five-fold, because
  a scope rule diverges silently: every copy prints the same green line whatever it scanned.

## The gates inside `verify`

### `test-devtools` — the guards' own tests

Added because three `check-docs` defects had passed every gate for their whole lifetime, all three failing
in the PERMISSIVE direction. Writing the tests found two more, one of which meant `check-sensitive --tree`
silently skipped any file with a non-ASCII NAME — in this repository, `docs/灵台.md` could have carried a <!-- link-ok: a guard FIXTURE's name, never a file here -->
leak past a "clean" run. The three measured defects are pinned as regression tests and each was
mutation-checked against the old behaviour.

**Every guard script now has a test.** What is left untested is the two deterministic stubs
(`provider-stub`, `codex-stub`) and the live pack/restore/build/run of `consumer-smoke` itself — a seam
that stubbed the pack would test the bookkeeping and none of the risk.

**It also holds a baseline**: on a green run it compares its own counts against `CLAUDE.md`'s
`guard-script tests N/N` and fails on a mismatch or an absent sentence (§Which numbers a gate holds).

The two traps already paid for — `node --test <dir>` on Node 24, and a fixture carrying a literal the leak
scanner flags — are `.claude/rules/windows-machine.md` §Node and `.claude/rules/repo-mechanics.md` §Dev loop.

### `check-warnings` — a warning in a published project is a defect

Not style policing: `IsAotCompatible=true` stamps `IsTrimmable` into the assembly, so an unfailed
IL2026/IL3050 is a FALSE trim promise shipping to consumers — four did — and an unresolved doc cref ships
inside the XML docs consumers read.

### `check-packages` — a package must be registered everywhere

The NINE registries, in the order the gate checks them: `packableProjects`, the solution, the csproj's
`<Description>`, `ApiSurfaceTests.Assemblies()`, its SEPARATE `Loaded` anchor map, the baseline file, the
test project's `ProjectReference`, the `docs/AOT.md` row, the README table row — plus the reverse, so a
deleted package leaves nothing stale behind.

**The misses are silent**: a missing `ApiSurfaceTests` entry means no API gate at all, and nothing reports
that. Many small packages is the intended shape — `docs/DECISIONS.md` D27 — and
`node devtools/dev.mjs new-package <Lyntai.X>` scaffolds into all nine.

### `check-bundle` — bundle membership is a dependency BUDGET

The `Lyntai` bundle forces every dependency on every one-line-install consumer (an untrimmed publish copies
the whole graph), so membership is a budget rather than a convenience. `docs/DECISIONS.md` **D26** holds
the rule; `bundle.allowedThirdParty` in `devtools/project.config.mjs` is the approved list. Bundle
membership is deliberately NOT automatic when a package is scaffolded.

### `check-encoding` — mojibake no other gate can see

UTF-8 decoded as another codepage and written back: the file stays valid UTF-8, compiles, passes every test
and ships, so **no other gate can see it**.

Added 2026-08-13 because the RULE already existed — `.claude/rules/windows-machine.md` §Text and encoding —
and was still broken **three times in one session**, each time caught only because a person or a diff
happened to look.

**Patterns are stored as CODE POINTS and re-derived in the test** (`TextDecoder(enc).decode(...)`) rather
than quoted, because writing them from memory produced the wrong characters three times — the same mistake
the guard catches. That also means neither the guard nor its test contains the sequences it hunts, so **the
exclusion list is empty**: an exclusion is a hole, and the fix was to stop needing one.

**It also fails a C0 control character** other than tab, LF and CR. Authored text never holds one, and a
literal backspace written through an escape had corrupted a trap's own text where no other gate could see
it.

It runs in `verify` over the tree AND in the pre-commit hook over the STAGED blobs (`--staged`) — what the
commit will record, not the working tree, so a file repaired after staging cannot smuggle its corrupt blob
in, and an unrelated dirty file cannot block the commit.

### `check-docs` — vocabulary a decision retired

The prose counterpart to `check-warnings`. The registry is `retiredTerms` in `devtools/project.config.mjs`:
a term, what to say instead, and why. **Add an entry whenever a decision renames or re-dimensions
something** — and since 2026-09-19, half of forgetting is gated: **the PAIRING AUDIT fails the gate when a
`retiredApiNames` entry has names no `retiredTerms` rule matches and no `proseExempt`**, because D157's
renames entered the surface registry alone and README recommended a deleted registration for a day while
every gate reported clean. The prose rule stays HAND-WRITTEN — auto-deriving it was measured at 1,861 hits
(records, vocabulary retired on one seam and live on others, ordinary words) and refused as the cry-wolf
shape — so the audit forces the decision at rename time rather than writing the rule itself. The honest
residual limit: **a rename that enters NEITHER registry is still invisible to every gate here.**

Historical records (`CHANGELOG.md`, `docs/task-archive.md`) are exempt because they are accurate BY using
the vocabulary of their day; `CHANGELOG.md`'s live `## Unreleased` prefix is scanned, because that section
describes behaviour that has not shipped and can still change under the words describing it. The design
records are untracked (`local/superpowers/`) and never scanned.

**A partly-historical file is scanned exactly where it is LIVE**, and that answer is computed ONCE —
`liveLineMask`, with `liveLinesOnly` over it — for this gate, `check-links` and `check-samples` alike.
Answering it differently in three places is how the permissive copy goes unnoticed on whichever was
forgotten. Two shapes qualify: a live
PREFIX (`CHANGELOG.md`, down to its first version heading) and live REGIONS (**D164** — the design
contract's inline dated amendments, its present-tense tier). Historical lines are BLANKED rather than
sliced out, so `file:line` stays true and a two-line window can never join live prose to a record's. **The
prefix is transient and the regions are not**, which is why `check-samples` refuses a compiled sample in
the former: the release stamp deletes that region on its way past.

**It also scans code COMMENTS** in `src/`, `tests/`, `bench/` and `devtools/` — the compiler resolves
`<see cref>` and nothing else, so a retired CLAIM in a `<c>` tag or a `//` comment was checked by no one.
Three `devtools/` files are out because quoting retired vocabulary is their job: the registry itself
(`project.config.mjs`), the roster of published package ids (`nuget-unlist.mjs`) and the guards' own tests,
whose fixtures are the terms. Excluding the whole tier once hid a stale embedder name in a sweep script.
**An empty `retiredTerms` registry FAILS** rather than reporting a clean tree it never checked.

**It matches on a TWO-LINE WINDOW**, because these documents wrap at ~110 columns and the sentence the gate
most needs to catch is the one most likely to straddle a break. A consequence worth knowing when moving
prose: **re-wrapping alone can trip it**, since a reflow joins text that was never adjacent. Run it after
each relocation, not only at the end.

### `check-links` — whether what a document POINTS AT still exists

`check-docs` asks whether a document still SAYS what a decision settled; this asks whether what it POINTS
AT is still there. It exists because `docs/superpowers/INDEX.md` had always ended its archiving procedure
with *"repoint every inbound reference, and check nothing dangles"* — and that step was simply skipped on
the one archive that had happened, leaving **six** dead references in maintained state (README ×3, the
design contract, `docs/DECISIONS.md` ×2) plus two in `CHANGELOG.md`'s live prefix. Every gate reported
clean; a reader found them.

It checks a reference **four ways**, because there are four ways one rots.

**The path half** asks whether the target still exists.

**The Part half** asks whether a reference naming a task record names the record that actually holds it —
the path resolves and the Part exists, in the OTHER file, so nothing else can see it. **Archiving a task is
what breaks these**, silently, for every inbound reference; five were live on 2026-08-14. A bare `Part 53`
with no record named is deliberately ignored — only a reference that NAMES one makes a checkable claim.

**It reached the CODE tier on 2026-09-16, and the delay is the lesson.** The tier had been scanned for
paths, sections and members since 2026-08-15, and the Part half was left out on a stated measurement — *"a
task-record reference is a prose convention, and the measurement found none in code"*. Re-run, that scan
finds **150 across 73 files**: every bench sweep and several gate scripts name the thread they belong to.
**88 of them were dead**, accumulated silently over every archiving since, and the four Parts retired that
day broke 28 more while the gate reported the tree clean. **A scope justified by a measurement needs the
measurement re-run when the tree grows around it** — the same expiry a blocked backlog item has, and it
expires the same invisible way. The code tier uses a two-line window for this half alone, because a Part
reference straddles a wrap for the reason it does in prose; 2 of the 150 do.

**It still stops at COMMENT lines, and that narrowing was re-measured rather than inherited.** Ten dead
references sat in `Console.WriteLine` strings — user-visible bench output, arguably worse than a comment —
which argues for dropping the restriction. Measured, dropping it yields **21 hits of which 11 are
`check-links.test.mjs`'s own fixtures**: synthetic `## Part 70 — still open` content the test builds to
drive the gate. A gate that fires on its own test's inputs is the cry-wolf shape, and it is exactly the
case `check-docs` states the rule for — *a term inside a string literal is data the program uses, not a
claim a reader believes*. The ten were fixed by hand; the narrowing stays. A comment line is `//`, `///` or
a block comment's `*` — never `#`, which in a `.cs` or `.mjs` file is code — and the guards' own tests are
scanned like any other file: their fixtures sit in string literals, and a comment NAMING a fixture takes
`link-ok`.

**`link-ok` excuses the line it sits on.** On the NEXT line it excuses only a match that straddles the
join, one neither line shows whole; a match the line alone can see needs its own line's token. The member
half reads the raw line, so a neighbouring token never reaches it.

**The section half** (added 2026-08-28) asks whether a `§`-citation names a heading that is there.
**Renumbering or FOLDING a section is what breaks these**: `docs/memory.md`'s `## 8. What is NOT measured`
was folded into `## 7` with §9/§10 left un-renumbered, and seven citations across six files kept naming it.

It was MEASURED before it was built, because the honest prior was discouraging: over the pre-fix tree, 100
citations in the unambiguous form, 12 flagged, **8 real defects**, and every false positive inside the
historical archive. Two design consequences, both from that run — a **bold bullet lead** counts as an
anchor (`.claude/rules/task-lifecycle.md` §Keep the summary honest names one, and it was the only recurring
false positive), and **every end of a RANGE is a claim**, since `§7–8` is invisible to a `§8` grep and
resolves under a first-number-only rule. That range form escaped both the human pass that filed the item
and the first probe written to measure it.

**Repointing is the fix and renumbering is the trap** — renumbering makes an existing citation resolve
silently to the WRONG section.

**A citation that resolves only through `## Unreleased` is refused on every run** (added 2026-09-26). The
release workflow stamps that heading with a version before it runs `verify`, so the citation resolves on
every ordinary run and dangles in the release alone — measured that way on a real release run
(`docs/FIXES.md`). Each citation is also resolved against the headings the stamp leaves alone, keyed on the
stamper's own `unreleasedHeading`, so the gate and the stamper cannot disagree about which heading goes.
It is the transient-region rule §check-samples states for a compiled fence, applied to a citation.

**The member half** (added 2026-08-30) asks whether a `Type.Member` citation — or a `see cref` naming one —
points at an identifier that EXISTS. The other three are structurally blind to it: the path resolves, the
record is right, the § is there, and only the member name is invented. Measured the same way, and the
numbers are the best of the four: **770 citations, ONE flag**, and zero in the code tier, which is why that
tier is included un-narrowed. Two design consequences, both forced by a run — the LEFT side must be a type
this repository DECLARES (which drops `Lyntai.Bundle`, a package id, by principle rather than by an
exclusion list, at the stated cost of never checking a BCL member), and the vocabulary is harvested from
code with **comments STRIPPED**, because an index built from prose lets a citation AUTHORIZE ITSELF: write
`Type.Nonexistent` in a `//` comment and the name joins the vocabulary, after which nothing can ever flag
it. That second one was caught by the gate's own test, not by review.

**Scope.** It scans the CODE tiers too, but narrower than the prose scan on two axes: **comment lines
only** (a path in a string literal is data the program uses, not a reference a reader follows) and — for
the PATH half alone — **`docs/` targets only**, because source files are renamed for legitimate reasons
while a moved DOCUMENT is the defect this gate exists for. The entry proposed a third narrowing, `///` XML
docs only, and **the measurement refused it**: replaying the pre-repair tree, 9 genuine dead references
lived in the code tiers, an XML-only rule catches 6, and all 3 it misses were in ordinary `//` comments and
all 3 were real. The hypothesis had been that `//` comments are where false positives live; every false
positive was in fact a guard script naming a FIXTURE, which is what `link-ok` is for. Cost: six
annotations, once.

**The `docs/`-only narrowing is the PATH half's alone**, and the section half deliberately does not take
it: that narrowing exists because source files get renamed for good reasons, an argument that cannot apply
to a citation whose target is a `.md` by construction. Three of the seven measured dead citations lived in
the code tiers, and the narrowing would cost most of the tier's coverage: of the **42** §-citations those
files carry, only **13** name `docs/`.

**EXISTENCE only, never line numbers** — a `file.cs:123` reference rots on the next edit for entirely
legitimate reasons, and `pitfalls.md` records line numbers rotting twice and being deleted in favour of
names; gating them would fail every refactor for no defect. `local/**` is skipped (untracked by design).

**One structural blind spot, stated so nobody relies on the gate for it.** A citation of the form
`` `CLAUDE.md` `` — a bare basename with no directory prefix — is invisible to the path half, and a
`§"quoted heading"` citation is invisible to the section half. Both forms exist in the tree and both rot in
total silence. Prefer the filename-plus-`§` form whenever a pointer survives a cut; it costs ~35 characters
and buys a real gate.

### `check-counts` — whether what a document COUNTS is still true

The THIRD member of the prose family. `check-docs` structurally cannot see a stale count — its registry
holds vocabulary a decision RETIRED, and a count going stale retires nothing, so the sentence stays
grammatical, plausible and wrong.

Measured cost of not having it, and it is the strongest case any gate here has: `docs/task-archive.md`
Part 73 found **six** corrections to a counted claim inside sixty commits, and **two more went stale during
the session that built the gate** — eight incidents, every one caught by a person, four of them by a person
who happened to be counting something else.

The registry is `COUNTED_CLAIMS` in the script rather than `project.config.mjs`, because an entry is a
regex plus a FUNCTION over the tree.

**Patterns are deliberately NARROW**, and each is required to match at least once, so a pattern too narrow
to find its own claim fails rather than passing silently. That cuts both ways and is the trap to know
before editing any prose a claim is anchored in: **deleting or rewording the only sentence a claim matches
makes the entry dead and turns the gate red.** Move such a sentence VERBATIM and run the gate between the
copy and the delete.

**Number words have no hyphenated compounds** — `parseCount('twenty-one')` returns null, the occurrence is
skipped rather than compared, and if it is the only occurrence the claim matches nothing. Write digits past
twenty.

**Its honest limit, stated rather than discovered: it only covers counts somebody REGISTERED**, so it is a
gate against recurrence and not a proof that every number in the docs is right.

### `check-comments` — whether a comment is still doing a comment's job

The FOURTH member of the prose family and the only one that looks at CODE — all four tiers (`src`, `tests`,
`devtools`, `bench`), `.cs` and `.mjs`.

Measured cost of not having it: **0.86 comment lines per line of real code** in `src/` (14,927 against
17,259, blanks and brace-only lines excluded), and `src/` carrying **1.6× more prose than
`docs/DECISIONS.md` + `.claude/knowledge/pitfalls.md` + `docs/task-archive.md` + the design contract
COMBINED** — prose in the one place no other gate scanned, rotting exactly as `pitfalls.md` records. A
third of it sat in blocks of 16+ lines, including a 120-line `<remarks>` on one method. The rule it
enforces is `.claude/rules/code-commentary.md`.

**It fails three other things besides length**, each added because a real defect walked past it: a doc run
carrying more than one `<summary>` (eight found — a long block describing member A sitting above member B's
own summary, so B has two and A has none; the compiler does not warn and it SHIPS), punctuation a deleted
clause left stranded (two shipped in public XML docs), and an allowance that no longer matches. A `/* … */`
block is measured like a run of `//` lines, and two JSDoc blocks stacked over one declaration fail as the
`.mjs` twin of the doubled `<summary>`.

**It is a RATCHET, not a threshold.** 49 files were already over the 25-line limit when it landed (1,879
lines), so a plain threshold would have been switched off on day one. Every over-limit block in a file is
recorded in `commentBlockAllowances`, as the MULTISET rather than just the file's worst — one number per
file left 279 lines of debt invisible and let a budgeted file grow new long blocks behind it.

**The 25-line proxy, its one exception (`IMemoryGraphStore.SeedAsync`, seven guarantees stated once each)
and the rule to be slow about claiming a second are `.claude/rules/code-commentary.md`.** What this gate
measured behind that rule: the sweep that introduced it declared 19 files irreducible — "contract, not
fat" — and an adversarial re-check the same day found that honest for **two**. Fourteen came under 25 by
relocating exactly what the rule's always-wrong list names, and the most common single offender was a
paragraph that points at a record and then restates it anyway. The same claim was made from having done
the work rather than from counting what was left, citing a number from the LEDGER rather than the tree —
which is how 279 lines of debt stayed invisible.

### `check-decisions` — whether a decision entry outgrew the decision

The second length ratchet, pointed at the record rather than the code. Measured 2026-08-28: mean non-blank
lines per entry went **11.6** across the log's first third → **14.2** across the second → **38.5** across
the last, a 3.3× growth in the file `CLAUDE.md` routes every session to BY RANGE. Not the entry count and
not data — that last third holds 40 table rows and 19 lines carrying a figure, in 1,556 lines. It is prose:
amendment narrative written in place, and several decisions stacked under one number.

`.claude/rules/persist-working-state.md` had already recorded the FIRST version of this and answered it
with a rule; that rule held on entry COUNT and did nothing about LENGTH, which is the argument for a gate.
The same measurement in its other form: by 2026-08-14 the log held 72 entries, of which **23 were created
on two consecutive days** — a session-note rate, not a decision rate — and six announced in their own
titles that they were a work session. One entry was a pure trap that belonged in `pitfalls.md`.

**It is NOT the archive's compression** — a decision's reasoning is its payload, so the gate bounds an
entry and never asks for one to be summarized away. Paying one down moves MEASUREMENT narrative to the
design record that owns it and AMENDMENT narrative to git history.

Limit **35** non-blank lines (median 16, p75 35); 21 entries were already over it, so it is a ratchet.

### `check-archive` — whether an archive entry outgrew the OUTCOME

The THIRD length ratchet. It shares its whole body with `check-decisions` (`devtools/scripts/_entry-length.mjs`),
as `check-pitfalls`' per-trap ratchet does — the ledger semantics are the subtle half and a second
hand-written copy would drift silently in the permissive direction.

**What it gates is different from its sibling's**, which is the point: a decision's reasoning IS its
payload, while an archive entry's detail belongs to whichever record owns it, so what this removes is
**DUPLICATION**. The measured cost: a retraction landed on a run whose narrative sat in BOTH the archive
entry and `docs/memory-measurements.md` §5, and only one of the two got edited.

Limit **20** non-blank lines against the rule's own "roughly ten lines does that" — chosen 2026-09-04, when
the median entry was 10 and p75 was 16, so the median already complied and the whole weight sat in the
tail. **Those figures are DATED on purpose**: paying debt down moves them (the same day's paydown took the
median to 9 and the worst entry from 40 to 27), so an undated distribution would go stale exactly when the
gate is working.

**The argument for building it is that the rule already existed and lost**: `.claude/rules/task-lifecycle.md`
was written on 2026-09-02 from a measurement (thirds 8.0 → 6.1 → 22.2) and the file grew anyway, reading
7.5 → 6.7 → 23.2 two days later.

### `check-backlog` — whether the OPEN backlog started summarizing the ARCHIVE

The FOURTH length ratchet, aimed at the file `.claude/rules/task-lifecycle.md` is most opinionated about
and the one that had no gate at all.

Measured 2026-09-10: the `## Active backlog` preamble had reached **478 lines carrying ZERO open
checkboxes** — five stacked `HANDOVER` blocks plus a running tally of what had closed — in a 1,308-line
file holding 17 open items. **The file had RECORDED deleting a 49-line tally for that exact reason on
2026-09-03 and then regrew a 19-line one in the same place.**

It checks FIVE things. Two are LENGTH: the preamble's non-blank line count against **40**
(`backlogPreambleAllowance`, a ratchet, no escape token), and that **no `HANDOVER` block survives
anywhere** — a handover describes work that is DONE, so its home is `docs/task-archive.md`, one Part per
task. The generated roster is excluded from the budget by sitting above the `## Active backlog` heading,
which is placement rather than a carve-out.

The other two are the **open-item MANIFEST** (`docs/DECISIONS.md` **D111**): every open `- [ ]` carries a
`<!-- item: … -->` marker on its own line and the table at the head of `TASKS.md` is GENERATED from those
markers, so a hand-edited table fails, and a blocker with no KIND or no testable `needs` fails too.
**State is AUTHORED and never inferred**, which is the whole design: `pitfalls.md` refuted deriving that
banner from the checkboxes, because "this is a WATCH item" is not computable from a `- [ ]`. The banner it
replaced advertised finished work four times.

The FIFTH, added 2026-09-16 (BL2): **a `## Part` heading holding no open checkbox FAILS.** The preamble
budget stops at the first `## Part` — `PREAMBLE_END`'s own boundary — so everything below it was unbounded,
and the same accumulation simply moved one level down: four Parts reached **295 lines and ZERO open
checkboxes**, every line a "CLOSED as archive Part N" note, while the preamble rule sat green above them.
That is the file's own conclusion applied to itself, twice over: *a rule that keeps being violated is a
missing gate*, and the first gate for it was scoped to where the defect had been rather than to where it
lives.

**An empty Part is never legitimate**, which is what makes this safe to fail on rather than warn about: a
Part is a GROUPING of open items, and an emptied one reads as a live home for its question — two stale
claims were reachable through exactly that. Struck-through items are not `- [ ]` and correctly do not keep
one alive. It is blind to any heading that is not `## Part <n>`, so a retirement can leave a pointer
behind without tripping the rule it just satisfied. Counted from the RAW checkbox line, so a Part whose
items all have broken markers is reported once, as a marker problem.

### `check-pitfalls` — whether a trap is filed, and its facet index current

The same authored-marker/generated-index shape as `check-backlog` — they share
`devtools/scripts/_markers.mjs` — pointed at the other measured cold-start cost.

**Retitling the headings was REFUSED, and that is the whole design.** A cold-start probe needed the traps
bearing on its task and the nine relevant ones spanned **five of nine headings**, two of which nobody
looking for that task would have opened. Any single hierarchy files a trap in one place and these belong in
two, so each carries `<!-- trap: sub=… shape=… -->` — **`sub=`** is which area of the repository breaks,
**`shape=`** is how the wrongness stays invisible, and the two are ORTHOGONAL to the headings. `shape` is
the half that transfers: most of these traps recur in a subsystem that had never met them.

Both vocabularies are CLOSED (`pitfallFacets` in `devtools/project.config.mjs`) and an unknown value fails
— an open one is a folksonomy, where the fourteenth synonym for "the check never ran" makes the index worse
than none because a reader who searches one believes they have seen them all. **A value NO TRAP USES also
fails.**

The index is **line numbers only**, deliberately: `decisions-index` was measured the same day and barely
helped LOCATING cost (~66 lines read against ~117), so an index sized for READING is over-building.

Adding or moving a trap means re-running `check-pitfalls --write`, because the trap count sits inside the
generated block.

**Its fifth check is the FIFTH length ratchet: no trap may outgrow `MAX_TRAP` = 14 non-blank lines.**
Measured 2026-09-25 over 224 traps — median 7, p95 12, p99 14, max 19 — and set at twice the median, the
rule `check-decisions` uses. The file is read "before extending anything" and nothing had bounded what it
asks a reader to hold. The ledger is `_entry-length.mjs`'s, shared with `check-decisions` and
`check-archive`; an allowance (`pitfallLengthAllowances`) is keyed by the start of the trap's lead with
markup stripped, because a trap has no id and its line moves with every edit above it, and a key matching
no trap or two FAILS. No escape token.

### `check-options` — whether a shipped option says what it is FOR

**The obligation this enforces is the owner's, stated 2026-09-12:** which model to run and which option to
select is the consuming application's job, so the library's job is to document all of them and why they
exist. A settable property on a public `*Options` type is exactly where that lands — it is what a consumer
assigns, and an undocumented one meets them in IntelliSense as a bare name with no hint of what it buys or
what its default costs.

**It catches NOTHING-AT-ALL and deliberately stops there**, which is the part to understand before widening
it. The first run found **5 undocumented options in 5 hits** — all genuine, four of them on
`AgentSessionOptions`, including `Model`, an option named for the very thing that is explicitly the
application's choice. The wider rule that suggests itself — *a one-line doc says WHAT and not WHY* — was
refused on measurement: **53** options have exactly one line and most are correct, because `ApiKey` and
`BaseUrl` do not need an essay. `.claude/knowledge/pitfalls.md` records two gates this repository built,
measured at a **0% defect rate**, and withdrew; a hit here is a defect by construction, which is the bar a
vocabulary gate has to clear before it ships. The one-line count is REPORTED on a passing run so the softer
tier stays visible without being enforced.

**What counts as an option was widened once, on a measured miss**: an accessor block on the line after the
property's name, an expression-bodied accessor and a positional record's parameters were all invisible to
the first pattern — 41 options nothing checked — so it reads each of those shapes.

**It fails closed on an empty scan** — a scan finding no options has proved the pattern stopped matching,
not that the tree is clean — and `optionDocAllowances` (`devtools/project.config.mjs`) excuses a single
option with a reason, where **a dead allowance FAILS** the moment its option is documented, renamed or
deleted. Scope is `src/` only: a test's or a bench's options type is an instrument, not a contract.

**What it does NOT hold:** whether the prose is any good, whether a default is right, or whether the doc
still describes the code — that last is `check-decision-claims`' question, one tier up, and only for
options a decision governs.

### `check-dev-loop` — whether the command table a session reads first is still the tooling's

The THIRD gate on the `devtools/scripts/_markers.mjs` seam, beside `check-backlog` (**D111**) and
`check-pitfalls` (**D112**), and the one whose subject is the roster itself: the `## Dev loop` table in
`CLAUDE.md`. The command NAMES and the `verify` column are IMPORTED from `devtools/commands.mjs`, the
side-effect-free roster `dev.mjs` dispatches over, so only each description is authored
(`devLoopCommands`) — `node devtools/dev.mjs check-dev-loop --write` rebuilds the table and `verify` fails
while the two disagree.

**It exists because the same drift already happened one layer down.** `dev.mjs`'s usage string was once a
hand-kept literal and slid to 24 of 30 commands — every memory sweep but one, plus a gate on the day it was
added — so the list CLAUDE.md called authoritative had quietly stopped being one. A prose table in the file
every session reads FIRST is that defect with a wider blast radius: a command nobody can see is a command
nobody runs, and the gate that would have caught it is the one most likely to be the missing row.

### `check-measurements` — whether a published FIGURE is still the current one

The FOURTH gate on the `devtools/scripts/_markers.mjs` seam, and the only one whose subject is a NUMBER
rather than a name. **None of the prose family can see this defect**: a figure quietly superseded retires
no vocabulary, dangles no reference and moves no registered count, so the sentence stays grammatical,
precise and wrong.

Measured 2026-09-10, by the cold-start probe that asked *"what is the best memory configuration"*: ~595
lines read, 55% of them wasted, and the answer **not current — unverifiable by construction**. The reason
is structural. The measurement record **RETRACTS INLINE**: a figure published in one section is corrected
three sections down, mid-paragraph, and one section read on its own is confidently out of date.

**Heading-level indexing is the fix that looks sufficient and is not**, which is the whole design. A
heading-grep is blind to a mid-section retraction, so the index is at RESULT-ROW granularity — a section
carries a second `<!-- result: … -->` marker whenever it holds a result whose currency differs from its
heading's, and that second marker is what lets the gate tell the live half from the dead half.

**`SUPERSEDED` is never authored.** An author writes `CURRENT` or `RETRACTED` and names what a new result
replaces in `supersedes=`; the index derives the rest. Two-sided bookkeeping is exactly where a record like
this rots, because the losing half of the pair is the half nobody revisits — and a hand-written
`status=SUPERSEDED` fails with that as its message. `supersedes=` resolves BACKWARDS only, which is what
makes a cycle unrepresentable rather than merely unlikely.

The check that repays the rest: **a result still reading CURRENT whose own body says something was
corrected, retracted, went stale or was superseded FAILS.** The common false positive — a row that is doing
the superseding — is exempted by `supersedes=` rather than by a token, so the escape for the ordinary case
is the bookkeeping the index needed anyway. `measure-ok` remains for a line genuinely discussing a third
result.

`measurementMetrics` (`devtools/project.config.mjs`) is CLOSED and a slug no result uses fails, for
`pitfallFacets`' reason. Two columns carry the questions the probe could not answer — **`ships`**, whether
the arm measured is the library's shipped default, and the derived status — and the index reports how many
rows are on the shipped arm, because reading a ladder rung, a ceiling or an oracle as a configuration
recommendation is the specific mistake this record invites.

**Two honest limits.** The gate checks that a status is WELL-FORMED, never that it is TRUE: a result
silently superseded by a run nobody wrote down is invisible, and the index asserts completeness. And a
**bare `§5`** — no filename — is checked by nothing, here or in `check-links`, whose pattern needs a
filename by design; splitting this record turned eight of them into silent lies in one commit, which is
recorded in `.claude/knowledge/pitfalls.md` §Refactoring & namespace moves.

### `check-decision-claims` — whether a DECISION still describes the code it governs

Its sibling `check-decisions` gates an entry's LENGTH; this gates its TRUTH, and it is the FIFTH member of
the prose family. **None of the other four can see it**: a decision going stale retires no vocabulary,
dangles no path and moves no registered count, so the sentence stays grammatical, plausible and wrong — and
`decisions-index` renders a stale TITLE into the index table on top of that.

Measured cost of not having it (2026-08-31, `docs/task-archive.md` Part 186): auditing the log against the
tree found it **accurate about VALUES and drifting on COUNTS and CLASSIFICATIONS** — every stated constant
verified, while D46's own title said "four DOMAINS" against seven and `CLAUDE.md` claimed five required
`IMemoryGraphStore` members against thirteen, having dropped the "in this major" qualifier D67 carries.

Registry is `DECISION_CLAIMS` in the script rather than `project.config.mjs`, because an entry is a
predicate over the tree. **Every registered predicate was verified BY HAND before being registered**, and
each is driven RED by a synthesized tree in its own test: a predicate nobody checked is a second unverified
claim, not a gate.

**Widened 2026-09-17 from ten claims to thirteen, over the D125–D147 band** (eleven after the two
retirements below) — nine decisions landed in a day, reshaped the whole provider layer, and not one of them was re-checked by anything. The three added are
the ones whose violation is SILENT rather than loud: **D25** (a third-party dependency in `Lyntai.Core`,
which every consumer is forced to take, and which is one line that compiles and passes every test — D146
deleted a 654 KB reference that had arrived exactly that way); **D127** (only `Id` and `Capabilities` are
required of an `IModelProvider`, which is precisely what one collapsed interface bought — a member declared
without `=>` compiles here, where every implementation is in this solution, and breaks every BYO provider
on upgrade); and **D129** (nothing outside `Core/Embeddings/` implements the embedding front door, because a backend
that does is reachable without routing, fallback or the capability filter).

**The D129 claim was RETIRED the same day, and how it died is the more useful half.** **D151** deleted the
embedder interface, so no file could declare it, so the predicate could never return anything again: green
for ever, over a rule with nothing left to break. A vacuous claim is worse than an absent one because it
reports a rule as HELD — and this gate's own header already calls an unchecked predicate "a second
unverified claim, not a gate". **A claim outlives the decision it encodes only while its SUBJECT does**;
when a later decision removes the subject, retire the claim rather than enjoy the green. The surviving
invariant — no embedder-shaped front door comes back — is vocabulary, so it is `retiredApiNames` against
the frozen surface, where a second copy here would add nothing.

**The D46 claim was retired for the opposite reason: it could not fail on the drift it was written for.**
It matched `DOMAINS are SEVEN` in `CLAUDE.md` with the word hard-coded as an alternative, so an eighth domain
folder still read HELD. `check-counts` already gates that sentence against the tree, so the claim went and
D47 now reads `check-counts`' root-level exemption registry rather than restating it — one registry for
both gates. No predicate here reads `CLAUDE.md` any more. A registered root that is missing (D14's)
FAILS, like every absent input this repository's gates read.

**Its first version of the D127 predicate could not fail, and that is worth keeping.** Blanking `{ get; }`
to a placeholder without its terminator merged each property into the NEXT member's chunk, so the first
`=>` in the run made everything look defaulted: it reported ZERO required members on an interface that has
two. It passed, it looked right, and it would have passed just as happily on the defect it exists to catch.
The test now asserts the POSITIVE control — that the predicate finds `Id` and `Capabilities` — before
asserting anything about a violation. **A gate's red case proves the pattern; only a positive control
proves the gate was looking.**

**Its honest limit**: it covers claims somebody registered, so it is a gate against recurrence rather than
a proof that every decision is true — and prose claims with no extractable shape are invisible to it.

### `check-api-vocabulary` — whether the frozen SURFACE reintroduced a retired name

`check-docs`' twin: one asks whether the PROSE still says what a decision settled, the other asks it of the
SURFACE. It exists because the API baseline **records parameter names without judging them** — it reports
THAT a name changed, never that one SHOULD have — so three retired parameter names reached the eve of the
3.0 freeze and a human, not a gate, caught them.

Registry is `retiredApiNames` in `devtools/project.config.mjs`, deliberately NOT `retiredTerms`: prose
needs loose patterns, a baseline needs whole-identifier equality. Escapes live in the registry, since a
generated file cannot hold a `drift-ok`.

**Its limit, stated rather than oversold:** it catches reintroduction of an EXACT retired identifier, not
every descendant of a retired word — a rule naming `ISalienceAppraiser` would not have caught the method
`Appraise`, which is how that one survived.

### `check-tautology` — what a rename LEAVES BEHIND, rather than what it retires

The third sibling of `check-docs`, and it exists because the other two are satisfied by the defect. When a
decision unifies two seams, the sweep rewrites both old names to the survivor. A sentence that merely
mentioned one is now correct; a sentence that **contrasted** them now names the same thing on both sides
and says nothing. The retired name is gone, so `check-docs` is happy; the survivor resolves, so
`check-links` is happy; prose was never on a baseline, so `check-api-vocabulary` never looked.

**It has happened, and the first remedy treated one file.** `check-docs`' own `HISTORICAL` list records the
2026-09-15 sweep collapsing a contrast in `docs/2026-07-17-lyntai-design.md`, and exempted that file —
whole, until **D164** narrowed the exemption to its seeds and period blockquotes: the inline dated
amendments, the file's present-tense contract tier, are scanned again through `LIVE_REGIONS`. The
same sweep left **six** more — `docs/DECISIONS.md` (D36, D128, D130), `CHANGELOG.md`,
`.claude/knowledge/pitfalls.md` and a shipped `//` comment in `Lyntai.Core` — plus four in `README.md` that
this rule's shape does not reach, one of them a compiled sample type-testing `IModelProvider` against
`IModelProvider`. <!-- tautology-ok: naming the defect this gate catches -->

**Measured before built**, per §Writing a new gate: the prose-only scan returned **6 defects and 0 false
positives** on the unfixed tree, against the 63-hits/0-defects and 42-of-50 signals this file records as
REFUSED. Validated by driving it RED against `git show HEAD:` copies of the six files — synthesized
fixtures prove the patterns, the real pre-fix tree proves the gate.

**It scans WIDER than `check-docs`, on purpose.** That gate exempts historical records because they are
accurate BY using the vocabulary of their day. A tautology was never anyone's vocabulary — it was wrong the
moment the sweep wrote it, in every era — so released `CHANGELOG.md` sections and the dated plans of record
are in scope here, and two of the six lived exactly there.

**The RENAME shape was missing, and it is the one most exposed to a rename campaign** (added 2026-09-17).
The first four patterns are all CONTRAST joiners — `and` / `or` / a slash / a parenthesised pair — and a
rename entry is not written that way. It is written old-then-new, so a sweep replacing every occurrence of
the old name rewrites BOTH sides and leaves "`X` is renamed `X`". `CHANGELOG.md`'s Breaking section is
written almost entirely in that shape, which is where it was found: a LIVE entry under `## Unreleased`,
release-facing prose, carrying it past every green gate. Five verbs and three arrow spellings now fire.
**Measured before adding, as above: 1 true hit against 0 false over 811 files**, and driven RED against the
real pre-fix `CHANGELOG.md` rather than only against fixtures.

**Its limit:** it catches a contrast collapsed onto ONE name. The `README.md` four are the other half — <!-- drift-ok: naming the retired seam is this paragraph's subject -->
`IGenerationStreamProvider` rewritten to `IModelProvider` beside a DIFFERENT surviving name, which no <!-- drift-ok: names the retired seam deliberately -->
backreference can see. Escape token: `tautology-ok`, its own and no other gate's.

### `check-samples` — whether a documented C# sample compiles

Blocks are wrapped by shape and built against the real projects; **default-ON**, because an opt-IN marker
makes coverage whatever someone remembered to tag. It runs in 6.0s, which is why it is in `verify` rather
than out beside `consumer-smoke`, and **it is the only gate whose subject breaks from editing a `.md`
alone** — which is exactly what a session does right before running `verify`.

Two annotations go before the fence. `<!-- compile-given: <declarations> -->` supplies the reader-side
context a fragment assumes and keeps the block **compiled** — and the declarations are compiled too, so a
given naming a nonexistent type fails like any other unresolved name, reported at the annotation's own
line. It cannot be used to wave a sample through, only to supply a context that genuinely type-checks; 5 of
the first 12 written were wrong and said so. `<!-- compile-skip: <reason> -->` takes a block out, correct
only where no context would help (a partial signature, a before/after pair, a menu of alternatives) or
where the context needed is a whole program — the BYO-seams tour needs ~26 lines of given for a 16-line
sample, and that is the line. Both on one block is an **error**, not a precedence rule.

**A compiled sample may not sit in a live PREFIX, and that is a release rule rather than a style one.**
`CHANGELOG.md` is historical except for `## Unreleased`, and the release workflow stamps that heading with
a version *before* it runs `verify` — so a fence there is compiled on every ordinary run and historical the
instant a release starts. The census moves under the gate's own feet and the run fails inside the pipeline
on a tree that was green minutes earlier, which is precisely the "red during every release" failure this
document warns about two sections down. Measured that way on 2026-09-19 (`docs/FIXES.md`), and now refused
with the stamp named: put the runnable recipe in a maintained document and let the entry point at it. The
rule keys on the `LIVE_PREFIX` registry, so an amendment region — permanent by nature — is untouched, and
a `compile-skip` fence in a changelog entry is still fine because only the compiled census moves.
`<!-- compile-skip-file: … -->` opts out a historical document. **Prefer `compile-given`: a skip is
unchecked, a given is checked.**

Measured cost of not having it: a README sample passing `task:` where the parameter is `taskKey`, and four
passing `/* … */` where an argument is required — a consumer copying either gets a compile error.

**Two ways the gate itself lied on its first runs** are in `pitfalls.md`: Roslyn binds NOTHING in a
compilation carrying a syntax error (so 11 samples naming nonexistent types were reported as compiling),
and a type declared in a sample OUTRANKS the same type from a referenced assembly compilation-wide
(`CS0436` is only a warning).

**Its sample-count check reads `CLAUDE.md` BY PATH and FAILS on an absent claim**, as `test-devtools`' does
(§Which numbers a gate holds). It once failed open, so as not to dictate prose — which made deleting the
baseline sentence a way to stop a real `verify` check with no failure and no output.

### `check-sensitive` — the leak scan

Runs in `verify` over the whole tree (`--tree`) and, on the staged blobs, in `devtools/hooks/pre-commit`;
the hook, the private pattern file and why a committed leak is a history rewrite are
`.claude/rules/repo-mechanics.md` §Sensitive info. A machine path is caught in every spelling it gets
written in — a backslash, a doubled one, a forward slash, the `/c/…` form — and a `--tree` run over an
EMPTY listing fails rather than reporting a clean tree it never read.

## Outside `verify`, and why

### `consumer-smoke` — the release gate

Packs every package to a scratch feed under a throwaway version, then restores + builds + runs a fresh
console app against the PACKAGES rather than project references. **The only check that exercises what
actually ships** — nuspecs, dependency groups, symbol packages, the bundle restore. Minutes, so
deliberately out of `verify`; run it before a release or after touching packaging.

**Being outside `verify` has a cost this entry did not state, and it was paid on 2026-09-17: the gate was
BROKEN and nothing said so.** Its consumer app is a fixture written in the library's own public API, and the
D125–D147 rename campaign went straight past it — `GenerationCandidate` after **D125** replaced it, a <!-- drift-ok: the retired name is what the fixture still held -->
`defaultModel:` argument after **D132** reshaped the registrations, and two types that had moved to
`Lyntai.Inference`. Four compile errors, sitting there across the largest breaking change in the project's
history, discovered only when someone ran it. **A gate nothing schedules rots exactly as an unrun test
does** — and this one surfaces its rot at release time, which is the worst moment to meet it. The trap is in
`.claude/knowledge/pitfalls.md`; the fixture's own staleness is `docs/FIXES.md`.

### `doctor` — the three version checks

README `## Status` ↔ `VersionPrefix`; `CLAUDE.md`'s `**Released:**` claim ↔ `VersionPrefix` and ↔ the
CHANGELOG heading's date; `VersionPrefix` ↔ the newest `v*` tag.

The middle one was added 2026-08-30, after `CLAUDE.md` announced **v3.0.0** for a whole release while the
other two agreed on 3.1.0: they check each other and `CLAUDE.md` was in neither, so the one copy
auto-loaded into every session was the one nothing held. It checks the DATE too, because the half-fix —
bump the version, leave the date — reads as synced.

Check-only, and deliberately NOT in `verify`: the release workflow bumps `VersionPrefix` before anything
stamps the CHANGELOG, and a gate that is red during every release is one people learn to skip. **The
corollary matters for any paydown of `CLAUDE.md`: a broken `**Released:**` claim passes a green `verify`.**

### `decisions-index` — the generated index at the head of `docs/DECISIONS.md`

Run it after adding a `D<n>` entry; `--check` reports staleness without writing. Deliberately NOT in
`verify`: a stale index costs a reader one `Ctrl-F`, and `verify` stays the build/test gate rather than
growing a documentation check. `check-docs` IS in `verify` by the opposite argument — a stale contract
costs an implementation.

Note it has written CRLF into the working tree; see `.claude/rules/windows-machine.md` §Text and encoding.

### `check-version` — the pre-commit version-authorship guard

Run by hand. It blocks a hand-edited `<VersionPrefix>` and a hand-stamped `## Unreleased` heading.

### `nuget-unlist` — hiding a superseded version from the feed

`node devtools/dev.mjs nuget-unlist [--below <version>] [--only <id>]`, **dry run by default**; add
`--apply` to act. Key from `NUGET_API_KEY` or `--api-key <key>`, minted on nuget.org scoped `Unlist` + glob
`Lyntai.*`. Prefer the environment variable — `--api-key` puts the key in shell history — and never commit
one; the tool redacts the key from its own error output.

Unlisting hides a version from search and from *range* resolution but never breaks a pinned consumer, and
never frees the number. Everything below 2.0.1 is unlisted (`docs/DECISIONS.md` D44), so
`Lyntai.Providers.ClaudeCli`, `.CodexCli` and `.OpenAiCompatible` have no listed version at all.

**The roster is derived from `src/*/*.csproj`**; only retired ids are hand-kept, in the script's `RETIRED`
array — the rule for adding one is `.claude/rules/repo-mechanics.md` §Package layout.

Deprecation, as opposed to unlisting, is **web-UI only** — no API, so it is not scriptable.

### `memory-*` — the sweeps

Every `memory-*` command is a measurement rather than a gate, all out of `verify` for the same cost reason
(tens of minutes each, and several need a live model server). What each one measures is in the command
table in `CLAUDE.md`; what each one FOUND is in `docs/memory-measurements.md` §5. Two are retired from the
roster — `memory-spacing` and `memory-reinforcement` answered their questions (**D53**, **D54**); the results
stand in that record and the instruments are in git history.

Four of them are not one-factor sweeps and each is exceptional differently: `memory-sweep` is the
{ranking × forgetting} 2×2; **`memory-scale`'s subject is COST**, so it generates plain entries and reports
no miss or pollution rate at all; and `memory-support` crosses rule × θ × clock × `ConnectionBoost` and
carries a second `--screen` mode for the model ladder; and `memory-decision` crosses shape × list length ×
model size.

**`memory-locomo` and `memory-longmemeval` belong to a different family** — they are the FIELD's
benchmarks, measured on data this repository did not build. Both are model-free (the dataset names the
evidence turn, so no reader and no judge can be credited or blamed), and both need a dataset the command
prints a `curl` for.

**`memory-contention` and `memory-decision` are ORCHESTRATED** — a Node script owns the server processes and
the C# sweep only measures, because both need several `llama-server` instances at once and a bench cannot be
trusted to tear them down. `memory-decision` is also the only sweep whose subject is not the memory engine
at all: it measures what a model does with a bounded list of options, and it touches no store.

**`memory-sweep`'s corpus holds neither authoritative material nor an authored headline BY DEFAULT** —
`CorpusShape.AuthoritativeCount` is opt-in and `0` unless a caller asks, so any change to grade behaviour
is unreachable *there*. An unchanged sweep number is "not exercised", never "no regression".
