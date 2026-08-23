// release-notes — turn a commit range into the GitHub Release body.
//
// WHY THIS IS A SCRIPT AND NOT INLINE POWERSHELL. It used to be ~40 lines of `pwsh` inside
// `.github/workflows/release.yml`, which meant the one piece of release output a consumer actually READS
// was the only piece nothing could test. `test-devtools` runs FIRST in `verify` for exactly this reason:
// a component whose failure mode is plausible-but-wrong output cannot be validated by running it once.
//
// THE MEASURED DEFECT THAT MOVED IT HERE (2026-08-16, on the eve of 3.0). The old categorizer keyed on
// `^feat(scope)?:` and `^fix(scope)?:`, neither of which matches conventional commits' BREAKING marker —
// `feat(memory)!:`, `fix(core)!:`, `refactor(api)!:`. So every breaking change fell through to a bucket
// titled "Other changes". That was **29 commits** of history when this was measured, and **11 of 11** in
// the v2.5.0..3.0 range — a major release whose entire story is breaking changes would have published
// notes reading "New features: 1" with the eleven listed under "Other". Those two figures are a dated
// measurement, not a live count; the RULE is pinned by a test over the real log, which is what cannot rot.
//
// The sharpest form of it: plain `refactor:` is DROPPED as non-user-facing, and `refactor!:` is the single
// most important line a consumer can read. The only reason a breaking refactor was not silently deleted is
// that the drop pattern ALSO failed to match the `!`. Correctness rested on a regex failing.
//
// THE HONEST LIMIT. Breaking-ness is read from the `!` marker in the SUBJECT only. Conventional Commits
// also allows a `BREAKING CHANGE:` footer in the body, and this reads `%s`, so a commit that uses only the
// footer is categorized by its kind. That is stated rather than discovered; this repository has used the
// `!` marker on all 29 of its breaking commits, and widening to bodies is a change to what git is asked
// for, not a change to these rules.
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);

/**
 * Commit kinds that are real work but not news to a consumer of the packages. A commit of one of these
 * kinds is DROPPED from the notes entirely — unless it is marked breaking, which outranks the kind.
 *
 * `bench` and `tasks` were added 2026-08-16 from a census of the actual history; without them a benchmark
 * commit surfaced in the notes under "Other changes", which is noise of exactly the kind this list exists
 * to remove.
 */
export const NON_USER_FACING = ['chore', 'docs', 'refactor', 'style', 'perf', 'test', 'ci', 'build', 'bench', 'tasks'];

/**
 * Scopes naming a tier that SHIPS NOTHING, so a change to one is never a consumer release note whatever its
 * kind. Dropped for the same reason as the kinds above, on a different axis.
 *
 * MEASURED 2026-08-23, previewing the notes after this tag's work: of four bullets a consumer would read,
 * THREE were internal — `feat(bench):` a measurement sweep listed under "New features", and two
 * `fix(devtools):` gate repairs listed under "Fixes". Only one bullet was about the library. The kind list
 * could not catch them because the non-shipping part is the SCOPE: the kind is a perfectly honest `feat`
 * or `fix`, of something that is not in `packableProjects`.
 *
 * This is the same blindness `pitfalls.md` records for this script's first defect — a classifier written
 * from a vocabulary list, missing an axis the vocabulary does not cover — and it fails the same silent way:
 * every commit still lands somewhere, under a heading a reader would believe.
 *
 * A BREAKING marker still outranks this, deliberately. `feat(devtools)!:` is nonsense today (nothing there
 * is public surface), but if it is ever written, a change announcing itself as breaking should be read by a
 * human rather than dropped by a scope rule.
 */
export const NON_SHIPPING_SCOPES = ['bench', 'devtools', 'tasks', 'docs', 'ci'];

// <kind>[(scope)][!]: <summary>. The kind is [a-z+]+ so a compound prefix (`docs+guards:`) parses as a kind
// rather than as no prefix at all — it will not match a known kind, so it lands in "Other changes", which
// is the honest answer for a subject that claims to be two things.
const SUBJECT = /^([a-z+]+)(\([^)]+\))?(!)?:\s*(.+)$/;

/**
 * Split commit subjects into the sections of a release body.
 *
 * Precedence is BREAKING first, and it is deliberate: a breaking `refactor!:` is news even though every
 * non-breaking `refactor:` is dropped. Kind is only consulted once breaking-ness has been ruled out.
 */
