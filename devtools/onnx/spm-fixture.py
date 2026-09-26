"""Generates the SentencePiece fixture SentencePieceTokenizerTests runs against, and its golden answers.

A tokenizer that disagrees with the reference by one rule returns finite, plausible, WRONG vectors, so the C#
tokenizer is pinned id for id against two independent references: HF `tokenizers` (the reference for the
tokenizer.json format it reads) and C++ `sentencepiece` (what the models were built with).

The fixture is a tiny Unigram model trained here on a small multilingual corpus with CUSTOM normalization
rules, so its precompiled charsmap is a real Darts double array small enough to commit. It is written as an
XLM-R-shaped tokenizer.json — fairseq specials (<s>=0 <pad>=1 </s>=2 <unk>=3, every piece shifted by one,
<mask> last) and the TemplateProcessing every target export declares.

Each input is classified by whether the two references agree:
  cases           both agree — the golden ids
  hf_only         they disagree, for a reason EXPECTED_HF_ONLY states, and the golden is HF's
  typed_specials  "<s>" typed in text: HF parses it as a control token, C++ keeps it text; the golden is C++'s
Any other disagreement stops the run: understand it before classifying it.

Needs a Python env with `sentencepiece` and `tokenizers`; this repository provisions none.
Run:  python devtools/onnx/spm-fixture.py
      python devtools/onnx/spm-fixture.py --real <dir holding an XLM-R tokenizer.json + sentencepiece.bpe.model>
"""

import base64
import json
import sys
from pathlib import Path

import sentencepiece as spm
from tokenizers import Tokenizer

REPO = Path(__file__).resolve().parents[2]
FIXTURES = REPO / "tests" / "Lyntai.Tests" / "Text" / "Fixtures"
SCRATCH = REPO / "devtools" / "_spm-fixture"
META = "▁"

CORPUS = [
    "The quick brown fox jumps over the lazy dog.",
    "A river runs north through the old city, past the market and the bridge.",
    "She said: <read the note> and then </stop> before the end.",
    "Numbers like 1, 2, 3 and 42 appear in lists; so do 3.14 and 1000.",
    "Tomorrow's weather forecast: rain in the morning, sun by the afternoon.",
    "Der schnelle braune Fuchs springt über den faulen Hund.",
    "Die Straße führt zum Bahnhof, und das Wetter ist heute schön.",
    "Le café est très bon, mais la crème brûlée est meilleure.",
    "Où est la bibliothèque? Elle est à côté de l'école.",
    "Привет, мир! Как дела сегодня?",
    "Москва и Санкт-Петербург — большие города.",
    "مرحبا بالعالم، كيف حالك اليوم؟",
    "नमस्ते दुनिया, आज मौसम अच्छा है।",
    "今天天气很好。我们去公园散步吧！",
    "北京是中国的首都，上海是最大的城市。",
    "東京タワーに行きました。とても高かったです。",
    "ひらがなとカタカナと漢字をまぜて書きます。",
    "한국어 문장입니다. 서울은 큰 도시입니다.",
    "Emoji: 👍 👍🏽 👨‍👩‍👧 🎉 and a heart ❤️ in text.",
    "Rule outputs: A B 1 fi TM カ é appear as themselves.",
    "résumé naïve façade coöperate élève",
    "The file path is /usr/local/bin and the URL is https://example.com/a/b.",
    "Programming: if (x > 0) { return y; } else { return z; }",
    "Mixed script: Berlin 柏林 Берлин برلين",
    "Short words: a an the of to in on at by for with",
    "Long words: internationalization characterization unbelievably",
    "Questions? Answers! Statements. Lists; clauses, and more.",
    "The meeting is at 10:30 on 2026-09-26 in room 4B.",
    "Symbols: # $ % & * + = @ ^ _ ` | ~ \\ \" '",
    "Tabs and newlines are whitespace too, as are other spaces.",
] * 3

