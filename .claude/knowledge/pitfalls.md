---
name: pitfalls
applies_when: before extending or refactoring any area of Lyntai — tooling, LLM/router, provider lifetime, storage, DI, or tests
enforces: don't reintroduce the measured traps — each one passed the build (and usually the tests) while being wrong
---

# Pitfalls — don't reintroduce these

Concrete traps, most surfaced by a real bug or an independent audit. Each passed the build (and usually
the tests) while being wrong. Skim before touching the relevant area — **or jump by facet**, below, which
is what to do when you know the SHAPE of what you are worried about but not where it lives.

<!-- facets:begin — GENERATED. Edit the per-trap `trap:` markers, never this index. -->

## Facets — 230 traps, indexed two ways

_Generated from the per-trap `<!-- trap: … -->` markers by `node devtools/dev.mjs check-pitfalls --write`. Edit a marker, never this index._
_Line numbers only, on purpose: this is for JUMPING, not for reading. The facets are ORTHOGONAL to the headings below — a probe's nine relevant traps once spanned five of them, two of which nobody_
_looking for that task would have opened._

**By AREA** — which part of the repository breaks.

- **`gates`** (46) — 57 · 90 · 99 · 108 · 115 · 121 · 128 · 134 · 143 · 153 · 160 · 168 · 174 · 189 · 197 · 216 · 221 · 299 · 355 · 364 · 374 · 397 · 404 · 422 · 429 · 620 · 726 · 739 · 754 · 764 · 773 · 781 · 804 · 960 · 1548 · 1564 · 1579 · 1590 · 1598 · 1605 · 1609 · 1625 · 1797 · 1839 · 1848 · 1855
- **`encoding`** (6) — 65 · 85 · 88 · 381 · 513 · 1625
- **`git`** (7) — 168 · 181 · 189 · 202 · 381 · 389 · 1548
- **`build`** (14) — 88 · 355 · 364 · 374 · 397 · 612 · 764 · 773 · 960 · 1525 · 1532 · 1536 · 1557 · 1569
- **`router`** (10) — 813 · 817 · 826 · 828 · 831 · 833 · 839 · 844 · 952 · 1379
- **`cli`** (20) — 65 · 435 · 513 · 813 · 839 · 849 · 857 · 866 · 870 · 877 · 881 · 888 · 892 · 899 · 903 · 926 · 933 · 937 · 944 · 1444
- **`lifetime`** (6) — 972 · 981 · 986 · 991 · 997 · 1005
- **`storage`** (19) — 796 · 916 · 1014 · 1062 · 1076 · 1081 · 1095 · 1103 · 1234 · 1237 · 1253 · 1260 · 1269 · 1308 · 1411 · 1461 · 1618 · 1706 · 1783
- **`memory`** (46) — 233 · 250 · 277 · 344 · 655 · 700 · 916 · 1024 · 1036 · 1042 · 1055 · 1062 · 1070 · 1087 · 1095 · 1103 · 1113 · 1120 · 1127 · 1132 · 1141 · 1144 · 1152 · 1159 · 1172 · 1179 · 1186 · 1221 · 1243 · 1276 · 1283 · 1299 · 1324 · 1334 · 1358 · 1367 · 1387 · 1400 · 1453 · 1461 · 1507 · 1516 · 1657 · 1724 · 1737 · 1776
- **`generation`** (13) — 648 · 671 · 717 · 817 · 997 · 1334 · 1352 · 1379 · 1419 · 1427 · 1438 · 1490 · 1500
- **`di`** (14) — 991 · 1120 · 1127 · 1141 · 1283 · 1291 · 1293 · 1296 · 1299 · 1308 · 1316 · 1358 · 1427 · 1453
- **`measurement`** (70) — 108 · 227 · 233 · 242 · 250 · 258 · 269 · 277 · 291 · 306 · 314 · 322 · 334 · 344 · 411 · 435 · 442 · 447 · 456 · 462 · 475 · 483 · 490 · 498 · 506 · 518 · 522 · 530 · 541 · 562 · 578 · 584 · 592 · 605 · 612 · 638 · 648 · 655 · 665 · 671 · 678 · 686 · 693 · 700 · 709 · 717 · 857 · 1014 · 1055 · 1144 · 1152 · 1172 · 1193 · 1204 · 1212 · 1221 · 1243 · 1324 · 1536 · 1579 · 1635 · 1675 · 1682 · 1691 · 1699 · 1706 · 1716 · 1731 · 1765 · 1776
- **`docs`** (31) — 74 · 115 · 134 · 143 · 207 · 389 · 404 · 411 · 422 · 620 · 626 · 630 · 634 · 739 · 754 · 804 · 1042 · 1352 · 1367 · 1406 · 1474 · 1484 · 1532 · 1564 · 1590 · 1598 · 1605 · 1609 · 1618 · 1747 · 1855
- **`tests`** (26) — 74 · 781 · 789 · 796 · 926 · 1048 · 1070 · 1087 · 1113 · 1132 · 1234 · 1237 · 1411 · 1646 · 1657 · 1668 · 1737 · 1753 · 1783 · 1791 · 1797 · 1809 · 1820 · 1826 · 1830 · 1834

**By SHAPE** — how the wrongness stays invisible. Orthogonal to the area, and usually the more useful
of the two: most of these traps recur in a subsystem that had never met them.

- **`fail-open`** (25) — 168 · 233 · 250 · 277 · 404 · 456 · 638 · 717 · 826 · 831 · 839 · 866 · 926 · 986 · 1036 · 1179 · 1186 · 1299 · 1324 · 1419 · 1427 · 1453 · 1500 · 1507 · 1724
- **`cancellation`** (5) — 813 · 817 · 1024 · 1036 · 1048
- **`vacuous`** (53) — 108 · 153 · 160 · 227 · 258 · 269 · 291 · 306 · 364 · 462 · 483 · 498 · 530 · 648 · 655 · 665 · 671 · 686 · 726 · 739 · 773 · 789 · 833 · 892 · 933 · 952 · 1048 · 1070 · 1087 · 1113 · 1132 · 1152 · 1193 · 1204 · 1221 · 1324 · 1367 · 1379 · 1618 · 1657 · 1668 · 1682 · 1731 · 1737 · 1753 · 1765 · 1783 · 1797 · 1809 · 1820 · 1830 · 1839 · 1848
- **`scope-blind`** (44) — 90 · 121 · 128 · 134 · 143 · 153 · 168 · 174 · 189 · 216 · 221 · 381 · 422 · 429 · 693 · 739 · 764 · 804 · 960 · 1120 · 1127 · 1172 · 1234 · 1237 · 1352 · 1367 · 1387 · 1411 · 1461 · 1516 · 1564 · 1569 · 1579 · 1590 · 1598 · 1605 · 1609 · 1618 · 1668 · 1731 · 1737 · 1839 · 1848 · 1855
- **`second-door`** (23) — 828 · 833 · 877 · 881 · 892 · 899 · 937 · 944 · 1005 · 1095 · 1260 · 1299 · 1358 · 1379 · 1400 · 1411 · 1419 · 1427 · 1438 · 1461 · 1474 · 1490 · 1516
- **`stale-claim`** (24) — 57 · 134 · 181 · 207 · 411 · 490 · 506 · 620 · 630 · 773 · 1042 · 1296 · 1352 · 1406 · 1438 · 1474 · 1532 · 1536 · 1590 · 1598 · 1605 · 1747 · 1809 · 1855
- **`silent-loss`** (65) — 65 · 74 · 85 · 88 · 115 · 197 · 202 · 216 · 221 · 242 · 250 · 334 · 381 · 397 · 404 · 435 · 447 · 483 · 513 · 522 · 541 · 562 · 605 · 638 · 671 · 678 · 717 · 754 · 764 · 781 · 844 · 870 · 881 · 916 · 972 · 997 · 1055 · 1076 · 1095 · 1103 · 1113 · 1186 · 1212 · 1243 · 1253 · 1260 · 1269 · 1276 · 1291 · 1296 · 1308 · 1316 · 1358 · 1387 · 1444 · 1490 · 1507 · 1525 · 1536 · 1557 · 1564 · 1625 · 1675 · 1706 · 1765
- **`wrong-subject`** (70) — 57 · 74 · 99 · 108 · 202 · 227 · 242 · 269 · 277 · 299 · 306 · 314 · 322 · 334 · 344 · 355 · 374 · 462 · 475 · 498 · 506 · 522 · 541 · 562 · 578 · 584 · 626 · 634 · 648 · 655 · 665 · 678 · 686 · 693 · 700 · 709 · 754 · 796 · 828 · 888 · 960 · 981 · 991 · 1014 · 1055 · 1087 · 1141 · 1144 · 1159 · 1172 · 1193 · 1204 · 1221 · 1283 · 1334 · 1500 · 1525 · 1548 · 1557 · 1635 · 1675 · 1682 · 1691 · 1699 · 1706 · 1753 · 1776 · 1797 · 1826 · 1830
- **`unmeasured`** (18) — 314 · 344 · 389 · 411 · 422 · 442 · 592 · 612 · 844 · 849 · 857 · 866 · 903 · 933 · 1159 · 1334 · 1484 · 1747
- **`ordering`** (13) — 374 · 870 · 944 · 1062 · 1076 · 1081 · 1269 · 1308 · 1316 · 1444 · 1453 · 1646 · 1716
- **`resource`** (13) — 355 · 397 · 518 · 584 · 709 · 849 · 926 · 1005 · 1014 · 1081 · 1293 · 1791 · 1834

<!-- facets:end -->

## Environment / tooling

- **`verify` answers a question about the bytes it READ, so an edit during the run voids its green line.** <!-- trap: sub=gates shape=wrong-subject,stale-claim -->
  Gated: `verify` content-hashes the tree before and after (`scripts/_tree-fingerprint.mjs`) and exits
  non-zero naming what moved. The habit the gate does not replace: start it and keep your hands off the
  tree; to keep working, work elsewhere and re-run. Two cheaper checks were tried and REFUSED — an mtime
  comparison (fires on a byte-identical rewrite) and `git status` (blind to a change inside an
  already-dirty file). **And never read its exit code through a pipe**: `verify | tail` reports tail's
  status (`.claude/rules/windows-machine.md` §Scripts and exit codes).

- **An unescaped BACKTICK inside `node -e "…"` under bash is command substitution, and it silently EATS <!-- trap: sub=encoding,cli shape=silent-loss -->
  the text it swallows — the edit lands, mangled, and reports success.** Hit three times on 2026-09-13
  editing backticked prose: `` `tool-affordance` `` was written to the file as nothing. Worse variants: bash
  also RUNS the swallowed words, creating files named after them (three reached a commit), and a `\n` inside
  a double-quoted `node -e` becomes a literal newline that splits a generated string literal. **The SHELL is a
  second parser between you and the bytes, so use the file-writing tools**; a `node -e` that writes a file is
  safe only with a single-quoted program and no backticks in the payload.
  <br>**And never clean up with `find . -maxdepth 1 -delete`**: it took `LICENSE` and `Lyntai.slnx` with the
  stray files it was meant for. Remove named paths, or `git clean -n` first.
- **A whole-token rename sweep rewrites PROSE and STRING LITERALS too, and both stay green.** Measured <!-- trap: sub=docs,tests shape=silent-loss,wrong-subject -->
  2026-09-17 renaming `embedder` → `vectorProvider` across 86 files (**D152**): comments read *"an
  vectorProvider's graph"*, and a user-facing error told a consumer to *"drop the vectorProvider
  registration"* — the build clean at every step, because none of it is code.
  <br>**Three passes, in this order**: identifiers; then comment prose, with `cref=`/`name=` values MASKED so a
  real type name survives; then string literals, with `{…}` interpolation holes masked. A single pass cannot
  do it — the same token is an identifier in one place and a noun in another. **And check whether the token
  appears in someone else's WIRE first** — `embedding` does (`/embeddings`, the `"embedding"` field,
  `max_position_embeddings`), so it was excluded: a vendor's route or a model file's key is not yours to
  rename. The SCOPE of a rename comes from what the word IS in your own model (**D152**), never from what
  everyone else calls it.
- **This machine's console is GBK/CP936, so UTF-8 written THROUGH it is corrupted irreversibly** — it once <!-- trap: sub=encoding shape=silent-loss -->
  mangled every `灵台`/`—`/`§` in `TASKS.md`. Write with the file tools and verify by codepoint, never by
  eyeballing console output (`.claude/rules/windows-machine.md` §Text and encoding).
- Sources are BOM-less UTF-8 + `<CodePage>65001</CodePage>` (in `Directory.Build.props`) — without it <!-- trap: sub=encoding,build shape=silent-loss -->
  csc reads CJK string literals as ANSI mojibake on a CJK-locale machine.
- **A prose gate that matches line-by-line is blind to any claim that WRAPS**, and it reports the file <!-- trap: sub=gates shape=scope-blind -->
  clean while doing it. `check-docs` tested each `retiredTerms` pattern against one line at a time, so
  `CLAUDE.md`'s "`ReciprocalRankFusionPolicy`, available\nbut not the default" — a stale claim in the one
  file auto-loaded into every session — matched nothing for the entire window in which it was false. These
  documents wrap at ~110 columns and the claims worth fencing are shorter than a line, so **the sentence
  the gate most needs to catch is the one most likely to straddle a break.** Fixed 2026-08-11 by testing
  each line AND that line soft-joined to the next; `drift-ok` on either line silences the pair. The general
  shape is worth carrying to any future text gate: **the unit you match must be the unit the claim is
  written in.** Line-oriented matching is a property of the scanner, never of the prose.
- **A doc gate built on "does this identifier EXIST in the tree?" is the wrong shape, and the reason is <!-- trap: sub=gates shape=wrong-subject -->
  worth knowing before someone builds it.** Tried 2026-08-14 as the prose counterpart to `check-samples`:
  **~45 hits and zero defects**, because naming something that does not exist is often CORRECT here — this
  file cites `LlmRouterTests` precisely because it does not exist, `generic-library.md` names a property a
  type deliberately lacks, and the decision record names the fakes and sibling types of its day.
  **A gate whose false positives are legitimate authorial choices cannot be tightened into usefulness** —
  only given an exclusion list nobody can see rot. That is why `check-docs` is a curated `retiredTerms`
  registry, where a hit is a defect by construction: reach for a registry, not a corpus scan, whenever
  "wrong" depends on intent rather than on the text.
- **A record's own SUBJECT MATTER is the vocabulary a gate over it wants to scan for — so the scan reads <!-- trap: sub=gates,measurement shape=vacuous,wrong-subject -->
  as full of defects and holds none.** `check-measurements`' planned body scan for
  `CORRECT(ED|ION)|RETRACT|STALE|SUPERSED` found **63 hits across 18 results and ZERO defects** (2026-09-10):
  `stale@k` is a METRIC NAME there and "the superseded fact" the PHENOMENON measured, and CAPITALS did not
  rescue it. **Narrowing the SUBJECT, not the pattern, worked** — the same vocabulary over section HEADINGS
  flagged 5 of 57, 4 genuine. **Before writing a vocabulary gate, run it and COUNT the defects**: a scan with a
  0% hit rate is indistinguishable from a strict one until somebody looks.
- **A marker value is not a string literal: a `>` or a `"` inside one truncates or deletes the row silently.** <!-- trap: sub=gates,docs shape=silent-loss -->
  `_markers.mjs` excludes `>` so a pattern cannot run past its own `-->`, which makes `arm="best threshold
  >= 6"` an UNMATCHED marker that every check skips (a result vanished from a generated index); a `\"` ends
  the attribute at the quote and strands the rest. Keep both characters out and reword rather than escape,
  and let any gate on this seam tell "no marker" from "a marker too broken to match" — test for the OPENER
  (`<!-- name:`) as well as the full pattern.
- **A gate weakened by a regex LOOKAHEAD has no expiry — worse than the escape it was built to avoid.** <!-- trap: sub=gates shape=scope-blind -->
  Found 2026-08-31 narrowing `retiredTerms` to silence seven accurate past-tense comments: a trailing
  negative lookahead excluded any hit followed by a VALUE-STATEMENT shape (`default of N`, `= N`), which is
  also the exact shape a REINTRODUCED stale doc takes — excluded everywhere, permanently. An allowance FAILS
  the moment it stops matching; a lookahead has nothing to expire. Withdrawn the same day, the seven comments
  reworded instead. **When a gate's false positives share one concrete pattern, exclude those exact LINES (a
  self-expiring allowance), never the pattern that makes them false positives.**
- **Scope a doc gate by "is this maintained state?", not by directory.** The same gate omitted the two <!-- trap: sub=gates shape=scope-blind -->
  repo-root files, `CLAUDE.md` and `TASKS.md`, purely because the scope test was three `startsWith` calls.
  Both are maintained state and `CLAUDE.md` is the highest-leverage document in the repo — a stale claim
  there is read by the next session *before it reads anything else*. Adding them found real drift on the
  first run. `docs/task-archive.md` stays excluded on purpose: it is accurate BY using the vocabulary of its
  day. **`CHANGELOG.md` is only HALF that**, and the split cost real drift — see the next entry.
- **Two pieces of prose SHIP to consumers and no gate reads either: the packaged `README.md` and every <!-- trap: sub=docs,gates shape=scope-blind,stale-claim -->
  csproj `<Description>`.** `PackageReadmeFile` packs the README into every package — it is what nuget.org
  renders — and on 2026-08-17 it named an untracked `local/…` path no consumer can follow, because
  `check-links` skips `local/**` by design; `check-packages` asserts a `<Description>` EXISTS and never reads
  it, so one still justified an exclusion with a reason **D69/D70** had retired. A gate's scope is drawn
  around what the repository MAINTAINS, and these are the most widely READ prose it publishes. **Before a
  release, re-read the README as a consumer who has only the package, and every `<Description>` against the
  decisions since the last one.** A forward version number in an XML doc is the third member: it ships as a
  prediction (`repo-mechanics.md` §"never NAME a version that has not shipped").
- **"Historical record" can be true of part of a file and false of the rest. `## Unreleased` is not <!-- trap: sub=gates,docs shape=scope-blind -->
  history.** `check-docs` exempted `CHANGELOG.md` wholesale on the rationale that a record is accurate by
  using the vocabulary of its day — true of a RELEASED section, false of `## Unreleased`, which describes
  behaviour that has not shipped and can still change under the words describing it. Measured 2026-08-09: an
  entry kept asserting a pre-fix behaviour *and* its retired justification after the code changed, while a
  paragraph five lines below was corrected in the same pass; the gate reported 39 docs clean and a human
  found it. Fixed 2026-08-11 by scanning the file down to its first released `## <version>` heading, sharing
  the boundary with `check-samples` so the two gates cannot answer it differently. Two hits on the first real
  run, both genuine. **The general shape: an exemption's rationale has a scope, and it is worth checking
  whether the FILE is that scope.**
- **Narrowing an exemption does nothing if an EARLIER filter already excluded the subject — and the gate <!-- trap: sub=gates shape=vacuous,scope-blind -->
  reports the same green line either way.** The first version of that CHANGELOG fix rewrote the historical
  filter, ran clean, and had changed nothing at all: `IN_SCOPE` runs first and had never listed
  `CHANGELOG.md`, so the file was gone one step before the filter being narrowed. It was caught by PROBING
  the gate — asking it whether the file was in its list and how many lines it would scan — rather than by
  reading its exit code. **For any filter chain, a clean run proves nothing about the stage you edited;
  assert on the intermediate (the file list, the line count), or write the positive control that must fail.**
- **A text predicate that checks a flag's CALL SITES is defeated by editing the flag's DEFINITION, and it <!-- trap: sub=gates shape=vacuous -->
  reports green while the defect it was written for is live.** Measured 2026-09-10 designing a predicate for
  **D29**: asserting `disposeHttpClient: !byo` at each registration survives mutating `var byo = httpClient
  is not null;` to `is null` — every literal intact, every host-supplied client disposed after its first
  call. **The predicate was checking spelling, not polarity.** Assert the polarity at its SOURCE, and prefer a
  BEHAVIOURAL test for a claim about behaviour (register a BYO client, drive two calls, assert the second does
  not throw). **Frozen counts are the same failure**: "all 11 lease sites" turns red when a correct twelfth
  arrives, so quantify over what is found, never over how many.
- **`git ls-files` (and `diff --name-only`) C-QUOTE any path containing a non-ASCII byte** — `docs/灵台.md` <!-- link-ok: a guard FIXTURE's name, never a file here --> <!-- trap: sub=gates,git shape=fail-open,scope-blind -->
  comes back as an escaped string matching no file on disk. **Always pass `-z` and split on NUL.** In
  `check-sensitive` the failure was silent AND permissive: the quoted name failed `readFileSync` with
  `ENOENT`, read as "deleted mid-refactor", so the file was skipped and the run printed `clean`
  (`docs/FIXES.md`, 2026-08-11). **A scanner that gets its file list from one tool and its bytes from another
  must agree with both about how a name is spelled** — and this repository is named 灵台.
- **A doc gate scoped by FILE TYPE leaves the identical defect alive in every other tier, and its own <!-- trap: sub=gates shape=scope-blind -->
  exclusion rationale is where to look for the hole.** `check-links` went green on `.md` on 2026-08-14 while
  seven more of the same dead references lived in `src/` and `tests/`, two inside shipped XML docs —
  excluded because "the compiler already gates their crefs", when **the compiler resolves `<see cref>` and
  NOTHING else**. **(1) When a gate's scope is narrower than the defect, name the UNCOVERED tiers in its
  header. (2) An exclusion justified by "another mechanism covers it" is a claim about that mechanism — name
  exactly what it covers.**
- **`.gitignore` does not untrack an ALREADY-TRACKED file, so `git mv`-ing a document into an ignored <!-- trap: sub=git shape=stale-claim -->
  directory leaves it tracked — and every downstream claim that it is untracked silently becomes false.**
  Measured 2026-08-14: of 29 files under `local/`, the one D43 had archived out of `docs/` by `git mv` was
  still tracked, falsifying three claims in `docs/superpowers/INDEX.md` — in the directory whose premise, for
  `local/sensitive-patterns.txt`, is that it does not ship. **Untracking is an explicit `git rm --cached`,
  never a side effect of a path change — and ASSERT it (`git ls-files <dir>` is empty)** rather than
  inferring it from the ignore rule, which is not what decides. A "verified" claim with no gate behind it
  decays like any counted claim.
