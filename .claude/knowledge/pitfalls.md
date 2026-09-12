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

## Facets — 198 traps, indexed two ways

_Generated from the per-trap `<!-- trap: … -->` markers by `node devtools/dev.mjs check-pitfalls --write`. Edit a marker, never this index._
_Line numbers only, on purpose: this is for JUMPING, not for reading. The facets are ORTHOGONAL to the headings below — a probe's nine relevant traps once spanned five of them, two of which nobody_
_looking for that task would have opened._

**By AREA** — which part of the repository breaks.

- **`gates`** (36) — 57 · 82 · 91 · 111 · 124 · 133 · 146 · 152 · 175 · 185 · 192 · 207 · 217 · 263 · 283 · 329 · 347 · 479 · 564 · 573 · 583 · 637 · 651 · 721 · 735 · 1041 · 1138 · 1393 · 2344 · 2354 · 2380 · 2391 · 2428 · 2799 · 2829 · 2838
- **`encoding`** (6) — 75 · 80 · 590 · 598 · 864 · 2428
- **`git`** (7) — 207 · 232 · 263 · 288 · 598 · 620 · 869
- **`build`** (8) — 80 · 564 · 573 · 583 · 637 · 1033 · 1393 · 2363
- **`router`** (6) — 1236 · 1241 · 1250 · 1252 · 1255 · 1385
- **`cli`** (18) — 750 · 864 · 1236 · 1257 · 1265 · 1274 · 1278 · 1290 · 1294 · 1301 · 1305 · 1312 · 1316 · 1349 · 1368 · 1372 · 1377 · 2214
- **`lifetime`** (6) — 1415 · 1424 · 1429 · 1434 · 1447 · 1455
- **`storage`** (17) — 1328 · 1464 · 1566 · 1594 · 1596 · 1597 · 1599 · 1601 · 1619 · 1630 · 1859 · 1871 · 2127 · 2244 · 2410 · 2592 · 2776
- **`memory`** (38) — 387 · 422 · 550 · 1328 · 1498 · 1517 · 1528 · 1554 · 1566 · 1581 · 1602 · 1619 · 1630 · 1669 · 1684 · 1691 · 1710 · 1721 · 1739 · 1752 · 1765 · 1823 · 1878 · 1898 · 1934 · 1970 · 1988 · 1995 · 2026 · 2048 · 2076 · 2148 · 2223 · 2244 · 2485 · 2644 · 2672 · 2755
- **`generation`** (11) — 1101 · 1131 · 1214 · 1241 · 1447 · 1970 · 2161 · 2173 · 2197 · 2304 · 2329
- **`di`** (12) — 1434 · 1684 · 1710 · 1890 · 1892 · 1895 · 1898 · 1916 · 1995 · 2026 · 2173 · 2223
- **`measurement`** (64) — 111 · 372 · 387 · 405 · 422 · 463 · 493 · 509 · 528 · 540 · 550 · 671 · 750 · 757 · 762 · 768 · 795 · 808 · 819 · 827 · 843 · 850 · 887 · 897 · 917 · 928 · 962 · 989 · 995 · 1011 · 1028 · 1033 · 1072 · 1101 · 1116 · 1131 · 1146 · 1164 · 1179 · 1196 · 1214 · 1265 · 1464 · 1483 · 1554 · 1721 · 1752 · 1779 · 1790 · 1805 · 1823 · 1878 · 1934 · 2380 · 2461 · 2512 · 2527 · 2551 · 2571 · 2592 · 2622 · 2659 · 2723 · 2755
- **`docs`** (23) — 124 · 152 · 175 · 296 · 620 · 651 · 671 · 721 · 1041 · 1052 · 1057 · 1064 · 1138 · 1528 · 2048 · 2115 · 2279 · 2289 · 2344 · 2354 · 2391 · 2410 · 2681
- **`tests`** (21) — 1349 · 1540 · 1581 · 1602 · 1669 · 1691 · 1859 · 1871 · 2127 · 2474 · 2485 · 2496 · 2672 · 2694 · 2776 · 2784 · 2792 · 2796 · 2799 · 2811 · 2824

**By SHAPE** — how the wrongness stays invisible. Orthogonal to the area, and usually the more useful
of the two: most of these traps recur in a subsystem that had never met them.

- **`fail-open`** (21) — 207 · 387 · 422 · 651 · 762 · 1072 · 1214 · 1250 · 1255 · 1274 · 1349 · 1429 · 1517 · 1765 · 1898 · 1934 · 2161 · 2173 · 2223 · 2329 · 2644
- **`cancellation`** (5) — 1236 · 1241 · 1498 · 1517 · 1540
- **`vacuous`** (42) — 111 · 185 · 192 · 372 · 405 · 463 · 493 · 573 · 768 · 808 · 827 · 917 · 1101 · 1116 · 1131 · 1164 · 1305 · 1368 · 1385 · 1540 · 1581 · 1602 · 1669 · 1691 · 1779 · 1790 · 1823 · 1934 · 2048 · 2410 · 2485 · 2496 · 2527 · 2659 · 2672 · 2694 · 2723 · 2776 · 2799 · 2811 · 2829 · 2838
- **`scope-blind`** (36) — 82 · 133 · 146 · 152 · 175 · 185 · 207 · 217 · 263 · 329 · 347 · 598 · 721 · 735 · 1179 · 1393 · 1684 · 1752 · 1859 · 1871 · 2048 · 2076 · 2127 · 2244 · 2344 · 2354 · 2363 · 2380 · 2391 · 2410 · 2496 · 2659 · 2672 · 2796 · 2829 · 2838
- **`second-door`** (20) — 1252 · 1290 · 1294 · 1305 · 1312 · 1372 · 1377 · 1455 · 1597 · 1619 · 1898 · 2026 · 2127 · 2148 · 2161 · 2173 · 2197 · 2244 · 2279 · 2304
- **`stale-claim`** (18) — 57 · 152 · 232 · 296 · 671 · 819 · 850 · 869 · 1041 · 1057 · 1528 · 1895 · 2115 · 2148 · 2197 · 2279 · 2391 · 2681
- **`silent-loss`** (53) — 75 · 80 · 124 · 283 · 288 · 329 · 347 · 540 · 590 · 637 · 651 · 750 · 808 · 864 · 897 · 928 · 962 · 1028 · 1072 · 1131 · 1138 · 1146 · 1214 · 1278 · 1294 · 1328 · 1415 · 1447 · 1483 · 1554 · 1594 · 1596 · 1597 · 1599 · 1619 · 1630 · 1669 · 1805 · 1878 · 1890 · 1895 · 1916 · 1988 · 2026 · 2076 · 2214 · 2304 · 2344 · 2354 · 2428 · 2512 · 2592 · 2723
- **`wrong-subject`** (60) — 57 · 91 · 111 · 288 · 372 · 405 · 422 · 479 · 493 · 509 · 528 · 540 · 550 · 564 · 583 · 768 · 795 · 827 · 843 · 850 · 897 · 928 · 962 · 989 · 995 · 1052 · 1064 · 1101 · 1116 · 1146 · 1164 · 1179 · 1196 · 1252 · 1301 · 1393 · 1424 · 1434 · 1464 · 1554 · 1602 · 1710 · 1721 · 1739 · 1752 · 1779 · 1790 · 1823 · 1970 · 1995 · 2329 · 2461 · 2512 · 2527 · 2551 · 2571 · 2592 · 2694 · 2755 · 2799
- **`unmeasured`** (17) — 509 · 550 · 620 · 671 · 721 · 757 · 1011 · 1033 · 1257 · 1265 · 1274 · 1316 · 1368 · 1739 · 1970 · 2289 · 2681
- **`ordering`** (10) — 583 · 1278 · 1377 · 1566 · 1601 · 1916 · 2214 · 2223 · 2474 · 2622
- **`resource`** (14) — 564 · 598 · 637 · 887 · 995 · 1196 · 1257 · 1349 · 1455 · 1464 · 1892 · 2784 · 2792 · 2824

<!-- facets:end -->

## Environment / tooling

- **`verify` answers a question about the bytes it READ, so editing anything while it runs makes its whole <!-- trap: sub=gates shape=wrong-subject,stale-claim -->
  report — the green summary included — describe a tree that no longer exists.** Measured 2026-08-30, twice
  in one session: docs were edited during two separate runs, so the prose gates' verdicts covered an
  indeterminate mix of before and after, and one of those runs also predated six new tests it therefore
  never executed. Both times the only thing that noticed was the author remembering, which is not a
  mechanism. **This is a false PASS**, the direction everything else here is built to avoid.
  <br>**Gated since**: `verify` content-hashes the tree before and after (`scripts/_tree-fingerprint.mjs`),
  names every file that moved, suppresses the green line and exits non-zero. Proven on the red path, not
  just written — an induced mid-run edit produced the failure and exit code 1.
  <br>**The habit the gate does not replace: start `verify` and then keep your hands off the tree.** If you
  need to keep working, work somewhere else and re-run it at the end — a re-run is five minutes, and a green
  line you have to reason about is worth nothing. Two cheaper checks that do NOT work: an mtime comparison
  (fires on a byte-identical rewrite, so it cries wolf and gets ignored) and `git status` (blind to a change
  within an already-dirty file, which is the normal state of a tree being verified).
  <br>**And never read an exit code through a pipe.** `node dev.mjs verify | tail -20` reports *tail's*
  status, so a failing verify looks like a clean one — the notification for the very run that proved this
  gate said `exit code 0` while the output said `✗`. Redirect to a file and echo `$?`, or check `PIPESTATUS`.

- **This machine's console is GBK/CP936.** Writing UTF-8 through it (PowerShell `Set-Content`/`Out-File` <!-- trap: sub=encoding shape=silent-loss -->
  without `-Encoding utf8`, `echo >`, a shell heredoc) **double-encodes and lossily corrupts** non-ASCII
  content — it once mangled every `灵台`/`—`/`§` in `TASKS.md` irreversibly. **Always write files with
  the Write/Edit tools** (they emit UTF-8 directly) or, in scripts, `fs.writeFileSync`/`-Encoding utf8`.
  Verify with an ASCII-safe check (codepoints), not by eyeballing console output (which re-mangles it).
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
  worth knowing before someone builds it.** Tried 2026-08-14 as the prose counterpart to `check-samples`
  (which compiles fenced C# but says nothing about a type named in a SENTENCE): scan maintained docs for
  backticked PascalCase names absent from the whole tree's vocabulary. It produced **~45 hits and zero
  defects**, because naming something that does not exist is frequently the CORRECT thing for this
  repository's prose to do:
  · `pitfalls.md` cites `LlmRouterTests` precisely because that class does not exist — it is the example in
    the "a filter matching nothing passes vacuously" entry;
  · `generic-library.md` says `AgentStreamEvent.ToolCall` "deliberately has *no* `FilePath` property";
  · `storage.md`'s backend-pair table writes `TraceStore`/`KeyValueStore` as shorthand for the Sqlite+Postgres
    PAIR, neither of which is a type name;
  · `DECISIONS.md` names test fakes (`FixedAppraiser`, `EmptyAppraiser`) that were real on the day of that
    decision and are history now, and the design record's §1 names SIBLING projects' types on purpose.
  **The general shape: a gate whose false positives are legitimate authorial choices cannot be tightened
  into usefulness — it can only be given an exclusion list, and an exclusion list nobody can see rot is the
  hole `check-api-vocabulary`'s "an allowance that matches nothing FAILS" rule exists to avoid.** This is
  why `check-docs` is a curated `retiredTerms` registry (a decision retired this word, say that instead)
  rather than an existence check: the registry only ever contains claims someone deliberately settled, so a
  hit is a defect by construction. Reach for a registry, not a corpus scan, whenever "wrong" depends on
  intent rather than on the text.
- **A record's own SUBJECT MATTER is the vocabulary a gate over it wants to scan for — so the scan reads <!-- trap: sub=gates,measurement shape=vacuous,wrong-subject -->
  as full of defects and holds none.** The second measured instance of the entry above, and the sharper
  one, because here the words genuinely belong. `check-measurements` was specified to fail a result reading
  CURRENT whose body matched `CORRECT(ED|ION)|RETRACT|STALE|SUPERSED`. Built and run over the real record
  (2026-09-10): **63 hits across 18 results, ZERO defects.** `stale@k` and `current@k` are METRIC NAMES
  there, "the superseded fact" is the PHENOMENON the knowledge-update workload measures, and `correction`
  is a corpus class in `memory-density` — a document about supersession is made of the word. Restricting
  the match to CAPITALS did not rescue it: the survivors were a table row reading `| the SUPERSEDED fact |`
  and a heading asking *"Does a CORRECTION separate from a RECURRENCE?"*. **What worked was narrowing the
  SUBJECT, not the pattern** — the same vocabulary over section HEADINGS only, which are short authored
  claims rather than prose: 5 of 57 flagged, 4 genuine. **Before writing a vocabulary gate, run it and
  COUNT the defects**, because a scan with a 0% hit rate is indistinguishable from a strict one until
  somebody looks, and shipping it teaches the next maintainer to reach for the escape token.
- **A `<!-- marker: … -->` whose value contains `>` matches NOTHING — the row does not fail, it VANISHES.** <!-- trap: sub=gates,docs shape=silent-loss -->
  `_markers.mjs`' pattern excludes `>` on purpose, so it cannot run past its own `-->`; the cost is that a
  value like `arm="best threshold >= 6"` makes the whole marker unmatched rather than malformed, and every
  check that iterates markers simply never sees it. Measured on `check-measurements`' first real run: one
  result disappeared from a generated index, and the ONLY thing that noticed was the closed-vocabulary rule
  reporting its metric as unused — had the metric been a common one, the row would have been silently
  absent from the index that is the whole point of the file. Any gate on this seam must tell "no marker
  here" from "a marker too broken to match": test for the OPENER (`<!-- name:`) as well as the full
  pattern, and report the difference.
- **A gate weakened by a regex LOOKAHEAD has no expiry — worse than the `drift-ok` it was built to <!-- trap: sub=gates shape=scope-blind -->
  avoid.** Found 2026-08-31 narrowing `retiredTerms` to silence seven accurate past-tense code comments
  (`` `SubjectSeedK`'s default of 5 ``, `` `SemanticSeedK` defaulted to 0 ``): <!-- drift-ok: names the two retired identifiers the withdrawn lookahead actually excluded --> a trailing negative lookahead
  excluded any hit followed by a VALUE-STATEMENT shape (`default of N`, `defaulted to N`, `= N`) within 20
  characters. That also silently excluded `SemanticSeedK = 30` and `defaulted to 5` — <!-- drift-ok: the reintroduction shape the withdrawn lookahead let through --> the exact shape a
  REINTRODUCED stale doc takes — in every file the gate scans, permanently, not just the seven lines it was
  written for. **It is worse than an explicit escape because `retiredApiNames`' allowances FAIL the moment
  they stop matching (self-correcting), while a lookahead has nothing to expire** — documenting the
  trade-off inline described the hole without closing it. Withdrawn the same day: reworded the seven
  comments instead (none needed an escape) and restored the plain alternation. **The general shape: when a
  gate's false positives all share one CONCRETE textual pattern, exclude those exact LINES (an allowance,
  self-expiring) rather than the pattern that makes them false positives (a lookahead, permanent)** — the
  choice `check-api-vocabulary`'s allowance list already made correctly.
- **Scope a doc gate by "is this maintained state?", not by directory.** The same gate omitted the two <!-- trap: sub=gates shape=scope-blind -->
  repo-root files, `CLAUDE.md` and `TASKS.md`, purely because the scope test was three `startsWith` calls.
  Both are maintained state and `CLAUDE.md` is the highest-leverage document in the repo — a stale claim
  there is read by the next session *before it reads anything else*. Adding them found real drift on the
  first run. `docs/task-archive.md` stays excluded on purpose: it is accurate BY using the vocabulary of its
  day. **`CHANGELOG.md` is only HALF that**, and the split cost real drift — see the next entry.
- **Two pieces of prose SHIP to consumers and no gate reads either: the packaged `README.md` and every <!-- trap: sub=docs,gates shape=scope-blind,stale-claim -->
  csproj `<Description>`.** Found 2026-08-17, auditing 3.0 before the stamp. `PackageReadmeFile` in
  `src/Directory.Build.props` packs the repo README into **every** package, so it is what nuget.org renders
  on every package's page — and it named an untracked `local/superpowers/records/…` path three times,
  a link no consumer can follow, because `check-links` skips `local/**` by design (correctly: those records
  are untracked). The `<Description>`s are worse off still — `check-packages` asserts one EXISTS and never
  reads it, and `check-docs`' `CODE_IN_SCOPE` is `.cs`/`.mjs` only — so `Lyntai.Bundle`'s blurb was still
  justifying its exclusions with "an unverified surface", the reason **D69/D70 retired** for
  `Lyntai.Generation` a release earlier.
  <br>**The general shape, and it is the reason this is filed rather than just fixed:** a gate's scope is
  drawn around *what the repository maintains*, and these two live outside that line while being the most
  widely READ prose the project publishes. **Before a release, re-read the README as a consumer who has only
  the package** — no clone, no `local/`, no git history — and read every `<Description>` against the
  decisions since the last release. Both are cheap; neither is automatic.
  <br>**A third member of that family, measured 2026-08-21: a FORWARD version reference in an XML doc is a
  prediction, and it ships.** Two sentences said a fix arrives in "3.1" — reasonable, since the change was
  additive public surface; the release was cut as **3.0.1**, so the published package's IntelliSense named a
  version that does not exist. **The interesting half is that no gate can see it**: `check-docs` gates retired
  VOCABULARY, `check-counts` gates counted CLAIMS, and a version number is neither — nor can a corpus scan
  become one, because `net10.0`, `llama3.1` and every historical `## 3.0.0` heading are the same token shape.
  The rule is `repo-mechanics.md` §"never NAME a version that has not shipped"; what belongs *here* is that
  the correction repeated the mistake in a different costume an hour later, in the ROADMAP row recording the
  release — so treat "is this number a record or a prediction?" as a review question, not a thing you fix once.
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
  reports green while the defect it was written for is live.** Measured 2026-09-10, designing a predicate
  for **D29** ("Lyntai disposes only what Lyntai created"). The proposal asserted the literals the wiring
  passes — `disposeHttpClient: !byo` at each registration. Mutating `var byo = httpClient is not null;` to
  `is null`, one token and twenty lines away, leaves **every asserted literal intact**: the gate stays
  green while every host-supplied client is disposed on its way out of the first call and every
  Lyntai-created one leaks — precisely the "cannot access a disposed object on the SECOND call" the
  decision exists to prevent. **The predicate was checking spelling, not polarity.**
  <br>**Two rules, and the second is the general one.** Assert the polarity at its SOURCE, not at the call
  sites that read it. And **a claim about BEHAVIOUR wants a behavioural test, not a text scan** — register a
  BYO client, drive two calls, assert the second does not throw. That is decidable and rename-proof, where
  a regex over call sites is neither.
  <br>**Frozen counts are the same failure wearing a different hat**: "all 11 lease sites" turns red when a
  twelfth correct backend arrives, so a predicate must be UNIVERSALLY QUANTIFIED over whatever it finds
  ("every lease site passes the flag"), never an equality on how many it found today.
- **`git ls-files` (and `diff --name-only`) C-QUOTE any path containing a non-ASCII byte** — `docs/灵台.md` <!-- link-ok: a guard FIXTURE's name, never a file here --> <!-- trap: sub=gates,git shape=fail-open,scope-blind -->
  comes back as the literal 8-character-escaped string `"docs/\347\201\265\345\217\260.md"`, which matches no
  file on disk. **Always pass `-z` and split on NUL.** Measured 2026-08-11 in `check-sensitive`, where the
  failure was silent AND permissive: the quoted name failed `readFileSync` with `ENOENT`, the ENOENT branch
  classifies that as "a tracked file deleted from the working tree" (a legitimate mid-refactor state), so the
  file was skipped, the leak inside it was never scanned, and the run printed `clean`. `check-docs` had the
  identical bug with an even quieter ending — a bare `catch { continue; }`. This repository is named 灵台, so
  a CJK-named document is not a hypothetical. The general shape: **a scanner that gets its file list from one
  tool and its bytes from another must agree with both about how a name is spelled**, and the disagreement
  shows up first in the paths nobody tests with.
- **A doc gate scoped by FILE TYPE leaves the identical defect alive in every other tier, and its own <!-- trap: sub=gates shape=scope-blind -->
  exclusion rationale is where to look for the hole.** `check-links` was added 2026-08-14 because six
  references to an archived document survived in maintained markdown. It scans `.md` and nothing else — so on
  the day it went green, **seven more of the same dead references were alive in `src/` and `tests/`**, two of
  them inside XML documentation that ships to consumers, plus a second archived document nobody had swept at
  all. The gate's own header defends excluding `src/` on the grounds that "the compiler already gates their
  crefs", and that is the tell: **the compiler resolves `<see cref>` and NOTHING else.** A type name in a
  `<c>` tag, a file path in a `<c>` tag, a test name in a `//` comment — all prose, all unchecked, and all
  places this repository's XML docs habitually put load-bearing references (`MemoryLanguageSweep` cited a test
  that had been renamed away, twice, while calling five corpus arms "the two arms").
  <br>Two things generalise. **(1) When a gate's scope is narrower than the defect, say which tiers are
  UNCOVERED in its header** — "maintained markdown" reads like "everywhere that matters" until someone counts.
  **(2) An exclusion justified by "another mechanism already covers it" is a claim about that other mechanism,
  and it should name exactly what it covers.** "The compiler gates crefs" was true and load-bearing in the
  half nobody checked.
- **`.gitignore` does not untrack an ALREADY-TRACKED file, so `git mv`-ing a document into an ignored <!-- trap: sub=git shape=stale-claim -->
  directory leaves it tracked — and every downstream claim that it is untracked silently becomes false.**
  Measured 2026-08-14 while squashing: of **29** files under `local/`, exactly **one** is tracked —
  `local/superpowers/records/2026-08-09-memory-policy-measurement.md`, the record D43 archived out of `docs/`.
  It was moved rather than removed-and-recreated, and `.gitignore`'s `local/` has no effect on a path git
  already has in the index. Nothing failed, and no gate could notice: the file is out of `check-docs`' scope
  because `IN_SCOPE` never lists `local/`, not because it is untracked.
  <br>**The cost is the three claims it falsifies**, all in `docs/superpowers/INDEX.md` and all load-bearing
  for how people treat that directory: "a fresh clone does not carry these files" (it does), "`check-docs` no
  longer gates them (it scans tracked files only)" — the right conclusion via a mechanism that is not the one
  operating — and "nothing was destroyed, only untracked" (it was not untracked). A reader who believes
  `local/` is unpublished is one `git mv` away from publishing something they wrote there on that belief;
  `local/sensitive-patterns.txt` lives in the same directory, and its whole premise is that the directory
  does not ship.
  <br>**It stayed tracked for two days after this entry was written, and the entry said otherwise.** It had
  closed with "Verified clean today — that file is untracked" while `git ls-files local/` returned it. So the
  entry describing the trap had itself fallen into the half it warns about: the escape was recorded, the
  remedy was not applied, and the closing sentence asserted it had been. **A "verified" claim with no gate
  behind it decays exactly like any other counted claim** — which is the argument for `check-counts`, one
  tier up, applied to a boolean. That is the durable half of this incident, and it is why the fix below is
  reported as an assertion rather than as a claim.
  <br>**Resolved 2026-08-16** on the owner's call: `git rm --cached` (never a path change), and
  `git ls-files local/` is now empty — which is the assertion, not "verified clean". The three claims in
  `docs/superpowers/INDEX.md` are true again. The content is not destroyed, only untracked, and the INDEX
  records how to retrieve it from history; that it leaves every other clone is D43's intended trade-off
  ("a fresh clone does not carry these files"), which is exactly why it was the owner's call and not a
  tidy-up. The TRAP is permanent regardless — `.gitignore` still has no effect on a path already in the
  index, so the next `git mv` into an ignored directory repeats it.
  <br>The general shape, and it applies to any "move it out of the way" procedure: **untracking is an
  explicit `git rm --cached`, never a side effect of a path change.** Assert it (`git ls-files <dir>` should
  be empty) rather than inferring it from the ignore rule, because the ignore rule is not what decides.
