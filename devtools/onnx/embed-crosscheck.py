"""Reference cross-check for OnnxEmbedder: does the C# pipeline agree with Python's?

The C# live test proves the vectors are PLAUSIBLE — right width, unit length, related pair ranks first.
None of that catches a wrong pooling mode, an attention mask that includes padding, or a token_type_ids
tensor the graph silently ignores: all of those still produce finite, well-ordered vectors. This runs the
same export through onnxruntime + the reference HF tokenizer and prints cosines the C# side can be pinned
against, which is the check that has somewhere to fail.

Needs a Python env with onnxruntime + tokenizers; this repository provisions none.
Run:  python devtools/onnx/embed-crosscheck.py <model-dir>
"""

import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from tokenizers import BertWordPieceTokenizer

SENTENCES = [
    "the weather forecast for tomorrow",
    "a stock market share price quote",
    "tomorrow's weather forecast",
]


def main(directory: Path) -> int:
    # do_lower_case: true and strip_accents: null (= follow lowercasing) are what this model declares,
    # which is what the C# side reads out of tokenizer_config.json rather than assuming.
    tokenizer = BertWordPieceTokenizer(
        str(directory / "vocab.txt"), lowercase=True, strip_accents=True)
    encodings = [tokenizer.encode(s) for s in SENTENCES]
    width = max(len(e.ids) for e in encodings)

    def pad(values):
        return np.array([list(v) + [0] * (width - len(v)) for v in values], dtype=np.int64)

    session = ort.InferenceSession(
        str(directory / "onnx" / "model.onnx"), providers=["CPUExecutionProvider"])
    feed = {
        "input_ids": pad([e.ids for e in encodings]),
        "attention_mask": pad([e.attention_mask for e in encodings]),
        "token_type_ids": pad([e.type_ids for e in encodings]),
    }
    wanted = {i.name for i in session.get_inputs()}
    hidden = session.run(None, {k: v for k, v in feed.items() if k in wanted})[0]

    mask = feed["attention_mask"][..., None].astype(np.float32)
    pooled = (hidden * mask).sum(axis=1) / np.clip(mask.sum(axis=1), 1e-9, None)   # MEAN over attended
    pooled /= np.linalg.norm(pooled, axis=1, keepdims=True)                        # Normalize module

    def cosine(a, b):
        return float(np.dot(pooled[a], pooled[b]))

    print(f"width            {pooled.shape[1]}")
    print(f"unit length      {float(np.linalg.norm(pooled[0])):.6f}")
    print(f"cosine related   {cosine(0, 2):.6f}   ('{SENTENCES[0]}' vs '{SENTENCES[2]}')")
    print(f"cosine unrelated {cosine(0, 1):.6f}   ('{SENTENCES[0]}' vs '{SENTENCES[1]}')")
    print(f"first 6 of v0    {', '.join(f'{v:.6f}' for v in pooled[0][:6])}")
    return 0


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1])))
