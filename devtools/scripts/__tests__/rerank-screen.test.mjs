// rerank-screen's own tests.
//
// The harness answers "does this GGUF work as a reranker at all". These tests drive the pure half: the
// two assertions llama.cpp #16407 calls for, the ARCHITECTURE-AWARE head check (whose whole reason for
// existing is a false positive this repository actually made), and the GGUF header reader.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  FIXTURE, REFERENCE, COMPRESSED_SPREAD, COMPRESSED_PROBABILITY_SPREAD, OVERLAP_TRAPS, longProbe,
  parseRerankRows, byDocumentOrder, evaluateOrdering, evaluateReference, evaluateTrap, maxDrift, headVerdict,
  HEAD_TENSORS, readGgufHeader, parseArgs, DEFAULT_PORT,
} from '../rerank-screen.mjs';

const rows = (...scores) => scores.map((score, index) => ({ index, score }));

describe('parseRerankRows', () => {
  it('accepts both the Cohere and the bare score spellings', () => {
    assert.deepEqual(parseRerankRows({ results: [{ index: 0, relevance_score: 1.5 }] }), [{ index: 0, score: 1.5 }]);
    assert.deepEqual(parseRerankRows({ results: [{ index: 0, score: 1.5 }] }), [{ index: 0, score: 1.5 }]);
  });

  it('returns NULL rather than a fabricated ordering when the shape is unusable', () => {
    // A fabricated ordering is indistinguishable from a real one once it reaches a table, which is the
    // whole reason this returns null instead of falling back to input order.
    assert.equal(parseRerankRows({}), null, 'no results member');
    assert.equal(parseRerankRows({ results: [] }), null, 'empty results');
    assert.equal(parseRerankRows({ results: [{ index: 0 }] }), null, 'no score of either spelling');
    assert.equal(parseRerankRows({ results: [{ score: 1 }] }), null, 'no index');
    assert.equal(parseRerankRows({ results: [{ index: 0, score: NaN }] }), null, 'NaN is not a score');
    assert.equal(parseRerankRows({ results: [{ index: 0, score: Infinity }] }), null, 'Infinity is not a score');
  });
});

describe('evaluateOrdering — the two assertions #16407 calls for', () => {
  it('passes a discriminating scorer that ranks the answer first and the unrelated doc last', () => {
    const v = evaluateOrdering(rows(7.2, -7.3, -10.8, -14.4));
    assert.equal(v.allDistinct, true);
    assert.equal(v.answerFirst, true);
    assert.equal(v.unrelatedLast, true);
  });

  it('FAILS a flat scorer, which is the failure that reads as a clean null result', () => {
    const v = evaluateOrdering(rows(0.5, 0.5, 0.5, 0.5));
    assert.equal(v.allDistinct, false, 'a model returning one value for every candidate reorders nothing');
    assert.equal(v.distinct, 1);
  });

  it('FAILS a shuffled scorer even though its scores are perfectly distinct', () => {
    // The positive control for the ordering half: distinctness alone cannot catch a head that loads but
    // computes the wrong thing, so the two assertions are genuinely independent.
    const v = evaluateOrdering(rows(-14.4, -10.8, -7.3, 7.2));
    assert.equal(v.allDistinct, true);
    assert.equal(v.answerFirst, false);
    assert.equal(v.unrelatedLast, false);
  });

  it('counts distinctness at 1e-6, far below the ~9e-3 drift a real server shows between calls', () => {
    // Repeat-call noise must never manufacture a "distinct" verdict on a flat scorer.
    assert.equal(evaluateOrdering(rows(0.5, 0.5 + 1e-9, 0.5, 0.5)).distinct, 1);
    assert.equal(evaluateOrdering(rows(0.5, 0.5 + 1e-3, 0.5 + 2e-3, 0.5 + 3e-3)).distinct, 4);
  });

  it('reports the top-two gap, which is what bounds whether the fixture could see a near-tie flip', () => {
    assert.equal(evaluateOrdering(rows(7.2, -7.3, -10.8, -14.4)).topTwoGap.toFixed(1), '14.5');
  });
});