- **A gate scoped by `git ls-files` is blind to the code most likely to need it — the file you just wrote — <!-- trap: sub=gates,git shape=scope-blind -->
  and it reports clean while doing so.** `git ls-files` lists the INDEX, and the workflow is *write →
  `verify` → commit*: a 36-line comment block in a new bench file passed two `verify` runs and failed the
  moment it was committed, byte-identical (2026-08-23). `--others --exclude-standard` adds new SOURCE while
  ignored scratch stays out. The rule had been written five times privately, so the blind spot was
  six-fold; it is now one `devtools/scripts/_repo-files.mjs` (`docs/task-archive.md` Part 96). **Ask what a
  gate's file list EXCLUDES and whether that set holds the thing it exists to catch — then prove it with an
  untracked probe**, not by reading the glob.
- **A gate that enumerates a directory must tolerate the directory being absent.** `check-packages` threw a <!-- trap: sub=gates shape=silent-loss -->
  raw `ENOENT` stack trace from `readdirSync(Baselines/)` when the last baseline was deleted, instead of
  reporting the per-package "no API baseline" problems it had already collected (`docs/FIXES.md`,
  2026-08-11). It failed CLOSED, but showed the operator a stack trace naming no package — which was the
  report they needed.
- **The release workflow builds from the REMOTE, so "finished" and "shipped" are different states and only <!-- trap: sub=git shape=wrong-subject,silent-loss -->
  one of them is pushed.** Measured 2026-08-05: **v2.2.0 was cut without the whole-library review that had
  been finished for it** — three commits sat locally, the workflow built what the remote had, and the
  release reported complete success. **Push before triggering a release**, and treat "the work is done" and
  "the work is on the remote" as two separate claims — the second is the only one a release can act on.
- **A backlog item amended IN PLACE does not amend the summary that points at it, and the amendment is <!-- trap: sub=docs shape=stale-claim -->
  exactly when the summary goes stale.** `TASKS.md`'s startable-set banner advertised finished work four
  times (2026-08-26 to 08-29), the last a CORRECTION of the one before that repeated it — re-reading the item
  is the check that fails, because the item is what changed. Three gates were considered and REFUSED, each
  catching one of four: a self-closing-phrase scan, a banner-to-open-item check, and deriving the banner from
  the checkboxes (a WATCH item is not computable from a `- [ ]`). **What worked was making the source CARRY
  the answer**: a per-item state marker and a GENERATED roster (`check-backlog`, **D111**). When a summary
  keeps going stale, ask whether the thing it summarizes can carry the answer as a field.

- **A parser that SCRAPES validates what it matched and cannot see what it skipped.** A `matchAll` of <!-- trap: sub=gates shape=silent-loss,scope-blind -->
  `key=value` parsed `needs=a real key` as `needs="a"` and passed every rule — the residue has no `=`. Assert
  the matches COVER the input: accumulate the gaps and fail on a non-blank residue (`_markers.mjs`
  `parseAttributes`). The damage READS AS DATA, which is what makes the class dangerous.

- **A generator must fail closed on anything that could have narrowed its input: a TOGGLE is not a boundary <!-- trap: sub=gates shape=scope-blind,silent-loss -->
  until something asserts it balanced, and a delimiter computed twice drifts permissively.** An unclosed
  fence once took this file's index from 157 traps to 131 and `--write` published it at exit 0; a second
  derivation of the block's anchor let a quoted anchor suppress everything below it. Both are fixed in
  `check-pitfalls.mjs` (one `blockRange`, a balance check at EOF), whose header keeps the story. Carry both
  rules to any scanner that WRITES.
- **Before optimizing against a latency number, measure the INSTRUMENT's noise — a p50 that moves less than <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  its own run-to-run spread has told you nothing.** Measured 2026-08-29 on `memory-scale` (**D99**): the 10k
  p50 read `11.0ms` before a change and `8.9ms` after, and a second run of the identical post-change code
  read `11.2ms` — a "19% improvement" that was noise. **The cheap defence is a repeat of the AFTER arm**, not
  a bigger sample; then convert the claim to something COUNTABLE if you can (ten round-trips became one — a
  test can count calls, and a count is gate-able where a millisecond is not).
- **A FAIL-OPEN seam is indistinguishable from one that agreed with you, so an arm measuring it must count <!-- trap: sub=measurement,memory shape=fail-open -->
  how often it actually FIRED.** `IMemoryVerificationPolicy` returns `NoOpinion` on every failure — refusal,
  timeout, unparseable reply — and that leaves the ranking untouched, so "the judge endorsed what already
  led" and "the judge never answered once" produce the same table, and the second gets published as "the
  seam is not worth it". **The model-free floor is a correct behaviour and a terrible observable.** Count the
  fires, not just the outcome, and print the count beside the score, for every best-effort seam. The LoCoMo
  judge arm was readable only because it scored BELOW its base (2026-09-03); a mediocre model would have
  landed on it and been unreadable.

- **A harness that ASSEMBLES a model's input must assemble the one the ENGINE assembles — and when it does <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  not, the distortion is UNEQUAL across models, so it inverts a ranking rather than shifting a level.**
  `memory-annotation-drift` built the annotator's request itself — `Recent` per cluster and unbounded where
  the engine passes 8 per task and scope, every handle where it shows 24 — handing the model a cleaner problem
  than any deployment. Corrected, five of six cells moved the wrong way and the headline was retracted
  (`docs/memory-measurements.md` §5, `annotation-drift-corrected-context`). **Open the engine's own call site
  and compare every field it fills**; a fixture that fills them differently measures a deployment that does
  not exist.
- **A SELECTIVE seam must bootstrap GENERATIVELY, or its empty state is ABSORBING.** Measured 2026-09-15 <!-- trap: sub=measurement,memory shape=fail-open,silent-loss -->
  building a `select-from-list` annotator: with nothing in use yet the prompt still offered the list, the
  model answered `{"pick": 1}` against zero options, the out-of-range pick was dropped as malformed — so no
  handle was ever recorded, the list could never grow, and **32 of 32 facts came back unlabelled**. The
  first call's failure is permanent, because the thing that would fix it is the thing the failure prevents.
  <br>**It presents as a model result, which is the danger**: the table read "this model cannot do the
  selective shape at all" when the model was answering correctly every time. Ask the empty case what it is
  supposed to do BEFORE running anything — and note the control that caught it was EMPTY count, not drift.
- **…and the fixture's WRITE ORDER is part of that input.** The same harness wrote each entity's facts <!-- trap: sub=measurement shape=vacuous -->
  CONSECUTIVELY, which is not what a real stream does — and a recency rule scored on it read **87.0% →
  34.8% drift at no cost**, the largest single win in the memory record. Interleaved one fact apart it buys
  8 points and collapses **half the handle space**; seven apart it makes one model WORSE. **An adjacency
  rule scored on an adjacency-ordered fixture is handed its own answer.** Add the interleaving knob BEFORE
  believing any result that depends on order, not after (`annotation-drift-recency-gap`).
  <br>**The same shape in TEXT: a similarity rule scored on a TEMPLATED cluster is handed its answer too.**
  `MemoryCorpus`'s attribute cluster shares the suffix *"stated once and never restated"* and nothing
  else, so any cosine or overlap detector links it through the template rather than the meaning — caught
  2026-09-23 before `memory-consolidation` was built on it (`docs/task-archive.md` Part 271). Before
  scoring a linking rule, ask what the cluster's members share besides the relation being tested.
- **A control counter is contaminated by any SETUP that exercises the thing it counts, and the <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  contamination is invisible because the number stays PLAUSIBLE.** Twice in `MemoryContentionSweep`:
  `SubjectsPerWrite` counted `SeedAsync`'s own untimed writes, and `CrossEncoderReranker.ReachableAsync()`
  scored two probe sentences on the SAME instance that became `Rig.Reranker`, so `DistinctRerankScores` read
  **2 under the `judge` backend**, which never calls the reranker at all. **Reset the audit at the
  setup/measured boundary**, the way a stopwatch is zeroed before the timed region: whenever a counter and
  the code under test share ONE instance across both phases, the control otherwise measures setup plus work.

- **A model given an UNBOUNDED task stops discriminating, and it reads as the model being too small.** <!-- trap: sub=memory,measurement shape=wrong-subject,fail-open -->
  Measured twice (2026-09-03/04) with one 4B model: the verification judge's endorsements grew with the list
  until it stopped judging and waved things through, and a fact extractor asked for "the facts" inflated the
  corpus sevenfold. **Neither is a capability failure** — the judge RANKS well and cannot tell where to stop.
  **The tell is a prompt that asks for a SELECTION and states no budget.** Before concluding a seam needs a
  bigger model, check what it HANDS the model: how many items, how long, whether the instruction bounds the
  answer.
  <br>**A stated budget binds a GENERATIVE task and not a SELECTIVE one over a visible list** — there it
  RAISED the output, visible only because the fires were counted. State the budget in the PROMPT, never
  truncate the reply in code, and count how often the model EXCEEDS it. **Price a number before building the
  surface that carries it**: the bound the library could express (the caller's limit) measured +0.5, an
  inexpressible one +4.0. Figures: `docs/memory-measurements.md` §5; the task-shape reading:
  `docs/model-tasks.md` §2.

- **An arm that is SUPPOSED to move needs a control proving it CAN — the mirror of the arm that cannot.** <!-- trap: sub=measurement shape=vacuous -->
  A bench arm replacing the engine's endorsed-first PARTITION with a rank FUSION scored identically to the
  partition in every cell (2026-09-03), reading as "fusing does not help" — and was arithmetically the
  partition: at `K = 60` the worst endorsed candidate still outscored the best unendorsed one. **Compare the
  arm's output against the thing it replaces, and say so** (same-page rate, mean overlap, a `! DEGENERATE`
  line). The error underneath: unendorsed candidates were scored as ABSENT, when an unlisted id "is judged
  NOT to have answered" — a "no" is a low RANK, not an absence.

- **A gate that scans SOURCE must blank comments first, and the false positive is always the code that most <!-- trap: sub=gates shape=wrong-subject -->
  explicitly obeys the rule.** Twice on 2026-09-04: a check for reflection `JsonSerializer` (**D14**) flagged
  the files whose comments say *"not JsonSerializer"*, and a check for a silent `IsAotCompatible=false`
  (**D7**) flagged a commented-out TEMPLATE. Prose about a rule quotes the rule's vocabulary — the mirror of
  a citation AUTHORIZING ITSELF in `check-links`. **Read the code, not what it says about itself**, and blank
  comment BODIES rather than dropping lines so reported line numbers stay true.

- **A benchmark arm whose CANDIDATE POOL reaches the size of its store has stopped measuring retrieval, and <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  the tell is a score too good to distrust.** An oracle arm at `CandidateMultiplier = 32` pooled 640
  candidates from conversations of 369–689 turns and printed **100.0% in every one of four categories**. The
  LongMemEval bench already had the counter that catches it; the LoCoMo bench did not, because it had been
  filed as one bench's instrument rather than as a property of pooled retrieval. **When a guard catches
  something structural, ask which other harness has the same structure** — and read UNIFORMITY, not
  implausibility, as the tell: `pool >= store` is a one-line check no score column can express.

- **An offline REPLICA of a shipped algorithm must be proven to reproduce it, and the difference that breaks <!-- trap: sub=measurement shape=wrong-subject,unmeasured -->
  it is never the interesting part of the algorithm.** A ladder scoring RRF outside the engine broke score
  ties ASCENDING where `MemoryRankingContract.Finish` breaks them descending, moving the shipped row 4 points
  while looking plausible (`docs/task-archive.md` Part 114); a bench-side character budget STOPPED at the
  first item that did not fit where `MemoryQuery.CharBudget` skips it and keeps filling, reading 0.0% where
  the shipped rule reads 30.0% (`docs/task-archive.md` Part 157). **The control is "reproduce the shipped
  policy's own output", not "look right" — and diff the shipped loop even when the rule fits in a sentence.**

- **Measure a component against the calls it could POSSIBLY change, not against every call.** Same day, and <!-- trap: sub=measurement shape=wrong-subject -->
  it reframed three runs at once. Every judge column here was scored over all 200 questions — but a verifier
  promotes, and promotion cannot invent a candidate, so it can only change a call whose returned page held
  no evidence while something deeper did. That was **19 of 200**. On the other 181 the best possible
  behaviour is to leave a correct page alone, so *"the judge scored its base"* was never evidence that it
  judged badly, and the denominator had been hiding the actual question.
  <br>Once conditioned, the answer was sharp and the opposite of the aggregate: the judge's cumulative
  precision by its OWN best-first order ran 34.5% at rank 1 against a 1.49% pool density (a 23× lift), yet
  on the rescuable calls it put the deep evidence in its top five **zero** times. **Its confidence tracked
  what the ranking had already found.** An aggregate lift can be large and land entirely on the calls where
  it is worth nothing — so before optimizing a re-ranker, count the calls it could improve at all.

- **A prediction landing where you expected is when you are least inclined to ask what produced it.** <!-- trap: sub=measurement shape=silent-loss,wrong-subject -->
  Measured 2026-09-02 (`docs/task-archive.md` Part 137): a walk arm scored 42.0%, inside its pre-registered
  band AND branch, while silently measuring 20 whole items plus ~20 TRUNCATED ones — a projection defect that
  the accuracy column could not show and the `chars/q` column did (6,053 where 40 whole turns cost ~7,000).
  **Pre-registration protects against motivated reading of the RESULT and not at all against a broken
  instrument**, and a confirmed prediction is precisely where nobody looks.
  <br>**So state the INSTRUMENT check in advance beside the outcome check** — the re-run declared that
  `chars/q` had to rise to ~6,600–6,900 or the fix had not reached the arm, and it read 7,084. A cheap
  non-outcome column that must move is what tells a real run from a plausible one.

- **A constant tuned against a PERFECT component inherits that component's assumptions, and can be actively <!-- trap: sub=measurement,memory shape=unmeasured,wrong-subject -->
  harmful for the real one.** `GraphMemoryOptions.DefaultVerificationDepthFactor` sits at 4 because rescue
  depth SATURATES — measured with an ORACLE, which never endorses junk, so depth was free; for a real 4B judge
  depth is a PRECISION trade, level with no judge at 2× and 10.5 points worse at the shipped 4×. **The tell is
  a doc naming the measurement without naming the instrument.** Record what stood in for the missing
  component, and treat every constant fitted beside an ideal as unmeasured for the real one.
  <br>**…and a HEADROOM measured beside the ideal is zero by construction**: a perfect annotator links every
  same-subject pair at write time, so gating offline consolidation on its headroom would have refuted the
  idea without measuring anything (`docs/task-archive.md` Part 271). Run the ideal as the SELF-CHECK — it
  must read zero — and take the verdict from the arm a deployment actually has.

