// _markers — the shared half of every index GENERATED from markers a person authored.
//
// Four gates derive a block of prose from authored markers: `check-backlog`'s open-item roster (D111),
// `check-pitfalls`' facet index, `check-measurements`' results index and `check-dev-loop`'s command table.
// What they share is the SUBTLE half — a second hand-written copy would drift silently in the PERMISSIVE
// direction, and every piece here is a defect this repository has already paid for once (`scanMarkers`
// owns the fence, block-skip and broken-marker rules; `regenerate` the anchors-fixed-point-write step):
//
//   1. `parseAttributes` returns the RESIDUE as well as the matches. `matchAll` reports what it recognised
//      and says nothing about what it skipped, so `needs=a real key` — quotes omitted — parsed as
//      `needs="a"`, satisfied every rule its caller checked, and published a one-word blocker that read
//      like a complete one. A caller fails on a non-blank residue. See `.claude/knowledge/pitfalls.md`
//      §Environment / tooling, "A parser that SCRAPES".
//   2. `fixedPoint` iterates. A generated row can carry the LINE NUMBER of something below the block, so
//      writing the block moves what it just published. Row COUNT does not depend on line numbers, so the
//      block's height is constant after the first pass and this converges in three.
//   3. `carriedEscapes` moves a line's own `link-ok`/`count-ok`/`drift-ok` onto the row generated FROM it.
//      A row reproduces its source's text, so an annotated path arrives unannotated one line-number away
//      and the sibling gate fires on the copy. Carrying the source's own escape expires when the source's
//      does, where a blanket annotation on every row would be an exclusion nobody could see rot.

/** `key=value` / `key="value with spaces"`. Global, so callers must not share one across a loop body. */
export const ATTR = /([A-Za-z_][\w-]*)=(?:"([^"]*)"|([^\s"]+))/g;

/** A marker of a named kind: `markerPattern('item')` matches `<!-- item: … -->`. */
export const markerPattern = (name) => new RegExp(`<!--\\s*${name}:\\s*([^>]*?)\\s*-->`);

/**
 * `{ attrs, residue }` for a marker body.
 *
 * The residue is every character no match consumed, trimmed. A caller MUST fail on a non-blank one: it is
 * the only signal that a value was silently truncated, and it is not an unknown attribute (it holds no
 * `=`), so nothing else can see it.
 */
export function parseAttributes(body) {
  const attrs = new Map();
  let cursor = 0;
  let residue = '';
  for (const m of body.matchAll(ATTR)) {
    attrs.set(m[1], m[2] ?? m[3]);
    residue += body.slice(cursor, m.index);
    cursor = m.index + m[0].length;
  }
  return { attrs, residue: `${residue}${body.slice(cursor)}`.trim() };
}

/** The escape tokens this repository keeps deliberately separate, so one cannot silence two gates. */
export const ESCAPES = ['link-ok', 'count-ok', 'drift-ok'];

export const carriedEscapes = (raw) => ESCAPES.filter((e) => raw.includes(e));

/** The escapes as trailing comments, for the row generated from a line that carried them. */
export const escapeComments = (escapes, noun) =>
  escapes.map((e) => ` <!-- ${e}: carried from this ${noun}'s own line -->`).join('');

/** A table cell: pipes escaped, whitespace flattened, long text cut. The line number is the real pointer. */
export function cell(text, max = 76) {
  const flat = String(text ?? '').replace(/\s+/g, ' ').trim().replace(/\|/g, '\\|');
  return flat.length > max ? `${flat.slice(0, max - 1)}…` : flat;
}

/**
 * The anchors' positions, or `null` when either is missing — a STRUCTURE problem, never a stale block.
 *
 * The begin anchor is matched by prefix so it can carry an explanatory tail; the end anchor is exact, so a
 * document quoting the begin form in prose cannot accidentally close a block it never opened.
 */
export function blockRange(lines, begin, end) {
  const start = lines.findIndex((l) => l.trimStart().startsWith(begin));
  if (start < 0) return null;
  const stop = lines.findIndex((l, i) => i > start && l.trim() === end);
  return stop < 0 ? null : { start, end: stop };
}

/**
 * Problems with the ANCHOR PAIR itself, as message strings — empty when it is a clean single pair.
 *
 * `blockRange` takes the FIRST begin and the first end after it, so a duplicate anchor silently moves the
 * block and a write splices over whatever sits between them. Measured 2026-09-10 on the real traps record:
 * a second begin anchor five lines above the real one made `--write` DELETE the document's intro paragraph
 * and exit 0, reporting nothing. Uniqueness is therefore a precondition, not an assumption.
 *
 * A caller must run this BEFORE writing. A document may still name an anchor in prose — just not at the
 * start of a line, which is the same rule that keeps a quoted end anchor from closing a block early.
 */