describe('maxDrift', () => {
  it('measures the largest per-document move between two responses', () => {
    assert.equal(maxDrift([1, 2, 3], [1, 2.5, 3]), 0.5);
  });

  it('is NaN — never 0 — when the two responses do not describe the same documents', () => {
    // Returning 0 here would report perfect agreement for two incomparable answers, which is the
    // fail-open direction: a broken call would read as the most stable possible result.
    assert.ok(Number.isNaN(maxDrift([1, 2], [1])));
    assert.ok(Number.isNaN(maxDrift([], [])));
    assert.ok(Number.isNaN(maxDrift([1], null)));
  });
});

describe('byDocumentOrder', () => {
  it('normalises away the server\'s chosen sort, so two responses are comparable', () => {
    const bestFirst = [{ index: 2, score: 9 }, { index: 0, score: 1 }, { index: 1, score: 5 }];
    assert.deepEqual(byDocumentOrder(bestFirst), [1, 5, 9]);
  });
});

describe('headVerdict — architecture-aware, because a name-only check condemns working models', () => {
  it('confirms a BERT head', () => {
    assert.equal(headVerdict('bert', ['blk.0.attn_q.weight', 'cls.output.weight']).state, 'present');
  });

  it('confirms a jina-bert-v2 head, which is named DIFFERENTLY', () => {
    // The measured false positive: all three independent conversions of jina-reranker-v1-tiny-en lack
    // `cls.output.weight` and the model screens 8/8 when served. `cls.weight` is its head.
    const names = ['blk.0.attn_q.weight', 'cls.weight', 'cls.bias'];
    assert.equal(headVerdict('jina-bert-v2', names).state, 'present');
    assert.equal(headVerdict('bert', names).state, 'missing',
      'the SAME tensor list is a missing head under bert — which is exactly how the false positive arose');
  });

  it('says UNKNOWN for an unlisted architecture rather than condemning it', () => {
    const v = headVerdict('some-arch-nobody-added', ['cls.weight']);
    assert.equal(v.state, 'unknown');
    assert.equal(v.wanted, null, 'unknown is a statement about HEAD_TENSORS, never about the file');
  });

  it('catches the real defect: a conversion carrying classifier.* under a bert arch', () => {
    // The positive control — without this the check could pass by never returning `missing`.
    assert.equal(headVerdict('bert', ['classifier.weight', 'classifier.bias']).state, 'missing');
  });

  it('has an entry for every architecture it claims to cover, spelled as GGUF spells it', () => {
    for (const arch of Object.keys(HEAD_TENSORS)) {
      assert.match(arch, /^[a-z0-9-]+$/, `${arch} is not a GGUF general.architecture spelling`);
      assert.ok(HEAD_TENSORS[arch].length > 0);
    }
  });
});

describe('readGgufHeader', () => {
  /** Minimal well-formed GGUF prefix: magic, version, counts, one string KV, one tensor entry. */
  function buildGguf({ arch = 'bert', tensors = ['cls.output.weight'] } = {}) {
    const parts = [];
    const u32 = (v) => { const b = Buffer.alloc(4); b.writeUInt32LE(v); return b; };
    const u64 = (v) => { const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(v)); return b; };
    const str = (s) => Buffer.concat([u64(Buffer.byteLength(s)), Buffer.from(s, 'utf8')]);
    parts.push(Buffer.from('GGUF', 'ascii'), u32(3), u64(tensors.length), u64(1));
    parts.push(str('general.architecture'), u32(8), str(arch));      // 8 = STRING
    for (const t of tensors) parts.push(str(t), u32(1), u64(4), u32(0), u64(0));
    return Buffer.concat(parts);
  }

  it('reads the architecture and the tensor names', () => {
    const h = readGgufHeader(buildGguf({ arch: 'jina-bert-v2', tensors: ['cls.weight', 'blk.0.attn_q.weight'] }));
    assert.equal(h.version, 3);
    assert.equal(h.tensorCount, 2);
    assert.equal(h.meta['general.architecture'], 'jina-bert-v2');
    assert.deepEqual(h.names, ['cls.weight', 'blk.0.attn_q.weight']);
  });

  it('throws SHORT — never "0 tensors" — when the buffer stops mid-header', () => {
    // The distinction is load-bearing: a truncated read reporting an empty tensor list is
    // indistinguishable from a headless conversion, so the caller must be told to fetch more.
    const full = buildGguf({ tensors: ['cls.output.weight', 'blk.0.attn_q.weight'] });
    assert.throws(() => readGgufHeader(full.subarray(0, full.length - 12)), /SHORT/);
  });

  it('rejects a non-GGUF buffer by magic rather than misparsing it', () => {
    assert.throws(() => readGgufHeader(Buffer.from('NOPE' + '\0'.repeat(40))), /not a GGUF/);
  });
});

