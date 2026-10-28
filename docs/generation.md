# Generation — backends, delivery, pipelines and durable renders

> **Maintained state.** The guide to the media generation platform as it is TODAY: what each shipped backend
> is, how a request is routed and delivered, and what is and is not verified against a real service. The
> reasoning behind each choice is `docs/DECISIONS.md` (**D24** scope, **D28** input roles, **D64** the
> Inconclusive submit, **D67** the stream door, **D69** an unmeasured mapping is an option, **D127**
> capabilities as data, **D156** one door for every backend, **D180** ComfyUI's inputs and `Model3d`, **D181**
> the durable pipeline, **D185** the one spend gate, **D189** a second queue vendor); `docs/guide.md`
> §Generation is the short version.

## 1. Registering backends

Every shipped backend has an `Add*` of its own — `AddOpenAiImageProvider`, `AddAutomatic1111Provider`,
`AddComfyUiProvider`, `AddFalProvider`, `AddLocalDiffusionProvider`, `AddPiperProvider` — and each takes a
**configure callback**, the same shape as `AddHttpProvider(id, o => …)` on the LLM side. Every option has a
default (each backend's conventional local URL, or the vendor's API root), so a registration sets only what
differs from it; a blank base URL reports `NotConfigured` rather than failing. For a render backend of your
own, `AddProvider(sp => …, declares: …)` registers it — the one door every backend comes through, whatever it
produces — and `AddMediaRouting()` wires the media router to route it.

**BYO `HttpClient`** is optional on the four HTTP backends; `AddLocalDiffusionProvider` and `AddPiperProvider`
take a BYO `IProcessRunner` instead, because they spawn a binary and never make a request. Lyntai **never
disposes a client you supply**: it is yours, and it may be carrying a Polly pipeline or an auth handler.
**ComfyUI's own client follows no redirect** (**D180**): it carries what you configured for ComfyUI, so point
`ComfyUiOptions.BaseUrl` at the address that answers. Omit
it and Lyntai registers a named client with an *infinite* `HttpClient` timeout, so the per-call deadline owns
cancellation rather than the 100-second default aborting a healthy render. To decorate Lyntai's own client
instead of replacing it, reach it by name:

<!-- compile-given: sealed class MyLoggingHandler : DelegatingHandler { } -->
```csharp
services.AddHttpClient(MediaBackendBuilderExtensions.HttpClientName("fal"))
        .AddHttpMessageHandler<MyLoggingHandler>();
```

**The deadline is per backend, and infinite there does not mean unbounded.** Every options object carries a
`Timeout`: 10 minutes for the inline HTTP render backends (`OpenAiImageOptions`, `Automatic1111Options`), 2
minutes for the queue ones (`ComfyUiOptions`, `FalOptions`, whose calls are submit/status/fetch round-trips
rather than renders), 5 minutes for `PiperOptions`, and 15 minutes for `LocalDiffusionOptions`. The two
subprocess backends pair theirs with an `InactivityTimeout`, because a local engine is legitimately slow but
never *silent*, so silence rather than elapsed time is what marks it wedged. A request's own
`MediaRequest.TimeoutSeconds` overrides the backend's deadline. A fired deadline is a `ProviderVerdict.Timeout`
**result**, not a throw; your own `CancellationToken` keeps its own meaning.
`Timeout = System.Threading.Timeout.InfiniteTimeSpan` drops the backend's own deadline, and a request that
names `TimeoutSeconds` still gets one.

**For a queue backend the deadline bounds one HTTP call, never the render** — the render outlives every call,
and bounding it is the durable job's retry budget. One consequence: a **submit whose outcome is unknown** — no
answer before the deadline, a connection dropped after the request was sent, or a 2xx answer carrying no
operation id — comes back `Failed` **and `Inconclusive`**, and the router *surfaces* it rather than trying the
next backend, because that queue may already hold a billable render and the next candidate would buy the same
generation twice (**D64**). It is not counted against the backend's cooldown either. A submit that provably
never left the process (a refused connection, a failed name lookup or TLS handshake) is conclusive and advances.

## 2. The shipped backends

`Lyntai.Generation` ships six (`dotnet add package Lyntai.Generation` — it pulls Core with it). Five have run
against a live engine or a local server speaking their API — the table says which, and neither of the two
HTTP image backends has been called on its vendor's own service or the WebUI itself; **fal's has been called on
fal's own queue for an image model, and a video model, `IN_PROGRESS` and a runtime failure are still the documented
shapes** (§3).

