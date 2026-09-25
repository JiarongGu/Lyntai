using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The three single-input backends — OpenAI images, Automatic1111 and <c>sd-cli</c> — each read ONE
/// source image, as the init image. <c>SupportsInputs</c> makes the router hand them any input-carrying request,
/// so an input they cannot place must be REFUSED (<see cref="ProviderVerdict.Unsupported"/>, nothing sent), never
/// used as the init image or dropped: a second input, or one in a role other than init.</summary>
public class GenerationInputAdmissionTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private static MediaRequest Ask(params MediaInput[] inputs) =>
        new() { Kind = ProviderKinds.Image, Prompt = "a red square", Inputs = inputs };

    public static TheoryData<string> Backends => ["openai", "a1111", "sd-cli"];

    private static (IModelProvider Provider, Func<int> Calls) New(string backend)
    {
        var http = new StubHttpHandler();
        http.Enqueue(HttpStatusCode.OK, """{"data":[{"b64_json":"iVBORw=="}],"images":["iVBORw=="]}""");
        switch (backend)
        {
            case "openai":
                return (new OpenAiImageProvider(new OpenAiImageOptions { ApiKey = "k" },
                    () => new HttpClient(http, disposeHandler: false)), () => http.Requests.Count);
            case "a1111":
                return (new Automatic1111Provider(new Automatic1111Options(),
                    () => new HttpClient(http, disposeHandler: false)), () => http.Requests.Count);
            default:
                var dir = Path.Combine(TestPaths.TestScratchDir, $"sd-admission-{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                var exe = Path.Combine(dir, "sd-cli.exe");
                var model = Path.Combine(dir, "sd15.gguf");
                File.WriteAllText(exe, "");
                File.WriteAllText(model, "");
                var runner = new FakeProcessRunner();
                return (new LocalDiffusionProvider(
                    new LocalDiffusionOptions { BinaryPath = exe, ModelPath = model, WorkDirectory = dir }, runner),
                    () => runner.Calls.Count);
        }
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_second_input_is_refused_rather_than_dropped(string backend)
    {
        var (provider, calls) = New(backend);

        var result = await provider.GenerateAsync(Ask(MediaInput.Init(Png, "image/png"), MediaInput.From("mask", Png, "image/png")));

        Assert.Equal(ProviderVerdict.Unsupported, result.Verdict);
        Assert.Contains("2", result.Detail);
        Assert.Equal(0, calls());
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task An_input_in_a_role_other_than_init_is_refused_rather_than_used_as_one(string backend)
    {
        var (provider, calls) = New(backend);

        var result = await provider.GenerateAsync(Ask(MediaInput.Reference(Png, "image/png")));

        Assert.Equal(ProviderVerdict.Unsupported, result.Verdict);
        Assert.Contains("reference", result.Detail);
        Assert.Equal(0, calls());
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_roleless_input_is_still_the_init_image(string backend)
    {
        var (provider, calls) = New(backend);

        await provider.GenerateAsync(Ask(new MediaInput("image/png", Data: Png)));

        Assert.Equal(1, calls());
    }
}
