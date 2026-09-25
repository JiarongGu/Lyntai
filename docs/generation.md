# Generation — backends, delivery, pipelines and durable renders

> **Maintained state.** The guide to the media generation platform as it is TODAY: what each shipped backend
> is, how a request is routed and delivered, and what is and is not verified against a real service. The
> reasoning behind each choice is `docs/DECISIONS.md` (**D24** scope, **D28** input roles, **D64** the
> Inconclusive submit, **D67** the stream door, **D69** an unmeasured mapping is an option, **D127**
> capabilities as data, **D156** one door for every backend, **D181** the durable pipeline); `README.md`
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
disposes a client you supply**: it is yours, and it may be carrying a Polly pipeline or an auth handler. Omit
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

`Lyntai.Generation` ships six (`dotnet add package Lyntai.Generation` — it pulls Core with it). Five are
measured against a real engine or ported from a production implementation; **fal's is written from vendor
documentation and has never been called** (§3).

| Backend | Delivery | Notes |
|---|---|---|
| `OpenAiImageProvider` | Inline | `/images/generations`, or `/images/edits` when the request carries an input image. Shapes ported from a production implementation; **not yet measured against OpenAI's current GPT-image models**, which are reported to reject `response_format`, so that family is sent none by default (`OpenAiImageOptions.ResponseFormat`). A `url` response comes back as a URI artifact — never downloaded for you |
| `Automatic1111Provider` | Inline | A locally-run SD WebUI: `txt2img` / `img2img`, shapes ported from a production implementation. A WebUI that is not running — nothing listening — reports **NotConfigured** (skipped, not blamed); one that drops a render mid-response is **Failed** and counts toward the dead-host threshold. Its probe checks a checkpoint is *loaded* — "up" isn't "usable". The WebUI's loaded checkpoint decides the model: `MediaRequest.Model`, including a candidate's `a1111:sd_xl_base` pin, is **not** sent |
| `ComfyUiProvider` | **Job** | *Measured against a live server, image, video and mesh workflows.* Workflow-driven: you supply the graph in `Options["workflow"]` (+ optional `Options["prompt-path"]` to place the prompt), and outputs come back as view URIs — a mesh as `model/gltf-binary`. Each input (bytes, or a URI it fetches from any http(s) server, capped by `MaxFetchBytes` and never with ComfyUI's credentials off its own origin — validate URIs a model supplies) is uploaded and its stored name written at the field `Options["input-path"]` names, or `Options["input-path:<role>"]` for an input with a role; an input with nowhere to go is refused, never dropped. A transport failure while polling reports **Running, not Failed**, while a 4xx or an unconfigured base URL stays terminal, so a bad id never polls forever |
| `LocalDiffusionProvider` | Inline | A local `sd-cli` / stable-diffusion.cpp subprocess through `IProcessRunner` — no key, no network, no content policy in the path. Argv and the multiple-of-64 size clamp are measured against a real engine (txt2img and img2img, end to end through the library) |
| `FalProvider` | **Job** | *Never called: written from fal.ai's public docs; no maintainer holds an account* (§3). One aggregator queue reaching the Wan/Kling/Veo-class video models. The operation id **carries its model** (`"model#requestId"`) because a resumed job has only the id, and a transport failure while polling reports **Running, not Failed** — a 500 says nothing about a paid render still in flight |
| `PiperProvider` | Inline + **Stream** | A local piper TTS engine through `IProcessRunner` — no key, no network. Raw PCM **streams** through the media stream door as it is synthesised (measured against a real engine: several chunks before one terminal), typed `audio/pcm;rate=…;bits=16;channels=1;endian=little` with the rate read from the voice's own config. `GenerateAsync` is the same stream, buffered |

A backend that catches its own exceptions applies the router's rules itself, so no shipped backend turns a
possibly-delivered submit into a conclusive `Failed`, and none reports a thrown exception as a content
`Refused`.

## 3. fal: documented, not measured

`FalProvider` was written from fal.ai's public queue documentation and **has never been called**, because no
maintainer holds a fal account. Its tests pin this library's behaviour for each documented shape, never that
fal answers in it, so treat the first real run as the verification.

**What a host can correct in configuration** (`FalOptions`, D69): the URL segments (`RequestsSegment`,
`StatusSegment`, `CancelSegment`), the status vocabulary (`StatusVocabulary`), the cost fields (`CostFields`),
the failure fields (`ErrorField` — fal documents a failed request as `COMPLETED` carrying an `error` field,
which polls as `Failed` here — and `ErrorTypeField`), the auth scheme (`AuthScheme`, `Key` by default) and
extra query parameters on every call (`QueryParameters`). Any request field goes through verbatim in
`MediaRequest.Options`. **What it cannot correct**: the response fields read by name — `request_id`, `status`,
`queue_position`, `url` and `content_type` — which a release would have to change.

**Cost**: fal's documented results carry **no cost field**, so unless a `CostFields` name matches,
`MediaUsage.CostUsd` is null and a spend cap never sees these renders. A reported cost is used as given, never
inferred from a rate card.

**Still open against the docs**: whether a model with a sub-path (`fal-ai/flux/dev`) takes its full path in
the status and result URLs, which is what this sends; and a cancel is a REQUEST (202), so `CancelAsync` reports
the render still running and only a poll says how it ended.

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
that also offers a synchronous mode). Its recommendation is to extract a shared internal queue engine from
`FalProvider` only when a second vendor is actually written, and to give each vendor its own provider named for
it.

Until then, a hosted fallback is a backend of your own, registered through `AddProvider`. For a durable render
it must implement `IMediaJobProvider` and declare `ProviderOperation.Queued`, and it helps only at SUBMIT: a
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

Backends declare what they can do in `ProviderCapabilities` — media kinds, input roles, duration ceilings,
model catalogues — and the router **skips a candidate that cannot serve the request** before spending
anything. Fallback is a **policy**, not a law. The default matches the LLM router (a content `Refused`
surfaces rather than being re-submitted elsewhere), but pairing a hosted backend with a locally-run one makes
the other choice reasonable:

```csharp
cfg.ConfigureMediaRouting(p =>
    p.On(ProviderVerdict.Refused, FallbackAction.Advance));   // local backend picks it up
```

Every backend answers **"are you usable?"** without generating anything (`ProbeAsync`), so a setup screen
never pays for a test image.

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
generation, and this library performs none — but a ComfyUI graph with a render node does: bind the mesh
through `Options["input-path"]` and it returns a view of the object.

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
    new GenerationPipelineJobStage(["fal"], video) { InputRole = MediaInputRoles.FirstFrame },
]);
await jobs.EnqueueAsync(new JobSpec("render", GenerationPipelineJobHandler.JobType, pipeline.ToJson()));
```

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
the LLM front door, so "what has this app spent" stays one number. Only COST caps bind a render, and the cap
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