| Backend | Delivery | Notes |
|---|---|---|
| `OpenAiImageProvider` | Inline | `/images/generations`, or `/images/edits` when the request carries an input image. *Measured against a local OpenAI-compatible server — stable-diffusion.cpp's `sd-server` (`656a135`), **not OpenAI***: the `/models` probe, a generation answered as inline `b64_json`, and the multipart edit all pass (`OpenAiImageLiveTests`), which proves the request is one a second implementation accepts and says nothing about OpenAI's service. The server takes `"model": null` and answers only `b64_json`, and the OpenAI shape carries no step count, so its own default (20) applies. **Not measured against OpenAI's current GPT-image models**, which are reported to reject `response_format`, so that family is sent none by default (`OpenAiImageOptions.ResponseFormat`). A `url` response comes back as a URI artifact — never downloaded for you |
| `Automatic1111Provider` | Inline | A locally-run SD WebUI's `/sdapi/v1` API: `txt2img` / `img2img`. *Measured against stable-diffusion.cpp's `sd-server` (`656a135`, Vulkan), which implements the WebUI's shapes — **not against the WebUI itself*** (`Automatic1111LiveTests`: the probe, txt2img at a non-square size, img2img, all passing). That proves the shapes it implements, not every WebUI behaviour. Seen different from the WebUI's documented API: `sd-server` lists only its one loaded model in `sd-models` where the WebUI lists the checkpoints it could load — so the probe's "loaded" reading is proven only against a server for which the two coincide — and answers `info` with its own metadata. It honoured `steps`, `cfg_scale`, `width`/`height` and `denoising_strength`, and (§5, *Parameters*) `seed`, `negative_prompt`, `sampler_name` and `scheduler`, while it ignores `override_settings` and `<lora:…>` tags. A WebUI that is not running — nothing listening — is **Failed**, a down host like any other (**D31**), and so is one that drops a render mid-response; both count toward the dead-host threshold, and only the detail tells them apart. Its probe checks a checkpoint is *loaded* — "up" isn't "usable". The WebUI's loaded checkpoint decides the model unless the request names one the host declared (§5, *Parameters*): `MediaRequest.Model`, including a candidate's `a1111:sd_xl_base` pin, is **not** sent |
| `ComfyUiProvider` | **Job** | *Measured against a live server, image, video and mesh workflows* — video both as a core-node MP4 and through a real video model, Wan 2.2 TI2V-5B, from text and from a `first-frame` input. Workflow-driven: you supply the graph in `Options["workflow"]` (+ optional `Options["prompt-path"]` to place the prompt), and outputs come back as view URIs — a mesh as `model/gltf-binary`. Each input (bytes, or a URI it fetches from any http(s) server, capped by `MaxFetchBytes`, and fetched off ComfyUI's own origin with no credentials so long as the ComfyUI client follows no redirect itself — `AddComfyUiProvider`'s does not, and a client you supply should not either (**D180**); validate URIs a model supplies) is uploaded and its stored name written at the field `Options["input-path"]` names, or `Options["input-path:<role>"]` for an input with a role; an input with nowhere to go is refused, never dropped. A transport failure while polling reports **Running, not Failed**, while a 4xx or an unconfigured base URL stays terminal, so a bad id never polls forever |
| `LocalDiffusionProvider` | Inline | A local `sd-cli` / stable-diffusion.cpp subprocess through `IProcessRunner` — no key, no network, no content policy in the path. Argv and the multiple-of-64 size clamp are measured against a real engine (txt2img and img2img, end to end through the library) |
| `FalProvider` | **Job** | *Written from fal.ai's public docs, then run against fal's own queue for an image model (submit → poll → fetch, a failed request); a video model has not been called* (§3). One aggregator queue reaching the Wan/Kling/Veo-class video models. The operation id **carries its model** (`"model#requestId"`) because a resumed job has only the id, and a transport failure while polling reports **Running, not Failed** — a 500 says nothing about a paid render still in flight |
| `PiperProvider` | Inline + **Stream** | A local piper TTS engine through `IProcessRunner` — no key, no network. Raw PCM **streams** through the media stream door as it is synthesised (measured against a real engine: several chunks before one terminal), typed `audio/pcm;rate=…;bits=16;channels=1;endian=little` with the rate read from the voice's own config. `GenerateAsync` is the same stream, buffered |

**A current model behind the local backends: Z-Image-Turbo** (**Apache-2.0**; the host supplies Comfy-Org's three
repackaged files from `Comfy-Org/Z-Image-Turbo`: the bf16 diffusion model, a Qwen3-4B text encoder — the fp8-mixed
one for ComfyUI, the bf16 one for stable-diffusion.cpp — and the VAE). SD 1.5 stays the cheap smoke model; the
new test sits beside it and is skipped without `LYNTAI_COMFYUI_ZIMAGE_MODEL`. The local backends apply **no content filter**, as before, and this adds none.