# spm normalization rules: source code points <TAB> target code points, hex, space-separated.
RULES = [
    ("FF21", "41"),        # Ａ -> A
    ("FF22", "42"),        # Ｂ -> B
    ("2460", "31"),        # ① -> 1
    ("FB01", "66 69"),     # ﬁ -> fi, an expansion
    ("A0", "20"),          # NBSP -> space
    ("3000", "20"),        # ideographic space -> space
    ("2122", "54 4D"),     # ™ -> TM
    ("FF76", "30AB"),      # halfwidth ｶ -> カ
    ("65 301", "E9"),      # e + combining acute -> é, a multi-code-point key
    ("1", ""),             # U+0001 deleted
]

INPUTS_TINY = [
    "",
    "   ",
    "Hello world.",
    "  leading and   inner   spaces  ",
    "tab\tnew\nline\r\nend",
    "ＡＢ ① ﬁ™ ｶ",
    "é combining",
    "é́ stacked",
    "é́́ stacked more",
    "nbsp and　ideographic",
    "\u0001deleted control",
    "a literal ▁ meta space",
    "a▁b joined",
    "family 👨‍👩‍👧 and 👍🏽",
    "runes ᚠᛇᚻ and ꙮ are unknown",
    "x" * 150,
    "replacement � char",
    "The quick brown fox jumps over the lazy dog.",
    "今天天气很好。我们去公园散步吧！",
    "東京タワーに行きました。",
    "한국어 문장입니다.",
    "Привет, мир!",
    "مرحبا بالعالم",
    "The zebra quietly vexed the jovial wizard.",
    "Gänsefüßchen und Übermut",
    "未见过的汉字组合测试",
    "Здравствуйте, товарищи",
    "Symbols: # $ % & * + = @",
]

INPUTS_REAL = [
    "Hello world.",
    "  leading and   inner   spaces  ",
    "tab\tnew\nline\r\nend",
    "naïve café résumé",
    "ＦＵＬＬＷＩＤＴＨ ｶﾀｶﾅ ①②",
    "今天天气很好。我们去公园吧！",
    "東京タワーに行きました",
    "한국어 문장입니다.",
    "Привет, мир!",
    "مرحبا بالعالم",
    "नमस्ते दुनिया",
    "é combining",
    "é́ stacked",
    "emoji 👍🏽 family 👨‍👩‍👧",
    "a literal ▁ meta space",
    "zero​width",
    "ﬁ ligature ﬀ",
    "x" * 150,
    " nbsp　ideographic",
    "Ǆ ǅ ǆ titlecase",
    "\U00020000 ext-b",
    "� replacement",
    "control\x01\x7fchars",
    "The meeting is at 10:30 on 2026-09-26 in room 4B.",
    "https://example.com/a/b?q=1&r=2",
    "Berlin had a population of 3,520,031 registered inhabitants.",
    "Die Straße führt zum Bahnhof.",
    "Où est la bibliothèque?",
    "北京是中国的首都，上海是最大的城市。",
    "ひらがなとカタカナと漢字",
]

TYPED_SPECIALS = ["a <s> b </s> c <mask> d", "<s>", "</s> at the start"]

PAIRS = [
    ("where does the river run?", "A river runs north through the old city."),
    ("", "only a document"),
    ("今天天气", "天气很好。"),
]

# Inputs the two references are KNOWN to disagree on, each with why; HF's answer is the golden, since HF is
# the reference for the tokenizer.json this reads.
GRAPHEME = ("HF replaces a grapheme cluster under 6 bytes WHOLE by its shortest key; C++ matches the longest "
            "key at each position")
UNMAPPED = ("the tiny charsmap does not map this to a space as nmt_nfkc does, so it reaches WhitespaceSplit and "
            "Metaspace, which split it where C++ keeps it in the text")
EXPECTED_HF_ONLY = {
    "é́ stacked": GRAPHEME,
    "é́́ stacked more": GRAPHEME,
    "tab\tnew\nline\r\nend": UNMAPPED,
    "a literal ▁ meta space": UNMAPPED,
}

# The same model in the pipeline multilingual-e5 and bge-m3 declare: a Sequence normalizer (the charsmap, then
# runs of spaces collapsed) and Metaspace with no WhitespaceSplit before it.
TRAILING = ("Metaspace with no WhitespaceSplit keeps a trailing space as a lone meta space; C++ strips trailing "
            "whitespace before it segments")
