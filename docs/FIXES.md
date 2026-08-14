# Fix log

Per-incident records: **symptom → root cause → fix → verification**, plus the commit that introduced the
bug. Newest first. The other two homes, so nothing has to be guessed (`.claude/rules/repo-mechanics.md`
§Fix log): a decision and its reasoning goes to `docs/DECISIONS.md`; the reusable trap a fix revealed goes
to `.claude/knowledge/pitfalls.md`; the release-facing line goes to `CHANGELOG.md`.

---

## 2026-08-14 — a recall returned more entries than its own `Limit`, because a per-ENGINE bound was never reconciled with a per-QUERY one

**Symptom.** `RecallAsync` returned three items for `Limit: 2`, and none of them was an ordinary hit. No
error and no warning: the caller simply got a larger result than it asked for, which downstream shows up as
a prompt that overruns its budget rather than as anything identifiable as a memory defect.

**Root cause.** `GraphMemoryEngine.RecallAsync` computed
`reserve = Math.Min(authoritative.Count, Math.Max(0, AuthoritativeReserve ?? limit))`. The `?? limit` caps
the DEFAULT; an explicitly configured value was never compared against the limit at all. The line that
follows, `ordinary.Take(Math.Max(0, limit - reserve))`, floors at zero — so once `reserve > limit` the
ordinary half contributes nothing and `reserved` is concatenated whole.

The two numbers live on different scopes and nothing else reconciled them: `AuthoritativeReserve` is an
engine option, sized against `GraphMemoryOptions.DefaultLimit`; `Limit` arrives per query. So the trigger is
the ordinary case — a reserve of `5` against a default limit of `10`, and any caller that passes something
smaller for a tight prompt budget.

**Why no test caught it.** Both existing reserve facts use a reserve BELOW the limit (`1` against `3` and
`4`), which is the only regime where the missing cap is unobservable. Nothing exercised reserve > limit, and
nothing anywhere asserted the plain property that a recall returns at most `Limit` items.