- **ComfyUI runs it, through the provider as it is.** No option was needed: the workflow already names each of the
  three files (`UNETLoader`, a `lumina2` `CLIPLoader`, `VAELoader`), which is why this backend's model is "the
  workflow" (`ComfyUiLiveTests`). Measured on a 12 GB RTX 4080 Laptop at 512×512, 8 steps, CFG 1, `res_multistep`:
  about 37 to 44 s for the first render of a server (the weights load) and 7 to 14 s warm. The bf16 model is 12.3 GB, so
  ComfyUI streams weights between RAM and VRAM on its own; nothing was cast.
- **`LocalDiffusionProvider` and the `sd-server` behind `Automatic1111Provider` run it, with the bf16 Qwen3-4B
  encoder** (`qwen_3_4b.safetensors` from the same repository, not fp8-scaled), on the measured engine (`656a135`,
  Vulkan). The same engine aborts loading the fp8-mixed encoder (`GGML_ASSERT(scale_nelements == 1 ||
  scale_nelements == out_features)`), with or without `--type bf16`, so the file the host supplies is the one that
  decides. **No option was added**: the argv is the provider's own with `ArgvFlags["model"]` set to
  `--diffusion-model` and `ExtraArgs` carrying `--llm <encoder> --vae <vae> --diffusion-fa --offload-to-cpu`
  (`A_split_model_is_expressible_by_renaming_the_model_flag_and_adding_its_files_as_extra_args` pins it); the
  `sd-server` is started with the same three files and flags, and the provider needs only `Steps = 8` and
  `CfgScale = 1`. Measured on a 12 GB RTX 4080 Laptop at 512×512, 8 steps, CFG 1, seed 8: the engine keeps the
  19.6 GB of weights in RAM and streams them through the card, so a render is minutes where ComfyUI's is seconds, and
  the card's own use peaks at about 7.5 GB above what was already resident. `sd-cli` is a fresh process per call, so
  both of its renders load the weights again (262 s, then 250 s); the server loads once and its first render is
  388 s against 169 s warm. Behind `LocalDiffusionLiveTests` (`LYNTAI_SD_ZIMAGE_MODEL`, `LYNTAI_SD_ZIMAGE_LLM`,
  `LYNTAI_SD_ZIMAGE_VAE`, each a full path) and `Automatic1111LiveTests` (`LYNTAI_A1111_ZIMAGE_URL`, a server of its
  own); each skips without its variables, and SD 1.5 stays the smoke model.

A backend that catches its own exceptions applies the router's rules itself, so no shipped backend turns a
possibly-delivered submit into a conclusive `Failed`, and none reports a thrown exception as a content
`Refused`.

## 3. fal: measured on an image model, documented for the rest