- **NuGet never re-extracts a package version it already has in the global cache**, so packing under a FIXED <!-- trap: sub=build,gates shape=resource,wrong-subject -->
  throwaway version (`consumer-smoke`'s `9.9.9-smoke`) tests the packages only ONCE — every later run restores
  the first run's copies from `~/.nuget/packages/` and reports success about code it never compiled against.
  Found 2026-08-04: a newly-added public method was "missing" from a package that demonstrably contained it,
  and evicting the cached ids reported **9 stale packages**. `consumer-smoke` now deletes
  `<global-packages>/lyntai.*/<version>` after packing; keep that step if you touch the script, and remember the
  general shape — *a distinct version is not enough on its own if the version is a constant.* Evicting by
  id+version beats isolating `NUGET_PACKAGES`, which would re-download every third-party dependency per run.

- **Roslyn BINDS NOTHING in a compilation that carries a syntax error, so a per-file verdict taken from a <!-- trap: sub=build,gates shape=vacuous -->
  failed batch certifies unbound code.** Parse errors in ONE file suppress semantic analysis for EVERY file
  in the compilation. Measured 2026-08-11 on `check-samples`' first run: the batch reported **132 errors,
  every one syntactic, and zero `CS0246`** — so eleven documented samples naming types that do not exist in
  this library were reported as compiling, and the gate announced 83/95 green over code whose names had
  never been resolved. The fix is the general one: **a batched checker may only accept a member from a run
  in which NOTHING failed** — iterate, drop or re-shape whatever errored, recompile, and take the verdict
  from the clean run. "No error was attributed to my file" is not evidence in a failed compilation, and
  this generalises past csc to any batch tool with a fail-fast phase (a linter that stops at a parse error,
  a migration runner that aborts the transaction).
- **A type declared in SOURCE outranks the same type from a referenced assembly for the whole compilation, <!-- trap: sub=build,gates shape=ordering,wrong-subject -->
  and the diagnostic is only a warning (`CS0436`).** So one documented sample opening
  `namespace Lyntai.Inference;` silently redefines the real `MediaRequest` for every other sample
  compiled alongside it, which then typecheck against the doc's copy instead of the shipped one — green,
  and meaningless. `check-samples` compiles any block declaring a namespace under the library root in its
  OWN compilation for this reason. The shape to remember: **whenever you compile untrusted-ish source
  beside real references, ask what that source is allowed to SHADOW.**
- **Line endings: the tracked `.gitattributes` (`* text=auto eol=lf`, **D95**) makes the COMMIT safe, never <!-- trap: sub=encoding,git shape=scope-blind,silent-loss -->
  the working tree.** Before it, a CRLF working tree against an LF index inflated a 131-line change to
  2055/1931 and git's stat cache hid the state until a file was touched, because this clone's
  `core.autocrlf=false` stored the tree verbatim. Three things still hold: a tool can write CRLF into the
  working tree (`dev.mjs decisions-index` did); splicing `lines.join('\n')` into a CRLF file leaves the
  inserted lines lone-LF; and in any repository with no declared attribute, never ASSERT a `core.autocrlf`
  value as a fact — `.git/config` is untracked, and even `true` keeps a blob already stored as CRLF. Check
  per file with `git ls-files --eol`, repair with a bytes replace (`windows-machine.md` §Text and encoding).
- **A claim about what a COMMAND does is testable in seconds, and guessing it is how four wrong sentences <!-- trap: sub=docs,git shape=unmeasured -->
  reached a decision record in one sitting.** Declaring the line-ending convention (**D95**,
  `docs/task-archive.md` Part 106), four plausible claims about git's own behaviour each fell to one command.
  **The tell is the sentence shape** — *"`-f` forces …"*, *"so it heals on the next checkout"*: observable
  behaviour, present tense, produced by no command in the transcript. **Reading the manual is not the fix**:
  two of the four fit a fast reading of `gitattributes(5)` and were still wrong, because the behaviour
  depended on a per-clone config and the stat cache. The claim is an experiment, so run it against the state
  you have.
- **`check-warnings` reports "build FAILED" for a build that SUCCEEDED once the build log outgrows Node's <!-- trap: sub=gates,build shape=resource,silent-loss -->
  1 MiB `spawnSync` buffer.** Measured 2026-08-09: one `ProjectReference` from the bench project to
  `Lyntai.Tests` dragged the test project's whole dependency graph into the build, the `-v normal` log hit
  1,049,602 bytes, Node returned `ENOBUFS`, and the gate announced a failure that did not happen. **This is
  the worst class of defect here — a gate that LIES, in the direction that trains a reader to ignore it.**
  Prefer `<Compile Include>` links over a `ProjectReference` to the test project, and when a gate's failure
  detail is empty or truncated, **suspect the harness before the build**.
- **A classifier written from a vocabulary list is blind to MODIFIERS on that vocabulary, and it fails <!-- trap: sub=gates,docs shape=fail-open,silent-loss -->
  silently because every item still lands somewhere.** The release-notes generator matched `^feat(scope)?:`
  and missed conventional commits' one modifier, the BREAKING `!`, so every `feat(memory)!:` fell to "Other
  changes" — 11 of 11 in the v2.5.0..3.0 range (`docs/FIXES.md`, 2026-08-16). **The tell is a catch-all that
  never looks wrong**: ask what is landing in it and whether that list looks like its name. Its sharpest
  form: plain `refactor:` is DROPPED, so a breaking refactor survived only because the drop pattern also
  missed the `!` — correctness resting on a second rule failing. Pin the rule as a PROPERTY over the real log.
- **The defects in a measurement write-up are almost never in the MEASUREMENT — they are in the prose about <!-- trap: sub=docs,measurement shape=unmeasured,stale-claim -->
  it, and every text gate is blind to them.** One sweep's report (2026-08-28) took five review rounds while
  its 44 result rows stayed byte-identical across four runs. Three kinds, and three different rules:
  **(1) provenance** — name the command AND the conditions that produced a number, or do not write it (a
  trio quoted from auto-loaded context, a row copied from its neighbour, a warm build called cold);
  **(2) summary statistic** — never report an extremum as a central value; give the spread and name the
  instrument; **(3) restatement** — a paraphrase, rounding or "orders of magnitude" is a NEW number, so do
  the arithmetic (37 ms → 8,905 ms is 240×: two orders, not three). `check-docs`, `check-counts` and
  `check-links` cannot see any of it. **About to write a number you did not just produce? Say you cannot
  source it** — a subagent that refused to was right.

- **A defect filed from a PARTIAL SCAN under-scopes its own fix, and the filing reads as authoritative.** <!-- trap: sub=docs,gates shape=scope-blind,unmeasured -->
  A backlog entry recorded that a folded `docs/memory.md` section was still cited in **four** places, with
  `file:line` precision; the real number was **seven across six files** (2026-08-28, `docs/task-archive.md`
  Part 107) — the scan missed the RANGE form, a second hit on a counted line, and the bench tier. **The tell
  is precision without provenance**: exact `file:line` references look like a tool's output and were the
  hits a reader happened to see. **Record the query beside the count**, so it can be re-run and disagreed with.

- **Verify a guarantee by IMPORTING the authoritative function, never by reimplementing it in the checker.** <!-- trap: sub=gates shape=scope-blind -->
  Compressing `docs/task-archive.md`, a check reimplemented "which Parts does this file declare" as
  `/^#{2,3} Part (\d+)/` and reported two Parts lost; both were fine, because `check-links`' `declaredParts`
  also accepts a bullet declaration. Importing the real function turned 19/21 into 21/21 with no change to
  the data. A reimplementation is a second definition of one rule and drifts in whichever direction its
  author forgot — here toward a false alarm, the lucky direction.
- **`curl -sSL` exits 0 on a TRUNCATED download, so verify the byte count or you are measuring a partial <!-- trap: sub=measurement,cli shape=silent-loss -->
  file.** Measured 2026-09-10 pulling two GGUFs: one arrived at 203 MB of 468, the other at 95 MB of 636,
  and `curl` reported success for both. A truncated model either fails to load — the lucky direction — or
  loads and scores garbage. **Compare `stat -c %s` against the size the registry states, and retry with
  `--fail --retry N --retry-all-errors -C -` until it matches**; resume is what makes the retry cheap. This
  is the lying-exit-code family `windows-machine.md` records, in a form that costs a whole measurement
  rather than a build.
- **A model file's size is quoted in two units that straddle a round threshold, and the answer flips.** <!-- trap: sub=measurement shape=unmeasured -->
  `bge-reranker-v2-m3` Q8_0 is 635,676,416 bytes = **606 MiB** by `ls` arithmetic and **636 MB** as
  HuggingFace displays it. At a "under 500 MB" requirement, `Q6_K` at 500,283,808 bytes PASSES in MiB
  (477.1) and FAILS in MB (500.3). **State the byte count** whenever a size decides anything; a sweep that
  ranked candidates by the wrong unit mis-sorted them by 27%.
- **…and on Windows, reading that byte count off a HuggingFace cache path gives 0.** Every file under <!-- trap: sub=measurement shape=silent-loss -->
  `snapshots/` is a SYMLINK into `blobs/`, and a reparse point's own length is zero — so
  `new FileInfo(path).Length` reports **0 B** for a 23,200,716 B model that loads perfectly, because
  `File.Exists` and every reader follow the link while the metadata call does not. Measured 2026-09-15: a
  bench preamble printed `(0 B, 512-token window)` beside a model it had just successfully run.
  <br>**It is the entry above's rule breaking in the one place the models actually live.** Follow the link
  — `File.ResolveLinkTarget(path, returnFinalTarget: true)` — or stat the blob. Git Bash `ls -la` shows the
  link and its 79-byte target string rather than the size, so the shell does not correct you either;
  `python -c "os.path.getsize(p)"` does, because it follows.
- **A community GGUF quant of a RERANKER can be missing its classification head and fail silently.** <!-- trap: sub=measurement shape=fail-open -->
  llama.cpp issue #16407: a conversion that drops `cls.output.weight` still loads and still returns
  scores — they are just wrong. One quant of the same model measured 310 tensors where the working ones
  have 311. **Smoke-test a reranker before trusting a run**: score a known-answer document against known
  distractors and assert the ordering AND that the scores are distinct. A flat or shuffled scorer reads as
  a clean null result, which is the shape `CrossEncoderRerank`'s own `DistinctScores` audit exists to catch.
- **ORDERING plus DISTINCTNESS is NOT enough — an easy fixture passes a reranker that ranks BACKWARDS, and <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  a published REFERENCE PAIR is what separates them.** With one answer and three unrelated distractors,
  `ms-marco-MiniLM-L6-v2` Q8_0 passed; on its OWN model card's pair (two on-topic documents, published
  `[8.607138, -4.320078]`) it scored **−0.093 / −0.078** and ranked the wrong one first (2026-09-12).
  Lexical overlap separates the easy fixture, so a degraded head coasts on it.
  <br>**The MAGNITUDE is a free second signal**: a healthy cross-encoder separates that pair by UNITS, and a
  model returning hundredths is not emitting logits. **DISTINCTNESS IS NOT DISCRIMINATION, and the near-flat
  case is the dangerous one**: a perfectly flat scorer is a no-op (a stable sort keeps the engine's order),
  while a noise-scaled one REORDERS by noise and passes every cardinality audit. Audit the SEPARATION against
  a known pair. Why the broken GGUFs break is upstream (`docs/model-tasks.md` §3: read `token_type_count`).
  <br>**…and the reference pair is not enough EITHER**: both its documents share the query's words, so a model
  ranking by overlap passes it (`xVITA-300M`, 2026-09-24, `rerank-screen-adopter-b10549`). Include a
  distractor that ECHOES the query more than the answer does — `rerank-screen` asserts one in two languages.
- **…and the tensor-NAME check that looks like the cheap version of that smoke test is ARCHITECTURE-BOUND, <!-- trap: sub=measurement shape=wrong-subject -->
  so it condemns working models.** `cls.output.weight` is the `bert` rerank path's head name, and a header
  read (`rerank-screen --inspect`) reported it ABSENT on three independent conversions of
  `jina-reranker-v1-tiny-en` — which names its head `cls.weight`/`cls.bias` and scores 8/8 when served.
  **Tensor presence is a POSITIVE signal only; its absence is evidence about your vocabulary, not the file.**
  Across ROLES too: a bi-encoder has no head by design, so `rerank-screen --inspect` reports `head: MISSING`
  for a healthy embedder — `embed-screen --inspect` prints the fields that decide one. Predicting a failure
  MODE from a header is a third claim on top; **serve it — that is the experiment.**
- **A BERT-family reranker SILENTLY IGNORES `--ctx-size` above its trained maximum, and the rejection <!-- trap: sub=measurement shape=vacuous,silent-loss -->
  arrives at REQUEST time on a server that started clean.** `ms-marco-MiniLM-L6-v2` launched at
  `--ctx-size 4096` loads, reports healthy and ranks a short fixture — then returns `400 … larger than the max
  context size (512 tokens)` on the first real document, because learned positional embeddings stop at 512
  and no flag moves them (2026-09-12). The size column cannot see it: the disqualified model is the SMALLER
  file. **Ask a candidate's `max_position_embeddings` before its byte count**, probe with a long input, and
  prefer an ALiBi/RoPE reranker where candidates are whole entries (**D108**).
- **`general.name` in a community GGUF is a stale template field, and the TENSOR COUNT is the identity <!-- trap: sub=measurement shape=stale-claim -->
  check.** Every `ms-marco-MiniLM` conversion surveyed 2026-09-12 — five uploaders, four different layer
  depths — reports `general.name : Ms Marco MiniLM L 12 v2`, including the L2 and L6 files. Reading it as
  identity says every one of them is the 12-layer model. The counts refute that cleanly and arithmetically:
  **39 tensors for L2, 103 for L6**, i.e. 16 per layer plus 7 fixed, where L12 would be 199. This is the
  `--alias` entry below ("a model NAME means different things on the two local servers") one layer earlier,
  in the FILE rather than at the endpoint — and the same rule closes both: **verify identity by something
  the file computes, never by something it is labelled.**
- **A screen fixture can be too HARD, and that fails WORKING models — the same defect as one too easy, <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  pointed the other way and much easier to be proud of.** Building `embed-screen` (2026-09-12), topic pairs
  sharing no content word were asserted as pass/fail; the control separated them and **all four sub-100 MB
  candidates conflated them while demonstrably healthy** — the screen would have published *"no sub-100 MB
  embedder works"*. **A screen asserts HEALTH and reports SHARPNESS**: health gets the EASY pair and a
  generous threshold, sharpness is a number printed beside a known-good control. The tell of merging them:
  your check fails a model you cannot otherwise fault. Only a PUBLISHED reference score licenses a hard
  threshold; otherwise run a control and report the difference (`embed-screen --control`).
- **A size floor attributed to a ROLE can belong to the TOKENIZER, and the arithmetic settles it in one <!-- trap: sub=measurement shape=wrong-subject,stale-claim -->
  line.** The reranker survey blamed sub-100 MB's failure on the cross-encoder role; an EMBEDDER of the same
  architecture measured 0.11% apart in size, because the wall is the vocabulary — 250,002 × 384 embedding
  parameters exceed 100 MB at Q8_0 before a single transformer layer. **Compute `vocab × hidden` before
  believing any story about why a family is too large**: quantisation is not a lever, MONOLINGUAL is the
  escape, and a mismatched vocabulary costs TOKENS too (a Chinese model spends 2,170 on text an English one
  covers in 1,207). Figures: `docs/model-tasks.md` §3.
- **CJK passed to a command-line argument goes through the console encoding and arrives mangled.** A <!-- trap: sub=encoding,cli shape=silent-loss -->
  `curl -d '{"documents":["评审会…"]}'` on this machine produced
  `parse error … ill-formed UTF-8 byte` from the server, because the GBK console rewrote the payload before
  `curl` ever saw it. **Write the payload to a UTF-8 file and pass `-d @file`** — the same rule
  `windows-machine.md` states for building file content, applied to arguments.
- **A shared runtime killed by IMAGE name takes down tenants that were never yours — and killing strictly <!-- trap: sub=measurement shape=resource -->
  by PID is still not evidence that it did not.** The rule is `windows-machine.md` §Processes; the incidents:
  on 2026-08-28 `taskkill //F //IM llama-server.exe` took down a sibling tool's embedding server on another
  port, and on 2026-09-10 a strictly-by-PID cleanup still ended with the sibling down, cause never established.
- **A port you did not CHECK is a port you do not own, and binding a busy one fails UPWARD: your requests <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  are answered by the incumbent.** 2026-09-11: 8090 was assumed free from an earlier sweep, a neighbour's
  server held it with a DIFFERENT embedding model, the launch printed no bind error, and the smoke test — *is
  the vector 768-dimensional?* — passed because both models are. **Check the SPECIFIC port immediately
  before binding; prefer one nothing conventionally uses; and verify IDENTITY, not plausibility** — read the
  served model back (`/v1/models`) and compare real vectors. A sweep of the ports you meant to use is not a
  census (enumerate `llama-server` processes), and the check must test `LISTENING`: matching the port alone
  also matches `TIME_WAIT` sockets and reports BUSY on a free port.
- **A smoke test on a TYPICAL input certifies a server that fails on the inputs the run actually sends.** <!-- trap: sub=measurement shape=vacuous -->
  Twice on 2026-09-11, and the second cost an hour of ingestion. A `llama-server` embedder answered a
  four-word probe perfectly and returned **HTTP 500 — *"input (1442 tokens) is too large… current batch
  size: 512"*** on a real turn, because the physical batch (`-ub`) defaults below the ~1,500 tokens the
  bench's own 6,000-character truncation permits. A separate config broke the same way at 400, because
  `--parallel N` DIVIDES the context across slots and a short probe never reached the slot ceiling.
  <br>**So probe the EXTREME, never the typical**: the longest input the harness can emit, the largest
  batch, the deepest candidate list. A probe that only exercises the easy case is a control that cannot
  fail — the same defect as the vacuous arms recorded above, one layer down in the stack. The pair worth
  running is one long input AND one short one: the long says the server can do the work, the short is the
  identity check that it is still the same model.
- **GPU CONTENTION inverts the offload decision, and it hits GENERATION hundreds of times harder than <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  encoding — so the same flag is right and then catastrophically wrong on one box within an hour.**
  Measured 2026-09-11 with `llama-bench`, 11.2 GiB of VRAM free in every cell; only the neighbour changed (a
  game-streaming host rendering for "busy", idle for "quiet").

  | model, test | `-ngl 0` busy | offloaded busy | `-ngl 0` quiet | offloaded quiet |
  |---|---|---|---|---|
  | `gemma-3-4b-it`, generation | 9.22 | **0.10** | 6.08 | **73.77** |
  | `nomic-embed-text`, encode | 175 | 839 | 9,901 | **53,295** |

  **Offloading is right by DEFAULT** — 12× on generation and 5.4× on encoding when the device is free. Under
  contention generation swings from **12.1× faster** to **92× slower**, a **~1,100× reversal**, while encoding
  barely moves (5.4× quiet, 4.8× busy): **encode-only work is ROBUST to a busy GPU and generation is not.**
  **Contention decides the offload level; the task's shape only decides how badly a guess is punished.**
  Encode-only vectors are byte-identical across devices, so switching there is a free speed choice and runs
  compare across it.
  <br>**The tell is a NON-MONOTONE offload curve** (0.45 / 2.49 / 0.33 / 0.10 across `-ngl` 8/16/24/34): a wrong
  setting degrades smoothly, contention does not, and prompt processing moves the OPPOSITE way to generation.
  **Always pass `-ngl` explicitly** — the default is not neutral and logs nothing — and **measure the serving
  configuration before the model**: a number that looks like a property of the model is one of the serving
  layer (**D107**'s warning in a second form).
- **A bare `new HttpClient` leaves PROXY RESOLUTION on, which costs up to 2 SECONDS per local call — and it <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  is BIMODAL, so it reads as the model's tail latency rather than as an offset.** Measured 2026-09-11: a
  number that looks like a property of the model is a property of the CLIENT.

  | client | mean | max |
  |---|---|---|
  | `localhost`, proxy on | 513 ms | **2,051 ms** |
  | `127.0.0.1`, proxy on | 8.9 ms | 34 ms |
  | either, `UseProxy = false` | **0.4 ms** | 1.0 ms |

  <br>**The widely-repeated explanation — `localhost` resolving to `::1` first — is wrong here**: nothing at
  the TCP layer is slow, and disabling the proxy collapses BOTH spellings. **So `127.0.0.1` only mitigates** —
  still 15× the direct path — and `UseProxy = false` on the handler is the fix. **Any bench that reports
  latency must set it**; most still build a bare `new HttpClient`, which is harmless only where latency is not
  published. The check that shipped with the wrong story asserted an ORDERING, which passes while the
  mechanism is wrong: **aim a check at the mechanism (proxy on against off), never at the symptom.**
- **A model NAME means different things on the two local servers, and a wrong one is not an error.** <!-- trap: sub=measurement shape=wrong-subject -->
  Ollama routes by it; a `llama-server` started with `--model` serves ONE model and answers to its
  `--alias`, so the name is a label and you get the loaded model whatever you ask for. It selects only on a
  router server (`--models-dir`). **Never infer from a green run that the model you named is the model that
  answered** — and run your OWN server on its own port rather than borrowing one that happens to be up,
  because a server is started with a context and a batch size and those decide what it will accept.
- **`llama-server`'s ROUTER mode is a process SUPERVISOR, not one process holding several models — so <!-- trap: sub=measurement shape=wrong-subject,resource -->
  consolidating seams onto a router saves no memory at all.** Measured 2026-09-11 on build 10603: the router
  spawns a full `llama-server.exe` CHILD per model (argv readable in `/v1/models` `status.args`), so weights,
  KV cache and compute are separate in both topologies — three dedicated servers and one router with three
  models cost the same, plus a ~115 MB supervisor. A router buys ONE endpoint, on-demand loading and an
  eviction policy (`--models-max`, default **4**). **Residency and routing are independent axes**, and two
  seams contend only when they want the SAME model. Tree-kill reaches all three levels (router → child →
  grandchild), but assert every PID gone rather than trusting the exit code.
- **`--embedding` and `--reranking` are PROCESS-WIDE, so a plain `--models-dir` router serves chat and <!-- trap: sub=measurement shape=unmeasured -->
  nothing else — while still LISTING every model it found.** A router started without them answers chat and
  returns `501 … This server does not support embeddings` after spawning the children anyway, so the model
  list and the process table both look right (2026-09-11). **Per-model roles need `--models-preset`**, an
  INI whose section is the served id and whose keys are long-form flags without the `--` (recoverable from
  the router's own `status.preset`):
  ```ini
  [embed]
  model = <dir>/embeddinggemma-300M-Q8_0.gguf
  n-gpu-layers = 99
  embeddings = true
  ```
  Check this before scoping a run around a router: the failure arrives after the model list reads fine.
- **The two local servers disagree about an over-long input, and the quiet one is the dangerous one.** <!-- trap: sub=measurement shape=silent-loss -->
  Ollama truncates and answers; `llama-server` returns `500 … input (N tokens) is too large`. Moving a
  bench across them turns an invisible truncation into a crash — LongMemEval reaches 76,560 characters
  against a median of 429, so every earlier figure was cut by whichever server answered. A CHARACTER budget
  cannot bound a TOKEN limit (density varies tenfold across scripts; **D177** takes one anyway, with
  margin): shrink and retry on the server's own complaint, floor it so a pathological input fails loudly,
  and COUNT the truncations in the output.
- **An Ollama model IS a GGUF on disk, and stock `llama-server` still may not load it.** The blobs under <!-- trap: sub=build,measurement shape=unmeasured -->
  `~/.ollama/models/blobs/sha256-*` carry the `GGUF` magic and Ollama runs them through its own bundled
  llama.cpp, so pointing your own `llama-server --model <blob>` at one looks like a free way to serve an
  already-downloaded model. Measured 2026-09-09 on `gemma3:4b` against build 10603:
  `error loading model hyperparameters: key not found in model: gemma3.attention.layer_norm_rms_epsilon`.
  Ollama's conversion omits a key upstream requires and its own runner supplies. **Read the manifest to find
  the blob** (`manifests/registry.ollama.ai/library/<model>/<tag>`, the `application/vnd.ollama.image.model`
  layer) — but expect to need the model's own GGUF from its source, and check before planning a run around it.
- **A gate anchored in PROSE dies when the prose moves, and the failure blames the wrong thing.** Rewording <!-- trap: sub=gates,docs shape=stale-claim -->
  the only sentence a `check-counts` claim matches turns the gate red with a message about a stale number.
  Move such a sentence VERBATIM and run the gate between the copy and the delete, so both copies match at the moment
  of the move. The narrow forms that bite: a trailing semicolon (`Twelve packages;`), bold markers and an <!-- count-ok: the package count is quoted as a FORM, not as a claim about how many packages ship -->
  em-dash (`**FIVE arms —`), digits rather than a number word (`573/573`), and an en-dash in a range
  (`D1–D112`). And `parseCount` has no hyphenated compounds, so past twenty write digits. <!-- count-ok: the en-dash range is quoted as a FORM, not as a claim about how far the log goes -->
- **A blocked item is refuted by looking where its BLOCKER lives.** The rule is <!-- trap: sub=docs shape=wrong-subject -->
  `.claude/rules/task-lifecycle.md`; the incident: on 2026-08-23 an item sat blocked on "a real embedding
  model" while one was pulled on the machine, because a careful re-check read the TREE, as the other five
  blockers needed, and never the MACHINE.
- **A decision written AHEAD of its code reads exactly like one describing the tree, and no gate can tell <!-- trap: sub=docs shape=stale-claim -->
  them apart.** `check-decision-claims` reads the tree for a claim, so a claim the tree has not caught up
  with is invisible to it. Carry a "not yet implemented" banner on the entry until the change lands, as
  **D151** did on the day it was written and shipped.
- **The rules tier's YAML frontmatter costs nothing in context, so deleting it saves nothing.** The harness <!-- trap: sub=docs shape=wrong-subject -->
  strips it before injection: 2,857 bytes off disk, **zero** off the context (measured for **D113**). When
  sizing an always-loaded tier, measure the BODY the session actually receives, not the file.

- **A server that ACCEPTS a `tools` array and returns 200 has not promised to use it — a model with no <!-- trap: sub=measurement shape=fail-open,silent-loss -->
  tool template drops the roster on the floor and answers from parametric knowledge instead.**
  `gemma-3-4b-it` on `llama-server` build 10603, sent three function definitions: **HTTP 200,
  `tool_calls: null`**, and a fabricated weather report; `tool_choice: "required"` and `--jinja` changed
  nothing (2026-09-12). A positive control — `qwen2.5-0.5b-instruct` on a byte-identical payload in the same
  run — returned the call on all three variants, so **the build is fine and the model is the cause**.
  **Measure a model you KNOW emits tool calls before concluding anything about one that does not**; read the
  GGUF's own `tokenizer.chat_template` (gemma-3 has no tool section), and prefer the prompt protocol, which
  `ToolLoop` falls back to, wherever the template is unknown.

- **An ACCURACY table is the wrong readout for a transport question — the two transports differ far more <!-- trap: sub=measurement,generation shape=wrong-subject,vacuous -->
  in HOW they fail than in what they score.** On `qwen2.5-0.5b-instruct`, native function-calling against
  `ToolLoop`'s prompt protocol moves accuracy by a few points while false calls on requests NO tool serves run
  20-30% against 90-100%, and the prompt path finishes the loop on only 11-24% of trials against 99-100%
  (2026-09-13; `docs/memory-measurements.md` §5, `affordance-native-transport`). **Count what the loop DID,
  not just what it chose** — a deployment is choosing which failure to handle, and an accuracy table reports
  that as a tie.
- **A scorer that matches TEXT is moved by any option that only changes how much text is SHOWN — so the <!-- trap: sub=measurement,memory shape=wrong-subject,vacuous -->
  arm reads as a retrieval change when nothing about retrieval moved.** Scoping `MemoryDetail.Full` against
  LongMemEval (2026-09-13): the knowledge-update scorer in fact matches `Turn.Tag`, a synthetic id at
  character 0 that every 120-character headline keeps, so that class was immune — but `Detail` CAN change
  which items come back, not through ranking but through SIZE: a character budget prices whole items higher,
  and `MemoryWalk` ends a step earlier under `Full`. **"Projection-only" is not "inert"**: a lever can reach
  the scored set through a SECOND channel. Of this repository's metrics, `evidence-hit@k` matches a `dia_id`
  and is immune, a reader-facing token-F1 genuinely measures the effect, and a text-matched model-free
  metric is confounded. **Ask whether the SCORER reads what the option varies**, and score by an identifier
  the option cannot touch.
- **A p-value that WANDERS between runs is evidence the test is underpowered, not evidence of no <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  effect.** Two runs of the transport grid each had one cell under p = 0.05, a different cell each time, and
  were written up as "a wash" — while the per-cell EFFECT SIZES agreed across both runs and a third landed on
  them again (2026-09-13). ~50 discordant trials cannot resolve a 10-trial net, so McNemar's p wanders.
  **Compare effect sizes across runs first and treat p as a property of the sample size**: two runs that
  disagree on significance while agreeing on magnitude are a reason to run a third, never to publish a null.
- **An OUTPUT CAP that cuts a turn off before any tool call reports as an empty `tool_calls`, which is <!-- trap: sub=measurement,generation shape=silent-loss,vacuous -->
  byte-identical to a model that DECLINED.** Same run. A 22-30% decline rate is a headline, and a harness
  that cannot separate the two would be publishing its own `max_tokens` as a model property. `finish_reason`
  is the separator and it is free — the field is already on the response. Measured and printed: **16 of
  1,469 native turns (1.09%)**, so the finding survived, but only because the counter existed to say so.
  **Any fail-open arm needs the artifact counted, not argued away** — this is the *answered / declined /
  unreachable* three-outcome rule one layer down, with the cap as a fourth.
- **A dataset's ground truth is a SET, and taking its first element turns the rest into DISTRACTORS — so <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  the benchmark scores a right answer as wrong.** `memory-decision` took LoCoMo's `Evidence[0]` as "the"
  answer while 26.6% of scored questions carry more than one evidence turn (97.9% of multi-hop), and drew
  distractors BY COSINE — exactly the discarded golds (2026-09-12). The bias FLATTERED the constant-emitting
  arm and reversed a headline. **The tell is a plural field read in the singular**; reusing a corpus loader
  does not inherit its SCORING rule. Filter to the questions with exactly one answer rather than picking a
  representative, and exclude every flagged id from the distractor pool.

- **A model asked for a score on a scale it will not use returns a BINARY verdict, and the tie rate that <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  follows reads as a property of the SHAPE under test rather than of the prompt.** Asked for `LlmScorerBase`'s
  own 0..1, a 4B and a 1B replied only `0` or `1` — an argmax over that is a coin flip — while an integer
  0-100 drew a genuinely graded scale from the same model (2026-09-12). **The tell is a tie rate, visible only
  if something COUNTS ties.** CAPTURE WHAT THE MODEL SAID before pricing what it did: a raw-reply dump over the
  first trials separates "cannot do this" from "was not asked for something it can give".

- **Pooling a per-POSITION table over list lengths confounds position with length, and the confound points <!-- trap: sub=measurement shape=wrong-subject,scope-blind -->
  the same way the real effect does.** Summed over N = 3..7, slot 7 exists only at N = 7 while accuracy falls
  with N, so every arm showed a slope by slot — **including three argmax arms that cannot have a position
  effect at all** (2026-09-12); read at ONE list length, the finding changed shape. **When a breakdown's
  categories are not available in every cell being summed, the aggregate measures availability**: report one
  cell, and keep the arm that structurally CANNOT show the effect in the table for exactly this reason.

- **A per-item signal "predicting" relevance is usually predicting LENGTH, so score it within length <!-- trap: sub=measurement,memory shape=wrong-subject -->
  strata or it will read as a finding.** Measured 2026-09-23 (`docs/task-archive.md` Part 273): a model's
  arousal rating separated LoCoMo's evidence turns from the rest at AUC **0.544**, with a **1.26×** lift in
  its top tenth — a publishable "affect weakly predicts relevance". Within length quartiles it read
  **0.474**, below chance, while length ALONE reached 0.724: a longer turn carries more facts to be asked
  about AND more words for a rater or a lexicon to react to. **Print the length AUC beside every per-item
  signal and score the signal within length bins**; any salience, importance or affect score computed from
  text inherits this confound by construction.

- **A fan-out bench that lets ONE call throw discards every trial it had already finished, and the longer <!-- trap: sub=measurement shape=resource,wrong-subject -->
  the run the likelier it is.** One chat call stalled past `HttpClient.Timeout` on a busy GPU and its
  `TaskCanceledException` escaped `Parallel.ForEachAsync`: 96 minutes and ~70 completed trials gone
  (2026-09-12). **A fail-open arm needs a THIRD outcome — answered, declined, UNREACHABLE** — or a stall
  publishes as "the model chose not to call a tool". Catch per call, count it, exclude it from every rate,
  PRINT the count, void the run above a threshold, and never swallow the caller's own cancellation (filter on
  `!ct.IsCancellationRequested`, since `TaskCanceledException` IS an `OperationCanceledException`).

- **A tool roster is not a menu a model will decline — hand a 4B seven tools and it invokes one for most <!-- trap: sub=measurement,generation shape=fail-open,silent-loss -->
  requests NONE of them serves**, fabricating arguments to force a fit (`restart_service {"service":
  "sourdough_starter_knowledge_base"}` for a baking question) while routing the roster WELL when a right tool
  exists (2026-09-12, `tool-affordance`). **Prompt wording does not reach it** — two rewrites in opposite
  directions moved nothing. **Bound the roster BEFORE the model sees it**, and make an expensive tool refuse
  rather than trusting the call not to arrive; a small model fails the MIRROR way. **A fixture where every
  request has a right answer cannot see a false positive** — build the negative half before tuning against
  the positive one. Figures: `docs/model-tasks.md` §3.1.

- <!-- trap: sub=gates shape=vacuous -->**A `retiredTerms` pattern written with `\b` in a JavaScript single-quoted string can NEVER fire, and
  nothing reports it.** The source needs `\\b`; a single backslash is JS's *backspace* escape, so the rule
  compiles to U+0008`Name`U+0008 and matches nothing. `check-docs` then passes on a tree full of the term it
  was added to ban — the gate is present, green, and dead, which reads exactly like "clean".
  <br>**Every registry entry that is a REGEX has this shape**, and CLAUDE.md's rule — *write every new
  allowance so that one looser than needed, or one that stops matching, FAILS* — is the same principle one
  level up: it covers allowances that rot, and this is an entry that was never alive.
  <br>**PROVE a new pattern matches before trusting it.** Load the config and run the regex against a
  positive probe and a negative one:
  `node -e "const c=require('./devtools/project.config.mjs');const e=(c.default??c).retiredTerms.find(t=>/YourName/.test(t.term));console.log(new RegExp(e.term).test('uses YourName here'))"`.
  A `true` on the positive and a `false` on the near-miss is the check; a green `check-docs` is not, because
  a dead rule and a clean tree are indistinguishable from the outside.

- <!-- trap: sub=gates,docs shape=scope-blind,vacuous -->**A rename registered by its METHOD name leaves the
  TYPE siblings live, and `check-api-vocabulary` reports a clean run over the half it was given.** Found
  2026-09-15 (**D138**): **D132** retired `AddLocalProvider` and `AddOnnxEmbedder`, so the registrations <!-- drift-ok: the entry naming the half-landed rename is its whole subject -->
  moved — while `LocalProvider`, `LocalModelOptions`, `OnnxEmbedder` and `OnnxEmbedderOptions` stayed, and <!-- drift-ok: as above -->
  `AddLlamaSharpProvider` went on constructing a `LocalProvider`. Both options types are what a consumer <!-- drift-ok: as above -->
  CONFIGURES, so the stale names were on the public surface the whole time. A name-driven gate is only as
  complete as the list it was handed: **enumerate the whole vocabulary before writing the entry** — the
  method, the type it constructs, its `*Options`, the `*BuilderExtensions` class, the NAMESPACE, the test and
  file names — and `grep -i` the old word over `src/` until only what you meant to keep remains.
  <br>**The same grep catches the other half: a substring replace MINTS names that never existed.** The sweep
  renaming `AddOpenAiCompatible` → `AddHttpProvider` turned the longer `AddOpenAiCompatibleEmbedder` into <!-- drift-ok: the sweep this entry is about -->
  `AddHttpProviderEmbedder`, in two runtime error messages and two released CHANGELOG entries, every gate <!-- drift-ok: as above -->
  green. **Anchor every rename with `\\b` on BOTH sides, and audit for concatenations afterwards**
  (`git grep -hoE "\\b<NewName>[A-Za-z0-9]+\\b"` returns the damage directly).

- <!-- trap: sub=gates,docs shape=wrong-subject,silent-loss -->**A vocabulary gate pointed at a FROZEN record
  does not find drift — it manufactures it, because the cheapest way to make it green is to rewrite the
  record.** A sweep renaming identifiers across every `.md` rewrote `CHANGELOG.md` BELOW its released headings
  (a 3.0.x entry came to read `HttpModelOptions.ContextSize` when that release shipped `OpenAiCompatibleOptions`, and a rename table's <!-- drift-ok: the corrupted spellings are this entry's subject -->
  TARGET column changed) and the frozen v0.1 design record, collapsing `IGenerationProvider` and `IGenerationStreamProvider` <!-- drift-ok: as above -->
  to one name inside a sentence that CONTRASTED them (2026-09-15). Every gate stayed green. **`check-docs` and
  `check-samples` share `HISTORICAL` for this** — add a record to it rather than annotating it. **The audit**:
  a rename touching `*.md` should change ONLY maintained state, so diff every historical document against the
  pre-sweep commit and expect zero; a record earns a DATED amendment, never an edit to its body.

- <!-- trap: sub=build,gates shape=scope-blind,silent-loss -->**A registry of PUBLISHED names is not prose,
  and a rename sweep rewrites it anyway — then the tool that reads it reports the corruption as a routine
  line.** `devtools/nuget-unlist.mjs`'s `RETIRED` array holds ids that exist only on nuget.org, so nothing on
  disk disagrees when a sweep rewrites one: three were rewritten on 2026-09-15, and the very next commit
  (**D145**) broke the published `Lyntai.Providers.ExtensionsAi` again, with ten listed versions behind it. A <!-- drift-ok: the PUBLISHED id is this trap's subject; naming it is the whole point -->
  404 printed `- not published, skipping` beside `Done.`; an unpublished `RETIRED` id is now an ERROR, true by
  construction. **When a rename campaign ends, diff every registry of HISTORICAL names, not just every
  historical document.**

- <!-- trap: sub=gates,build shape=stale-claim,vacuous -->**A gate deliberately kept OUT of the routine run
  rots exactly as an unrun test does, and it tells you at the worst possible moment.** `consumer-smoke`, the
  release gate, takes minutes and so is out of `verify`; nobody ran it through the D125–D147 renames, and on
  2026-09-17 it failed with four compile errors — its consumer fixture still named `GenerationCandidate`. The <!-- drift-ok: the retired name the fixture still held is the subject -->
  fixture is a CONSUMER written in the public API, living in `devtools/` where no prose gate scans and no
  solution build compiles it, so every sweep that updates callers must update it too. **"Run it before a
  release" is not a mechanism**: a gate too slow for `verify` still needs a trigger someone cannot forget.

- <!-- trap: sub=tests,gates shape=silent-loss -->**Deleting a test file removes the tests you did not read,
  and a green run cannot tell you — the SKIP count is the only tell.** Splitting the fused cross-encoder
  policy (**D139**, 2026-09-15), the old suite was read to line ~192 and deleted, taking a `[SkippableFact]`
  live test and a DISPOSE CONTROL with it; the pair moved +12 where +18 was expected and the skip roster went
  29 → 28. A LOWER skip count means "a live suite ran" only when one could have. **Before deleting a test
  file, list what is in it** (`grep -nE "\[Fact\]|\[Theory\]|\[SkippableFact\]"`) and account for every entry:
  the tail you never opened is where live tests and controls collect.

- <!-- trap: sub=tests shape=vacuous -->**A TAMPER test that writes a FIXED value tampers with nothing on
  the runs where the value was already there — and then asserts that an UNTOUCHED input throws.**
  `Tampered_recovery_wrap_throws` overwrote the last two base64 characters of a random wrap with `"AA"`, so
  whenever the wrap already ended that way the test failed (2026-09-15). It reads as flakiness and will not
  reproduce in isolation, the shape `TASKS.md` Part 99 watches. **Derive the mutation from the input and
  assert it changed** (`Assert.NotEqual(original, mutated)`) before the real assertion.

- <!-- trap: sub=storage,tests shape=wrong-subject -->**`SqliteConnection.ClearAllPools()` is
  PROCESS-GLOBAL, so one test's teardown evicts another test's pooled connection mid-query** — the victim
  fails with `ObjectDisposedException … 'SQLitePCL.sqlite3'` naming the CONNECTION FACTORY, in a different
  class under the parallel runner, and is green in isolation. **`SqliteConnection.ClearPool(connection)` is the
  scoped form** `TempDbPath.Dispose` uses; a pooled handle keeps `-wal`/`-shm` alive, which is why the delete
  is wrapped in a swallow. **A "clear everything" API in per-test teardown is scoped to the PROCESS**: reach
  for the per-resource overload, or move the cleanup to a fixture whose lifetime matches.

- <!-- trap: sub=gates,docs shape=scope-blind -->**`check-samples` proves a sample is TYPE-correct, never
  that a consumer could write it** — its scratch project opens EVERY `Lyntai.*` namespace, so a block
  compiles wherever the member it calls lives. The README once showed `client.AsChatClient()` while that <!-- drift-ok: the retired extension this entry is about -->
  extension sat in `Lyntai.Providers.ExtensionsAi`, a PROVIDERS namespace (**D145**). The ambient list is <!-- drift-ok: the namespace this entry is about -->
  right for the gate's job, so this is a LIMIT: **to check reachability, build a throwaway project that
  imports ONLY what the prose does** (`devtools/_probe`, gitignored).

## LLM / router (details in `llm-and-router.md`)

- **A timeout as a single `CancelAfter` over a whole call** — a wall clock that kills a slow-but-alive <!-- trap: sub=router,cli shape=cancellation -->
  child (a long tool loop, a big prompt) exactly like a dead one. Both the STREAMING path and the BUFFERED
  `ProcessRunner.RunAsync` must use a per-chunk inactivity clock (re-armed on each read); the buffered path
  adds an absolute `maxDuration` backstop and reports `ProcessResult.TimeoutKind`.
- **A non-positive resolved budget means the OPPOSITE thing in the two domains — don't "unify" the idiom <!-- trap: sub=router,generation shape=cancellation -->
  casually.** The LLM sites arm the clock unconditionally (`HttpModelProvider.CompleteAsync`,
  `HttpVectorTransport.EmbedBatchAsync` and `HttpRerankTransport.ScoreAsync` all `CancelAfter(timeout)`), and app-configured
  values are trusted rather than clamped (`LyntaiOptions.ResolveTimeout`), so a `TimeoutByConsumer` entry of
  `TimeSpan.Zero` **cancels the call instantly**. `GenerationDeadline.GuardAsync` reads the same value as
  **no deadline at all** — the documented escape hatch for a host that owns its own clocks. Both are
  deliberate; flipping either is a behaviour change no consumer can detect at compile time (major-bump
  material, `docs/DECISIONS.md` D18), and a shared helper that keeps both behaviours behind a flag has
  consolidated nothing but the line count.
- **Committing a stream on an empty content chunk** — disables fallback for a zero-content first chunk. <!-- trap: sub=router shape=fail-open -->
  Gate the commit on `Text.Length > 0`.
- **Hand-rolled verdict heuristics** in a provider — they drift. Always route through <!-- trap: sub=router shape=wrong-subject,second-door -->
  `ProviderVerdictClassifier`. And keep it conservative: a bare word like "unauthorized" or a "429" in a
  stack frame must not trip a verdict that benches a healthy host.
- **Empty provider output as `Ok`** — must be `Failed` (and a terminal `Error` chunk when streaming) so <!-- trap: sub=router shape=fail-open -->
  the router can fall over.
- **A backend whose `StreamAsync` throws from the CALL, not from inside an iterator, walks past a guard on <!-- trap: sub=router shape=second-door,vacuous -->
  `MoveNextAsync`.** An async iterator defers every throw to the first read, so a router guarding only
  `MoveNextAsync` looks complete, and every iterator-based fake agrees. A non-iterator implementation throws
  from `StreamAsync` itself: that escaped both routers with no fallback, and the text span recorded `Ok`.
  **Open the stream inside the guarded read** (`StreamOpening.Deferred`), and prove it with a fake that is
  NOT an iterator.
- **A "never throws" JSON reader that catches only `JsonException` throws on valid JSON of the wrong <!-- trap: sub=router,cli shape=fail-open -->
  SHAPE.** `TryGetProperty` throws `InvalidOperationException` on a non-object and `GetInt32` a
  `FormatException` on `1.5`, so `{"choices":[null]}`, `{"type":"assistant","message":"x"}` or a rerank body
  of `[]` — all well-formed — escaped `CompleteAsync`, a stream or an agent session. Read wire JSON through
  `WireJson`'s guarded reads, and catch `WireJson.IsShapeFault` as the backstop, never `JsonException` alone.
- **A streamed tool-call SLOT read from the wrong JSON level merges calls silently.** Ollama nests `index` <!-- trap: sub=router shape=silent-loss,unmeasured -->
  inside `function`, so a rule of "top-level `index`, else position within the line" put two lines' calls in
  slot 0 — the first name kept, the arguments concatenated into invalid JSON, the second call gone — and only
  the buffered path had a live test (`docs/FIXES.md`, 2026-09-25). **A wire whose calls arrive whole marks
  them `Complete` and never joins slots across lines**; measure the STREAMED shape before trusting it.
- **Asking the `claude` CLI a question it doesn't recognize SPENDS A TURN.** An unrecognized token is <!-- trap: sub=cli shape=unmeasured,resource -->
  treated as a PROMPT, not an error (`claude zzznotacommand` answers in prose), and `config`/`models` hang
  waiting on a session. So a turn-free question must be a **flag** or a **verified** subcommand — `--version`
  (`ProbeAsync`), and `update`/`install`/`auth {status,login,logout}`, each checked against the installed CLI's
  own `--help` before being wired. Never "just try" a plausible subcommand to see what it reports: the build
  stays green while every call quietly costs tokens.
  Corollary: the CLI has no turn-free model readout — `ProviderProbeResult.Model` is null there by design,
  and the resolved model comes from `AgentStreamEvent.UsageFinal.Model` after a turn.
- **…and the flip side, because "it costs a turn" was used to defer measurable work twice: a CLI's own <!-- trap: sub=cli,measurement shape=unmeasured -->
  READ-ONLY subcommands are a free measuring instrument.** `--help` is the obvious one, but a CLI that
  manages configuration usually has a command that PRINTS what it would use, and printing config spends no
  tokens. Measured 2026-08-05 while closing CLI14: `codex mcp list -c 'mcp_servers.x.command="node"' …` and
  `codex mcp get x …` echo back the server codex actually registered, which turned every `-c mcp_servers.*`
  key from documented-not-measured into measured; the claude side was settled by dropping a candidate
  document in as a project `.mcp.json` and reading back `claude mcp list`. **Before writing "unverified" into
  an XML doc, look for the read-only subcommand** — CLI13 waited on "an unmeasured CLI" when
  `codex exec resume --help` had been free all along.
- **A subcommand you rely on may not exist on an OLDER install — pin a FLAG so it fails loudly.** <!-- trap: sub=cli shape=fail-open,unmeasured -->
  `auth status --json` sends `--json` explicitly even though it is the CLI's default (measured on v2.1.220),
  precisely because a build predating `auth` rejects the unknown *flag* (exit non-zero, no turn) instead of
  billing a turn to answer the sentence "auth status". Same reasoning as above, one step further out.
- **Trusting the exit code over the machine-readable answer.** A signed-out `auth status` may report its <!-- trap: sub=cli shape=ordering,silent-loss -->
  state AND exit non-zero — that's an ANSWER, not a broken backend. Parse first, then fall back to the exit
  code (`CliProviderEngine.StatusAsync`), and parse the WHOLE body: the `Tail()` helper keeps the LAST 500
  chars, which would decapitate a JSON document. **The completion path shipped the same defect** — a 401
  reported in band with a non-zero exit was classified from stderr as `Failed` instead of `AuthFailed`
  (`docs/FIXES.md`, 2026-08-05). **Whenever a backend can answer in TWO channels, decide the precedence
  explicitly and pin it with a test**: the backend's own words outrank the exit code.
- **Re-implementing the CLI rules for a new CLI backend.** Everything above lives in <!-- trap: sub=cli shape=second-door -->
  `CliProviderEngine` (Core, `Lyntai.Inference.Cli`); a new CLI is an `ICliBackend`, never a fresh
  `IModelProvider` (`docs/DECISIONS.md` D21). The reason these traps were fixable at all is that there is now
  ONE copy.
- **Assuming a non-zero exit means failure — a CLI can report failure IN BAND and exit 0.** Measured on <!-- trap: sub=cli shape=second-door,silent-loss -->
  codex-cli 0.146.0: a 401 turn prints `{"type":"turn.failed","error":{"message":"… 401 Unauthorized …"}}`
  and the process still exits cleanly. Map it to `CliOutputEvent.Failure` so the message is classified
  (`AuthFailed` cools the host; a bare `Failed` just advances). Ignoring it yields "no output produced" —
  right verdict class, no reason, wrong routing. **And it can do BOTH:** measured 2026-08-05, an expired
  login prints `turn.failed` AND exits non-zero, so "in band" and "non-zero exit" are not two disjoint
  cases to branch on — see the exit-code-precedence entry below.
- **…and the mirror image: treating every error-ish line as terminal.** Also measured on codex: a bare <!-- trap: sub=cli shape=wrong-subject -->
  `{"type":"error","message":"Reconnecting... 2/5"}` and an `item.completed` whose item type is `error`
  ("Model metadata not found") both appeared in a run that went on to **succeed**. Only the terminal event
  (`turn.failed`) may fail a call, or healthy calls die on a retry they recovered from.
- **A neutral working directory can break a CLI that expects a repo.** The engine spawns from a temp dir on <!-- trap: sub=cli shape=second-door,vacuous -->
  purpose (§6 hygiene). codex refuses to run outside a git repository, so its backend MUST pass
  `--skip-git-repo-check` — every completion would fail on a perfectly good install otherwise. Check what
  your CLI assumes about its cwd. **And the flag is needed on the AGENT path too, where the cwd is the
  caller's project**: that reads as "obviously a repo" on a developer's machine and is very often not one in
  a shipped bundle — a passing test that hides a shipped failure. Both codex paths build argv from
  `CodexExecArgs` for exactly this reason; a second copy is a second chance to lose the flag.
- **Adding a SECOND seam over the same CLI without sharing the wire knowledge.** `CodexAgentSession` reads <!-- trap: sub=cli shape=second-door -->
  the same JSONL as `CodexCliProvider`, so the vocabulary and the non-terminal-`error` rule live once in
  `CodexEnvelope`. Two readers of one wire format drift, and the drift is invisible until the halves disagree
  about whether a turn failed.
- **Mapping a wire format you have not measured, name by name.** <!-- trap: sub=cli shape=unmeasured -->
  > MEASURED since `docs/task-archive.md` Part 260 (codex-cli 0.155.1): `item.started` fires for every tool
  > item, and `reasoning` with field `text` is confirmed. What remains true: the tool arm is reached by
  > elimination, so a tool step's KIND is provisional and its PAYLOAD reliable.

  The codex agent session's tool-step half was written **shape-driven** before it was measured: any unknown
  item type becomes a tool step under the BACKEND's own name carrying the BACKEND's own payload, nothing
  renamed or normalised. That guarantees *no payload is invented or dropped* and *every uncertainty stays
  inside the tool-step half* — NOT the right KIND of event: `CodexAgentReader.ReadItem` reaches the tool arm by
  **elimination** against three names, so a renamed `reasoning` would surface as a *fabricated* `ToolCall`.
  Tell consumers to switch on `ToolCall.Name`. Mark every inferred member as inferred in the XML docs, and
  where a guess would COST something (codex spends a turn on an unrecognized subcommand), refuse rather than
  guess (`docs/DECISIONS.md` D35).
- **When one layer RESOLVES a caller's "not stated" into a concrete value, the next layer cannot tell it <!-- trap: sub=memory,storage shape=silent-loss -->
  from a value the caller chose — and writing it back destroys their data silently.** Three times in one
  subsystem (**D91**; `docs/FIXES.md`, 2026-08-26): `GraphMemoryEngine` turned `MemoryGrade.Inherit` into
  `Associative` and a null `Headline` into a truncation, and the store overwrote unconditionally; `Metadata`
  was ignored from the other side. **Carry the DISTINCTION, not the resolved value** (`GradeStated`,
  `HeadlineStated`, `COALESCE(@metadata, stored)`). Ask of any field a caller may omit: **does some layer turn
  the omission into a value, and does a later layer persist it as chosen?** Two of the three were found only
  by grepping the first fix's own diff for the other places the distinction applied — this file's
  §"Copying a rule copies its assumptions" advice, run against its own author.

- **A NEGATIVE result cached in a process-lifetime cache is permanent, and it is the one answer that had no <!-- trap: sub=cli,tests shape=resource,fail-open -->
  business being remembered.** `ProcessRunner.ResolveCommandPath` memoized a failed `where.exe` lookup, so
  one transient failure made an installed CLI look absent until restart (`docs/FIXES.md`, 2026-08-26). **A
  success is a fact, a failure is a MOMENT.** It presented as a TEST FLAKE of a constant 9 tests — **a stable
  flake count points at one shared piece of state, not a race.** When you fix a cache, write the POSITIVE
  control too: "the failure is not cached" passes on an implementation that caches nothing at all.

- **Trusting an explicit command without checking it exists.** For a PORTABLE install (an app's own bundled <!-- trap: sub=cli shape=unmeasured,vacuous -->
  CLI copy) `IsAvailable` must verify presence — `ProcessRunner.CommandExists`, which also accepts an
  extensionless launcher with a spawnable sibling. Returning true for a path that isn't there turns a
  skippable candidate into a failed turn.
- **Forwarding a free-form option value straight into argv.** `ProviderLoginRequest.Mode` / <!-- trap: sub=cli shape=second-door -->
  `ProviderInstallRequest.Version` are free-form so other backends fit the contract — but an adapter must
  REFUSE a value it doesn't recognize instead of synthesizing `--<whatever>`, and must refuse a flag-shaped
  value in a data slot (`Email: "--dangerously-x"`, `Version: "--force"`). `ArgumentList` prevents *shell*
  injection, not the backend's own argument parser reading your value as an option. An agent session's
  `ResumeToken` is such a slot, and the claude session shipped without the refusal its codex twin had
  (`docs/FIXES.md`, 2026-09-25): both now share `AgentResumeToken`.
- **Spawning a Windows CLI by its EXTENSIONLESS npm/nvm shim** — a global npm install writes three <!-- trap: sub=cli shape=ordering,second-door -->
  launchers side by side (`claude`, `claude.cmd`, `claude.ps1`); the extensionless one is a POSIX `sh`
  script, and CreateProcess rejects it with *"The specified executable is not a valid application for this
  OS platform"*. Green on every dev box whose `where.exe` happens to list the `.cmd` first, broken on the
  one whose PATH/PATHEXT doesn't — and broken for any caller handed the bare shim path directly
  (`CLAUDE_CMD`, a BYO command). `ProcessRunner.ResolveLauncher` swaps in the spawnable sibling; keep shim
  handling THERE, never at a call site, or the paths that don't go through it (the turn-free probe/update
  seams did) silently regress on shimmed installs.
- The retry in `CompleteJsonAsync` must **differ** from the first attempt (feed back the bad reply + a <!-- trap: sub=router shape=vacuous -->
  corrective instruction) — re-sending the identical request to a temperature-0 model just repeats it.

## Provider lifetime, cooldown & admission (`Lyntai.Inference`; details in `docs/DECISIONS.md` D30)

Every one of these is a *silent* failure: the build is green, the tests are green, and the damage is a
benched tenant, an unbounded engine or a render nobody cancelled.

- **Hoisting a member into a new BASE interface, and deleting it from the derived one, breaks every <!-- trap: sub=build,gates shape=scope-blind,wrong-subject -->
  pre-compiled caller.** Adding a base interface is binary-safe; removing the member from the interface that
  used to declare it is not, and the refactor that does both reads as pure cleanup. A caller compiled against
  the old surface emits `callvirt IModelProvider::get_Id`, and member resolution **does not walk base
  interfaces**, so `provider.Id` throws `MissingMethodException` until that assembly is recompiled — which
  upgrading a package does not do. Nothing here catches it: `check-warnings` is silent, the baseline shows a
  line moving, and **`consumer-smoke` rebuilds its consumer from source**. To test such a claim, compile a
  probe against the OLD assembly and run it against the new one. `IModelProvider` therefore keeps its own
  `new string Id { get; }` beside `IProviderIdentity`, pinned by a test; an implementor-only check proves
  nothing about callers. **The next two places:** `IScorer` and `ICliBackend` each declare `string Id { get; }`
  with `IProviderIdentity`'s shape and look like leftovers to hoist — if either ever gains the base, it keeps
  its own `new` declaration and a line in `ProviderIdentityTests`.
- **Disposing a replaced instance aborts in-flight work.** Retiring an entry looks like it should clean up <!-- trap: sub=lifetime shape=silent-loss -->
  after itself, and "clean up" reads as `Dispose`. It isn't: retirement removes the entry and drops the
  pool's reference, and the runtime reclaims the instance once the last caller finishes. **Without leases a
  pool cannot know when that is**, so disposing on retirement means disposing while callers may still be
  running — and with configuration arriving from a source that changes at any moment, a routine poll then
  kills a healthy render that had minutes to go. `IHttpClientFactory` is the model: an expired handler leaves
  the lookup so new callers get the fresh one, while existing users keep the old one until they're done.
  **Expiry is not disposal.** Pinned by a disposable fake whose flag must stay false through `Retire`,
  `RetireSlot`, LRU eviction and idle eviction.
- **Cooldown keyed on the provider id benches other tenants.** The key was `generation::{providerId}`. With <!-- trap: sub=lifetime shape=wrong-subject -->
  two configurations of one backend live under different credentials, the one that exhausts its quota benches
  the other, whose key was fine — while two consumers of the same downed self-hosted host *should* share a
  bench and don't. Key on the `ProviderKey` (`configuration` delegate → `pool.TryGetKey`) and both are right;
  key on the id and exactly one is wrong, only in production, and only under multi-tenancy no test simulates.
- **A per-instance concurrency semaphore admits everyone under a no-reuse strategy.** A `SemaphoreSlim` field <!-- trap: sub=lifetime shape=fail-open -->
  on a provider (or on a decorator over one) bounds anything only while the instance is shared. Register
  `TransientProviderPool` and every call builds its own limiter with its own fresh permits, so the local
  engine the limit exists to protect thrashes exactly as it would with no limit configured. Keyed and shared
  (`ProviderAdmission`) is the only shape that survives both strategies.
- **A key derived from the options OBJECT is wrong for every generation backend, and silently.** Every <!-- trap: sub=lifetime,di shape=wrong-subject -->
  generation options type is a mutable `sealed class` compared by REFERENCE, and `ComfyUiOptions.Produces` /
  `FalOptions.Produces` are lists built per instance, so two identical configurations never compare
  equal. Name every contribution (`ProviderKey.For(id).With("baseUrl", …).WithSecret("apiKey", …)`) so a
  forgotten member is visible in review, and fold in values the backend resolves at RUNTIME —
  `ProviderKeyBuilder`'s own doc says why.
- **Decorating a provider erases its optional capability interfaces.** The generation seam expresses <!-- trap: sub=lifetime,generation shape=silent-loss -->
  long-running and streaming delivery as *additional* interfaces the router type-tests:
  `if (provider is not IMediaJobProvider job) continue;` (`MediaRouter.SubmitAsync`). Any wrapper
  implementing only `IModelProvider` makes a queue backend invisible, so **every video render stops
  routing while every image render keeps working and every inline-only test stays green.** This is why
  admission is applied by the router rather than by a decorator — and the trap applies to *any* future
  wrapper (telemetry, retries, redaction), not just this one. If you must wrap, forward every optional
  interface the wrapped instance implements, and prove it with a test that submits a job.
- **A `using` that is one frame shallower than you think.** `MediaRouter.GenerateAsync`'s `Surface` arm <!-- trap: sub=lifetime shape=resource,second-door -->
  `return`s from inside the fallback switch, which is safe **only** because the admission permit is held in
  `AttemptAsync`, one frame deeper. Inlining `AttemptAsync` in the name of simplification turns that return
  into a permanent gate leak. More generally: every `ProviderAdmission.EnterAsync` result must reach a
  `using` on **every** path — fallback, refusal, cancellation — because a caller that abandons the `ValueTask`
  without disposing the handle pins its gate forever.

## Storage (details in `storage.md`)

- **A single-threaded benchmark cannot see a PROCESS-GLOBAL ceiling, and SQLite ships one that is ON by <!-- trap: sub=storage,measurement shape=resource,wrong-subject -->
  default.** SQLite's memory-allocation statistics take a process-global mutex on every allocation, so
  concurrent readers serialise on a counter unrelated to the database (**D107**; `docs/memory-measurements.md`
  §5, `scale-sqlite-statistics-off`) — while every test here passed, because every one is single-threaded.
  **The diagnostic lesson is worth more than the setting**: six plausible causes were refuted first, and what
  located it was SCOPE — a CPU-over-wall column (threads burning cores are not waiting on a lock) and an
  isolation ladder ending in separate PROCESSES. **When per-object and per-file isolation change nothing and a
  second process fixes it, stop looking for shared state in your own code**: something in the process is
  global, and a native dependency's configuration is invisible to every grep. A flattering 29× still needed
  repeats before it was believed.
- **`catch (OperationCanceledException) { throw; }` makes a fail-open seam fail CLOSED the moment the work <!-- trap: sub=memory shape=cancellation -->
  it wraps is an HTTP call.** An `HttpClient` timeout surfaces as `TaskCanceledException`, which IS an
  `OperationCanceledException`, so a bare rethrow cannot tell "the caller cancelled" from "my own request
  timed out" — a judge call exceeding its timeout took down a recall and 40 minutes of ingestion
  (`docs/FIXES.md`, 2026-09-09). **The fix is a filter, not a broader catch**: `when (ct.IsCancellationRequested)`,
  with BOTH halves pinned (`MemoryVerificationTimeoutTests`). **Whenever a fail-open catch wraps work that can
  be a network call, ask which exception the timeout actually throws.** Census on 2026-09-09:
  `Lyntai.Core/Memory` held 21 such sites; every one is now
  guarded and **0** bare. The idiom had been in `SemanticMemory`'s embed catch since 2026-07-18 — **look for
  your own prior art before concluding a defect is novel**; it turns "add a guard" into "make the rule uniform".
  The storage adapters were outside that census, and six of their fail-open catches still rethrew by TYPE
  until 2026-09-25 (`StoreFailOpenCancellationTests`).
- **Fail-open handlers nest, so the promise is only as good as the WEAKEST link in the chain — and testing <!-- trap: sub=memory shape=fail-open,cancellation -->
  a link in isolation cannot see that.** One timeout from a BYO embedder passes through the seed source, the
  engine's gather, the composite and then the walk: four handlers, each documented fail-open, each looking
  correct, and a bare rethrow at ANY of them breaks all of them (16 sites at once, 2026-09-09). **Test a
  multi-level fail-open promise END TO END, from the deepest fault to the outermost caller**, and distrust
  "each site needs its own answer" when every site carries the same promise.
- **When a CONTRACT states the false premise, fixing the code alone ships a doc that contradicts it — and <!-- trap: sub=docs,memory shape=stale-claim -->
  no gate can see that.** `IMemoryEngine`'s recall doc said *"Only `OperationCanceledException` propagates,
  because cancellation belongs to the caller"*, and `IMemorySeedSource`'s said cancellation "is always
  propagated" beside "must not throw for a transient fault" — which an `HttpClient` timeout is BOTH of. **A
  contract that justifies itself is where a wrong premise hides best**: grep the seam docs for the RULE, and
  phrase a promise as a TEST the reader can apply (`ct.IsCancellationRequested`) rather than a type.
- **A caller-cancel test written as `ThrowsAnyAsync` on a PRE-cancelled token usually cannot fail, so the <!-- trap: sub=tests shape=vacuous,cancellation -->
  control that was supposed to stop a bad fix certifies it instead.** The twin of "the seam's own timeout
  degrades" is "a real cancel still propagates", and on a pre-cancelled token something else satisfies it:
  `RecallAsync` checks the token before reaching the verifier, and on the write path a store call outside
  every `try` threw its own cancellation — both twins passed under the wrong fix (`docs/FIXES.md`,
  2026-09-09). **MARK the exception the seam throws and assert on the marker, cancel MID-CALL from inside the
  policy, then apply the wrong fix and watch the twin go red.** A control nobody has seen fail is not one.
- **A seam that hands a model a TRUNCATION measures the truncation, and the model takes the blame.** <!-- trap: sub=memory,measurement shape=wrong-subject,silent-loss -->
  `IMemoryVerificationPolicy` once received only a candidate's 120-character `Headline`; on LoCoMo a
  purpose-built cross-encoder then read as a refutation of the design, until the same model with headlines
  long enough to hold the turn gained 13 points (`docs/memory-measurements.md` §5; `Content` is now passed,
  **D108**). **The tell was a CLEAN audit** — the model discriminated almost perfectly and still lost, which is
  the signature of the wrong input rather than a weak model. **Before concluding a model class does not
  transfer, check what the seam passed it**, and prefer a control that varies the INPUT over the model.
- **An admission guarantee must survive the LIMIT, not just the WHERE.** "Admitted unconditionally" that is <!-- trap: sub=storage,memory shape=ordering -->
  implemented only as a predicate is still excluded by `ORDER BY … LIMIT` whenever the ordering key is the
  axis the protected row is weakest on — a long-quiet exact fact sorted last by recency and was cut before
  ranking saw it, on every backend (`docs/FIXES.md`, 2026-08-09). Grade now leads the seed ordering on the
  paths that have one — the in-process store, Postgres, and two of SQLite's three branches. **SQLite's
  FTS/bm25 branch has no grade term by design**; an authoritative fact is fetched separately and merged, so a
  fixture on that path alone passes a rule it never exercises. Read each backend's actual query, and never
  generalise an ordering from one branch or a sibling backend.
- **A bound configured on one SCOPE and enforced on another is not a bound — and the tests will only ever <!-- trap: sub=memory,tests shape=vacuous -->
  exercise the regime where the gap is invisible.** `GraphMemoryOptions.AuthoritativeReserve` (per ENGINE)
  was capped by `MemoryQuery.Limit` (per QUERY) only when null, so reserve `5` with `Limit: 2` returned three
  items and no ordinary hit (`docs/FIXES.md`, 2026-08-14). Both tests used a reserve BELOW the limit — the one
  regime where the missing cap is unobservable. **Whenever two limits come from different scopes, write the
  fact that the narrower still binds, and pick fixture values from the regime where they DISAGREE.**
- **The SQL traps that corrupt silently, each stated in `storage.md`**: a missing FTS `'delete'` trigger row <!-- trap: sub=storage shape=silent-loss,ordering -->
  on delete/update (three triggers, always); a double read without `CAST(x AS REAL)` (integer affinity); a
  connection opened outside the factory (it loses `foreign_keys=ON`, so cascades stop); a reused migration
  number (silently skipped — use `dev.mjs new-migration`); an `ORDER BY` on a non-unique column with no
  tiebreaker (nondeterministic on ties).
- **Windows refuses a replace-rename while another process holds the target, and a burst of rewrites to one <!-- trap: sub=storage shape=resource,ordering -->
  file provokes it reliably.** An indexer, or a scan of the file just written, is enough: the file store's
  write-to-temp-then-move threw `UnauthorizedAccessException` / `IOException` on every run of a racing
  contract fact, while every sequential test passed (`docs/FIXES.md`, 2026-09-25). **An atomic replace on
  Windows retries a refusal briefly and boundedly**, then reports it; a single attempt is right only for a
  file nothing else is watching.
- **A `Relevance` normalized by RANK POSITION, not by score margin, makes a bounded rank boost's effect <!-- trap: sub=memory,tests shape=wrong-subject,vacuous -->
  CANDIDATE-COUNT DEPENDENT.** The stores report `MemoryRelevance.ByRankPosition` (`1 - i / count`): with two
  candidates that is exactly 1.0 and 0.5, a fixed 2× gap however close the underlying `bm25` scores are; with
  ten the same 2nd-place gap is 10%. A logarithmic salience rank boost defaulting ON could not clear the
  2-candidate gap at any real salience — part of why ranking by salience defaults OFF (**D45**). **A test (or
  a consumer's expectation) for any rank-lifting signal needs a result set large enough that the position gap
  it fights is smaller than the boost it proves** — two candidates is the WORST case, not a representative
  one (`docs/task-archive.md` Part 53).
- **One stored value read at N sites grows N coercion rules, and a helper whose doc says "EVERY read site <!-- trap: sub=memory,storage shape=second-door,silent-loss -->
  calls this" is a claim nothing checks.** `salience` was read at FOUR sites with THREE rules (2026-08-09), so
  `{salience: 0.5}` admitted differently per backend and a `NaN` crashed one write, sorted to the top on
  another and emptied the recall in the engine. **Put the coercion in ONE public function on the value's own
  type and make every reader call it** (`MemorySignals.Salience`) — and a FOURTH reader added later
  (`SalienceRetentionPolicy`, 2026-08-17) spelled the read out itself and got `NaN` wrong. **Whenever you
  extract a coercion, grep for the raw accessor it wraps** (here `Get(WellKnown.Salience`) and route every hit
  through the helper; two correct copies is how three incorrect ones started.
- **A clamp is not a finiteness guard — `Math.Max`/`Math.Min`/`Math.Clamp` PROPAGATE `NaN` (IEEE 754).** <!-- trap: sub=memory,storage shape=silent-loss -->
  It landed FOUR times in one subsystem (`docs/FIXES.md`, 2026-08-14 and 2026-08-17), the last two with this
  rule already written: `DsrRetrievability.Reinforce` had the guard and a second reader of the same value
  re-derived finiteness and concluded the opposite; then `GraphMemoryEngine` persisted `Math.Max(0, tick…)`
  from the public `IMemoryAgePolicy.Advance`, which a long contract paragraph about `Age` read as though it
  covered. Write `double.IsFinite(x) ? Math.Max(0, x) : fallback` wherever the input is derived arithmetic; a
  persisted `NaN` compares false against every threshold, so the entry neither ranks, prunes nor reports.
  **A guard belongs to the VALUE, not the call site** — push it to where the value is produced, or make one
  public coercion every reader calls — and **ask what a seam RETURNS and whether any of it is stored**: a
  contract paragraph about one member does not cover its siblings. Route implementers here in the dispatch.
- **A ceiling written as a bare `Math.Min(grown, max)` is a CUT, not a cap, for anything already above it — <!-- trap: sub=memory,tests shape=silent-loss,vacuous -->
  and the value it cuts is usually persisted.** `DsrRetrievability.Reinforce` shipped that shape, so recalling
  an entry stored above `MaxStability` wrote the ceiling back — a 50× shortening against its own "never
  smaller" guarantee — while `EffectiveStability`, one method away, had the right shape:
  `Math.Max(current, Math.Min(current * factor, max))`. **Ask what a bound means for an input already past
  it** ("cannot grow beyond X" is not "is never above X"), and **give a monotonicity guarantee a fixture that
  can violate it** — the contract fact had only ever run far below the ceiling (`docs/task-archive.md` Part 54).
- **A record where ONE field is domain-guarded and its neighbours are not is more dangerous than one with no <!-- trap: sub=di,memory shape=scope-blind -->
  guards at all** — the guard reads as evidence the record was audited. `DsrOptions.Decay` got a validating
  `init` in 2026-08-09's Task 1 (after a review found `Decay = 0` made everything permanently unforgettable
  while pruning still deleted); `InitialStability`, declared three lines away and equally load-bearing, kept
  none, and `InitialStability = 0` then reached `Math.Pow(0, -0.4)` = `+∞` → `0 × (1+∞)` = `NaN`. When a
  review forces a guard onto one option, **audit every other field of that record in the same fix**, and
  prefer a test that walks the whole options surface over one that pins the field just fixed.
- **A validation that enumerates its subjects BY NAME goes wrong the moment the set grows, in either <!-- trap: sub=di,memory shape=scope-blind -->
  direction.** RRF's "at least one signal above zero" check listed four weights by name, so adding a fifth
  made it REFUSE a coherent diagnosticity-only configuration (**D62**); the mirror — a weight added to the
  score and not to the guard — admits a configuration whose score is identically zero, so ordering falls to
  the id tiebreak. Check the same list the score sums, never a second copy of it.
- **A test whose recall silently returns nothing exercises only the write path — and stays green.** Hit <!-- trap: sub=tests,memory shape=vacuous -->
  twice reaching for `InMemoryMemoryGraphStore` "for speed" (2026-08-09, 2026-08-10): a recall-quality guard
  measured a ~93% miss rate, and an identity test instrumented at 25 writes, 69 queries and **0** touches
  stayed green with the store's touch stamping deleted. The cause — only SQLite's FTS path split a query into
  terms — was fixed by `SearchTerms` (**D55**); prefer SQLite anyway when the subject is RANKING, which the
  in-process store has none of. **The lesson outlived the cause: mutate the behaviour the test claims to
  cover and watch it stay green.** Its guard `Assert.True(comparisons > ids.Count)` was met by writes alone —
  **a guard that cannot observe the thing it guards reads as coverage** — and a divergence filed as *by
  design* had been failing recall on two backends for a year, defended by a test that asserted it.
- **`GraphMemoryOptions.EdgeHalfLife` and `DsrOptions.EdgeHalfLife` share a name and a default of 100 and <!-- trap: sub=di,memory shape=wrong-subject -->
  govern different things** — an edge's weight during traversal, and connection strength inside the curve.
  Name which one; the second's XML doc says how.
- **A PERMANENT change driven by the system's own retrieval decisions is the dangerous shape — not merely an <!-- trap: sub=memory,measurement shape=wrong-subject -->
  unbounded one.** Five studies on 2026-08-12: salience (keyed on write-time content) HELPS, the age reset a
  recall performs HELPS, and stability growth HURTS in every form tested — compounding, capped, and computed
  from the recall COUNT so it could not compound (**D54**). Two framings died on the way: "reinforcement is
  harmful" (the age reset helps) and "bounded is safe" (the non-compounding form still lost). **What fits**:
  the age reset EXPIRES while growth PERSISTS, so permanent × retrieval-conditioned banks the ranker's own
  error. **Ask of any such mechanism whether its effect expires, and if not, whether it is driven by
  something other than the system's own output** — offered as the surviving hypothesis, not a law.
- **A `DsrOptions` sweep on the field benches reads FLAT by construction — the constants are invisible there, <!-- trap: sub=memory,measurement shape=vacuous -->
  not inert.** Under the shipped `ReciprocalRankFusionPolicy` retrievability votes by RANK, and
  `(1 + f·age/S)^Decay` orders entries by `age/S` whatever `Decay` is — so no curve constant reorders a recall
  directly. They act only through reinforcement history, and `memory-locomo`/`memory-longmemeval` answer every
  question on a fresh clone of the ingested store. Found 2026-09-23 by arithmetic, before FSRS-B spent a run
  on it (`docs/task-archive.md` Part 281). **Before sweeping any curve constant, ask whether the workload recalls the same
  store more than once.**
- **A feedback loop conditioned on the system's OWN output is not learning, and it reads like learning at <!-- trap: sub=memory shape=wrong-subject,unmeasured -->
  every call site.** Measured 2026-08-12 across three studies (`docs/task-archive.md` Part 64,
  `docs/DECISIONS.md` D53):
  `GraphMemoryEngine` reinforces every node `RecallAsync` returned, which sounds like the documented promise
  ("material you keep coming back to becomes durable") and is not it. What the ranker RETURNED is the
  ranker's own opinion; reinforcing it compounds that opinion rather than the entry's usefulness, so recall
  quality gets measurably WORSE the more reinforcement happens — monotonically, concentrated in exactly the
  class where the same entries recur. **The tell to generalise: when you import "use strengthens X" from a
  domain, check whether that domain's "use" was VERIFIED.** FSRS's learner knows whether they recalled
  correctly; a query-driven engine does not, so the same rule becomes positive feedback on its own mistakes.
  The fix is not to weaken the loop but to condition it on an act that carries evidence — here `ExpandAsync`,
  a caller choosing to pay for full content, which this engine already produces and then weights identically
  to a guess.
- **An English, space-separated test corpus measures the FRIENDLIEST tokenization a trigram-FTS store <!-- trap: sub=measurement,memory shape=wrong-subject,scope-blind -->
  supports, and every number it produces is a best case.** The same cluster-recall question scored ~0.47 miss
  with a cue whose only live token reaches the target and ~0.82 with ordinary overlapping words (2026-08-12)
  — a gap larger than any policy effect that day. The overlapping cue was first dismissed as a stopword
  "mistake"; under trigram matching almost any two texts share trigrams, and **for CJK there is no stopword to
  strip**, so the "contaminated" case was the representative one. **Ask which tokenizer the store uses and
  which languages it claims before calling incidental matching contamination.**
- **A best-effort catch turns a BUG into "nothing matched", and you will debug the wrong layer for hours.** <!-- trap: sub=memory shape=fail-open -->
  `GraphMemoryEngine.RecallAsync` wraps `GatherAsync` in a catch that logs and returns `MemoryRecall.Empty` —
  a deliberate promise — so any defect in the gather path looks like a query that matched nothing, and with
  no logger attached there is no signal at all (two implementations were debugged blind against a feature
  that was correct, 2026-08-13). **Attach a logger and assert it stayed silent** (`SemanticSeedProbeTests`),
  and wherever a catch converts an exception into a plausible empty value, **write first the test that can
  tell the two apart.**
- **One best-effort `try` around several steps turns the FIRST failure into the loss of all of them — and <!-- trap: sub=memory shape=fail-open,silent-loss -->
  its log line names the lesser loss.** Found twice in one method pair on 2026-09-24 (`docs/FIXES.md`,
  **D175**): the graph engine's link loop sat before the vector upsert in one `try`, and its embed shared one
  with the neighbour search, so a failed link or a failed search each cost the vector. Neither step needed
  the other, so nothing justified one block — it simply read as tidy, and "best-effort" made every failure
  look equally survivable. **Wrap each step whose failure should cost only itself in its own block, the
  valuable one first**, and pin each with a fact that makes THAT step fail and asserts the next still ran.
- **The controls a run checks must be read out of the MODE it runs, not recalled from the most-quoted <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  sentence about the benchmark.** Measured 2026-09-02: a design record named **31.4% / 10.0%** as the
  knowledge-update controls for `memory-longmemeval`, confidently and in a table headed "nothing else is
  readable if these move". Those are the `--shots` mode's `clean` column at shot 1 — a different metric in a
  different mode. They are also the pair `CLAUDE.md` quotes, which is exactly how they got there: the most
  memorable number about a benchmark is rarely the one your run produces. The default mode's controls are
  `prefers current` 96.9%/47.1% (oracle) and 86.4%/46.4% (haystack).
  <br>**The tell is that a control you cannot point at a printed table for is not a control**, it is a
  memory. Open the mode's own published table and copy the row. This was caught by reading before the run
  rather than by the run — a wrong control does not fail, it silently certifies.

- **A benchmark class whose STORE is smaller than the PAGE cannot measure retrieval at all, and it looks <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  like a perfect score rather than a broken instrument.** On LongMemEval's oracle variant 63% of
  `single-session-assistant` questions have a store that fits inside `k = 10`, so the first recall returns
  the whole conversation and any shot curve is flat by construction (2026-09-04; `docs/task-archive.md` Part
  112 met it in a milder form). **Before scoring a new class, divide its store size by `k`.** In the same pass,
  drop the ZERO-EVIDENCE questions, or the denominator silently lies — a new class needs that guard rather
  than inheriting it by luck.

- **A sample size can hide a CRASH, not only a wrong number — so run the instrument once at the size you <!-- trap: sub=measurement shape=silent-loss -->
  intend to draw conclusions at.** `memory-locomo --retrieval`'s oracle arm could not be CONSTRUCTED at
  `--n 1540`: it keyed evidence by question text, LoCoMo repeats 11 QA rows verbatim, and `ToDictionary`
  threw — for an arm whose ceiling three records quoted (`docs/FIXES.md`, 2026-09-02). **Its silent twin is
  the part to remember**: the QA path made the same assumption with `dict[key] = value`, which OVERWRITES.
  When a key turns out not to be unique, audit every other use of it. And LoCoMo's `dia_id` is
  conversation-scoped — 871 of 1,033 occur in more than one conversation — so a globally keyed evidence
  index endorses the wrong turn.

- **A measurement that cannot observe a change reports "nothing moved", which reads exactly like "no <!-- trap: sub=measurement,memory shape=vacuous,wrong-subject -->
  regression".** Reconciling `GraphNode.Relevance` across backends (2026-08-12), the fixed-corpus pin and a
  28-minute sweep came back bit-identical — and `MemoryCorpus` held **zero** graded entries, so the changed
  behaviour was unreachable on that instrument. **Before citing an unchanged measurement as reassurance, ask
  whether that instrument can express the thing you changed**; the right claim was "not exercised".
  <br>**A documented blind spot is still blind**: teaching the corpus to express the promise
  (`CorpusShape.AuthoritativeCount`) took an afternoon and immediately found objective (1) broken in all five
  languages (**D56**). If the instrument cannot express the promise, fix the instrument.
  <br>**Its mirror: a knob that scales a CONSTANT is unmeasurable.** A `SalienceWeight` sweep would have
  returned a perfectly flat curve, because without an embedder `StructuralSaliencePolicy` declines on every
  write and RRF ranks by competition (**D82**), so a uniformly-tied signal cannot move the ordering at any
  weight. **Ask not "does each arm carry a different knob value?" but "does the SIGNAL the knob scales vary on
  this corpus?"** — and assert it in the study as a distinct-value count.
- **A COUNT is not exhaustiveness.** A reflected count of a contract's facts compared against a hand-bumped <!-- trap: sub=tests,storage shape=scope-blind -->
  literal passes whenever the author bumps it — including for a fact wired to one backend alone.
  `MemoryGraphStoreCoverageTests` made coverage structural; carry that shape to any new cross-backend contract.
- **A cross-backend invariant enforced on ONE backend's test class is not enforced.** The non-finite-salience <!-- trap: sub=tests,storage shape=scope-blind -->
  guard was pinned only in `SqliteMemoryGraphStoreTests`, so the in-process store's own divergence survived a
  full review. A fact about the CONTRACT belongs in `MemoryGraphStoreContract`, wired to every backend
  (`InMemory`, `Sqlite`, `Postgres`, `FileSystem`) — coverage is reflection-fed, so there is no count to bump.
  Keep a backend-specific assertion only where it cannot be portable, such as reading a raw column.

- **An id that is unique WITHIN one engine is not unique across a composite, and keying on it alone works <!-- trap: sub=memory,measurement shape=silent-loss -->
  right up until there are two members.** Measured 2026-08-30 merging the two field harnesses onto
  `MemoryWalk` (`docs/task-archive.md` Part 120): both keyed accumulated results on `MemoryRef.Id`, and both
  got away with it only because each ran a single engine — on a composite, two members may each own id
  `"1"`, so one silently overwrites the other. **Identity is the whole `MemoryRef`.**
  <br>**The tell is that the code is correct for the configuration you test and wrong for one you ship**, so
  no test fails and no review catches it; `CompositeMemoryEngine` is what makes it reachable. Wherever
  results from more than one member are pooled — a dictionary, a `HashSet`, a dedup — check what the key
  actually identifies.

- **An unconstrained generic `T?` is NOT `Nullable<T>` for a value type, so a reader returning `T?` for an <!-- trap: sub=storage shape=silent-loss -->
  ABSENT field hands back `0` or `false`, not null — and a `?? throw` guarding a required field never fires.**
  Caught 2026-09-23 writing the file-system backend's header reader before any test ran: `Long("id")` on a
  record with no `id` would have loaded it as id 0 instead of skipping it. The fix is to make `T` itself the
  nullable type at every call site (`Read<long?>`) so `default` is null; `FileSystemFormatTests` pins an
  absent field reading back null. The compiler says nothing either way.

- **"A file that does not parse is never written over" held on the NUMBERED path and broke on every path whose <!-- trap: sub=storage shape=silent-loss,second-door -->
  name derives from a KEY.** Found 2026-09-23 by the pre-release review of the file-system backend:
  `FileSystemRoot.MaxId` kept a broken `000007.md`'s number taken, while a prompt's `v0001.md`, a thread's
  `thread.md` and a key's own file were each rewritten by the next save — the person's edit gone, and a
  recreated thread adopting the old one's events. The same pass found the curated store writing to
  `IdFile(id)` rather than the file it loaded, so a hand-renamed record was duplicated on update and came back
  after a remove. **Every write path is a door; enumerate them by how the NAME is chosen, not by domain** —
  **D171** states the rule, and `FileSystemRestartTests` pins each door. The graph store will have more.

- **A line format that picks a record's KIND by testing for a PROPERTY NAME misparses as soon as another kind <!-- trap: sub=storage shape=silent-loss,ordering -->
  carries that name as a DATA field.** Found 2026-09-23 building the file graph store's journal: a review
  line carries `"node"` — the id of the node it reviewed — so a `node`-first dispatch read every review as a
  node state. `GraphLines.Parse` now tests first the kinds whose fields cannot collide, and
  `GraphJournalTests` pins every kind's round trip. **A new line kind re-asks the question**: check its
  discriminator against every other kind's DATA fields, not only against their discriminators.

- **`Stability` has ONE meaning here and adopting the published convention would silently reinterpret every <!-- trap: sub=memory shape=silent-loss -->
  stored value.** It is the position delta at which retrievability is `0.5`; FSRS anchors at 90%. Nothing
  about the type says which, both are defensible, and a change would rewrite the meaning of every row
  already in every consumer's database without failing anything. The convention is pinned by an assertion
  rather than by prose — `DsrRetrievabilityTests` requires retrievability at age 20 / stability 20 to be
  `0.5` — which is what makes the reinterpretation unshippable. **When a stored number's UNIT is a
  convention, pin it with a test, not a comment.**
- **A context record that names what the ENGINE measured reads as the whole input a policy gets.** <!-- trap: sub=memory,di shape=wrong-subject -->
  `SalienceContext` carries the engine name, novelty, comparables and `SimilarCount`, so reading that record
  alone says a policy can judge nothing else — and a sweep was designed around adding surface before anyone
  read the method signature, which already passes the whole `MemoryWrite`. **The seam is the signature, not
  the context type**; check what a policy is HANDED before concluding it needs more.

## DI / config

- **Calling `AddLyntai` twice** — registers a second `LyntaiOptions` (shadows the first) while both <!-- trap: sub=di shape=silent-loss -->
  calls' providers pile into the collections. It now throws; compose everything in one callback.
- **A singleton capturing a scoped/transient dependency** (captive dependency). Providers/stores are <!-- trap: sub=di shape=resource -->
  singletons; resolve per-call transients (like an `HttpClient` from `IHttpClientFactory`) inside the
  method, not the constructor.
- **A documented option/env-var that isn't wired** — the `LYNTAI_MODEL_<CONSUMER>` override and the OTel <!-- trap: sub=di shape=stale-claim,silent-loss -->
  cost attribute were both documented but silently dropped, and no test caught it. When you add a
  documented knob, add the test that exercises the documented path.
- **TWO construction sites for one type, each with its own copy of a long optional-argument list, is how a <!-- trap: sub=di,memory shape=second-door,fail-open -->
  documented knob stops being wired — and the compiler is structurally unable to notice.**
  `MemoryEngineBuilder` built `GraphMemoryEngine` in both `UseGraph` and `UseBestAvailable` (what `AddMemory()`
  resolves to), and `annotation:`/`verification:` were added to only the first, so
  `AddMemory().AddMemoryVerification()` registered a policy that never ran (`docs/FIXES.md`, 2026-08-14).
  Optional parameters compile when omitted, the fallback is a real behaviour, and the only symptom is
  quality. **The fix is one call site, never a second copy kept in step by review**, and the test asserts the
  policy was CONSULTED (a recording fake), not that recall still worked. The engine now takes one
  `GraphMemorySeams` record, which the one site fills.
- **A service registered FIRST-wins that resolves its dependency from the container at run time gets the <!-- trap: sub=di,storage shape=ordering,silent-loss -->
  LAST registration of that dependency — another wiring's.** Every SQL store was `TryAdd`-ed, so an app's own
  store wins, but built over the `IDbConnectionFactory` it resolved when first used, and every wiring
  registered its factory with a plain `AddSingleton`. Two `Use*Storage` calls split by feature therefore ran
  the first wiring's stores over the second's database — memory writes in the wrong file, or SQLite SQL over an
  Npgsql connection — and recall failed open to nothing (`docs/FIXES.md`, 2026-09-25). **Build a wiring's
  services over what THAT wiring holds**, captured at registration, and write a test with two wirings in one
  container: every single-wiring test is blind to this.
- **A `TryAddSingleton` reached during `configure(builder)` BEATS `AddLyntai`'s own options-built <!-- trap: sub=di shape=ordering,silent-loss -->
  registration.** `AddLyntai` invokes `configure(builder)` before `RegisterTextFrontDoor`, where the
  `DeadHostTracker` built from `LyntaiOptions` is registered, so a `TryAddSingleton<DeadHostTracker>()` inside
  a `Use*`/`Add*` extension wins and silently swaps the configured threshold, cooldown and logger for
  defaults, in both domains — found by mutation, with 1,427 tests green. **Inside a builder callback, resolve
  what `AddLyntai` registers later with `GetRequiredService<T>()`, never seed it with a `TryAdd`**;
  `RegisterProviderLifetime` is all `TryAdd` for the mirror reason — everything it seeds is meant to lose.

- **A seam whose EMPTY registration means "take the default" has no off switch, and an arm built to turn <!-- trap: sub=measurement,memory shape=vacuous,fail-open -->
  it off runs the shipped behaviour under an OFF label.** `GraphMemoryEngine` substitutes the shipped
  `StructuralSaliencePolicy` for a null or empty collection, so `memory-salience`'s `SalienceOff` arm ran
  salience at the shipped weight for its whole life (`docs/FIXES.md`, 2026-08-30). **The off arm must be an
  explicit neutral IMPLEMENTATION (`NeutralSaliencePolicy`), and its control must assert it was CONSULTED and
  DECLINED** — "the signal is absent" is also what a never-registered policy reports. A control arm that
  differs from a treatment by more than the treatments differ from each other is reporting a confound.

## Copying a rule copies its assumptions

- **A rule moved from where it was true to where it is not looks like careful reuse, and carries a premise <!-- trap: sub=generation,memory shape=unmeasured,wrong-subject -->
  that held only where it came from.** The instances (`docs/FIXES.md`, 2026-08-15 and 2026-08-17):
  · `ComfyUiProvider`'s *"a 4xx is terminal"* copied onto the fal provider, where one `429` dead-lettered a
    running, billed render — the premise "this backend does not rate-limit" was never written down;
  · `SeedAsync`'s portable guarantee read as a CEILING when it is a FLOOR — a minimum and a maximum are the
    same sentence in English until you ask what it FORBIDS;
  · the composite's fan-out justification applied to members that could remove, and not to those that could not;
  · a "never reached the backend" distinction taught in one file, and a catch in another treating every throw
    as ambiguous;
  · the submit door's `NeverReachedTheBackend` filter carried onto `MediaRouter.StreamAsync`, where its
    premise ("asking may have been billed") is false;
  · a summary function tallying MISS alone beside a detail view corrected hours earlier to report pollution —
    and on its "5/5" a shipped default was changed, then reverted.
  <br>**What to do**: when you reuse a rule, write down the premise that makes it true THERE and check it
  holds HERE. When a rule you just wrote is about a distinction, grep your own diff for the other places it
  applies — the round that articulates a rule is the round most likely to violate it elsewhere. A SUMMARY is
  a second site, and the one people act on; "N/N better" is a count on one metric wearing a verdict's costume.

- **A work item named after its most EXPENSIVE instance hides the cheapest one, and every later reader <!-- trap: sub=docs,generation shape=scope-blind,stale-claim -->
  inherits that framing — including the reader who SPLITS it.** GEN-VERIFY covered three unmeasured backends
  at very different costs — ComfyUI a local server, `sd-cli` two downloads, fal an ACCOUNT — and its summary
  named only two; the item then took the state of its most expensive member, and a split along the axis the
  NAME suggested reproduced the omission in two items (2026-09-16). **Sort a multi-part item by what each part
  COSTS before splitting it.** The tell is a summary enumerating fewer members than the code does.
- **ADDING a second way to configure something that already has one is where double-application comes from <!-- trap: sub=di,memory shape=second-door,silent-loss -->
  — not from decorators being decorators.** `GraphMemoryEngine` gained `retentionPolicies` while retention
  still arrived pre-wrapped in a hand-built `ModulatedRetrievability`; supplying BOTH applied retention twice
  and broke `CandidateCutoff`'s superset guarantee, whose only consumer DELETES (2026-08-31). Making the type
  internal was proposed and REJECTED — hiding a type to remove an ambiguity would have hidden the data-loss
  path with it. A sweep found every pre-existing decorator had exactly ONE application path, so the rule is:
  **when you add a configuration route, ask what the existing route was and whether both can be supplied at
  once** — and report the collision at wiring time (**D85**) rather than picking one silently.

- **A doc that enumerates what a feature does NOT do, without ever stating what it DOES, is read as <!-- trap: sub=docs,memory shape=scope-blind,vacuous -->
  "nothing" — and the reader who reaches that conclusion turns the feature off.** `IMemoryVerificationPolicy`'s
  summary said a verifier "only narrows what a recall already found … and by default removes none", every
  clause true, and never said a verdict PROMOTES every endorsed candidate before the limit; an adopter built
  and reverted an app-side promotion on that reading, and their correction invented a plausible wrong
  mechanism (2026-08-22/23). **A mechanism invented to explain a real observation is harder to dislodge than
  the error, because it arrives labelled as a correction.** Say what a default DOES first and what it
  withholds second, and **write the fixture that tells two readings apart before writing the paragraph**
  (`MemoryVerificationOrderingTests`, whose null-result control is the adopter's own observation).

## Second doors

- **A rule a router applies to a THROW is dead code when every shipped backend catches its own exceptions <!-- trap: sub=generation,router shape=second-door,vacuous -->
  first.** `MediaRouter` marks a thrown submit `Inconclusive` unless it provably never left the process, but fal
  and ComfyUI caught every submit exception themselves and returned a conclusive `Failed` — so a possibly
  delivered submit was re-bought from the next backend (`docs/FIXES.md`, 2026-09-25; **D64**). The router's
  tests threw from a fake, the one path no shipped backend takes. **Ask of any rule a caller applies to a
  failure whether the callee lets that failure reach it**; the exposing fact throws INSIDE the real backend,
  after sending, and asserts the second candidate is never asked (`GenerationBackendThrowTests`).

- **A field accepted on WRITE and absent from the READ record is a write-only field, and every gate on earth <!-- trap: sub=memory shape=silent-loss,scope-blind -->
  says it is fine.** `MemoryWrite.Metadata` was accepted, persisted and returned by all three stores — then
  dropped at the **three** lines in `GraphMemoryEngine` that project a node onto `MemoryItem`, for four
  releases (**D93**). **The fix found two of the three, because a target-typed `new(...)` is invisible to a
  search for the constructor's NAME**, and six records copied that blind spot as independent confirmation.
  **Count a record's construction sites from the TYPE, not its constructor's name** — the compiler knows them
  all (rename the record, or comment out a member and read the errors); `new()`, a collection-initializer
  element, `return new(...)`, `default` and `with` are all unreachable by text search.
  <br>**The tell is a near-mirror pair where one side carries a member the other does not** — here
  `MemoryWrite`/`MemoryItem`. Whenever a write record grows a member, diff it against the read record and
  decide out loud whether each gap is a design choice or an omission. And "the projection passes the instance
  through" is not a test: pin the round trip, and mutate the pass-through to prove the pin is real.

- **When a null argument GAINS a meaning, every place it is INTERPOLATED into a key is a second door.** <!-- trap: sub=memory shape=second-door -->
  **D86** made a null scope mean "every scope" in `SemanticMemoryEngine`, and the graph engine's own semantic
  seeding still built `{Name}|{task}|{scope}` — for a null scope, a collection no write can create — so the
  common unscoped path stayed silently unimproved until an adopter found it. Both sites read `query.Scope`;
  only one was in the first fix's diff. Grep for the interpolations, not for the seam that was reported.

- **Inserting a member ABOVE an existing one strands that one's doc onto your new member.** Four times on <!-- trap: sub=docs shape=stale-claim -->
  2026-08-16/17, by one hand: the neighbour's `<summary>` (twice, only its `<param>` tags) attached to the
  newcomer, which then carried two docs while the member below carried none — the same in JSDoc. **The
  insertion anchor is a declaration; what sits above it is somebody else's doc.** Gated: `check-comments`
  fails a stacked `<summary>`, which the compiler does not warn about and which SHIPS.
- **An invariant DOCUMENTED in one implementation is not a contract, and the backend that wrote it down is <!-- trap: sub=storage,tests shape=second-door,scope-blind -->
  usually the only one that keeps it.** Three times in one storage pass (`docs/FIXES.md`, 2026-08-16):
  `InMemoryVectorStore`'s top-k tiebreak, documented as *"load-bearing"*, was absent from both persistent
  backends, and `MemoryGraphSql.MinimumStability` was hoisted while the in-process store kept a second literal
  that SUBSTITUTED where SQL FLOORS. **The tell is a doc that argues for a rule in the first person**; ask
  "what holds the others to it?" — here a shared contract every backend runs, never a per-backend test file.
  **A green cross-backend run does not mean the backends agree for the same reason**: SQLite passed the
  tiebreak by walking an autoindex in id order by accident, so find out WHY an expected failure passed.
- **A SPEND cap is a capability too, and its second door is the one that moves the money.** Both <!-- trap: sub=generation shape=second-door,fail-open -->
  `GenerationFetchTool` and the render job's handler fetch a finished render, only the handler recorded its
  cost, and a queue backend prices at FETCH — so a configured cap never fired (`docs/FIXES.md`, 2026-08-16).
  **The tell is a check and a record living on different paths**: ask where the total is INCREMENTED and
  whether every path that spends reaches it. A cap whose total never moves fails silently and permissively,
  and exercising a path end to end is not testing its accounting. **The GATE can differ per door too**: the
  router recorded under a media-budget marker while the job handlers and `generate_fetch` recorded whenever
  ANY `IUsageTracker` was registered, so a chat-only budget billed queued renders (**D185**).
- **A capability enforced at one entry point is not enforced if a second entry point reaches the same <!-- trap: sub=di,generation shape=second-door,fail-open -->
  objects.** The hosted MCP endpoint ran the app's `ITool`s through `ToolFunction` with no `IGuardRail`, while
  the tool loop gated every call (`docs/FIXES.md`, 2026-08-15); both doors now run
  `ToolInvocation.InvokeGatedAsync`, and a new door onto the app's `ITool`s runs it too. **The tell is shared
  STATE, not shared code**: ask "what else can reach these objects, and does it apply the same rules?"
  whenever a new surface is given the app's tools, stores or providers — guards, budgets, rate limits and
  redaction each live at one door.
  **Gated for decorators**: `DecoratedInterfaceTests` fails a default body on any interface a shipped class
  decorates, because a default compiles clean while a decorator silently skips the new door (the compiler
  forced both media decorators to take `StreamAsync`, **D67**; a null-bodied async probe slipped through,
  **D176**). The compiler forces a member, not a governed one, so each door still needs a behaviour test.
- **A constant does not live in one place just because it was written once — and the copy nothing ENFORCES <!-- trap: sub=generation shape=stale-claim,second-door -->
  is the one that rots silently.** The local-diffusion ceiling of 768 lived in the scale, a second clamp in
  the rounding (a knob that appears to work and cannot exceed its old value), and the ADVERTISED
  `Capabilities.Limits` (`docs/FIXES.md`, 2026-08-17; **D68**). An enforced constant is corrected by its own
  failure; an advertised one is read only by a person, so it lies to consumers who plan against it. **When a
  value becomes configurable, grep the NUMBER itself, not the identifier.**
- **A rule that is right for the only implementation exercising it is a coincidence, and the second <!-- trap: sub=cli shape=ordering,silent-loss -->
  implementation is where that shows.** `CliProviderEngine` appended an `ICliToolProvisioner`'s args after
  the backend's argv. Correct for `claude`, whose argv ends in options; wrong for `codex`, whose argv ends
  in the `-` stdin positional, where everything after it is read as PROMPT text and **a swallowed flag is a
  spent turn rather than an error**. `CodexExecArgs` had documented that hazard and taken an `extraOptions`
  parameter for it; the agent path used it and the completion path structurally could not
  (`docs/DECISIONS.md` **D65**). **When a shared engine composes something whose correctness depends on a
  backend's own grammar, the backend places it** — the engine may supply the pieces, not choose the order.

- **A member of a variation-point collection that the ROUTING RULE can never select is inert, and the <!-- trap: sub=memory,di shape=ordering,fail-open -->
  registration is what makes it look wired.** `CompositeMemoryEngine` routed a write to the FIRST member
  supporting its grade, so in `UseGraph().UseSemantic()` the semantic store stayed empty — reported by two
  adopters on 3.0.0 (`docs/task-archive.md` Parts 89 and 90; **D85**), and present in this repository's own
  README. **Ask not "is this registered?" but "which member does the selection rule actually PICK?"** of any
  collection whose consumer picks ONE. And a check that also fires on correct wiring is worse than none: the
  narrow rule reports a member only when EVERY grade it supports is already claimed.

- **A projection the OWNING STORE does not hold is a second door onto every removal verb, and the shared <!-- trap: sub=memory,storage shape=second-door,scope-blind -->
  contract that would catch it structurally cannot see it.** `GraphMemoryEngine` indexed each write's FULL
  content into an `IVectorStore` collection it addresses itself, and neither `ForgetAsync` nor `PruneAsync`
  touched it, so a consent withdrawal left the content readable at rest (`docs/FIXES.md`, 2026-08-26) —
  twelve days after the same defect was caught on subjects, which live INSIDE the store's contract. **A
  cross-backend contract proves a promise about the store it is a contract FOR.** **Ask of any removal verb
  what else the write touched** (index rows, vectors, caches, review logs). **Order the two deletes by which
  failure you can live with** — projection first where a residue is the defect, store first where an orphan
  is — and give the ERROR HANDLING the same asymmetry as the order; **derive a projection's address from the
  records removed**, and sweep a PREFIX as well only where no key can contain its separator
  (`MemoryVectorCollection.PrefixFor`, U+001F): over a printable one a prefix match over-deletes into a
  neighbouring task, and without the sweep an orphan of an earlier partial failure survives an unscoped forget.

- **When a decision falsifies a claim, grep the CLAIM — not the file you happened to be reading.** Measured <!-- trap: sub=docs shape=stale-claim,second-door -->
  2026-08-30 (`docs/task-archive.md` Part 126). The 3D survey established that `3d → image → video` chains
  nothing, and the false sentence was corrected in `ProviderKinds.Model3d`'s shipped XML doc — while the
  IDENTICAL claim in `README.md` survived two consecutive passes, because each fix was made where the defect
  was FOUND rather than everywhere the claim lived.
  <br>**No gate can catch this shape**: `check-docs` only knows vocabulary a decision RETIRED, and a claim
  going false retires no word, so the sentence stays grammatical, plausible and wrong. The cost is one
  `grep` for the claim's distinctive phrase at the moment you correct it, against a reader implementing the
  wrong thing later.

- **A grep with context shows you a method's BODY, and inferring its NAME from the lines around it is how a <!-- trap: sub=docs shape=unmeasured -->
  member that does not exist gets into prose.** A `-C 4` hit showed a body with the signature just above the
  window, and the method was cited as `ProviderCapabilities.CanServe` <!-- link-ok: the WRONG name, quoted --> in two commits; the real
  name is `Supports`. **Read the DECLARATION before citing a member.** `check-links`' member half now gates
  `Type.Member` citations, and the lesson survives it: an identifier written into prose is a checkable claim.

- **A capability FLAG is a promise, and a flag nothing implements is a silent wrong answer rather than a <!-- trap: sub=generation shape=silent-loss,second-door -->
  missing feature.** `ComfyUiProvider` declared `SupportsInputs = true` and never read `request.Inputs`, and
  because the flag is an ADMISSION filter the router CHOSE it for exactly the work it could not do — the
  input dropped, the render plausible and billed (`docs/FIXES.md`, 2026-08-30). **The detection: grep the
  flag's own subject** — declared once and read never is the whole bug. **The fix belongs in the CONTRACT**
  (`GenerationProviderContract.A_handed_input_is_consumed_or_refused`): whenever a capability, delivery mode
  or kind is DECLARED, something must assert the code path behind it exists. **And the contract's own INPUT is
  an axis**: the fact first handed ONE input, so four backends that read only the first passed it while
  dropping a second — a pipeline stage's chained artifact. It hands two now.

- **"Pick the first `image/*` artifact" chains a texture ATLAS, which renders perfectly and is completely <!-- trap: sub=generation shape=wrong-subject,fail-open -->
  wrong — and the media type cannot save you.** From a desk survey of 3D backends (`docs/task-archive.md`
  Part 124): a mesh backend's only `image/*` outputs are UV texture atlases, a flattened skin rather than a
  view, and branching on `MediaType` fails too because a GLB can arrive as `application/octet-stream`. **A
  stage that cannot positively identify a chainable artifact must REFUSE rather than fall back** — which is
  why `MediaArtifact.ToInput(role)` takes an explicit role.

- **A loud refusal raised into a FAIL-OPEN consumer is a silent one, and the code that raises it cannot <!-- trap: sub=memory shape=fail-open,silent-loss -->
  tell.** `CrossEncoderLogits.Read` refused a multi-label (NLI) head "safe and LOUD" — inside
  `ScoringVerificationPolicy`, which is fail-open by contract, so every recall went silently unverified
  (`docs/FIXES.md`, 2026-09-15). **A guard's audibility is a property of its CALLER**: name who catches it,
  and move a check whose caller swallows to where a throw still stops something (composition). **The tell
  is a PERMANENT condition reported through a channel built for TRANSIENT ones**; where it cannot be lifted,
  separate it by LOG LEVEL. And check against the DECLARATION rather than a native handle, or no test can
  reach the check.

- **`string.ReplaceLineEndings` does not fold `\v`, nor FS/GS/RS (U+001C–U+001E)** — its roster is CR, LF, <!-- trap: sub=memory shape=scope-blind,second-door -->
  CRLF, NEL, LS, FF and PS — **so a "one line" guarantee built on it has doors it never names.** **D166**'s
  `MemoryLine` doc claimed `\v` was folded, and the claim is what made the gap invisible: a reader checks a
  sentence, not an API's roster. Found 2026-09-23 by the pre-release review; Python's `str.splitlines` breaks
  on all four. `MemoryLineTests` enumerates every character, and `MemoryHeadline.Derive` now shares the rule
  rather than a second copy of the call.

## Refactoring & namespace moves

- **A domain PREFIX is evidence of where a type was BORN, not of what it belongs to — so a family rename <!-- trap: sub=build shape=silent-loss,wrong-subject -->
  moves the cross-domain members into the wrong family, and the compiler agrees with you.** Renaming `Llm*` → <!-- drift-ok: the trap IS the rename; it must name the family it moved -->
  `Text*` (**D154**), `LlmConsumers` was read by the media tools too, so it became `ProviderConsumers`, not <!-- drift-ok: names the ONE type that kept the wrong prefix, which is this trap's whole subject -->
  `TextConsumers`, which would have compiled, shipped and argued for a second media-side copy. **Check each
  member's READERS before the sweep, not its name.** And a word can mean two things, one of them PERSISTED
  (`is_llm`, the `"llm"` score group): **grep the token in the MIGRATIONS and the wire before setting a
  rename's scope**, because the stored spelling is what cannot move cheaply.
- **A sweep that renames every sibling BUT one has decided about that one, invisibly.** Everything around <!-- trap: sub=docs,build shape=stale-claim -->
  it moved, so the survivor reads as settled rather than unexamined — `AddGenerationProvider` was left <!-- drift-ok: names the registration D156 deleted, which is the trap's example -->
  deliberately odd among five renamed siblings, and **D156** then answered it by deleting it (**D154**).
  Name the survivor out loud, or leave it visibly odd, rather than let momentum settle it.
- **Moving a member OFF an interface leaves an instrument compiling through a bridge whose runtime cast can <!-- trap: sub=measurement,build shape=stale-claim,silent-loss -->
  no longer succeed — every sweep on that path then crashes at FIRST USE, days after the refactor shipped
  green.** Found 2026-09-21 by the first sweep run since **D153** step 4 (2026-09-18) moved `EmbedAsync` off
  `IModelProvider`: `BenchVectors.EmbedAsync` was rewritten to cast to `IVectorProvider`, but the bench's own
  doubles (`SweepDoubles.OpenAiCompatibleVectorProvider`, `SweepDoubles.CachingVectorProvider`) still
  declared bare `IModelProvider`, so every single-text embed call in every sweep threw
  `InvalidCastException` — and every recorded sweep figure predates the refactor, so nothing had noticed.
  <br>**No gate can see this by design**: `verify` BUILDS the bench project, and the break is a runtime cast
  behind an extension method, so the build stays green — while the sweeps themselves are not in `verify`
  because they need a live model. **When a refactor touches a seam the instruments double, run ONE sweep
  before recording the refactor done** — the cheapest on the roster suffices, because the crash sits at the
  first call, not in the tails.
- **Deleting a tracked source file with `rm` instead of `git rm` breaks the GUARD TESTS, and the error <!-- trap: sub=gates,git shape=wrong-subject -->
  names a path that is plainly gone — which sends you looking at the wrong thing.** Several guard tests
  enumerate sources through git rather than the filesystem, so an unstaged deletion leaves the file in the
  listing and the next `readFileSync` throws `ENOENT` on a path you deleted on purpose. Hit twice in one
  session (2026-09-14), by `check-comments`' real-tree tests both times.
  <br>**The fix is `git add -A` (or `git rm`), not a code change**, and the tell is that the failure is an
  ENOENT rather than an assertion: a guard test asserting about a missing file has found a defect, one
  CRASHING on it has been handed a stale listing. `verify` runs `test-devtools` FIRST, so this presents as
  the whole gate suite failing immediately after a refactor that was actually fine.
- **A `new`-SHADOWED method dispatches on the DECLARED TYPE, so narrowing a field's type — the safest-looking <!-- trap: sub=build shape=silent-loss,wrong-subject -->
  tidy-up there is — silently changes behaviour with no call site edited.** `BertTokenizer` shadows
  `Tokenizer.EncodeToIds` with a `new` method adding `[CLS]`/`[SEP]`; `Model2VecProvider` held a `Tokenizer`
  field and correctly got the base one, so narrowing the field to `BertTokenizer` would fold two extra rows
  into every vector (**D122**). `override` would be safe and `new` is not, and nothing marks the difference at
  the use site. It was found by accident, by a test whose `var` inferred the other type — **when you pin
  behaviour that depends on a declared type, the test must state that type deliberately.**
- **Splitting or merging a document breaks its `§` citations two ways `check-links` cannot see.** A BARE <!-- trap: sub=docs,gates shape=scope-blind,silent-loss -->
  `§N` has no filename and the gate's `ANCHOR_PATTERN` requires one on purpose (guessing names the wrong
  file) — the D114 split left ten resolving to nothing. And a sweep fencing sub-sections with `(?!\d|\.)`
  skipped every citation that ended a sentence (`§5.`), 43 of 139; the pair is `(?!\d)(?!\.\d)`. Before a
  split, grep for a bare `§` and decide each by hand, and count the population a second way.
- **A compiler error list is not the authoritative site-list for a rename or move — it misses silently in <!-- trap: sub=build shape=scope-blind -->
  three distinct ways.** **(1) It cannot see warnings**: `CS1574`, an unresolved `<see cref>`, is a warning,
  so a file referencing a moved type only in an XML comment produces no error — and the cref that survives a
  namespace sweep is the PARTIALLY-qualified one (`Generation.MediaRequest`, `Routing.MediaRoutingPolicy`),
  invisible to a pattern anchored on `Lyntai.<Old>`: grep `(^|[^\w.])<Segment>\.[A-Z]` too (**D154**). **(2) It
  truncates across projects** — MSBuild stops a dependent once its dependency fails (4 of 78 true sites
  surfaced). **(3) It truncates within a line** — five test files with genuine `CS0246` errors reported
  none. **Search the moved type NAMES across every file, code and prose alike**; an exclusion added to that
  search is a bet that the excluded area is covered elsewhere, and a mostly-right bet hides exactly the part
  that was not.
- **A member name reached by REFLECTION is a rename site the compiler cannot see** — and here the one gate <!-- trap: sub=gates,measurement shape=scope-blind -->
  that would catch it is deliberately outside `verify`. `bench/Lyntai.Benchmarks/MemoryPolicySweep.cs` reads
  engine internals with `GetField("_agePolicies", …)!`: rename that field and the code still compiles, `verify`
  still goes green over every one of its gates, and the sweep throws `NullReferenceException` on the `!` the next
  time somebody runs `node devtools/dev.mjs memory-sweep` — which is slow by design and therefore not in
  `verify`. `docs/DECISIONS.md` D47's own amendment justifies that reflection as a trip-wire making "a Core
  rename fail LOUDLY", and it does — **but only when the sweep is run, which nothing forces.** Before any
  rename, grep for the identifier as a STRING (`GetField("`, `GetProperty("`, `GetMethod("`, `nameof` is
  safe, serializer attribute names, SQL column names, reflection in tests) and treat each hit as a site. The
  general shape: **a green build proves nothing about names the build never resolved**, and "the compiler
  will find the call sites" is exactly the reasoning that misses them.
- **A rename that stops at the types leaves the retired word alive in PARAMETER NAMES and PROSE, and the <!-- trap: sub=docs,gates shape=stale-claim,scope-blind -->
  prose is what puts it back.** D47 renamed four memory seams and deferred the parameter names as "purely
  cosmetic"; three survived into 3.0's frozen surface — `ageClocks`, `appraisers`, `modulators` — beside about
  a hundred XML-doc sentences, **the parameters named after the prose**. **Named arguments are public API**
  (recorded in the baseline without judgement, and a major to rename after a freeze), and **a deferral whose
  justification depends on a window staying open needs re-checking when it closes**. `check-docs` now scans
  code COMMENTS; a parameter name is SURFACE, which is why `check-api-vocabulary` is a separate gate.

- **A rename sweep that REACHES prose destroys a contrast, and the result passes every gate because the <!-- trap: sub=docs,gates shape=stale-claim,scope-blind -->
  retired name is gone.** When a decision unifies two seams, a sentence that CONTRASTED them comes out naming
  one thing on both sides — grammatical, plausible, empty. Measured 2026-09-16 after **D127**: six sites
  across the decision record, `CHANGELOG.md`, this file and a shipped comment, plus four README sites
  wrecked differently (one a compiled sample whose type test was always false). The remedy then exempted ONE
  file. **When a sweep is found to have hurt one file, the finding is about the SWEEP.** `check-tautology`
  now gates the collapsed-onto-one-name half.
- **A gate's SCOPE justified by a measurement expires the way a blocked item does — silently, when the <!-- trap: sub=gates,docs shape=scope-blind,stale-claim -->
  tree grows around it.** `check-links` left its code tier's Part half out on "the measurement found none";
  re-run, it found dozens and most were dead (`docs/GATES.md` §check-links holds the count). **When you
  narrow a gate on a count, write the count down, and re-run it when the excluded area has visibly grown.**
- **A `retiredTerms` rule anchored on the FULLY-QUALIFIED name is anchored on the spelling prose is least <!-- trap: sub=docs,gates shape=scope-blind -->
  likely to use.** `\bLyntai[.]Providers[.]Default\b` missed two live sites by exactly one word — the
  README and `docs/AOT.md` both wrote the bare `Providers.Default`, because prose drops a namespace prefix
  the moment context makes it obvious. **And widening it is NOT the fix**, which is the half worth carrying:
  dropping `Lyntai[.]` finds those two and sixteen others, five of them decision entries recording what the
  package was called on the day they decided, so it measures 2 true against 16 false. Both directions are
  wrong for the same underlying reason — a package id is a WORD in prose and a PATH in a registry — and the
  honest resolution was to fix the two by hand and record this rather than ship a rule that would collect
  sixteen `drift-ok`s and rot. Measure a candidate widening the way `docs/GATES.md` requires of a new gate.
- **A doc comment asserting "this is NOT duplication waiting to be extracted" is unfalsifiable, and it is <!-- trap: sub=docs,storage shape=vacuous,scope-blind -->
  the one claim nobody re-reads.** `PostgresMemoryGraphStore`'s class doc said exactly that, justified by
  three true dialect differences — all about the SQL — while the file held four materialization row types
  byte-identical to the SQLite twin's, where a column↔property mismatch is a SILENT null (**D77**). **Treat a
  "these must stay separate" claim like a bound: ask what it FORBIDS, and check that the answer covers the
  whole file it is written on** — and when you write one, name the part it does NOT cover.

- **A "tidy up the formatting" regex in a scripted sweep is a rewrite of every line it can match, and the <!-- trap: sub=gates,encoding shape=silent-loss -->
  build stays green while it happens.** Stripping work-log citations from `src/` comments (2026-08-16), a
  cleanup rule appended after the real substitutions collapsed the INDENTATION of all 349 tracked files in
  `src/` — "349 files swept" read like success — and C# does not care about indentation (`docs/FIXES.md`,
  2026-08-16). **(1) A sweep must only touch a line one of its REAL rules changed. (2) Assert the invariant
  it must not break, in the script** — line count, per-line leading whitespace. **(3) The file COUNT is the
  tell**: a targeted sweep that touches nearly every file has stopped being targeted.