EXPECTED_HF_ONLY_SEQUENCE = {
    "é́ stacked": GRAPHEME,
    "é́́ stacked more": GRAPHEME,
    "  leading and   inner   spaces  ": TRAILING,
    "   ": TRAILING,
}


def varint(buf, i):
    value = shift = 0
    while True:
        byte = buf[i]
        i += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return value, i
        shift += 7


def fields(buf):
    """(field number, wire type, value) for each field of one protobuf message."""
    i = 0
    while i < len(buf):
        key, i = varint(buf, i)
        number, wire = key >> 3, key & 7
        if wire == 0:
            value, i = varint(buf, i)
        elif wire == 1:
            value, i = buf[i:i + 8], i + 8
        elif wire == 2:
            length, i = varint(buf, i)
            value, i = buf[i:i + length], i + length
        elif wire == 5:
            value, i = buf[i:i + 4], i + 4
        else:
            raise ValueError(f"wire type {wire}")
        yield number, wire, value


def charsmap_of(model_bytes):
    """ModelProto field 3 is the NormalizerSpec; its field 2 is precompiled_charsmap."""
    for number, wire, value in fields(model_bytes):
        if number == 3 and wire == 2:
            for inner, inner_wire, inner_value in fields(value):
                if inner == 2 and inner_wire == 2:
                    return bytes(inner_value)
    raise ValueError("no precompiled_charsmap in the model")


def fairseq(ids):
    # XLM-R: every SentencePiece id shifted by one, SentencePiece's unknown (0) at 3
    return [3 if i == 0 else i + 1 for i in ids]


def xlmr_tokenizer_json(sp, charsmap):
    vocab = [["<s>", 0.0], ["<pad>", 0.0], ["</s>", 0.0], ["<unk>", 0.0]]
    vocab += [[sp.id_to_piece(i), sp.get_score(i)] for i in range(3, sp.get_piece_size())]
    vocab.append(["<mask>", 0.0])

    def special(i, content, lstrip=False):
        return {"id": i, "content": content, "single_word": False, "lstrip": lstrip, "rstrip": False,
                "normalized": False, "special": True}

    def token(t):
        return {"SpecialToken": {"id": t, "type_id": 0}}

    def seq(s):
        return {"Sequence": {"id": s, "type_id": 0}}

    return {
        "version": "1.0",
        "truncation": None,
        "padding": None,
        "added_tokens": [special(0, "<s>"), special(1, "<pad>"), special(2, "</s>"), special(3, "<unk>"),
                         special(len(vocab) - 1, "<mask>", lstrip=True)],
        "normalizer": {"type": "Precompiled", "precompiled_charsmap": base64.b64encode(charsmap).decode()},
        # the legacy Metaspace form paraphrase-multilingual and mmarco declare: no prepend_scheme, no split
        "pre_tokenizer": {"type": "Sequence", "pretokenizers": [
            {"type": "WhitespaceSplit"}, {"type": "Metaspace", "replacement": META, "add_prefix_space": True}]},
        "post_processor": {
            "type": "TemplateProcessing",
            "single": [token("<s>"), seq("A"), token("</s>")],
            "pair": [token("<s>"), seq("A"), token("</s>"), token("</s>"), seq("B"), token("</s>")],
            "special_tokens": {"<s>": {"id": "<s>", "ids": [0], "tokens": ["<s>"]},
                               "</s>": {"id": "</s>", "ids": [2], "tokens": ["</s>"]}},
        },
        "decoder": {"type": "Metaspace", "replacement": META, "add_prefix_space": True},
        "model": {"type": "Unigram", "unk_id": 3, "vocab": vocab},
    }


