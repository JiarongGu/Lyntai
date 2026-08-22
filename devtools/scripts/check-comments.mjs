// check-comments — FAIL when a comment block outgrows what it explains.
//
// WHY THIS IS A GATE. Measured 2026-08-16: `src/` carried **0.86 comment lines per line of real code** and
// 1.6× more prose than `DECISIONS.md` + `pitfalls.md` + the task archive + the design contract COMBINED. A
// third of it sat in blocks long enough that nobody reads them in place — including a 120-line `<remarks>`
// on one method.
//
// A long comment is an unindexed, ungated, unreviewed document in the worst possible location. This
// repository runs `check-docs`, `check-links` and `check-counts` to stop its MAINTAINED prose rotting; none
// of them can see a code comment, so the longest and least-read prose in the tree was also the only prose
// nothing checked. `pitfalls.md` records it rotting exactly as you would expect.
//
// The rule this enforces is `.claude/rules/code-commentary.md`: the XML doc is the CONTRACT, a `//` comment
// ANNOTATES the code beneath it, and the DESIGN argument belongs in a record.
//
// SCOPE — all four tiers (`src`, `tests`, `devtools`, `bench`), `.cs` and `.mjs`. Measured 2026-08-16,
// comment lines per line of real code:
//
//     src/       378 files   ratio 0.65   (was 0.86, 28 long blocks / 893 lines, before that day's paydown)
//     tests/     262 files   ratio 0.27
//     devtools/   47 files   ratio 0.27
//     bench/      13 files   ratio 0.27
//
// It scanned `src/` ONLY at first, and this header argued that was deliberate: the RATIO problem is
// `src/`-specific by roughly 3x, `src/` is the only tier whose comments ship to consumers as XML docs, and a
// test explaining at length what its fixture proves is doing the job the rule asks. Half of that survived
// contact with a measurement. The worst block in `tests/` — 88 lines — states a real constraint on what any
// number measured from that corpus may claim, AND carries a dated heading plus a long narration of what an
// earlier version did wrong. Same defect, lower density.
//
// Widening cost nothing, because the ratchet does not demand a paydown — it freezes. And it immediately
// found FIVE stacked-summary defects the other tiers had been hiding, one of them a 53-line record doc
// stranded above a different type, so the record it documented had no doc at all. That is the argument
// against "this tier is different": the tiers were not better, they were unmeasured.
//
// THE RATCHET, and why it is not a plain threshold. 69 blocks were already over the limit when this landed,
// so a gate that simply failed would have to be switched off. Instead every offending FILE carries its
// current worst block in `commentBlockAllowances`, and an allowance that is LARGER than the file's actual
// worst block FAILS — so the numbers can only ever come down, and a file that improves must record it. That
// is the same "an allowance that stops matching FAILS" discipline `check-api-vocabulary` and `check-links`
// already carry, turned into a budget instead of a boolean.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { repoFiles } from './_repo-files.mjs';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

/** The limit a block may reach before the design argument has leaked in. */
export const MAX_BLOCK = 25;

/** Put this on a block's FIRST line to exempt it. Deliberately its own token, never `drift-ok`. */
export const ESCAPE = 'comment-ok';

/** The tiers scanned. `src/` ships to consumers; the other three rot the same way and were unscanned. */
export const TIERS = ['src', 'tests', 'devtools', 'bench'];

export const trackedFiles = (repo) => repoFiles(repo, TIERS);

