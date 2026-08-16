using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>Per-backend <c>Add*</c> shims, the generation counterpart of <c>AddOpenAiProvider()</c> /
/// <c>AddOllamaProvider()</c>. Before these, every media backend had to be hand-constructed WITH its
/// <c>Func&lt;HttpClient&gt;</c> — an asymmetry the pre-2.0.1 consumer smoke surfaced (TASKS.md Part 34).
///
/// What they must preserve: the BYO-HttpClient seam (design §7), and the rule that a Lyntai-created client has
/// an INFINITE HttpClient timeout so the per-call deadline owns cancellation — a 100-second default would kill
/// a perfectly healthy video render.</summary>
public class GenerationProviderWiringTests
{
    private const string OneImage = """{"data":[{"b64_json":"iVBORw=="}]}""";

    [Fact]
    public async Task AddOpenAiImageProvider_registers_a_backend_that_uses_the_BYO_client()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OneImage);
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddOpenAiImageProvider(
            o => { o.BaseUrl = "https://example.invalid/v1"; o.ApiKey = "k"; },
            _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        var provider = Assert.Single(sp.GetServices<IGenerationProvider>());
        Assert.Equal("openai-images", provider.Id);

        var result = await provider.GenerateAsync(
            new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" });

        Assert.Equal(GenerationVerdict.Ok, result.Verdict);
        Assert.StartsWith("https://example.invalid/v1", handler.Requests[0].Uri!.ToString());
    }

    [Theory]
    [MemberData(nameof(HttpBackends))]
    public void Every_http_backend_registers_under_its_configured_id(string id, Action<LyntaiBuilder> add)
    {
        // the id is the ROUTING handle — a shim that dropped it would make the backend unaddressable by a
        // candidate list, which fails at route time rather than at wiring time
        var services = new ServiceCollection();
        services.AddLyntai(b => add(b));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(id, Assert.Single(sp.GetServices<IGenerationProvider>()).Id);
    }

    public static TheoryData<string, Action<LyntaiBuilder>> HttpBackends() => new()
    {
        { "images", b => b.AddOpenAiImageProvider(
            o => { o.BaseUrl = "https://example.invalid/v1"; o.Id = "images"; }) },
        { "webui", b => b.AddAutomatic1111Provider(
            o => { o.BaseUrl = "http://127.0.0.1:7860"; o.Id = "webui"; }) },
        { "comfy", b => b.AddComfyUiProvider(
            o => { o.BaseUrl = "http://127.0.0.1:8188"; o.Id = "comfy"; }) },
        { "queue", b => b.AddFalProvider(o => { o.ApiKey = "k"; o.Id = "queue"; }) },
    };

    [Fact]
    public void A_Lyntai_created_client_has_an_infinite_timeout_so_the_per_call_deadline_owns_cancellation()
    {
        // HttpClient's 100s default would abort a legitimately slow render (a hosted video submit, a local
        // WebUI at high step counts) as a transport failure — the same rule the LLM presets follow
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddFalProvider(o => { o.ApiKey = "k"; }));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("lyntai.generation.fal");

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public async Task A_BYO_client_survives_a_second_render_because_Lyntai_never_disposes_it()
    {
        // the backends take a Func<HttpClient> and disposed whatever it returned, which is right for a factory
        // that MAKES a client per call and wrong for the natural BYO lambda `_ => _myClient`: the first render
        // succeeds and the second throws ObjectDisposedException. BYO means the host owns the lifecycle — the
        // same rule the LLM side has always followed (`disposeHttpClient: !byo`).
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OneImage).Enqueue(HttpStatusCode.OK, OneImage);
        using var mine = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddOpenAiImageProvider(
            o => { o.BaseUrl = "https://example.invalid/v1"; o.ApiKey = "k"; }, _ => mine));
        using var sp = services.BuildServiceProvider();

        var provider = Assert.Single(sp.GetServices<IGenerationProvider>());
        var ask = new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" };

        Assert.Equal(GenerationVerdict.Ok, (await provider.GenerateAsync(ask)).Verdict);
        Assert.Equal(GenerationVerdict.Ok, (await provider.GenerateAsync(ask)).Verdict);
    }

    [Fact]
    public async Task A_factory_that_MAKES_a_client_per_call_still_has_it_disposed()
    {
        // the other half of the same rule, and the default: a hand-constructed backend passing
        // `() => new HttpClient(...)` must not leak one client per render
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, OneImage);
        HttpClient? made = null;
        // The PROVIDER constructor still takes an options instance — only the Add* registration shims moved
        // to a configure callback, because a callback is a registration idiom and this is direct construction.
        var provider = new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", ApiKey = "k" },
            () => made = new HttpClient(handler, disposeHandler: false));

        await provider.GenerateAsync(new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "x" });

        Assert.Throws<ObjectDisposedException>(() => made!.GetAsync("https://example.invalid/").GetAwaiter().GetResult());
    }

    [Fact]
    public void The_named_client_a_backend_uses_is_reachable_so_a_host_can_decorate_it()
    {
        // a host that wants Polly or a logging handler on Lyntai's own client shouldn't have to abandon the
        // shim and hand-construct the backend
        Assert.Equal("lyntai.generation.fal", GenerationProviderBuilderExtensions.HttpClientName("fal"));
    }

    [Fact]
    public void The_backends_compose_into_one_candidate_list_like_any_other_DI_collection()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddFalProvider(o => { o.ApiKey = "k"; })
            .AddAutomatic1111Provider(o => { o.BaseUrl = "http://127.0.0.1:7860"; })
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "byo" })   // the BYO seam stays open
            .UseDefaultGenerationCandidates("fal", "a1111", "byo"));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(["fal", "a1111", "byo"], sp.GetServices<IGenerationProvider>().Select(p => p.Id));
    }

    [Fact]
    public async Task AddLocalDiffusionProvider_takes_the_process_runner_from_DI()
    {
        // the sd-cli backend spawns rather than calls, so its seam is IProcessRunner — a shim that newed up a
        // ProcessRunner would silently bypass a host's audited/sandboxed one
        var dir = Path.Combine(TestPaths.TestScratchDir, $"sd-wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "sd-cli.exe");
        var model = Path.Combine(dir, "sd15.gguf");
        File.WriteAllText(exe, "");
        File.WriteAllText(model, "");

        var runner = new FakeProcessRunner();
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);         // BYO, registered BEFORE AddLyntai's TryAdd
        services.AddLyntai(b => b.AddLocalDiffusionProvider(o => { o.BinaryPath = exe; o.ModelPath = model; o.WorkDirectory = dir; }));
        using var sp = services.BuildServiceProvider();

        var provider = Assert.Single(sp.GetServices<IGenerationProvider>());
        Assert.Equal("local-diffusion", provider.Id);

        await provider.GenerateAsync(new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "x" });

        Assert.NotEmpty(runner.Calls);   // the host's runner did the spawning
    }

    [Fact]
    public void An_options_object_is_required_rather_than_a_configure_callback()
    {
        // the options are records with `required`/`init` members, so a mutate-after-construction callback
        // cannot work — passing the instance is what keeps `required BaseUrl` compiler-enforced
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddLyntai(b => b.AddOpenAiImageProvider(null!)));
    }
}