**Fix.** `Math.Min(limit, …)` around the whole expression, so the option can only ever REDUCE displacement —
the only direction its own documentation describes. Objective (1) is unaffected: the default was already
unbounded-within-the-limit and still is. `AuthoritativeReserve`'s XML doc was corrected in the same change; its
last paragraph still described the rejected first version of the mechanism ("applies only to entries
re-admitted BY GRADE"), three lines from the engine comment that says the opposite in capitals.

**Verification.** `GraphMemoryRankingGoldenTests.A_reserve_larger_than_the_query_limit_still_returns_at_most_the_limit`
— red before the fix (3 items), green after, with the eight pre-existing golden facts unchanged.

**Introduced by.** The 3.0 authoritative-reserve work (D56); the option was born with the gap.

---

## 2026-08-14 — a non-finite age poisoned an entry's persisted `Difficulty`, on the reinforcement path the docs recommend

**Symptom.** With a `IMemoryAgePolicy` reporting a non-finite age, `ExpandAsync` persisted
`MemoryDecayState.Difficulty` as `NaN` on the in-process store; on a SQL backend the write threw and
`ReinforceAsync`'s deliberate catch-all swallowed it, so the reinforcement was silently lost instead. Either
way the review log — the artifact that exists to make FSRS parameter fitting possible — recorded a `NaN` row.

**Root cause.** `DsrRetrievability.Reinforce` guards the STABILITY half (`double.IsFinite(increase)`) and its
own comment explains why it must: the increase term depends on `Age`/`Strength`/`StrengthAge`, which arrive
per-call in the caller's state and so cannot be validated at construction the way `DsrOptions` validates its
constants. The DIFFICULTY half, added later by the fsrs-properly work, asserted the opposite in
`NextDifficulty`'s own doc — "every term feeding `D''` is provably finite before the clamp runs".

Both cannot be true. The derived grade is `2 + 2 × Retrievability(state)`, and `Retrievability` is a function
of exactly the age the stability half declines to trust. `Math.Clamp` PROPAGATES `NaN` (IEEE-754), so
`Retrievability` returned `NaN`, `DerivedGrade` returned `NaN`, `NextDifficulty`'s final
`Math.Clamp(_, 1, 10)` returned `NaN`, and `GraphMemoryEngine` wrote it straight to `TouchAsync`.
`DerivedGrade(double)`'s own summary carried the same false premise — "already clamped to `[0,1]` by
`Retrievability` itself".

**Why the recall path looked fine.** It is covered by accident: `MemoryRankingContract.Rankable` drops a
candidate whose retrievability is non-finite, so the poisoned entry never reaches reinforcement.
`ExpandAsync` reinforces a node the caller named with no ranking in between — and
`ReinforceOn = MemoryReinforcementActs.Expansion` is what `docs/memory.md` and this release's changelog
recommend as the measurably better setting, so the unguarded route is the one the documentation points at.

**Fix.** `DerivedGrade(in MemoryDecayState)` reports NO JUDGEMENT for a non-finite retrievability, reusing the
meaning `null` already carries for the Δt=0 bypass: nothing computable happened, so nothing should move.
`Reinforce`'s existing `grade is null` branch then leaves difficulty at its already-coerced, finite value.
Both over-claiming doc paragraphs corrected in the same change.

**A distinction the first attempt got wrong.** Only `NaN` is uncomputable. `+Infinity` age gives
`Math.Pow(+Infinity, decay)` with a negative exponent — exactly `0`, "fully forgotten" — and derives a real
Hard grade; `-Infinity` was always caught by `Age <= 0`. A first test asserted `null` for all three and failed
on `+Infinity`, which was the TEST being wrong rather than the fix. The pinning fact now says which is which.

**Verification.** `DsrPathologyTests.A_non_finite_age_never_produces_a_non_finite_grade_or_difficulty` and
`Expanding_under_a_non_finite_age_policy_never_persists_a_non_finite_difficulty` — both red before, green
after. The second uses `InMemoryMemoryGraphStore` deliberately: on a SQL backend the poisoned write throws and
is swallowed, so the stored value stays finite and the fact would pass while the defect was live.

**Introduced by.** The fsrs-properly difficulty work (D49's follow-up), which added a second consumer of
`Retrievability` without carrying the finiteness reasoning the first one already had.

---

## 2026-08-14 — a multi-word CJK query produced NO search terms, on every backend, because one short-circuit skipped the short grams

**Symptom.** A keyword recall whose tokens are all short CJK words — `"配偶 客户"` ("spouse", "client"), two
ordinary two-character Chinese words — matched nothing on any storage backend. No error, no exception: the
query simply returned no keyword hits, which is indistinguishable from "nothing was stored". Found
2026-08-14 by a code-review agent reading `SearchTerms`, and confirmed by executing the method rather than
by reasoning about it.

**Root cause.** `SearchTerms.SubstringTerms` short-circuited on the index pass:

<!-- compile-skip: the defective body AS IT WAS, quoted from inside SearchTerms — `Extract` and
     `ShortGrams` are private statics of that class, so the fragment cannot stand alone, and supplying a
     context for it would mean re-declaring the very method this entry is about. -->
```csharp
var terms = Extract(raw);
return terms.Count == 0 ? terms : [.. terms, .. ShortGrams(raw)];
```

`Extract` emits 3-grams (the trigram-index floor), so a token of exactly two Han characters yields nothing.
With EVERY token below that floor, `Extract` returns empty and the ternary returns immediately — never
calling `ShortGrams`, which exists precisely to carry those two-character words and would have returned
`["配偶", "客户"]`. `LikeClause`'s own `terms.Count == 0` fallback then substituted the whole trimmed query
as a single pattern, `%配偶 客户%`, demanding that exact phrase *including the space* — which ordinary prose
never contains.

**Why it hid for so long.** Three coincidences.
1. **The single-token case works by accident.** `"配偶"` alone also yields no terms, and the whole-query
   fallback is then `%配偶%` — exactly what the term-based clause would have produced. Every existing test
   used one token.
2. **A long neighbour masks it.** `"配偶 叫什么名字"` makes `Extract` non-empty, so the short grams ARE
   appended and `配偶` survives. The same token was kept or dropped according to its neighbours — which is
   the tell, and no design would choose it.
3. **The rescue built for this case defeated itself.** Postgres runs a narrow pass, then consults
   `HasShortSpacelessTerms` — which calls `ShortGrams` DIRECTLY and correctly answered `true` — and paid a
   second round trip to widen. That widening called `SubstringTerms`, hit the same short-circuit, and
   returned the same empty list. The pass that exists to rescue short CJK words rescued nothing while
   costing the sequential scan it was budgeted for.

**Fix.** Union the two sources unconditionally, de-duplicated across the union (every shipped
`ScriptProfile` uses index 3 / substring 2 so no gram can repeat today, but `ScriptProfile` is public and
consumer-constructible, and equal lengths would otherwise double-count a gram in `LikeTermClause.MatchCount`
and distort the ordering that expression exists to provide). Single-token and all-ASCII behaviour is
byte-identical: `Spaced` does not expand, so an English query gains nothing.

**Verification.** `SearchTermsTests.Short_grams_survive_when_every_token_is_below_the_index_floor` pins the
multi-token case and the neighbour asymmetry; two sibling facts pin the single-token and ASCII paths that
must NOT change. Executed before and after against the real method:
`"配偶 客户"` → `[]` before, `[配偶, 客户]` after. Full affected suites green (1386 passed), including the
corpus goldens, the CJK recall tests and the Postgres leg.

**Introduced by** the D55 tokenization work that added `ShortGrams` — the short-circuit was carried over
from when `SubstringTerms` had only one source and empty meant empty. Unreleased, so no shipped version
carries it.

---

## 2026-08-14 — the one-line memory path ignored two registered policies, because one engine had two constructors' worth of call site

**Symptom.** None that surfaces as an error. `services.AddLyntai(cfg => cfg.UseSqliteStorage(…).AddMemory()
.AddMemoryVerification())` builds, resolves, remembers and recalls — and the verification policy is never
called. The only observable is recall QUALITY, which a consumer has no baseline for: they enabled the
feature, saw results, and had no way to learn it was inert. The identical registration behind
`AddMemoryEngine("m", e => e.UseGraph())` worked, so the difference was invisible unless someone compared
the two paths. Found 2026-08-14 during the 3.0 pre-freeze whole-repo review, by reading the two call sites
side by side rather than by any failing test.

**Root cause.** `MemoryEngineBuilder` constructed `GraphMemoryEngine` in TWO places — `UseGraph` (the
configured path) and `UseBestAvailable` (what `AddMemory()` resolves to) — each with its own copy of a
fourteen-argument list. `annotation:` and `verification:` were added to the first when those seams shipped
and never to the second, so the zero-configuration path passed neither and the engine took its documented
model-free floor for both. Nothing could catch it: the parameters are optional, so omitting them compiles;
the engine's fallback is a real, supported behaviour, so it does not throw; and every existing wiring test
exercised `UseGraph`. This is `.claude/knowledge/pitfalls.md` §DI/config's *"a documented option that isn't
wired"* for the third recorded time, and the first where the unwired option is the one its own registration
doc calls **the single largest recall-quality lever the subsystem has**.

**Fix.** One construction site — a private `BuildGraph(sp, full, store, …)` carrying the DI reads and the
five per-engine overrides. `UseGraph` passes its overrides; `UseBestAvailable`, which has no configuration
surface to name one with, passes none, and null already means "take the container registration" for every
one of them. The behaviour change is exactly that the one-line path now honours those two registrations;
nothing else moves. Fixing the SHAPE rather than adding the two missing arguments is the point: a second
copy kept in step by memory is how this happened, and the next engine parameter would have done it again.

**Verification.**
`GraphMemoryWiringTests.The_one_line_AddMemory_path_honours_a_registered_annotation_and_verification_policy`
registers recording policies through the container, drives `AddMemory("m")`, and asserts BOTH were consulted
— red against the old wiring (`Expected: ["the spouse is called Alice"]  Actual: []`), green after. Full
`verify` green: 13 gates, 2727 passed / 2748.

**Introduced by** the commits that added each seam — `annotation:` with the subject-linking work (D55) and
`verification:` with D59 — neither of which touched `UseBestAvailable`. Both are unreleased, so no shipped
version carries the defect.

---

## 2026-08-11 — the version-authorship guard reported "no problems" whenever git could not answer

**Symptom.** None, again by construction: `check-version-bump` exits 0 and prints nothing on a clean index,
which is byte-for-byte the output it produced when it could not read the index at all. A corrupt
`.git/index`, a missing `git`, or a cwd with no repository anywhere above it each yielded a passing
pre-commit guard. The pre-commit hook only masked it because `check-sensitive` runs first and fails closed
on the same condition, so the hook path usually stopped anyway — `node devtools/dev.mjs check-version` did
not. Found 2026-08-11 while writing that guard's tests (`docs/task-archive.md` Part 60), filed as Part 62, fixed here.

**Root cause.** `stagedDiff` wrapped `git diff --cached` in `try { … } catch { return '' }`. The empty
string is exactly what a file with no staged changes produces, so every failure was laundered into the
benign case and the three rules above it saw "nothing staged". The comment above it named the benign case
it was written for and the catch was never narrowed to it — this is fail-OPEN on the one check standing
between a hand-authored `<VersionPrefix>` and D19's silently-skipped release.

**Fix.** `stagedDiff` throws `StagedDiffFailure` instead; `checkVersionBump` catches it, reports it as a
problem (so every caller blocks by default) and also returns it as `gitError`, because the remediation is
the opposite one — "stop hand-editing the version" is nonsense advice for a corrupt index, and the CLI now
prints a different block for it. The benign cases were MEASURED before the catch was narrowed, not assumed:
`git diff --cached -- <file>` exits 0 with empty output for a file with no staged change, for a file that
does not exist, **and for a repository with no HEAD** (a fresh clone's first commit). All three still pass.
Not a git failure either, and worth knowing: a cwd that is not itself a repository, because git walks up.

**Verification.** `check-version-bump.test.mjs` → "when GIT ITSELF fails, the guard fails CLOSED" (6 tests:
a really corrupt index, an injected `ENOENT` for the spawn-failure branch, git's own stderr surfaced, one
problem rather than one per file, the `LYNTAI_RELEASE` hatch still short-circuiting before git is asked, and
the CLI's own exit-1 branch driven end to end through a bogus `GIT_DIR`), plus two new benign-case tests
pinned FIRST. Mutation-checked: restoring the `return ''` reddens exactly those 6 and leaves the 16 benign
ones green.

**Introduced.** `check-version-bump.mjs` at its creation; latent in every run since.

---

## 2026-08-11 — check-docs exempted the whole CHANGELOG, and the first fix for it was inert

**Symptom.** A `## Unreleased` entry asserted a behaviour the code had since changed, plus the retired
justification for it, and `check-docs` reported 39 docs clean with it present (measured 2026-08-09). A human
reviewer caught it — the exact failure `docs/DECISIONS.md` D42 created the gate to prevent. A paragraph five
lines below in the same bullet was corrected in the same editing pass, so the text was read and the stale
claim still survived.

**Root cause.** Two, one behind the other. (1) `HISTORICAL` exempted `CHANGELOG.md` wholesale on the
rationale that a record is "accurate BY using the vocabulary of its day" — true of a released section and
false of `## Unreleased`, which describes behaviour that has not shipped and can still change under the
words describing it. (2) Narrowing that exemption changed NOTHING, because `IN_SCOPE` runs first and had
never listed `CHANGELOG.md` at all: the file was excluded one stage earlier, and the gate printed the same
clean line either way. The second cause was found by probing the gate (asking what was in its file list and
how many lines it would scan) rather than by reading its exit code, which is the only way a filter-chain
edit can be verified.

**Fix.** `CHANGELOG.md` added to `IN_SCOPE`, plus a new `LIVE_PREFIX` rule that scans it down to the first
released `## <version>` heading and no further; the two-line wrap window is clipped at the same boundary so
it can never join a live line to a historical one. `check-samples` imports the same boundary rather than
keeping a second copy of the answer. One `retiredTerms` pattern was widened in the same pass, because the
first real run showed the same claim caught in one phrasing and missed, four hundred lines earlier, in
another: "only reads … never writes it back" versus "ours reads … never writes it back". <!-- drift-ok -->

**Verification.** `check-docs.test.mjs` → "CHANGELOG.md is historical only BELOW its first released heading"
(7 tests, including `IN_SCOPE('CHANGELOG.md')` itself — the assertion that fails against the inert first
attempt — a released section staying exempt, and the window not reaching across the boundary), plus
`check-samples.test.mjs` → the same split for a fenced block. Mutation-checked 4 ways (drop it from
`IN_SCOPE`; drop the `LIVE_PREFIX` rule; remove the prefix clip; move the boundary three lines) — each
reddens 1–4 tests. On the real repository the first run reported 3 stale passages: two fixed, one
`drift-ok`-annotated (it names the policy as a mutation's value, never as a default).

**Introduced.** The exemption in `6837ba3` (2026-08-08), with `check-docs` itself.

---

## 2026-08-11 — the leak guard never scanned a file whose NAME is non-ASCII, and called it a deletion

**Symptom.** None, which is the whole problem: `check-sensitive --tree` reported `clean` over a tree
containing a file with a dev-machine path in it. Reproduced by the first fixture that named a document in
CJK — `docs/灵台.md`, holding a synthesized Windows user-home path — while `docs/plain.md` with the identical <!-- link-ok: guard FIXTURE names, never files here -->
content was flagged. Found while writing this guard's first tests (`docs/task-archive.md` Part 60), not by any run.

**Root cause.** `git ls-files` C-QUOTES a path containing any non-ASCII byte: `docs/灵台.md` comes back as <!-- link-ok: guard FIXTURE names, never files here -->
the literal string `"docs/\347\201\265\345\217\260.md"`. That name matches no file on disk, so `readFileSync`
threw `ENOENT` — and the ENOENT branch of the scan classifies ENOENT as *a tracked file deleted from the
working tree*, a real and legitimate mid-refactor state that is deliberately skipped rather than
fail-closed (added earlier so a refactor did not look like a leak report). The two behaviours compose into a
false PASS: the file is silently not scanned, and the only output is a line saying one tracked file was
deleted. Staged mode had the same root cause with the opposite ending — `git show :"docs\347…"` fails, which
lands in `unreadable` and correctly fails closed, so the pre-commit hook blocked with a confusing git error
while `verify` sailed through. `check-docs` had the identical defect via `git ls-files`, ending in its bare
`catch { continue; }`: an in-scope document with a CJK name was never scanned and never reported as unscanned.

**Fix.** `-z` on every git path listing, split on NUL instead of newline — `git ls-files -z` in
`check-sensitive.mjs`'s `sources()` and in `check-docs.mjs`'s `trackedFiles()`, and
`git diff --cached --name-only --diff-filter=ACM -z` for the staged list. `-z` disables quoting entirely, so
paths arrive as raw bytes and both readers agree with git about how a name is spelled. No pattern, scope or
classification logic changed.

**Verification.** `devtools/scripts/__tests__/check-sensitive.test.mjs` → "sees a leak in a file whose NAME
is non-ASCII": a fixture repository with `docs/灵台.md`, asserting `sources()` returns the path raw, that <!-- link-ok: guard FIXTURE names, never files here -->
`checkSensitive` returns 1 naming `docs/灵台.md:1`, and that the run does NOT report it as a deletion (the <!-- link-ok: guard FIXTURE names, never files here -->
last assertion is the one that fails against the old behaviour). Twin in
`check-docs.test.mjs` → "returns a NON-ASCII path unquoted". Both fail against the pre-fix scripts.
Adjacent regression cover added at the same time: the deletion branch itself still passes (a genuinely
deleted tracked file is reported and does not block), so the fix did not simply disarm it.

**Introduced.** `check-sensitive.mjs` in `55c079b` (2026-07-17, the original scaffolding);
`check-docs.mjs` in `6837ba3` (2026-08-08). Latent in every run of both since.

---

## 2026-08-11 — check-packages crashed instead of reporting when the Baselines directory was absent

**Symptom.** `readdirSync` `ENOENT` stack trace out of `check-packages`, naming no package, when the last
file in `tests/Lyntai.Tests/Api/Baselines/` is deleted (a directory with no files does not exist in git, so a
fresh branch that deletes a package's baseline can reach this).

**Root cause.** The orphan-baseline sweep enumerated the baseline directory unconditionally, after the
per-package loop had already collected the correct `no API baseline` problems. The exception discarded them.

**Fix.** `existsSync` first; an absent directory yields an empty list, so the per-package problems are
reported instead. Fails closed either way — this only changes a stack trace into the report the operator
needed.

**Verification.** `check-packages.test.mjs` → "the API baseline file" and "returns 1 and prints every problem
with its fix", both of which delete the only baseline and assert the *reported* problem.

**Introduced.** `d9ab870` (2026-08-04), with the gate.

---

## 2026-08-09 — all three backends cut a long-quiet exact fact at the candidate LIMIT, not just the WHERE

**Symptom.** `IMemoryGraphStore.SeedAsync` documents, in prose, that authoritative material is admitted
unconditionally. The predicate half of that promise was fixed the same day (the entry directly below this
one), but a second route to the same exclusion survived it: every backend's `SeedAsync` ordered candidates by
`last_recalled_position DESC` and took a capped number of rows. A long-quiet exact fact — one nobody had
touched since it was written — has the *lowest* `last_recalled_position` in its scope, so it sorts last, and
the `LIMIT` cuts it before the engine ever ranks anything. This is D41's rejected behaviour ("faintness never
excludes") reached by ordering instead of by predicate — the row was never excluded by the `WHERE`, only by
running out of `LIMIT` before reaching it.

**Root cause.** `SqliteMemoryGraphStore.SeedAsync`'s LIKE and no-query branches, `PostgresMemoryGraphStore
.SeedAsync`'s single query, and `InMemoryMemoryGraphStore.SeedAsync`'s LINQ pipeline all ordered candidates by
recency alone (`ORDER BY n.last_recalled_position DESC, n.id DESC` / `.OrderByDescending(n =>
n.LastRecalledPosition)`) before applying the `limit`. The grade carve-out in the `WHERE`/`.Where(...)`
predicate (fixed in the entry below) guarantees an authoritative row is never *filtered out* — it says
nothing about whether the row survives the `LIMIT` that runs after the filter and before the caller ever sees
a rank.

**Fix.** Grade now leads every seed ordering, ahead of recency: `ORDER BY (n.grade = @authoritative) DESC,
n.last_recalled_position DESC, n.id DESC` on both relational backends (`SqliteMemoryGraphStore.cs`'s LIKE and
no-query branches; `PostgresMemoryGraphStore.cs`'s single query, both call sites), and
`.OrderByDescending(n => n.Grade == MemoryGrade.Authoritative).ThenByDescending(n =>
n.LastRecalledPosition).ThenByDescending(n => n.Id)` on `InMemoryMemoryGraphStore.cs`. Exact facts now occupy
the head of the candidate set regardless of how stale they are, so the `LIMIT` cuts fresher associative
material first.

**Deliberately NOT changed: `SqliteMemoryGraphStore.Merge`.** Task 1's reserved-capacity `Merge` (which
combines the FTS branch's matches with a separately-fetched exact-facts query) already guarantees survival
past ITS limit by construction — it reserves capacity for exact facts rather than relying on ordering. An
earlier draft of this fix flipped `Merge` to put exact facts first too; that was wrong, because `Merge`
renormalizes `Relevance` over the merged order, and ordering exact facts first would hand them the *top* of
the gradient and outrank every genuine match — the opposite of the intent (see `Merge`'s own XML doc and
`Exact_facts_survive_a_full_page_of_matches`, which pins the current order).

**Amended 2026-08-09 (final branch review): `Merge` DID need one change, in the other direction.** It sent
*all* of `exact` to the tail, including rows that were also a top bm25 match — the `exactIds` filter stripped
those out of the matched portion and re-appended them last. An exact fact that directly ANSWERS the query
therefore took the bottom of the gradient (`≈ 1/merged.Count` where pre-branch it kept `≈ 1.0`), and because
`GraphMemoryEngine.RecallAsync` ranks by `Relevance × Retrievability × HopAttenuation^Hop` and then
`.Take(limit)`s, it could be dropped from recall outright — strictly worse than pre-branch. The partition is
now on `matched`'s ids, so only genuine non-matches are tail-ordered; the paragraph above stands for the
*ordering of non-matches*, which is unchanged. Pinned by
`SqliteMemoryGraphStoreTests.An_exact_fact_that_matches_the_query_keeps_its_earned_position` — red at
`Relevance 0.0999…` before the change, green after — which is the case both tests named above structurally
cannot see, because each uses an exact fact matching nothing.

**Verification.** `MemoryGraphStoreContract.Seeding_admits_a_long_quiet_exact_fact_over_fresher_material`:
one authoritative fact, then 60 fresher associative writes in the same scope, `SeedAsync(..., limit: 10)`
must still return the authoritative fact. Wired against all three backends
(`SqliteMemoryGraphStoreTests.Exact_survives_the_limit`, `InMemoryMemoryGraphStoreTests
.Exact_survives_the_limit`, inline in `PostgresStorageTests.Graph_store_satisfies_the_contract`, coverage
guard bumped 19→20). Red before the fix on all three
(`node devtools/dev.mjs test --filter "Exact_survives_the_limit|Graph_store_satisfies_the_contract"`:
`Assert.Contains() Failure` — the count assertion passed (10 returned), the membership one did not — on
SQLite, InMemory, and a real Postgres container), green after (3/3). Full `--filter "MemoryGraphStore"`
regression: 43/43, including `Exact_facts_survive_a_full_page_of_matches` and
`Upsert_then_seed_by_single_token_substring` (`Assert.Single`, unaffected by the reordering). `node
devtools/dev.mjs verify` green: 1910 passed, e2e 3/3.

---

## 2026-08-09 — SQLite's FTS seed path silently dropped an authoritative fact the query didn't match

**Symptom.** `IMemoryGraphStore.SeedAsync` documents, in prose, that AUTHORITATIVE material is admitted
unconditionally — "the query does not exclude them." On SQLite this was false whenever the query's trigram
index matched *something else* in scope: an exact fact sharing no trigram with the query was silently not
seeded. Ask about "restaurant" and a stored dietary constraint never reaches the prompt.

**Root cause.** `SqliteMemoryGraphStore.SeedAsync`'s FTS branch (`src/Lyntai.Storage.Sqlite/SqliteMemoryGraphStore.cs`,
introduced in `5756755` *feat(storage): persist graph memory on SQLite*) filtered its `MATCH` query on
`engine`/`task_key`/`scope` only. The `authoritative` parameter was bound into the query's parameter object
but never referenced in the SQL. Worse, `if (hits.Count > 0) return hits;` returned as soon as the trigram
index matched anything, before ever reaching the LIKE branch further down — which *does* carry the grade
carve-out (`n.grade = @authoritative OR n.content LIKE @pattern`). So the carve-out existed in the code, just
on a branch that a matching query never reached. Postgres (`PostgresMemoryGraphStore`) and InMemory
(`InMemoryMemoryGraphStore`) apply the carve-out directly in their query/filter and were already correct.

**Fix.** When the FTS branch gets a hit, fetch the scope's authoritative facts in a second query (ordered by
`last_recalled_position DESC, id DESC`, same shape as the LIKE branch) and merge them with the matched set
via a new `Merge` helper. `Merge` gives exact facts RESERVED capacity rather than appending them after a full
page of matches and truncating the tail — `matched` is itself `LIMIT`-bound, so an append-then-truncate drops
every exact fact whenever the query alone fills `limit`, which is the same defect wearing a hit-count
condition (caught in review before it shipped). The merged list is then renormalized as one `Relevance`
gradient — matches lead, exact non-matches take the low end — because `Relevance` is a per-query rank
position, and splicing two independently-1.0-topped batches would report a fact that matched nothing as the
best hit for the query. Only runs when the trigram index matched something at all: with no match the
existing fallback to LIKE (which already carries the carve-out) still applies.

**Verification.** Two tests, at two different scopes. The backend-agnostic contract fact,
`MemoryGraphStoreContract.Seeding_admits_authoritative_material_the_query_does_not_match` (one exact fact,
one non-matching associative note — both must come back), is wired against all three backends
(`SqliteMemoryGraphStoreTests.Admits_exact_on_query_path`, `InMemoryMemoryGraphStoreTests.Admits_exact_on_query_path`,
and inline in `PostgresStorageTests.Graph_store_satisfies_the_contract`) and pins the ORIGINAL defect —
red before the fix (SQLite only; InMemory and Postgres already passed), green after on all three
(`node devtools/dev.mjs test --filter "Admits_exact_on_query_path"`: 2/2 SQLite+InMemory;
`--filter "Graph_store_satisfies_the_contract"`: 1/1 against a real Postgres container).

A second, SQLite-only test, `SqliteMemoryGraphStoreTests.Exact_facts_survive_a_full_page_of_matches`, pins
the `Merge` mechanism itself — reserved capacity and renormalized relevance — by writing 12 matching notes
against a `limit` of 10, so the query branch alone fills the page and an append-then-truncate `Merge` would
silently drop the exact fact again. It stays SQLite-only rather than joining the shared contract fact: no
other backend merges two independent queries (InMemory and Postgres admit an exact fact in the SAME
query/filter as the match, so there is nothing to merge), and `Relevance < 1` is not a portable assertion:
only SQLite computes a non-trivial relevance gradient at all. Red against the pre-fix
append-then-truncate `Merge` (`Sequence contains no matching element` — the exact fact is entirely absent),
green after
(`node devtools/dev.mjs test --filter "Exact_facts_survive_a_full_page_of_matches"`: 1/1). Full
`--filter "MemoryGraphStore"` regression run: 41/41, including `Fts_stays_in_sync_after_a_delete` and
`Cjk_substring_recall`, the two other tests exercising the same branch.

*(Whether the same reserved-capacity truncation defect exists in InMemory/Postgres when their own match count
exceeds `limit` is a separate question — tracked and fixed as part of the grade-priority ordering work, not
this fix.)*

---

## 2026-08-05 — a non-zero exit code masked a CLI backend's own account of a failed turn

**Symptom (reported by a consuming app, filed as `TASKS.md` CLI15).** A `codex` turn run against an account
whose login had expired came back as a bare `LlmVerdict.Failed` whose detail was
`exit 1: Reading prompt from stdin...`. The actual failure — a 401 — appeared nowhere, so the app told its
user the CLI was missing or not on PATH. The right remedy was "log in again".

**Root cause.** `CliProviderEngine.CompleteAsync` returned on `result.ExitCode != 0` *before* it parsed
stdout, classifying the stderr tail. The dialect's in-band `CliOutputEventKind.Failure` — which carries the
backend's own message and is what makes the verdict actionable — was therefore never read on that path.

The two halves arrived in the **same** commit, `58a5657` (*feat(cli): generic CLI provider engine + codex
backend…*), which is why nothing looked wrong: the in-band failure vocabulary was added because codex reports
a failed turn **at exit 0**, and that measured case works correctly. Nobody asked what should happen when a
backend does *both*, and no test paired them. A consumer's measurement supplied the missing case.

Two consequences, not one. The detail was wrong (chatter instead of the reason), and so was the **verdict**:
`AuthFailed` benches the host for the cooldown window, `Failed` merely advances — so a fleet-wide credential
problem kept being retried against the same backend.

**Fix.** `src/Lyntai.Core/Llm/Cli/CliProviderEngine.cs` — parse stdout first; a reported in-band failure wins
and is classified, with the exit code kept in the detail as context (`exit {code}: {message}`). The exit-code
reply is unchanged when the backend reported nothing in band. This is the ordering `StatusAsync` already
used ("parse the answer, then fall back to the exit code"); the completion path simply never had it.

**Verification.**
- Three failing tests first, all red before the change and green after: `CliProviderEngineTests`
  `A_reported_failure_wins_over_a_NONZERO_exit_and_its_stderr_chatter` (generic seam, `FakeCliDialect`),
  `CodexCliProviderTests.An_in_band_failure_is_classified_even_when_the_process_ALSO_exits_nonzero`
  (the measured JSONL pair), and `…_over_a_real_spawn` (the same pair through `codex-stub.mjs`, so the
  ordering is pinned against a real exit code and a real stderr rather than a hand-built `ProcessResult`).
- A fourth test, `A_nonzero_exit_with_NO_reported_failure_still_reports_the_exit_and_its_stderr`, pins the
  half that must **not** change — it passed before and after.
- `devtools/scripts/codex-stub.mjs` grew the measured `AUTH_ERROR_EXIT` shape (a bare `error` line whose
  `message` is a **string**, a `turn.failed` whose `error` is an **object**, stderr chatter, non-zero exit).
  While there, its three prompt-marker branches stopped calling `process.exit()` mid-write and set
  `process.exitCode` instead — exiting with queued stdout writes can drop the very lines a test asserts on
  (`.claude/rules/windows-machine.md` §Scripts and exit codes).
- `node devtools/dev.mjs verify` green.

**Not affected, checked rather than assumed.** `CliProviderEngine.StreamAsync` already ends the stream on the
in-band `Failure` event before the runner's non-zero-exit `ProcessRunException` can surface, and both
`ClaudeAgentSession` and `CodexAgentSession` suppress a second terminal via their `sawTerminal` guard — so
the "one failure reported twice" half of the consumer's report was already handled everywhere except here.