/**
 * Every comment block in one file, as `{ line, length, escaped }`.
 *
 * A BLOCK is consecutive `//` or `///` lines, ended by a blank line, a line of code — or by the start of a
 * doc tag that documents a DIFFERENT thing. The unit being measured is how much uninterrupted prose a
 * reader meets about ONE subject, which is what the rule is about ("a comment should be smaller than what
 * it explains").
 *
 * <p>Without that last boundary the metric is wrong in a way that punishes the right thing: a record with
 * twenty constructor parameters has twenty adjacent `&lt;param&gt;` tags and no blank lines, so a perfectly
 * proportionate two lines each reads as one 40-line "block" — indistinguishable from a 40-line `<remarks>`
 * on a single method, which is the actual defect. Splitting on the tag boundary measures prose-per-subject
 * either way.</p>
 *
 * <p>`summary` and `remarks` are in the list for the same reason and were MISSING from it until 2026-08-16,
 * which made the gate disagree with the sentence above: a `&lt;summary&gt;` followed with no blank line by a
 * `&lt;remarks&gt;` fused into one measurement, so a 11-line summary plus a 16-line remarks reported as a
 * 27-line block and went onto an allowance it did not need. Worse, the fusion HID a real defect — two
 * stacked `&lt;summary&gt;` elements on one method (the first belonging to a method 40 lines below, which was
 * therefore left with no summary at all) measured as a single long block and read as a fat comment, so the
 * allowance blessed it and nothing looked again. A second `&lt;summary&gt;` is by definition a different
 * subject; the gate now says so.</p>
 */
const NEW_SUBJECT = /^\/{2,3}\s*<(summary|remarks|param|typeparam|returns|exception|value)\b/;

export function blocksIn(text) {
  const lines = text.split(/\r?\n/);
  const out = [];
  let run = 0, start = 0, escaped = false;
  const flush = () => { if (run > 0) out.push({ line: start, length: run, escaped }); run = 0; };
  for (let i = 0; i <= lines.length; i++) {
    const t = (lines[i] ?? '').trim();
    if (!t.startsWith('//')) { flush(); continue; }
    if (NEW_SUBJECT.test(t)) flush();
    if (run === 0) { start = i + 1; escaped = t.includes(ESCAPE); }
    run++;
  }
  return out;
}

/** The worst block in a file, ignoring escaped ones. 0 when the file has none. */
export const worstBlock = (text) =>
  blocksIn(text).filter((b) => !b.escaped).reduce((n, b) => Math.max(n, b.length), 0);

/** Every unescaped block over the limit, worst first — the unit the ledger records. */
export const overLimitBlocks = (text) =>
  blocksIn(text).filter((b) => !b.escaped && b.length > MAX_BLOCK).sort((a, b) => b.length - a.length);

/**
 * An allowance as the MULTISET of a file's over-limit blocks, worst first. A bare number reads as `[n]`, so
 * a one-block file stays readable in the config.
 *
 * The ledger recorded one number per FILE until 2026-08-16 — its worst block — so every OTHER over-limit
 * block in that file was invisible both to the gate and to the debt total it printed. Measured when this
 * changed: the ledger said 19 files / 614 lines while the tree held 28 blocks / 893, leaving 279 lines of
 * debt outside the budget and one file carrying SEVEN over-limit blocks against a single recorded number.
 * Worse than the undercount, a file on an allowance of 38 could grow unboundedly many new 38-line blocks and
 * stay green — the opposite of a ratchet.
 */
export const asAllowanceList = (v) => (Array.isArray(v) ? [...v] : [v]).sort((a, b) => b - a);

/**
 * Punctuation left STRANDED by a deleted clause — the wreckage of an edit, not a style opinion.
 *
 * Both shapes were produced by this repository's own comment sweep and SHIPPED, inside `///` docs on public
 * members, so they reached consumers' IntelliSense as "…reads as the neutral 5 instead (" and "…field
 * instead . This exists so…". Every gate was green: `check-comments` measures block LENGTH, `check-docs`
 * scans prose files not `src/`, and the compiler has no opinion about a sentence. A person found them.
 *
 * The two rules are narrow ON PURPOSE, because the obvious broad versions are all false positives here:
 * "a comment line starting with punctuation" hits `.cmd, then .exe` and a wrapped `: 1e-6</c>)`, and "a line
 * ending in `(`" hits a wrapped `services.AddHttpClient(`. Measured across `src`, `tests`, `devtools` and
 * `bench`: the broad forms give 5 hits and 0 defects, these give 0 hits — while still firing on both real
 * ones. The distinguishing feature is the SPACE: a deleted parenthetical leaves `instead (`, a wrapped call
 * leaves `AddHttpClient(`; a deleted clause leaves `. This`, a file extension is `.cmd`.
 *
 * There is deliberately no escape token. Nothing in the tree legitimately matches, so an allowance would be
 * a hole opened for a case that does not exist — if a legitimate one ever appears, narrow the rule.
 */
