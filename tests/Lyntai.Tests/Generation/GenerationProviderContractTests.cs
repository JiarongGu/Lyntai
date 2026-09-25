using Lyntai.Inference;
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
    protected abstract IModelProvider New(StubHttpHandler http);

    /// <summary>A request this backend would accept — the shape its own suite uses.</summary>
    protected abstract MediaRequest Ask();

    /// <summary>An operation id this backend can PARSE. It must be well-formed, or a job backend rejects it
    /// before it ever calls out — which is how the first draft of the fetch fact silently tested fal's
    /// operation-id validation instead of its verdict classification, and reported the defect it was hunting
    /// for the wrong reason.</summary>
    protected virtual string OperationId => "some-operation-id";

    protected IModelProvider Healthy() => New(new StubHttpHandler());

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
    private IModelProvider Rejecting()
    {
        var http = new StubHttpHandler();
        http.Enqueue(_ => GenerationProviderContract.Unauthorized());
        return New(http);
    }

    private IModelProvider Refusing()
    {
        var http = new StubHttpHandler();
        http.Enqueue(_ => throw new HttpRequestException("connection refused"));
        return New(http);
    }

    [Fact]
    public async Task A_transport_failure_is_a_verdict_rather_than_a_throw()
    {
        var provider = Refusing();
        if (!provider.Capabilities.Operations.Contains(ProviderOperation.Complete)) return;

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
        if (!provider.Capabilities.Operations.Contains(ProviderOperation.Complete)) return;

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
        var marker = Encoding.ASCII.GetBytes("LYNTAI-INPUT-MARKER-7F3A");

        await HandInputs(MediaInput.Init(marker, "image/png"));
    }

    /// <summary>The same fact with TWO inputs in two roles — the shape a pipeline stage carrying its own input
    /// hands a backend once the previous stage's artifact is chained in after it.</summary>
    [Fact]
    public async Task Every_handed_input_is_consumed_or_the_call_refused()
    {
        var first = Encoding.ASCII.GetBytes("LYNTAI-INPUT-MARKER-1B2C");
        var second = Encoding.ASCII.GetBytes("LYNTAI-INPUT-MARKER-3D4E");

        await HandInputs(MediaInput.FirstFrame(first, "image/png"), MediaInput.Init(second, "image/png"));
    }

    private async Task HandInputs(params MediaInput[] inputs)
    {
        var http = new StubHttpHandler();
        // junk to every backend but an uploading one, which must get past its first upload to send the second
        http.Enqueue(HttpStatusCode.OK, """{"name":"stored.png"}""");
        var provider = New(http);
        if (!provider.Capabilities.SupportsInputs) return;

        var ask = Ask() with { Inputs = inputs };

        if (provider.Capabilities.Operations.Contains(ProviderOperation.Complete))
            await provider.GenerateAsync(ask);
        else if (provider is IMediaJobProvider jobs)
            await jobs.SubmitAsync(ask);

        GenerationProviderContract.A_handed_input_is_consumed_or_refused(
            provider.Id, [.. http.Requests.Select(r => r.Body)], [.. inputs.Select(i => i.Data!)]);
    }

    /// <summary>The INLINE door classifies a 401.</summary>
    [Fact]
    public async Task An_inline_401_is_classified()
    {
        var provider = Rejecting();
        if (!provider.Capabilities.Operations.Contains(ProviderOperation.Complete)) return;

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
        if (New(http) is not IMediaJobProvider jobs) return;

        var result = await jobs.FetchAsync(OperationId);

        GenerationProviderContract.An_authentication_failure_is_classified_rather_than_flattened(
            "FetchAsync", ((IModelProvider)jobs).Id, result.Verdict);
    }
}

public class OpenAiImageProviderContractTests : HttpGenerationProviderContractFacts
{
    protected override IModelProvider New(StubHttpHandler http) =>
        new OpenAiImageProvider(
            new OpenAiImageOptions { ApiKey = "k" },
            () => new HttpClient(http, disposeHandler: false));

    protected override MediaRequest Ask() =>
        new() { Kind = ProviderKinds.Image, Prompt = "a red square" };
}

public class Automatic1111ProviderContractTests : HttpGenerationProviderContractFacts
{
    protected override IModelProvider New(StubHttpHandler http) =>
        new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" },
            () => new HttpClient(http, disposeHandler: false));

    protected override MediaRequest Ask() =>
        new() { Kind = ProviderKinds.Image, Prompt = "a red square" };
}

public class ComfyUiProviderContractTests : HttpGenerationProviderContractFacts
{
    // nodes 10 and 11 load the first frame and the init image, so the input facts see both CONSUMED
    // (uploaded) rather than refused
    private const string Workflow =
        """{"3":{"class_type":"KSampler","inputs":{"seed":0}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}},"10":{"class_type":"LoadImage","inputs":{"image":"none"}},"11":{"class_type":"LoadImage","inputs":{"image":"none"}}}""";

    protected override IModelProvider New(StubHttpHandler http) =>
        new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(http, disposeHandler: false));

    protected override MediaRequest Ask() => new()
    {
        Kind = ProviderKinds.Image,
        Prompt = "a red square",
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflow"] = Workflow,
            ["prompt-path"] = "6.inputs.text",
            ["input-path:first-frame"] = "10.inputs.image",
            ["input-path:init"] = "11.inputs.image",
        },
    };
}

public class FalProviderContractTests : HttpGenerationProviderContractFacts
{
    protected override IModelProvider New(StubHttpHandler http) =>
        new FalProvider(
            new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" },
            () => new HttpClient(http, disposeHandler: false));

    protected override MediaRequest Ask() =>
        new() { Kind = ProviderKinds.Video, Prompt = "a cat surfing" };

    /// <summary>fal encodes the MODEL into the operation id, because the queue's status and result URLs need
    /// it while a resumed job hands back only an id. A bare id is rejected before any call is made.</summary>
    protected override string OperationId => "fal-ai/wan-t2v#req-123";
}

/// <summary>The subprocess backend. It takes the universal facts only — it speaks no HTTP, so a 401 has no
/// meaning for it and asserting one would be a fact that cannot fail.</summary>
public class LocalDiffusionProviderContractTests : GenerationProviderContractFacts, IDisposable
{
    private readonly ScratchDir _scratch = new("sd-contract");

    public void Dispose() => _scratch.Dispose();

    protected override IModelProvider New(StubHttpHandler http)
    {
        return new LocalDiffusionProvider(
            new LocalDiffusionOptions
            {
                BinaryPath = _scratch.File("sd-cli.exe"), ModelPath = _scratch.File("sd15.gguf"),
                WorkDirectory = _scratch.Path,
            },
            new FakeProcessRunner());
    }

    protected override MediaRequest Ask() =>
        new() { Kind = ProviderKinds.Image, Prompt = "a red square" };
}