## Testing

- **Every recall-quality number is a property of the INSTRUMENT until proven otherwise, and this has cost <!-- trap: sub=measurement shape=wrong-subject -->
  four published figure sets.** `docs/task-archive.md` Part 118 (questions sharing a store), Part 119 (a
  near-tie noise floor of ~1 point read as a result), **D100**'s own withdrawn *"search wants two shots"*,
  and `memory-salience`'s OFF arm that was never off (`docs/FIXES.md`, 2026-08-30). Every one looked like a
  finding about the library and was a finding about the harness.
  <br>**Before believing a delta, run the arm that structurally CANNOT move** — `vector` never touches the
  graph store, a verifier shown exactly the page being returned cannot change what is in it, a cap above the
  depth cannot bind. An arm that moves when it provably cannot is the instrument, and one that reproduces
  its anchors cell-for-cell is what licenses reading everything else in the table. **Then repeat the after
  arm rather than reasoning about it.**

- **Asserting a specific FAILURE MODE when the claim is only "it tried" makes a test race the clock.** <!-- trap: sub=tests shape=ordering -->
  Measured 2026-09-04: `ByoHttpClientTests.Default_path_still_creates_a_lyntai_client` pointed at a closed
  local port (a scripted handler now) and pinned `ProviderVerdict.Failed`, whose own comment says it is proving *the client existed and
  tried*. Under load — a session that had been driving a local model server for an hour — the connect
  outlasted the 5 s `ProviderTimeout` and the verdict was `Timeout`: **a different correct answer, and a red
  `verify`.** It passed standalone immediately afterwards, which is what makes this class expensive to
  diagnose.
  <br>**Assert the CLAIM, not one way of satisfying it.** Here both `Failed` and `Timeout` prove DI wired a
  real client; only `Ok` would refute it. Wherever a test provokes a failure to prove a path exists, accept
  every failure that proves it — a narrower assertion is not a stronger test, it is a timing dependency.

