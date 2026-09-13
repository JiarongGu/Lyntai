# Deployment shapes — what to run where, and what each choice was measured to cost

> **What this is.** `docs/model-tasks.md` organises this library's model-backed seams by the SHAPE OF THE
> QUESTION they ask — extract, select, score, embed. This document is the other axis: **the shape of the
> DEPLOYMENT you are running in**. Same evidence, re-cut, because a consumer does not arrive asking "what is
> a selective task" — they arrive saying *"this is a game"* or *"this is a shared server"*.
>
> **Every figure here is a pointer.** `docs/memory-measurements.md` §5 owns them all and carries the caveats;
> nothing is re-derived here, because a second copy of a number is wrong the moment the first is retracted.
>
> **It is advice over seams that already exist.** No shape below needs new API, and no default enables any
> of it.

**The one rule that survives every shape:** a value only the deployment can know is the deployment's to
declare. Nothing here is a probe the library performs or a default it asserts — `LocalDiffusionOptions.Accelerator`
(**D68**) is the precedent, where the host declares the device and the library derives from it rather than
guessing.

## The three facts that decide most of it

**1. Contention is per-MODEL, not per-machine.** Two seams that want the SAME model fight over that
server's `--parallel` slots; two seams on different models are different processes and cannot contend for
slots at all. Measured (`contention-mixed-recall-quiet-rerank`): a judge sharing the instruct server that
annotation already writes through degrades **9.95×** under mixed load (268.6 ms → 2,672.0 ms recall p50),
while a reranker on its own server does not degrade at all on a quiet device and **1.72×** on a busy one.

**2. A router saves no memory.** `llama-server`'s router mode is a process SUPERVISOR that spawns a full
child per model — three dedicated servers and one router with three models are the same process count plus
a ~115 MB supervisor. Residency and routing are independent axes; "consolidate onto a router to save VRAM"
is refuted. What a router buys is one endpoint, on-demand loading and an eviction policy.

**3. Encode-only work survives a busy device and generation does not.** Measured with a game rendering
(`.claude/knowledge/pitfalls.md` §Environment / tooling): a 4B's generation goes from **12.1× faster** than CPU
when offloaded on a free device to **92× slower** when the device is contended — a ~1,100× reversal —
while an embedder stays **4.8×** faster than CPU even while the game renders. **The tell you are in the bad
regime is a NON-MONOTONE offload curve**, because a genuinely wrong `-ngl` degrades smoothly and contention
does not.

## Shape: a shared server, many tenants

**What you are optimising:** throughput and tail latency across tenants, with the memory subsystem as
infrastructure beside whatever the application's own model is doing.

- **Give the verification seam its own backend.** This is the largest single measured win in this document
  — ~10× on recall p50 — and **only about 2× of it is the smaller model; the rest is the un-sharing**
  (`docs/task-archive.md` Part 190). Splitting beats shrinking.
- **A cross-encoder is the measured way to spend under a gigabyte on that seam.** 468,393,760 B captures
  **6.0 of the 7.0 points** a perfect judge offers, where 806,058,240 B of instruct model captures **0.0**
  and is inert. A 4B judge at the shipped depth SPENDS 10.5 points. `AddMemoryCrossEncoderVerification`
  (**D115**) is the shipped route; set `CrossEncoderVerificationOptions.EndorseCount` to your recall limit,
  because endorsing more than a page replaces the ranking instead of refining it.
- **Annotation runs on EVERY write**, so it is the seam most worth pinning to a small fast backend —
  `LlmAnnotationOptions.ClientName` exists for exactly that.
- **Pin with `ClientName`, never with `Model`.** A candidate's own model wins over the request's, so on any
  deployment whose default candidates pin models, setting a seam's `Model` does nothing and both memory
  seams are fail-open — they run on another model and nothing reports it (**D87**; `TASKS.md` Part 128).

**Not measured here:** concurrency beyond 4 workers per loop, more than one tenant's corpus, and anything
about cross-tenant cache behaviour. The contention figures are one box.

## Shape: a game, or anything that already owns the device

**What you are optimising:** never being the reason the frame rate drops, on hardware you do not control.

- **Prefer the encode-only seams.** The embedder and the cross-encoder keep their advantage while the GPU
  is contended; the generative seams — judge, annotator, extractor — are the ones that collapse. This is a
  cost argument that is INDEPENDENT of the quality argument for a cross-encoder, and the two happen to
  agree.