describe('parseArgs', () => {
  it('defaults to the registry port — _llama-harness.test holds it disjoint', () => {
    assert.equal(parseArgs([]).port, DEFAULT_PORT);
  });

  it('collects --inspect urls and ignores later flags', () => {
    assert.deepEqual(parseArgs(['--inspect', 'https://a/x.gguf', 'https://b/y.gguf', '--port', '9']).inspect,
      ['https://a/x.gguf', 'https://b/y.gguf']);
  });

  it('reads --model and --label', () => {
    const a = parseArgs(['--model', 'C:/m.gguf', '--label', 'tiny']);
    assert.equal(a.model, 'C:/m.gguf');
    assert.equal(a.label, 'tiny');
  });
});

describe('evaluateReference — the check FIXTURE was too easy to make', () => {
  const ref = (a, b) => [{ index: 0, score: a }, { index: 1, score: b }];

  it('passes a healthy cross-encoder: right order, logit-scaled spread', () => {
    // LAMAR-600m, measured: 6.1589 / -7.7068 against the card's 8.607 / -4.320.
    const v = evaluateReference(ref(6.1589, -7.7068));
    assert.equal(v.ordered, true);
    assert.equal(v.compressed, false);
    assert.ok(v.ratio > 0.5 && v.ratio < 2, `ratio ${v.ratio} should be near 1 for a healthy model`);
  });

  it('FAILS the GGUF that inverts the pair — which the easy fixture passed', () => {
    // ms-marco-MiniLM-L6-v2 Q8_0, measured: -0.0927 / -0.0778. It ranks "Berlin is well known for its
    // museums" ABOVE the population figure. This is the case the whole reference pair exists for.
    const v = evaluateReference(ref(-0.09269176, -0.07776129));
    assert.equal(v.ordered, false, 'the answering passage must not score below the merely on-topic one');
    assert.equal(v.compressed, true);
  });

  it('flags a COLLAPSED spread even when the ordering survives', () => {
    // jina-reranker-v1-tiny-en Q4_K_M, measured: 0.1228 / 0.0290. Order right, spread 137.8x too small —
    // degraded, not broken, and the two must not report the same verdict.
    const v = evaluateReference(ref(0.12275171, 0.02895314));
    assert.equal(v.ordered, true);
    assert.equal(v.compressed, true, 'a 0.094 spread is not a logit separation');
    assert.ok(v.ratio > 100);
  });

  it('reads the relevant document from the fixture rather than assuming index 0', () => {
    const flipped = { ...REFERENCE, relevant: 1 };
    assert.equal(evaluateReference(ref(5, -5), flipped).ordered, false);
    assert.equal(evaluateReference(ref(-5, 5), flipped).ordered, true);
  });

  it('keeps the published pair and its source together, so the magnitude is attributable', () => {
    assert.equal(REFERENCE.published.length, 2);
    assert.ok(REFERENCE.published[0] > 0 && REFERENCE.published[1] < 0);
    assert.match(REFERENCE.source, /model card/);
    assert.ok(COMPRESSED_SPREAD > 0 && COMPRESSED_SPREAD < 12.9272,
      'the threshold must sit below the published spread or it can never pass');
  });

  it('uses two ON-TOPIC documents, which is what makes it harder than FIXTURE', () => {
    // Both mention Berlin, so lexical overlap cannot separate them and a degraded head cannot coast.
    assert.equal(REFERENCE.documents.length, 2);
    for (const d of REFERENCE.documents) assert.match(d, /Berlin/);
  });

  it('reads a PROBABILITY-scaled reranker as separated rather than collapsed', () => {
    // Qwen3-Reranker-0.6B Q6_K, measured by an adopting application: 0.998495 / 0.001490. A yes/no
    // probability, not a logit — a spread of 0.997 is the most separation that scale can show.
    const v = evaluateReference(ref(0.998495, 0.00149));
    assert.equal(v.scale, 'probability');
    assert.equal(v.ordered, true);
    assert.equal(v.compressed, false);
    assert.equal(v.ratio, null, 'a logit card\'s spread says nothing about a probability');
  });

  it('keeps collapsed logits COLLAPSED even though they happen to fall inside [0, 1]', () => {
    // The jina pair above sits in [0, 1] too; reading every such pair as probabilities would pass the
    // very defect the spread check exists for. A probability decision straddles one half; this does not.
    const v = evaluateReference(ref(0.12275171, 0.02895314));
    assert.equal(v.scale, 'logit');
    assert.equal(v.compressed, true);
  });

  it('flags an UNDECIDED probability pair, which separates no better than a collapsed logit', () => {
    const v = evaluateReference(ref(0.6, 0.4));
    assert.equal(v.scale, 'probability');
    assert.equal(v.compressed, true);
    assert.ok(COMPRESSED_PROBABILITY_SPREAD > 0.2 && COMPRESSED_PROBABILITY_SPREAD < 0.997);
  });
});