- **A SELF-HEALING mechanism absorbs the bug you are asserting against, so the test passes with the fix <!-- trap: sub=tests,memory shape=vacuous -->
  reverted.** Measured 2026-09-02 (`docs/task-archive.md` Part 137). `MemoryWalk` upgrades an entry
  discovered as a headline once a later step seeds it, so an assertion on the LAST step's held set is true
  whether or not expansion honoured the projection it was supposed to — it passed against the unfixed tree,
  and only a mutation check said so. **Assert at the step that DISCOVERED the thing** (`NewItems`), not on
  the accumulated state, wherever a later step can repair what an earlier one got wrong.
  <br>**The general shape: any convergent or retrying mechanism hides a defect in the step you are testing.**
  If reverting the fix leaves the test green, the assertion is on the wrong observable — and the only way to
  learn that is to actually revert it, which is cheap and is the check people skip when a test passes first
  time.

- **A rule only the NON-DEFAULT path can break has no test, and the mutation check run through the default <!-- trap: sub=tests shape=scope-blind,vacuous -->
  reports it as dead code.** `MemoryWalk` is finite for two reasons — the default seed selector runs dry, AND
  a step that moves nothing ends it — and deleting the second guard failed no test, because every fact used
  the default selector (`docs/task-archive.md` Part 120). **Ask of a belt-and-braces guard which caller
  reaches it FIRST, and whether any test looks like that caller** (here `SeedSelector = s => s.Items`).
  **Bound the loop inside the TEST too**: a missing termination guard should fail in milliseconds, not hang.