- **Pass `-ngl` explicitly.** The server's default is not neutral and its log prints no offload line, so a
  run that does not set it has not chosen.
- **Budget VRAM as additive.** Four servers (4B + 1B + embedder + reranker) measured a ~6,419 MiB footprint;
  the harness refuses to start under 7,000 MiB free because launching into too little kills the process
  **silently** — no exception, output truncated mid-sentence, exit code 127.
- **A static embedder is the only option that contends for nothing at all**, and on the MEMORY workload it
  is nearly free. `potion-base-8M` is **30,236,760 B** of CPU lookup table — no server, no GPU, no port.
  Against the 333,590,944 B incumbent it costs **0.5 points on the shipped default** (54.0% against 54.5%,
  one question in two hundred) and reproduces `+forget0` EXACTLY, because those arms do not seed
  semantically; it costs **10 points** only where semantic seeding is on
  (`locomo-retrieval-static-embedder`). On a purely embedding-bound selective task it is ~12 points behind
  (`affordance-static-embedders`) — **how much an embedder is worth is a property of the ARM**.
  <br>And turning semantic seeds ON is worth more than the embedder is: **+21.5** points over the shipped
  default even with the static model. Do not read "cannot afford a GPU embedder" as "cannot afford the
  arm". Nothing in this library can call a static embedder yet — `TASKS.md` Part 196 holds that ruling.

**Not measured here:** anything on a device this repository does not have, and any frame-time impact — the
contention figures measure the MODEL's throughput while a neighbour rendered, never the neighbour's.

## Shape: several models in parallel, for different jobs

**What you are optimising:** total footprint and interference across seams that each want their own model.

- **Count contention per model, not per machine** (fact 1). An annotator and a judge on one instruct model
  contend; an embedder and a reranker beside them cannot.
- **Do not consolidate onto a router to save memory** (fact 2). Consolidate for one endpoint and an
  eviction policy, which are real, and budget the weights as if every model were its own process, because
  it is.
- **`--embedding` and `--reranking` are PROCESS-WIDE**, so a plain `--models-dir` router serves chat and
  answers **501** to embeddings while still listing every model it found. Per-model roles need
  `--models-preset`.
- **Two seams wanting the same model divide `--parallel` slots**, and `--parallel N` divides the CONTEXT
  across slots — so a roster that fits at N = 1 can exceed a slot at N = 4.

## Shape: no server at all (in-process), and why it is NOT a quality question

**A game cannot spawn `llama-server.exe` on a stranger's machine.** That is the real driver here, and it is
worth separating from performance, because two things already measured settle the performance half:

- **The same model gives the same vectors.** Encode-only output is byte-identical across devices
  (`.claude/knowledge/pitfalls.md` §Environment / tooling), so moving a model in-process cannot change a
  retrieval score. There is no quality measurement to run.
- **The HTTP hop is already noise.** A local call with `UseProxy = false` measures **0.4 ms** mean against
  an embed that costs far more. In-process buys back ~0.4 ms per call, which is not a reason.

**So the case for in-process is entirely operational**: no second process to ship, start, supervise and
tear down; no port to conflict or prompt a firewall; a lifetime tied to the application's. Those are real
and they are decisive for a distributed application — they are simply not things a benchmark answers.

**The option space is a 2×2, and the dependency cost differs by an order of magnitude across it.** Surveyed
2026-09-13; all four exist and are maintained, none is adopted here.

| | CPU | GPU |
|---|---|---|
| **static** (lookup table, no matmul) | `model2vec` — measured at ~12 points behind; needs a managed tokenizer (`Microsoft.ML.Tokenizers`), pure managed, smallest possible footprint | n/a — there is nothing to accelerate |
| **transformer** | ONNX Runtime — native dependency | ONNX Runtime **DirectML**, or LLamaSharp + a CUDA backend |

**For a game, DirectML is the one worth noting**: it is vendor-neutral on any DX12 device and ships with
Windows, where a CUDA backend requires the user to have an NVIDIA card and runtime. A library that
hard-wired CUDA would be choosing the user's hardware for them, which is the thing **D68** refuses.

**None of this is shipped, and the ruling is the dependency, not the capability.** `IEmbedder` is already
the seam; `dotnet-package-layout.md` forbids a third-party dependency in Core, so every cell above is an
ADAPTER package plus a public type on an API frozen under SemVer since 1.0 (**D70**). `TASKS.md` Part 196
holds it.

