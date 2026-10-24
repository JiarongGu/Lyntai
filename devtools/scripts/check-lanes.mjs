// check-lanes — FAIL when `daoris.lanes.json` stops describing the tree: a file in no lane and not listed
// laneless, a file a lane owns that is ALSO listed laneless, a glob that matches nothing, or a map that does
// not parse into lanes at all.
//
// The map is what work is dispatched by (D214), so a path outside it is work no lane is told it owns, and a
// stale glob is a claim of ownership over nothing. Both are silent without a gate. What it holds and why:
// `docs/GATES.md` §check-lanes.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { repoFiles } from './_repo-files.mjs';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

export const LANES_FILE = 'daoris.lanes.json';
export const LANE_ID = /^[a-z][a-z0-9-]*$/;

// A glob as an anchored regex over a forward-slash path: `*` and `?` stay inside one segment, `**` crosses
// segments (`a/**` is everything under `a`, a leading `**/` is any depth), `{a,b}` is either branch. Line
// comments, because a block comment would end at the `*/` inside `**/`.
export function globToRegExp(glob) {
  let out = '';
  let depth = 0;
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*') {
      const before = i === 0 || glob[i - 1] === '/';
      const after = glob[i + 2] === '/';
      if (before && after) { out += '(?:.*/)?'; i += 2; }
      else { out += '.*'; i += 1; }
    } else if (c === '*') out += '[^/]*';
    else if (c === '?') out += '[^/]';
    else if (c === '{') { out += '(?:'; depth++; }
    else if (c === '}' && depth > 0) { out += ')'; depth--; }
    else if (c === ',' && depth > 0) out += '|';
    else out += c.replace(/[.+^$()|[\]\\{}]/g, '\\$&');
  }
  if (depth > 0) throw new Error(`unclosed "{" in glob "${glob}"`);
  return new RegExp(`^${out}$`);
}

/**
 * A glob's `{a,b}` alternations expanded into one glob per branch, so a misspelt BRANCH is caught as stale
 * rather than hidden by a sibling that matches. Innermost-first handles nesting.
 */
export function expandBraces(glob) {
  const m = /\{([^{}]*)\}/.exec(glob);
  if (!m) return [glob];
  return m[1].split(',').flatMap((b) => expandBraces(glob.slice(0, m.index) + b + glob.slice(m.index + m[0].length)));
}

/**
 * A path list as a predicate: each glob in order adds its matches, and a `!glob` removes its matches from
 * what the globs BEFORE it added. Also returns each positive glob's regex, for the stale-glob check.
 */
export function compileList(paths) {
  const steps = paths.map((p) => {
    const negate = p.startsWith('!');
    const glob = negate ? p.slice(1) : p;
    return { glob, negate, re: globToRegExp(glob) };
  });
  const matches = (file) => {
    let hit = false;
    for (const s of steps) if (s.re.test(file)) hit = !s.negate;
    return hit;
  };
  return { matches, positives: steps.filter((s) => !s.negate) };
}

/** Every structural problem in the parsed map, each named; an empty list means it can be classified. */
export function validateMap(map) {
  const problems = [];
  if (map === null || typeof map !== 'object' || Array.isArray(map)) return ['the map is not a JSON object'];
  if (!Array.isArray(map.lanes) || map.lanes.length === 0) return ['`lanes` is missing or empty'];

  const seen = new Set();
  const stewards = [];
  map.lanes.forEach((lane, i) => {
    const at = `lanes[${i}]${typeof lane?.id === 'string' ? ` (${lane.id})` : ''}`;
    if (lane === null || typeof lane !== 'object') { problems.push(`${at}: not an object`); return; }
    if (typeof lane.id !== 'string' || lane.id === '') problems.push(`${at}: missing id`);
    else if (!LANE_ID.test(lane.id)) problems.push(`${at}: id "${lane.id}" is not lower-case letters, digits and dashes starting with a letter`);
    else if (seen.has(lane.id)) problems.push(`${at}: id "${lane.id}" is repeated`);
    else seen.add(lane.id);
    if (typeof lane.title !== 'string' || lane.title.trim() === '') problems.push(`${at}: missing title`);
    if (typeof lane.summary !== 'string' || lane.summary.trim() === '') problems.push(`${at}: missing summary`);
    problems.push(...pathProblems(at, lane.paths));
    if ('gates' in lane) problems.push(`${at}: has \`gates\`, but daoris.gates.json declares none`);
    if (lane.steward === true) stewards.push(lane.id);
  });
  if (stewards.length > 1) problems.push(`more than one steward lane: ${stewards.join(', ')}`);

  if (map.laneless !== undefined && !Array.isArray(map.laneless)) problems.push('`laneless` is not a list');
  (Array.isArray(map.laneless) ? map.laneless : []).forEach((group, i) => {
    const at = `laneless[${i}]`;
    if (group === null || typeof group !== 'object') { problems.push(`${at}: not an object`); return; }
    if (typeof group.what !== 'string' || group.what.trim() === '') problems.push(`${at}: missing what`);
    problems.push(...pathProblems(at, group.paths));
  });
  return problems;
}

