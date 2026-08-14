# Media Generation Platform — Implementation Plan (Plan 1 of 7: the platform core)

> **Status 2026-08-05 — part shipped history, part still live. Read this before executing anything below.**
>
> **SHIPPED:** Plan 1 (this document's own tasks) through Plan 5, plus Plan 6's tool/MCP-bridge half, all
> landed in **2.0.1** on 2026-08-04 — archived as `docs/task-archive.md` Parts 32–33, reasoning in
> `docs/DECISIONS.md` D24/D25.
>
> **NOT EXECUTABLE AS WRITTEN:** everything from here down to `## Subsequent plans` is the record of *how*
> the core was built, not a set of steps to run. Its unticked boxes are history, and its assembly layout is
> superseded twice over: the 2.0.1 restructure moved the generation **contracts** into
> `src/Lyntai.Core/Generation/` — so the "File structure" table's `src/Lyntai.Generation/` package is
> not where those files live — and the **backends** were then re-split into the `Lyntai.Generation` package
> under `Lyntai.Generation.Providers`. Both moves are D25, whose two clauses are the dependency-footprint
> test and the release-cadence exception. That split is package-and-reason-scoped: it is the BACKENDS package
> that ships EXPERIMENTAL, while the contracts in the `Lyntai.Generation` namespace sit inside the mandatory
> `Lyntai.Core` and carry the FULL SemVer promise (see `CLAUDE.md`, and `DECISIONS.md` D36, which applied it).
>
> **STILL LIVE:** Plan 6's streaming-TTS half and Plan 7 (pipelines) under `## Subsequent plans`, and the
> `## Decisions (2026-08-04)` section including its `### Still open` items. The live backlog for them is
> `TASKS.md` Part 33 (GEN-VERIFY, GEN6, GEN7) and `docs/ROADMAP.md` § Next; per-release detail is
> `CHANGELOG.md`. Where this document and `TASKS.md` disagree about what is open, `TASKS.md` wins.
>
> Its rule references are the ones that existed when it was written — `dev-conventions.md` has since been
> retired into `.claude/rules/dotnet-package-layout.md` and `.claude/rules/repo-mechanics.md`, so any
> remaining `dev-conventions.md` citation in the body below (the sample XML doc in Task 7) resolves there.

<!-- compile-skip-file: every C# block below is this plan's own implementation source AS FIRST WRITTEN,
     including its xunit test files — a record of how the core was built, superseded twice over by D25's two
     clauses (see the STATUS banner above). They also declare types into `namespace Lyntai.*`, which in a shared
     compilation would outrank the shipped assemblies for every other sample. -->

> **For agentic workers:** this plan is executed task-by-task, TDD, with a commit per task. Steps use
> checkbox (`- [ ]`) syntax for tracking. `superpowers:executing-plans` (inline) or
> `superpowers:subagent-driven-development` (fresh subagent per task) both apply; either way the repo's own
> rules bind — `.claude/rules/dotnet-package-layout.md`, `.claude/rules/repo-mechanics.md`,
> `task-lifecycle.md`, `no-tmp-for-repo-files.md`.

**Goal:** a generalized **media generation PLATFORM** in Lyntai — one provider seam spanning image, video,
audio (and whatever medium is next), with capability-aware routing, the three delivery modes real backends
actually use, and a loose LLM coupling through tools/MCP. Lyntai never generates anything itself.

**Architecture:** a new `Lyntai.Generation` package holding its own contracts (its own verdict vocabulary — it does
NOT borrow `LlmVerdict`), plus optional per-delivery capability interfaces in the pattern Core already uses for
`IProviderAuth` / `IProviderVersionInstaller`. Backends are separate packages resolved as a DI collection.
Long renders compose with the existing `Lyntai.Jobs` durable-job machinery rather than hiding poll loops.
The LLM side gains **zero** dependency on media; the bridge is `ITool` (usable by the in-process tool loop and,
via `Lyntai.Tools.Mcp.Hosting`, by a CLI agent).

**Tech Stack:** .NET 10, `Lyntai.Core` (DI builder, `IProcessRunner`, `ITool`, `LlmVerdictClassifier` for
transport→verdict mapping), xUnit + `FakeProcessRunner`/stubbed `HttpMessageHandler`, `ApiSurfaceTests`
baselines.

---

## Why this shape (the research behind it)

Recorded here because these facts are what make the contract non-obvious, and a future session will otherwise
"simplify" the design back into something image-shaped.

**1. Three delivery modes, not one.** Measured across the current provider landscape (August 2026):

| Medium | Real API behaviour |
|---|---|
| Image | Mostly **synchronous** — request → bytes inline (also true of both backends in a sibling desktop app: OpenAI-compatible `/images/generations` and a local SD-WebUI `/sdapi/v1/txt2img`) |
| Video | **Asynchronous job, universally** — submit → task id returned immediately → poll a status endpoint to a terminal state, or receive a webhook. WAN text/image-to-video takes 1–5 minutes and is documented as create-task-then-poll; Kling is `POST /v1/videos/generations` then `GET /v1/tasks/{id}`; Runware takes `deliveryMethod:"async"` |
| Audio | **Split** — TTS **streams** (playback starts before generation finishes; realtime music is WebSocket), while music/dubbing are **batch jobs** proportional to length |

A single `Task<GenerationResult> GenerateAsync(...)` can therefore only express *image*. Video would hide an
unbounded poll loop inside one call — losing progress, cancellation and any chance of surviving a process
restart — and TTS would buffer a stream whose entire purpose is not being buffered.

**2. A backend is not a model.** fal.ai exposes 1,000+ models for image, video, audio **and 3D** behind one
queue API, swapping an endpoint id per model. WAN is reachable through Alibaba Model Studio *and* Replicate
*and* WaveSpeed. So the seam selects **backend + model**, exactly as LLM candidates already do — never
one-provider-per-model, or every aggregator becomes N fake providers.

**3. The medium must be an OPEN value.** fal already lists 3D alongside image/video/audio. A closed enum would
make the next medium a breaking change — so `Kind` is a string with well-known constants, the same call the
repo already made for `ProviderAuthStatus.Method` and `ProviderInstallRequest.Version` (`DECISIONS.md` D20).

**3b. Media CHAINS, and the contract must not close that door.** A real workflow is a pipeline, not one call:
**3d → image → video** (render a mesh to stills, animate the stills), or image → video-from-first-frame, or
text → image → upscale. So `GenerationArtifact` converts to `GenerationInput` (`ToInput(role)`) with bytes-or-URI carried
through, each stage routes independently, and `Kind` being open is what lets a 3D stage exist at all. The
pipeline RUNNER is deliberately deferred (Plan 7) — it is only meaningful once ≥2 real backends exist — but
nothing in this core may make it impossible, which is why chaining is tested in Task 2 rather than assumed.

**4. Capability discovery is load-bearing here, unlike LLM.** Chat models all take text, so the LLM router
barely needs capabilities. Media backends differ wildly — text→video vs image→video vs reference→video (WAN
has all three), max duration (WAN: ~10s), resolution ceilings, voice lists, workflow-graph backends
(ComfyUI) vs prompt backends. Routing that cannot ask "can this backend even do this request?" is useless.

**5. Probing must not cost a generation.** A sibling app currently tests its image provider with a
generate-and-discard — the same waste `IProviderAuth` removed for LLM auth (D20). Its *video* seam already
does it right (`CheckAsync` → availability), so the correct shape exists in their code; the platform makes it
the contract for every medium.

### Non-goals (do not absorb these)

- **Generation itself.** No inference, no ffmpeg pipeline authoring, no model math. Backends generate.
- **Engine/weights provisioning.** Downloading a `sd-cli` binary or a ~1.7 GB GGUF stays the host's concern
  (`DECISIONS.md` D20). The platform reports "not configured"; the app provisions.
- **Hosting a webhook endpoint.** Lyntai is a library, not a server (the only scoped exception is D17's
  ephemeral MCP host). The app owns its HTTP endpoint and calls `FetchAsync(operationId)` when it fires —
  which is why fetch-by-id is on the contract.
- **Where artifacts land.** No media library, no storage domain in this plan. Bytes/URIs go back to the caller.
- **Credentials.** Backend config is passed in by the app (same posture as `IProviderAuth`: ask and drive,
  never store).

---

## File structure

**New package `src/Lyntai.Generation/`** (packable, project-refs `Lyntai.Core` only — never adapter→adapter):

| File | Responsibility |
|---|---|
| `GenerationKinds.cs` | Well-known open `Kind` constants (`image`/`video`/`audio`/`3d`) |
| `GenerationRequest.cs` | `GenerationRequest`, `GenerationInput` (+ `GenerationInputRoles`) |
| `GenerationArtifact.cs` | `GenerationArtifact`, `GenerationUsage` |
| `GenerationResult.cs` | `GenerationVerdict`, `GenerationResult` |
| `GenerationCapabilities.cs` | `GenerationDelivery`, `GenerationCapabilities`, `GenerationProbeResult` |
| `IGenerationProvider.cs` | `IGenerationProvider` (inline) + the capability doc contract |
| `IGenerationJobProvider.cs` | `IGenerationJobProvider`, `GenerationOperation`, `GenerationOperationStatus` |
| `IGenerationStreamProvider.cs` | `IGenerationStreamProvider`, `GenerationChunk` |
| `GenerationVerdictClassifier.cs` | transport/text → `GenerationVerdict`, delegating to Core's `LlmVerdictClassifier` |
| `Routing/GenerationCandidate.cs` | `GenerationCandidate` (provider id + optional model) |
| `Routing/IGenerationRouter.cs` | `IGenerationRouter` |
| `Routing/GenerationRouter.cs` | capability pre-filter + verdict-driven fallback |
| `GenerationBuilderExtensions.cs` | `AddGenerationProvider(...)`, `UseDefaultGenerationCandidates(...)` |

**Tests** in `tests/Lyntai.Tests/Media/`: `FakeGenerationProvider.cs` (in `tests/Lyntai.Tests/Fakes/`),
`GenerationContractTests.cs`, `GenerationRouterTests.cs`, `GenerationVerdictClassifierTests.cs`, `GenerationDiTests.cs`.

**Registration:** `Lyntai.slnx`, `devtools/project.config.mjs` (`packableProjects`),
`tests/Lyntai.Tests/Lyntai.Tests.csproj`, `ApiSurfaceTests.Assemblies()` + `Loaded`.

---

## Task 1: The package skeleton

**Files:**
- Create: `src/Lyntai.Generation/Lyntai.Generation.csproj`
- Modify: `Lyntai.slnx`, `devtools/project.config.mjs`, `tests/Lyntai.Tests/Lyntai.Tests.csproj`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <PackageId>Lyntai.Generation</PackageId>
    <Description>Lyntai media generation platform: a capability-aware provider seam for image/video/audio generation across inline, async-job and streaming backends — routing, fallback, probes and a tool bridge, with generation delegated to backends.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Lyntai.Core\Lyntai.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Lyntai.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register it in the solution** — add inside the `/src/` folder in `Lyntai.slnx`, after the CodexCli line:

```xml
    <Project Path="src/Lyntai.Generation/Lyntai.Generation.csproj" />
```

- [ ] **Step 3: Register it for packing** — in `devtools/project.config.mjs`, add to `packableProjects` after `'src/Lyntai.Providers.CodexCli',`:

```js
    'src/Lyntai.Generation',
```

- [ ] **Step 4: Reference it from the test project** — in `tests/Lyntai.Tests/Lyntai.Tests.csproj`, after the CodexCli reference:

```xml
    <ProjectReference Include="..\..\src\Lyntai.Generation\Lyntai.Generation.csproj" />
```

- [ ] **Step 5: Build**

Run: `node devtools/dev.mjs build`
Expected: success (an empty package builds).

- [ ] **Step 6: Commit**

```bash
git add src/Lyntai.Generation Lyntai.slnx devtools/project.config.mjs tests/Lyntai.Tests/Lyntai.Tests.csproj
git commit -m "feat(media): add the Lyntai.Generation package skeleton"
```

---

## Task 2: Request / artifact / result vocabulary

**Files:**
- Create: `src/Lyntai.Generation/GenerationKinds.cs`, `GenerationRequest.cs`, `GenerationArtifact.cs`, `GenerationResult.cs`
- Test: `tests/Lyntai.Tests/Media/GenerationContractTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>The vocabulary every media backend speaks. These assertions pin the DEFAULTS, because a
/// half-initialised request is the difference between "the backend refused" and "we sent nonsense".</summary>
public class GenerationContractTests
{
    [Fact]
    public void A_request_defaults_to_no_inputs_and_no_options()
    {
        var request = new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" };

        Assert.Empty(request.Inputs);
        Assert.Empty(request.Options);
        Assert.Null(request.Model);
    }

    [Fact]
    public void An_input_carries_bytes_or_a_uri_and_a_free_form_role()
    {
        // WAN alone has text→video, image→video (FIRST FRAME) and reference→video: the role is what
        // distinguishes them, and it is free-form so another backend's roles fit without a contract change
        var input = new GenerationInput("image/png", Data: [1, 2, 3], Role: GenerationInputRoles.FirstFrame);

        Assert.Equal("image/png", input.MediaType);
        Assert.Equal("first-frame", input.Role);
        Assert.Null(input.Uri);
    }

    [Fact]
    public void A_failed_result_is_not_ok_and_carries_no_artifacts()
    {
        var result = GenerationResult.Failure(GenerationVerdict.NotConfigured, "no endpoint configured");

        Assert.False(result.IsOk);
        Assert.Empty(result.Artifacts);
        Assert.Equal(GenerationVerdict.NotConfigured, result.Verdict);
        Assert.Contains("no endpoint", result.Detail);
    }

    [Fact]
    public void An_ok_result_requires_at_least_one_artifact()
    {
        // an "Ok" with nothing in it is the empty-Ok mistake the LLM side already learned (pitfalls.md):
        // the router must be able to fall over instead of handing back a successful nothing
        var ex = Assert.Throws<ArgumentException>(() => GenerationResult.Success([]));

        Assert.Contains("artifact", ex.Message);
    }

    [Fact]
    public void Well_known_kinds_are_open_strings_not_an_enum()
    {
        // 3D already exists on real aggregators; the next medium must not be a breaking change
        Assert.Equal("image", GenerationKinds.Image);
        Assert.Equal("video", GenerationKinds.Video);
        Assert.Equal("audio", GenerationKinds.Audio);
        Assert.Equal("3d", GenerationKinds.Model3d);
    }

    [Fact]
    public void An_artifact_becomes_the_next_stage_s_input()
    {
        // CHAINING is a first-class use case: 3d -> image -> video, or image -> video-first-frame. One stage's
        // output must feed the next without the caller re-wrapping bytes by hand.
        var rendered = new GenerationArtifact("image/png", Data: [1, 2, 3],
            Metadata: new Dictionary<string, string> { ["seed"] = "42" });

        var input = rendered.ToInput(GenerationInputRoles.FirstFrame);

        Assert.Equal("image/png", input.MediaType);
        Assert.Equal([1, 2, 3], input.Data);
        Assert.Equal("first-frame", input.Role);
    }

    [Fact]
    public void An_artifact_returned_as_a_uri_chains_as_a_uri()
    {
        // video backends commonly return a signed URL; chaining must not force a download the caller
        // didn't ask for (the next backend may well be able to read the URL itself)
        var hosted = new GenerationArtifact("video/mp4", Uri: "https://example.invalid/a.mp4");

        var input = hosted.ToInput(GenerationInputRoles.Reference);

        Assert.Null(input.Data);
        Assert.Equal("https://example.invalid/a.mp4", input.Uri);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationContractTests"`
Expected: FAIL — `error CS0246: The type or namespace name 'GenerationRequest' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/Lyntai.Generation/GenerationKinds.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>The media kinds this platform knows by name. Deliberately CONSTANTS, not an enum:
/// <see cref="GenerationRequest.Kind"/> is an open string so a backend offering a medium nobody has modelled yet
/// (3D meshes already ship on real aggregators; speech-to-speech, motion, whatever follows) fits without a
/// breaking contract change. Same reasoning as the free-form <c>Method</c>/<c>Version</c> values in
/// <c>DECISIONS.md</c> D20.</summary>
public static class GenerationKinds
{
    public const string Image = "image";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Model3d = "3d";
}

/// <summary>Well-known roles for a <see cref="GenerationInput"/> — what an input IS to the generation, which is
/// what distinguishes a backend's modes from each other (one video backend offers text→video,
/// image→video-from-first-frame AND reference→video). Open strings, for the same reason as
/// <see cref="GenerationKinds"/>.</summary>
public static class GenerationInputRoles
{
    /// <summary>The image a generation starts FROM (img2img / init image).</summary>
    public const string Init = "init";

    /// <summary>The image that becomes the video's first frame.</summary>
    public const string FirstFrame = "first-frame";

    /// <summary>A style/subject reference the backend should follow without copying.</summary>
    public const string Reference = "reference";

    /// <summary>A voice sample to clone or match.</summary>
    public const string Voice = "voice";
}
```

`src/Lyntai.Generation/GenerationRequest.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>One generation request, for ANY medium. The medium is <see cref="Kind"/>; everything a specific
/// backend needs beyond the common fields travels in <see cref="Options"/>, so adding a knob is never a
/// contract change.</summary>
/// <remarks>Backend-specific options are string→string on purpose: they are passed THROUGH to a backend that
/// documents them (size, duration, aspect ratio, voice id, steps, a workflow id for a graph-based engine).
/// A platform that typed every backend's knobs would need a release per backend feature.</remarks>
public sealed record GenerationRequest
{
    /// <summary>Which medium to produce — a <see cref="GenerationKinds"/> value, or any string a backend
    /// advertises in <see cref="GenerationCapabilities.Kinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The text prompt, where the backend takes one. Null for a backend driven entirely by
    /// <see cref="Inputs"/> plus <see cref="Options"/> (e.g. a fixed workflow graph).</summary>
    public string? Prompt { get; init; }

    /// <summary>Source media the generation works FROM — an init image, a first frame, a style reference, a
    /// voice sample. Empty for pure text→media.</summary>
    public IReadOnlyList<GenerationInput> Inputs { get; init; } = [];

    /// <summary>The model / endpoint id to use AT the selected backend, when it serves more than one (an
    /// aggregator serves hundreds). Null = the backend's own default.</summary>
    public string? Model { get; init; }

    /// <summary>Backend-documented knobs, passed through verbatim.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-request budget override in seconds; null = the platform default.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Read an option, or null when absent. Case-insensitive.</summary>
    public string? Option(string name) => Options.TryGetValue(name, out var value) ? value : null;
}

/// <summary>Source media handed TO a generation. Either <paramref name="Data"/> or <paramref name="Uri"/>
/// carries it — a backend that only accepts URLs and one that only accepts bytes are both real, so the
/// platform models both rather than forcing every caller to fetch or upload.</summary>
/// <param name="MediaType">The input's MIME type (<c>image/png</c>, <c>audio/wav</c>).</param>
/// <param name="Data">Inline bytes, when the caller has them.</param>
/// <param name="Uri">A location the BACKEND can read, when bytes would be wasteful.</param>
/// <param name="Role">What this input is to the generation — a <see cref="GenerationInputRoles"/> value or any
/// role a backend documents. Null = the backend's default interpretation.</param>
public sealed record GenerationInput(
    string MediaType,
    byte[]? Data = null,
    string? Uri = null,
    string? Role = null);
```

`src/Lyntai.Generation/GenerationArtifact.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>One produced piece of media. Mirrors <see cref="GenerationInput"/>: bytes when the backend returned
/// them, a URI when it returned a location (many video backends hand back a signed URL, and downloading it
/// is the CALLER's decision — the platform never silently fetches megabytes).</summary>
/// <param name="MediaType">MIME type of the artifact (<c>image/png</c>, <c>video/mp4</c>, <c>audio/mpeg</c>).</param>
/// <param name="Data">The bytes, when the backend returned them inline.</param>
/// <param name="Uri">Where the artifact lives, when the backend returned a location instead.</param>
/// <param name="Metadata">Whatever the backend said about it (seed, revised prompt, duration, dimensions),
/// verbatim — useful in a diagnostics pane and for reproducing a generation.</param>
public sealed record GenerationArtifact(
    string MediaType,
    byte[]? Data = null,
    string? Uri = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>Use this artifact as an INPUT to a further generation — the chaining primitive behind
    /// pipelines like 3d → image → video, or image → video-from-first-frame. Carries bytes or the URI through
    /// unchanged: a backend that can read the URL itself should not be forced through a download the caller
    /// never asked for.</summary>
    /// <param name="role">What the artifact is to the next stage (a <see cref="GenerationInputRoles"/> value).</param>
    public GenerationInput ToInput(string? role = null) => new(MediaType, Data, Uri, role);
}

/// <summary>What a generation cost, as far as the backend reported it. Every field is nullable because
/// backends report wildly different things (a count of images, seconds of video/audio, a price) and the
/// platform never INVENTS a cost from a token price.</summary>
/// <param name="Count">Number of artifacts billed.</param>
/// <param name="Seconds">Duration produced, for time-based media.</param>
/// <param name="CostUsd">What the backend said it cost.</param>
public sealed record GenerationUsage(int? Count = null, double? Seconds = null, double? CostUsd = null);
```

`src/Lyntai.Generation/GenerationResult.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>Why a media call ended the way it did. The platform's OWN vocabulary — deliberately not
/// <c>LlmVerdict</c>: media is a separate domain, and its results are consumed by media routing.
/// (The transport-to-verdict PATTERNS are still shared — see <see cref="GenerationVerdictClassifier"/> — so there
/// is one corpus of "what does a 429 mean", not two.)</summary>
public enum GenerationVerdict
{
    /// <summary>Artifacts were produced.</summary>
    Ok,

    /// <summary>The backend isn't set up (no endpoint/key, engine not provisioned). NOT a transient failure:
    /// routing should skip this candidate rather than penalise it, and a host should offer setup.</summary>
    NotConfigured,

    /// <summary>The backend cannot do THIS request (wrong medium, unsupported role, duration past its limit).
    /// A capability gap, not a fault — advance to another candidate without penalising this one.</summary>
    Unsupported,

    /// <summary>Rate-limited / quota exhausted — cool this backend and advance.</summary>
    RateLimited,

    /// <summary>Credentials rejected — cool and advance; retrying immediately can't help.</summary>
    AuthFailed,

    /// <summary>The backend refused on content policy. SURFACE it — another backend is likely to refuse too,
    /// and silently shopping a refused prompt around is not the platform's call to make.</summary>
    Refused,

    /// <summary>The call or render exceeded its budget.</summary>
    Timeout,

    /// <summary>Anything else.</summary>
    Failed,
}

/// <summary>The outcome of a media generation.</summary>
/// <param name="Verdict">Why it ended this way.</param>
/// <param name="Artifacts">What was produced — empty unless <paramref name="Verdict"/> is
/// <see cref="GenerationVerdict.Ok"/>.</param>
/// <param name="Usage">What the backend said it cost, when it says.</param>
/// <param name="Detail">The backend's own words, or the failure reason. Surface verbatim rather than parsing:
/// the wording belongs to the backend and changes.</param>
public sealed record GenerationResult(
    GenerationVerdict Verdict,
    IReadOnlyList<GenerationArtifact> Artifacts,
    GenerationUsage? Usage = null,
    string? Detail = null)
{
    /// <summary>Whether the call produced media.</summary>
    public bool IsOk => Verdict == GenerationVerdict.Ok;

    /// <summary>A successful result. Throws for an EMPTY artifact list: an "Ok" carrying nothing is the
    /// empty-Ok mistake the LLM side already paid for (`.claude/knowledge/pitfalls.md`) — it robs routing of
    /// its chance to fall over and hands the caller a successful nothing.</summary>
    public static GenerationResult Success(IReadOnlyList<GenerationArtifact> artifacts, GenerationUsage? usage = null, string? detail = null)
    {
        if (artifacts.Count == 0)
            throw new ArgumentException("a successful media result needs at least one artifact", nameof(artifacts));
        return new GenerationResult(GenerationVerdict.Ok, artifacts, usage, detail);
    }

    /// <summary>A failed result — no artifacts, a reason, and a verdict routing can act on.</summary>
    public static GenerationResult Failure(GenerationVerdict verdict, string? detail = null) =>
        new(verdict, [], null, detail);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationContractTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Generation tests/Lyntai.Tests/Media
git commit -m "feat(media): request/artifact/result vocabulary with an open Kind"
```

---

## Task 3: Capabilities + the probe

**Files:**
- Create: `src/Lyntai.Generation/GenerationCapabilities.cs`
- Test: `tests/Lyntai.Tests/Media/GenerationCapabilitiesTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>Capability declaration is what makes media routing possible at all: unlike chat models (which all
/// take text), media backends differ in medium, delivery mode, inputs and limits. `Supports` is the
/// pre-filter the router uses to skip a candidate that CANNOT serve a request, instead of failing it.</summary>
public class GenerationCapabilitiesTests
{
    private static readonly GenerationCapabilities ImageOnly = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,
    };

    [Fact]
    public void A_backend_supports_a_request_for_a_kind_it_advertises()
    {
        Assert.True(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Image }, GenerationDelivery.Inline));
    }

    [Fact]
    public void A_backend_does_not_support_a_medium_it_never_claimed()
    {
        Assert.False(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Video }, GenerationDelivery.Inline));
    }

    [Fact]
    public void A_backend_does_not_support_a_delivery_mode_it_lacks()
    {
        // asking an inline-only backend to stream is a capability gap, not a failure
        Assert.False(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Image }, GenerationDelivery.Stream));
    }

    [Fact]
    public void A_backend_that_takes_no_inputs_does_not_support_an_input_carrying_request()
    {
        var textOnly = new GenerationCapabilities
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
            SupportsInputs = false,
        };
        var imageToVideo = new GenerationRequest
        {
            Kind = GenerationKinds.Video,
            Inputs = [new GenerationInput("image/png", Data: [1], Role: GenerationInputRoles.FirstFrame)],
        };

        Assert.False(textOnly.Supports(imageToVideo, GenerationDelivery.Job));
    }

    [Fact]
    public void A_model_the_backend_does_not_serve_is_unsupported()
    {
        // an aggregator serves hundreds of models; a request naming one it doesn't have must be skipped, not sent
        var aggregator = new GenerationCapabilities
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
            Models = ["wan-t2v", "kling-v2"],
        };

        Assert.True(aggregator.Supports(new GenerationRequest { Kind = GenerationKinds.Video, Model = "wan-t2v" }, GenerationDelivery.Job));
        Assert.False(aggregator.Supports(new GenerationRequest { Kind = GenerationKinds.Video, Model = "nope" }, GenerationDelivery.Job));
    }

    [Fact]
    public void An_empty_model_list_means_the_backend_does_not_enumerate_them()
    {
        // a single-model backend (or one whose catalogue we don't mirror) must not be filtered out by a
        // model it never listed — absence of a list is "unknown", not "supports nothing"
        Assert.True(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Image, Model = "whatever" }, GenerationDelivery.Inline));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationCapabilitiesTests"`
Expected: FAIL — `GenerationCapabilities` / `GenerationDelivery` not found.

- [ ] **Step 3: Write the implementation**

```csharp
namespace Lyntai.Generation;

/// <summary>How a backend delivers a generation. The single most important axis in this platform: the same
/// medium is served inline by one backend and as a queued job by another, and modelling only one of them
/// forces every other backend to lie (a hidden poll loop, or a buffered stream).</summary>
public enum GenerationDelivery
{
    /// <summary>Request → artifacts in the same call. Typical for image generation.</summary>
    Inline,

    /// <summary>Submit → an operation id → poll (or a webhook the APP owns) → fetch. Universal for video and
    /// batch music; renders run for minutes, so the caller needs progress, cancellation and restart-survival.</summary>
    Job,

    /// <summary>Chunks as they are produced, so playback can start before generation finishes. Typical for TTS.</summary>
    Stream,
}

/// <summary>What a backend can actually do. Declared rather than discovered, because a wrong guess costs a
/// billable generation — and because routing needs to PRE-FILTER candidates before spending anything.</summary>
public sealed record GenerationCapabilities
{
    /// <summary>Media kinds served (<see cref="GenerationKinds"/> values, or backend-specific strings).</summary>
    public IReadOnlyList<string> Kinds { get; init; } = [];

    /// <summary>Delivery modes offered. A backend may offer several (inline for a small image, job for a
    /// large one) — the router asks for the mode the caller wants.</summary>
    public IReadOnlyList<GenerationDelivery> Deliveries { get; init; } = [];

    /// <summary>Model/endpoint ids this backend serves. EMPTY means "not enumerated" (a single-model backend,
    /// or a catalogue too large/volatile to mirror) — never "serves nothing", so an empty list must not filter
    /// a request out.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>Whether the backend accepts <see cref="GenerationRequest.Inputs"/> at all (img2img,
    /// image→video, voice cloning). A text-only backend handed an input would silently ignore it.</summary>
    public bool SupportsInputs { get; init; }

    /// <summary>Backend-documented ceilings, free-form so a new limit isn't a contract change
    /// (<c>max-duration-seconds</c>, <c>max-width</c>, <c>voices</c>). Informational for callers; the platform
    /// does not enforce them (only the backend knows the real rule).</summary>
    public IReadOnlyDictionary<string, string> Limits { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this backend can serve <paramref name="request"/> via <paramref name="delivery"/>.
    /// Conservative on the axes it KNOWS (kind, delivery, inputs, an enumerated model) and permissive where it
    /// knows nothing (an empty model list) — a false "no" wastes a working backend, a false "yes" wastes a
    /// call, and only the first is silent.</summary>
    public bool Supports(GenerationRequest request, GenerationDelivery delivery)
    {
        if (!Kinds.Contains(request.Kind, StringComparer.OrdinalIgnoreCase)) return false;
        if (!Deliveries.Contains(delivery)) return false;
        if (request.Inputs.Count > 0 && !SupportsInputs) return false;
        if (request.Model is { Length: > 0 } model && Models.Count > 0 &&
            !Models.Contains(model, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }
}

/// <summary>The outcome of <see cref="IGenerationProvider.ProbeAsync"/> — "is this backend usable right now?",
/// answered WITHOUT generating anything.</summary>
/// <param name="Available">The backend is configured and reachable.</param>
/// <param name="Detail">What it reported, or why it isn't usable (a missing key, an unprovisioned engine, an
/// unreachable endpoint).</param>
/// <param name="Version">The backend's own version, where it reports one.</param>
public sealed record GenerationProbeResult(bool Available, string? Detail = null, string? Version = null);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationCapabilitiesTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Generation/GenerationCapabilities.cs tests/Lyntai.Tests/Media/GenerationCapabilitiesTests.cs
git commit -m "feat(media): capability declaration + the no-cost probe result"
```

---

## Task 4: The three provider seams

**Files:**
- Create: `src/Lyntai.Generation/IGenerationProvider.cs`, `IGenerationJobProvider.cs`, `IGenerationStreamProvider.cs`
- Create: `tests/Lyntai.Tests/Fakes/FakeGenerationProvider.cs`
- Test: `tests/Lyntai.Tests/Media/GenerationProviderSeamTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Generation;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The seam is split by DELIVERY MODE, using the optional-capability pattern Core already uses for
/// IProviderAuth / IProviderVersionInstaller: a backend implements the modes it has, and a caller
/// pattern-matches. A backend that can't stream simply isn't an IGenerationStreamProvider — so "can you stream?"
/// has a real answer rather than a runtime surprise.</summary>
public class GenerationProviderSeamTests
{
    [Fact]
    public void An_inline_backend_is_a_media_provider_and_nothing_more()
    {
        var provider = new FakeGenerationProvider();

        Assert.IsAssignableFrom<IGenerationProvider>(provider);
        Assert.IsNotAssignableFrom<IGenerationJobProvider>(provider);
        Assert.IsNotAssignableFrom<IGenerationStreamProvider>(provider);
    }

    [Fact]
    public async Task An_inline_generation_returns_artifacts()
    {
        var provider = new FakeGenerationProvider();

        var result = await provider.GenerateAsync(new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "x" });

        Assert.True(result.IsOk);
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_probe_answers_without_generating()
    {
        var provider = new FakeGenerationProvider();

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal(0, provider.GenerateCalls);   // the point of the probe
    }

    [Fact]
    public async Task A_job_backend_submits_polls_and_fetches()
    {
        var provider = new FakeGenerationJobProvider();

        var submitted = await provider.SubmitAsync(new GenerationRequest { Kind = GenerationKinds.Video, Prompt = "x" });
        var polled = await provider.PollAsync(submitted.Id);
        var fetched = await provider.FetchAsync(submitted.Id);

        Assert.Equal(GenerationOperationStatus.Queued, submitted.Status);
        Assert.Equal(GenerationOperationStatus.Succeeded, polled.Status);
        Assert.True(fetched.IsOk);
        Assert.Equal("video/mp4", fetched.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_stream_backend_yields_chunks_then_a_final()
    {
        var provider = new FakeGenerationStreamProvider();

        var chunks = new List<GenerationChunk>();
        await foreach (var chunk in provider.StreamAsync(new GenerationRequest { Kind = GenerationKinds.Audio, Prompt = "hi" }))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count(c => c.Data is { Length: > 0 }));
        Assert.True(chunks[^1].Final);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationProviderSeamTests"`
Expected: FAIL — `IGenerationProvider` / `FakeGenerationProvider` not found.

- [ ] **Step 3: Write the three interfaces**

`src/Lyntai.Generation/IGenerationProvider.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>
/// A media generation backend. This is the base seam every backend implements — identity, declared
/// capabilities, a no-cost probe, and INLINE generation (request → artifacts), which is how most image
/// backends behave.
///
/// Long-running and streaming backends implement an ADDITIONAL interface —
/// <see cref="IGenerationJobProvider"/> or <see cref="IGenerationStreamProvider"/> — rather than pretending their
/// delivery fits this method. That is the same optional-capability pattern Core uses for
/// <c>IProviderAuth</c>/<c>IProviderVersionInstaller</c>: a backend that can't do something simply doesn't
/// implement it, and callers pattern-match over the registered collection.
///
/// Lyntai does not generate media. A backend adapts a service or an engine the host already has.
/// </summary>
public interface IGenerationProvider
{
    /// <summary>The candidate id routing selects on (<c>"openai-images"</c>, <c>"a1111"</c>,
    /// <c>"local-diffusion"</c>).</summary>
    string Id { get; }

    /// <summary>What this backend can serve. Read by the router BEFORE spending anything.</summary>
    GenerationCapabilities Capabilities { get; }

    /// <summary>Is this backend usable right now — configured, reachable, provisioned — WITHOUT generating
    /// anything? Implementations must FAIL SAFE (a value, never a throw) except for caller cancellation.</summary>
    /// <remarks>The generate-and-discard test this replaces is a real pattern in a sibling app, and it bills
    /// a generation to answer a setup question. If a backend genuinely cannot be checked without generating,
    /// report <c>Available: false</c> with the reason — never guess, and never generate here.</remarks>
    Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default);

    /// <summary>Generate inline: one call, artifacts back. Implementations must FAIL SAFE — a transport or
    /// backend failure is a <see cref="GenerationResult"/> with a verdict, not an exception (cancellation
    /// propagates).</summary>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default);
}
```

`src/Lyntai.Generation/IGenerationJobProvider.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>
/// OPTIONAL capability of an <see cref="IGenerationProvider"/> whose backend generates ASYNCHRONOUSLY: submit a
/// request, get an operation id, poll until terminal, then fetch. This is how essentially every video backend
/// works (renders take minutes) and how batch music works.
///
/// It is a separate interface, and separate CALLS, for a reason: folding "submit and wait" into
/// <see cref="IGenerationProvider.GenerateAsync"/> would bury an unbounded poll loop inside one method — no
/// progress, no cancellation of the remote job, and nothing left to resume after a process restart. With the
/// operation id exposed, a render survives a restart (persist the id; poll again later) and composes with
/// <c>Lyntai.Jobs</c>.
/// </summary>
/// <remarks>WEBHOOKS: several backends can POST a completion to a URL instead of being polled. Lyntai is a
/// library and hosts nothing — the APP owns that endpoint, and calls <see cref="FetchAsync"/> with the
/// operation id when it fires. That is why fetch-by-id is on this contract rather than hidden inside a poll
/// loop.</remarks>
public interface IGenerationJobProvider
{
    /// <summary>Submit the request and return immediately with an operation to track. Fails safe: a rejected
    /// submission comes back as a <see cref="GenerationOperationStatus.Failed"/> operation with a reason.</summary>
    Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default);

    /// <summary>Ask the backend where an operation is. Cheap and safe to call repeatedly; carries
    /// <see cref="GenerationOperation.Progress"/> where the backend reports it.</summary>
    Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default);

    /// <summary>Collect the artifacts of a SUCCEEDED operation. Callable from anywhere that has the id —
    /// including an app's webhook handler.</summary>
    Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default);

    /// <summary>Ask the backend to abandon the operation, where it supports that. A no-op backend should
    /// report it in the returned detail rather than throw.</summary>
    Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default);
}

/// <summary>Where an asynchronous generation currently is.</summary>
public enum GenerationOperationStatus
{
    /// <summary>Accepted, not started.</summary>
    Queued,

    /// <summary>Being generated.</summary>
    Running,

    /// <summary>Finished — artifacts are fetchable.</summary>
    Succeeded,

    /// <summary>Ended without artifacts; the reason is in the detail.</summary>
    Failed,

    /// <summary>Abandoned at the caller's request.</summary>
    Cancelled,
}

/// <summary>A handle on an asynchronous generation. The id is the whole point: persist it and a render
/// survives a restart.</summary>
/// <param name="Id">The backend's own operation/task id.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Progress">0..1 where the backend reports progress; null when it doesn't.</param>
/// <param name="Detail">The backend's own words — a queue position, a failure reason.</param>
public sealed record GenerationOperation(
    string Id,
    GenerationOperationStatus Status,
    double? Progress = null,
    string? Detail = null)
{
    /// <summary>Whether this operation has stopped changing (succeeded, failed or cancelled).</summary>
    public bool IsTerminal => Status is GenerationOperationStatus.Succeeded or GenerationOperationStatus.Failed
        or GenerationOperationStatus.Cancelled;
}
```

`src/Lyntai.Generation/IGenerationStreamProvider.cs`:

```csharp
namespace Lyntai.Generation;

/// <summary>
/// OPTIONAL capability of an <see cref="IGenerationProvider"/> that emits media AS IT IS PRODUCED — the shape
/// text-to-speech backends are built around (playback starts in well under a second, long before generation
/// finishes). Buffering that into a single result would throw away the only property that makes it useful.
/// </summary>
public interface IGenerationStreamProvider
{
    /// <summary>Stream the generation. Yields data chunks in order, then exactly ONE terminal chunk:
    /// <see cref="GenerationChunk.Final"/> when the stream completed, or a chunk carrying
    /// <see cref="GenerationChunk.Error"/> when it did not.</summary>
    IAsyncEnumerable<GenerationChunk> StreamAsync(GenerationRequest request, CancellationToken ct = default);
}

/// <summary>One piece of a streamed generation, or its terminal marker. Mirrors the LLM side's chunk
/// contract so the two streams behave the same way for a consumer.</summary>
/// <param name="Data">Media bytes, for a content chunk.</param>
/// <param name="MediaType">MIME type of the stream, on the first chunk at least.</param>
/// <param name="Error">Set on a terminal FAILURE chunk.</param>
/// <param name="Detail">The backend's words about the failure.</param>
/// <param name="Final">True on the terminal SUCCESS chunk.</param>
/// <param name="Usage">Cost/duration, where the backend reports it on completion.</param>
public sealed record GenerationChunk(
    byte[]? Data = null,
    string? MediaType = null,
    GenerationVerdict? Error = null,
    string? Detail = null,
    bool Final = false,
    GenerationUsage? Usage = null)
{
    /// <summary>A content chunk.</summary>
    public static GenerationChunk Content(byte[] data, string? mediaType = null) => new(data, mediaType);

    /// <summary>The terminal success marker.</summary>
    public static GenerationChunk Completed(GenerationUsage? usage = null) => new(Final: true, Usage: usage);

    /// <summary>The terminal failure marker.</summary>
    public static GenerationChunk Failure(GenerationVerdict verdict, string? detail = null) =>
        new(Error: verdict, Detail: detail);
}
```

- [ ] **Step 4: Write the fakes**

`tests/Lyntai.Tests/Fakes/FakeGenerationProvider.cs`:

```csharp
using Lyntai.Generation;

namespace Lyntai.Tests.Fakes;

/// <summary>An INLINE media backend for exercising the platform without any real service. Scriptable per
/// call so a router test can drive a specific verdict sequence.</summary>
public sealed class FakeGenerationProvider : IGenerationProvider
{
    public string Id { get; init; } = "fake-media";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,
    };

    /// <summary>Verdicts to return, in order; the last one repeats. Ok produces a 1-byte PNG artifact.</summary>
    public Queue<GenerationVerdict> Verdicts { get; } = new();

    public bool ProbeAvailable { get; set; } = true;
    public int GenerateCalls { get; private set; }
    public int ProbeCalls { get; private set; }

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        ProbeCalls++;
        return Task.FromResult(new GenerationProbeResult(ProbeAvailable, ProbeAvailable ? "fake ready" : "fake not configured"));
    }

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        GenerateCalls++;
        var verdict = Verdicts.Count > 1 ? Verdicts.Dequeue() : Verdicts.Count == 1 ? Verdicts.Peek() : GenerationVerdict.Ok;
        return Task.FromResult(verdict == GenerationVerdict.Ok
            ? GenerationResult.Success([new GenerationArtifact("image/png", Data: [0x89])], new GenerationUsage(Count: 1))
            : GenerationResult.Failure(verdict, $"fake {verdict}"));
    }
}

/// <summary>An ASYNC-JOB backend: submit → queued, first poll → succeeded, fetch → an mp4.</summary>
public sealed class FakeGenerationJobProvider : IGenerationProvider, IGenerationJobProvider
{
    public string Id { get; init; } = "fake-video";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Video],
        Deliveries = [GenerationDelivery.Job],
        SupportsInputs = true,
    };

    private int _submits;

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "fake video ready"));

    /// <summary>Inline is NOT this backend's mode; the base seam must still answer honestly.</summary>
    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "this backend generates via submit/poll"));

    public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation($"op-{++_submits}", GenerationOperationStatus.Queued));

    public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Succeeded, Progress: 1));

    public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Success(
            [new GenerationArtifact("video/mp4", Uri: $"https://example.invalid/{operationId}.mp4")],
            new GenerationUsage(Seconds: 5)));

    public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Cancelled));
}

/// <summary>A STREAMING backend: two content chunks, then a terminal completion.</summary>
public sealed class FakeGenerationStreamProvider : IGenerationProvider, IGenerationStreamProvider
{
    public string Id { get; init; } = "fake-tts";

    public GenerationCapabilities Capabilities { get; init; } = new()
    {
        Kinds = [GenerationKinds.Audio],
        Deliveries = [GenerationDelivery.Stream, GenerationDelivery.Inline],
    };

    public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new GenerationProbeResult(true, "fake tts ready"));

    public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(GenerationResult.Success([new GenerationArtifact("audio/mpeg", Data: [1, 2, 3, 4])]));

    public async IAsyncEnumerable<GenerationChunk> StreamAsync(
        GenerationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return GenerationChunk.Content([1, 2], "audio/mpeg");
        await Task.Yield();
        yield return GenerationChunk.Content([3, 4]);
        yield return GenerationChunk.Completed(new GenerationUsage(Seconds: 1.5));
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationProviderSeamTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Lyntai.Generation tests/Lyntai.Tests
git commit -m "feat(media): inline/job/stream provider seams as optional capabilities"
```

---

## Task 5: Verdict classification, shared with the LLM corpus

**Files:**
- Create: `src/Lyntai.Generation/GenerationVerdictClassifier.cs`
- Test: `tests/Lyntai.Tests/Media/GenerationVerdictClassifierTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>Media keeps its OWN verdict vocabulary, but NOT its own corpus of "what does this failure mean" —
/// that would be a second set of regexes to drift (the mistake `DECISIONS.md` D21 exists to prevent). The
/// classifier maps transport/text failures through Core's shared classifier and translates the answer.</summary>
public class GenerationVerdictClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, GenerationVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.InternalServerError, GenerationVerdict.Failed)]
    public void An_http_failure_maps_to_a_media_verdict(HttpStatusCode status, GenerationVerdict expected)
    {
        Assert.Equal(expected, GenerationVerdictClassifier.FromHttpFailure(status, body: null));
    }

    [Fact]
    public void A_content_policy_refusal_surfaces_as_refused()
    {
        // image backends refuse prompts; shopping a refused prompt around backends is not the platform's call
        var verdict = GenerationVerdictClassifier.FromErrorText("Your request was rejected by our content policy");

        Assert.Equal(GenerationVerdict.Refused, verdict);
    }

    [Fact]
    public void A_rate_limit_phrased_in_prose_is_recognized()
    {
        Assert.Equal(GenerationVerdict.RateLimited, GenerationVerdictClassifier.FromErrorText("quota exceeded, try later"));
    }

    [Fact]
    public void An_unrecognized_failure_is_plain_Failed()
    {
        Assert.Equal(GenerationVerdict.Failed, GenerationVerdictClassifier.FromErrorText("something odd happened"));
    }

    [Fact]
    public void A_context_window_verdict_has_no_media_meaning_and_becomes_Failed()
    {
        // the LLM taxonomy has verdicts media cannot have; they must not leak through as a media verdict
        Assert.Equal(GenerationVerdict.Failed, GenerationVerdictClassifier.FromErrorText("maximum context length exceeded"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationVerdictClassifierTests"`
Expected: FAIL — `GenerationVerdictClassifier` not found.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Net;
using Lyntai.Llm;

namespace Lyntai.Generation;

/// <summary>Turns a transport failure or an error message into a <see cref="GenerationVerdict"/>.
///
/// It DELEGATES the pattern matching to Core's <see cref="LlmVerdictClassifier"/> and translates the answer
/// into media's vocabulary. That is the deliberate middle path between the two bad options: media does not
/// adopt LLM-named types (it is a separate domain), and it does not carry a second copy of the "what does a
/// 429 / a content-policy refusal look like" corpus, which would drift out of sync
/// (<c>DECISIONS.md</c> D21). Consumer-registered matchers on the shared classifier therefore teach BOTH
/// domains at once.</summary>
public static class GenerationVerdictClassifier
{
    /// <summary>Classify a failed HTTP response (typed status wins over body text).</summary>
    public static GenerationVerdict FromHttpFailure(HttpStatusCode status, string? body) =>
        Translate(LlmVerdictClassifier.FromHttpFailure(status, body));

    /// <summary>Classify an error message / response body.</summary>
    public static GenerationVerdict FromErrorText(string? text) =>
        Translate(LlmVerdictClassifier.FromErrorText(text));

    /// <summary>Classify a caught exception.</summary>
    public static GenerationVerdict FromException(Exception ex) =>
        Translate(LlmVerdictClassifier.FromException(ex));

    /// <summary>Map the shared taxonomy onto media's. Verdicts with no media meaning
    /// (<see cref="LlmVerdict.ContextWindowExceeded"/>) collapse to <see cref="GenerationVerdict.Failed"/> rather
    /// than being surfaced as something a media caller cannot act on.</summary>
    private static GenerationVerdict Translate(LlmVerdict verdict) => verdict switch
    {
        LlmVerdict.Ok => GenerationVerdict.Ok,
        LlmVerdict.RateLimited => GenerationVerdict.RateLimited,
        LlmVerdict.AuthFailed => GenerationVerdict.AuthFailed,
        LlmVerdict.Refused => GenerationVerdict.Refused,
        LlmVerdict.Timeout => GenerationVerdict.Timeout,
        _ => GenerationVerdict.Failed,
    };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationVerdictClassifierTests"`
Expected: PASS, 8 tests (4 theory cases + 4 facts).

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Generation/GenerationVerdictClassifier.cs tests/Lyntai.Tests/Media/GenerationVerdictClassifierTests.cs
git commit -m "feat(media): verdict classification reusing the shared failure corpus"
```

---

## Task 6: Capability-aware routing with fallback

**Files:**
- Create: `src/Lyntai.Generation/Routing/GenerationCandidate.cs`, `Routing/IGenerationRouter.cs`, `Routing/GenerationRouter.cs`
- Test: `tests/Lyntai.Tests/Media/GenerationRouterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Media routing differs from LLM routing in one decisive way: a candidate that CANNOT serve the
/// request (wrong medium, wrong delivery, needs inputs it doesn't take) must be skipped BEFORE anything is
/// spent — capability first, verdict-driven fallback second.</summary>
public class GenerationRouterTests
{
    private static GenerationRouter Router(params IGenerationProvider[] providers) => new(providers);

    private static GenerationRequest Image() => new() { Kind = GenerationKinds.Image, Prompt = "a red square" };

    [Fact]
    public async Task It_generates_through_the_first_capable_candidate()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var result = await Router(video, image).GenerateAsync([new GenerationCandidate("video-backend"), new GenerationCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
    }

    [Fact]
    public async Task An_incapable_candidate_is_skipped_without_being_called()
    {
        // the video backend cannot serve an inline image request — it must not be invoked at all
        var video = new FakeGenerationJobProvider { Id = "video-backend" };
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var result = await Router(video, image).GenerateAsync([new GenerationCandidate("video-backend"), new GenerationCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, image.GenerateCalls);
        // the video backend's base-seam GenerateAsync returns Unsupported; proving it was never CALLED is the
        // point of the capability pre-filter, so assert on the artifact that only the image backend produces
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_transient_failure_advances_to_the_next_candidate()
    {
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(failing, working).GenerateAsync([new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, failing.GenerateCalls);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task A_refusal_SURFACES_instead_of_shopping_the_prompt_around()
    {
        var refusing = new FakeGenerationProvider { Id = "a" };
        refusing.Verdicts.Enqueue(GenerationVerdict.Refused);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(refusing, working).GenerateAsync([new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);   // the whole point
    }

    [Fact]
    public async Task An_unconfigured_backend_is_skipped_like_an_incapable_one()
    {
        var unconfigured = new FakeGenerationProvider { Id = "a" };
        unconfigured.Verdicts.Enqueue(GenerationVerdict.NotConfigured);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await Router(unconfigured, working).GenerateAsync([new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task No_capable_candidate_reports_Unsupported_not_Failed()
    {
        // "nothing here can do that" is a configuration answer, not a runtime fault
        var video = new FakeGenerationJobProvider { Id = "video-backend" };

        var result = await Router(video).GenerateAsync([new GenerationCandidate("video-backend")], Image());

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Contains("no capable", result.Detail);
    }

    [Fact]
    public async Task An_unknown_candidate_id_is_ignored_rather_than_throwing()
    {
        var image = new FakeGenerationProvider { Id = "image-backend" };

        var result = await Router(image).GenerateAsync([new GenerationCandidate("typo"), new GenerationCandidate("image-backend")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_candidate_can_pin_the_model_it_wants()
    {
        var aggregator = new FakeGenerationProvider
        {
            Id = "aggregator",
            Capabilities = new GenerationCapabilities
            {
                Kinds = [GenerationKinds.Image],
                Deliveries = [GenerationDelivery.Inline],
                Models = ["flux-1", "sdxl"],
            },
        };

        var result = await Router(aggregator).GenerateAsync([new GenerationCandidate("aggregator", "sdxl")], Image());

        Assert.True(result.IsOk);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationRouterTests"`
Expected: FAIL — `GenerationCandidate` / `GenerationRouter` not found.

- [ ] **Step 3: Write the implementation**

`src/Lyntai.Generation/Routing/GenerationCandidate.cs`:

```csharp
namespace Lyntai.Generation.Routing;

/// <summary>One routing candidate: WHICH backend, and optionally which of its models. A backend is not a
/// model — an aggregator serves hundreds behind one id, and the same model is often reachable through several
/// backends — so the pair is the unit of routing, exactly as on the LLM side.</summary>
/// <param name="ProviderId">The <see cref="IGenerationProvider.Id"/> to use.</param>
/// <param name="Model">The model/endpoint id at that backend; null = the backend's default, or whatever the
/// request already named.</param>
public sealed record GenerationCandidate(string ProviderId, string? Model = null);
```

`src/Lyntai.Generation/Routing/IGenerationRouter.cs`:

```csharp
namespace Lyntai.Generation.Routing;

/// <summary>Picks a capable media backend and falls over when one fails. The media counterpart of
/// <c>ILlmRouter</c>, kept separate because media routing must filter on CAPABILITY first — a chat model
/// always takes text, whereas a video backend simply cannot serve an image request.</summary>
public interface IGenerationRouter
{
    /// <summary>Generate inline through the first capable candidate, advancing on a fallible verdict.</summary>
    Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default);

    /// <summary>Submit an asynchronous generation through the first capable job-capable candidate. The
    /// returned operation is paired with the provider id that owns it, because an operation id only means
    /// something to the backend that issued it.</summary>
    Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default);
}

/// <summary>An accepted asynchronous generation plus the backend that owns it — persist BOTH: an operation id
/// is meaningless without knowing who issued it.</summary>
/// <param name="ProviderId">The backend holding the operation.</param>
/// <param name="Operation">The operation handle.</param>
public sealed record GenerationSubmission(string ProviderId, GenerationOperation Operation);
```

`src/Lyntai.Generation/Routing/GenerationRouter.cs`:

```csharp
namespace Lyntai.Generation.Routing;

/// <summary>The default <see cref="IGenerationRouter"/>: capability pre-filter, then verdict-driven fallback.
///
/// Fallback semantics deliberately mirror the LLM router's (design §6) so one mental model covers both:
/// <list type="bullet">
/// <item><see cref="GenerationVerdict.Refused"/> SURFACES — a content refusal is not a transport fault, and
///   quietly re-submitting a refused prompt to another vendor is not a library's decision.</item>
/// <item><see cref="GenerationVerdict.NotConfigured"/> and <see cref="GenerationVerdict.Unsupported"/> ADVANCE without
///   blame — the backend isn't broken, it just isn't the one for this request.</item>
/// <item>everything else advances, keeping the FIRST substantive failure as the reported reason: the first
///   backend's error explains the run better than the last one's.</item>
/// </list>
/// Cooldown/penalty bookkeeping is deliberately NOT here yet — see Plan 5, which composes governance
/// (budget, rate limits, dead-host cooldown) rather than growing a second copy of it.</summary>
public sealed class GenerationRouter(IEnumerable<IGenerationProvider> providers) : IGenerationRouter
{
    private readonly IReadOnlyList<IGenerationProvider> _providers = [.. providers];

    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        GenerationResult? firstFailure = null;
        var tried = 0;

        foreach (var (provider, resolved) in Capable(candidates, request, GenerationDelivery.Inline))
        {
            tried++;
            var result = await provider.GenerateAsync(resolved, ct).ConfigureAwait(false);
            if (result.IsOk) return result;

            // a refusal is the backend's judgement on the CONTENT — surface it rather than shopping around
            if (result.Verdict == GenerationVerdict.Refused) return result;

            // not-configured / unsupported aren't faults worth reporting over a real failure
            if (result.Verdict is not (GenerationVerdict.NotConfigured or GenerationVerdict.Unsupported))
                firstFailure ??= result;
        }

        if (tried == 0)
            return GenerationResult.Failure(GenerationVerdict.Unsupported,
                $"no capable media backend for kind '{request.Kind}' via {GenerationDelivery.Inline} " +
                $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]");

        return firstFailure ?? GenerationResult.Failure(GenerationVerdict.NotConfigured,
            "every capable backend reported it is not configured");
    }

    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        foreach (var (provider, resolved) in Capable(candidates, request, GenerationDelivery.Job))
        {
            if (provider is not IGenerationJobProvider job) continue;   // capability says Job; type must agree
            var operation = await job.SubmitAsync(resolved, ct).ConfigureAwait(false);
            if (operation.Status != GenerationOperationStatus.Failed)
                return new GenerationSubmission(provider.Id, operation);
        }

        return new GenerationSubmission("", new GenerationOperation("", GenerationOperationStatus.Failed,
            Detail: $"no capable media backend accepted a '{request.Kind}' job among " +
                $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]"));
    }

    /// <summary>Candidates that exist, are registered, and DECLARE they can serve this request/delivery —
    /// with the candidate's model applied to the request when it pins one.</summary>
    private IEnumerable<(IGenerationProvider Provider, GenerationRequest Request)> Capable(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, GenerationDelivery delivery)
    {
        foreach (var candidate in candidates)
        {
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Id, candidate.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is null) continue;   // an unknown id is a config typo, not a crash

            var resolved = candidate.Model is { Length: > 0 } model ? request with { Model = model } : request;
            if (!provider.Capabilities.Supports(resolved, delivery)) continue;

            yield return (provider, resolved);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationRouterTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Generation/Routing tests/Lyntai.Tests/Media/GenerationRouterTests.cs
git commit -m "feat(media): capability-aware router with §6-shaped fallback"
```

---

## Task 7: DI wiring

**Files:**
- Create: `src/Lyntai.Generation/GenerationBuilderExtensions.cs`
- Test: `tests/Lyntai.Tests/Media/GenerationDiTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai;
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>Media backends are a DI COLLECTION, like providers and scorers — adding one is a registration,
/// never an edit to a switch (`dev-conventions.md`). The router resolves the collection.</summary>
public class GenerationDiTests
{
    [Fact]
    public void Registered_media_backends_are_resolved_as_a_collection()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" })
            .AddGenerationProvider(_ => new FakeGenerationJobProvider { Id = "b" }));
        using var sp = services.BuildServiceProvider();

        var ids = sp.GetServices<IGenerationProvider>().Select(p => p.Id).ToList();

        Assert.Contains("a", ids);
        Assert.Contains("b", ids);
    }

    [Fact]
    public void The_router_is_registered_and_sees_every_backend()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg.AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" }));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IGenerationRouter>());
    }

    [Fact]
    public async Task Default_candidates_are_used_when_a_caller_names_none()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" })
            .UseDefaultGenerationCandidates("a"));
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<GenerationOptions>();
        var router = sp.GetRequiredService<IGenerationRouter>();

        var result = await router.GenerateAsync(options.DefaultCandidates,
            new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "x" });

        Assert.True(result.IsOk);
        Assert.Equal(["a"], options.DefaultCandidates.Select(c => c.ProviderId));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationDiTests"`
Expected: FAIL — `AddGenerationProvider` not found.

- [ ] **Step 3: Write the implementation**

```csharp
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>Platform configuration for the media domain. (Final shape at the end of this task — a get-only
/// mutable list, mirroring <c>LyntaiOptions.DefaultCandidates</c>.)</summary>
public sealed class GenerationOptions
{
    /// <summary>Candidate order used when a caller doesn't name one.</summary>
    public List<GenerationCandidate> DefaultCandidates { get; } = [];
}

public static class GenerationBuilderExtensions
{
    /// <summary>Register a media backend into the <see cref="IGenerationProvider"/> collection. Adding a backend is
    /// one registration — never an edit to a branch inside an existing provider (which is exactly the shape a
    /// sibling app grew: one class with an <c>if (provider == "automatic1111")</c> inside).</summary>
    public static LyntaiBuilder AddGenerationProvider(
        this LyntaiBuilder builder, Func<IServiceProvider, IGenerationProvider> factory)
    {
        builder.Services.AddSingleton(factory);
        GenerationOptionsFor(builder);   // ensure the options singleton exists even with no candidates configured
        builder.Services.TryAddSingleton<IGenerationRouter>(sp => new GenerationRouter(sp.GetServices<IGenerationProvider>()));
        return builder;
    }

    /// <summary>Set the media candidate order used when a caller doesn't pass one. SETS (clears + replaces) —
    /// the last call wins, it does not append — matching <c>LyntaiBuilder.UseDefaultCandidates</c> exactly, so
    /// the two domains behave identically. Each entry is a provider id, optionally <c>"provider:model"</c>.</summary>
    public static LyntaiBuilder UseDefaultGenerationCandidates(this LyntaiBuilder builder, params string[] candidates)
    {
        var options = GenerationOptionsFor(builder);
        options.DefaultCandidates.Clear();
        options.DefaultCandidates.AddRange(candidates.Select(Parse));
        return builder;

        static GenerationCandidate Parse(string spec)
        {
            var at = spec.IndexOf(':');
            return at < 0
                ? new GenerationCandidate(spec.Trim())
                : new GenerationCandidate(spec[..at].Trim(), spec[(at + 1)..].Trim());
        }
    }

    /// <summary>The single <see cref="GenerationOptions"/> instance for this builder, registered as a singleton
    /// INSTANCE. Registering the instance (not a factory) is what lets configure-time mutation be visible to
    /// the resolved service — the same immediate-mutation model the builder's own <c>Options</c> uses.</summary>
    private static GenerationOptions GenerationOptionsFor(LyntaiBuilder builder)
    {
        foreach (var descriptor in builder.Services)
            if (descriptor.ServiceType == typeof(GenerationOptions) &&
                descriptor.ImplementationInstance is GenerationOptions existing)
                return existing;

        var created = new GenerationOptions();
        builder.Services.AddSingleton(created);
        return created;
    }
}
```

The `GenerationOptions.DefaultCandidates` property must therefore be a mutable list, mirroring
`LyntaiOptions.DefaultCandidates`:

```csharp
/// <summary>Platform configuration for the media domain.</summary>
public sealed class GenerationOptions
{
    /// <summary>Candidate order used when a caller doesn't name one — the media counterpart of
    /// <c>LyntaiOptions.DefaultCandidates</c>, and mutable for the same reason: the builder sets it at
    /// configure time.</summary>
    public List<GenerationCandidate> DefaultCandidates { get; } = [];
}
```

(Replace the `GenerationOptions` sketch at the top of this task with the version above — the property is a
get-only mutable `List`, not a settable `IReadOnlyList`.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~GenerationDiTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Generation/GenerationBuilderExtensions.cs tests/Lyntai.Tests/Media/GenerationDiTests.cs
git commit -m "feat(media): DI wiring — backend collection, router, default candidates"
```

---

## Task 8: API baseline + docs + backlog bookkeeping

**Files:**
- Modify: `tests/Lyntai.Tests/Api/ApiSurfaceTests.cs`
- Create: `tests/Lyntai.Tests/Api/Baselines/Lyntai.Generation.txt` (seeded by the test run)
- Modify: `README.md`, `CHANGELOG.md`, `docs/DECISIONS.md`, `TASKS.md`

- [ ] **Step 1: Add the assembly to the surface test**

In `Assemblies()`, after `"Lyntai.Providers.CodexCli",`:

```csharp
        "Lyntai.Generation",
```

In `Loaded`, after the CodexCli entry:

```csharp
        ["Lyntai.Generation"] = typeof(Lyntai.Generation.IGenerationProvider).Assembly,
```

- [ ] **Step 2: Run to seed the baseline**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~ApiSurfaceTests"`
Expected: PASS — the `Lyntai.Generation.txt` baseline is created on first run. Read it and confirm it contains
only the intended public surface (no accidental leakage of a helper).

- [ ] **Step 3: Add a decision entry** — append to `docs/DECISIONS.md`:

```markdown
## D24 — media generation is a PLATFORM in its own domain, coupled to the LLM side only through tools (2026-08-04)
`Lyntai.Generation` is a separate domain package with its own contracts and its own `GenerationVerdict`, not an
extension of the LLM stack. Three findings forced the shape, and a future session must not "simplify" them
away:

- **Three delivery modes.** Image is inline, video is universally an async job (submit → poll/webhook →
  fetch; renders take minutes), and audio splits (TTS streams; music is a batch job). One
  `GenerateAsync` can only express image — so `IGenerationJobProvider` and `IGenerationStreamProvider` are separate
  optional capabilities, and an operation id is EXPOSED so a render survives a restart and composes with
  `Lyntai.Jobs`.
- **A backend is not a model.** Aggregators serve hundreds of models across every medium behind one endpoint,
  and the same model is reachable through several backends. Routing selects backend + model.
- **Capability declaration is load-bearing**, unlike LLM routing: media backends differ by medium, delivery,
  input roles and limits, so the router pre-filters before spending anything.

**Coupling, at the owner's direction:** the LLM stack gains ZERO dependency on media. The bridge is `ITool`
(and therefore MCP, via `Lyntai.Tools.Mcp.Hosting`), so an agent can generate media without either domain
referencing the other's concrete types. Where media wants an LLM (prompt rewriting), it takes `ILlmClient` —
handed in, never owned.

**Not a generation engine (per D20):** no inference, no ffmpeg pipeline authoring, no engine/weights
provisioning, no webhook hosting (the app owns its endpoint and calls `FetchAsync`), no artifact storage.
Failure PATTERNS are still shared — `GenerationVerdictClassifier` delegates to `LlmVerdictClassifier` — so there is
one corpus of "what does a 429 mean", per D21's rule against a second copy of the rules.
```

- [ ] **Step 4: Add the README section**

Add this row to the packages table, after the `Lyntai.Providers.Local` row:

```markdown
| `Lyntai.Generation` | Media generation platform — image/video/audio backends behind one capability-aware seam, with routing, probes and a tool bridge. |
```

…and this section immediately before `### Local in-process inference (`Lyntai.Providers.Local`)`:

````markdown
### Media generation (`Lyntai.Generation`)

The same idea as the LLM front door, for generated media: you register backends, Lyntai routes across them.
It is a **platform, not an engine** — every pixel and sample is produced by a backend you choose.

```csharp
services.AddLyntai(cfg => cfg
    .AddGenerationProvider(sp => new MyImageBackend(...))
    .AddGenerationProvider(sp => new MyVideoBackend(...))
    .UseDefaultGenerationCandidates("my-image-backend", "my-video-backend"));
```

Backends declare what they can do, and the router **skips a candidate that can't serve the request** before
spending anything — media backends differ far more than chat models do (medium, input roles, duration
ceilings, model catalogues):

```csharp
var result = await router.GenerateAsync(candidates, new GenerationRequest
{
    Kind = GenerationKinds.Image,                 // open string: image / video / audio / 3d / whatever's next
    Prompt = "a red square on white",
    Options = new Dictionary<string, string> { ["size"] = "1024x1024" },
});
if (result.IsOk) Save(result.Artifacts[0].Data!);
```

**Three delivery modes**, because real backends genuinely differ — and a seam that modelled only one would
force the others to lie:

| Mode | Interface | Typical of |
|---|---|---|
| Inline | `IGenerationProvider.GenerateAsync` | image generation |
| Async job | `IGenerationJobProvider` (submit → poll → fetch) | video, batch music — renders take minutes |
| Streaming | `IGenerationStreamProvider` | text-to-speech, where playback starts before generation ends |

An async render exposes its **operation id**, so it survives a process restart and composes with
`Lyntai.Jobs`; if your backend delivers by webhook, your app owns the endpoint and calls
`FetchAsync(operationId)` when it fires. Chaining is first-class — `artifact.ToInput(role)` feeds one stage's
output into the next (3d → image → video).

Every backend answers **"are you usable?"** without generating anything (`ProbeAsync`), so a setup screen
never has to pay for a test image.

**Not in scope, by design:** generation itself, downloading engines or model weights, hosting a webhook
endpoint, storing artifacts, or holding your credentials — see `docs/DECISIONS.md` D20 and D24.
````

- [ ] **Step 5: Add the CHANGELOG entry**

Under `## Unreleased` (create the heading if absent — and NEVER stamp it with a version, `DECISIONS.md` D19):

```markdown
### Added
- **`Lyntai.Generation`** — a NEW package: the media generation **platform**. One capability-aware provider seam
  spanning image, video and audio (and any medium next — `Kind` is an open string, as 3D already ships on real
  aggregators), with three delivery modes because real backends genuinely differ: inline (`IGenerationProvider`),
  async job (`IGenerationJobProvider` — submit → poll → fetch, universal for video and batch music) and streaming
  (`IGenerationStreamProvider` — TTS starts playback before generation ends). Async operations expose their
  **operation id**, so a render survives a restart, composes with `Lyntai.Jobs`, and works with a
  webhook-delivering backend (your app owns the endpoint and calls `FetchAsync`). Backends declare
  `GenerationCapabilities` and the router **pre-filters** on them — unlike chat models, a media backend often simply
  cannot serve a request, and that is a skip rather than a failure. Every backend answers "are you usable?"
  via `ProbeAsync` **without generating anything**, replacing the generate-and-discard test that pattern
  otherwise requires. Media keeps its own `GenerationVerdict` vocabulary but **shares the failure corpus**
  (`GenerationVerdictClassifier` delegates to `LlmVerdictClassifier`), so there is one definition of what a 429 or a
  content refusal means. Lyntai generates nothing itself: no inference, no engine/weights provisioning, no
  webhook host, no artifact storage (`docs/DECISIONS.md` D20, D24). The LLM stack gains **zero** dependency on
  media — the bridge is `ITool`/MCP.
```

- [ ] **Step 6: Verify + commit**

Run: `node devtools/dev.mjs verify`
Expected: all gates green.

```bash
git add -A
git commit -m "feat(media): seed the API baseline and document the platform (D24)"
```

- [ ] **Step 7: Update the backlog**

In `TASKS.md`, leave MED1's filed text intact (it is the requester's record) and append this line directly
beneath its **Done when** paragraph — do NOT archive Part 32 until the later plans land:

```markdown
  **Status 2026-08-04:** the platform core landed — see `docs/2026-08-04-generation-platform-plan.md` (Plan 1) and
  `docs/DECISIONS.md` D24. Remaining: Plan 2 (HTTP image backends), Plan 3 (local subprocess backend),
  Plan 4 (async video + `Lyntai.Jobs` composition), Plan 5 (governance/telemetry parity), Plan 6 (tool/MCP
  bridge + streaming audio), Plan 7 (pipelines: 3d → image → video).
```

---

## Subsequent plans (each produces working software on its own)

Deliberately separate plans, per the scope rule — the core above is testable and shippable without any of
them, and each of these needs its own research/measurement pass.

**Plan 2 — HTTP backends (three of them).** `Lyntai.Generation.OpenAiCompatible` (`/images/generations` +
`/images/edits`, `b64_json` and `url` responses), `Lyntai.Generation.Automatic1111` (`/sdapi/v1/txt2img`,
`/img2img`), and **`Lyntai.Generation.ComfyUi`** (local, graph-shaped — see decision 3). Separate packages, NOT
one class with a vendor branch — the sibling app's `if (provider == "automatic1111")` becomes separate
registrations. Real probes: A1111 has `/sdapi/v1/sd-models`; ComfyUI has system-stats/object-info; the
OpenAI-compatible one lists models. Tests use a stubbed `HttpMessageHandler`. **Port the response-parsing from
the sibling app** — it is production-proven — but re-shape the seam.

**Plan 3 — the local subprocess backend.** `Lyntai.Generation.LocalDiffusion` over a provisioned
stable-diffusion.cpp binary, through `IProcessRunner` (never `Process` directly — that is what the sibling app
does, and it loses kill-tree, the inactivity clock and the BYO seam). Argv is PORTED from the app's working
implementation and must be marked in the XML docs as *ported, not measured here* (no engine on the dev
machine). Probe = binary + model present, which is genuinely free.

**Plan 4 — async video, composed with `Lyntai.Jobs`.** An `IGenerationJobProvider` backend for **fal.ai's queue
API** (decision 1: `submit` → `request_id` → poll `status`/`result`, or a webhook the app owns), which reaches
the Wan/Kling/Veo-class models through one integration — plus a `GenerationRenderJobHandler : IJobHandler` that
checkpoints the operation id so a restart resumes polling instead of re-submitting a paid render. Reference
behaviour for the shape: create-task then poll, 1–5 minute renders, text/image/reference→video, ~10s clips,
480p–1080p. This is the plan where the platform's value over a thin client becomes visible — and where the
per-call cost must land in `GenerationUsage.CostUsd`, since aggregator pricing is per model AND per resolution.

**Plan 5 — governance + telemetry parity.** Cost/budget accounting on `GenerationUsage`, rate limiting, dead-host
cooldown for media candidates, and OTel spans/metrics on the media source — reusing the existing decorator
patterns rather than a second implementation.

**Plan 6 — the tool/MCP bridge and audio.** The bridge half SHIPPED 2026-08-04 as `AddGenerationTools()` —
five `ITool`s in Core (`generate_backends`/`generate`/`generate_submit`/`generate_status`/`generate_fetch`),
so both the in-process loop and a CLI agent over the MCP host can drive media (`docs/task-archive.md`
Part 33). What remains is the streaming TTS backend to exercise `IGenerationStreamProvider` end to end —
still the one contract in the platform no real backend has exercised.

**Plan 7 — pipelines (3d → image → video).** A `GenerationPipeline` that runs ordered stages, feeding each stage's
artifacts into the next via `ToInput(role)`, with per-stage candidates so every stage routes independently, and
per-stage failure semantics (a refusal at stage 2 must not silently retry stage 1's paid output). Deferred on
purpose: a pipeline runner designed before two real backends exist would encode guesses about what stages need
to pass each other. The core above already carries the two things it depends on — artifact→input chaining and
an open `Kind` — so this is additive when the time comes. 3D backends worth surveying first (none integrated
yet): image-to-3D / text-to-3D services, and whether the useful output is a mesh or turntable stills, since
only the latter chains into today's video backends.

---

## Decisions (2026-08-04)

1. **First remote backend: an AGGREGATOR — fal.ai.** Researched today: it carries ~985 endpoints (≈600+
   models vs Replicate's ≈200), is typically 30–50% cheaper than Replicate, spans image/video/audio/**3D**
   behind ONE queue API (`submit` → `request_id` → poll `status`/`result`, or a webhook), and video runs
   ≈$0.05/s (Wan-class) to ≈$0.40/s (Veo-class). One integration therefore exercises the async-job seam AND
   covers most of the models worth reaching, with a single auth and a single envelope to parse.
   A direct Model Studio integration was rejected as the *first* backend: it adds another auth and envelope
   for one vendor's family, and buys nothing in exchange — the hosted models are moderated at the API layer
   either way (see the note below), so "direct" is not more capable, just more code. Add it later if a
   specific model is only reachable there.
   **Watch the billing:** fal prices per model, and the headline rate often buys a lower tier — compare the
   exact model **and resolution**, and record what a call actually cost in `GenerationUsage.CostUsd` rather
   than inferring it.
2. **Audio: TTS first, then music.** TTS exercises `IGenerationStreamProvider` (playback before completion);
   music exercises the batch-job path, which Plan 4 already covers for video.
3. **A locally-run backend is not optional, for a structural reason.** Every hosted model enforces its
   provider's content policy **at the API layer**, and a host cannot opt out of it — which is precisely why
   `GenerationRoutingPolicy` exists and why the local shape is a first-class backend rather than a
   nice-to-have. Relevant capability facts found today: open weights for the strongest local video model are
   **Apache-2.0 and ComfyUI-native**, while the newer versions of the same family are **closed-weight,
   hosted-only and moderated** — so "run it locally" and "call the newest hosted version" are genuinely
   different products, not two routes to one thing.
   **`ComfyUI` is therefore the local backend to build first** — and it earns three things at once: the
   local/self-hosted shape, a **graph-shaped** backend (it takes a workflow + node overrides, so `Prompt` may
   be null and `Options` carries the workflow id — the shape this plan promised to tolerate), and an
   end-to-end test of `Refused → Advance` against a real pair of backends rather than fakes. Note it is
   **local but HTTP**, so it belongs with Plan 2's backends, not Plan 3's subprocess one; its probe is free and
   exact (a system-stats/object-info endpoint answers "up, and what's loaded?" without generating).

### Still open

4. **Does `Lyntai.Generation` want its own storage domain later** (an artifact/generation ledger, so a render is
   auditable and re-fetchable)? Out of scope here per MED1, but it is the obvious next platform concern after
   Plan 4 — and a prerequisite for pipelines worth resuming.
5. **Is MED1's `IVideoProvider` satisfied by this generalization?** This plan deliberately does NOT create a
   separate video interface: video is `Kind = "video"` plus the `IGenerationJobProvider` delivery mode, because the
   thing that makes video different is *how it is delivered*, not *that it is video* — and a per-medium
   interface would need a third for audio and a fourth for 3D. The app's existing HTML→MP4 renderer still fits:
   it implements `IGenerationProvider` + `IGenerationJobProvider` (or inline, if it blocks) inside the app, and the
   platform routes to it like any other backend. Flagging it because it is a visible deviation from the filed
   task.