## Shape: the size class you can afford

The same seam is worth wildly different amounts at different sizes, and the direction is not uniform. Every
row is `docs/memory-measurements.md` §5.

| seam | what a small model does | where the evidence is |
|---|---|---|
| **embed** (tool routing) | 25,008,064 B reads 78.6% at 3 options against a 333,590,944 B incumbent's 81.0% — but decays with roster size where the incumbent is flat | `affordance-cosine-sub100mb` |
| **embed** (static, no server) | 30,236,760 B reads 69.0%; retrieval tuning at 4.3× the bytes does not close it | `affordance-static-embedders` |
| **score-a-pair** (cross-encoder) | 468,393,760 B captures 6.0 of a perfect judge's 7.0 points | `locomo-lamar600m-q8-n200` |
| **select-from-list** (judge) | a 1B is INERT — 0 of 19 rescuable calls; a 4B SPENDS 10.5 points | `locomo-judge-1b-n200` |
| **affordance** (tool choice) | family beats size: 491,400,032 B reads 60.7% where 806,058,240 B reads 10.1% | `affordance-native-transport` |
| **reader** (answers from a page) | arm sensitivity collapses with size — spread 14.7 / 8.8 / 4.7 points across 4B / 1B / 0.5B | `locomo-qa-completeness-vs-depth` |

**The reader row is the one that bounds everything else**: the smaller the reader, the less the memory
layer's quality registers at all. Memory work buys less the smaller the consumer, which is worth knowing
before choosing one.

## What to spend a context budget on, in any shape

**Completeness, not count — on a SEARCH workload, which is the half that was measured.** Giving a reader
whole items instead of 120-character headlines is worth **+14.9 / +9.2 / +7.1** token-F1 at 4B / 1B / 0.5B
— on HALF the context that doubling the item count spends, which buys nothing at any size.
`MemoryQuery.Detail = MemoryDetail.Full` (**D104**) is the shipped route and
`GraphMemoryOptions.HeadlineChars` still defaults to 120. A second seam agrees independently: **D108**'s
reranker went 78.0% → 91.0% on whole turns.

**On a SUPPRESSION workload it behaves completely differently — there is an OPTIMUM, not a direction**
(`longmemeval-ku-completeness-peak`). Whole items are bigger, so a character cap admits fewer of them, and
on the class that scores preferring a revised fact over the one it superseded that cap stops being an
overhead and becomes a FILTER. Swept over six budgets, `clean` runs **32.9 → 40.0 → 48.6 → 54.3 → 52.9 →
44.3%** against a flat 44.3% for headlines: it costs at a tight cap, peaks around **4,200 characters**, and
returns to exactly break-even once the cap no longer binds. Too tight and the page starves (the current
fact itself falls out, 82.9% → 47.1%); too loose and the superseded fact comes back with everything else.

**Two things follow, and the second is the one to act on.** Do not read the search figures as a general
rule about context budgets — the lever's sign depends on the workload AND the cap. And on this workload
completeness is not the lever to reach for at all: a deeper first recall trimmed hard (`fill` at 1,200,
or `CandidateMultiplier = 16`) reaches **61.4%** at a third of the context, beating completeness at its own
optimum.

**And bound a SELECTIVE input before buying a bigger model.** List length governs a judge more than model
choice does (20 shown → 16.2% precision, 80 → 2.6%), and for a tool roster the native transport bounds what
no prompt could: **20-30%** false calls against the prompt protocol's **90-100%**, plus convergence of
99.4-100% against 11.3-24.4% (`affordance-native-false-calls`). Check the model's chat template carries a
tool section first — one that does not returns 200 with `tool_calls: null` and answers anyway.

## Where this is thin

Stated rather than left implied, because a shape-by-shape document reads as more complete than it is.

- **One box, one GPU, one operating system.** Every contention and latency figure here is a laptop with a
  12 GB device.
- **No multi-tenant corpus.** The server shape is argued from a single-corpus contention bench.
- **No frame-time measurement** for the game shape — only the model's own throughput beside a renderer.
- **English, and mostly synthetic.** The tool-routing corpus is a fixture built to be measured.
- **Nothing about cost in money**, or about a hosted API as a deployment shape at all.
