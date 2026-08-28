---
name: model-decoupling
applies_when: designing or building any feature that uses — or could use — a language model, an embedding model, or any AI service, including one you are about to specify with only model-free options
enforces: specify the feature without naming a model; select the provider by deployment; report which tier ran; list a model-assisted option among the candidates and price it on the corpus at hand
---

# The model is a deployment choice, not part of the feature

**Use models freely — and never let one into the definition of a feature.** Specify what the feature
does, take the provider through a seam, and let the **deployment** decide which model serves it.

This is not "avoid models". A feature may depend on one entirely. What it must not depend on is *which*.

## Why

**The right model differs by deployment, today.** The same capability running beside a developer's
session and running as a shared service has genuinely different constraints — one already has a model
in the room and cannot afford a network round trip, the other answers for throughput and cost. A single
hard-wired choice is wrong in at least one of them.

**And the models turn over faster than your project will.** What is best now will not be best in a
year. A feature welded to one ages at the speed of the fastest-moving part of the stack rather than its
own — and the parts of a codebase that encode hard-won judgement should turn over far more slowly than
the inference layer beneath them.

**The corollary is where the real bug lives.** Decoupled also means a feature must not become
*unavailable* because one provider is absent. The failure is specific and easy to miss: a feature built
model-first returns nothing without an endpoint, its author reports the missing endpoint as *blocked*,
and the blockage gets treated as an external constraint rather than as the design decision it was. The
dependency was never required by the problem — only by the implementation.

## How to apply

- **Write the feature's definition with no model in it.** If you cannot say what it does without naming
  one, the model has become part of the specification.
- **Select the provider at the composition root**, by deployment — not inside the feature, and not from
  a default that quietly assumes a particular runtime is installed.
- **Ask what the feature can still do when the provider is absent.** Usually more than expected: exact
  matching, structural comparison, counting, ranking by overlap. Ship that, and let the model raise the
  ceiling rather than being the floor.
- **Report which tier ran, on every result.** A reader who cannot tell why a category is empty will
  assume a bug and be right to. Disclosing only on an empty result is not enough — a caller *with*
  results reads them as complete.
- **A failure in the model half must not fail the whole.** Wrap it, carry the reason, continue with
  what works — and never swallow cancellation, which belongs to the caller.
- **Where a capability genuinely needs a model**, make its absence an explicit, informative message
  rather than silence or an error.
- **Give the model only what it is genuinely better at.** It is not better at exact comparison, at
  counting, or at anything with a deterministic answer — using it there is slower, costlier and less
  correct.

## A model-assisted option is a CANDIDATE in every design — list it, then measure it locally

**Everything above says *which* model is a deployment choice. This says something the rest of the document
does not imply: that a model belongs among the options being compared at all.** Enumerating three formulas
and never asking whether a small local model already answers the question better than any of them is an
incomplete design, not a conservative one.

The evidence is this repository's own. `docs/memory.md` §6 puts **not registering a judge at ~28 points of
miss** (0.5357 with the model-free floor against 0.2571 with a local 4B judge, on this repository's corpus) —
more than every ranking-policy decision in the subsystem combined, all of which move recall by hundredths. It
needed no new API to reach; the seam was already there and unpriced.

**And the second half is what keeps the first half honest: MEASURE the magnitude locally, never inherit it.**
An adopting application ran the same seam against its own real data and reported a gain **a fraction of the
corpus figure, at ~240× the latency per query** — and its own reading was that its probe
set could not resolve an effect smaller than one query moving. **Those are a consumer's reported numbers on a
corpus this repository does not have and cannot reproduce**, which is exactly why they are given as a
direction and a rough order rather than as figures: a document that ships should not lean on a measurement
its reader cannot open. A corpus figure, by contrast, is measured on a fixture built to be measured, so treat
`~28 points` as the optimistic end of a range rather than as the expected value.

Three rules follow, and they are cheap:

- **Put the model arm in the candidate table** beside the formulas, with its cost column filled in — latency,
  memory, and whether it needs a server the deployment does not already run.
- **Report its DIRECTION from someone else's measurement; never its MAGNITUDE.** Direction transfers, size
  does not. A number quoted from another corpus is a hypothesis, not a result — and quoting one you cannot
  re-run makes it a hypothesis your reader cannot check either.
- **Price it on the corpus at hand, and say what the prompt gave away.** A prompt that names the answer in an
  option label measures label-following, not reasoning — one such arm scored a perfect rate on a question a
  constant *prefer the newer regime* already answered, and only the prompt's own wording explained it.