export function anchorProblems(lines, begin, end) {
  const begins = lines.filter((l) => l.trimStart().startsWith(begin)).length;
  const ends = lines.filter((l) => l.trim() === end).length;
  const problems = [];
  if (begins > 1)
    problems.push(`${begins} \`${begin}\` anchors — the block is delimited by the FIRST, so a second one `
      + 'silently moves it and a write splices over everything between them');
  if (ends > 1) problems.push(`${ends} \`${end}\` anchors — exactly one may close the generated block`);
  if (begins === 1 && ends === 0) problems.push(`\`${begin}\` is never closed by \`${end}\``);
  if (begins === 0 && ends > 0) problems.push(`\`${end}\` appears with no \`${begin}\` above it`);
  return problems;
}

const FENCE = /^\s*```/;

/**
 * The lines of a marker-bearing record a parser may read, each with its marker of kind `name` — the three
 * rules every such parser needs, owned once:
 *
 *   - the GENERATED block is skipped, located by `blockRange` (the splicer's own function, so the two can
 *     never disagree about where it is) — its rows would otherwise be read back as source;
 *   - a fenced code block is skipped, and a fence that never CLOSES is a problem, because a toggle nothing
 *     asserts balanced silently drops everything below it;
 *   - a marker that OPENS (`<!-- name:`) but does not match is `broken` and reported — a value holding `>`
 *     matches nothing, and calling that "no marker" sends the author hunting for one that is there.
 *
 * @returns {{ visible: { i: number, raw: string, marker: RegExpExecArray | null, broken: boolean }[],
 *   problems: { line: number, why: string }[] }}
 */
export function scanMarkers(lines, { name, begin = null, end = null, noun = 'entry' }) {
  const pattern = markerPattern(name);
  const opener = new RegExp(`<!--\\s*${name}:`);
  const block = begin ? blockRange(lines, begin, end) : null;
  const visible = [];
  const problems = [];
  let fenceOpenedAt = 0;
  lines.forEach((raw, i) => {
    if (block && i >= block.start && i <= block.end) return;
    if (FENCE.test(raw)) { fenceOpenedAt = fenceOpenedAt ? 0 : i + 1; return; }
    if (fenceOpenedAt) return;
    const marker = pattern.exec(raw);
    const broken = !marker && opener.test(raw);
    if (broken)
      problems.push({ line: i + 1, why: `this \`${name}:\` marker contains \`>\` and therefore matches NOTHING — `
        + 'reword the value; `>` is what would let a marker run past its own `-->`' });
    visible.push({ i, raw, marker, broken });
  });
  if (fenceOpenedAt)
    problems.push({ line: fenceOpenedAt, why: `a code fence opened here is never closed, so every ${noun} below `
      + 'it is invisible to this gate — the index would be written over a truncated read' });
  return { visible, problems };
}

/**
 * Regenerate a record's block to its fixed point: `'current'`, `'written'` (after `write(next)`),
 * `'stale'` when a write was not asked for, or `'missing'` when the anchors are not both there.
 */
export function regenerate(text, render, begin, end, write = null) {
  const normalized = text.split(/\r?\n/).join('\n');
  const next = fixedPoint(normalized, render, begin, end);
  if (next === null) return 'missing';
  if (next === normalized) return 'current';
  if (!write) return 'stale';
  write(next);
  return 'written';
}

/** The text with `body` spliced between the anchors, or `null` when they are not both there. */
export function spliceBlock(text, body, begin, end) {
  const lines = text.split(/\r?\n/);
  const range = blockRange(lines, begin, end);
  if (!range) return null;
  return [...lines.slice(0, range.start + 1), '', ...body, '', ...lines.slice(range.end)].join('\n');
}

/**
 * The text with its block regenerated until it stops moving, `null` if the anchors are missing.
 *
 * `render` takes the CURRENT text and returns the block's body lines, so a renderer that publishes line
 * numbers sees the positions its own last pass produced. Line endings are normalised to LF on the way
 * through, which is what `.gitattributes` declares (D95) — a caller comparing for staleness should compare
 * against the normalised text so an EOL problem is never reported as a stale block.
 */
export function fixedPoint(text, render, begin, end, maxRounds = 6) {
  let cur = text.split(/\r?\n/).join('\n');
  for (let i = 0; i < maxRounds; i++) {
    const next = spliceBlock(cur, render(cur), begin, end);
    if (next === null || next === cur) return next;
    cur = next;
  }
  return null;
}