export function categorize(subjects) {
  const breaking = [], features = [], fixes = [], other = [], dropped = [];

  for (const raw of subjects) {
    const subject = String(raw).trim();
    if (!subject) continue;

    const m = SUBJECT.exec(subject);
    if (!m) { other.push(subject); continue; }

    const [, kind, scope, bang, summary] = m;

    // The `!` outranks the kind. A breaking change keeps its scope in the text, because "which package
    // breaks" is the first thing a consumer needs and the summary alone rarely says it.
    if (bang) { breaking.push(scope ? `**${kind}${scope}** ${summary}` : `**${kind}** ${summary}`); continue; }

    // A scope naming a tier that ships nothing is dropped whatever the kind — checked BEFORE kind, because
    // the kind is honest and the scope is what makes it not a consumer's business.
    if (scope && NON_SHIPPING_SCOPES.includes(scope.slice(1, -1))) { dropped.push(subject); continue; }

    if (kind === 'feat') features.push(summary);
    else if (kind === 'fix') fixes.push(summary);
    else if (NON_USER_FACING.includes(kind)) dropped.push(subject);
    else other.push(subject);
  }

  return { breaking, features, fixes, other, dropped };
}

/**
 * The highest release tag STRICTLY BELOW `current`, compared numerically so `v1.10.0` outranks `v1.6.0`.
 * Returns null when there is no earlier tag, which is what a first release looks like.
 */
export function previousTag(tags, current) {
  const parse = (t) => {
    const m = /^v?(\d+)\.(\d+)(?:\.(\d+))?/.exec(String(t).trim());
    return m ? [Number(m[1]), Number(m[2]), Number(m[3] ?? 0)] : null;
  };
  const cmp = (a, b) => (a[0] - b[0]) || (a[1] - b[1]) || (a[2] - b[2]);

  const currentVersion = parse(current);
  let best = null, bestVersion = null;
  for (const tag of tags) {
    const v = parse(tag);
    if (!v) continue;
    if (currentVersion && cmp(v, currentVersion) >= 0) continue;   // skip the current tag and anything newer
    if (!bestVersion || cmp(v, bestVersion) > 0) { best = String(tag).trim(); bestVersion = v; }
  }
  return best;
}

/** Render the release body. Sections are omitted when empty, and Breaking leads when present. */
export function renderNotes(tag, subjects) {
  const { breaking, features, fixes, other } = categorize(subjects);
  const lines = [`## Lyntai ${tag}`, ''];

  const section = (title, items, lead) => {
    if (!items.length) return;
    lines.push(`### ${title}`, '');
    if (lead) lines.push(lead, '');
    for (const item of items) lines.push(`- ${item}`);
    lines.push('');
  };

  section('Breaking changes', breaking, 'This release requires changes to consuming code. See `docs/migration-2.5-to-3.0.md` for the ordered upgrade path.');
  section('New features', features);
  section('Fixes', fixes);
  section('Other changes', other);

  if (!breaking.length && !features.length && !fixes.length && !other.length) {
    lines.push('See CHANGELOG.md for details.', '');
  }

  lines.push('---', '');
  lines.push('Install from NuGet: `dotnet add package Lyntai.Core` (+ the provider/storage packages you need). Full detail in CHANGELOG.md.');
  return lines.join('\n');
}

/** Subjects in `from..to`, merge commits excluded — a merge's subject describes the branch, not a change. */
export function subjectsInRange(from, to = 'HEAD', cwd = undefined) {
  const range = from ? `${from}..${to}` : to;
  const out = execFileSync('git', ['log', range, '--pretty=format:%s', '--no-merges'], { encoding: 'utf8', cwd });
  return out.split('\n').filter(Boolean);
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const arg = (name) => {
    const i = process.argv.indexOf(name);
    return i >= 0 ? process.argv[i + 1] : undefined;
  };

  const tag = arg('--tag');
  if (!tag) {
    console.error('release-notes: --tag <vX.Y.Z> is required');
    process.exitCode = 1;
  } else {
    const tags = execFileSync('git', ['tag'], { encoding: 'utf8' }).split('\n').filter(Boolean);
    const from = arg('--from') ?? previousTag(tags, tag);
    console.error(from ? `release-notes: ${from}..HEAD (current ${tag})` : 'release-notes: no previous release tag — using all commits');
    process.stdout.write(renderNotes(tag, subjectsInRange(from)) + '\n');
  }
}