describe('OVERLAP_TRAPS — the pair a model ranking by word overlap cannot pass', () => {
  const words = (s) => new Set(s.toLowerCase().match(/[\p{Script=Han}]|[\p{L}\p{N}]+/gu) ?? []);
  const shared = (query, doc) => [...words(query)].filter((w) => words(doc).has(w)).length;

  it('holds its premise: the distractor shares MORE of the query than the answer does', () => {
    // An adopting application found a reranker that passes REFERENCE and FAILS this shape; the check is
    // worthless the moment the answer happens to echo the query more than the distractor does.
    for (const t of OVERLAP_TRAPS) {
      const answer = t.documents[t.answer], distractor = t.documents[1 - t.answer];
      assert.ok(shared(t.query, distractor) > shared(t.query, answer),
        `${t.lang}: distractor ${shared(t.query, distractor)} vs answer ${shared(t.query, answer)}`);
    }
  });

  it('covers a language that tokenizes without spaces', () => {
    assert.ok(OVERLAP_TRAPS.some((t) => /\p{Script=Han}/u.test(t.query)));
    assert.ok(OVERLAP_TRAPS.some((t) => /^[\x20-\x7e]+$/.test(t.query)));
  });

  it('passes a scorer that ranks the answer first and fails one that ranks the echo first', () => {
    const [t] = OVERLAP_TRAPS;
    const scored = (answer, distractor) => (t.answer === 0 ? rows(answer, distractor) : rows(distractor, answer));
    assert.equal(evaluateTrap(scored(3.1, -2.0), t).ordered, true);
    // the adopter's capture from the failing model: distractor 10.019 against answer 7.420
    assert.equal(evaluateTrap(scored(7.419926, 10.018924), t).ordered, false);
  });
});

describe('the fixture itself', () => {
  it('names an expected best and worst that are actually in the document list', () => {
    assert.ok(FIXTURE.documents[FIXTURE.expectedBest]);
    assert.ok(FIXTURE.documents[FIXTURE.expectedWorst]);
    assert.notEqual(FIXTURE.expectedBest, FIXTURE.expectedWorst);
  });

  it('includes distractors that SHARE vocabulary with the query', () => {
    // A fixture of one answer plus unrelated noise is passed by a model reduced to mean-pooled cosine,
    // so the overlapping distractors are what make the screen able to fail.
    const overlapping = FIXTURE.documents.filter((d, i) => i !== FIXTURE.expectedBest && /Apollo|Moon/.test(d));
    assert.ok(overlapping.length >= 2, 'need at least two lexically-overlapping distractors');
  });

  it('emits a long probe past the 512-token ceiling that disqualifies a BERT reranker', () => {
    // ~6,000 characters is what the memory benches can emit; a 512-token model 400s here and passes
    // every shorter fixture, so the probe has to be this long to be worth running.
    assert.ok(longProbe().length > 5000, `probe is only ${longProbe().length} chars`);
  });
});
