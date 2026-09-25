using Lyntai.Inference;
using Lyntai;
using Lyntai.Providers.LlamaSharp;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// Deterministic wiring tests for the local (LLamaSharp) provider. These never load a real model or
/// touch the native backend: a missing model file (and/or an absent backend in the test run) both
/// resolve to a Failed verdict, which is exactly the router-fallback contract we want to pin. Real
/// inference is covered by the opt-in <see cref="LlamaSharpProviderLiveTests"/>.
/// </summary>
public class AddLlamaSharpTests : IDisposable
{
    private readonly ScratchDir _scratch = new("llamasharp");

    public void Dispose() => _scratch.Dispose();

    private static TextRequest Ask(string prompt = "hi") => new() { Messages = [TextMessage.User(prompt)] };

    private string MissingModel() => _scratch.Combine("does-not-exist.gguf");

    [Fact]
    public void AddLlamaSharp_registers_a_provider_under_the_id()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddLlamaSharpProvider(MissingModel(), id: "local"));
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<IModelProvider>(), p => p.Id == "local");
    }

    [Fact]
    public void AddLlamaSharp_honors_a_custom_id_and_options()
    {
        // the options action runs AFTER the positional path, so pointing it at a file that exists is what
        // makes the provider available — observable proof the action reached the provider
        var present = _scratch.File("present.gguf", "placeholder");
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddLlamaSharpProvider(MissingModel(), o => o.ModelPath = present, id: "phi-local"));
        using var sp = services.BuildServiceProvider();

        var provider = Assert.Single(sp.GetServices<IModelProvider>(), p => p.Id == "phi-local");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public void IsAvailable_is_false_when_the_model_file_is_missing()
    {
        using var provider = new LlamaSharpProvider("local", new LlamaSharpOptions { ModelPath = MissingModel() }, new LyntaiOptions());
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void IsAvailable_is_true_when_the_model_file_exists()
    {
        var path = _scratch.File("present.gguf", "placeholder — presence is all IsAvailable checks");

        using var provider = new LlamaSharpProvider("local", new LlamaSharpOptions { ModelPath = path }, new LyntaiOptions());
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public async Task CompleteAsync_returns_Failed_not_throw_when_the_model_cannot_load()
    {
        // an absent model (and/or no native backend in the test run) must degrade to a Failed verdict,
        // never an escaping exception — that's what lets the router fall over to the next candidate
        using var provider = new LlamaSharpProvider("local", new LlamaSharpOptions { ModelPath = MissingModel() },
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(5) });

        var reply = await provider.CompleteAsync(Ask());

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
        Assert.Equal("", reply.Text);
    }

    [Fact]
    public async Task Router_skips_an_unavailable_local_candidate()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddLlamaSharpProvider(MissingModel(), id: "local")    // IsAvailable false → skipped by the router
            .UseDefaultCandidates("local"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask());

        Assert.NotEqual(ProviderVerdict.Ok, reply.Verdict); // no live candidate remained
        // SKIPPED, not called: calling it would also fail, so only the reason tells the two apart
        Assert.Contains("provider reports unavailable", reply.Detail, StringComparison.Ordinal);
    }
}