export const STRANDED = [
  { name: 'a clause was deleted before it', test: (body) => /^[.,;]\s+\S/.test(body) },
  { name: 'an opening parenthesis is left dangling', test: (body) => /\s\(\s*$/.test(body) },
];

/**
 * Doc runs carrying more than one `<summary>` — two members' documentation fused onto one.
 *
 * Found three times on 2026-08-16, which is why it is a gate and not a note. In each case a long block
 * describing member A sat directly above member B's own `<summary>`, so B carried two summaries and A
 * carried none — in one case a `<param>` list with nothing above it. The compiler does not warn, the XML is
 * well-formed, and the docs SHIP that way.
 *
 * The third instance was created BY THIS SESSION, inserting a helper between an existing summary and the
 * method it described — while the session was fixing the other two. That is the argument for gating it: the
 * defect is a side effect of ordinary editing, invisible in review because both halves look correct on their
 * own, and `check-comments` used to make it worse by FUSING the two into one long block that read as a fat
 * comment and got an allowance.
 */
export function stackedSummaries(text) {
  const lines = text.split(/\r?\n/);
  const out = [];
  let summaries = 0, start = 0, inRun = false;
  const flush = () => {
    if (summaries > 1) out.push({ line: start, count: summaries });
    summaries = 0;
    inRun = false;
  };
  for (let i = 0; i <= lines.length; i++) {
    const t = (lines[i] ?? '').trim();
    if (!t.startsWith('///')) { flush(); continue; }
    if (!inRun) { inRun = true; start = i + 1; }
    if (/<summary\b/.test(t)) summaries++;
  }
  return out;
}

/** Comment lines whose punctuation says an edit removed the text around it. */
export function strandedIn(text) {
  const out = [];
  text.split(/\r?\n/).forEach((line, i) => {
    const t = line.trim();
    if (!t.startsWith('//')) return;
    const body = t.replace(/^\/{2,3}\s?/, '');
    for (const rule of STRANDED) if (rule.test(body)) out.push({ line: i + 1, why: rule.name, text: t });
  });
  return out;
}

export function checkComments(repo, cfg, log = console.log, files = null) {
  const source = files ?? trackedFiles(repo);
  // `.mjs` too — the guard scripts and the dev loop are the `devtools/` tier, and a gate that exempted its
  // own author's prose would be the least defensible scope of all.
  const scanned = source.filter((f) => f.endsWith('.cs') || f.endsWith('.mjs'));

  // Fail CLOSED on an empty SOURCE list — a broken listing indicts whoever supplied it, which is the shape
  // that let `check-sensitive` skip every renamed file. Zero SURVIVORS of the .cs filter is different and
  // legitimate (a caller may inject a list with none).
  if (source.length === 0) {
    log('check-comments: ✗ nothing to scan — the file list is empty, which is a broken listing, not a clean tree');
    return 1;
  }

  const allowances = cfg.commentBlockAllowances ?? {};
  const over = [];
  const slack = [];
  const stranded = [];
  const stacked = [];
  const seen = new Set();

  for (const f of scanned) {
    let text;
    try { text = fs.readFileSync(path.join(repo, f), 'utf8'); } catch { continue; }
    seen.add(f);

    for (const s of strandedIn(text)) stranded.push({ file: f, ...s });
    for (const s of stackedSummaries(text)) stacked.push({ file: f, ...s });

    const blocks = overLimitBlocks(text);
    const actual = blocks.map((b) => b.length);
    const allowed = allowances[f] === undefined ? [] : asAllowanceList(allowances[f]);

    // The ledger must EQUAL the tree, block for block. Position-wise against the worst-first ordering, so a
    // file's blocks are budgeted individually rather than summarized — that is what makes a NEW long block
    // in an already-budgeted file fail instead of hiding behind its worst one.
    for (let i = 0; i < Math.max(actual.length, allowed.length); i++) {
      const a = actual[i], b = allowed[i];
      if (a !== undefined && (b === undefined || a > b))
        over.push({ file: f, line: blocks[i].line, length: a, budget: b ?? MAX_BLOCK });
      // An allowance is a DEBT, not a permission: once the file improves it must be lowered, or the next
      // regression back up to the old number passes unnoticed. Same reasoning as an exclusion that stops
      // matching.
      else if (b !== undefined && (a === undefined || b > a))
        slack.push({ file: f, allowed: b, actual: a ?? 0 });
    }
  }

  const stale = Object.keys(allowances).filter((f) => !seen.has(f));

  if (over.length === 0 && slack.length === 0 && stale.length === 0 && stranded.length === 0
      && stacked.length === 0) {
    const budgeted = Object.keys(allowances).length;
    const lists = Object.values(allowances).map(asAllowanceList);
    const blocks = lists.reduce((s, l) => s + l.length, 0);
    const debt = lists.reduce((s, l) => s + l.reduce((t, n) => t + n, 0), 0);
    log(`check-comments: ${scanned.length} source file(s) — no comment block over ${MAX_BLOCK} lines `
      + `✓ (${blocks} block(s) across ${budgeted} file(s) still on a recorded allowance, ${debt} lines of debt)`);
    return 0;
  }

  if (over.length > 0) {
    log(`check-comments: ✗ ${over.length} comment block(s) over their limit\n`);
    for (const o of over.slice(0, 40))
      log(`  ${o.file}:${o.line}  ${o.length} lines (limit ${o.budget})`);
    if (over.length > 40) log(`  … and ${over.length - 40} more`);
    log('');
    log('  A comment should be smaller than what it explains (`.claude/rules/code-commentary.md`).');
    log('  Move the design argument to the record that owns it and keep the RULE plus a pointer;');
    log(`  a block that genuinely earns its length takes \`${ESCAPE}\` on its first line.`);
  }

  if (slack.length > 0) {
    log(`\ncheck-comments: ✗ ${slack.length} allowance entr(ies) are now LOOSER than the file needs — lower them`);
    for (const s of slack)
      log(`  ${s.file}: allowed ${s.allowed}, the matching block is now ${s.actual || 'gone'}`);
    log('  An allowance is a debt, not a permission: leave it high and the next regression passes unnoticed.');
  }

  if (stranded.length > 0) {
    log(`\ncheck-comments: ✗ ${stranded.length} comment line(s) carry punctuation an edit left behind`);
    for (const s of stranded.slice(0, 20)) log(`  ${s.file}:${s.line}  ${s.why}\n      ${s.text}`);
    log('  A deleted clause takes its punctuation with it. Two of these SHIPPED in public XML docs.');
  }

  if (stacked.length > 0) {
    log(`\ncheck-comments: ✗ ${stacked.length} doc run(s) carry more than one <summary>`);
    for (const s of stacked) log(`  ${s.file}:${s.line}  ${s.count} summaries in one run`);
    log('  Two members\' docs are fused: one member has two summaries and another has none.');
    log('  Move the displaced block onto the member it describes, or make it a <remarks>.');
  }

  if (stale.length > 0) {
    log(`\ncheck-comments: ✗ ${stale.length} allowance(s) name a file that is not scanned — delete them`);
    for (const f of stale) log(`  ${f}`);
  }

  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkComments(repo, config);
}