function pathProblems(at, paths) {
  if (!Array.isArray(paths) || paths.length === 0) return [`${at}: no paths`];
  const out = [];
  for (const p of paths) {
    if (typeof p !== 'string' || p === '' || p === '!') { out.push(`${at}: a path that is not a glob: ${JSON.stringify(p)}`); continue; }
    try { globToRegExp(p.replace(/^!/, '')); } catch (e) { out.push(`${at}: ${e.message}`); }
  }
  if (out.length === 0 && paths.every((p) => p.startsWith('!'))) out.push(`${at}: only carve-outs, nothing to carve from`);
  return out;
}

/**
 * The gate. `files` is a caller-chosen list (a test's); `null` = the tree every full-tree gate scans.
 * @returns {number} the exit code
 */
export function checkLanes(repo, log = console.log, files = null) {
  const label = 'check-lanes';
  let map;
  try {
    map = JSON.parse(fs.readFileSync(path.join(repo, LANES_FILE), 'utf8'));
  } catch (e) {
    log(`${label}: ✗ ${LANES_FILE} could not be read as JSON — ${e.message}`);
    return 1;
  }
  const problems = validateMap(map);
  if (problems.length > 0) {
    log(`${label}: ✗ ${LANES_FILE} is malformed — ${problems.length} problem(s)`);
    for (const p of problems) log(`  ${p}`);
    return 1;
  }

  const tree = (files ?? repoFiles(repo)).map((f) => f.replace(/\\/g, '/'));
  if (tree.length === 0) {
    log(`${label}: ✗ found no files to classify — this gate proves nothing; check the repo root and the listing`);
    return 1;
  }

  // The steward's lane first, then file order: a path belongs to the first lane that owns it.
  const lanes = [...map.lanes.filter((l) => l.steward === true), ...map.lanes.filter((l) => l.steward !== true)]
    .map((l) => ({ id: l.id, ...compileList(l.paths), owned: 0, at: `lane ${l.id}` }));
  const groups = (map.laneless ?? [])
    .map((g, i) => ({ ...compileList(g.paths), at: `laneless[${i}]` }));

  const unplaced = [];
  const both = [];
  let laneless = 0;
  for (const f of tree) {
    const owner = lanes.find((l) => l.matches(f));
    const group = groups.find((g) => g.matches(f));
    if (owner && group) both.push(`${f} — owned by ${owner.at} and listed in ${group.at}`);
    else if (owner) owner.owned++;
    else if (group) laneless++;
    else unplaced.push(f);
  }

  const stale = [];
  for (const list of [...lanes, ...groups])
    for (const { glob } of list.positives)
      for (const branch of expandBraces(glob)) {
        const re = globToRegExp(branch);
        if (!tree.some((f) => re.test(f)))
          stale.push(`${list.at}: "${glob}"${branch === glob ? '' : ` (its branch "${branch}")`} matches no file`);
      }

  if (unplaced.length + both.length + stale.length === 0) {
    log(`${label}: ${lanes.length} lanes over ${tree.length} files — `
      + `${lanes.map((l) => `${l.id} ${l.owned}`).join(' · ')} · laneless ${laneless} ✓`);
    return 0;
  }

  const section = (title, items) => {
    if (items.length === 0) return;
    log(`  ${title} (${items.length}):`);
    for (const x of items.slice(0, 30)) log(`    ${x}`);
    if (items.length > 30) log(`    …and ${items.length - 30} more`);
  };
  log(`${label}: ✗ ${LANES_FILE} no longer describes the tree`);
  section('in no lane and not listed laneless — place it in the lane that owns it, or list it laneless with the group whose reason fits', unplaced);
  section('owned by a lane AND listed laneless — a path is one or the other', both);
  section('a glob that matches nothing — stale or misspelt', stale);
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = checkLanes(repo);
}