`FalProvider` was written from fal.ai's public queue documentation. `FalLiveTests` has called it on fal's own
queue (`queue.fal.run`, `Authorization: Key …`) with a funded account: a text-to-image on the cheapest model,
`fal-ai/flux/schnell` (fal's pricing page lists it at $0.003 per megapixel, billed rounded up to one) at its
smallest preset (`image_size` `square`, 512×512), and an image-to-image on `fal-ai/flux/schnell/redux` whose input
cannot be downloaded. The wire replayed from those answers is in `FalProviderTests` ("recorded from fal").

**Measured**: a locked account (no balance) answers a submit `403 {"detail":"User is locked. Reason: Exhausted
balance. Top up your balance at fal.ai/dashboard/billing."}`, classified by its status (403 and 401
`AuthFailed`, 429 `RateLimited`); the live tests **skip**, naming the balance, on that answer. On a funded
account:

- **Submit**: `200` with `status` `IN_QUEUE`, `request_id`, `response_url`, `status_url` and `cancel_url` (all under
  the model's owner and app, `fal-ai/flux`, never the full model path), `queue_position` (`0`), `logs` null and
  `metrics` empty.
- **Status**: `200` with `status` `COMPLETED` on the first read, a second after the submit (`IN_QUEUE` was seen only
  on the submit answer, `IN_PROGRESS` not at all), the same URL fields and `metrics.inference_time`. No `error`
  field and no cost.
- **Result**: `{"images":[{"url","width","height","content_type":"image/png"}],"timings","seed",
  "has_nsfw_concepts","prompt"}`, the URL a public CDN link that served a 512×512 PNG. No cost field, so
  `MediaUsage.CostUsd` is null and a spend cap does not see fal renders.
- **A failed request**: the redux request whose `image_url` does not resolve was accepted (`200 IN_QUEUE`), polled
  `COMPLETED` with no `error` field, and answered the result fetch `422 {"detail":[{"loc":["body","image_url"],
  "msg":"Failed to download the file. …","type":"file_download_error",…}]}`. So the poll does not see this failure
  (the `ErrorField` check never fires on it) and the fetch reports it `Failed` with fal's words; a job that polls
  `Succeeded` then fetches ends "succeeded but its artifacts could not be fetched: 422 …".

**Still documented, not measured**: `IN_PROGRESS`, `queue_position` on a status read, a failure fal reports on the
status document itself (`ErrorField`, `ErrorTypeField`), a video model, cancel, an object-valued request field
(`JsonFields`), and what a failed request bills. One `FalLiveTests` run is two submits at about a third of a cent.
Its tests pin this library's behaviour for each, never that fal answers in it.

**What a host can correct in configuration** (`FalOptions`, D69): the URL segments (`RequestsSegment`,
`StatusSegment`, `CancelSegment`), the status vocabulary (`StatusVocabulary`), the cost fields (`CostFields`),
the failure fields (`ErrorField` — fal documents a failed request as `COMPLETED` carrying an `error` field,
which polls as `Failed` here — and `ErrorTypeField`), the auth scheme (`AuthScheme`, `Key` by default) and
extra query parameters on every call (`QueryParameters`), the per-model request path (`RequestPaths`) and
which request fields are raw JSON (`JsonFields`). Any request field goes through verbatim in
`MediaRequest.Options`, as a string unless `JsonFields` names it. **What it cannot correct**: the response fields read by name — `request_id`, `status`,
`queue_position`, `url` and `content_type` — which a release would have to change.

**Cost**: fal's results and statuses carry **no cost field** (measured), so unless a `CostFields` name matches,
`MediaUsage.CostUsd` is null and a spend cap never sees these renders. A reported cost is used as given, never
inferred from a rate card, and the shipped names (`cost`, `cost_usd`, `price`) match nothing on fal's own queue.

**Sub-path models, measured**: fal documents status, result and cancel URLs as using "only the owner and the app"
and says one carrying the endpoint path returns `405`. The run confirmed it for `fal-ai/flux/schnell`: the
status URL under `fal-ai/flux` answered `200`, under the full path `405`, and the submit answer's own
`status_url`, `response_url` and `cancel_url` use `fal-ai/flux`. So the backend submits to the full path and
polls, fetches and cancels under `fal-ai/flux` (`FalProviderTests`); the operation id keeps the whole model. A
`405` polls as still running, so the full path would never have polled a sub-path model to completion. The
live test records the full-path answer on every finished request. A host whose model fal addresses differently
sets the path per model in `FalOptions.RequestPaths` (`["fal-ai/flux/schnell"] = "fal-ai/flux/schnell"` polls under
the full path); a model not listed keeps the owner-and-app default, pinned byte for byte. The path is **not** read
from the submit answer's `status_url`, `response_url` and `cancel_url`: a durable job resumes with only the operation
id (`"{model}#{requestId}"`), so those URLs would have to ride inside it. The answer shows the fields are there
and agree with the default for the models run, so reading them stays an option for a model that breaks the rule. A cancel is a REQUEST (202), so `CancelAsync` reports the render still running and only a poll says how it
ended. A model's input fields other than the prompt go through `MediaRequest.Options` **as strings**
(`"image_size":"square_hd"`), except a key listed in `FalOptions.JsonFields`, whose value is sent as raw JSON, so an
object-valued field such as a custom `image_size` is `o.JsonFields = ["image_size"]` plus
`Options["image_size"] = """{"width":1024,"height":768}"""`. A listed option whose value is not valid JSON
(a preset name, say) is refused as `Unsupported` before any call. The list is a name list rather than a marker in the
value, because whether a field is an object is a property of the model's schema, and no string can be mistaken for JSON by
what it begins with. Nothing has sent an object-valued field to fal yet: the run's `image_size` was a preset name.

**A free way to verify the wire.** Hugging Face's router proxies fal's own queue wire, and a free Hugging Face
account carries a small monthly inference credit and needs no fal account. The route is a configuration, not
a class:

<!-- compile-given: string hfToken; -->
```csharp
services.AddLyntai(cfg => cfg
    .AddFalProvider(o =>
    {
        o.BaseUrl = "https://router.huggingface.co/fal-ai";
        o.AuthScheme = "Bearer";
        o.ApiKey = hfToken;                          // an hf_ token
        o.QueryParameters["_subdomain"] = "queue";
    }));
```

It confirms the queue vocabulary, the error fields, the result shape and the sub-path question. It does not
confirm fal's direct `Key` auth or fal's own billing, and because it reaches the same backend it is a
verifier rather than a fallback: an outage at fal takes both down. The route is documented by Hugging Face;
nobody has run it from this library yet.

## 4. Hosted fallbacks beside fal

**The library ships no second hosted queue backend, and none of the candidates below has been called.** A
survey of hosted vendors' public documentation found three worth considering as a fallback: **WaveSpeedAI**
(a queue over the same model families, with the submit shape closest to fal's), **OpenRouter** for video (one
queue wire with normalised parameters, and a cost it documents in USD) and **Replicate** (a mature queue API
that also offers a synchronous mode). **D189** makes that the rule: a second vendor is its OWN provider named for
its backend — its own options class and `Add<Backend>Provider`, every mapping an option — and the shared
internal queue engine is extracted from `FalProvider` only when that vendor is actually written. A backend that
speaks fal's own wire is a `FalOptions` preset (§3), not a class.

Until then, a hosted fallback is a backend of your own, registered through `AddProvider`. To be tried beside
fal on the queued door it must implement `IMediaJobProvider` and declare `ProviderOperation.Queued`; a
pipeline-job stage (§8) also falls back to INLINE candidates once its queued ones run out without committing
anything, while the render job is queued-only. Either way it helps only at SUBMIT: a
render that fails after a backend accepted it is not re-routed, and an Inconclusive submit never falls back.
`MediaRequest.Options` is request-wide, so fal and the fallback receive the same option keys — a cross-vendor
fallback is reliable only on the fields both share.

## 5. Requests, inputs and routing

Inputs — an init image, a first frame, a style reference, a voice sample — are built with the **named
factories** `MediaInput.Init` / `FirstFrame` / `Reference` / `Voice`, or `MediaInput.From(role, …)` for a
role a backend documents itself — never the positional constructor. That constructor takes
`(MediaType, Data, Uri, Role)` with `Role` **last**, so a plausible positional call compiles clean, binds the
role string to the media type and leaves the role null — and then nothing fails: the backend gets a
well-formed roleless input and an img2img request quietly becomes text-to-image (**D28**).

Backends declare what they can do in `ProviderCapabilities` — media kinds, delivery modes, whether a request
may carry inputs, model catalogues — and the router **skips a candidate that cannot serve the request** before
spending anything. An input ROLE a backend has no place for is refused by the backend itself (`Unsupported`,
nothing sent), which the router advances past; `Limits` is advisory and enforced by nothing. Fallback is a
**policy**, not a law. The default matches the LLM router (a content `Refused` surfaces rather than being
re-submitted elsewhere), but pairing a hosted backend with a locally-run one makes the other choice
reasonable:

```csharp
cfg.ConfigureMediaRouting(p =>
    p.On(ProviderVerdict.Refused, FallbackAction.Advance));   // local backend picks it up
```

Every backend answers **"are you usable?"** without generating anything (`ProbeAsync`), so a setup screen
never pays for a test image.

### Parameters: a checkpoint, LoRAs and sampling

A request names more than a prompt through typed fields on `MediaRequest`, one name for each in
`MediaParameters`: `Checkpoint` (a file name in the model folder the SELECTED backend reads, never a path),
`Loras` (a list of `MediaLora(name, weight)`), `NegativePrompt`, `Seed`, `Steps`, `CfgScale`, `Sampler` and
`Scheduler`. The size stays the advisory `Options["size"]` hint every backend reads or ignores. Sampler and scheduler are
spelled as the selected backend spells them, since the engines disagree (`euler_a` / `Euler a` / `euler`) and nothing
translates; `MediaRequest.Model` still means an aggregator's endpoint id, not a checkpoint file. Every field is
null or empty by default, and a durable job carries them through its JSON. A host points a local engine at its model
folders (`o.CheckpointDirectory = checkpoints; o.LoraDirectory = loras;` in `AddLocalDiffusionProvider`) and a request
names files in them: `Checkpoint = "sd15.safetensors"`, `Loras = [new MediaLora("lcm-lora-sdv1-5.safetensors", 1.0)]`,
`Seed = 7, Steps = 4, CfgScale = 1, Sampler = "lcm"`.

A backend states which it honours in `ProviderCapabilities.Parameters`; empty honours none. **A request naming one a backend
does not declare is skipped by the router and, called directly, refused by the backend as `Unsupported` before anything is
sent or spawned** — the way an agent door refuses an option whose ability it states unsupported — because a backend that took
the call and ignored a checkpoint would return a plausible, wrong image. That holds for the hosted and speech backends too:
`OpenAiImageProvider`, `FalProvider` and `PiperProvider` declare none and refuse one through the same check and in the same
words, a request naming none reaching the wire as before (`GenerationProviderContract`'s parameter fact runs over every
shipped backend). A hosted API may well take a seed or a step count on its own wire, but declaring one is a separate step,
its mapping measured or made an option. The local backends also refuse a value no engine can
read (a non-positive step count, a non-finite weight, a name carrying a folder or a tag character).

| | checkpoint | LoRAs | negative prompt, seed, steps, CFG, sampler, scheduler |
|---|---|---|---|
| `LocalDiffusionProvider` (sd-cli) | `CheckpointDirectory`: the file there by name, else with `.safetensors`, `.gguf` or `.ckpt` added, passed as `-m`; declared only when the folder is set | `LoraDirectory`: passed as `--lora-model-dir`, each LoRA written into the prompt as `<lora:name:weight>`; declared only when the folder is set | `-n`, `-s`, `--steps`, `--cfg-scale`, `--sampling-method`, `--scheduler` (each flag a settable `ArgvFlags` key) |
| `Automatic1111Provider` | `override_settings.sd_model_checkpoint`, if the host declared it | tags in the prompt (extension dropped), or the payload field `LoraRequestField` names, if declared | `negative_prompt`, `seed`, `steps`, `cfg_scale`, `sampler_name`, `scheduler` |
| `ComfyUiProvider` | the graph field `checkpoint-path` names | per LoRA `n` (from 1): `lora-path:n` for the name and `lora-weight-path:n` for the weight, comma-separated for a loader with a model and a text-encoder strength | `negative-prompt-path`, `seed-path`, `steps-path`, `cfg-scale-path`, `sampler-path`, `scheduler-path`, and `width-path` / `height-path` for the size hint |

- **sd-cli** takes its folders from the host (`LocalDiffusionOptions.CheckpointDirectory`, `LoraDirectory`) and the host's
  `ModelPath` stays the checkpoint a request that names none renders with. A name the folder does not hold is refused
  before the spawn.
- **The Automatic1111 API** has no way to ask a server what it honours, so the host declares it:
  `Automatic1111Options.HonouredParameters` defaults to what was measured on stable-diffusion.cpp's `sd-server` — every
  field but `Checkpoint` and `Loras` — and a host running the WebUI adds those two. **`sd-server` loads one checkpoint and
  answers `override_settings` with the render it would have made anyway** (`Automatic1111LiveTests`), so it cannot switch per
  request and a request naming one is refused. It also ignores `<lora:…>` tags and takes LoRAs from a `lora: [{path,
  multiplier}]` payload field (`LoraRequestField = "lora"`), refusing any `path` its `/sdapi/v1/loras` has not listed;
  that listing happens only when asked, so the backend reads it before a render that names a LoRA. It matches `sampler_name`
  in either spelling (`LCM`, `lcm`, `Euler a`, `euler_a`) and **silently ignores one it does not know**.
- **ComfyUI** is template-driven, as for the prompt and the inputs: the host's workflow says where each field goes through
  dotted-path options, the library inserts no node, and a request naming a field whose path is unset or not in the
  workflow is refused at submit with nothing uploaded or queued. A LoRA loader's strengths in the template should be 0, which
  ComfyUI treats as no LoRA, for a request that names none. The checkpoint's field differs by family (`4.inputs.ckpt_name` on
  an SD 1.5 graph, `37.inputs.unet_name` on Z-Image's), so switching across families switches the host's template too; the
  server resolves the name against its own model folders and refuses one it lacks, which the submit reports as `Unsupported`.

**Measured** on the 12 GB RTX 4080 Laptop (`LocalDiffusionLiveTests`, `Automatic1111LiveTests`, `ComfyUiLiveTests`;
checkpoint switching, the LoRA and the seed each a gated live test, skipped without their variables): consecutive requests
switch SD 1.5 and SDXL base on sd-cli (the engine's own log names the file it loaded, and the first checkpoint comes back
bit for bit) and SD 1.5 and Z-Image-Turbo on ComfyUI; the LCM LoRA at 4 steps, CFG 1, seed 7 turns a soft smear into a
recognisable picture on all three (mean neighbouring-pixel difference 3.3 → 9.7 on the engines, 2.0 → 3.9 on ComfyUI, the same
seed, steps and sampler with and without) and costs about 2 to 3 s a request on a warm server, to apply its weights (sd-server
2.5 s → 4.8 s, ComfyUI 4.2 s → 10.8 s; an sd-cli call is a fresh process, so its timings are dominated by the model load and
are not comparable). Measured with the LCM LoRA of SD 1.5 under its own licence; a host reads that licence for the LoRAs it
runs. The local backends apply no content filter, and this adds none.

## 6. Delivery modes

Real backends genuinely differ, and a seam that modelled only one mode would force the others to lie:

| Mode | Interface | Typical of |
|---|---|---|
| Inline | `IModelProvider.GenerateAsync` | image generation |
| Async job | `IMediaJobProvider` (submit → poll → fetch) | video, batch music — renders take minutes |
| Streaming | `IModelProvider.StreamAsync` | text-to-speech, where playback starts before generation ends |

A backend declares which of the three it serves in `ProviderCapabilities.Operations` — data, not a type per
mode (**D127**). Only the stateful job protocol is its own interface, because submit → poll → fetch → cancel
is a contract SHAPE rather than a content type. An async render exposes its **operation id**, so it survives a
process restart and composes with `Lyntai.Jobs`; if your backend delivers by webhook, your app owns the
endpoint and calls `FetchAsync(operationId)` when it fires.

## 7. Pipelines

`RunPipelineAsync` runs a chain: ordered stages, each stage's artifact fed into the next through
`artifact.ToInput(role)`. Every stage carries its own candidates and routes independently, and each is an
ordinary routed call, so spend caps, throttling and dead-host cooldown govern a pipeline exactly as they
govern one render. It drives the **inline** door, so every stage needs an inline backend:

<!-- compile-given: MediaRequest image;
     MediaRequest edit; -->
```csharp
var result = await router.RunPipelineAsync(
[
    new GenerationStage(image, [new ProviderCandidate("openai-images")]),
    new GenerationStage(edit, [new ProviderCandidate("a1111")])
    {
        InputRole = MediaInputRoles.Init,              // the still is the img2img source
    },
]);

if (!result.IsOk)
{
    // nothing is ever re-run, so the still stage 1 already paid for is still here
    var stills = result.Stages[0].Artifacts;
}
```

A stage that cannot identify a single artifact to chain **refuses** (`Unsupported`) rather than guessing — a
media type cannot be branched on, and "the first `image/*`" picks a texture atlas on a mesh backend;
`GenerationStage.SelectInput` is where you state your own rule.

**A mesh chains into an image only through a backend that RASTERIZES it.** That edge is a render, not a
generation, and this library performs none — but a ComfyUI graph with a render node does, as a durable-job
stage (§8): bind the mesh through `Options["input-path"]`, or `Options["input-path:<role>"]` when the stage
sets `InputRole`, and it returns a view of the object.

## 8. Durable renders

**A queued backend (ComfyUI, fal) is not reachable from `RunPipelineAsync`, so run such a chain as a durable
job.** `GenerationPipelineJobHandler` takes each stage through the door of its FIRST capable candidate, in
your order — submitted, checkpointed and polled when that candidate can queue it, rendered inline otherwise —
and through the other door's candidates when the first runs out of them without committing anything. It
delivers every stage to your `IGenerationArtifactSink` as the stage finishes, tagged `StageIndex` (and
`IsFinal` on the last):

<!-- compile-given: IJobQueue jobs; MediaRequest image; MediaRequest video; -->
```csharp
builder.AddJobHandler<GenerationPipelineJobHandler>();   // with your IGenerationArtifactSink registered

var pipeline = new GenerationPipelineJob(
[
    new GenerationPipelineJobStage(["openai-images"], image),
    new GenerationPipelineJobStage(["comfyui"], video) { InputRole = MediaInputRoles.FirstFrame },
]);
await jobs.EnqueueAsync(new JobSpec("render", GenerationPipelineJobHandler.JobType, pipeline.ToJson()));
```

Mind what each backend takes: fal takes its one input only as a URL and refuses bytes (`Unsupported`, nothing
sent), so a stage chaining INTO fal needs a predecessor that returns a URI — OpenAI images returns bytes by
default. ComfyUI uploads bytes or fetches a URI, bound at the field the stage's request names
(`Options["input-path:first-frame"]` here, **D180**).

`GenerationRenderJobHandler` is the one-render form: a single queued stage on the same machine, enqueued with
`GenerationRenderJob.ToJson()`.

**What a restart or a failing sink costs.** A queued stage's operation id is checkpointed before its first
poll, so a restart polls that render rather than submitting another, and a stage's result is checkpointed
before it is delivered, so a sink that throws gets the same artifacts again with no second render, fetch or
charge. Three cases are paid for again: a crash in the instant between a submission returning and its
operation id being saved, which submits again on resume; a crash between a render returning and its
checkpoint; and a result carrying more inline bytes than `GenerationPipelineJobOptions.MaxCheckpointBytes`
(4 MiB), which is delivered without that checkpoint. So store deliveries keyed on the job id AND
`StageIndex`, a redelivery replacing what that key holds: the latest is the one the next stage chained.

A stage says which artifact it chains with `InputMediaType` (`"model/*"` picks a mesh out of its textures)
where `RunPipelineAsync` takes a delegate, because a delegate does not survive a restart. That artifact must
fit `MaxCheckpointBytes` too; past it the job fails and names the stage to move to a backend that returns a
URI.

## 9. Spend, throttling and agents

`AddMediaUsageBudget()` meters what generation costs against the same `BudgetOptions` and `IUsageTracker` as
the LLM front door, so "what has this app spent" stays one number. It is also the ONE switch that records
media spend at all (**D185**): the router, both durable job handlers and `generate_fetch` record only under it
— a text-only `AddUsageBudget()` or a storage package's usage tracking registers an `IUsageTracker` too, and
bills no render. Only COST caps bind a render, and the cap
is checked before a render and before a SUBMISSION — submitting is what commits the money for a hosted video.
`AddMediaRateLimit()` throttles generation on its OWN rate, separate from chat's.

`AddGenerationTools(consumer)` exposes the platform to an agent as tools: `generate_backends` (what exists and
what it serves), `generate` (inline), and `generate_submit` / `generate_status` / `generate_fetch` (the
asynchronous path a video render needs). **Every render those tools start or fetch bills to `consumer`** —
`"agent"` by default, not the platform's `"default"` — so `Budget.PerConsumer["agent"]` caps agent-driven
renders without touching what a user pressing a button may spend:

```csharp
services.AddLyntai(cfg => cfg
    .AddComfyUiProvider(o => { })
    .AddMediaUsageBudget(b => b.PerConsumer["agent"] = new(MaxCostUsd: 5.00))
    .AddGenerationTools());                       // pass a consumer to bill one agent's renders apart
```

A model passes a source image as `imageUrl`, and `imageRole` says what it is to the render: `init` (an edit or
img2img source), `first-frame` (a video's opening frame) or `reference` (a style or subject to follow).
`generate` defaults to `init` and `generate_submit` to `first-frame`; any other value is refused with the
allowed list. Bytes never come back in a tool observation: a registered `IGenerationArtifactSink` receives the
artifacts and the observation says where they went. `generate_backends` probes every backend
**concurrently, under one aggregate `MediaOptions.ProbeDeadline`** (20s); a backend that overruns it or throws
is listed `usable: false` with the reason rather than dropped.

## 10. Backends your users configure

Everything above assumes the *deployment* configures the backends: you call `Add*` once and the container
holds them. If instead an **end user** — or a store your process polls — owns that configuration, the
settings change at any moment, the choice of backend is itself one of those settings, and several
configurations of one backend are live at the same time. Hand the router factory a key and a way to build
each backend:

<!-- compile-skip: per-tenant pseudo-code over the reader's own settings service and router cache -->
```csharp
var cfg = await _settings.ForTenantAsync(tenantId, ct);   // your source; Lyntai never asks where it lives

var openAiKey = ProviderKey.For(cfg.OpenAi.Id)
    .With("baseUrl", cfg.OpenAi.BaseUrl).With("model", cfg.OpenAi.Model)
    .WithSecret("apiKey", cfg.OpenAi.ApiKey)               // hashed into the key, never retained
    .Build();

var localKey = ProviderKey.For(cfg.Local.Id)
    .With("binary", cfg.Local.BinaryPath).With("model", cfg.Local.ModelPath)
    .With("steps", cfg.Local.Steps)
    .Build();

var router = _routers.For([                                // IMediaRouterFactory, injected
    new(openAiKey, () => new OpenAiImageProvider(cfg.OpenAi, _httpFactory, disposeHttpClient: false)),
    new(localKey,  () => new LocalDiffusionProvider(cfg.Local, _runner)),
]);

var result = await router.GenerateAsync(candidates, request, ct);
```

Name every contribution to the key, and include the values the backend resolves at **runtime** as well as
the ones the user typed — a locally-provisioned engine's binary and model paths appear when a download
finishes, at which point the saved settings have not changed at all and an instance holding empty paths
would keep failing forever.

Whether that rebuilds the backend or reuses it is decided **at startup, not at the call site** — the code
above is byte-for-byte the same under either:

```csharp
services.AddLyntai(b => b.UseProviderPool());        // reuse while the key is unchanged (the default)
services.AddLyntai(b => b.UseTransientProviders());  // a fresh instance every call
```

`IProviderPool<TProvider>` is there for a strategy of your own, and the same factory exists for chat
(`ITextRouterFactory`). **Dead-host cooldown and concurrency admission are keyed on the configuration, not on
the backend id** — so one tenant's rate limit never benches another's, while two consumers pointing at the
same self-hosted host do share a bench. A configuration that changes mid-render never aborts it: a replaced
entry is **retired**, not disposed, so in-flight calls finish normally (`docs/DECISIONS.md` D30).

```csharp
services.AddLyntai(b => b.ConfigureProviderAdmission(a => a.BySlot["local-diffusion"] = 1));  // one render at a time
```

## 11. Not in scope, by design

Generation itself, downloading engines or model weights, hosting a webhook endpoint, storing artifacts, or
holding your credentials — see `docs/DECISIONS.md` D20 and D24.