- **Refactoring a measurement harness can change a DENOMINATOR rather than a result, and every rate moves <!-- trap: sub=measurement shape=silent-loss,wrong-subject -->
  while retrieval is untouched.** The LoCoMo loop ran shots 2 and 3 UNCONDITIONALLY; the library's walk ENDS
  when a step moves nothing, which would silently have dropped those rows from every tally. **When an
  unconditional loop becomes one that can terminate early, ask what the dropped iterations were
  CONTRIBUTING.** The defence is to **run the before arm** — stash the harness, run, restore, re-run — and
  here every published cell reproduced exactly.

- **A PRE-REGISTERED prediction is only adjudicated by a run powered to adjudicate it, and pre-registration <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  gives no protection against judging it on one that is not.** At n = 200 a 44.8 against 43.9 was written up
  as "refuted my scepticism"; the full 1,540 read 44.0 against 44.4 and the write-up was retracted
  (`docs/task-archive.md` Part 137) — while two arms sending BYTE-IDENTICAL prompts had differed by 0.4 in the
  same table, a noise floor measured for free. It was the SECOND instance (`docs/task-archive.md` Part 134),
  quoted during the session that repeated it. **Compare a delta to a floor measured in the SAME run** — a pair
  of arms that must agree — and call anything not several times that floor "unresolved at this sample". A
  filtered confirmation run must keep the controls of every claim it touches.