def classify(hf, sp, inputs, with_normalized, expected=EXPECTED_HF_ONLY):
    golden = {"cases": [], "hf_only": [], "typed_specials": [], "pairs": []}
    unexpected = []
    for text in inputs:
        ids = hf.encode(text, add_special_tokens=False).ids
        spm_ids = fairseq(sp.encode(text))
        case = {"text": text, "ids": ids}
        if with_normalized:
            case["normalized"] = hf.normalizer.normalize_str(text)
        if ids == spm_ids:
            golden["cases"].append(case)
        elif text in expected:
            golden["hf_only"].append({**case, "spm_ids": spm_ids, "why": expected[text]})
        else:
            unexpected.append(f"{text!r}\n  hf  {ids}\n  spm {spm_ids}")
    for text in TYPED_SPECIALS:
        golden["typed_specials"].append(
            {"text": text, "ids": fairseq(sp.encode(text)), "hf_ids": hf.encode(text, add_special_tokens=False).ids})
    for a, b in PAIRS:
        encoding = hf.encode(a, b)
        golden["pairs"].append({"a": a, "b": b, "ids": encoding.ids, "type_ids": encoding.type_ids})
    return golden, unexpected


def write(path, value):
    FIXTURES.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="ascii", newline="\n") as f:
        json.dump(value, f, ensure_ascii=True, indent=1)
        f.write("\n")


def report(name, golden, unexpected):
    print(f"{name}: {len(golden['cases'])} agree, {len(golden['hf_only'])} HF-only, "
          f"{len(golden['typed_specials'])} typed-special, {len(golden['pairs'])} pairs")
    if unexpected:
        print("UNEXPECTED disagreements — not written:")
        print("\n".join(unexpected))
        return 1
    return 0


def tiny():
    SCRATCH.mkdir(parents=True, exist_ok=True)
    corpus = SCRATCH / "corpus.txt"
    corpus.write_text("\n".join(CORPUS) + "\n", encoding="utf-8")
    rules = SCRATCH / "rules.tsv"
    rules.write_text("".join(f"{src}\t{dst}\n" for src, dst in RULES), encoding="utf-8")
    prefix = SCRATCH / "tiny"
    spm.SentencePieceTrainer.train(
        input=str(corpus), model_prefix=str(prefix), model_type="unigram", vocab_size=600,
        hard_vocab_limit=False, character_coverage=1.0, normalization_rule_tsv=str(rules),
        minloglevel=2)

    model_bytes = (SCRATCH / "tiny.model").read_bytes()
    sp = spm.SentencePieceProcessor(model_proto=model_bytes)
    tokenizer = xlmr_tokenizer_json(sp, charsmap_of(model_bytes))
    hf = Tokenizer.from_str(json.dumps(tokenizer))

    golden, unexpected = classify(hf, sp, INPUTS_TINY, with_normalized=True)
    if report("spm-tiny", golden, unexpected):
        return 1

    sequence = json.loads(json.dumps(tokenizer))
    sequence["normalizer"] = {"type": "Sequence", "normalizers": [
        tokenizer["normalizer"], {"type": "Replace", "pattern": {"Regex": " {2,}"}, "content": " "}]}
    sequence["pre_tokenizer"] = {"type": "Metaspace", "replacement": META, "add_prefix_space": True}
    hf_sequence = Tokenizer.from_str(json.dumps(sequence))
    golden_sequence, unexpected = classify(
        hf_sequence, sp, INPUTS_TINY, with_normalized=True, expected=EXPECTED_HF_ONLY_SEQUENCE)
    if report("spm-tiny-sequence", golden_sequence, unexpected):
        return 1

    write(FIXTURES / "spm-tiny.tokenizer.json", tokenizer)
    write(FIXTURES / "spm-tiny.golden.json", {"reference": "tokenizers + sentencepiece", **golden})
    write(FIXTURES / "spm-tiny-sequence.tokenizer.json", sequence)
    write(FIXTURES / "spm-tiny-sequence.golden.json", {"reference": "tokenizers + sentencepiece", **golden_sequence})
    return 0


def real(directory):
    hf = Tokenizer.from_file(str(directory / "tokenizer.json"))
    sp = spm.SentencePieceProcessor(model_file=str(directory / "sentencepiece.bpe.model"))
    golden, unexpected = classify(hf, sp, INPUTS_REAL, with_normalized=False)
    if report("spm-xlmr", golden, unexpected):
        return 1
    write(FIXTURES / "spm-xlmr.golden.json", {"reference": "tokenizers + sentencepiece, XLM-R", **golden})
    return 0


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--real":
        sys.exit(real(Path(sys.argv[2])))
    sys.exit(tiny())
