using System.Net;
using System.Text;

using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Every <see cref="GenerationProviderContract"/> fact against all FIVE shipped backends. Derive a
/// class per backend so a new one gets the whole contract by adding one file — the same shape
/// <c>MemoryAgePolicyContractFacts</c> and <c>JobStoreContractFacts</c> use.</summary>
public abstract class GenerationProviderContractFacts
{
    /// <summary>Build the backend. <paramref name="http"/> is ignored by a backend that speaks no HTTP.</summary>
    protected abstract IGenerationProvider New(StubHttpHandler http);

    /// <summary>A request this backend would accept — the shape its own suite uses.</summary>
    protected abstract GenerationRequest Ask();

    /// <summary>An operation id this backend can PARSE. It must be well-formed, or a job backend rejects it
    /// before it ever calls out — which is how the first draft of the fetch fact silently tested fal's
    /// operation-id validation instead of its verdict classification, and reported the defect it was hunting
    /// for the wrong reason.</summary>
    protected virtual string OperationId => "some-operation-id";

    protected IGenerationProvider Healthy() => New(new StubHttpHandler());

    [Fact] public void Identity() => GenerationProviderContract.It_declares_a_usable_identity(Healthy());

    [Fact] public void Deliveries_are_backed() =>
        GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements(Healthy());

    [Fact] public Task Job_only_declines_inline() =>
        GenerationProviderContract.An_inline_call_to_a_job_only_backend_is_Unsupported(Healthy(), Ask());
}

/// <summary>The four HTTP backends, which additionally have to classify a transport answer. Split from the
/// base rather than gated by a flag: the facts below are meaningless for a subprocess backend, and a fact
/// that cannot fail is worse than no fact at all.</summary>
public abstract class HttpGenerationProviderContractFacts : GenerationProviderContractFacts
{
    /// <summary>A 401 on every request, which is what an authenticating proxy in front of a local backend
    /// looks like — the reachable case, not a hypothetical.</summary>
    private IGenerationProvider Rejecting()
    {
        var http = new StubHttpHandler();
        http.Enqueue(_ => GenerationProviderContract.Unauthorized());
        return New(http);
    }

    private IGenerationProvider Refusing()
    {
        var http = new StubHttpHandler();
        http.Enqueue(_ => throw new HttpRequestException("connection refused"));
        return New(http);
    }

    [Fact]
    public async Task A_transport_failure_is_a_verdict_rather_than_a_throw()
    {
        var provider = Refusing();
        if (!provider.Capabilities.Deliveries.Contains(GenerationDelivery.Inline)) return;

        await GenerationProviderContract.A_backend_failure_is_a_verdict_rather_than_a_throw(provider, Ask());
    }

    /// <summary>Driven through a handler that HONOURS the token. <c>StubHttpHandler</c> ignores it and
    /// answers from its script, so a fact written against that one passed vacuously for the job backends and
    /// failed for the inline ones by testing the stub rather than the provider — which is the shape this
    /// repository treats as worse than no fact at all. What is under test is the provider's catch ORDERING:
    /// <c>catch (OperationCanceledException) { throw; }</c> ahead of the fail-safe catch that turns
    /// everything else into a verdict.</summary>
    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var http = new StubHttpHandler();
        http.Enqueue(_ => throw new OperationCanceledException());
        var provider = New(http);
        if (!provider.Capabilities.Deliveries.Contains(GenerationDelivery.Inline)) return;

