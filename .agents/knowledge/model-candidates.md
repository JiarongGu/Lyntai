---
name: model-candidates
applies_when: designing or building any feature that uses — or could use — a language model, an embedding model, or any AI service, including one you are about to specify with only model-free options
enforces: list a model-assisted option among the candidates and price it on the corpus at hand; bound the input SHAPE before buying a bigger model
---

# A model-assisted option is a CANDIDATE in every design — list it, then measure it locally

**The shared `model-decoupling.md` says WHICH model is a deployment choice. This says a model belongs among
the options being compared at all, and that its magnitude is measured here, never inherited.**

## Why

Enumerating three formulas and never asking whether a small local model already answers the question
better than any of them is an incomplete design, not a conservative one.

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

## How to apply

- Follow the three rules above, in the candidate table of the design you are writing.
- **And which SHAPE you hand it decides more than which model does** — a generative task takes a
  budget and a selective task over a visible list does not, so bound the input before buying a bigger
  model. The inventory of every shape this library performs, and what is measured about each, is
  `docs/model-tasks.md`.