- **A gate scoped by `git ls-files` is blind to the code most likely to need it — the file you just wrote — <!-- trap: sub=gates,git shape=scope-blind -->
  and it reports clean while doing so.** `git ls-files` lists the INDEX, so a file that is neither committed
  nor `git add`ed does not appear. The ordinary workflow is *write → `verify` → commit*, which means `verify`
  scans everything EXCEPT the new work. Measured 2026-08-23: a 36-line comment block in a brand-new bench file
  passed two full `verify` runs and the individual gate twice, then failed the moment its file was committed,
  **byte-identical**. The first instinct was that the gate was non-deterministic, which is the wrong and much
  more alarming conclusion.
  <br>Ignored paths (`local/`, `devtools/_*`, `bin`/`obj`) stay out on their own via
  `--others --exclude-standard`, which is exactly what makes index-only look sufficient: the reason to scan
  untracked files is new SOURCE, and the reason not to is scratch — and git already distinguishes them.
  <br>**The wider finding:** the scope rule was written FIVE times privately (`check-comments`,
  `check-counts`, `check-docs`, `check-encoding`, `check-links`) plus `check-sensitive`'s own two-mode
  `sources()`, so the blind spot was six-fold and fixing one fixed one. That is the same "one rule, N copies"
  shape this document records for `salience` coercion, applied to gate SCOPE — where the divergence is
  invisible because every copy reports the same green line. Closed by hoisting to one
  `devtools/scripts/_repo-files.mjs`; see `docs/task-archive.md` Part 96, including the miscount worth
  knowing (`check-samples` looked like a seventh copy and was an IMPORTER of `check-docs`').
  <br>**When you write a gate, ask what its file list EXCLUDES and whether that set contains the thing it
  exists to catch.** Then prove it with an untracked probe rather than by reading the glob — this one was
  confirmed by dropping a 30-line comment block into an unstaged file and watching the gate fail.
- **A gate that enumerates a directory must tolerate the directory being absent.** `check-packages` threw a <!-- trap: sub=gates shape=silent-loss -->
  raw `ENOENT` stack trace from `readdirSync(Baselines/)` when the last baseline was deleted, instead of
  reporting the per-package "no API baseline" problems it had already collected. It failed CLOSED, so nothing
  shipped wrongly — but the operator is shown a stack trace naming no package, which is the report they
  needed. Found 2026-08-11 by the test that deletes the last baseline.
- **The release workflow builds from the REMOTE, so "finished" and "shipped" are different states and only <!-- trap: sub=git shape=wrong-subject,silent-loss -->
  one of them is pushed.** Measured 2026-08-05: **v2.2.0 was cut without the whole-library review that had
  been finished for it** — three commits were sitting locally, the workflow built what the remote had, and
  the release reported complete success. Nothing failed, nothing warned, and the missing work was invisible
  until someone compared the tag against the local branch. **Push before triggering a release**, and treat
  "the work is done" and "the work is on the remote" as two separate claims — the second is the only one a
  release can act on. (Recorded as a decision until 2026-08-14, which was the wrong home: nobody CHOSE this
  behaviour, it was discovered — so it lives here rather than in the decision record.)
- **A backlog item amended IN PLACE does not amend the summary that points at it, and the amendment is <!-- trap: sub=docs shape=stale-claim -->
  exactly when the summary goes stale.** `TASKS.md`'s startable-set banner has now advertised finished work
  **four** times (2026-08-26, 2026-08-28, and twice on 2026-08-29). The mechanism is the same every time and
  it is not carelessness: a session runs a sweep, writes the result into the item's own prose, and the item
  is where the banner's claim came from — so the banner is stale the moment the item improves, and re-reading
  the item is exactly the check that fails, because the item is what changed.
  <br>**The 2026-08-29 pair is the proof, because the second was a CORRECTION of the first and repeated it.**
  The banner named a `many-candidates` paired sweep that had run the day before; the fix re-read Part 65's
  prose, found the sentence "the remaining one-factor sweep is `NoveltyWeight`", and advertised that instead
  — and `NoveltyWeight` had also already run, in the very commit that added the sentence. It was committed
  and every gate was green. **So the check is not "re-read the entry", it is "ask the INSTRUMENT"**:
  `docs/memory-measurements.md` §5 and `docs/task-archive.md` record what has actually run, and neither is written by the
  person amending the backlog.
  <br>**Do not build the obvious gate — this one is the shape that cannot be tightened.** Three forms were
  considered against the four real instances. Scanning open `- [ ]` items for a self-closing phrase
  ("CLOSED as", "LANDED", "RAN on") catches one of four and fires on legitimate prose, because items here
  correctly report a closed HALF of themselves (*"the OVERFLOW half of this item is closed"*, *"the BLOCKING
  half is gone"*). Requiring every Part named in the banner to own an open, unblocked `- [ ]` also catches
  one of four. Deriving the banner mechanically loses the judgement it exists to carry (*"Part 99 is a WATCH
  item and not startable work"* is not computable from a checkbox). That is the *"false positives are
  legitimate authorial choices"* shape recorded above for the existence-check gate: **reach for a registry or
  a habit, not a corpus scan, when "wrong" depends on intent.**
  <br>**The REGISTRY was built on 2026-09-10 (`docs/DECISIONS.md` D111), and all three refuted forms above
  stay refuted** — they are the same mistake, which is INFERRING a state from a checkbox. What changed is
  that the judgement is now supplied as data: each open `- [ ]` carries
  `<!-- item: state=… kind=… needs="…" -->`, `check-backlog` generates the roster from those markers, and an
  unmarked item FAILS rather than being given a default. So *"Part 99 is a WATCH item"* is still not
  computable — it is now written down, once, on the item, where amending the item amends it.
  <br>**The general shape, and it is the reusable half: when a summary keeps going stale, ask whether the
  thing it summarizes can be made to CARRY the answer.** Deriving is impossible and habits lose; a field on
  the source is neither. The cost is that the field is another thing to get right — which is why the gate
  rejects an unknown state, a blocker with no kind, and a startable item that names one anyway.

- **A parser that SCRAPES validates what it matched and is structurally blind to what it skipped, so the <!-- trap: sub=gates shape=silent-loss,scope-blind -->
  half it dropped is unreportable.** Measured 2026-09-10 by an adversarial review of the item markers in
  the entry above, hours after that gate went green. It collected `matchAll` of a `key=value` pattern into
  a map and then validated the MAP — unknown key, unknown value, a blocker missing its kind, a startable
  item carrying one. Omit the quotes on one value (`needs=a real key and a download`, an easy slip in a
  hand-written HTML comment) and it parses as `needs="a"`, satisfies every one of those rules, and the
  generator publishes a one-word blocker while printing a green line. **The check that looks like it should
  have caught it cannot**: the residue contains no `=`, so it is not an unknown attribute either.
  <br>**The general shape: when a parser scrapes rather than consumes, assert that the matches COVER the
  input.** A tokenizer that must account for every byte fails on garbage; a `matchAll` that harvests the
  interesting spans reports success over any input holding at least one of them. The fix is four lines —
  accumulate the gaps between matches, fail on a non-blank residue — and it is worth reaching for before
  the first time a value legitimately contains a space.
  <br>**The tell that this class is dangerous rather than merely wrong is that the damage READS AS DATA.**
  A truncation to `codex-cli` looks like a terse complete answer, not like loss, so nothing downstream —
  human or gate — has a reason to doubt it. Compare the noisier failures this file usually records, which
  announce themselves as soon as anybody looks.

- **A TOGGLE is not a boundary until something asserts it balanced, and a scanner that skips to the end of <!-- trap: sub=gates shape=scope-blind,silent-loss -->
  the file reports a clean run over the part it stopped reading.** Measured 2026-09-10 on this very
  document, by an adversarial review of the gate that indexes it. `check-pitfalls` flipped a `fenced` flag
  on each ```` ``` ```` line so a captured block would not be mistaken for prose, and never checked the
  flag at EOF. **One forgotten closing fence took 157 traps to 131**; the gate's only complaint was that
  the index was "STALE", the remedy its own help text prescribes is `--write`, and that published
  `131 traps` and exited 0. Every run after it was green — **including one with an unfiled trap sitting in
  the suppressed tail**. Three of the gate's four checks were voided by a one-line prose edit to the file
  it guards.
  <br>**The same review found the twin defect, and it is the more general one: TWO derivations of one
  boundary drift, and they drift permissively.** The parser decided where the generated block was by its
  own scan while the splicer used `blockRange`, so they disagreed about what counted as an anchor — the
  scan armed on ANY line carrying the begin prefix, the splicer took the FIRST. An anchor quoted lower in
  the file therefore suppressed everything below it, and a duplicate ABOVE the real one made `--write`
  delete the document's intro paragraph and exit 0. Fixed by deleting the second derivation: the parser
  now calls the same `blockRange` the splicer does, so they cannot disagree.
  <br>**Two rules, and the first is the cheap one.** *Assert the toggle balanced where the scan ends* — a
  four-line check that turns a silent truncation into a named line number. And *never let a delimiter be
  computed twice*: this repository already records that shape for gate SCOPE (six copies of one rule, every
  copy reporting the same green line), and it is the same defect one layer down.
  <br>**What makes this class nastier than an ordinary scanner bug: the gate WRITES.** A read-only check
  that under-scans merely fails to catch something. One that regenerates an index publishes the truncated
  read as the authoritative answer, so the next reader is told there are 131 traps by the mechanism whose
  entire job is to know there are 157. **A generator must fail closed on anything that could have narrowed
  its input**, which is a stronger bar than a checker needs.
- **Before optimizing against a latency number, measure the INSTRUMENT's noise — a p50 that moves less than <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  its own run-to-run spread has told you nothing.** Measured 2026-08-29 on `memory-scale`: a recall's
  co-activation write went from ten store round-trips to one (**D99**), the 10k p50 read `11.0ms` before and
  `8.9ms` after, and a second run of the *identical post-change code* read `11.2ms`. The "19% improvement"
  was noise, and it would have been published as a result by anyone who ran the before/after pair once —
  which is the normal way to run a before/after pair.
  <br>**The cheap defence is a repeat of the AFTER arm, not a bigger sample.** One extra run costs the same
  as the one already planned and bounds the noise directly; chasing significance with more repetitions costs
  far more and answers a question nobody asked. This repository already uses that move elsewhere and for the
  same reason — the LoCoMo isolation fix was confirmed by two byte-identical runs rather than by reasoning
  that the stores were now separate.
  <br>**Then convert the claim to something countable if you can.** The change here really did replace ten
  round-trips with one; that is a fact about the code, checkable by a test that counts calls, and it stays
  true on a machine whose milliseconds differ. A COUNT is gate-able where a millisecond is not — which is
  the same reasoning `check-counts` rests on, applied to a benchmark instead of to prose.
- **A FAIL-OPEN seam is indistinguishable from one that agreed with you, so an arm measuring it must count <!-- trap: sub=measurement,memory shape=fail-open -->
  how often it actually FIRED.** `IMemoryVerificationPolicy` returns `NoOpinion` on every failure — refusal,
  timeout, unparseable reply, an invented id — and `NoOpinion` leaves the ranking untouched. So a judge arm
  that scores its own base has two readings that no score column can separate: *the judge endorsed what
  already led* (a result about the corpus) and *the judge never answered once* (a broken arm), and the
  second is the one that gets published as "the seam is not worth it". Measured 2026-09-03 on the LoCoMo
  judge arm, where the counter was added BEFORE the number was believed: 0 of 200 calls declined, which is
  what makes "the model answered and was wrong" a claim rather than a hope.
  <br>**The rule generalises past this seam to every best-effort one this library ships** — an annotator, a
  verifier, anything whose contract says *degrade to the model-free floor*. The floor is a correct
  behaviour and a terrible observable: it is silent, it is the same shape as success, and the arm still
  produces a full table. **Count the fires, not just the outcome**, and put the count in the output beside
  the score.
  <br>**The lucky direction here was that the arm moved.** It scored 10.5 points BELOW its base, which is
  unreachable by a judge that declined — so the wiring was provable after the fact. Had the model been
  mediocre instead of wrong, the arm would have landed on its base and the honest reading would have been
  unavailable, permanently, from that run's data.

- **A control counter is contaminated by any SETUP that exercises the thing it counts, and the <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  contamination is invisible because the number stays PLAUSIBLE.** Two instances in one bench
  (`MemoryContentionSweep`). `CountingAnnotation`'s `SubjectsPerWrite` counted `SeedAsync`'s own untimed
  writes until a `Reset()` call after seeding fixed it — without it, the TIMED region's control read the
  union of setup plus work, not either alone. The identical shape recurred one control over:
  `CrossEncoderReranker.ReachableAsync()` scores two fixed probe sentences on the SAME instance that becomes
  `Rig.Reranker`, so `DistinctRerankScores` read **2 under the `judge` backend** — which never calls the
  reranker at all — confirmed empirically against the real `bge-reranker-v2-m3` (`3.6249` and `-11.0215`,
  both distinct). A reader trusting the count would have concluded the reranker discriminated under an arm
  that never invoked it once.
  <br>**The rule: reset the audit after setup, or the control measures setup plus work and can no longer
  refute either.** A contaminated count rarely looks wrong — 2 distinct scores reads exactly like a
  reranker doing a little bit of discriminating, not like a probe leaking through, which is what makes this
  shape survive review. Whenever a counter and the code under test share ONE instance across a setup phase
  and a measured phase, reset the counter at the setup/measured boundary, the same way a stopwatch is
  zeroed before the timed region starts rather than at process start.

- **A model given an UNBOUNDED task stops discriminating, and it reads as the model being too small.** <!-- trap: sub=memory,measurement shape=wrong-subject,fail-open -->
  Measured twice in one session (2026-09-03/04), on two different seams, with the same 4B model:
  · the verification judge endorsed **17% of a 20-item list, 19% of a 40-item one, and 36% of an 80-item
    one**, its lift over chance falling from ~3.2× to 1.74× — so the SHIPPED depth of 4× the recall limit
    is where it stopped judging and started waving things through, costing 10.5 points of evidence-hit;
  · the fact extractor, asked for "the facts" in a turn with no budget, produced **7.1 facts per turn** and
    inflated the corpus 7.1×, which cost 14 points of `current@k` through near-duplicate dilution.
  <br>**Neither is a capability failure.** The judge RANKS well — 34.5% precision at its own top pick
  against a 1.49% base rate — it simply cannot tell where to stop. **The tell is a prompt that asks for a
  SELECTION and states no budget**: "be selective" is not a number, and a model will not invent one.
  <br>**Before concluding a seam needs a bigger model, check what the seam HANDS it** — how many items, how
  long, and whether the instruction bounds the answer. Here the library could not even supply the bound:
  `MemoryVerificationRequest` carries the query and the candidates and NOT the caller's limit, so the policy
  cannot say "at most 20" for a page of 20. **A knob that shapes the input is usually cheaper than a bigger
  model, and it is testable on the model you already have.**
  <br>**TESTED on the extractor 2026-09-04 (`docs/memory-measurements.md` §5), and the prediction held: the inflation was
  the PROMPT.** Adding one line — *at most 2 facts* — took 7.1 facts/turn to 2.1 on the same 4B model, cost
  no evidence (survival stayed 142/142) and recovered 11.4 of the 14.3 points of `current@k` the unbounded
  prompt had lost. **Two rules came out of doing it**, and both generalise to any prompt-level bound:
  **state the budget in the PROMPT and never truncate the reply in code** — a code truncation measures
  truncation, not the model discriminating — and **count how often the model EXCEEDS it**, because "the
  bound did not help" and "the model ignored the bound" are the same score. Here it was 8.3%, which is what
  made "a bounded corpus" a claim rather than an assumption.
  <br>**And the honest other half: a better-shaped input fixed what it was aimed at and nothing else.** The
  same run's `stale@k` ROSE 40.0% → 57.1% — a smaller corpus lets both the current and the superseded fact
  compete — so input shaping bought back the DILUTION and moved the underlying judgement not at all. **Fix
  the input before buying a bigger model; do not expect it to buy a capability the seam never had.**
  <br>**Then the SAME budget was put to the JUDGE and did not bind at all, so this is two cases and not one
  rule** (2026-09-04, `docs/memory-measurements.md` §5). Asked for at most 20 of 80 the model endorsed **34.9 — MORE than
  the 29.1 it endorsed unbudgeted**; asked for at most 5 it endorsed 27.4. **A GENERATIVE task takes a count
  naturally; a SELECTIVE task over a list the model can SEE does not**, because every candidate looks locally
  defensible and a stated number reads as an expectation rather than a cap. **A budget that RAISES the output
  is the tell**, and it is only visible if the counter is there — which is the rule two bullets up, earning
  itself a second time.
  <br>**The expensive half of that is a design consequence, not a curiosity.** The plan was to add the
  caller's limit to `MemoryVerificationRequest` so a policy could say "at most 20" for a page of 20.
  Measured, that exact number is worth **+0.5 points**, while 5 — a number the recall limit would never
  supply — is worth +4.0. **The API change would have shipped the useless arm.** Price the number BEFORE
  building the surface that carries it; "the library cannot even express this" is an argument for measuring
  it bench-side first, never for assuming the expressible value is the valuable one.

- **An arm that is SUPPOSED to move needs a control proving it CAN — the mirror of the arm that cannot.** <!-- trap: sub=measurement shape=vacuous -->
  This repository already requires a structural null control before believing a delta. The other half went
  unwritten until 2026-09-03, and cost two runs. A bench arm replacing the engine's endorsed-first PARTITION
  with a rank FUSION returned scores identical to the partition in every cell, which reads as *"fusing does
  not help"* — and was arithmetically the partition: at the shipped `K = 60` with weight 1, the worst
  endorsed candidate still outscored the best unendorsed one, so the two orderings could never differ. A
  second arm (truncating the verdict to its top 5) was inert for a different reason and looked the same.
  <br>**Both were caught by ONE cheap control: compare the arm's output against the output of the thing it
  replaces, and say so in the report** — here, same-page rate and mean overlap, with an explicit
  `! DEGENERATE` line when they never differ. A score is not evidence that an arm did anything.
  <br>**The modelling error underneath is worth its own sentence, because the fix was in a doc already
  written:** unendorsed candidates were given a judge term of ZERO — treated as *unranked* — when
  `MemoryVerification.RelevantIds` says an unlisted id "is judged NOT to have answered, which is the half
  that carries new information". A "no" is a low RANK, not an absence, and modelling it as absence is what
  made the fusion degenerate.

- **A gate that scans SOURCE must blank comments first, and the false positive is always the code that most <!-- trap: sub=gates shape=wrong-subject -->
  explicitly obeys the rule.** Measured twice in one session (2026-09-04), on two unrelated predicates:
  · a check for reflection `JsonSerializer` in the wire paths (**D14**) flagged the two files whose comments
    read *"JsonDocument.Parse (not JsonSerializer) so the package stays trim/AOT-clean"*;
  · a check for a silent `IsAotCompatible=false` (**D7**) flagged `Lyntai.Generation.csproj`, which does not
    opt out at all — it carries a commented-out TEMPLATE showing what to write if it ever needed to.
  <br>**The mechanism is that prose about a rule quotes the rule's own vocabulary**, so a text scan hits the
  documentation of compliance and the example of violation before it hits any real one. `check-links`
  records the mirror image — an index built from prose lets a citation AUTHORIZE ITSELF — and the fix is the
  same in both directions: **read the code, not what it says about itself.**
  <br>**Blank comment BODIES rather than dropping the lines**, so reported line numbers still point at the
  real file. And the tell that you have this bug is not a red gate — it is a red gate naming a file you
  believe is correct; check whether the hit is inside a comment before you doubt the file.

- **A benchmark arm whose CANDIDATE POOL reaches the size of its store has stopped measuring retrieval, and <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  the tell is a score too good to distrust.** Measured twice, in two benches, and the second time is the
  point: the LongMemEval bench grew a counter after a `fill` arm scored 90% by returning most of a 25-turn
  store — that counter fired and saved the run. The LoCoMo bench had none, so an oracle arm at
  `CandidateMultiplier = 32` (640 candidates against conversations of 369–689 turns, larger than the whole
  store for six of ten) printed **100.0% in every one of four categories** and looked like a breakthrough.
  A perfect judge handed an entire conversation cannot score anything else.
  <br>**A counter that lives in one bench does not protect the other.** Both benches build arms from the same
  `FieldArms` registry and share the same failure mode, and the fix had to be written twice because it was
  filed as one bench's instrument rather than as a property of pooled retrieval. **When a guard catches
  something structural, ask which other harness has the same structure.**
  <br>**The tell is not implausibility, it is UNIFORMITY** — four independent categories reading exactly
  100.0% is not what retrieval does, and a refuted prediction landing on *too good* deserves more suspicion
  than one landing on *too bad*. Compare the pool against the store and print it; `pool >= store` is a
  one-line check that no score column can express.

- **An offline REPLICA of a shipped algorithm must be proven to reproduce it, and the difference that breaks <!-- trap: sub=measurement shape=wrong-subject,unmeasured -->
  it is never the interesting part of the algorithm.** Measured 2026-08-29 (`docs/task-archive.md` Part 114):
  a ladder scoring RRF outside the engine got the ranking right and the TIE-BREAK wrong —
  `MemoryRankingContract.Finish` breaks score ties by DESCENDING id, so the newer entry wins, and a replica
  breaking them ascending **moved the shipped row by 4 points while looking entirely plausible.**
  <br>**So the control is "reproduce the shipped policy's own output", not "look right"**: agreement on the
  real top-k, per sample, printed. A replica exists precisely where instrumenting the real path is
  inconvenient, which is also where nobody notices it has drifted — and a wrong replica does not fail, it
  publishes a table.
  <br>**Second instance, 2026-09-06 (`docs/task-archive.md` Part 157), and it cost 30 points on a smoke
  sample.** A bench-side character budget cut the body by stopping at the first item that did not fit;
  `GraphMemoryEngine`'s own `MemoryQuery.CharBudget` **skips** that item and keeps filling, and never returns
  empty. On the walk's second step the head of the list is the same entries upgraded from headline to full
  content, so a prefix rule threw away every cheap headline behind them and the arm read **0.0%** where the
  shipped rule reads 30.0%. **Both rules are one loop over a list with a running total** — which is exactly
  why nobody re-reads the shipped one. The tell was an arm scoring zero, and it could as easily have been a
  plausible number. **Open the shipped implementation and diff the loop, even when the rule fits in a
  sentence**; "whole items until the budget runs out" describes both.

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
  harmful for the real one.** This repository prices model-in-the-loop seams against an ideal first — a
  perfect annotator, a perfect judge — which is a good habit for deciding whether to spend a model run, and
  a trap for anything you tune while you are there. Measured 2026-09-03:
  `GraphMemoryOptions.DefaultVerificationDepthFactor` sits at 4 because rescue depth SATURATES, which is
  true and was measured with an ORACLE — and an oracle never endorses junk, so for it depth is free and the
  only question is how far down an answer can be rescued from. For a real judge depth is a PRECISION trade,
  and the same 4B model that was level with no judge at 2× cost **10.5 points** at the shipped 4×.
  <br>**The tell is a doc sentence naming the measurement without naming the instrument** — *"the MEASURED
  saturation point, not a round number"* was accurate and omitted the one word (*oracle*) that bounds it.
  So: **when a default is justified by a measurement, record what stood in for the missing component**, and
  treat every constant fitted beside an ideal as unmeasured for the real one. The ideal run is a ceiling on
  the MECHANISM; it is not a fit for the KNOB.

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
  `namespace Lyntai.Generation;` silently redefines the real `GenerationRequest` for every other sample
  compiled alongside it, which then typecheck against the doc's copy instead of the shipped one — green,
  and meaningless. `check-samples` compiles any block declaring a namespace under the library root in its
  OWN compilation for this reason. The shape to remember: **whenever you compile untrusted-ish source
  beside real references, ask what that source is allowed to SHADOW.**
- **Splicing lines into a CRLF file with `lines.join('\n')` leaves the inserted lines lone-LF**, because <!-- trap: sub=encoding shape=silent-loss -->
  splitting on `'\n'` leaves each original line's `\r` attached to its own end. `git` says only
  *"LF will be replaced by CRLF the next time Git touches it"* — a warning that reads like routine
  autocrlf noise — and the content diff looks perfect. Measured 2026-08-11 inserting 28 `compile-skip`
  markers. Detect and repair with a codepoint check (`s.replace(/(?<!\r)\n/g, '\r\n')`), never by eye.
  <br>**Unreachable on a tracked file here since 2026-08-28**: the working tree is LF (`docs/DECISIONS.md`
  D95), so there is no `\r` left to strand. It still applies to an untracked file, to one a tool has written
  as CRLF since the last checkout, and in any repository that has not declared a convention.
- **A working tree that is CRLF against an LF index inflates every diff, and git's stat cache hides it until <!-- trap: sub=encoding,git shape=scope-blind,resource -->
  a file is touched.** **CLOSED here on 2026-08-28** by a tracked `.gitattributes` (`* text=auto eol=lf`,
  `docs/DECISIONS.md` D95): checkin normalization means a CRLF or mixed working file can no longer reach the
  index, and `git ls-files --eol` now reads `i/lf w/lf` on every tracked file. The entry stays because the
  trap is invisible by construction and recurs in any repository that has not declared a convention.
  <br>**What it looked like here.** About four in five tracked files read `i/lf w/crlf`, plus a handful of
  `i/lf w/mixed`, and nothing reported it — git compares size and mtime before content, so the state
  surfaced only when a file was touched, and then the WHOLE file surfaced as changed. `core.autocrlf` was
  `false` at REPO scope, overriding `true` at both system and global scope, so the index took the tree
  verbatim and a flipped file COMMITTED as CRLF. It bit twice on one branch: a **1267 / 1063** diff for a
  real 204-line change, and a **100-line** diff for a 6-line csproj addition, both caught by a person after
  the fact.
  <br>**The figure above is a ratio because the count was wrong once.** An earlier version of this entry
  published absolute numbers measured before that same session's own repairs, and they did not reproduce —
  the count is a property of the WORKING TREE at a moment and drops by one every time somebody repairs a
  file.
  <br>**Where no convention is declared, the check is `git diff --stat` against
  `git diff --ignore-cr-at-eol --stat` after every edit**, then `git ls-files --eol` to confirm `w/lf`.
  Repair with a bytes replace (`b.replace(b'\r\n', b'\n')`), never with a PowerShell round-trip
  (`windows-machine.md` §Text and encoding). A TOOL can flip a file you did not hand-edit — `dev.mjs
  decisions-index` rewrote `docs/DECISIONS.md` as CRLF in one session — which under a declared convention
  costs a locally-mixed file rather than a bad commit.
- **A claim about what a COMMAND does is testable in seconds, and guessing it is how four wrong sentences <!-- trap: sub=docs,git shape=unmeasured -->
  reached a decision record in one sitting.** Measured 2026-08-28 while declaring the line-ending convention
  (`docs/task-archive.md` Part 106, `docs/DECISIONS.md` D95). All four were plausible, all four were about
  git's own behaviour, and each fell to a single command: `checkout-index -a -f` does **not** rewrite an
  up-to-date file; a stray CRLF file **is** reported by `git status`; it **does** survive `git checkout --`,
  but only once a `git add` has refreshed the stat cache, and not before; and `eol=lf` is **not** redundant
  with `text=auto` — under `core.autocrlf=true` they check out CRLF and LF respectively, which turned out to
  be the decision's actual justification rather than a detail.
  <br>**The tell is the sentence shape**: *"`-f` forces …"*, *"git would see it as unchanged"*, *"so it
  heals on the next checkout"* — a claim about observable behaviour, in the present tense, that no command
  in the transcript produced. Three of the four were caught by re-reading prose already written, not by any
  gate. **Reading the manual is not the fix**: two of the four are consistent with a fast reading of
  `gitattributes(5)` and still wrong in context, because the behaviour depends on state the page does not
  know about (here, a per-clone config and the stat cache). Run the command against the state you actually
  have. This is the mechanism-shaped sibling of the numeric-provenance entry below, and it is filed
  separately because the remedy differs: that one says *name where the number came from*, this one says
  *the claim is an experiment, so run it*.
- **`check-warnings` reports "build FAILED" for a build that SUCCEEDED once the build log outgrows Node's <!-- trap: sub=gates,build shape=resource,silent-loss -->
  1 MiB `spawnSync` buffer.** Measured 2026-08-09 adding the memory policy sweep: a single
  `ProjectReference` from `bench/Lyntai.Benchmarks` to `Lyntai.Tests` dragged Postgres, Testcontainers, MCP,
  ExtensionsAi, Generation and xunit into the bench build, and the full-solution `-v normal` log hit
  **1,049,602 bytes** — just over the cap. Node returns `ENOBUFS`, the script sees no output, and the gate
  announces a failure that did not happen.
  **This is the worst class of defect this repo has** — not a gate that misses something, but a gate that
  LIES, in the direction that trains a reader to ignore it. It is the `windows-machine.md` §Scripts and
  exit codes trap wearing a different hat, and it will recur for *any* future change that grows the build log,
  with a message pointing nowhere near the cause.
  Two takeaways: **a `ProjectReference` to the test project pulls its ENTIRE dependency graph** into
  whatever references it — prefer `<Compile Include>` links for the handful of files you actually need; and
  when a gate reports a failure whose detail is empty or truncated, **suspect the harness before the build**.
  A `--verbosity` reduction or an explicit `maxBuffer` on the spawn would close it at the root.
- **A classifier written from a vocabulary list is blind to MODIFIERS on that vocabulary, and it fails <!-- trap: sub=gates,docs shape=fail-open,silent-loss -->
  silently because every item still lands somewhere.** Measured 2026-08-16, in the release workflow's
  notes generator. It matched `^feat(scope)?:` and `^fix(scope)?:` and dropped
  `^(chore|docs|refactor|…)(scope)?:` — a complete-looking vocabulary that omits conventional commits'
  one modifier, the BREAKING `!`. So `feat(memory)!:` matched no rule and fell to the catch-all bucket,
  titled "Other changes": **29 commits of history, and 11 of 11 in the v2.5.0..3.0 range.** A major release
  whose entire story is breaking changes was one run away from publishing "New features: 1".
  <br>**The tell is a catch-all that never looks wrong.** Nothing errored, nothing was dropped, no count
  disagreed — every commit appeared in the output, under a heading a reader would skim. Contrast a
  classifier that throws on an unrecognized token, where the same defect is a build failure. **When a
  classifier has an "everything else" branch, the question is not "does it run?" but "what is landing in
  the catch-all, and does that list look like the name on it?"**
  <br>Its sharpest form here: plain `refactor:` is *dropped* as non-user-facing, so a breaking refactor —
  the single most important line a consumer can read — was one regex away from being deleted rather than
  merely misfiled. The only reason it survived is that the drop pattern *also* failed to match the `!`.
  **Correctness rested on a second rule failing**, which is not a property anybody can maintain.
  <br>The fix is the shape this repo already uses for gates: a pure function in
  `devtools/scripts/release-notes.mjs`, tested by `test-devtools`, with the workflow a thin caller — and
  the rule pinned by a test over the REAL commit log, as a property ("no breaking commit lands in Other")
  rather than a count, since a count there fails on the next commit.
- **The defects in a measurement write-up are almost never in the MEASUREMENT — they are in the prose about <!-- trap: sub=docs,measurement shape=unmeasured,stale-claim -->
  it, and every text gate is structurally blind to them.** Measured 2026-08-28 over one sweep's report, which
  took **five review rounds**. The code was correct throughout: all 44 result rows byte-identical across four
  independent executions, including captures taken BEFORE the first fix round. What kept failing was the
  sentences around the numbers. They fail in **three distinguishable kinds** — two in that report, and a third
  found while writing this entry — and collapsing them into one is itself a mistake this write-up made twice.
  <br>**Kind 1, provenance: asserting where a number came from instead of checking.** Four instances, and the
  object moved every time while the failure did not.
  · `CLAUDE.md`'s test trio quoted from the session's **auto-loaded context** rather than read from the file —
    auto-loaded context is a snapshot taken before the branch's base commit, not live state, and the number on
    disk was already correct.
  · A retrievability band row **copied from its neighbouring row**, with a false interpretation then built on
    it — the sentence it supported claimed a band "does not move" when it moves *more* than the one cited as
    proof.
  · *"12.7 s on a cold build"* — an in-process figure taken on a **warm** build; nothing in the exercise ever
    measured a cold one.
  · An independent reviewer's own figures **attributed to the wrong clock** (wall clock, when they were
    in-process readings), which also manufactured an agreement that does not hold — against the correct row
    they sit outside the range, so the sentence read as corroboration while being the opposite.
  <br>**Kind 2, summary statistic: reporting an extremum as a central value.** One instance: 6.8 s published as
  the run time when it was the **minimum** of a distribution spanning 6.8–12.7 s on the same host.
  <br>**Kind 3, restatement: a paraphrase of a number is a NEW number, and here the arithmetic was simply
  never done.** One instance, from the round that wrote this entry: `37 ms → 8 905 ms` (an adopting
  application's own reported per-query figures, not reproducible here) was de-quantified into *"three orders
  of magnitude"*, read off the figures' SHAPE rather than divided — it is 240×, so just over **two**.
  Provenance was never in doubt; the source was correct and on the page. **The aggravating condition
  is what generalises: de-quantifying a passage removes the reader's ability to check whatever quantitative
  token SURVIVES it**, so the survivor needs more scrutiny than the figures taken out, not less.
  <br>**Three rules, and they are not the same rule.** (1) *If a number appears, name the command AND the
  conditions that produced it — and if you cannot, do not write it.* Naming the command alone is necessary and
  insufficient: a number can be genuinely produced by a command and still be attributed to the wrong one.
  (2) *Never report an extremum as a central value — give the spread*, and say which instrument produced it
  when more than one is in play (an in-process stopwatch and a wall clock differ here by a startup cost that is
  a property of the host, not of the tooling). (3) *A paraphrase, a rounding or an order-of-magnitude
  restatement is a new number — do the arithmetic rather than reading the figures' shape.*
  <br>**`check-docs`, `check-counts` and `check-links` cannot see any of this**, and not because they are weak:
  the document lived outside the tree they scan, and a count going stale retires no vocabulary and dangles no
  path, so the sentence stays grammatical, plausible and wrong. **Review was the only gate**, which is why the
  finding is filed here rather than as a gate request.
  <br>**The strongest evidence that the trap is real is that writing it up kept producing fresh instances —
  first in the write-up, then in the rounds correcting the write-up.** The same session asserted *"zero code
  defects"* — contradicted by a genuine latent crash its own record documents being fixed in round 2 — and
  twice collapsed that report's five defects into a single kind; a later round asserted a GATE'S SCOPE without
  reading the gate; the CRLF counts in the entry above were published from a measurement taken before that
  same session's own repairs; and Kind 3 above was introduced by the very round that removed the figures it
  misparaphrased. **No running total is given, deliberately**: it moved every round, and a stale count is what
  teaches a reader to stop comparing. A subagent declined to publish the controller's figures because it could
  not source them, **and was right**; that refusal is the behaviour to copy. If you are about to write a
  number you did not just produce, the correct move is to say you cannot source it.

- **A defect filed from a PARTIAL SCAN under-scopes its own fix, and the filing reads as authoritative.** <!-- trap: sub=docs,gates shape=scope-blind,unmeasured -->
  Measured 2026-08-28 (archive Part 107). A backlog entry recorded that `docs/memory.md` §8 had been folded  link-ok
  away and that **four** places still cited it, naming all four with `file:line` precision. The real number
  was **seven across six files**: the entry's `§8` scan missed the RANGE form (`§7–8` — the token is there,
  the string is not), a second hit on a line it had already counted, and both bench-tier files. Acting on
  the entry as written would have left three live dead citations behind a task marked done.
  <br>**The tell is precision without provenance.** Four exact `file:line` references look like the output
  of a tool and were in fact the hits a reader happened to see; nothing in the entry said which command
  produced them, so nothing invited re-running it. **Record the query beside the count** — a count whose
  scan is written down can be re-run and disagreed with, and one that is not can only be believed.
  <br>The generalisation is the one this file already carries from the other direction: a written-down
  finding is not a verified one. Here the finding was true and its SCOPE was wrong, which is worse, because
  a wrong scope survives the fix that was supposed to close it.

- **Verify a guarantee by IMPORTING the authoritative function, never by reimplementing it in the checker.** <!-- trap: sub=gates shape=scope-blind -->
  Same day, while compressing `docs/task-archive.md`. The compression had to preserve every Part number the
  rest of the repository cites, because `check-links` resolves each inbound `` `docs/task-archive.md` Part N ``
  against that file and eliding one turns the reference into "in NEITHER record" — silently, for every
  reference at once. The verification reimplemented "which Parts does this file declare" as
  `/^#{2,3} Part (\d+)/` and reported **two Parts lost**. Both were fine: `declaredParts` in
  `check-links.mjs` also accepts a bullet declaration (`- [x] **Part 41 — …**`), which is how Part 40 is
  written, and Part 64 is a `### Part 64` sub-entry. **The checker was wrong, not the output.**
  <br>Importing the real function turned 19/21 into 21/21 with no change to the data. A reimplementation is
  a second definition of the same rule, and it drifts in whichever direction its author forgot — here
  toward a FALSE ALARM, which is the lucky direction; the same mistake in a permissive direction passes a
  broken compression as safe. `check-links` itself already carries this lesson for scope predicates
  ("imported rather than restated… two copies of that question drift the moment a document is archived").
  <br>**Both mistakes above are the same shape** — a scan that answers a narrower question than the one
  being asked, while reading as though it answered the whole one.
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
- **A community GGUF quant of a RERANKER can be missing its classification head and fail silently.** <!-- trap: sub=measurement shape=fail-open -->
  llama.cpp issue #16407: a conversion that drops `cls.output.weight` still loads and still returns
  scores — they are just wrong. One quant of the same model measured 310 tensors where the working ones
  have 311. **Smoke-test a reranker before trusting a run**: score a known-answer document against known
  distractors and assert the ordering AND that the scores are distinct. A flat or shuffled scorer reads as
  a clean null result, which is the shape `CrossEncoderRerank`'s own `DistinctScores` audit exists to catch.
- **ORDERING plus DISTINCTNESS is NOT enough — an easy fixture passes a reranker that ranks BACKWARDS, and <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  a published REFERENCE PAIR is what separates them.** The entry above prescribes "a known answer against
  known distractors"; measured 2026-09-12, that is too weak, and the screen built from it certified a
  broken model. With one answer plus three unrelated distractors, `ms-marco-MiniLM-L6-v2` Q8_0 passed —
  distinct scores, answer first. Given its OWN model card's pair (*"How many people live in Berlin?"* against
  the population figure and *"Berlin is well known for its museums"*, published `[8.607138, -4.320078]`) it
  scores **−0.093 / −0.078** and ranks the museums sentence FIRST. **Both documents on topic is what makes
  it discriminating**: lexical overlap separates the easy fixture, so a degraded head coasts on it.
  <br>**The magnitude is the second signal and it is free.** A healthy cross-encoder separates that pair by
  UNITS — the control reproduces it at 13.861 against 12.927, a 0.9× ratio. A model returning hundredths is
  not emitting logits any more: `jina-reranker-v1-tiny-en` orders it correctly at **137.8× too little
  spread**, which is degraded-but-not-inverted and must not report the same verdict as either neighbour.
  <br>**DISTINCTNESS IS NOT DISCRIMINATION, and the near-flat case is the dangerous one — not the flat
  one.** Worth stating because the intuition runs the wrong way. A PERFECTLY flat scorer is harmless here:
  `MemoryVerificationRequest.Candidates` arrives *in rank order* (its own XML doc says so), `OrderByDescending`
  is a STABLE sort, so equal scores preserve that order, the endorsed set is the engine's own top-k, and
  under `Partition` promoting it reproduces the engine's ranking. It is a no-op. **A noise-scaled scorer is
  not**: its scores are distinct, so they genuinely REORDER — by noise — and every audit passes them.
  `CrossEncoderRerank`'s `DistinctScores` counts 4 of 4 on a model whose whole spread is 0.094. **Audit the
  SEPARATION against a known pair, never the cardinality of the score set.**
  <br>**The cause is upstream and no reconversion fixes it.** llama.cpp PR **#21729** is `state: open`,
  `merged: false` (opened 2026-04-10): *"token_type_ids were hardcoded to zero and pooling layers were
  discarded during conversion"*. So a BERT cross-encoder loses its pooler in the FILE and its segment
  signal in the RUNTIME — and a cross-encoder needs segments to tell the query from the document. Check
  `tokenizer.ggml.token_type_count`: **2 means the model wants a signal it will not get; 1 means the
  RoBERTa/XLM-R family, which never had segment embeddings and is therefore immune.** That single field
  predicts every result in this row.
- **…and the tensor-NAME check that looks like the cheap version of that smoke test is ARCHITECTURE-BOUND, <!-- trap: sub=measurement shape=wrong-subject -->
  so it condemns working models.** Measured 2026-09-12, having made the mistake: `cls.output.weight` is the
  head name the **`bert`** rerank path uses, and a remote header read (`node devtools/dev.mjs rerank-screen
  --inspect <url>`, an HTTP range request with no download) reported it **ABSENT** on all three independent
  conversions of `jina-reranker-v1-tiny-en`. Three uploaders agreeing is what made it look systematic. It is
  not a defect: **`jina-bert-v2` names its head `cls.weight`/`cls.bias`**, and the model scores 8/8 when
  actually served. **So tensor presence is a POSITIVE signal only — its absence is evidence about your
  vocabulary, not about the file.** The same read did earn its keep in the other direction, flagging two
  conversions that carried `classifier.weight`/`classifier.bias` plus a `bert.pooling_type` override; both
  turned out to be broken, **but not for the predicted reason** — they fail
  `error loading model: bert model needs to define token type count`, a missing metadata KEY, and never
  reach the scoring path at all. Predicting the failure MODE from a header is a third claim on top of the
  other two. **Serve it; that is the experiment, and it costs a 30 MB download.**
- **A BERT-family reranker SILENTLY IGNORES `--ctx-size` above its trained maximum, and the rejection <!-- trap: sub=measurement shape=vacuous,silent-loss -->
  arrives at REQUEST time on a server that started clean.** Measured 2026-09-12 on
  `ms-marco-MiniLM-L6-v2` Q8_0 (25,281,216 B): launched with `--ctx-size 4096`, it loads, reports healthy,
  answers `/v1/models`, ranks a short fixture correctly — and returns
  `400 … input (1221 tokens) is larger than the max context size (512 tokens). skipping` on the first real
  document, because BERT's learned positional embeddings stop at 512 and no flag moves them. **The size
  column cannot see this**: the disqualified model is the SMALLER file. This is the
  *"probe the EXTREME, never the typical"* entry above with the failure moved from the serving
  configuration into the model's own architecture, so re-sizing the server is not a fix and the only
  signal is a long-input probe. Ask a candidate's `max_position_embeddings` before its byte count, and
  prefer an ALiBi/RoPE-based reranker where the candidates are whole entries (**D108**).
- **`general.name` in a community GGUF is a stale template field, and the TENSOR COUNT is the identity <!-- trap: sub=measurement shape=stale-claim -->
  check.** Every `ms-marco-MiniLM` conversion surveyed 2026-09-12 — five uploaders, four different layer
  depths — reports `general.name : Ms Marco MiniLM L 12 v2`, including the L2 and L6 files. Reading it as
  identity says every one of them is the 12-layer model. The counts refute that cleanly and arithmetically:
  **39 tensors for L2, 103 for L6**, i.e. 16 per layer plus 7 fixed, where L12 would be 199. This is the
  `--alias` entry below ("a model NAME means different things on the two local servers") one layer earlier,
  in the FILE rather than at the endpoint — and the same rule closes both: **verify identity by something
  the file computes, never by something it is labelled.**
- **A screen fixture can be too HARD, and that fails WORKING models — the same defect as one too easy, <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  pointed the other way and much easier to be proud of.** Measured 2026-09-12 building `embed-screen`.
  The reranker lesson above ("an easy fixture certifies a broken model") was applied enthusiastically: four
  topic pairs whose within-pair sentences share NO content word while two different pairs share `Moon` and
  `Earth`, asserted as pass/fail. The 333,590,944 B control separated it (+0.1634); **all four sub-100 MB
  candidates conflated it (−0.06 to −0.13) while being demonstrably healthy** — stable vectors, full cosine
  range, correct dimensions, every easy comparison right. Shipped as written, the screen would have
  published *"no sub-100 MB embedder works"*, which is the retracted reranker row's mistake inverted.
  <br>**The rule is that a screen asserts HEALTH and reports SHARPNESS, and they are different questions.**
  A health check must pass any working model — its job is to catch a dead conversion — so it gets the EASY
  pair and a generous threshold. Sharpness is a number, printed beside a known-good control, never a
  verdict. The tell that you have merged them: your check fails a model you cannot otherwise fault.
  <br>**The reranker screen could assert a hard case only because it had a PUBLISHED reference score.** An
  external ground truth licenses a threshold; a fixture you authored does not, and an absolute cosine
  threshold is not portable across embedding families anyway. **With no published number, run a control and
  report the difference** — which is also why `embed-screen` takes `--control`.
- **`rerank-screen --inspect` reporting `head: MISSING` says NOTHING about an embedder, and the word <!-- trap: sub=measurement shape=wrong-subject -->
  reads like a defect.** A cross-encoder has a classification head; a bi-encoder has none BY DESIGN. The
  same field that is evidence in one role is vacuous in the other, and `all-MiniLM-L6-v2` — which screens
  healthy — reports MISSING. This is the architecture-bound entry above with the ROLE varying instead of
  the architecture, so the same correction applies twice over: **tensor presence is a positive signal
  only.** `embed-screen --inspect` prints the fields that do decide an embedder — `embedding_length`,
  `context_length`, `pooling_type`, `token_type_count` — and says so inline.
- **A size floor attributed to a ROLE can belong to the TOKENIZER, and the arithmetic settles it in one <!-- trap: sub=measurement shape=wrong-subject,stale-claim -->
  line.** The reranker survey concluded that sub-100 MB is dead because a cross-encoder needs a segment
  signal and a pooler, so only the RoBERTa/XLM-R family works and that family is too big. The second half
  is true and the reason given is not: measured one role over, `multilingual-e5-small` Q8_0 is
  **132,439,008 B** against the reranker survey's **132,584,000 B** — **0.11% apart**, same architecture,
  completely different role. The wall is the vocabulary: 250,002 × 384 = 96,000,768 embedding parameters,
  **102,000,816 B at Q8_0's 8.5 bits per weight — over 100,000,000 B before a single transformer layer.**
  <br>**So quantisation is not a lever and MONOLINGUAL is the escape**, which the role-based explanation
  hides: `bge-small-zh-v1.5` carries a 21,128-token vocabulary and fits the whole model in 47,886,240 B.
  **Compute `vocab × hidden` before believing any story about why a family is too large** — it is one
  multiplication, and it distinguishes a structural limit from a re-survey worth running.
  <br>**And a mismatched vocabulary costs TOKENS, not just quality**: that Chinese model spends **2,170**
  tokens on the same 6,263-character English text `all-MiniLM-L6-v2` covers in **1,207**, so it hits a
  context or batch ceiling at 55% of the text — a second, quieter cost of the wrong tokenizer.
- **CJK passed to a command-line argument goes through the console encoding and arrives mangled.** A <!-- trap: sub=encoding,cli shape=silent-loss -->
  `curl -d '{"documents":["评审会…"]}'` on this machine produced
  `parse error … ill-formed UTF-8 byte` from the server, because the GBK console rewrote the payload before
  `curl` ever saw it. **Write the payload to a UTF-8 file and pass `-d @file`** — the same rule
  `windows-machine.md` states for building file content, applied to arguments.
- **Where no `.gitattributes` declares a convention, `core.autocrlf`'s two values mean nearly opposite <!-- trap: sub=git shape=stale-claim -->
  things — and a rule that ASSERTS one of them as a fact about the repository is the trap.**
  `.claude/rules/windows-machine.md` asserted `true` (index gets LF, so a `w/mixed` tree is cosmetic) and
  this repository was `false` (**the index gets the tree verbatim, so it commits**). A tool wrote CRLF, git
  faithfully stored CRLF, and `git show --stat` read **2055 insertions / 1931 deletions** for a change whose
  real size was **131 / 7**. `--ignore-cr-at-eol` and `git ls-files --eol` named it in seconds; the written
  rule sent the reader the other way first.
  <br>**`true` is not unconditional either.** It is defined as `text=auto`, and gitattributes(5) says of
  that: a blob already stored with CRLF stays CRLF on re-add, and binary-detected content (`i/-text`) is
  never converted at all. So under EITHER setting a `w/mixed` tree can be real and can still commit — which
  is why the per-file `git ls-files --eol` check is the thing to trust, never the config value.
  <br>**State the rule, never the value — and better, DECLARE it so there is no value to state.**
  `.git/config` is untracked, so no document can say what a given clone holds. A tracked `.gitattributes`
  is the only line-ending declaration that travels; **this repository has one** (`* text=auto eol=lf`,
  `docs/DECISIONS.md` **D95**), which turns the whole investigation above into a property you can assert.
  <br>**What declaring it buys, and what it does not.** It makes the COMMIT safe unconditionally — a CRLF
  or mixed working file is normalized on checkin, so it can no longer reach the index or inflate a diff. It
  does **not** stop a tool writing CRLF into the working tree (`dev.mjs decisions-index` did exactly that).
- **A shared runtime killed by IMAGE name takes down tenants that were never yours — and killing strictly <!-- trap: sub=measurement shape=resource -->
  by PID is still not evidence that it did not.** Written down in `windows-machine.md` and violated anyway:
  on 2026-08-28 a measurement run finished with `taskkill //F //IM llama-server.exe` and took down a
  *second* instance on another port, a sibling tool's embedding server that nothing in the run had started.
  Then on 2026-09-10 a cleanup killed five servers strictly by PID, none of them the sibling's, and the
  sibling was down at the end of it anyway; whether the kills caused it was never established, and that is
  the point — *"I only killed my own PIDs"* is an argument, not evidence. **Query the neighbour's health
  after you clean up** and restart it if it is gone. Note also that `taskkill //F //PID` reported SUCCESS
  for a process still listening seconds later, so its exit code does not prove the port is free; re-read
  `netstat` rather than trusting it.
- **A port you did not CHECK is a port you do not own, and binding a busy one fails UPWARD: your requests <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  are answered by the incumbent.** Measured 2026-09-11 starting an embedder for a ladder. An earlier sweep
  had checked 8080/8081/8082/11434 and found nothing, so 8090 was assumed free; a neighbour's server had
  held it since the afternoon, serving a DIFFERENT embedding model. The launch printed no bind error, the
  endpoint answered, and the smoke test — *is the vector 768-dimensional?* — **passed, because both models
  are 768-dimensional.** Every number would have been taken on the wrong embedder, which on this axis is
  known to pick the opposite end of a ladder.
  <br>**Three rules, and the third is the one that saved it.** Check the SPECIFIC port immediately before
  binding, never a range you checked earlier. Prefer a port nothing conventionally uses, since a default is
  exactly what a neighbour also chose. And **verify identity, not plausibility**: read the served model back
  (`/v1/models`) and compare the actual vectors against the other candidate — a shape check cannot separate
  two models that share a shape, and the failure it misses is silent.
  <br>**The SAME neighbour was still holding 8090 on 2026-09-11**, invisible to a fresh sweep of
  8080/8081/8082/11434/1234 that reported "no model server ports listening". It survived a design session's
  spawn-and-teardown only because it was looked for. **A sweep of the ports you were going to use is not a
  census of the machine** — enumerate `llama-server` processes and their parents, not a port list you wrote.
  <br>**And the check must test `LISTENING`, which is the same trap's other direction.** Matching the port
  alone (`netstat | grep ':8137 '`) also matches the `TIME_WAIT` sockets a server you just tore down leaves
  behind, so it reports BUSY on a free port — a false ABORT where the entry above is a false PROCEED. One is
  merely annoying and the other is silent, which is exactly why the loose check survives review.
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
  Measured 2026-09-11 with `llama-bench`, 11.2 GiB of VRAM free in every cell. Only the neighbour changed:
  a game-streaming host was rendering for the "busy" column and idle for the "quiet" one.

  | model, test | `-ngl 0` busy | offloaded busy | `-ngl 0` quiet | offloaded quiet |
  |---|---|---|---|---|
  | `gemma-3-4b-it`, generation | 9.22 | **0.10** | 6.08 | **73.77** |
  | `nomic-embed-text`, encode | 175 | 839 | 9,901 | **53,295** |

  **Offloading is right by DEFAULT — 12× on generation and 5.4× on encoding when the device is free.** What
  contention does is not slow it evenly: generation swings from **12.1× faster** (73.77 against 6.08) to
  **92× slower** (0.10 against 9.22), a **~1,100× reversal**, while encoding barely moves — 5.4× faster
  quiet against 4.8× faster busy. **Encode-only work is ROBUST to a busy GPU and generation is not**, which
  is a second, independent reason a cross-encoder beats an instruct model beside a game — its cost survives
  the neighbour.
  <br>_Arithmetic corrected 2026-09-13. This paragraph read **"26× slower, roughly a 300× reversal"** and
  the heading said **"about sixty times harder than encoding"**; none of the three divides out of the table
  directly above it. They were read off the figures' SHAPE rather than computed — which is the
  de-quantification trap this file records two entries up, committed in the entry's own summary line. The
  table itself was never in doubt; only the sentence derived from it._
  <br>**The tell that you are in the bad regime is a NON-MONOTONE offload curve** (0.45 / 2.49 / 0.33 /
  0.10 across `-ngl` 8/16/24/34): a setting that is genuinely wrong degrades smoothly, and contention does
  not. Prompt processing also moves the OPPOSITE way to generation under contention, so a run judged on
  prompt speed alone picks the configuration that destroys generation.
  <br>**Correcting what this entry said first**, because it shipped for a few hours as guidance: *"the
  SHAPE of the task decides the offload level — generative wants `-ngl 0`"*. It does not. **Contention
  decides it**; the shape only decides how badly you are punished for guessing. Every figure in the busy
  column was taken while a game rendered, and reading any of them as a hardware ceiling was the mistake.
  <br>**So: always pass `-ngl` explicitly** — the default is not neutral and the log prints no offload line
  — and **measure the serving configuration before measuring the model**, which is **D107**'s warning in a
  second form: a number that looks like a property of the model is a property of the serving layer. Vectors
  are byte-identical across devices, so for encode-only work switching is a free speed choice, and a run
  may be compared across it.
- **A bare `new HttpClient` leaves PROXY RESOLUTION on, which costs up to 2 SECONDS per local call — and it <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  is BIMODAL, so it reads as the model's tail latency rather than as an offset.** Measured 2026-09-11. The
  same entry one layer further out than the one above: a number that looks like a property of the model is
  a property of the CLIENT.

  | client | mean | max |
  |---|---|---|
  | `localhost`, proxy on | 513 ms | **2,051 ms** |
  | `127.0.0.1`, proxy on | 8.9 ms | 34 ms |
  | either, `UseProxy = false` | **0.4 ms** | 1.0 ms |

  <br>**This CORRECTS a widely-repeated explanation, and the correction is the useful half.** The delay was
  attributed to Windows resolving `localhost` to `::1` first and an IPv4-only listener paying a failed
  connect. **Nothing at the TCP layer is slow here**: a raw connect to `[::1]` on a port with no IPv6
  listener refuses in **5.8 ms**, and to a dead `::1` port in **0.4 ms**. Disabling the proxy collapses
  BOTH spellings, which address-family selection cannot explain.
  <br>**So `127.0.0.1` only mitigates** — still 15× the direct path — and `UseProxy = false` on the handler
  is the actual fix. **Every bench in `bench/Lyntai.Benchmarks` builds a bare
  `new HttpClient { Timeout = … }`**, so any new one that reports latency must set it — **except
  `MemoryContentionSweep`**, which already does (`new HttpClientHandler { UseProxy = false }`).
  `memory-scale` and `memory-contention` are the two published latency sweeps: the former has no HTTP
  client at all, the latter disables the proxy.
  <br>**And the verification lesson generalises past networking.** The superseded claim shipped with a
  check asserting *"localhost never faster"* — an ORDERING, which passes cleanly while the stated mechanism
  is wrong, because `localhost` really is slower just not for that reason. **A check aimed at the symptom
  confirms the symptom and certifies whatever story is attached to it**; aim it at the mechanism (here,
  proxy-on against proxy-off) or it cannot fail in the direction that matters.
- **A model NAME means different things on the two local servers, and a wrong one is not an error.** <!-- trap: sub=measurement shape=wrong-subject -->
  Ollama routes by it; a `llama-server` started with `--model` serves ONE model and answers to its
  `--alias`, so the name is a label and you get the loaded model whatever you ask for. It selects only on a
  router server (`--models-dir`). **Never infer from a green run that the model you named is the model that
  answered** — and run your OWN server on its own port rather than borrowing one that happens to be up,
  because a server is started with a context and a batch size and those decide what it will accept.
- **`llama-server`'s ROUTER mode is a process SUPERVISOR, not one process holding several models — so <!-- trap: sub=measurement shape=wrong-subject,resource -->
  consolidating seams onto a router saves no memory at all.** Measured 2026-09-11 on build 10603. The
  router builds a full `llama-server.exe` command line per model and spawns it as a CHILD: the argv is
  readable in `/v1/models` (`status.args`), and three loaded models showed as three children of the router
  PID, each on its own ephemeral port, each with a grandchild of its own. **Weights, KV cache and compute
  are separate in both topologies** — three dedicated servers and one router with three models are the same
  process count plus a ~115 MB supervisor. What a router actually buys is ONE endpoint, on-demand loading
  and an eviction policy (`--models-max`, default **4**, so a router holding three models never swaps).
  <br>**Two consequences worth having before you plan a run.** "Several resident servers, or one router
  which swaps" is a false binary — residency and routing are independent axes, and a router is only a
  swapping one when `--models-max` binds. And **two seams contend only when they want the SAME model**,
  because anything else is a different process; an annotator and a judge sharing one instruct model contend
  for that child's `--parallel` slots, while an embedder and a reranker beside them cannot contend at all.
  <br>**Tree-kill reaches three levels** (router → child → grandchild) and `/T` was observed clearing all
  of them — but assert every PID gone afterwards rather than trusting the exit code, per the image-name
  entry above.
- **`--embedding` and `--reranking` are PROCESS-WIDE, so a plain `--models-dir` router serves chat and <!-- trap: sub=measurement shape=unmeasured -->
  nothing else — while still LISTING every model it found.** Measured 2026-09-11. `--embedding` is
  documented as *"restrict to only support embedding use case"*; neither flag is per-request. A router
  started without them answered `/v1/chat/completions` normally and returned
  **`501 … This server does not support embeddings. Start it with --embeddings`** (and the same for
  reranking) — after spawning the child models anyway, so the model list and the process table both look
  correct. **Per-model roles need `--models-preset`**, an INI whose section is the served id and whose keys
  are long-form flags without the `--`; the format is recoverable from the router's own `status.preset`
  field. This serves all three roles on one port, verified end to end:
  ```ini
  [embed]
  model = <dir>/embeddinggemma-300M-Q8_0.gguf
  n-gpu-layers = 99
  embeddings = true
  ```
  <br>**Check this before scoping any run around a router**, because the failure arrives after the servers
  are up and the model list reads fine — the shape the Ollama-GGUF entry below has in a different costume.
- **The two local servers disagree about an over-long input, and the disagreement is silent on one side.** <!-- trap: sub=measurement shape=silent-loss -->
  Ollama truncates and answers; `llama-server` returns `500 … input is too large`. So a run that "worked"
  on Ollama can crash on llama.cpp, and what that proves is that the truncation was always happening and
  nothing reported it. The benches now truncate explicitly and COUNT it in the footer. Related and easy to
  get wrong in the fix: a character budget cannot bound a token limit.
- **An Ollama model IS a GGUF on disk, and stock `llama-server` still may not load it.** The blobs under <!-- trap: sub=build,measurement shape=unmeasured -->
  `~/.ollama/models/blobs/sha256-*` carry the `GGUF` magic and Ollama runs them through its own bundled
  llama.cpp, so pointing your own `llama-server --model <blob>` at one looks like a free way to serve an
  already-downloaded model. Measured 2026-09-09 on `gemma3:4b` against build 10603:
  `error loading model hyperparameters: key not found in model: gemma3.attention.layer_norm_rms_epsilon`.
  Ollama's conversion omits a key upstream requires and its own runner supplies. **Read the manifest to find
  the blob** (`manifests/registry.ollama.ai/library/<model>/<tag>`, the `application/vnd.ollama.image.model`
  layer) — but expect to need the model's own GGUF from its source, and check before planning a run around it.
- **A gate anchored in PROSE dies when the prose moves, and the failure blames the wrong thing.** <!-- trap: sub=gates,docs shape=stale-claim -->
  `check-counts` requires every registered claim to match at least once, so a pattern too narrow to find its
  own claim fails rather than passing silently — which is the right design and also means **deleting or
  rewording the only sentence a claim matches turns the gate red with a message about a stale number.**
  Six of the registered claims were anchored in exactly one sentence each, all in `CLAUDE.md`. **Move such a
  sentence VERBATIM and run the gate between the copy and the delete**, so both copies match at the moment
  of the move. The narrow forms that bite: a trailing semicolon (`Twelve packages;`), bold markers and an
  em-dash (`**FIVE arms —`), digits rather than a number word (`573/573`), and an en-dash in a range
  (`D1–D112`). And `parseCount` has no hyphenated compounds, so past twenty write digits. <!-- count-ok: the en-dash range is quoted as a FORM, not as a claim about how far the log goes -->
  <br>This entry's own range example turned the gate red the moment the next decision landed, which is the
  trap demonstrating itself — the escape is `count-ok` on that line, never a silent edit to the number.
- **A skill's template is a general procedure; the FILE in front of you is the convention.** Following <!-- trap: sub=docs shape=wrong-subject -->
  `fix-log`'s bulleted template literally in 2026-08-30 produced a `**Commit:** pending` field in
  `docs/FIXES.md` that matched nothing else in the file and that nobody would ever have gone back to fill —
  the file's own entries are bolded prose paragraphs with no `Commit:` field at all. **Read the newest
  existing entry and mirror it**: the same `pattern-finder` rule that applies to code applies to records.
- **A version number written before the release exists SHIPS.** Measured twice on 2026-08-21 in one day's <!-- trap: sub=docs shape=stale-claim -->
  work: additive surface was written up as arriving in "3.1" on the reasonable assumption that SemVer makes
  additions a minor, and **one of those sentences was inside an XML doc**, so the published package's
  IntelliSense named a version that does not exist. The correction then repeated the mistake in a different
  costume — a ROADMAP row closing with "the next additive release is 3.1.0", the same promise with more
  confidence. Deliberately NOT gated: a scan for version-shaped tokens drowns in legitimate ones (`net10.0`,
  a model tag, a vendor's `0.3.40`, every historical heading).
- **A blocked item is refuted by looking where its BLOCKER lives, and a careful re-check can read the <!-- trap: sub=docs shape=wrong-subject -->
  wrong place and still be thorough.** Measured 2026-08-23: an item sat labelled blocked on "a real
  embedding model" while one was pulled on the machine the whole time. The previous re-check had been
  careful — and it read the **tree**, because that is what the other five blockers needed, and never asked
  the **machine**. The sweep was honest about what it checked and still wrong about the conclusion, which is
  why the fix is procedural: **record the blocker's KIND** (tree / environment / decision / data) and
  re-check against that kind.

- **A server that ACCEPTS a `tools` array and returns 200 has not promised to use it — a model with no <!-- trap: sub=measurement shape=fail-open,silent-loss -->
  tool template drops the roster on the floor and answers from parametric knowledge instead.** Measured
  2026-09-12 scoping the affordance shape. `gemma-3-4b-it` Q4_K_M on `llama-server` build 10603, sent three
  well-formed OpenAI function definitions: **HTTP 200, `tool_calls: null`**, and a confidently fabricated
  weather report dated *November 2023* in the content field. `tool_choice: "required"` — the one structural
  constraint the wire format offers — changed nothing; still `null`. **`--jinja` changed nothing either**,
  byte-identically, so it is not the built-in-template fallback: gemma-3's own template has no tool section,
  and the array is simply discarded.
  <br>**The failure shape is the dangerous one.** No error, no warning, no empty reply — a plausible answer.
  A deployment wiring a tool loop onto this model gets invented data where it expected a tool call, and
  nothing in the transport reports it. The library's own fallback is the protection: `ToolLoop` prefers
  native function-calling and degrades to a PROMPT protocol, which needs no template support at all.
  <br>**The POSITIVE CONTROL now exists and it CONFIRMS the reading (2026-09-13).** This entry said the
  finding was unusable without a model known to emit tool calls, because *"this model will not"* cannot be
  told from *"this build drops the array"*. Both models were then sent a **byte-identical payload in one
  run on one build**: `qwen2.5-0.5b-instruct` Q4_K_M (**491,400,032 B**) returns `finish_reason=tool_calls`
  with `get_current_weather {"location": "Galway", "unit": "celsius"}` on all three of default,
  `tool_choice: "required"` and a named `tool_choice`, while `gemma-3-4b-it` returns `null` on all three.
  **The build is fine; the model is the cause**, and the conclusion above stands as written.
  <br>**Two things the control corrected on the way.** **`--jinja` is irrelevant on this build for BOTH
  models** — byte-identical with and without, so Qwen needs no flag and gemma is not rescued by one; do not
  reach for it as the fix. And **`tool_choice: "required"` DOES bind** — on Qwen it is honoured, so its
  failure on gemma is a property of that model rather than of the wire format, which is the opposite of
  what a single-model probe suggests.
  <br>**The rule survives the unblocking and is the transferable half: measure a model you KNOW emits tool
  calls before concluding anything about one that does not.** Verify it from the GGUF's own
  `tokenizer.chat_template` rather than a model card — Qwen2.5, Qwen3 and Llama-3.2 all carry a real tool
  section, gemma-3 carries none — and prefer the prompt protocol wherever the template is unknown.

- **An ACCURACY table is the wrong readout for a transport question — the two transports differ far more <!-- trap: sub=measurement,generation shape=wrong-subject,vacuous -->
  in HOW they fail than in what they score.** Measured 2026-09-13, the same `qwen2.5-0.5b-instruct` on
  `ToolLoop`'s prompt protocol and on native function-calling, paired trial for trial. Accuracy moves by
  2.4-9.6 points. Meanwhile native declines on **21.6-30.3%** of trials against the prompt path's 1.8-5.4%,
  hallucinates a tool name **0.0%** of the time against 0.6-1.8%, emits unusable arguments **0.0%** against
  0.0-2.4% — and on requests NO tool serves it invokes one on **20-30%** against **90-100%**, which is the
  largest number anywhere in the grid and is invisible to every accuracy column.
  <br>**A deployment picking between them is choosing which failure to handle**, and an accuracy table
  reports that choice as nearly a tie.
  <br>**The largest effect was in a column nobody would have added for the accuracy question**:
  CONVERGENCE. The prompt protocol picks a tool on 94-96% of trials and then finishes the loop on
  **11.3-24.4%** of them, against native's **99.4-100%** — the model emits a well-formed `{"tool": …}` and
  then cannot emit a well-formed `{"final": …}` after the observation. It also costs the extra call that
  repair round bills: 2.34-2.54 model calls per run against 1.70-1.79. **Count what the loop DID, not just
  what it chose**; the published grid could not see this because its models were 1.6x and 5x larger.
- **A p-value that WANDERS between runs is evidence the test is underpowered, not evidence of no <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  effect — and reading it the other way publishes a real result as a null.** Walked into 2026-09-13 and
  caught by a third run. Two runs of the transport grid each had exactly one cell under p = 0.05 and **it
  was a different cell each time** (N = 6 then N = 5), which was written up as *"on accuracy it is a wash,
  and the run that said otherwise did not replicate"*. **The EFFECT SIZES had been stable across both runs
  all along** — the per-cell net was −14 then −16 at N = 5, −16 then −13 at N = 6 — and the third run
  landed on −16 and −14. The conclusion was taken from the noisiest statistic on the table while the
  steadiest one sat beside it.
  <br>**The mechanism: ~50 discordant trials cannot resolve a 10-trial net**, so McNemar's p wanders across
  0.03-0.07 on an effect that is genuinely there. A wandering p is what an underpowered test DOES.
  <br>**So compare EFFECT SIZES across runs first, and treat the p-value as a property of the sample
  size.** Two runs are enough to notice a disagreement and not enough to call it noise — if two runs
  disagree about significance while agreeing about magnitude, that is a reason to run a third, never a
  reason to publish a null. This file already says a negative result is a deliverable; it is only a
  deliverable when it is a negative RESULT rather than an underpowered one.
- **An OUTPUT CAP that cuts a turn off before any tool call reports as an empty `tool_calls`, which is <!-- trap: sub=measurement,generation shape=silent-loss,vacuous -->
  byte-identical to a model that DECLINED.** Same run. A 22-30% decline rate is a headline, and a harness
  that cannot separate the two would be publishing its own `max_tokens` as a model property. `finish_reason`
  is the separator and it is free — the field is already on the response. Measured and printed: **16 of
  1,469 native turns (1.09%)**, so the finding survived, but only because the counter existed to say so.
  **Any fail-open arm needs the artifact counted, not argued away** — this is the *answered / declined /
  unreachable* three-outcome rule one layer down, with the cap as a fourth.
- **A `<!-- result: … -->` attribute value containing a DOUBLE QUOTE truncates at that quote**, and the <!-- trap: sub=gates,docs shape=silent-loss -->
  gate reports it as stray text rather than as the escaping problem it is. Writing
  `arm="… under default / tool_choice: \"required\" / …"` — a perfectly reasonable attempt to quote a wire
  value — ended the attribute at the first `\"`, leaving the rest of the sentence loose in the marker.
  `check-measurements` caught it, which is the good case; the same shape in a marker nothing gates would
  simply lose half a value. This is the `>`-in-a-marker entry above in a second costume, and the same rule
  closes both: **a marker value is not a string literal — keep `"` and `>` out of it entirely** and reword
  rather than escape.
- **A dataset's ground truth is a SET, and taking its first element turns the rest into DISTRACTORS — so <!-- trap: sub=measurement shape=wrong-subject,silent-loss -->
  the benchmark scores a right answer as wrong.** Measured 2026-09-12 building `memory-decision`. LoCoMo's
  `evidence` is a list of turn ids; the harness took `Evidence[0]` as "the" answer and drew distractors from
  the rest of the conversation. **409 of 1,540 scored questions (26.6%) carry more than one evidence turn —
  97.9% of the multi-hop class** — and because the distractors were the top turns BY COSINE, the discarded
  golds are exactly what that pool selects. A quarter of trials had a second correct option while the prompt
  asserted exactly one did.
  <br>**The bias has a DIRECTION, and it is the one that flatters a broken arm.** Re-measured clean, every
  arm that actually reads the options gained 7-15 points while the arm that emits a CONSTANT moved −0.3 to
  +2.9 — nothing. So the contamination compressed the gap between judgement and none, which was the axis the
  write-up argued from, and it reversed a headline: a 468 MB cross-encoder read 3.0 points behind a 2.49 GB
  instruct model and actually sits ahead of it.
  <br>**The tell is a plural field read in the singular**, and the loader it borrowed from had it right all
  along — `MemoryLocomoBench` scores a hit as `Evidence.Any(...)`. Reusing a corpus loader does not inherit
  its SCORING rule. Ask of any ground-truth field: *is this a set, and does my task permit more than one
  right answer?* If the task needs exactly one, FILTER to the questions that have exactly one rather than
  picking a representative — and exclude every flagged id from the distractor pool, not just the one chosen.

- **A model asked for a score on a scale it will not use returns a BINARY verdict, and the tie rate that <!-- trap: sub=measurement shape=wrong-subject,vacuous -->
  follows reads as a property of the SHAPE under test rather than of the prompt.** Measured 2026-09-12
  building `memory-decision` (`docs/memory-measurements.md` §5). Asked for `LlmScorerBase`'s own 0..1, a 4B
  and a 1B both replied `{"score": 0}` or `{"score": 1}` and **nothing between**, across every captured
  reply. A binary score cannot rank 7 options, so an argmax over it is a coin flip — and the arm would have
  been published as "the score-a-pair shape loses" when what lost was the scale the prompt asked for.
  Widening to an integer 0-100 took the same model to a genuinely graded 0/10/20/30/60/70/75/90/95/100.
  <br>**The tell is a tie rate, and it is only visible if something COUNTS ties.** Distinctness is not
  discrimination and this file already says so for a reranker; the same audit is owed to any generative
  scorer, because a coarse scale and a model that cannot tell the options apart produce identical tables.
  <br>**The general rule: CAPTURE WHAT THE MODEL SAID before pricing what it did.** A raw-reply dump over
  the first few trials is a few lines of harness and it is what separates "the model cannot do this" from
  "the model was not asked for something it can give". Reasoning about the prompt would not have found it —
  0..1 is the scale the library itself ships.

- **Pooling a per-POSITION table over list lengths confounds position with length, and the confound points <!-- trap: sub=measurement shape=wrong-subject,scope-blind -->
  the same way the real effect does.** Measured 2026-09-12, in the first run of the same bench. Accuracy by
  the gold option's slot was summed over N = 3..7 — but slot 7 occurs ONLY at N = 7, slot 6 only at N = 6
  and 7, while slot 1 occurs at every length, and accuracy falls with N. So every arm showed a downward
  slope by slot, **including three argmax arms that cannot have a position effect at all** (an argmax over
  per-option scores does not know what order they were shown in). Read as position bias, it would have been
  an artifact of which cells each slot averages.
  <br>**The fix is to read it at ONE list length**, where every slot exists and every cell shares a length,
  so a slope IS position. Doing that reversed the finding's shape: pooled, the 4B looked like it had a
  monotone first-slot preference; at N = 7 alone its penalty lands on the LAST slot (21% against 46-81%
  elsewhere) and the three controls show no dip there.
  <br>**The general shape: when a breakdown's categories are not available in every cell you are summing
  over, the aggregate measures availability as much as effect.** Ask of any pooled table — *does every
  bucket draw from the same cells?* — and if not, report one cell rather than the sum. The control that
  caught it is the one this file already prescribes: an arm that structurally CANNOT show the effect, kept
  in the table for exactly this reason.

- **A fan-out bench that lets ONE call throw discards every trial it had already finished, and the longer <!-- trap: sub=measurement shape=resource,wrong-subject -->
  the run the likelier it is.** Measured 2026-09-12 building `tool-affordance`: a single chat call stalled
  past the `HttpClient.Timeout` of 300 s on a device at 98.4% mean GPU util, and the `TaskCanceledException`
  came out of `Parallel.ForEachAsync` unhandled — **96 minutes and roughly 70 of 168 completed trials, gone,
  with nothing written down.** Every server was torn down correctly and the neighbour survived; the hygiene
  held and the RESULT still evaporated.
  <br>**The modelling error is the transferable half.** The harness already had a vocabulary for *the seam
  declined* — the fired counter this file insists on — and none for *the endpoint never answered*. Those are
  different events and only one is about the model, so had the call merely returned nothing the run would
  have finished and published a stall as "the model chose not to call a tool". **A fail-open arm needs a
  THIRD outcome, not two**: answered, declined, and unreachable.
  <br>**So catch per call, count it, exclude it from every rate, print the count, and VOID the run above a
  threshold** — and shorten the deadline while you are there, because a call an order of magnitude past the
  median is unusable whether or not it eventually returns. Excluding without printing is the worse bug: it
  shrinks every denominator while the table still looks complete. Never swallow the CALLER's cancellation
  doing it — `TaskCanceledException` is an `OperationCanceledException`, so the filter has to ask
  `!ct.IsCancellationRequested` rather than match on the type.

- **A tool roster is not a menu a model will decline — hand a 4B seven tools and it invokes one for 90-95% <!-- trap: sub=measurement,generation shape=fail-open,silent-loss -->
  of requests NONE of them serves.** Measured 2026-09-12 (`tool-affordance`, `docs/memory-measurements.md`
  §5). It does not pick something defensibly adjacent; it fabricates arguments to force a fit —
  `restart_service {"service": "sourdough_starter_knowledge_base"}` for a baking question,
  `historical_weather {"place": "room"}` for *how much paint do I need*, `translate_text {"to": "en"}` on
  English. The same model routes the roster WELL when a right tool exists (86.3% at seven options), so this
  is not weakness: **the selective half works and the refusal half does not.**
  <br>**Prompt wording does not reach it, and that is the expensive half to learn.** Two rewrites in
  opposite directions, ten paired cells each: pushing toward tool use took false calls to **100%**, putting
  the escape first left them at **90-95%** and was significant in **0 of 10**. Do not spend a round of
  prompt engineering on this — it is a property of handing a model a roster.
  <br>**So bound the roster BEFORE the model sees it**, because the model supplies no bound of its own; and
  where a false call is expensive, make the tool itself refuse rather than trusting the decision not to
  arrive. **A small model fails the MIRROR way** — 0-5% false calls and it will not call a tool when one
  does fit — so a fix aimed at one size makes the other worse, and neither is visible without BOTH a
  positive and a negative corpus.
  <br>**The measurement trap underneath is the general one**: a fixture where every request has a right
  answer cannot see a false positive at all, so any change that merely pushes harder scores as a clean win.
  Build the negative half before tuning anything against the positive half.

## LLM / router (details in `llm-and-router.md`)

- **A timeout as a single `CancelAfter` over a whole call** — a wall clock that kills a slow-but-alive <!-- trap: sub=router,cli shape=cancellation -->
  child (a long tool loop, a big prompt) exactly like a dead one. Both the STREAMING path and the BUFFERED
  `ProcessRunner.RunAsync` must use a per-chunk inactivity clock (re-armed on each read); the buffered path
  adds an absolute `maxDuration` backstop and reports `ProcessResult.TimeoutKind`. (Streaming shipped the
  bug in two providers; the buffered path shipped it too — both fixed.)
- **A non-positive resolved budget means the OPPOSITE thing in the two domains — don't "unify" the idiom <!-- trap: sub=router,generation shape=cancellation -->
  casually.** The LLM sites arm the clock unconditionally (`OpenAiCompatibleProvider.CompleteAsync`,
  `HttpEmbedder.EmbedBatchAsync`, `ExtensionsAiProvider` all `CancelAfter(timeout)`), and app-configured
  values are trusted rather than clamped (`LyntaiOptions.ResolveTimeout`), so a `TimeoutByConsumer` entry of
  `TimeSpan.Zero` **cancels the call instantly**. `GenerationDeadline.GuardAsync` reads the same value as
  **no deadline at all** — the documented escape hatch for a host that owns its own clocks. Both are
  deliberate; flipping either is a behaviour change no consumer can detect at compile time (major-bump
  material, `docs/DECISIONS.md` D18), and a shared helper that keeps both behaviours behind a flag has
  consolidated nothing but the line count.
- **Committing a stream on an empty content chunk** — disables fallback for a zero-content first chunk. <!-- trap: sub=router shape=fail-open -->
  Gate the commit on `Text.Length > 0`.
- **Hand-rolled verdict heuristics** in a provider — they drift. Always route through <!-- trap: sub=router shape=wrong-subject,second-door -->
  `LlmVerdictClassifier`. And keep it conservative: a bare word like "unauthorized" or a "429" in a
  stack frame must not trip a verdict that benches a healthy host.
- **Empty provider output as `Ok`** — must be `Failed` (and a terminal `Error` chunk when streaming) so <!-- trap: sub=router shape=fail-open -->
  the router can fall over.
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
  code (`CliProviderEngine.StatusAsync`). And parse the WHOLE body: the `Tail()` helper keeps the LAST 500
  chars, which would decapitate a JSON document.
  **The COMPLETION path had the same defect and shipped with it** (fixed 2026-08-05, `docs/FIXES.md`):
  `CompleteAsync` returned on a non-zero exit *before* parsing stdout, so a turn that reported its failure in
  band AND exited non-zero was classified from whatever was on stderr — measured as `Failed` /
  "exit 1: Reading prompt from stdin..." for a 401 that should have been `AuthFailed` (which benches the
  host; `Failed` merely advances). **The generalisation, since one seam having this fixed did not stop the
  next one shipping it: whenever a backend can answer in TWO channels, decide the precedence explicitly and
  pin it with a test.** The backend's own words outrank the exit code every time; the exit code is context
  for the detail, not the reason.
- **Re-implementing the CLI rules for a new CLI backend.** Everything above lives in <!-- trap: sub=cli shape=second-door -->
  `CliProviderEngine` (Core, `Lyntai.Llm.Cli`); a new CLI is an `ICliProviderDialect`, never a fresh
  `ILlmProvider` (`docs/DECISIONS.md` D21). The reason these traps were fixable at all is that there is now
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
  purpose (§6 hygiene). codex refuses to run outside a git repository, so its dialect MUST pass
  `--skip-git-repo-check` — every completion would fail on a perfectly good install otherwise. Check what
  your CLI assumes about its cwd. **And the flag is needed on the AGENT path too, where the cwd is the
  caller's project**: that reads as "obviously a repo" on a developer's machine and is very often not one in
  a shipped bundle — a passing test that hides a shipped failure. Both codex paths build argv from
  `CodexExecArgs` for exactly this reason; a second copy is a second chance to lose the flag.
- **Adding a SECOND seam over the same CLI without sharing the wire knowledge.** `CodexAgentSession` reads <!-- trap: sub=cli shape=second-door -->
  the same JSONL as `CodexCliProvider`, so the vocabulary and the non-terminal-`error` rule live once in
  `CodexEnvelope`. Two readers of one wire format drift, and the drift is invisible until the halves disagree
  about whether a turn failed.
- **Mapping a wire format you have not measured, name by name.** The codex agent session's tool-step half is <!-- trap: sub=cli shape=unmeasured -->
  INFERRED (the measured capture ran no tools). It is written **shape-driven**: any unknown item type becomes
  a tool step under the BACKEND's own name carrying the BACKEND's own payload, nothing renamed or normalised.
  **Be precise about what that buys, because the first draft of this entry was not:** it guarantees *no
  payload is invented or dropped* and *every uncertainty stays inside the tool-step half* — it does NOT
  guarantee the right KIND of event. `CodexAgentReader.ReadItem` reaches the tool arm by **elimination**
  against three names, one of which (`reasoning`) is itself a guess, so a renamed `reasoning` surfaces the
  model's thought as a *fabricated* `ToolCall`. A tool step's **kind is provisional, its payload is
  reliable** — tell consumers to switch on `ToolCall.Name`. Mark every inferred member as inferred in the
  XML docs — and where a guess would COST something (codex reads an unrecognized subcommand as a prompt and
  spends a turn), refuse instead of guessing, and refuse instead of silently ignoring. A safety claim that
  overreaches is itself a documented-not-measured surface (`docs/DECISIONS.md` D35).
- **When one layer RESOLVES a caller's "not stated" into a concrete value, the next layer cannot tell it <!-- trap: sub=memory,storage shape=silent-loss -->
  from a value the caller chose — and writing it back destroys their data silently.** Measured 2026-08-26,
  **three times in one subsystem** (`docs/DECISIONS.md` **D91**, `docs/FIXES.md`).
  `GraphMemoryEngine` turned `MemoryGrade.Inherit` into `Associative`, a null `Headline` into a truncation
  of the content, and passed both to a store whose upsert overwrote unconditionally. So refreshing a fact
  demoted it out of the authoritative grade, and replaced an authored headline with a machine-made one.
  `Metadata` was the same family from the other side: the store simply ignored it, so a correction was
  dropped.
  <br>**Every one was silent, destroyed the caller's own data, and was undiscoverable without reading the
  SQL** — and the fix in each case is to carry the DISTINCTION, not the resolved value: `GradeStated`,
  `HeadlineStated`, `COALESCE(@metadata, stored)`.
  <br>**The generalisation, and the reason this is one entry rather than three:** a default that means
  "decide for me" is information, and resolving it early throws that information away. Ask of any field a
  caller may omit — **does some layer turn the omission into a value, and does a later layer then persist
  it as though it were chosen?**
  <br>**Two of the three were found only by looking**, which is the part worth copying. The first was found
  by asking what a re-remember overwrites; the second and third by this document's own §"Copying a rule
  copies its assumptions" advice — *grep your own diff for the other places the distinction applies* — run
  against the session that had just fixed the first. A rule articulated and not applied to its own author's
  work is a rule that catches one instance.

- **A NEGATIVE result cached in a process-lifetime cache is permanent, and it is the one answer that had no <!-- trap: sub=cli,tests shape=resource,fail-open -->
  business being remembered.** Measured 2026-08-26 (`docs/FIXES.md`).
  `ProcessRunner.ResolveCommandPath` memoized `Locate(cmd) ?? cmd` into a static `ConcurrentDictionary` —
  fallback included — so ONE transient `where.exe` failure pinned the unresolved bare name forever, and
  `CommandExists` reads a name with no directory part as NOT FOUND. From that moment every provider's
  `IsAvailable` reported an installed CLI as absent, silently, until the process restarted.
  <br>**The asymmetry is the rule: a success is a fact, a failure is a MOMENT.** "node is at
  C:\…\node.exe" stays true; "the locator did not answer just now" says nothing about the next call, and
  the thing being cached is a process spawn under load — exactly the operation whose failures are
  transient. Re-looking-up a genuinely missing command costs one spawn per call, which is the cheap side:
  a command that is absent is absent once, a command wrongly believed absent is wrong until restart.
  <br>**How it presented, because that is the part worth recognising:** as a TEST FLAKE. `verify`
  intermittently failed exactly 9 tests while a standalone run never did — a constant count, because one
  poisoned entry fails everything downstream of it at once. A varying count would have suggested load; a
  constant one is a single cause. **When a flake's count is stable, look for one shared piece of state, not
  for a race.**
  <br>And when you fix a cache, **write the positive control**: a fact asserting the failure is not cached
  passes on an implementation that caches nothing at all, which fixes the bug by deleting the optimization.

- **Trusting an explicit command without checking it exists.** For a PORTABLE install (an app's own bundled <!-- trap: sub=cli shape=unmeasured,vacuous -->
  CLI copy) `IsAvailable` must verify presence — `ProcessRunner.CommandExists`, which also accepts an
  extensionless launcher with a spawnable sibling. Returning true for a path that isn't there turns a
  skippable candidate into a failed turn.
- **Forwarding a free-form option value straight into argv.** `ProviderLoginRequest.Mode` / <!-- trap: sub=cli shape=second-door -->
  `ProviderInstallRequest.Version` are free-form so other backends fit the contract — but an adapter must
  REFUSE a value it doesn't recognize instead of synthesizing `--<whatever>`, and must refuse a flag-shaped
  value in a data slot (`Email: "--dangerously-x"`, `Version: "--force"`). `ArgumentList` prevents *shell*
  injection, not the backend's own argument parser reading your value as an option.
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

## Provider lifetime, cooldown & admission (`Lyntai.Lifecycle`; details in `docs/DECISIONS.md` D30)

Every one of these is a *silent* failure: the build is green, the tests are green, and the damage is a
benched tenant, an unbounded engine or a render nobody cancelled.

- **Hoisting a member into a new BASE interface, and deleting it from the derived one, breaks every <!-- trap: sub=build,gates shape=scope-blind,wrong-subject -->
  pre-compiled caller.** Adding a base interface is binary-safe; removing the member from the interface that
  used to declare it is not, and the refactor that does both in one step reads as pure cleanup. A consumer
  compiled against the old surface emits `callvirt ILlmProvider::get_Id`, and member resolution **does not
  walk base interfaces** — so `provider.Id` throws `MissingMethodException` until that assembly is
  *recompiled*, which upgrading a package reference does not do. Nothing in this repository catches it:
  `check-warnings` is silent, the API baseline shows a line moving from one interface to another, and
  **`consumer-smoke` cannot see it at all** because it rebuilds its consumer from source every run. To test a
  binary-compatibility claim you must compile a probe against the OLD assembly and run it against the new one
  (a `callvirt` on a `null` argument is enough — `NullReferenceException` means the member resolved,
  `MissingMethodException` means it did not). `ILlmProvider` and `IGenerationProvider` therefore keep their
  own `new string Id { get; }` next to `IProviderIdentity`; the declarations are the compatibility, and a
  test pins them. Implementors are unaffected either way — one implicit `public string Id` satisfies both
  slots — so an implementor-only compatibility check proves nothing about callers.
  **The next two places this can happen, named so nobody has to rediscover them:** `IScorer`
  (`src/Lyntai.Core/Cortex/IScorer.cs`) and `ICliProviderDialect`
  (`src/Lyntai.Core/Llm/Cli/ICliProviderDialect.cs`) each declare their own `string Id { get; }` with exactly
  the shape `IProviderIdentity` supplies, so both look like leftovers a tidy-up should hoist. Neither derives
  from `IProviderIdentity` today, and neither should be *changed to derive from it by deleting its own
  declaration* — that is the same `MissingMethodException` for every pre-compiled caller of `scorer.Id` or
  `dialect.Id`. If either ever gains the base interface, it keeps its own `new string Id { get; }` too, and
  gets a line in `ProviderIdentityTests` alongside the two provider seams.
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
- **A key derived from the options object is right for some backends and silently wrong for others.** Four of <!-- trap: sub=lifetime,di shape=wrong-subject -->
  the five generation options types are records with `init` members and compare structurally; but
  `LocalDiffusionOptions` is a plain class and compares by **reference**, and `ComfyUiOptions.Kinds` /
  `FalQueueOptions.Kinds` are `IReadOnlyList<string>` members that record equality also compares by reference
  (both defaults are collection expressions evaluated per instance, so even two default-constructed options
  compare unequal). So automatic derivation reuses correctly for **two** backends and rebuilds-or-reuses
  arbitrarily for **three**. (The measured basis, so the next audit can re-check the tally without re-deriving
  it: `Automatic1111Options` and `OpenAiImageOptions` are the two with no collection member at all — only
  `string`/`int`/`double`/`TimeSpan`.) Name every contribution
  (`ProviderKey.For(id).With("baseUrl", …).WithSecret("apiKey", …)`) so a forgotten member is visible in
  review — and include values the backend resolves at **runtime** (a downloaded engine's binary/model paths
  appear when the download completes; the saved configuration has not changed, yet a pooled instance holding
  empty paths keeps failing forever).
- **Decorating a provider erases its optional capability interfaces.** The generation seam expresses <!-- trap: sub=lifetime,generation shape=silent-loss -->
  long-running and streaming delivery as *additional* interfaces the router type-tests:
  `if (provider is not IGenerationJobProvider job) continue;` (`GenerationRouter.SubmitAsync`). Any wrapper
  implementing only `IGenerationProvider` makes a queue backend invisible, so **every video render stops
  routing while every image render keeps working and every inline-only test stays green.** This is why
  admission is applied by the router rather than by a decorator — and the trap applies to *any* future
  wrapper (telemetry, retries, redaction), not just this one. If you must wrap, forward every optional
  interface the wrapped instance implements, and prove it with a test that submits a job.
- **A `using` that is one frame shallower than you think.** `GenerationRouter.GenerateAsync`'s `Surface` arm <!-- trap: sub=lifetime shape=resource,second-door -->
  `return`s from inside the fallback switch, which is safe **only** because the admission permit is held in
  `AttemptAsync`, one frame deeper. Inlining `AttemptAsync` in the name of simplification turns that return
  into a permanent gate leak. More generally: every `ProviderAdmission.EnterAsync` result must reach a
  `using` on **every** path — fallback, refusal, cancellation — because a caller that abandons the `ValueTask`
  without disposing the handle pins its gate forever.

## Storage (details in `storage.md`)

- **A single-threaded benchmark cannot see a PROCESS-GLOBAL ceiling, and SQLite ships one that is ON by <!-- trap: sub=storage,measurement shape=resource,wrong-subject -->
  default.** SQLite collects memory-allocation statistics unless told not to, and maintaining them takes a
  process-global mutex on every allocation and free — so concurrent readers serialise on a counter that has
  nothing to do with the database. It cost **12–29×** here: read-only recalls peaked at TWO workers on a
  22-core machine and fell to 216/s by sixteen, against 6,275/s with the statistics off (`docs/memory.md`
  §7, **D107**). Every test in this repository passed throughout, because every one of them is
  single-threaded.
  <br>**The diagnostic lesson is worth more than the setting.** Six plausible causes were refuted before
  the real one — the connection open, GC, exceptions, the WAL, journal mode, the measurement window — and
  the two measurements that actually located it were both about SCOPE rather than about SQLite: a CPU-over-
  wall column (the threads were burning 7.7 cores, so they were NOT waiting on a lock, which killed every
  lock hypothesis at once), and an isolation ladder ending in **separate PROCESSES**. One engine, one store
  and one database file per worker collapsed identically, while eight separate processes scaled fine.
  **When per-object and per-file isolation change nothing and a second process fixes it, stop looking for
  shared state in your own code** — something in the process is global, and a native dependency's
  configuration is invisible to every grep you would think to run.
  <br>**And a flattering refutation still needs repeats.** A 29× improvement is exactly the shape this
  repository has twice published and retracted; it was re-run interleaved with its control three times
  (on 330/320/317, off 3,154/4,056/4,060) before being believed.
- **Two embedding servers disagree about an OVER-LONG input, and the quiet one is the dangerous one.** <!-- trap: sub=measurement shape=silent-loss -->
  Ollama truncates silently and answers; `llama-server` returns `500 … input (N tokens) is too large`. So
  moving a bench from one to the other turns an invisible behaviour into a crashed run — and the crash is
  what revealed that the behaviour was there all along. Measured 2026-09-08: LongMemEval's texts reach
  **76,560 characters against a median of 429**, so every figure on record was taken with that tail quietly
  cut by whichever server happened to answer, at whatever limit it happened to be started with.
  <br>**A CHARACTER budget cannot bound a TOKEN limit** — density varies by an order of magnitude across
  scripts, and a constant picked against prose fails on dense text. The first attempt at 6,000 characters
  cleared the corpus's LONGEST text at 1,290 tokens and still 500'd on a denser one. The fix is to shrink
  and retry on the server's own complaint, with a floor so a pathological input fails loudly rather than
  being cut to nothing and embedded as a meaningless vector.
  <br>**And COUNT it.** A run that truncates and does not say so is claiming to have embedded text it did
  not; the footer now reports `N input(s) truncated`. That is the same defect as a table naming the model
  it REQUESTED rather than the one that answered — a silent difference between what was measured and what
  was reported.
- **`catch (OperationCanceledException) { throw; }` makes a fail-open seam fail CLOSED the moment the work <!-- trap: sub=memory shape=cancellation -->
  it wraps is an HTTP call.** An `HttpClient` timeout surfaces as `TaskCanceledException`, which IS an
  `OperationCanceledException`, so a bare rethrow cannot tell "the caller cancelled" from "my own request
  timed out" — and rethrowing the second one propagates out of a method whose whole contract is to degrade.
  Measured 2026-09-09: a judge call exceeding its timeout took down a recall, and with it 40 minutes of
  ingestion (`docs/FIXES.md`).
  <br>**The fix is a filter, not a broader catch**: `when (ct.IsCancellationRequested)`. Swallowing every
  cancellation is the wrong repair — it makes a cancelled operation look like a successful one — so pin BOTH
  halves, which is what `MemoryVerificationTimeoutTests` does.
  <br>**Why it survived**: the seam is opt-in and defaults to none, so the shipped path never exercises it.
  **Whenever a fail-open catch wraps work that can be a network call, ask which exception the timeout
  actually throws.** Census on 2026-09-09: `Lyntai.Core/Memory` held 21 such sites; every one is now
  guarded and **0** bare.
  <br>**The idiom was ALREADY drawn here, and that is the durable half.** `SemanticMemory.cs:50` has
  carried `when (ct.IsCancellationRequested)` over an embedder since 2026-07-18 (`8a2cde6`), so this was
  never a technique nobody had; it was one applied unevenly two directories away. **A repository that
  already solved something in one place will solve it inconsistently unless something checks** — look for
  your own prior art before concluding a defect is novel, because finding it changes the fix from "add a
  guard" to "make the rule uniform".
- **Fail-open handlers nest, so the promise is only as good as the WEAKEST link in the chain — and testing <!-- trap: sub=memory shape=fail-open,cancellation -->
  a link in isolation cannot see that.** One timeout from a BYO embedder passes through the seed source,
  the engine's gather, the composite, and then the walk or the composition: four nested handlers, each
  documented fail-open, each individually looking correct. A bare rethrow at ANY of them breaks the promise
  of ALL of them, and a per-layer test stays green while the chain leaks end to end. Measured 2026-09-09 in
  `Lyntai.Core/Memory`, where the same defect sat at 16 sites at once.
  <br>**So when a fail-open promise is made at more than one level, test it END TO END from the deepest
  fault to the outermost caller**, not once per layer — and treat "each site needs its own answer" with
  suspicion when every site carries the same promise. Here they did: "must not sink the blend", "returning
  nothing", "stored unlinked", "must not sink the caller's prompt". The per-site framing was what made a
  one-line-per-site fix look like an unjustified sweep.
- **When a CONTRACT states the false premise, fixing the code alone ships a doc that contradicts it — and <!-- trap: sub=docs,memory shape=stale-claim -->
  no gate can see that.** The cancellation defect above was not only in the catches: `IMemoryEngine`'s own
  recall doc said *"Only `OperationCanceledException` propagates, because cancellation belongs to the
  caller"*, and `IMemorySeedSource`'s said *"Cancellation is the exception and is always propagated"* in
  the same paragraph as *"it must not throw for a transient fault"* — which an `HttpClient` timeout is
  BOTH of. The seed sources' own docs went further and asserted the wrong behaviour as a feature
  (*"that is the caller leaving, not an enrichment fault"*).
  <br>**A contract that justifies itself is where a wrong premise hides best**, because the justifying
  clause reads as reasoning rather than as a claim to check. Grep the seam docs for the RULE, not just the
  code for the idiom — and prefer a promise phrased as a TEST the reader can apply
  (`ct.IsCancellationRequested`) over one phrased as a type (`OperationCanceledException`), because the
  type is what was ambiguous.
- **A caller-cancel test written as `ThrowsAnyAsync` on a PRE-cancelled token usually cannot fail, so the <!-- trap: sub=tests shape=vacuous,cancellation -->
  control that was supposed to stop a bad fix certifies it instead.** The pair above is the standard shape:
  one test says the seam's own timeout degrades, its twin says a real cancel still propagates. The twin is
  what stops the lazy repair (`catch (OperationCanceledException) { return None; }`) — and on a pre-cancelled
  token it is usually satisfied by something else entirely. Measured 2026-09-09 on both twins in this
  repository: `RecallAsync` checks the token before anything else, so the verifier is **never reached**; and
  on the write path the annotator's exception was swallowed by the wrong fix, the write continued, and
  `store.UpsertAsync` — outside every `try` — threw an `OperationCanceledException` of its own. Both twins
  passed under the wrong fix, and `docs/FIXES.md` claimed both halves were pinned.
  <br>**Two fixes, and they are different because the two paths are.** MARK the exception the seam throws
  and assert on the marker, so another component's cancellation cannot satisfy it; and reach the seam with
  a token that is cancelled — which on a path with an entry check means cancelling MID-CALL, from inside the
  policy, rather than before. **Then mutation-test it**: apply the wrong fix and watch the twin go red. A
  control nobody has seen fail is not a control.
- **A seam that hands a model a TRUNCATION measures the truncation, and the model takes the blame.** <!-- trap: sub=memory,measurement shape=wrong-subject,silent-loss -->
  `IMemoryVerificationPolicy` receives `MemoryVerificationCandidate.Headline` and never `Content`, and
  `GraphMemoryOptions.HeadlineChars` ships at 120. Measured 2026-09-08 on LoCoMo, whose turns have a median
  of 133 characters: a purpose-built cross-encoder scored **78.0%** against its base's 85.5% and read as a
  refutation of the whole design lead — until the same model on the same arm with headlines long enough to
  hold the turn read **91.0%**. Headline length alone was worth **+13.0** to the reranked arm and +0.5 to
  the base (`docs/memory-measurements.md` §5, **D107**'s neighbour).
  <br>**The tell was that the audit was CLEAN.** 16,002 pairs scored, 15,958 distinct — the model
  discriminated almost perfectly and still lost, which is the signature of a model being fed the wrong
  input rather than of a model that cannot do the job. **Before concluding a model class does not transfer,
  check what the seam actually passed it** — and prefer a control that varies the INPUT (a longer headline)
  over one that varies the model.
- **An admission guarantee must survive the LIMIT, not just the WHERE.** "Admitted unconditionally" that <!-- trap: sub=storage,memory shape=ordering -->
  is implemented only as a predicate is still excluded by `ORDER BY … LIMIT` whenever the ordering key is
  the axis the protected row is weakest on. All three `IMemoryGraphStore` backends filtered authoritative
  material into every seed candidate set correctly, then ordered by recency alone and took a capped
  count — so a long-quiet exact fact, which by definition has the lowest recency in its scope, sorted last
  and was cut before ranking ever saw it. Grade now leads the seed ordering ahead of recency **on the paths
  that HAVE one** — the in-process store's single ordering, and two of `SqliteMemoryGraphStore.SeedAsync`'s
  three branches (the LIKE-fallback and no-query ones). Check both halves whenever a query claims to exempt
  something.
  <br>**Grade-first is not uniform across backends, found measuring the seed-source fusion plan
  (2026-08-31).** SQLite's FTS/bm25 branch has NO grade term in its own `ORDER BY` at all — bm25 leads
  because everything in that result already matched, so match quality outranks grade there by design; an
  authoritative fact is instead fetched by a SEPARATE query and merged in afterwards. **A fixture written
  against SQLite's FTS path alone passes a rule it never exercises.** Read the backend's actual query, never
  generalise the ordering from one branch or from a sibling backend.
- **A bound configured on one SCOPE and enforced on another is not a bound — and the tests will only ever <!-- trap: sub=memory,tests shape=vacuous -->
  exercise the regime where the gap is invisible.** The sequel to the entry above, and it shipped inside the
  fix for it. `GraphMemoryOptions.AuthoritativeReserve` is a per-ENGINE option that reserves recall slots;
  `MemoryQuery.Limit` arrives per QUERY. Only the `null` DEFAULT was capped by the limit (`?? limit`), so an
  explicit value larger than a caller's limit overran it outright — measured at reserve `5` / `Limit: 2`,
  **three items came back for a limit of two, and not one ordinary hit**, against a promise written down in
  three maintained documents. `5` is a sensible bound against the default limit of `10`; the trigger is
  simply a caller passing something smaller, which is what a prompt budget does.
  <br>**Both existing tests used a reserve BELOW the limit** (`1` against `3` and `4`) — the only regime where
  the missing cap cannot be observed — and nothing anywhere asserted the flat property that a recall returns
  at most `Limit` items. **Whenever two limits come from different scopes, write the fact that the narrower
  one still binds**, and pick fixture values from the regime where they DISAGREE; a fixture in which the
  configured bound is always the smaller one tests the reconciliation you did not write.
- **Missing FTS `'delete'` trigger row on delete/update** — silent index corruption. Three triggers, <!-- trap: sub=storage shape=silent-loss -->
  always.
- **A double column read without `CAST(x AS REAL)`** — the SQLite integer-affinity trap. <!-- trap: sub=storage shape=silent-loss -->
- **Opening a connection outside the factory** — loses per-connection `foreign_keys=ON`, so cascades <!-- trap: sub=storage shape=second-door,silent-loss -->
  silently stop.
- **Reusing a migration number** — silently skipped, so the migration never runs. Use <!-- trap: sub=storage shape=silent-loss -->
  `dev.mjs new-migration`.
- **`ORDER BY` on a non-unique column with no tiebreaker** — nondeterministic on ties. <!-- trap: sub=storage shape=ordering -->
- **A `Relevance` normalized by RANK POSITION, not by score margin, makes a bounded rank boost's effect <!-- trap: sub=memory,tests shape=wrong-subject,vacuous -->
  CANDIDATE-COUNT DEPENDENT.** `SqliteMemoryGraphStore.SeedAsync` (and the other two backends' own
  orderings) reports `Relevance = 1 - i / rows.Count` — with only two candidates that is *exactly* 1.0 and
  0.5, a fixed 2× gap no matter how close the underlying `bm25` scores actually are; with ten candidates the
  same 2nd-place gap shrinks to 10%. Found 2026-08-09 closing the memory-retention-model Plan 2
  (`docs/DECISIONS.md` D45, `GraphMemoryRankingTests`): a first pass shipped a LOGARITHMIC rank boost
  (then `GraphMemoryOptions.SalienceRankWeight`; the property moved to
  `Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight` when the ranking-policy seam landed,
  same name, same default) defaulting ON, and the default value could not clear the 2× worst-case gap even at
  the maximum salience a real appraiser can report — which is *why* the owner then ruled the boost OFF by
  default in `docs/DECISIONS.md` **D45** (salience means "does not fade away", not "first priority"; store
  admission, which is unconditional, already delivers the former). The reusable trap either way: **a test (or
  a consumer's own expectation) for any rank-lifting signal over this store needs a result set large enough
  that the position-based gap it is fighting is smaller than the boost it is proving** — two candidates is
  the WORST case, not a representative one. `docs/task-archive.md` Part 53 item 1 (the ranking-policy seam
  itself, shipped 2026-08-09) is where this was found; the salience-measurement residue is `TASKS.md`
  Part 65.
- **One stored value read at N sites grows N coercion rules, and the divergence is silent.** Found <!-- trap: sub=memory,storage shape=second-door,silent-loss -->
  2026-08-09 in the same work: `salience` was read at FOUR sites with THREE rules — both SQL stores coerced
  `IsFinite ? Math.Max(1, x) : 1` into their promoted column, the in-process store ordered the raw bag value,
  and the engine's rank boost used a bare `Math.Max(1, x)`. So `{salience: 0.5}` admitted level with
  unappraised rows on SQL and BELOW them in-process — same data, same query, different backend — and a `NaN`
  (reachable through the public appraiser seam) crashed the write on one backend, sorted to the TOP on
  another, and made every candidate's rank `NaN` in the engine, which empties the recall entirely because
  `NaN >= floor` is false. **Put the coercion in ONE public function on the value's own type and make every
  reader call it** (`MemorySignals.Salience`), the same discipline
  `ModulatedRetrievability.Declared` already applies within one file — and remember `Math.Max` PROPAGATES
  `NaN` per IEEE 754, so a clamp is not a finiteness guard.
- **A clamp is not a finiteness guard — `Math.Max`/`Math.Min`/`Math.Clamp` PROPAGATE `NaN` (IEEE 754).** <!-- trap: sub=memory,storage shape=silent-loss -->
  Promoted to its own entry because it landed **twice in four days in the same subsystem**, the second time
  *in a file whose neighbour documents the identical fact*: `ModulatedRetrievability` already warns about
  `Math.Max(1, NaN)`, and `DsrRetrievability.Reinforce` still shipped `Math.Max(0, increase)` as its safety
  floor. Both authors read the clamp as "this can only make it safe". Being written down was not enough —
  **route an implementer to this document in the dispatch**, because nothing surfaces it otherwise
  (`.claude/rules/skills-workflow.md`).
  Write `double.IsFinite(x) ? Math.Max(0, x) : fallback` wherever the input is derived arithmetic rather
  than a literal. The consequence is never local: `GraphMemoryEngine` feeds `Reinforce` straight into
  `TouchAsync`, so on the in-process store one `NaN` stability is **persisted permanently** — and `NaN`
  compares false against every threshold, so the entry neither ranks, prunes, nor reports as broken.
  <br>**Third occurrence, 2026-08-14, and it is the one that changes the prescription.** The two above were
  authors reading a clamp as a safety guard. This one is different and worse: `DsrRetrievability.Reinforce`
  had the guard, in that exact form, with a comment explaining that `Age` arrives per-call and therefore
  cannot be validated at construction — and then the difficulty law was added as a **second reader of the
  same `Retrievability(state)` value**, re-derived the finiteness question independently, and concluded the
  opposite in writing ("every term feeding `D''` is provably finite"). A `NaN` age produced a `NaN` grade and
  a persisted `NaN` difficulty, through `ExpandAsync`, which is the act this library's own docs recommend
  reinforcing on. **So the rule is not "remember to guard" — it is that a guard belongs to the VALUE, not to
  the call site.** When a second consumer of a already-guarded expression appears, it does not inherit the
  reasoning, and the only two shapes that survive are pushing the guard down to where the value is produced
  (here, `DerivedGrade` screening the retrievability once for both readers) or the `MemorySignals.Salience`
  treatment — one public coercion function every reader calls. Two call sites reasoning separately about the
  same double is the same defect as two call sites coercing it separately, which is the entry above this one.
  <br>**Fourth occurrence, 2026-08-16, on the WRITE path — and the prescription two lines up is what missed
  it.** `GraphMemoryEngine` shipped `_policy.InitialStability * Math.Max(0, tick.Encoding)` and
  `Math.Max(0, tick.Position)`, both persisted, both fed from the public `IMemoryAgePolicy.Advance` seam. The
  rule was already written HERE, in this entry, naming the exact replacement — and the search that would
  have found it is not "where do we clamp a derived double" but "**what does this seam RETURN, and is any of
  it stored**". `IMemoryAgePolicy.Age` carries a long paragraph promising a non-finite return "cannot corrupt
  the store, the review log, or anyone else's recall" because everything downstream that would persist the
  poison is defended. That was true of `Age`, which is never persisted, and false of `Advance` on the very
  next member — so the doc's own confidence was what made the gap invisible. **A contract paragraph that
  reasons about one member of a seam does not cover its siblings**, and the more thorough it is, the more it
  reads as though it does.
  <br>The two backends then disagreed about the poisoned write, which is worse than either answer: Postgres
  added `NaN` into the engine's running position permanently (every LATER entry reports a non-finite age, so
  nothing ranks and nothing prunes), while SQLite refused the bind and threw. Fixed by coercing to `1` —
  taken from `MemoryTick.One`'s existing definition of an ordinary write, rather than inventing a neutral.
- **A ceiling written as a bare `Math.Min(grown, max)` is a CUT, not a cap, for anything already above it — <!-- trap: sub=memory,tests shape=silent-loss,vacuous -->
  and the value it cuts is usually persisted.** `DsrRetrievability.Reinforce` shipped that shape through
  2.5.x, so recalling an entry whose STORED stability already exceeded `MaxStability` wrote the ceiling back:
  a stored `100000` returned `2000`, a **50× shortening**, violating `IMemoryRetrievabilityPolicy.Reinforce`'s
  own written "never smaller than the current one" guarantee — which the interface then *disclosed* for a
  release rather than closing. Reachable by lowering the ceiling under an existing corpus, or by any value
  written outside the policy. **The correct shape was already in the same file, one method away**:
  `EffectiveStability` has always written `Math.Max(current, Math.Min(current * factor, max))`, where the
  outer floor makes the bound cap GROWTH without ever acting as a CUT — an over-ceiling entry is *frozen*,
  which is all a growth ceiling ever claims to do. Two things generalise. **(1) Ask what a bound means for an
  input already past it**: "cannot grow beyond X" and "is never above X" are different promises, and `Math.Min`
  alone silently implements the second. **(2) A monotonicity guarantee needs a fixture that can actually
  violate it** — the contract fact that should have caught this (`Reinforcement_never_shortens_a_memory`) only
  ever ran on a state at `InitialStability`, structurally far below any ceiling, so it passed for two years
  while the guarantee was false. Fixed 2026-08-11 (`docs/task-archive.md` Part 54, DSR2).
- **A record where ONE field is domain-guarded and its neighbours are not is more dangerous than one with no <!-- trap: sub=di,memory shape=scope-blind -->
  guards at all** — the guard reads as evidence the record was audited. `DsrOptions.Decay` got a validating
  `init` in 2026-08-09's Task 1 (after a review found `Decay = 0` made everything permanently unforgettable
  while pruning still deleted); `InitialStability`, declared three lines away and equally load-bearing, kept
  none, and `InitialStability = 0` then reached `Math.Pow(0, -0.4)` = `+∞` → `0 × (1+∞)` = `NaN`. When a
  review forces a guard onto one option, **audit every other field of that record in the same fix**, and
  prefer a test that walks the whole options surface over one that pins the field just fixed.
- **A test whose recall silently returns nothing exercises only the write path — and stays green.** Hit <!-- trap: sub=tests,memory shape=vacuous -->
  twice, four days apart, both times while reaching for `InMemoryMemoryGraphStore` "for speed": 2026-08-09 a
  recall-quality guard measured a **~93% miss rate** and switched to `SqliteMemoryGraphStore`; 2026-08-10 an
  age-primitive identity test instrumented at **writes 25, queries 69, `TouchAsync` calls 0** — every recall
  returned `MemoryRecall.Empty` *before* `ReinforceAsync`, so deleting the store's entire touch stamping left
  the test green.
  **The CAUSE was fixed in 3.0 and the LESSON was not.** The cause was that only SQLite's FTS path split a
  query into terms; every other path matched the whole query as one contiguous substring, so
  `"item topic0 repeat0"` could never match `"item topic0 covers <filler> ordinary material…"`.
  `SearchTerms` now gives every backend the same split (`docs/DECISIONS.md` D55), so the old prescription —
  *any test whose subject is the RECALL or TOUCH path must run on SQLite* — no longer holds **for that
  reason**. Prefer SQLite anyway when the subject is RANKING, which the in-process store still has none of.
  What survives unchanged is how both were caught, and neither was caught by reading: **mutate the behaviour
  the test claims to cover and watch it stay green.** Note also that the second one shipped a guard
  *intended* to catch this — `Assert.True(comparisons > ids.Count)` — which 25 writes alone satisfy at 325
  comparisons, so it could never detect zero touches. **A guard that cannot observe the thing it guards is
  worse than none: it reads as coverage.** And the wider one, which is why this entry keeps its history:
  a cross-backend difference filed as *by design* had been quietly failing recall on two of three backends
  for a year, defended by a test that asserted the divergence rather than questioning it.
- **`EdgeHalfLife` exists on TWO options records, with the same name and the same default `100`, governing <!-- trap: sub=di,memory shape=wrong-subject -->
  DIFFERENT things.** `GraphMemoryOptions.EdgeHalfLife` decays an **edge's weight** during traversal — it is
  what stops the graph saturating, and it is read by the engine for every arm regardless of which
  retrievability policy is installed. `DsrOptions.EdgeHalfLife` decays **connection strength** inside the
  curve, feeding `EffectiveStability` and therefore retrievability. Found 2026-08-11 writing the 3.0
  migration guide; a review three days earlier had already been wrong about this once in the other
  direction, generalising "the shared one governs edge weight for every arm" into "nothing reads the
  policy's own copy" — which was false and reached a decision record before it was caught.
  **Both default to 100, so they agree by coincidence and nothing surfaces the confusion until someone tunes
  one and expects the other's behaviour.** When you touch either, name which one in the same sentence;
  "the edge half-life" is ambiguous in this subsystem and always has been.
- **A PERMANENT change driven by the system's own retrieval decisions is the dangerous shape — not merely an <!-- trap: sub=memory,measurement shape=wrong-subject -->
  unbounded one.** Five studies on 2026-08-12 across `GraphMemoryEngine`'s three retrievability-raising
  mechanisms, including two that refuted the investigator's own earlier framings, which is why the
  qualifications below are part of the entry rather than trimmed out of it.
  <br>**Measured:** salience (keyed on write-time content novelty) HELPS; the age reset a recall performs
  (age → 0) HELPS; stability growth HURTS badly, and **in every form tested** — the shipped compounding rule,
  a capped one, and one computed purely from the entry's recall COUNT so that it cannot compound by
  construction. Not growing at all beat all three on every corpus shape.
  <br>**Two framings this refuted, both plausible and both wrong.** "Reinforcement is net-harmful" was wrong
  because every arm still had the age reset on — the harm is growth alone. "A bounded effect is safe, a
  compounding one is not" was wrong because the non-compounding form still lost to not growing.
  <br>**What fits all of it**: the age reset EXPIRES (the entry decays again at the same rate), while growth
  PERSISTS (it decays slower forever), and salience persists but is keyed on content rather than on
  retrieval. So the combination that does damage is *permanent* × *retrieval-conditioned* — the system banks
  its own ranking error instead of letting it wash out. **The question to ask of any such mechanism: does its
  effect expire, and if not, is it driven by something other than the system's own output?** Offered as the
  hypothesis that survives, not as a proven law — it is the third framing in one day and has not been tested
  on its own terms.
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
  supports, and every number it produces is a best case.** Measured 2026-08-12: the same cluster-recall
  question scores ~**0.47** miss with a cue whose only live token reaches the target, and ~**0.82** with a cue
  carrying ordinary words that also appear elsewhere — a gap far larger than any policy effect measured that
  day. **Cue quality dominated every knob in the subsystem.**
  <br>The trap is the reflex, not the number: the overlapping cue was first dismissed as a stopword
  "mistake" and deleted. That is wrong here, because this store tokenizes FTS as `trigram` precisely so
  non-Latin text works, and **under trigram matching almost any two texts share trigrams — for CJK there is
  no stopword to strip and the contention is unavoidable.** So the "contaminated" case was the representative
  one for a large part of the audience, and the "clean" one was the artificial best case.
  <br>**Before calling incidental match contamination, ask which tokenizer the store actually uses and which
  languages it claims to support.** A repository named 灵台 with CJK storage tests does not get to measure
  only English.
- **A best-effort catch turns a BUG into "nothing matched", and you will debug the wrong layer for hours.** <!-- trap: sub=memory shape=fail-open -->
  Measured 2026-08-13 adding query-time semantic seeding: `GraphMemoryEngine.RecallAsync` wraps
  `GatherAsync` in `catch (Exception ex) { _logger.LogWarning(...); return MemoryRecall.Empty; }` — a
  deliberate best-effort promise, so a storage outage degrades to "no memory" rather than a throw. The cost
  is that **any** defect in the gather path produces an empty recall, which is indistinguishable from a
  query that legitimately matched nothing. With no logger attached — the default in a hand-built engine —
  there is no signal at all. Two implementations were debugged blind against a feature that was, on the
  third attempt, correct all along.
  <br>**Before concluding "the feature does not work", attach a logger and assert it stayed silent.** The
  fix is not to remove the catch (the promise is right) but to make the silence assertable:
  `SemanticSeedProbeTests` asserts an empty warning list, so a swallowed failure fails the test rather than
  looking like a null result. The general rule: **wherever a catch converts an exception into a plausible
  empty value, a test must be able to tell the two apart** — and that test is the one to write FIRST when a
  new code path in that method appears to do nothing.
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
  like a perfect score rather than a broken instrument.** Measured 2026-09-04 over LongMemEval's oracle
  variant, per class: `single-session-assistant` has a median of **8 turns**, and **63% of its questions have
  a store that fits entirely inside `k = 10`** — the first recall returns the whole conversation, so any
  shot curve over it is flat by construction and any recall metric reads ~100%. `single-session-user` is 11%
  and `single-session-preference` 0%, against 0% for the multi-session classes.
  <br>**This is Part 112's finding in its sharpest form** — there the oracle returned 40% of its store and
  "barely tests retrieval"; here it returns ALL of it. **Before scoring a new class, divide its store size
  by `k`.** If the answer is near or below 1, the class needs the haystack variant or a smaller `k`, and a
  high score on it is a statement about the fixture.
  <br>**And check for ZERO-EVIDENCE questions in the same pass**: 6 knowledge-update, 8 multi-session, 6
  single-session-user and 1 temporal-reasoning question carry no flagged turn at all. They are unscorable by
  any evidence metric, so a loader must drop them or the denominator silently lies — `Load` already does for
  the two classes that run, and a new class needs the same guard rather than inheriting it by luck.

- **A sample size can hide a CRASH, not only a wrong number — so run the instrument once at the size you <!-- trap: sub=measurement shape=silent-loss -->
  intend to draw conclusions at.** Measured 2026-09-02 (`docs/FIXES.md`): `memory-locomo --retrieval` had
  only ever been run at `--n 200`, and its `+forget0+oracle` arm could not be CONSTRUCTED at `--n 1540` —
  the oracle keys evidence by question text and LoCoMo repeats 11 QA rows verbatim inside `conv-48`, so
  `ToDictionary` throws the moment the sample draws both copies. The arm whose 77.5% ceiling was quoted in
  three maintained records had therefore never run on the whole benchmark. **The failure was in the
  INSTRUMENT and reachable only at a size nobody had used**, which is why "it has always worked" says
  nothing about a size you have not tried.
  <br>**Its silent twin is the part to remember**: the QA path makes the same uniqueness assumption and
  says nothing, because `dict[key] = value` OVERWRITES where `ToDictionary` throws. One shape fails loudly
  at n = 1,540 and the other quietly returns a plausible table. **When a key turns out not to be unique,
  audit every OTHER use of that key** — the throwing one is the lucky case.
  <br>**And two facts about the LoCoMo instrument itself, so nobody re-derives them:** question text is not
  unique within a conversation (11 exact duplicate rows, category 4, identical evidence and gold), and
  **`dia_id` is conversation-scoped rather than global — 871 of 1,033 belong to more than one conversation**
  (`D1:1` exists in all ten). An evidence index keyed globally on `dia_id` would silently endorse another
  conversation's turn, and every arm would still look plausible.

- **A measurement that cannot observe a change reports "nothing moved", which reads exactly like "no <!-- trap: sub=measurement,memory shape=vacuous,wrong-subject -->
  regression".** Measured 2026-08-12 reconciling `GraphNode.Relevance` across the three backends: both
  `MemoryDefaultRecallQualityTests`'s fixed-corpus pin and the 28-minute `memory-sweep` came back
  bit-identical, which looks like strong evidence the change is safe. It is not evidence at all — `MemoryCorpus`
  contains **zero `MemoryGrade` references**, so it holds no authoritative material, and the changed
  behaviour (what an authoritative node the query did NOT match reports) is structurally unreachable on that
  instrument. The right claim is "not exercised", and the change is carried by contract facts on all three
  backends instead. This is the same shape as the guard-that-cannot-observe entry under Testing below, one
  level up: **before citing an unchanged measurement as reassurance, ask whether that instrument can express
  the thing you changed** — and prefer a corpus/fixture audit (`grep` for the feature) over inferring it from
  a stable number.
  <br>**Sequel, 2026-08-13, and it is the reason this entry is worth re-reading rather than filing away:**
  the gap above was correctly identified, correctly written down here AND in `CLAUDE.md` — and then left
  open, because documenting a blind instrument feels like handling it. Teaching the corpus to express the
  promise (`CorpusShape.AuthoritativeCount`, opt-in and byte-identical when unset) took one afternoon and
  immediately found the library's **highest-priority** promise broken in all five languages — objective (1),
  the one with no acceptable failure rate, cut by `Take(limit)` for the whole life of the feature
  (`DECISIONS.md` D56). **A documented blind spot is still blind.** If the instrument cannot express the
  promise, the fix is the instrument, and the cost of not fixing it is bounded only by how important the
  unmeasured promise happens to be.
  <br>**Sequel, 2026-08-21, and it is the trap on the OTHER side of that advice.** "Build the instrument" was
  about to be applied to `TASKS.md` Part 65's `many-candidates` item by sweeping
  `ReciprocalRankFusionOptions.SalienceWeight` — a knob that already exists, over a corpus that already
  exists, mirroring `MemorySpacingSweep` almost line for line. It would have returned a **perfectly flat
  curve**, and the flatness would have been an artifact end to end. Two facts compose, and neither is visible
  from the sweep: `GraphMemoryEngine.Probe` returns `(Novelty 0, Comparables 0)` when there is no vector
  search, so **without an embedder `StructuralSaliencePolicy` declines on every single write** and salience is
  uniformly absent; and `ReciprocalRankFusionPolicy` ranks by COMPETITION (**D82**), so a signal on which
  every candidate ties contributes the same constant term and **cannot move the ordering at any weight**. Arm
  0 and arm 4 are then the same engine.
  <br>The output would have read as a clean exoneration — flat, tight CIs, every control green, because every
  control that exists checks that the arms carry their own *weights*, which they would have. **Ask not "does
  each arm carry a different knob value?" but "does the SIGNAL that knob scales actually VARY on this
  corpus?"** — and assert the second one in the study, as a distinct-value count, the same way
  `MemoryCorpusGoldenTests` pins that a language arm genuinely differs. A knob that scales a constant is
  unmeasurable, and nothing about the shape of the experiment says so.
- **The guard that stops a contract fact being wired to one backend only counts the CONTRACT's declarations, <!-- trap: sub=tests,storage shape=scope-blind -->
  not what each backend CALLS — so it is blind in exactly one direction.** Measured 2026-08-14: all 68
  `MemoryGraphStoreContract` facts are wired on all three backends today (68/68/68), so this is latent, not
  live. But `PostgresStorageTests`' `Assert.Equal(declared, covered)` compares a reflected count of the
  contract's public statics against a hand-bumped literal — it catches a fact added and wired NOWHERE, and a
  fact added and wired to InMemory/Sqlite but not Postgres, and it does **not** catch a fact added and wired
  to **Postgres alone**: the author bumps `covered`, the assertion passes, and the invariant the entry above
  exists to protect is enforced on one backend. Closing it properly means driving all three through a
  reflection-fed `[Theory]` so exhaustiveness is structural rather than counted — deliberately not done days
  before a freeze, because it restructures ~200 tests to close a gap with no live instance. **Re-measure with
  a per-backend call census (parse each test file for `MemoryGraphStoreContract.<name>`) rather than trusting
  the count**, which is how the 68/68/68 above was established.
- **A cross-backend invariant enforced on ONE backend's test class is not enforced.** The non-finite-salience <!-- trap: sub=tests,storage shape=scope-blind -->
  guard above was pinned only in `SqliteMemoryGraphStoreTests`, so the in-process store's own divergence
  survived a full review. If a fact is about the CONTRACT, it belongs in `MemoryGraphStoreContract` and must
  be wired to all three backends (`InMemory`, `Sqlite`, `Postgres` — the last also bumps the `covered` count
  that guards against a silent skip). Keep a backend-specific assertion only where it genuinely cannot be
  portable — reading a raw column, for instance.

- **An id that is unique WITHIN one engine is not unique across a composite, and keying on it alone works <!-- trap: sub=memory,measurement shape=silent-loss -->
  right up until there are two members.** Measured 2026-08-30 merging the two field harnesses onto
  `MemoryWalk` (`docs/task-archive.md` Part 120): both keyed accumulated results on `MemoryRef.Id`, and both
  got away with it only because each ran a single engine — on a composite, two members may each own id
  `"1"`, so one silently overwrites the other. **Identity is the whole `MemoryRef`.**
  <br>**The tell is that the code is correct for the configuration you test and wrong for one you ship**, so
  no test fails and no review catches it; `CompositeMemoryEngine` is what makes it reachable. Wherever
  results from more than one member are pooled — a dictionary, a `HashSet`, a dedup — check what the key
  actually identifies.

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
  documented knob stops being wired — and the compiler is structurally unable to notice.** Measured
  2026-08-14 (`docs/FIXES.md`): `MemoryEngineBuilder` built `GraphMemoryEngine` in both `UseGraph` (the
  configured path) and `UseBestAvailable` (what the one-line `AddMemory()` resolves to). `annotation:` and
  `verification:` were added to the first when those seams shipped and never to the second, so
  `AddMemory().AddMemoryVerification()` registered a policy that never ran, while the identical registration
  behind `AddMemoryEngine(…, e => e.UseGraph())` worked.
  **Three things had to line up, and they always will for this shape:** the parameters are OPTIONAL, so
  omitting them compiles; the fallback is a real, documented behaviour ("the model-free floor"), so nothing
  throws and no result is missing; and the only symptom is QUALITY, which a consumer enabling a feature for
  the first time has no baseline for. `check-warnings` cannot see it, the API baseline is unchanged, and
  every existing wiring test happened to exercise the other path.
  **The fix is one call site, never a second copy kept in step by review** — an override-taking private
  factory, with the zero-configuration caller passing nothing (null already means "take the container
  registration"). And the test that catches it must assert the policy was **CONSULTED**, not that recall
  still worked: a recording fake whose `Seen` list must be non-empty. This is the same family as the
  documented-but-unwired entry below, arrived at from a different direction — there the knob was never read,
  here it was read on one path of two.
- **A `TryAddSingleton` reached during `configure(builder)` BEATS `AddLyntai`'s own options-built <!-- trap: sub=di shape=ordering,silent-loss -->
  registration.** All of this is in `src/Lyntai.Core/DependencyInjection/ServiceCollectionExtensions.cs`:
  `AddLyntai` invokes `configure(builder)` well before it calls `RegisterLlmFrontDoor`, and that method is
  where the `DeadHostTracker` built from `LyntaiOptions` is registered. **Names first, lines second** — these
  numbers rot, and this very entry was made stale by a change inside the branch that added it; follow the
  method names if a line disagrees. **It has now rotted TWICE** (a 2026-08-05 audit found all four numbers
  pointing at a blank line, a doc comment and unrelated calls), so the numbers are gone: cite `AddLyntai`,
  `RegisterLlmFrontDoor` and `RegisterProviderLifetime` by name, which is the only form that cannot rot a
  third time. So a `TryAddSingleton<DeadHostTracker>()` added inside a `Use*`/`Add*` extension reaches the
  collection FIRST, and `TryAdd` keeps the first — silently swapping the configured `DeadHostThreshold`,
  `DeadHostCooldown` and logger for the parameterless defaults, **for both domains**. Nothing in 1427 tests
  noticed; it was found by mutation (adding the line failed exactly one new guard test and nothing else) and
  confirmed twice. The rule:
  inside a builder callback, resolve what `AddLyntai` registers later with `GetRequiredService<T>()` — never
  seed it with a `TryAdd`, and if you need a service that must exist regardless, register it in the
  Register* block that owns it. `RegisterProviderLifetime` is all `TryAdd` deliberately for the mirror-image
  reason: everything it seeds is meant to lose to a host or a `Use*` call.

- **A seam whose EMPTY registration means "take the default" has no off switch, and the arm you build to <!-- trap: sub=measurement,memory shape=vacuous,fail-open -->
  turn it off is the shipped behaviour wearing an OFF label.** Measured 2026-08-30 (`docs/FIXES.md`).
  `GraphMemoryEngine.NormalizeSaliencePolicies` substitutes a fresh `StructuralSaliencePolicy` for a null or
  empty collection — deliberate, documented, and the reason `NeutralSaliencePolicy` exists at all.
  `MemorySalienceSweep` nevertheless built its control arm as `saliencePolicies: null`, so **the arm labelled
  `SalienceOff` ran the shipped policy at the shipped weight** for the whole life of the sweep. Its tables
  compared retention-on against retention-off with salience's *admission* consumer live in both arms, while
  the sweep's preamble, its class doc and `docs/memory.md` all described it as measuring both consumers.
  <br>**Nothing in the harness could have caught it.** The build is green, every existing control is green,
  the arms carry different options objects, and the numbers are plausible in both sign and magnitude.
  <br>**And it is the SECOND time this exact trap bit, which is the part worth carrying.** The first was in
  the test tier, and it is written up inside `MemorySalienceInversionTests`: *"The first three-arm run here
  asserted the control judged nothing salient and got 255 — the 'control' was a second copy of the
  treatment."* That incident is why `NeutralSaliencePolicy` exists at all; its own note says a trap that
  costs a measurement its control belongs fixed in the library rather than in one file. By 2026-08-30 the
  rule was written down in **four** places — that type, a test pinning it
  (`An_empty_policy_collection_leaves_salience_ON_and_only_the_neutral_policy_turns_it_off`), `TASKS.md` Part
  65 verbatim ("registering an empty collection does NOT — that takes the shipped default"), and two sibling
  sweeps doing it correctly — and a harness written afterwards still did it wrong.
  <br>**What separated the two incidents was not knowledge, it was the CONTROL.** The test tier caught its
  version in one run because it reports `SalientWrites` per arm and asserts the control's is zero; the bench
  sweep counted salient writes only on its treatment arms, so its off arm contributed no row and there was
  nothing to be non-zero. **Writing the rule down again is not the fix — porting the control is.**
  <br>**What exposed it was widening the study, not checking it.** The confound was invisible on the two
  corpus shapes the ladders ran; going to six put a provably-silent arm significantly *worse* than "off" on
  two new shapes, by more than the entire spread of the arms being ranked. **A control arm that differs from
  a treatment by more than the treatments differ from each other is reporting a confound, not a result** —
  that comparison costs nothing and is worth making on any ladder.
  <br>**The general rule: for any seam with a default-on fallback, the off arm must be an explicit neutral
  IMPLEMENTATION, and a control must assert it was CONSULTED and DECLINED.** Asserting "the signal is
  absent" is not enough — absence is exactly what a never-registered policy also produces, and it is what
  the broken arm reported. This is the same shape as the `MemoryEngineBuilder` entry above ("assert the
  policy was CONSULTED, not that recall still worked"), arrived at from the instrument side.

## Copying a rule copies its assumptions

- **A rule moved from where it was true to where it is not — the shape behind three of the four regressions <!-- trap: sub=generation,memory shape=unmeasured,wrong-subject -->
  a single review round introduced (2026-08-15, `docs/FIXES.md`).** Each looked like careful reuse of an
  existing, correct decision, and each carried an unstated premise that held only where it came from:
  · `ComfyUiProvider`'s poll rule — *"a 4xx is terminal, a 5xx is transport"* — copied onto
    `FalQueueProvider`. True for a loopback server that never rate-limits; on a hosted, paid, rate-limiting
    API one `429` permanently dead-lettered a render that was still running and already billed. **The
    premise "this backend does not rate-limit" was never written down, because where the rule was written
    it was always true.**
  · `IMemoryGraphStore.SeedAsync`'s portable guarantee — *"a node whose content contains a query token is
    found on every backend"* — read as a CEILING and used to justify narrowing the one backend that matched
    more. It is a FLOOR. **A minimum and a maximum are the same sentence in English**, and the difference
    only shows when you ask what the sentence FORBIDS.
  · `CompositeMemoryEngine`'s fan-out justification — *"removing one member and returning success leaves the
    blend holding the data"* — applied to the members that could remove and silently not to the ones that
    could not, which is the case it was written for.
  <br>**The fourth is the same shape aimed inward**: the same round taught `FalQueueProvider` to distinguish
  "never reached the backend" from "may have been delivered", and then wrote a `catch` in another file that
  treated every throw as ambiguous — turning a connection-refused blip into a dead-lettered job.
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
  <br>**Fifth instance, 2026-08-17, found by review rather than by a consumer:** `GenerationRouter.StreamAsync`
  carried the SUBMIT door's `NeverReachedTheBackend` filter on its catch — a filter whose premise is "the
  act of asking may have been billed", true of a queue submission and false of a stream open — so the one
  failure class fallback most exists for (a refused connection before the first byte) was the one that
  skipped fallback and escaped raw. The filter's own doc said "used ONLY on the submit path" the whole time.
  <br>**Sixth instance, 2026-08-23, and it is the one that PROVES the closing advice below rather than
  merely illustrating it — the violation was in the very same file, minutes later, by the hand that wrote the
  fix.** `MemorySalienceWeightSweep.PrintVerdict` was corrected to report miss AND pollution, with a comment
  saying in as many words that "reporting miss alone would have hidden the trade that decides it". The next
  function added to that file, `PrintLanguageVerdict`, summarised five languages by counting **miss-better
  shapes and nothing else**. It printed `5/5` for every language, which read as unanimous — and on that
  reading a SHIPPED ranking default was changed. The pollution column, once looked at, showed Korean's
  ordinary shapes trading a small miss gain for a larger pollution rise, which is the trade §5.7.0
  explicitly refuses. The default was reverted.
  <br>Two things sharpen the advice. **A per-item report and its SUMMARY are two sites, and the summary is
  the one people act on** — fixing the detail view while the roll-up still hides the same column is worse
  than fixing neither, because the roll-up now looks endorsed by the corrected detail beneath it. And
  **"N/N better" is a count on ONE metric wearing the costume of a verdict**; if an objective is
  lexicographic, the summary has to evaluate the objective, not tally the primary term.
  <br>**What to actually do**, since "review harder" is not a technique: when you reuse a rule, write down
  the premise that makes it true THERE and check it holds HERE. If you cannot name the premise, you are
  copying a conclusion rather than reasoning. And when a rule you just wrote is about a distinction (this
  versus that), grep your own diff for the other places that distinction applies — the round that
  articulates a rule is the round most likely to violate it elsewhere, because attention is on the sentence
  rather than on the code.

- **ADDING a second way to configure something that already has one is where double-application comes from — <!-- trap: sub=di,memory shape=second-door,silent-loss -->
  not from decorators being decorators.** Measured 2026-08-31. `GraphMemoryEngine` gained
  `retentionPolicies` so retention could arrive as its own registered collection (D48's shape); retention had
  always arrived pre-wrapped in a hand-built `ModulatedRetrievability` passed as `retrievability:`. Supplying
  BOTH then wrapped an already-wrapped curve and applied retention TWICE, multiplying stability twice over,
  silently — and an entry outliving what any retention policy declared breaks `CandidateCutoff`'s superset
  guarantee, whose only consumer DELETES.
  <br>**The first proposed fix was to make `ModulatedRetrievability` internal**, which would have made the
  combination unreachable and left the author believing an ambiguity had been tidied rather than a data-loss
  path closed. It was rejected on the owner's rule — *a policy replaceable by injection belongs on the
  surface so other services can route on it* — which is the same standard `DsrRetrievability`,
  `SalienceRetentionPolicy` and `MultiplicativeRankingPolicy` already meet. **Hiding a type to remove an
  ambiguity fixes the wrong thing**, and here it would have hidden the bug with it.
  <br>**A sweep for siblings found none, and the NEGATIVE result is the useful half.** Nesting
  `ModulatedRetrievability` is safe (`CandidateCutoff` composes multiplicatively, so it tracks the true
  factor); the LLM front door is idempotent per decorator order and says why ("two rate limiters in series
  would double-charge permits"); the generation routers are factory-composed and applied once; the two
  `Composite*` types nest on purpose. **Every pre-existing decorator has exactly ONE application path.** So
  the rule is not "audit decorators" — it is: **when you add a configuration route, ask what the existing
  route was, and whether both can be supplied at once.** Report the collision at wiring time (**D85**) rather
  than picking one silently; either silent choice is a value the caller never asked for.

- **A doc that enumerates what a feature does NOT do, without ever stating what it DOES, is read as <!-- trap: sub=docs,memory shape=scope-blind,vacuous -->
  "nothing" — and the reader who reaches that conclusion turns the feature off.** Measured 2026-08-22/23,
  twice over the same sentence. `IMemoryVerificationPolicy`'s summary said a verifier *"only narrows what a
  recall already found; it cannot add an entry, and by default removes none from the caller's answer
  either"*. Every clause was true. What it never said is that with filtering OFF a verdict **promotes** every
  endorsed candidate to the front, before the caller's limit is applied and over a candidate set
  `VerificationDepth` deep — the effect the whole seam exists for. An adopter read the source, concluded a
  verdict cannot reach the ranking at all, built an app-side promotion step on that belief, and reverted it
  when four successive fixtures passed with the promotion disabled.
  <br>**The second half is the part worth carrying.** Their CORRECTION was also wrong, in a way that looks
  much more careful: it kept "no re-sort is written anywhere" and explained the movement it had now observed
  as retention state updating underneath the ordering. There is an explicit promotion, ten lines above the
  filter, in every release since 3.0.0 — and the retention story cannot be right for the same call, because
  the ranking reads state gathered before `ReinforceAsync` runs. **A plausible mechanism invented to explain
  a real observation is harder to dislodge than the original error**, because it arrives labelled as a
  correction.
  <br>What actually settled it was three deterministic facts, not a closer reading:
  `MemoryVerificationOrderingTests` pins that an endorsed candidate leads the page, that one below the limit
  is RESCUED onto it, and — the control that keeps the other two honest — that a judge endorsing what already
  leads returns a byte-identical page. That last one is the adopter's own null result, and it is a fact about
  the CORPUS rather than about the wiring.
  <br>Two rules. **When you document a default posture, say what it DOES first and what it withholds second**
  — "removes none" after "promotes" is a qualifier, before it is a denial. And **before writing a paragraph
  about behaviour, write the fixture that would tell the two readings apart**; both readings here were
  reachable from the source, so no amount of re-reading could have chosen between them.

## Second doors

- **A field accepted on WRITE and absent from the READ record is a write-only field, and every gate on earth <!-- trap: sub=memory shape=silent-loss,scope-blind -->
  says it is fine.** Measured 2026-08-27 (**D93**). `MemoryWrite.Metadata` was accepted, persisted into
  `GraphNode.Metadata`, returned by all three stores — and then dropped at the **three** lines in
  `GraphMemoryEngine` that project a node onto `MemoryItem`. It compiled, every test passed, the data really
  was in the database, and a consumer wanting it back had to keep a second copy of the store outside the
  library and re-read it after every recall. Nobody noticed for four releases.
  <br>**Three, and the fix found two — because a target-typed `new(...)` is invisible to a search for the
  constructor's NAME.** `grep "new MemoryItem("` returns the two sites written longhand; the third, the entry
  a caller NAMED in `ExpandAsync`, sits in a collection initializer where the element type is already known
  and is written `new(reference, …)`. So the repair, the changelog entry, `DECISIONS.md`, `docs/memory.md`,
  the XML doc and this paragraph all said "two lines" while the projection that drops the caller's OWN entry
  was still live — one search's blind spot copied into six records, each of which reads as independent
  confirmation. **Count a record's construction sites from the TYPE, not from its constructor's name**: the
  compiler knows them all (rename the record, or comment out a member and read the errors), and every
  target-typed form — `new()`, a collection initializer element, a `return new(...)`, an implicit array —
  is unreachable by text search. The same applies to `default`-shaped and `with`-shaped writes.
  <br>The measured consequence beyond the miss: `ExpandAsync` returned the named entry with null metadata
  **beside neighbours that had it**, inside one `MemoryRecall` — a self-inconsistent result, which is worse
  to debug than a uniformly missing field because it reads as data-dependent.
  <br>**The tell is a pair of near-mirror records where one side carries a member the other does not** —
  here `MemoryWrite`/`MemoryItem`, and separately `MemoryVerificationCandidate`, whose interface doc promised
  "best-effort over a model-free floor" while giving an implementer nothing to compute a floor FROM. A
  seam whose only possible implementation is the one already shipped is the same defect wearing a doc
  comment.
  <br>**The cheap detection, worth running whenever a write record grows a member:** diff the write record's
  members against the read record's, and for each one the read side lacks, decide out loud whether that is a
  design choice or an omission. `Signals` is a defensible omission — it is the engine's bookkeeping, not the
  caller's. `Metadata` was not, because the caller wrote it.
  <br>**And "the projection passes the instance through" is not a test.** `CompositeMemoryEngine` preserved
  metadata by re-using its member's `MemoryItem` rather than rebuilding it, which is true today and is
  exactly what a later refactor that maps items — to renormalize a score, say — would undo with every other
  fact still green. Pin the round trip; mutating the pass-through to `i with { Metadata = null }` is a
  two-minute check that the pin is real.
  <br>**And a contract fact covers the METHOD it calls, not the property it is named for.**
  `Metadata_written_is_returned_or_explicitly_absent` runs on all five engines, which reads as exhaustive —
  and it only calls `RecallAsync`, so the whole expansion path stayed uncovered and the third projection
  stayed broken through the very fix that named it. When a fact asserts a property of "what a read returns",
  enumerate the READS as deliberately as you enumerate the implementations.

- **Inserting a member ABOVE an existing one strands that one's doc onto your new member.** Measured FOUR <!-- trap: sub=docs shape=stale-claim -->
  times on 2026-08-16/17, by the same hand, in one session — adding a private helper before its neighbour
  left the neighbour's `<summary>` (or, twice, only its `<param>` tags) attached to the newcomer, so the
  newcomer carried two docs and the member below carried none or half. It is a JSDoc problem too: the same
  edit in `check-counts.mjs` separated `countGuardTests`' block from its function.
  <br>**The insertion anchor is a declaration; the thing above it is somebody else's doc.** So the check is
  not "did I write my doc correctly" but **"what was already sitting above the line I anchored to?"** Anchor
  below the neighbour's doc, or move the doc with the declaration.
  <br>Every one of the four was caught by `check-comments`' stacked-`<summary>` rule and none by the compiler
  — duplicate `<summary>` is not a warning, the XML stays well-formed, and it SHIPS. The two `<param>`-only
  cases did warn (CS1571 duplicate param tag), which is why they were found in seconds and the summary cases
  were not found for a release.
- **An invariant DOCUMENTED in one implementation is not a contract, and the backend that wrote it down is <!-- trap: sub=storage,tests shape=second-door,scope-blind -->
  usually the only one that keeps it.** Measured 2026-08-16, three times in one pass, all in storage:
  `InMemoryVectorStore` carried a top-k tiebreak and a doc calling it *"load-bearing, not tidiness"* while
  neither persistent backend had one; `MemoryGraphSql.MinimumStability` was hoisted with a doc saying *"two
  literals is how that happens"* while the in-process store was the second literal — and spelled it as a
  SUBSTITUTE where SQL FLOORS, which is identical at the value the guard was written for and different for
  everything between it and zero, on a DELETE path.
  <br>**The tell is a doc that argues for a rule in the first person.** Prose explaining why THIS class does
  something careful is evidence the author knew it mattered and no evidence that anyone else does — so the
  question is not "is this implementation correct?" but **"is this a promise, and if so what holds the
  others to it?"** In this repository the answer is a shared contract class every backend runs
  (`VectorStoreContract`, `MemoryGraphStoreContract`), never a per-backend test file.
  <br>A single-backend test file with a rule's name on it is the same smell as the doc:
  `InMemoryVectorStoreTiebreakTests` existed, was thorough, and proved the property for the one backend that
  already had it. **Moving a fact into the contract is the fix; adding a second per-backend test is the
  defect repeated.**
  <br>**And a green cross-backend run does not mean the backends AGREE for the same reason.** SQLite passed
  the tiebreak facts before the fix, because `PRIMARY KEY (collection, vec_id)` gives it an autoindex the
  plan happens to walk in `vec_id` order — an accident that a rewrite, an `ANALYZE`, or a different plan
  changes silently. When a contract fact passes on a backend you expected to fail, find out WHY before
  believing it.
- **A shared helper whose doc says "EVERY read site calls this" is making a claim nothing checks — and the <!-- trap: sub=memory shape=second-door,stale-claim -->
  site that does not call it is usually the newest one.** `MemorySignals.Salience` was extracted for exactly
  this reason and says so, naming the three readers that once "normalized the same value three different
  ways, which made identical data admit differently on different backends". Measured 2026-08-17:
  `SalienceRetentionPolicy` was a FOURTH reader, added later, that spelled the read out itself — and got it
  wrong in the one way the helper exists to prevent (`Math.Clamp` propagates `NaN` where the helper coerces
  it), returning a factor outside the range its own interface promises.
  <br>**The cheap detection, worth running whenever you extract a coercion:** grep for the raw accessor the
  helper wraps (here `Get(WellKnown.Salience`) and check every hit is inside the helper. A helper's
  usefulness is entirely in being universal, so the enumeration in its doc is load-bearing — and it is prose,
  which means it was true when written and nothing has held it true since. Prefer routing the new caller
  through the helper over adding a second correct copy of the guard: two correct copies is how the three
  incorrect ones started.
- **A SPEND cap is a capability too, and its second door is the one that moves the money.** Measured <!-- trap: sub=generation shape=second-door,fail-open -->
  2026-08-16. `GenerationFetchTool` and `GenerationRenderJobHandler` both fetch a finished render; only the
  handler recorded its cost. Because a queue backend prices at FETCH — the only point the total is known —
  the unbilled door was the one carrying the entire cost, and `GenerationSubmitTool` then re-checked its cap
  on every submit against a total that could not grow. A configured cap never fired.
  <br>**The tell is a check and a record living on different paths.** A limit is two halves — something
  reads the total, something else adds to it — and reviewing either half alone reads as correct. So the
  question is not "is this path budgeted?" but **"where is the total INCREMENTED, and does every path that
  spends reach it?"** A cap whose total never moves fails silently and permissively, and looks like a
  generous limit.
  <br>Coverage was no help: a test already drove submit → status → fetch end to end and asserted nothing
  about usage. **Exercising a path is not testing its accounting.**
- **A capability enforced at one entry point is not enforced if a second entry point reaches the same <!-- trap: sub=di,generation shape=second-door,fail-open -->
  objects.** Measured 2026-08-15. `ToolLoop.GatedInvokeAsync` runs every model-driven tool call through
  `IGuardRail` — that gap was found and closed for the tool loop in 2026-07 and recorded as a security item.
  The hosted MCP endpoint, added later and in a different package, executes **the same `ITool` instances**
  (`sp.GetServices<ITool>()`) through `ToolFunction`, which called `tool.InvokeAsync` directly. So an app
  that registered a guard had it applied on one door and silently skipped on the other — and neither
  `ChatOrchestrator` gate could see it, because gate 1 sees the user message and gate 2 sees only the final
  answer. A `grep` for `guard` across both MCP packages returned one unrelated comment.
  <br>**The tell is shared STATE, not shared code.** The two paths have nothing in common to review side by
  side; what they share is the collection they both resolve. So the question that finds this is not "is this
  code guarded?" but **"what else can reach these objects, and does it apply the same rules?"** — asked
  whenever a new surface is given the app's tools, stores, or provider collections.
  <br>The same shape is worth checking for any capability that reads as a global promise: guards, budgets,
  rate limits, redaction. Each is enforced by a specific wrapper at a specific door, and adding a door is
  the cheapest way to lose one.
  <br>**Third instance, 2026-08-16 — and this time the compiler found it, which is the point.** Adding
  `IGenerationRouter.StreamAsync` as a required member with NO default body turned "a decorator silently
  fails to govern the new door" into a build error naming both offenders — and there were **two**,
  `BudgetedGenerationRouter` and `RateLimitedGenerationRouter`, where only the first had been anticipated. A
  default interface implementation would have compiled clean and shipped an ungoverned path. **When adding a
  member to an interface the library itself decorates, no default body is the cheap gate**; it costs BYO
  implementers a compile error in a major, which is exactly when that is affordable. Note the limit, though:
  the compiler forces a decorator to HAVE the member and cannot tell a governed implementation from a
  pass-through, so the behaviour still needs a test per door (`GenerationGovernanceTests`).
- **A constant does not live in one place just because it was written once — and the copy nothing ENFORCES <!-- trap: sub=generation shape=stale-claim,second-door -->
  is the one that rots silently.** Measured 2026-08-16 making the local-diffusion size ceiling configurable
  (`docs/DECISIONS.md` **D68**). The hard-coded 768 lived in **three** places, and each was found a different
  way. The **scale** was obvious. The **rounding** re-clamped through a second copy, so raising the cap would
  have moved the scale and left every result pinned at 768 by the rounder — a knob that appears to work and
  cannot exceed its old value; found only by writing the test at 1024 *because* that is above the old
  constant. The **advertisement** — `GenerationCapabilities.Limits["max-width"]` — was found by a human
  reading the diff and asking where the number came from.
  <br>**The third is the general lesson.** `Limits` is documented as informational: *"the platform does not
  enforce them (only the backend knows the real rule)"*. So a stale value there fails no test, trips no gate,
  and breaks nothing — the backend simply **lies to consumers who plan against a published ceiling**. An
  enforced constant is corrected by its own failure; an advertised one has no such feedback, and the only
  thing that reads it is a person. **When a value becomes configurable, grep the number itself, not the
  identifier** — the identifier finds the code paths, and the literal finds the documentation of them.
  <br>Related: with no ceiling the keys are now OMITTED rather than set to something large. An absent key
  already reads as "not enumerated"; a number would have been a limit nobody measured, which is the same
  defect wearing a bigger value.
- **A rule that is right for the only implementation exercising it is a coincidence, and the second <!-- trap: sub=cli shape=ordering,silent-loss -->
  implementation is where that shows.** `CliProviderEngine` appended an `ICliToolProvisioner`'s args after
  the dialect's argv. Correct for `claude`, whose argv ends in options; wrong for `codex`, whose argv ends
  in the `-` stdin positional, where everything after it is read as PROMPT text and **a swallowed flag is a
  spent turn rather than an error**. `CodexExecArgs` had documented that hazard and taken an `extraOptions`
  parameter for it; the agent path used it and the completion path structurally could not
  (`docs/DECISIONS.md` **D65**). **When a shared engine composes something whose correctness depends on a
  backend's own grammar, the backend places it** — the engine may supply the pieces, not choose the order.

- **A member of a variation-point collection that the ROUTING RULE can never select is inert, and the <!-- trap: sub=memory,di shape=ordering,fail-open -->
  registration is what makes it look wired.** Measured 2026-08-21, reported by two adopters on 3.0.0
  (`docs/task-archive.md` Parts 89 and 90; the answers are `docs/DECISIONS.md` **D85**).
  `CompositeMemoryEngine.RememberAsync` routed a write to the FIRST member supporting its grade, which is
  correct and documented — but in `UseGraph().UseSemantic()` the graph supports both grades, so it took every
  write and the semantic member's store stayed permanently empty. Nothing threw, `Supported` widened, the
  builder read as if both were live, and the only symptom was recall quality. The same shape one layer out:
  `AddMemoryVerification()` registers a policy only a graph member consults, and its absence renders as
  `Answered = null` — indistinguishable from a judge that ran and abstained.
  <br>**This repository's own README carried an instance of the reported defect**
  (`e.UseLexical().UseSemantic()`), which is the part worth carrying: the documentation is written by whoever
  understands the routing best, and it is exactly as blind to this as a consumer is, because *adding* the
  member is the whole visible act.
  <br>**The question that finds it is not "is this registered?" but "which member does the selection rule
  actually PICK, and what happens to the others?"** — asked of any DI collection whose consumer picks ONE
  (a first-match write route, a keyed lookup, a type-test) rather than iterating. And when you add the check:
  **a rule that also fires on correct wiring is worse than no rule.** The narrow version here reports a member
  only when EVERY grade it supports is already claimed, which keeps the documented
  `UseCurated("glossary").UseGraph()` blend silent; the naive "shares a grade with an earlier member" version
  fires on it, and a warning people learn to skip is the `check-warnings` ENOBUFS failure in a different hat.

- **A projection the OWNING STORE does not hold is a second door onto every removal verb, and the shared <!-- trap: sub=memory,storage shape=second-door,scope-blind -->
  contract that would catch it structurally cannot see it.** Measured 2026-08-26 (`docs/FIXES.md`).
  `GraphMemoryEngine` indexes each write into an `IVectorStore` collection it addresses itself, with the
  entry's **full content as the payload** — and neither `ForgetAsync` nor `PruneAsync` touched it, so the
  path `IForgettableMemory` documents as *"what an application calls when a user withdraws consent"* left
  the content readable at rest.
  <br>**The instructive half is that the identical defect had already been caught, twelve days earlier, on
  the other index.** Orphaned SUBJECT rows after a delete are pinned in `MemoryGraphStoreContract` — a fact
  every backend runs — because subjects live INSIDE `IMemoryGraphStore`. Vectors live in a different seam,
  so the contract that exists to make removal complete had no reach over the second projection and reported
  green. **A cross-backend contract proves a promise about the store it is a contract FOR**, which is
  precisely as far as it goes, and the further the composition spreads the less of the promise that is.
  <br>Nothing was missing to notice: `IVectorStore` has had `DeleteAsync` and `RemoveCollectionAsync` all
  along. And nothing surfaced it, because an orphan cannot appear as a recall ITEM — the gather path drops
  an id the node store no longer resolves — so the symptoms were data at rest plus a silently wasted seed
  slot, neither of which any assertion was watching.
  <br>**Ask of any removal verb: what else did the write touch?** Enumerate the write path's side effects
  (index rows, vectors, caches, blobs, review logs) and check each has a removal counterpart — the write is
  where the doors are visible, and the removal path is where they are not. Two more rules the fix turned up,
  both general. **Order the two deletes by which failure you can live with**: erase the projection first
  where a residue is the defect (a consent withdrawal), the store first where an orphan is (capacity
  pruning). And **derive a projection's address from the records being removed rather than matching a
  prefix** — `{engine}|{task}|{scope}` with a separator either field may contain means a sweep for task
  `"t"` also matches task `"t|x"`, and over-deleting is the one direction a removal must never err in.
  <br>**Sequel the same day, in the fix itself: an asymmetry in ORDER needs the matching asymmetry in ERROR
  HANDLING, and getting one without the other is worse than neither.** The fix ordered the two verbs
  deliberately opposite ways and then gave both the same uncaught propagation — so a failing index threw out
  of `PruneAsync` *after* the nodes were already deleted, losing the COUNT and leaving the caller unable to
  tell that the prune had in fact succeeded. `IPrunableMemory` documents itself as best-effort ("removing
  fewer entries than hoped is a deferred cost rather than a defect"), and the honest degradation there is
  the orphan the code had just been written to avoid — which is fine, because it is exactly the state every
  prune left behind before. The tell: **the doc comment already argued the asymmetry** ("the cheap failure
  is an orphan rather than a residue") and the `try`/`catch` did not implement it. When you write down why
  two paths differ, check every mechanism that expresses the difference, not just the one you were editing.

- **When a decision falsifies a claim, grep the CLAIM — not the file you happened to be reading.** Measured <!-- trap: sub=docs shape=stale-claim,second-door -->
  2026-08-30 (`docs/task-archive.md` Part 126). The 3D survey established that `3d → image → video` chains
  nothing, and the false sentence was corrected in `GenerationKinds.Model3d`'s shipped XML doc — while the
  IDENTICAL claim in `README.md` survived two consecutive passes, because each fix was made where the defect
  was FOUND rather than everywhere the claim lived.
  <br>**No gate can catch this shape**: `check-docs` only knows vocabulary a decision RETIRED, and a claim
  going false retires no word, so the sentence stays grammatical, plausible and wrong. The cost is one
  `grep` for the claim's distinctive phrase at the moment you correct it, against a reader implementing the
  wrong thing later.

- **A grep with context shows you a method's BODY, and inferring its NAME from the lines around it is how a <!-- trap: sub=docs shape=unmeasured -->
  member that does not exist gets into prose.** Measured 2026-08-30: a `-C 4` hit displayed
  `if (request.Inputs.Count > 0 && !SupportsInputs) return false;` with the signature just above the window,
  and the method was cited as `GenerationCapabilities.CanServe` <!-- link-ok: the WRONG name, quoted --> in an archive Part and in `TASKS.md` — two
  commits — before an unrelated test failed to compile. The real name is `Supports`.
  <br>**Read the DECLARATION before citing a member**, not the body: one `Read` at the right offset, or
  `grep -n "\(public\|internal\).*MemberName"`. A body tells you what a method does and never what it is
  called.
  <br>**Why nothing caught it, which is the transferable half.** `check-links` had three halves — path, Part,
  section — and a member name is none of them; `check-docs` only knows vocabulary a decision RETIRED, and
  `CanServe` was never a word here to retire; the compiler sees `///` crefs and not `//` comments or
  markdown. **A member name quoted in prose had no gate at all.** It has one now (`check-links`' fourth
  half), and the general lesson survives it: when you write an identifier into prose, you are making a
  checkable claim in a place nothing was checking.

- **A capability FLAG is a promise, and a flag nothing implements is a silent wrong answer rather than a <!-- trap: sub=generation shape=silent-loss,second-door -->
  missing feature.** Measured 2026-08-30 (`docs/FIXES.md`, `docs/task-archive.md` Part 124).
  `ComfyUiProvider` declared `SupportsInputs = true` and never read `request.Inputs` — the identifier
  occurred once in the whole file, in the declaration. **What makes this expensive is that the flag is an
  ADMISSION filter**: `GenerationCapabilities.Supports` returns false for an input-carrying request when the
  flag is unset, so declaring it does not merely describe the backend, it makes the router *choose* it for
  exactly the work it cannot do. The input was dropped, the graph ran as authored, and the render came back
  plausible and billed. Wrong from the commit that added the backend; it survived 26 days after the
  identical bug was found and fixed in a sibling backend, because the cure was written as one provider's
  comment instead of as a shared fact.
  <br>**The tell: grep the flag's own subject.** If a backend declares it consumes X, `X` should appear more
  than once in the file — once to declare, at least once to read. One occurrence means the declaration is
  the only thing that knows about it. This is cheaper than any reasoning about behaviour and it is the whole
  detection.
  <br>**And the fix belongs in the CONTRACT, not the provider.** Four backends already honoured this and one
  did not, which is precisely the shape a shared contract fact catches and a per-backend test never will —
  `GenerationProviderContract.A_handed_input_is_consumed_or_refused` now hands every HTTP backend an input
  and asserts it was either used or refused, never quietly discarded. Written against the unfixed tree it
  failed for one backend and passed for three, which is what makes it a real fact rather than a restatement
  of the bug. **A declaration/implementation pair is a second door**, so the rule generalises: whenever a
  capability, delivery mode or supported-kind list is DECLARED, something must assert the code path behind
  it exists — the sibling fact
  `Its_declared_deliveries_are_backed_by_the_interfaces_it_implements` was already doing this for delivery
  modes and its existence did not suggest the inputs axis to anyone.

- **"Pick the first `image/*` artifact" chains a texture ATLAS, which renders perfectly and is completely <!-- trap: sub=generation shape=wrong-subject,fail-open -->
  wrong — and the media type cannot save you.** From the 2026-08-30 3D-backend survey (`docs/task-archive.md`
  Part 124), a DESK survey, so these are shapes read from published API pages rather than measured calls.
  A mesh backend's only `image/*` outputs are **UV texture atlases** (Rodin's `textures[]`,
  `content_type: image/png`) — a flattened skin, not a view of anything — so a pipeline stage selecting by
  media type feeds the next stage a plausible image of nothing. Same silent-plausible class as the
  capability flag above.
  <br>**And branching on `MediaType` instead does not work either**, which is what makes this a trap rather
  than an oversight: Hunyuan3D reports its GLB as `application/octet-stream`, so the type is opaque exactly
  where the decision matters. **A stage that cannot positively identify a chainable artifact must REFUSE
  rather than fall back** — the fallback is the bug, and it is invisible from the contract, which is why
  `GenerationArtifact.ToInput(role)` takes an explicit role.

## Refactoring & namespace moves

- **Splitting a document silently breaks its BARE `§N` self-citations, and `check-links` cannot see one.** <!-- trap: sub=docs,gates shape=scope-blind,silent-loss -->
  Measured 2026-09-10 splitting `docs/memory.md` §5 out to `docs/memory-measurements.md` (**D114**): the <!-- link-ok: names the citation that MOVED, which is this entry's whole subject -->
  gate named all **141** citations that carry a filename, and was structurally blind to **ten** written as
  a bare `§5`/`§6`/`§7` — its `ANCHOR_PATTERN` requires a filename before the `§` **on purpose**, because
  "`design §7`" sits within a few words of an unrelated filename and a gate that guesses the target names
  the wrong file. Eight of the ten pointed from the old file INTO the section that had left it, and two
  pointed from the new file back at sections that had not. **Every one still rendered, resolved to nothing,
  and failed no gate.** Before splitting or merging any document, grep it for a bare `§` and decide each
  one by hand — the gate covers the easy half only. One of them lived inside a fenced `csharp` sample,
  where it also ships to a reader who copies the block.
- **A `§N` at the end of a sentence reads as `§N.`, so a fence written `(?!\.)` to exclude sub-sections <!-- trap: sub=docs,gates shape=scope-blind,silent-loss -->
  silently drops a THIRD of the population.** The same split: a sweep repointing `memory.md` §5 fenced the <!-- link-ok: names the citation that MOVED, which is this entry's whole subject -->
  digit with `(?!\d|\.)` so it would not match the design record's `§5.7`, reported a confident **96**
  citations across 28 files, and had skipped **43** — every citation that happened to close a sentence.
  Caught only by counting the same population a second way and not believing the first number.
  `(?!\d)(?!\.\d)` is the correct pair: reject `5.7`, accept `5.`. **The general shape — a lookahead
  written for one exclusion catches a second one nobody enumerated** — is the trap the withdrawn
  `retiredTerms` lookahead records from the other direction, and both are silent in the PERMISSIVE
  direction.
- **A compiler error list is not the authoritative site-list for a rename or move — it misses silently in <!-- trap: sub=build shape=scope-blind -->
  three distinct ways.** Found closing the memory-domain restructure (moving the graph-retention types into
  `.Interference`/`.Forgetting`/`.Modulation`/`.Salience` sub-namespaces): **(1) it cannot see warnings.**
  `CS1574` (an unresolved doc `<see cref>`) is a *warning*, so a file that references a moved type only
  inside an XML comment produces zero errors and never shows up — three such files were missed this way,
  one of them in a different package from the one being edited. **(2) it truncates across projects.**
  MSBuild stops building a dependent project once its own dependency fails, so one build surfaced 4 of 78
  true sites and the rest arrived only after those 4 were fixed and the build ran a second time. **(3) it
  truncates within a single line.** A line with three unresolved names produced only one reported error, so
  **five whole test files** with genuine `CS0246` errors produced **no reported error at all**. The reliable
  method instead: **search for the moved type NAMES across the tree** — a plain text search over every
  file, code and prose alike, which does not care whether the reference is a compiled statement or a
  comment — rather than trusting whatever the compiler chose to surface. **A second-order trap sits inside
  that fix, too:** an exclusion filter added to such a search to cut noise (for instance, excluding a
  directory believed to be covered by some other step) is a bet that the excluded area really is covered
  elsewhere. On this branch that bet was three-quarters right — the worst outcome, because a mostly-correct
  exclusion looks exactly as safe as a fully-correct one and hides precisely the quarter that wasn't.
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
  prose is what puts it back.** D47 renamed four memory seams (`IMemoryClock` → `IMemoryAgePolicy`,
  `ISalienceAppraiser` → `IMemorySaliencePolicy`, `IRetentionModulator` → `IMemoryRetentionPolicy`) and
  explicitly deferred the parameter names as "purely cosmetic". Three of them survived into 3.0's frozen
  public surface — `ageClocks`, `appraisers`, `modulators` — beside roughly a hundred XML-doc sentences
  still calling those policies appraisers and modulators. **The parameters were named after the prose**,
  which is exactly what `repo-mechanics.md` §Naming warns about for `Dto`, in a domain nobody connected to
  that rule. Two things to carry: **named arguments are public API** (source-compatible surface, recorded in
  the baseline, and renaming one after a freeze costs a major), and **a deferral whose justification depends
  on a window staying open needs re-checking when that window closes** — by then it reads as a settled
  choice rather than a deferral, and nobody re-opens it.
  It also escaped every gate by construction: `check-docs` excluded `src/` entirely at the time, and the
  API-surface baseline **records parameter names without judging them**, so a stale name round-trips
  through the one gate that exists to notice API changes. A human review caught all three.
  <br>Half of that has since been closed — `check-docs` scans code COMMENTS in `src`, `tests` and `bench`
  as of 2026-08-17 — but only half, and the halves are different in kind: a comment is PROSE and a parameter
  name is SURFACE, which is why `check-api-vocabulary` is a separate gate with a separate registry rather
  than a wider glob on this one.

- **A doc comment asserting "this is NOT duplication waiting to be extracted" is unfalsifiable, and it is <!-- trap: sub=docs,storage shape=vacuous,scope-blind -->
  the one claim nobody re-reads.** Measured 2026-08-16 (`docs/DECISIONS.md` **D77**).
  `PostgresMemoryGraphStore`'s class doc said exactly that, and justified it with three real dialect
  differences — `GREATEST` versus `MAX`, an `ILIKE` over a GIN index versus an FTS5 virtual table, the table
  reference Postgres requires in `DO UPDATE SET`. Every clause was TRUE and every clause was about the SQL.
  The file also held four materialization row types and a projection that were **byte-identical** to the
  SQLite twin's, and the sentence covered them by adjacency alone.
  <br>The cost is the class of defect it protected: those types alias 25 columns explicitly *because a
  column↔property mismatch is a SILENT null rather than an error*, so two copies is two places for that
  silence to appear, and nothing can see either — the compiler binds nothing there, and a test only ever
  exercises whichever backend it runs on. The repository's own counter-example was one directory away
  (`JobStoreSql`, which hoists the job state machine for the same pair and says why in its header).
  <br>**What generalises: a "these must stay separate" claim needs the same treatment as a bound — ask what
  it FORBIDS, and check that the answer covers the whole file it is written on.** Here it forbade sharing
  the SQL, and the SQL was maybe half the file. A defence written against the part someone was looking at
  reads, forever after, as a defence of everything beside it. When you write one, name the part it does NOT
  cover — the same rule `check-links`' own header states for a gate whose scope is narrower than its defect.

- **A "tidy up the formatting" regex in a scripted sweep is a rewrite of every line it can match, and the <!-- trap: sub=gates,encoding shape=silent-loss -->
  build stays green while it happens.** Measured 2026-08-16 stripping work-log citations out of `src/`
  comments. The substitutions were narrow and correct; three cleanup rules appended to "fix the punctuation
  a removed parenthetical strands" were not. `'  +([a-z(<])' → ' \1'` collapsed the INDENTATION of **all
  349 tracked files in `src/`** — the script reported "349 files swept" and the number read like success —
  and `body.replace('()', '')` turned `AddScorer&lt;T&gt;()` into `AddScorer&lt;T&gt;` in a file the sweep
  had no business editing at all. **C# does not care about indentation, so `dotnet build` succeeded both
  times.** Recovered by `git checkout -- src/` and re-running the intentional passes, which only worked
  because every one of them was already a script.
  <br>Three things generalise, and the third is the one worth carrying past this incident.
  **(1) A sweep must only touch a line one of its REAL rules changed** — compare before/after per line and
  keep the original otherwise; an unconditional tidy applied after the substitutions cannot tell the lines
  it was written for from the rest of the file. **(2) Assert the invariant the sweep must not break**, in
  the script: line count unchanged, and per-line leading whitespace unchanged for every line. That check
  costs six lines and turns this class of accident into a crash. **(3) The file COUNT is the tell.** A
  targeted sweep that reports touching nearly every file in the tree has stopped being targeted, and that
  is visible in the output before any diff is read — the corrected run touched 15.
  <br>Related but distinct from the CRLF entry above, which the same session also hit: splicing `\n`-joined
  lines into CRLF files left three of them mixed, and `git diff` says only *"LF will be replaced by CRLF"*,
  which reads like routine autocrlf noise. Detect by codepoint and normalise deliberately.
  <br>**This bullet used to justify that with "`core.autocrlf` is `true` here, so the working-tree convention
  is CRLF" — measure it before believing it, and the reason was backwards anyway.** `windows-machine.md`
  §Text and encoding carries the measurement and the diagnostic; the short version is that this setting is
  routinely `true` at system and global scope and overridden per-clone, and the repo-local value wins. Where
  it is `false` git converts NOTHING, so there is no enforced convention to lean on and a mixed file simply
  commits mixed — which makes "normalise deliberately" more load-bearing, not less. Corrected 2026-08-27.
  <br>**And superseded here on 2026-08-28**, which is the durable answer to the whole paragraph: a tracked
  `.gitattributes` (`docs/DECISIONS.md` D95) overrides `core.autocrlf` outright, so a mixed file normalizes
  on checkin and there is no per-clone value left to measure. The advice above is what to do where nobody
  has declared one.

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
  <br>_Relocated from `TASKS.md` on 2026-09-10, where it had been carried in the banner. A standing trap is
  knowledge, not backlog — the backlog holds what is left to do._

- **Asserting a specific FAILURE MODE when the claim is only "it tried" makes a test race the clock.** <!-- trap: sub=tests shape=ordering -->
  Measured 2026-09-04: `ByoHttpClientTests.Default_path_still_creates_a_lyntai_client` points at a closed
  local port and pinned `LlmVerdict.Failed`, whose own comment says it is proving *the client existed and
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
  reports it as dead code.** Measured 2026-08-30 building `MemoryWalk` (`docs/task-archive.md` Part 120).
  The walk is finite for two independent reasons: the default seed selector eventually returns nothing, AND
  a step that discovers and upgrades nothing ends it. Deleting the second guard failed **no test** — every
  fact used the default selector, which hits the first guard first — so the rule that actually makes the
  sequence finite for a caller-supplied `SeedSelector` was uncovered, and the evidence said to delete it.
  <br>**The tell is a guard whose removal changes nothing, in code that has a seam.** That reads as "this is
  redundant" and is in fact "no test takes the path where it matters". The question to ask of any
  belt-and-braces guard is not *"does a test fail without it?"* but **"which caller reaches this one FIRST,
  and does any test look like that caller?"** Here the adversarial caller is one line —
  `SeedSelector = s => s.Items`, a selector that never empties.
  <br>**Bound the loop inside the TEST, not only in the code under test.** A missing termination guard makes
  the honest test hang, and a hanging test is a worse signal than a failing one — it looks like an
  environment problem and it blocks the suite. Collect into a list, `break` past a generous ceiling, and
  assert the count came in under it; the regression then fails in milliseconds.

- **Refactoring a measurement harness can change a DENOMINATOR rather than a result, and every rate moves <!-- trap: sub=measurement shape=silent-loss,wrong-subject -->
  while retrieval is untouched.** Same day, in the same work. The pre-refactor LoCoMo loop ran shots 2 and 3
  **unconditionally**, re-snapshotting an unchanged context when the frontier was empty; the library's walk
  ENDS instead when a step moves nothing. Behaviourally better, and it would have silently dropped those
  rows — so `asked`, `returned` and `chars` would have lost their shot-2/shot-3 entries and every published
  rate would have shifted for a harness reason wearing a result's clothes.
  <br>**When you replace an unconditional loop with one that can terminate early, ask what the dropped
  iterations were CONTRIBUTING** — a row in a tally counts even when its content is identical to the last
  one. Restoring it is three lines and the alternative is an unexplainable delta.
  <br>The general defence is the one this file already prescribes for latency, applied to quality: **run the
  before arm.** Stash only the harness file, run, restore, re-run. Here every published cell reproduced
  exactly across LongMemEval (oracle and haystack), LoCoMo `--shots` and all six arms of the retrieval
  ladder — which is a far stronger claim than "the tests pass", and it is what turned a plausible refactor
  into a verified one.

- **A PRE-REGISTERED prediction is only adjudicated by a run powered to adjudicate it, and pre-registration <!-- trap: sub=measurement shape=vacuous,wrong-subject -->
  gives no protection against judging it on one that is not.** Measured 2026-09-02 (`docs/task-archive.md`
  Part 137). The prediction was "40–45%, gaining but NOT reaching 44.5". An n = 200 run read 44.8 against a
  control's 43.9, so the second clause was declared REFUTED and written up as *"the run refuted my
  scepticism, not the design"* — a headline claim that the engine had beaten its baseline for the first time.
  The full 1,540-question run read **44.0 against 44.4**: sign flipped, both clauses of the original
  prediction correct, the write-up retracted.
  <br>**The instrument said so at the time and was not consulted.** Two arms sending BYTE-IDENTICAL prompts
  scored 40.7 and 41.1 at n = 200 — a 0.4-point reader-nondeterminism floor, measured for free, in the same
  table. A ±1-point difference was therefore never resolvable there. At n = 1,540 those same two arms read
  42.2 and 42.2.
  <br>**This is the SECOND instance and the first was already written down.** `docs/task-archive.md` Part 134
  adjudicated an interaction at n = 100 and had to retract it at full sample; that lesson sits in
  `docs/memory-measurements.md` §5, was quoted during the session that repeated it, and was repeated anyway. Knowing the
  rule is not applying it.
  <br>**The habit that would have caught it, and it is cheap:** before reading a delta as a result, compare
  it to a floor measured IN THE SAME RUN — a pair of arms that must agree, a repeat of one arm, anything that
  structurally cannot differ. If the delta is not several times that floor, the run has not adjudicated
  anything and the honest verdict is "unresolved at this sample", not a direction.
  <br>**And a filtered confirmation run must keep the controls of every claim it touches.** That full-sample
  pass selected four arms for the question it was re-testing and dropped the 20-item cosine control, leaving
  a NEIGHBOURING claim (Part 136's 20-slot parity) with no full-sample comparison at all — a second,
  quieter cost of the same run.

- **A metric whose MATCH TARGET survives the transformation it ought to detect is blind to that <!-- trap: sub=measurement shape=wrong-subject -->
  transformation, and it reports a clean comparison between arms that differ by exactly it.** Measured
  2026-09-02 (`docs/memory-measurements.md` §5). LoCoMo evidence-hit asks whether a returned turn contains
  `"(" + dia_id + ")"`. The graph arms return HEADLINES — `MemoryHeadline.Derive` cuts content at 120
  characters — while the cosine arms return whole turns. But the `dia_id` rides in the 44-character HEADER
  every turn carries, so it is never the part that gets cut: it survived truncation in **5,882 of 5,882**
  turns, **100.00%**, while the answer text behind it survived at a mean of **56.2%**. The metric scored a
  half-turn and a whole turn identically, and the arms it was comparing differed in precisely that way.
  <br>**The cost is a wrong INFERENCE rather than a wrong number.** Evidence-hit was right about what it
  measures — the turn was found — so the ladder's results stand. What silently failed is the step everyone
  takes next: the same configuration scores **+2.5 against cosine on retrieval and −13.3 on QA**, and for a
  year the obvious reading would have been "the reader is the bottleneck" rather than "the arms deliver
  different amounts of the same turn".
  <br>**The tell is that the metric's key and its payload can be damaged independently**, and only the
  payload matters downstream. Ask of any matching metric: *what part of the item does the match actually
  touch, and can the rest be destroyed without moving the score?* If yes, it cannot compare two arms that
  transform the rest differently — and it will not say so. The defence is a second column pricing what was
  DELIVERED (here `chars/q`, which was present and never read against the hit rate), or an arm that differs
  only in the transformation.

- **A diagnostic built to EXPLAIN a published number runs on its own defaults, not on the flags that <!-- trap: sub=measurement shape=wrong-subject -->
  produced that number — and it explains a different arm while looking entirely reasonable.** Measured
  2026-09-02 building `memory-locomo --composition` (`docs/task-archive.md` Part 135), which decomposes the
  `lyntai-fused-3shot` row's `chars/q` into headline versus content. The published row was measured at
  `--seeds 16`; the harness field defaults to `3`, and `--composition` inherited the default. Its first run
  reported **4,950 chars/q and 123.2 chars/item** for a row published at **6,536 / 162.4** — no error, no
  warning, and a composition table that is internally consistent and perfectly plausible. Re-run at the
  published flag it reproduces **6,556 / 162.9 on 20 questions, 0.3%**.
  <br>**The tell is that there is none**, which is the whole entry. Every other trap here leaves something
  that looks wrong; a diagnostic pointed at the wrong parameter point produces a well-formed answer to a
  question nobody asked, and the reader has no way to tell it apart from the right one — the derived
  conclusion ("only 15% of the context is upgraded") would have been published as a property of the design
  when it was a property of a flag.
  <br>**So a mode that explains a published figure must ASSERT it reproduces that figure**, printing the
  comparison rather than leaving it to a reader who would have to go and find the other table. That is the
  same reasoning `check-links` rests on one tier up — a claim about another record is checkable, so check
  it — and it is cheap here because the reproducing column (`chars/q`, `items/q`) is already being computed.
  <br>**The general shape: a derived instrument inherits its subject's CONFIGURATION, not just its code.**
  Copying the arm's config object is necessary and not sufficient; the flags that were passed on the day are
  part of the arm, and the defaults are a different arm wearing the same name.

- **A benchmark whose store MUTATES on read has non-independent trials, and the contamination presents as a <!-- trap: sub=measurement,storage shape=wrong-subject,silent-loss -->
  RESULT rather than as a bug.** Measured 2026-08-29 (`docs/task-archive.md` Part 118). LoCoMo questions
  within a conversation shared one store, and this engine writes on every read — a recall reinforces what it
  returned, `ExpandAsync` reinforces what it walks — so question N read a graph that questions 1..N−1 had
  already dug through. Nothing errored, every arm looked plausible, and the numbers went into `docs/memory.md`.
  <br>**The tell is a figure moving that logically CANNOT depend on what was varied**, and it is the whole
  diagnostic. Evidence-hit for `shot-1` — the FIRST recall, before any expansion happens — read **65.4%** at
  `--seeds 3` and **53.8%** at `--seeds 20`, an 11.6-point swing caused entirely by how much a LATER shot
  expanded. Shot 1 is the same query against the same ingested corpus in both runs; the only channel by which
  a later shot can reach it is the store. A real effect cannot travel backwards, so a number that moves
  anyway is measuring leakage.
  <br>The item had been filed off a 2-point observation (30.0% → 28.0%) and the real magnitude was **five
  times that**, which is the ordinary shape: a leak's size depends on how hard the *other* arm digs, so
  whatever you happened to notice is a lower bound.
  <br>**The fix is a per-trial store, and cloning a migrated file is what makes it affordable** — ingest once
  into a template nothing reads, then byte-copy it per question. Re-ingesting per question costs the whole
  study; copying costs milliseconds. The VECTOR store is deliberately left shared, and the rule that decides
  it is *which component mutates on read*: vectors are written only by `RememberAsync`, so they are read-only
  once ingestion ends, while the graph store is the one being isolated.
  <br>**Two traps inside the fix.** A byte copy taken while SQLite's write-ahead log still holds committed
  rows is a **silently partial** database — checkpoint first, and copy a surviving `-wal` too, since
  `TRUNCATE` cannot always finish while another connection holds a read snapshot. And a clone that lost rows
  presents as a *recall-quality regression*, not as a broken harness, which is the direction that gets
  published — so count the rows in the clone and print it (`419 of 419`) rather than trusting that the copy
  succeeded.
  <br>**The control has to fail on the old code**, or it is only evidence that the number is stable. Re-run
  the identical pair against the pre-fix harness: it read 65.4 / 53.8 where the fixed one reads 65.4 / 65.4.
  Stash the fix, run, restore — the same positive-control discipline `pitfalls.md` already demands of a cache
  fix, applied to a measurement harness.

- **When two arms that are the SAME OPERATION disagree, you have found either a code difference or your <!-- trap: sub=measurement shape=ordering -->
  noise floor — and the two are told apart by cheap experiments, not by reading the diff.** Measured
  2026-08-29 (`docs/task-archive.md` Part 119). A new `shot-1` arm read 48.5% where the existing `lyntai`
  arm read 47.7% on LongMemEval temporal-haystack: same sample digest, same embedder miss count, same 132
  questions. Reading the code found only one difference (an explicit options object) and it was provably
  inert — `options ?? new GraphMemoryOptions()` makes it identical to passing none — so the diff said
  "equivalent" while the numbers said otherwise.
  <br>**Two experiments settled it, in increasing cost.** First, run the pair on the variant with FEWER
  near-ties: on the oracle (25 turns/question) the two paths agreed to the decimal, which already says the
  code is equivalent and the disagreement is a property of the harder corpus. Then repeat the identical
  haystack run: every graph arm moved exactly 0.8 points — one question in 132 — and the repeat landed on
  the other arm's own 47.7%.
  <br>**The control that localises it is the arm that CANNOT be affected.** Both `vector` arms were
  byte-identical across every run while the graph arms moved, so the variation is the ranking's own
  sensitivity to near-ties — a fusion over ~490 candidates — and not the sample, the embedder or the
  harness. An arm that structurally cannot vary is worth keeping in a table for exactly this reason; it is
  the same control that made the LoCoMo isolation fix readable one entry above.
  <br>**The rule: a benchmark's reproducibility is a property of the ARM, not of the benchmark**, so measure
  it per arm before quoting a figure to a tenth of a point. Here the levels are good to about a point and
  the DELTAS are stable across runs (+4.5 and +4.6), which is why a difference six times the floor is a
  finding and a 0.0 inside it is "no measurable gain" rather than "exactly none".

- **A candidate scored on a DIFFERENT SCALE than the pool it joins cannot win, however relevant it is — and <!-- trap: sub=memory shape=fail-open -->
  the feature that added it then reads as inert rather than as broken.** Measured 2026-08-29
  (`docs/task-archive.md` Part 110). `GraphMemoryOptions.SemanticSeedK` adds semantically-similar entries to <!-- drift-ok link-ok: names the property under measurement on the dated run; since removed and replaced by `SemanticSeedOptions.K`, whose per-source rank fusion is what closed this trap -->
  a recall's candidate pool carrying their raw COSINE as `Relevance`; the pool they join is saturated with
  flat `1.000`. The best semantic match in an entire 369-vector collection entered at **0.785** and was
  outranked by everything already there, so raising the knob from 0 to 20 moved a benchmark's evidence-hit
  rate by **0.0 points**.
  <br>**Four plumbing hypotheses were refuted before the real one was found**, all by reading the code and
  all wrong: the vectors are stored (369 of 369), the collection name matches, the search returns a full k,
  and every id parses. The seeds arrive; they lose. **The tell to look for is a knob that does nothing at
  all rather than a little** — a weak signal moves a number slightly, while an incommensurable one moves it
  exactly zero, because it is being sorted below the floor of the other scale every single time.
  <br>`docs/DECISIONS.md` **D93** already recorded that these values are not commensurable within one recall
  and drew the conclusion that no ANSWER may be computed from them. This is the same defect reaching a
  second victim: it also defeats any feature that ADDS candidates scored on its own scale.
- **A control that compares POOLED verdicts cannot see a split that cancels in the pool, and it reports <!-- trap: sub=measurement shape=scope-blind,vacuous -->
  "no change" while doing it.** Measured 2026-08-28 (`docs/task-archive.md` Part 108). The gist sweep's
  `ConnectionBoost = 0` control printed *"verdict did NOT move — every rule selects the same regime under
  both curves"* on both clocks. True of the ARGMAX, and false of everything underneath: with the boost on,
  `count@0.9` reads tie/A/B/B across the four cardinality rungs; with it off, B/B/B/B. The pooled winner
  agreed because the disagreements cancelled. **A control is only as fine-grained as the axis it groups
  by** — if the experiment gained an axis, the control has to gain it too, or it silently starts averaging
  over the thing being measured.
  <br>**Its loud sibling is the good direction and arrived in the same run**: a control asserting a LITERAL
  the new axis varies (`both regimes fully enumerated (8/4)` — the `RoutineCount = 12` split) failed on 1800
  of 2400 cells and refused to publish the table. That is the failure you want; it is a function now
  (**D60**). The pair is worth remembering together, because the same edit produced both and only one of
  them announced itself.
- **A read path a contract fact does not CALL is a read path that contract does not cover, however <!-- trap: sub=tests,memory shape=scope-blind,vacuous -->
  completely it enumerates implementations.** Measured 2026-08-27 (`docs/DECISIONS.md` D93): a metadata
  round-trip fact ran on all five memory engines and still missed a third broken projection, because it
  called `RecallAsync` and nothing else — `ExpandAsync` projects the entry the caller NAMED separately from
  its neighbours, and no amount of engine coverage reaches a method the fact never invokes. **Coverage of
  IMPLEMENTATIONS reads as coverage and is the axis that is easy to count; coverage of ENTRY POINTS is the
  one that was missing.** The fix is a second fact per read path, each asserting positively — including the
  negative branches, since a single "null is acceptable" assertion passes vacuously for every implementation
  that cannot carry the value at all.
- **A finding that is WRITTEN DOWN is not a finding that was VERIFIED, and this repository keeps treating <!-- trap: sub=docs shape=unmeasured,stale-claim -->
  the two as the same.** Twice in two days (2026-08-17): `TASKS.md`'s then-`Startable` section (closed as
  `docs/task-archive.md` Part 87) recorded a divergence as
  "ComfyUI hardcodes `Failed` while `FalQueueProvider` routes the same class through
  `GenerationVerdictClassifier`" — so fal was believed correct, and the item was filed as coverage work
  rather than a bug fix. Measured with the first contract test that reached it: **fal reports `Failed` too**,
  because it classifies the failure TEXT and a `"401: …"` status line is not vocabulary `FromErrorText`
  matches on. The other was a `CHANGELOG.md` entry asserting "neither shipped curve sets anything beyond
  `Stability`" that the same release had already falsified.
  <br>Both were written by someone who had read the code, which is precisely why they were persuasive. **The
  half that gets recorded is the half that was READ; the half that was RUN is what a claim needs.** When a
  record names two implementations and says one of them is fine, the fine one is the claim to check first —
  it is the only one nobody is planning to touch.
- **A contract fact can pass or fail for reasons that have nothing to do with its subject, and the failing <!-- trap: sub=tests shape=wrong-subject,vacuous -->
  direction is the dangerous one because it looks like evidence.** Both shapes hit within one hour writing
  the 3.0 seam contracts:
  - **Testing the FAKE.** A cancellation fact asserted that a pre-cancelled token propagates — against a
    stub that ignores its token entirely (`FakeLlmClient`, `StubHttpHandler`). Nothing cancelled, so the fact
    passed vacuously for some subjects and failed for others by measuring the stub. What it was FOR is the
    subject's catch ORDERING (`catch (OperationCanceledException) { throw; }` ahead of a fail-safe catch),
    which needs a double that honours the token.
  - **Failing before reaching the subject.** A fetch fact passed a bare operation id to every job backend;
    fal encodes the model into its ids and rejects a malformed one BEFORE calling out, so the fact failed on
    id validation while reporting the classification defect it was hunting. It would have been "confirmed"
    without ever exercising the code under test — and then "fixed", and still red.
  - **Timing what you could OBSERVE.** A concurrency fact asserted three stalled probes finish "under a
    second" against a 400ms deadline — green alone, failed inside a full-suite run.
    <br>**This bullet used to cite "the `ElapsedAgePolicy` entry above" as the prior record of the same
    shape. There is no such entry, and there never was** — across every revision of this file the name
    occurs only inside the citation itself, never as an entry it could point at. The lesson it was offered
    as evidence for still holds, and it is the one the entry ABOVE this one carries: **the half that gets
    recorded is the half that was READ, and here what was recorded was a pointer to nothing.**
    Corrected 2026-08-27.
    <br>**The fix generalizes: assert the PROPERTY, not the clock.** Concurrency is observable — have the
    fakes record their peak overlap, which serial execution cannot push above 1 at any speed. That is both
    deterministic and a stronger claim than any elapsed bound. And where a bound's failure mode is a HANG
    rather than a slow pass (a probe stalling on `Task.Delay(Timeout.Infinite)`), drop the clock assertion
    entirely: the runner reports a hang far more loudly, so the assertion was buying nothing and costing
    load-dependence.
  <br>**When a new fact fails, confirm it failed where you think.** Read the failure message against the
  code path, not against the expectation.

- **Adding a VARIANT to a measurement instrument moves the templates and the READERS, and forgetting the <!-- trap: sub=measurement shape=silent-loss,vacuous -->
  readers produces flattering numbers rather than a failure.** Measured 2026-08-12 adding a Chinese arm to
  `MemoryCorpus` (D55). The generator had ten template sites and they were easy; what nearly broke the
  measurement was everything that parses generated text back — the corpus's own invariants read the attribute
  value as "the word after `is`" and found a cue by `EndsWith(" recallcue")`. Those are English grammar in a
  helper's costume. On Chinese input they do not crash; they return something plausible and wrong, so the
  variant that most needs its guarantees checked is the one running unchecked. **Put the readers beside the
  templates** (`CorpusLexicon`) so the compiler names every hole when a language is added.
  Two bugs got through anyway, and BOTH made the new arm look better, which is the direction that does not
  announce itself:
  - **A corpus token below the store's term floor silently stops competing.** The Chinese shared leading
    token was `条目` — two characters, under `SearchTerms.MinimumTermLength`, so it yielded no trigram and was
    dropped from every query. That deleted the corpus's central property (every queried entry shares a token,
    so a broad recall makes them COMPETE) and the sweep reported `topical` miss AND pollution of exactly
    `0.0000` on almost every shape. An implausibly *good* number is a bug report; treat a clean sweep with the
    suspicion of a failing one.
  - **A classifier that matches wording silently empties a whole class out of the report.**
    `ClassifyQuery` used `EndsWith(" recallcue")`, so every Chinese attribute cue landed in `other` and the
    class the study existed to measure was simply absent. **Absence reads as "not exercised", never as
    "mislabelled"** — so a report missing a row is a louder signal than a row with a bad number.
  - **Every invariant pinned the cue's UPPER bound and none pinned the LOWER one, so a cue matching NOTHING
    passed them all.** The Chinese discriminative cue reached zero of three cluster members where English
    reaches exactly one; the arm reported miss ≈ 0.889 with pollution 0.000 against English's 0.299. That is
    a cue returning almost nothing, and it reads as a language finding. Caught only by disbelieving a number,
    not by any gate. **When a fixture is constrained to avoid matching the wrong thing, assert separately
    that it still matches the RIGHT thing** — "shares no term with material outside the cluster" and "reaches
    exactly one member inside it" are two facts, and the second is the one that keeps the first honest.
  The general rule, and the reason this sits under Testing rather than in the memory section: **an instrument
  is code that nothing else validates.** Its output is a number, and a number is never obviously wrong. Guard
  a variant with (a) goldens on the ORIGINAL captured before the axis exists, (b) an assertion that the
  variant genuinely DIFFERS, and (c) an assertion that the property the instrument depends on still holds in
  the variant — all three, because each catches a different way of quietly measuring nothing.
- **A wall-clock burst-detection policy replayed IN-PROCESS measures the fixture's replay SPEED, not a <!-- trap: sub=measurement,memory shape=wrong-subject -->
  deployment regime — so its arm is not "the shipped default", however faithfully the default is
  registered.** Found 2026-08-27 by `tests/Lyntai.Tests/Memory/MemoryGistSupportRuleTests.cs`.
  `BurstDampenedAgePolicy` is `GraphMemoryEngine`'s default age policy and it reads a REAL clock: writes
  closer together than its 5-second window are one burst, and the policy exists to protect material written
  BEFORE a burst from being aged by it. A corpus replay of **305 writes and 147 recalls against the InMemory
  store finishes in about half a second** (0.48–0.50 s across runs on this machine), so the ENTIRE corpus is
  one burst, `n` reaches 305 for a reason no deployment shares, and the damping ends up arbitrating WITHIN a
  single bulk ingest — a relationship its own contract was never written to judge.
  <br>**The distortion is large and it is not uniform**, which is what makes the arm's output look like a
  result rather than an artifact. `Stability` runs 20.0 down to 7.071 across the older phase purely by
  position in the unbroken burst, against the newer phase's own ~1.145; and which members earn a
  `ConnectionBoost` changes between a real clock and a stepped one at the SAME seed, so even the
  co-activation structure is a function of how fast the host ran. A per-member spread read off that arm is
  write-time ENCODING wearing recency's clothes.
  <br>**So give the clock its own arm and state the pacing as a fixture assumption** — an injected clock
  stepped over the burst window is one arm, the real clock is a different and faster-than-any-deployment
  one — rather than registering the shipped policy and calling whatever falls out "the default's behaviour".
  And never report a finding about the LIBRARY from an arm whose distinguishing input is the test host's
  speed: say what the number is a fact about. Same family as the instrument rule above — an instrument is
  code that nothing else validates, and a policy that reads a clock quietly makes the host one of its inputs.
- **Microsoft.Data.Sqlite completes its "async" methods SYNCHRONOUSLY, so two awaited store calls issued <!-- trap: sub=tests,storage shape=vacuous -->
  from one thread are sequential by construction — and a concurrency test over them measures nothing.**
  Measured 2026-08-17: an overlap test called `Task.WhenAll(store.ReportStepAsync(a…), store.ReportStepAsync(b…))`
  and failed even AFTER the per-job locking it was written for landed, because the first call ran to
  completion on the test thread (blocking inside the instrumented clock) before the second expression was
  even evaluated — the store's gate never entered into it. Wrap each call in `Task.Run` when the subject is
  concurrency and the backend is SQLite (or anything whose async is sync), and treat "still red after the
  fix" as a cue to ask WHERE the serialization actually is before touching the fix.
- **An orphaned child process inherits the test host's console handles, so a test that STRANDS one wedges <!-- trap: sub=tests shape=resource -->
  the whole runner — past its own failure, past every later test.** Measured 2026-08-17 by the RED run of
  the hanging-locator test: the test itself failed correctly at its 30s bound, and `dotnet test` then hung
  indefinitely anyway, because the stranded node child held the inherited stdout the harness reads to EOF;
  it unblocked the moment the child was killed by hand. A bounded in-test wait is NOT enough when the
  failure mode leaves a child alive — give the fixture child a self-exit (`setTimeout(() => process.exit(0), …)`)
  so a regression costs one slow red run rather than a wedged pipeline. Same family as `windows-machine.md`
  §Processes (kill your own tree), from the harness's side.
- Provider/e2e tests must not hit a live endpoint or spend real tokens — HTTP providers get a stubbed <!-- trap: sub=tests shape=resource -->
  `HttpMessageHandler`; CLI providers and the Playground get `provider-stub.mjs` via
  `LYNTAI_PROVIDER_CMD`. Extend the stub's prompt-marker behavior when a test needs a new deterministic
  output.
- 221 passing tests didn't catch any of the LLM/contract bugs above — **tests passing ≠ correct.** For <!-- trap: sub=tests shape=scope-blind -->
  load-bearing semantics (fallback, streaming, verdicts, FTS sync) reason about the code, then add the
  test that would have caught the bug.
- **This repo has TWO independent ways for a targeted test run to verify nothing.** (1) `dotnet test <!-- trap: sub=tests,gates shape=vacuous,wrong-subject -->
  --filter` **reports success when it matches zero tests** — a filter naming `LlmRouterTests`, a class that
  does not exist (the real ones are `LlmRouterCompleteTests` / `LlmRouterStreamTests`), passed vacuously and
  looked like a clean regression run. (2) Through the wrapper it depends on `--`, which is not obvious.
  `node devtools/dev.mjs test --filter X` forwards straight into `dotnet test` (`devtools/dev.mjs` spreads
  `...args` into the argv, and always has) and inherits trap (1) whole — a name matching nothing passes
  vacuously, exit 0. `node devtools/dev.mjs test -- --filter X` runs the **WHOLE suite** instead: VSTest reads
  post-`--` tokens as RunSettings arguments and the filter is dropped (measured 2026-08-05: 1573 passed /
  1582 total). Slow but honest — and easy to mistake for the filtered run you asked for, in the other
  direction from (1). **Always read the matched/total count**, and prefer a broad filter over an
  exact class name (`~Router|~Routing|~DeadHost`) so a renamed class degrades to running too much rather than
  to running nothing.
- **A test that is already RED for an unrelated reason cannot be mutation-killed, and a mutation check <!-- trap: sub=tests shape=vacuous -->
  against it proves nothing.** Found 2026-08-09 running the two required mutation checks for the salience
  rank boost's FIRST round (`docs/DECISIONS.md` D45, before the owner's same-day correction that ranking
  defaults off): deleting the boost factor entirely and hard-coding the weight both left the "salient entry outranks a
  better match" fact failing (it was already failing at the round-1 default weight — see the
  `Relevance`-normalization entry above) and left the "weight leaves ranking unchanged" fact passing EITHER
  way, because the relevance gap it measures against dominated regardless of the weight. Neither mutation was
  actually caught. **Ask "which specific wrong implementation does this test reject?" of the MUTATED run, not
  just the correct one** — a fact that is red (or green) independent of the mutation is not exercising what
  its name claims, however correct the underlying implementation is. Round 2, with ranking defaulted OFF and
  the opt-in fact (`An_explicitly_configured_rank_weight_can_outrank_a_better_textual_match`, weight 1.0)
  chosen to clear the measured gap, both mutations DO now kill the opt-in fact — the fix was not to accept a
  test with no discriminating power, but to pick a scenario the mechanism could actually win.
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
  legitimate zero.** Measured 2026-08-15 giving `check-docs` / `check-encoding` / `check-links` /
  `check-samples` the fail-closed rule `check-api-vocabulary` already carried (a gate that scanned nothing
  must never print a tick). Written the obvious way — `if (candidates.length === 0) return 1` — it
  immediately failed check-encoding's own `an unscanned extension is ignored` fact, and that test was RIGHT:
  a call whose only input is a binary has legitimately nothing to scan. The distinction that makes the guard
  correct is **who chose the scope**. An empty SOURCE list is a broken listing whoever supplied it (the exact
  shape that let `check-sensitive` skip every renamed file), so it indicts any caller; zero SURVIVORS of the
  filters is an indictment only on the full-tree path, where this repository cannot legitimately produce one
  — README/CLAUDE.md/TASKS.md guarantee three docs, 761 files pass the text filter, 74 samples exist. Hence
  `source.length === 0 || (files === null && filtered.length === 0)`. **Write the both-directions test at the
  same time**: a fail-closed guard that over-fires is a broken build on legitimate input, which is the
  failure people fix by deleting the guard.
