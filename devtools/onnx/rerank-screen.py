"""Does ONNX Runtime sidestep llama.cpp PR #21729 for a BERT cross-encoder?

A SCREEN, not a quality measurement. It scores the one pair `rerank-screen.mjs` calls REFERENCE — the pair
with a published score on `cross-encoder/ms-marco-MiniLM-L6-v2`'s own model card — through ONNX Runtime,
and compares against two knowns:

  published (the card)        [ 8.607138, -4.320078 ]   spread  12.93
  llama.cpp GGUF (Part 177)   ranks these BACKWARDS,    spread   0.015

The GGUF failure has a stated cause: conversion drops the pooling layers and the runtime hardcodes
token_type_ids to zero, so a BERT cross-encoder loses the segment signal telling query from document. ONNX
carries the graph as exported and takes token_type_ids as a real input, so the prediction is that it
reproduces the card. That prediction is what this refutes or confirms.

Needs a Python env with onnxruntime + huggingface_hub + tokenizers; this repository provisions none.
Run:  python devtools/onnx/rerank-screen.py
"""

import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from huggingface_hub import hf_hub_download
from tokenizers import Tokenizer

REPO = "cross-encoder/ms-marco-MiniLM-L6-v2"
QUERY = "How many people live in Berlin?"
DOCUMENTS = [
    "Berlin had a population of 3,520,031 registered inhabitants in an area of 891.82 square kilometers.",
    "Berlin is well known for its museums.",
]
PUBLISHED = [8.607138, -4.320078]
RELEVANT = 0
# rerank-screen.mjs's own threshold: below this a score has stopped being a logit.
COMPRESSED_SPREAD = 1.0

# fp32 first as the control, then the int8 variants — those are the ones that would be sub-100 MB, and
# quantisation is exactly where a head can degrade silently.
VARIANTS = ["onnx/model.onnx", "onnx/model_qint8_avx512_vnni.onnx", "onnx/model_quint8_avx2.onnx"]


def score(session: ort.InferenceSession, tokenizer: Tokenizer) -> list[float]:
    """One logit per document, batched as the real seam would."""
    encodings = [tokenizer.encode(QUERY, doc) for doc in DOCUMENTS]
    width = max(len(e.ids) for e in encodings)

    def pad(values, fill=0):
        return [list(v) + [fill] * (width - len(v)) for v in values]

    feed = {
        "input_ids": np.array(pad([e.ids for e in encodings]), dtype=np.int64),
        "attention_mask": np.array(pad([e.attention_mask for e in encodings]), dtype=np.int64),
        "token_type_ids": np.array(pad([e.type_ids for e in encodings]), dtype=np.int64),
    }
    wanted = {i.name for i in session.get_inputs()}
    missing = wanted - feed.keys()
    if missing:
        raise SystemExit(f"model wants inputs this probe does not supply: {sorted(missing)}")

    logits = session.run(None, {k: v for k, v in feed.items() if k in wanted})[0]
    return [float(row[0]) for row in logits]


def main() -> int:
    tokenizer = Tokenizer.from_file(hf_hub_download(REPO, "tokenizer.json"))

    published_spread = PUBLISHED[RELEVANT] - PUBLISHED[1 - RELEVANT]
    print(f"reference pair: {QUERY!r}")
    print(f"published      : {PUBLISHED}  spread {published_spread:.3f}  ({REPO} model card)")
    print("llama.cpp GGUF : ranks BACKWARDS, spread 0.015  (TASKS.md Part 177)")
    print()

    ok = True
    for variant in VARIANTS:
        try:
            path = hf_hub_download(REPO, variant)
        except Exception as error:  # a missing variant is a finding, not a crash
            print(f"{variant:34} DOWNLOAD FAILED  {type(error).__name__}")
            continue

        size = Path(path).stat().st_size
        scores = score(ort.InferenceSession(path, providers=["CPUExecutionProvider"]), tokenizer)
        spread = scores[RELEVANT] - scores[1 - RELEVANT]
        ordered = spread > 0
        compressed = abs(spread) < COMPRESSED_SPREAD
        verdict = "OK" if ordered and not compressed else ("BACKWARDS" if not ordered else "COMPRESSED")
        if verdict != "OK":
            ok = False

        print(f"{variant:34} {size:>11,} B")
        print(f"    scores  [{scores[0]:9.4f}, {scores[1]:9.4f}]   spread {spread:8.3f}"
              f"   ratio-to-published {published_spread / spread if spread else float('inf'):6.2f}x")
        print(f"    verdict {verdict}")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