- **A metric whose MATCH TARGET survives the transformation it ought to detect is blind to that <!-- trap: sub=measurement shape=wrong-subject -->
  transformation, and it reports a clean comparison between arms that differ by exactly it.** LoCoMo
  evidence-hit matches a `dia_id` in each turn's 44-character HEADER, which survived headline truncation in
  5,882 of 5,882 turns while the answer text survived at 56.2% — so a half-turn and a whole turn scored alike
  (`docs/memory-measurements.md` §5), and "the reader is the bottleneck" was the tempting wrong inference.
  **The tell is a metric whose key and payload can be damaged independently.** Price what was DELIVERED in a
  second column (`chars/q`), or add an arm that differs only in the transformation.

- **A diagnostic built to EXPLAIN a published number runs on its own defaults, not on the flags that <!-- trap: sub=measurement shape=wrong-subject -->
  produced that number — and it explains a different arm while looking entirely reasonable.**
  `memory-locomo --composition` inherited `--seeds 3` for a row published at `--seeds 16` and reported a
  well-formed, wrong decomposition (`docs/task-archive.md` Part 135). **There is no tell, which is the point**:
  a mode that explains a published figure must ASSERT it reproduces that figure. A derived instrument
  inherits its subject's CONFIGURATION, not just its code — the flags passed on the day are part of the arm.