        await GenerationProviderContract.Caller_cancellation_propagates_rather_than_becoming_a_verdict(
            provider, Ask());
    }

    /// <summary><b>A backend that declares it takes inputs must not silently drop one.</b> Driven through
    /// whichever door the backend actually serves, because the job-only backends are exactly where a chained
    /// artifact arrives. The response is deliberately junk — what is under test is the request that was SENT,
    /// not what came back, so no backend needs its own success shape scripted here.</summary>
    [Fact]
    public async Task A_declared_input_capability_is_honoured()
    {
        var http = new StubHttpHandler();
        http.Enqueue(HttpStatusCode.OK, "{}");
        var provider = New(http);
        if (!provider.Capabilities.SupportsInputs) return;

        var marker = Encoding.ASCII.GetBytes("LYNTAI-INPUT-MARKER-7F3A");
        var ask = Ask() with { Inputs = [GenerationInput.FirstFrame(marker, "image/png")] };

        if (provider.Capabilities.Deliveries.Contains(GenerationDelivery.Inline))
            await provider.GenerateAsync(ask);
        else if (provider is IGenerationJobProvider jobs)
            await jobs.SubmitAsync(ask);

        GenerationProviderContract.A_handed_input_is_consumed_or_refused(
            provider.Id, [.. http.Requests.Select(r => r.Body)], marker);
    }

    /// <summary>The INLINE door classifies a 401.</summary>
    [Fact]
    public async Task An_inline_401_is_classified()
    {
        var provider = Rejecting();
        if (!provider.Capabilities.Deliveries.Contains(GenerationDelivery.Inline)) return;

        var result = await provider.GenerateAsync(Ask());

        GenerationProviderContract.An_authentication_failure_is_classified_rather_than_flattened(
            "GenerateAsync", provider.Id, result.Verdict);
    }

    /// <summary><b>The FETCH door classifies a 401 too — the door the divergence lived on.</b> Fetch is the
    /// one place a job backend returns a verdict rather than a status, and it is reached after a render has
    /// been paid for, so a host that cannot tell "your proxy rejected me" from "the render failed" retries a
    /// generation it already owns.</summary>
    [Fact]
    public async Task A_fetch_401_is_classified()
    {
        var http = new StubHttpHandler();
        http.Enqueue(_ => GenerationProviderContract.Unauthorized());
        if (New(http) is not IGenerationJobProvider jobs) return;

        var result = await jobs.FetchAsync(OperationId);

        GenerationProviderContract.An_authentication_failure_is_classified_rather_than_flattened(
            "FetchAsync", ((IGenerationProvider)jobs).Id, result.Verdict);
    }
}

public class OpenAiImageProviderContractTests : HttpGenerationProviderContractFacts
{
    protected override IGenerationProvider New(StubHttpHandler http) =>
        new OpenAiImageProvider(
            new OpenAiImageOptions { ApiKey = "k" },
            () => new HttpClient(http, disposeHandler: false));

    protected override GenerationRequest Ask() =>
        new() { Kind = GenerationKinds.Image, Prompt = "a red square" };
}

public class Automatic1111ProviderContractTests : HttpGenerationProviderContractFacts
{
    protected override IGenerationProvider New(StubHttpHandler http) =>
        new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
            () => new HttpClient(http, disposeHandler: false));

    protected override GenerationRequest Ask() =>
        new() { Kind = GenerationKinds.Image, Prompt = "a red square" };
}

public class ComfyUiProviderContractTests : HttpGenerationProviderContractFacts
{
    private const string Workflow =
        """{"3":{"class_type":"KSampler","inputs":{"seed":0}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}}}""";

    protected override IGenerationProvider New(StubHttpHandler http) =>
        new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(http, disposeHandler: false));

    protected override GenerationRequest Ask() => new()
    {
        Kind = GenerationKinds.Image,
        Prompt = "a red square",
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflow"] = Workflow,
            ["prompt-path"] = "6.inputs.text",
        },
    };
}

public class FalQueueProviderContractTests : HttpGenerationProviderContractFacts
{
    protected override IGenerationProvider New(StubHttpHandler http) =>
        new FalQueueProvider(
            new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" },
            () => new HttpClient(http, disposeHandler: false));

    protected override GenerationRequest Ask() =>
        new() { Kind = GenerationKinds.Video, Prompt = "a cat surfing" };

    /// <summary>fal encodes the MODEL into the operation id, because the queue's status and result URLs need
    /// it while a resumed job hands back only an id. A bare id is rejected before any call is made.</summary>
    protected override string OperationId => "fal-ai/wan-t2v#req-123";
}

/// <summary>The subprocess backend. It takes the universal facts only — it speaks no HTTP, so a 401 has no
/// meaning for it and asserting one would be a fact that cannot fail.</summary>
public class LocalDiffusionProviderContractTests : GenerationProviderContractFacts
{
    protected override IGenerationProvider New(StubHttpHandler http)
    {
        var dir = Path.Combine(TestPaths.TestScratchDir, $"sd-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "sd-cli.exe");
        var model = Path.Combine(dir, "sd15.gguf");
        File.WriteAllText(exe, "");
        File.WriteAllText(model, "");

        return new LocalDiffusionProvider(
            new LocalDiffusionOptions { BinaryPath = exe, ModelPath = model, WorkDirectory = dir },
            new FakeProcessRunner());
    }

    protected override GenerationRequest Ask() =>
        new() { Kind = GenerationKinds.Image, Prompt = "a red square" };
}
