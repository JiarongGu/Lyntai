---
name: add-scorer
description: Use when adding an evaluation scorer to Lyntai's cortex layer (a new IScorer — deterministic or an LLM judge via LlmScorerBase — for the scoring/eval loop).
---

# Add a scorer to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add a scorer). This is the cheapest extension: a class +
one registration, no new package. Built-ins live in `Lyntai.Core/Cortex/Scorers/`.

## Checklist
- [ ] Pick the kind:
  - **Deterministic** → implement `IScorer` directly; compute in code; return `ScoreResult`, or `null`
    when the scorer doesn't apply to this `ScoreContext` (the service skips nulls).
  - **LLM judge** → extend `LlmScorerBase`; supply the criterion prompt. The base runs a one-shot judge
    through the front door and parses a clamped `{score,reason}` — a prose or out-of-range reply yields
    no score (dimension skipped), so don't re-parse yourself.
- [ ] Keep it generic — no domain assumptions baked into a library scorer.
- [ ] Register into the DI collection: `builder.AddScorer<MyScorer>()`. Never add an `if`/`switch` over
      scorer ids — `ScoringService` iterates `IEnumerable<IScorer>` and isolates a throwing scorer.
- [ ] Test it: deterministic scorers get a plain unit test; a judge scorer runs against the
      `provider-stub.mjs` `SCORING TASK` path for a deterministic verdict.
- [ ] `node devtools/dev.mjs verify` green.