- **A benchmark whose store MUTATES on read has non-independent trials, and the contamination presents as a <!-- trap: sub=measurement,storage shape=wrong-subject,silent-loss -->
  RESULT rather than as a bug.** LoCoMo questions shared one store and this engine writes on every read, so
  question N read a graph questions 1..N−1 had dug through (`docs/task-archive.md` Part 118). **The tell is a
  figure moving that logically CANNOT depend on what was varied**: shot 1, before any expansion, moved 11.6
  points with how much a LATER shot expanded. **The fix is a per-trial store**, byte-copied from a migrated
  template (vectors, written only at ingestion, stay shared). Two traps inside it: checkpoint SQLite's WAL
  before copying, or the clone is silently partial — and COUNT the clone's rows, since a lost row presents as
  a recall regression. **The control has to fail on the old code**: re-run the pair against the pre-fix
  harness.

- **When two arms that are the SAME OPERATION disagree, you have found either a code difference or your <!-- trap: sub=measurement shape=ordering -->
  noise floor — and cheap experiments tell them apart, not reading the diff.** A new `shot-1` arm read 48.5%
  where `lyntai` read 47.7% (`docs/task-archive.md` Part 119); the diff said equivalent. Run the pair on the
  variant with fewer near-ties (it agreed to the decimal), then repeat the run (every graph arm moved one
  question in 132). **The control that localises it is the arm that CANNOT be affected** — both `vector`
  arms were byte-identical throughout. **Reproducibility is a property of the ARM**: measure it per arm
  before quoting a figure to a tenth of a point.

- **A candidate scored on a DIFFERENT SCALE than the pool it joins cannot win, however relevant it is — and <!-- trap: sub=memory shape=fail-open -->
  the feature that added it then reads as inert rather than as broken.** Semantic seeds carried a raw cosine
  into a pool saturated at a flat `1.000`, so the best match entered at 0.785 and raising the seed count from
  0 to 20 moved evidence-hit by exactly **0.0** (`docs/task-archive.md` Part 110; closed since by per-source
  rank fusion, **D103**). Four plumbing hypotheses were refuted first — the seeds arrived and lost. **The tell
  is a knob that does NOTHING rather than a little**: an incommensurable signal sorts below the other scale's
  floor every time. It is **D93**'s point reaching a second victim.
- **A control that compares POOLED verdicts cannot see a split that cancels in the pool, and it reports <!-- trap: sub=measurement shape=scope-blind,vacuous -->
  "no change" while doing it.** The gist sweep's `ConnectionBoost = 0` control said the verdict did not move —
  true of the ARGMAX, while `count@0.9` read tie/A/B/B across four rungs with the boost and B/B/B/B without
  (`docs/task-archive.md` Part 108). **A control is only as fine-grained as the axis it groups by**: when the
  experiment gains an axis, so must the control. Its loud sibling from the same run is the good direction — a
  control asserting a LITERAL the axis varies failed and refused to publish (**D60**).
- **A read path a contract fact does not CALL is a read path that contract does not cover, however <!-- trap: sub=tests,memory shape=scope-blind,vacuous -->
  completely it enumerates implementations.** Measured 2026-08-27 (`docs/DECISIONS.md` D93): a metadata
  round-trip fact ran on all five memory engines and still missed a third broken projection, because it
  called `RecallAsync` and nothing else — `ExpandAsync` projects the entry the caller NAMED separately from
  its neighbours, and no amount of engine coverage reaches a method the fact never invokes. **Coverage of
  IMPLEMENTATIONS reads as coverage and is the axis that is easy to count; coverage of ENTRY POINTS is the
  one that was missing.** The fix is a second fact per read path, each asserting positively — including the
  negative branches, since a single "null is acceptable" assertion passes vacuously for every implementation
  that cannot carry the value at all. The same holds for a numeric column: assert it is NOT A CONSTANT, since
  a field wired to a literal satisfies an equality check on every row.
- **A finding that is WRITTEN DOWN is not a finding that was VERIFIED, and this repository keeps treating <!-- trap: sub=docs shape=unmeasured,stale-claim -->
  the two as the same.** Twice on 2026-08-17: a backlog entry recorded fal as the backend that classified an
  error correctly — the first contract test to reach it showed fal reported `Failed` too — and a CHANGELOG
  entry asserted a behaviour the same release had already falsified. Both were written by someone who had
  READ the code. **The half that gets recorded is the half that was read; the half that was RUN is what a
  claim needs** — and when a record says one of two implementations is fine, check that one first.
- **A contract fact can pass or fail for reasons that have nothing to do with its subject, and the failing <!-- trap: sub=tests shape=wrong-subject,vacuous -->
  direction is the dangerous one because it looks like evidence.** Three shapes, all within an hour of
  writing the 3.0 seam contracts:
  - **Testing the FAKE** — a cancellation fact run against stubs that ignore their token passed or failed by
    measuring the stub; its subject needs a double that honours the token.
  - **Failing before the subject** — a fetch fact handed fal a malformed id, which fal rejects before calling
    out, so it "confirmed" a classification defect without exercising it.
  - **Timing what you could OBSERVE** — "three stalled probes finish under a second" passed alone and failed
    in the full suite. **Assert the PROPERTY, not the clock**: have the fakes record peak overlap, which
    serial execution cannot raise above 1, and drop a clock bound whose failure mode is a hang.
  <br>**When a new fact fails, confirm it failed where you think** — read the message against the code path.

- **Adding a VARIANT to a measurement instrument moves the templates and the READERS, and forgetting the <!-- trap: sub=measurement shape=silent-loss,vacuous -->
  readers produces flattering numbers rather than a failure.** Adding a Chinese arm to `MemoryCorpus`
  (**D55**), the templates were easy; the code parsing generated text back was English grammar in a
  helper's costume, and it returned plausible wrong answers instead of crashing. **Put the readers beside
  the templates** (`CorpusLexicon`) so the compiler names every hole. Three bugs got through anyway, each
  flattering: a two-character shared token below the term floor stopped competing (miss AND pollution read
  `0.0000`); a wording-matched classifier emptied the class the study existed for (**absence reads as "not
  exercised"**); and invariants pinned the cue's UPPER bound only, so a cue matching nothing passed them
  all. **An instrument is code nothing else validates**: guard a variant with goldens on the ORIGINAL
  captured before the axis, an assertion that the variant genuinely DIFFERS, and one that the property the
  instrument depends on still holds in the variant.
- **A wall-clock burst-detection policy replayed IN-PROCESS measures the fixture's replay SPEED, not a <!-- trap: sub=measurement,memory shape=wrong-subject -->
  deployment regime.** `BurstDampenedAgePolicy`, the shipped default, treats writes closer than 5 s as one
  burst, and a 305-write corpus replay finishes in half a second — so the whole corpus is one burst, and
  stability and even co-activation structure become functions of how fast the host ran (2026-08-27,
  `MemoryGistSupportRuleTests`). **Give the clock its own arm** — an injected clock stepped over the window
  is one arm, the real clock another — and never report a finding about the LIBRARY from an arm whose
  distinguishing input is the test host's speed.
- **Microsoft.Data.Sqlite completes its "async" methods SYNCHRONOUSLY, so two awaited store calls issued <!-- trap: sub=tests,storage shape=vacuous -->
  from one thread are sequential by construction — and a concurrency test over them measures nothing.**
  Measured 2026-08-17: an overlap test called `Task.WhenAll(store.ReportStepAsync(a…), store.ReportStepAsync(b…))`
  and failed even AFTER the per-job locking it was written for landed, because the first call ran to
  completion on the test thread (blocking inside the instrumented clock) before the second expression was
  even evaluated — the store's gate never entered into it. Wrap each call in `Task.Run` when the subject is
  concurrency and the backend is SQLite (or anything whose async is sync), and treat "still red after the
  fix" as a cue to ask WHERE the serialization actually is before touching the fix.
- **An orphaned child process inherits the test host's console handles, so a test that STRANDS one wedges <!-- trap: sub=tests shape=resource -->
  the whole runner — past its own failure, past every later test.** The RED run of the hanging-locator test
  failed correctly at its 30 s bound, then `dotnet test` hung, because the stranded child held the inherited
  stdout the harness reads to EOF (`docs/FIXES.md`, 2026-08-17). A bounded in-test wait is not enough when the
  failure leaves a child alive — give the fixture child a self-exit, so a regression costs one slow red run
  rather than a wedged pipeline.
- **This repo has TWO independent ways for a targeted test run to verify nothing.** (1) `dotnet test <!-- trap: sub=tests,gates shape=vacuous,wrong-subject -->
  --filter` **reports success when it matches zero tests** — a filter naming `LlmRouterTests`, a class that
  does not exist (the real ones are `TextRouterCompleteTests` / `TextRouterStreamTests`), passed vacuously and
  looked like a clean regression run. (2) Through the wrapper it depends on `--`, which is not obvious.
  `node devtools/dev.mjs test --filter X` forwards straight into `dotnet test` (`devtools/dev.mjs` spreads
  `...args` into the argv, and always has) and inherits trap (1) whole — a name matching nothing passes
  vacuously, exit 0. `node devtools/dev.mjs test -- --filter X` runs the **WHOLE suite** instead: VSTest reads
  post-`--` tokens as RunSettings arguments and the filter is dropped (measured 2026-08-05: 1573 passed /
  1582 total). Slow but honest — and easy to mistake for the filtered run you asked for, in the other
  direction from (1). **Always read the matched/total count**, and prefer a broad filter over an
  exact class name (`~Router|~Routing|~DeadHost`) so a renamed class degrades to running too much rather than
  to running nothing.
- **A THIRD way, and it reports PASSES rather than nothing: `--no-build` after a build that FAILED tests <!-- trap: sub=tests shape=vacuous,stale-claim -->
  the previous binary.** Measured 2026-09-15 adding a cross-backend contract: a missing `using` failed the
  build, the chained `dotnet test --no-build` ran anyway against the last good dll and printed
  `Passed! - Failed: 0, Passed: 134`. The number is real; it is just an answer about code that is no longer
  on disk, which is worse than the two above because a count that large reads as strong evidence. It is the
  same shape as the mtime trap in `windows-machine.md` §Processes — a stale artifact surviving a change —
  reached from the runner's side instead of the file system's.
  <br>**Chain on the build's EXIT CODE, never on the two commands being adjacent**: `dotnet build …; code=$?;
  if [ $code -eq 0 ]; then dotnet test --no-build …; fi`. Writing them as two lines in one shell call looks
  sequential and is not conditional. `verify` is not exposed to this — it stops at the first failing step —
  so this bites exactly the targeted runs used while iterating, where nobody is watching the build output.
- **A test that is already RED for an unrelated reason cannot be mutation-killed, and a mutation check <!-- trap: sub=tests shape=vacuous -->
  against it proves nothing.** Mutating the salience rank boost's first round (**D45**) left one fact failing
  either way — it was already red at the default weight — and another passing either way, because the gap
  it measured against dominated. **Ask "which specific wrong implementation does this test reject?" of the
  MUTATED run, not just the correct one**; the fix was to pick a scenario the mechanism could actually win
  (`An_explicitly_configured_rank_weight_can_outrank_a_better_textual_match`).
- **A test that pins the OLD behaviour usually encodes it by accident — separate what it was written to <!-- trap: sub=tests shape=wrong-subject -->
  PROVE from what it happens to ASSERT before changing it to unblock a fix.** `A_throwing_backend_still_releases_its_permit`
  asserted the throw escaped, but its subject was permit release; rewritten rather than deleted, it now
  asserts release on the CLASSIFIED path, the stronger claim (`docs/DECISIONS.md` **D64**).
- **A single-runner test cannot tell a GLOBAL cap from a per-process one, and a cap bounds concurrency, not <!-- trap: sub=tests shape=vacuous,wrong-subject -->
  throughput.** "Two passes run 2 jobs, not 4" failed at 4 correctly — a pass hands its slots back, so four
  jobs over two sequential passes never breaks a cap of two. Block inside the handler, count what is in
  flight, and run TWO runners over one store, or a per-process implementation passes (**D73**).
- **A test that HANGS on the failure it detects is worse than no test.** A permit-leak test that blocks a <!-- trap: sub=tests shape=resource -->
  second caller on a gate proves the leak by never completing — which turns `verify` into an opaque hang
  instead of a red test, and the next person bisects the harness rather than reading the failure. Bound every
  await that can block on a permit (a timeout on the wait, asserting the timeout is what fails), so the
  regression the test exists for arrives as an assertion.
- **A counter can return the RIGHT number from two errors that cancel, and only comparing the ITEMS finds <!-- trap: sub=gates shape=scope-blind,vacuous -->
  it.** Measured 2026-08-15 writing `check-counts`' `verify`-gate counter. It parsed `dev.mjs`'s `steps`
  array with `\['[a-z-]+'`, which cannot match `e2e` (the class excludes digits) and DOES match the inner
  argument array in `['check-sensitive', ['--tree']]`. Twelve real steps plus one phantom is thirteen — the
  documented number, on a clean tree, agreeing with the prose it was gating. A test asserting only the COUNT
  passes forever. The test that caught it compared the parsed NAMES and asserted two specific properties:
  `e2e` is present, `--tree` is not. **When a counter's output is a number, pin the SET it derived the number
  from** — the number alone cannot distinguish a correct parse from a wrong one that happens to tie, and the
  tie is likelier than it sounds because both errors are off-by-one in opposite directions.
- **A fail-closed guard belongs on the SOURCE list, not on the post-filter count — the filtered count has a <!-- trap: sub=gates shape=vacuous,scope-blind -->
  legitimate zero.** Giving four gates `check-api-vocabulary`'s rule "a gate that scanned nothing must never
  print a tick", the obvious `if (candidates.length === 0) return 1` failed check-encoding's own test, and
  the test was RIGHT: a call whose only input is a binary has nothing to scan. **An empty SOURCE list is a
  broken listing whoever supplied it; zero SURVIVORS of the filters indicts only the full-tree path**, where
  the tree cannot legitimately produce one — hence `source.length === 0 || (files === null && filtered.length === 0)`.
  Write the both-directions test at the same time: a guard that over-fires gets deleted.
- **A gate whose subject includes a TRANSIENT region is green until the pipeline changes that region — and <!-- trap: sub=gates,docs shape=stale-claim,scope-blind -->
  the pipeline is the one run you cannot afford to fail.** `check-samples` compiled a fence under
  `CHANGELOG.md`'s `## Unreleased`, the release workflow stamps that heading before `verify`, and the census
  dropped mid-pipeline — red in the release, green locally, same commit (`docs/FIXES.md`, 2026-09-19). It
  recurred a week later in `check-links`, as a CITATION resolved through that heading (2026-09-26). **Ask of
  EVERY gate that reads the region, not only the one that burned, which part a scheduled process deletes**;
  reproduce a pipeline-only failure by replaying the pipeline's transformation through the gate's seam; and
  an existing test may ENCODE the defect — invert it with its reason rather than deleting it.
