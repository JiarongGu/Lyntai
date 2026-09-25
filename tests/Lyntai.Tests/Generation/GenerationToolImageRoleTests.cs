using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>What an agent's <c>imageUrl</c> IS to the render. Both tools used to tag it
/// <see cref="MediaInputRoles.Init"/> whatever their schema promised, so an image→video submit reached a video
/// backend as an img2img source — fal sends only a first frame as <c>image_url</c>. The model now says which
/// role it means, and each tool defaults to the one its medium implies.</summary>
public class GenerationToolImageRoleTests
{
    /// <summary>Serves both doors and remembers what it was asked.</summary>
    private sealed class Recorder : IModelProvider, IMediaJobProvider
    {
        public List<MediaRequest> Requests { get; } = [];

        public string Id => "recorder";

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Image, ProviderKinds.Video],
            Operations = [ProviderOperation.Complete, ProviderOperation.Queued],
            SupportsInputs = true,
        };

        public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProviderProbeResult(true));

        public Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(MediaResponse.Success([new MediaArtifact("image/png", Data: [1])]));
        }

        public Task<QueuedOperation> SubmitAsync(MediaRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new QueuedOperation("op-1", QueuedOperationStatus.Queued));
        }

        public Task<QueuedOperation> PollAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new QueuedOperation(operationId, QueuedOperationStatus.Running));

        public Task<MediaResponse> FetchAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(MediaResponse.Failure(ProviderVerdict.Failed, "not finished"));

        public Task<QueuedOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new QueuedOperation(operationId, QueuedOperationStatus.Cancelled));
    }

    private static (ServiceProvider Services, Recorder Backend) Host()
    {
        var backend = new Recorder();
        var services = new ServiceCollection();
        services.AddLyntai(cfg =>
        {
            cfg.AddProvider(_ => backend);
            cfg.UseDefaultMediaCandidates("recorder");
            cfg.AddMediaRouting();
            cfg.AddGenerationTools();
        });
        return (services.BuildServiceProvider(), backend);
    }

    private static ITool Tool(ServiceProvider sp, string name) => sp.GetServices<ITool>().Single(t => t.Name == name);

    [Theory]
    [InlineData("generate_submit", null, MediaInputRoles.FirstFrame)]
    [InlineData("generate", null, MediaInputRoles.Init)]
    [InlineData("generate_submit", "reference", MediaInputRoles.Reference)]
    [InlineData("generate", "first-frame", MediaInputRoles.FirstFrame)]
    public async Task The_image_url_carries_the_role_the_model_names_or_the_tools_default(
        string tool, string? role, string expected)
    {
        var (sp, backend) = Host();
        using var _ = sp;
        var roleArgument = role is null ? "" : $",\"imageRole\":\"{role}\"";

        await Tool(sp, tool).InvokeAsync(
            $$"""{"kind":"video","prompt":"a cat surfing","imageUrl":"https://cdn.invalid/a.png"{{roleArgument}}}""");

        var input = Assert.Single(Assert.Single(backend.Requests).Inputs);
        Assert.Equal(expected, input.Role);
        Assert.Equal("https://cdn.invalid/a.png", input.Uri);
        Assert.False(backend.Requests[0].Options.ContainsKey("imageRole"));   // an argument, not a backend knob
    }

    [Fact]
    public async Task A_role_the_tools_do_not_offer_is_an_error_observation_and_calls_nothing()
    {
        var (sp, backend) = Host();
        using var _ = sp;

        var observation = JsonDocument.Parse(await Tool(sp, "generate_submit").InvokeAsync(
            """{"kind":"video","prompt":"x","imageUrl":"https://cdn.invalid/a.png","imageRole":"mask"}""")).RootElement;

        Assert.False(observation.GetProperty("ok").GetBoolean());
        Assert.Contains("imageRole", observation.GetProperty("error").GetString());
        Assert.Empty(backend.Requests);
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("generate_submit")]
    public void Each_schema_offers_the_role(string tool)
    {
        var (sp, _) = Host();
        using var _ = sp;

        var schema = JsonDocument.Parse(Tool(sp, tool).ParametersJsonSchema!).RootElement;

        var role = schema.GetProperty("properties").GetProperty("imageRole");
        Assert.Equal(["init", "first-frame", "reference"],
            role.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }
}
