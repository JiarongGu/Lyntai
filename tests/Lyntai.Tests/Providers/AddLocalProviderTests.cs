using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.Local;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// Deterministic wiring tests for the local (LLamaSharp) provider. These never load a real model or
/// touch the native backend: a missing model file (and/or an absent backend in the test run) both
/// resolve to a Failed verdict, which is exactly the router-fallback contract we want to pin. Real
/// inference is covered by the opt-in <see cref="LocalProviderLiveTests"/>.
/// </summary>
public class AddLocalProviderTests
{
    private static LlmRequest Ask(string prompt = "hi") => new() { Messages = [LlmMessage.User(prompt)] };

    // scratch under devtools/_* (family rule: never OS temp), gitignored
    private static string ScratchDir()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-models"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MissingModel() => Path.Combine(ScratchDir(), $"does-not-exist-{Guid.NewGuid():N}.gguf");

    [Fact]
    public void AddLocalProvider_registers_a_provider_under_the_id()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddLocalProvider(MissingModel(), id: "local"));
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<ILlmProvider>(), p => p.Id == "local");
    }

    [Fact]
    public void AddLocalProvider_honors_a_custom_id_and_options()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddLocalProvider(MissingModel(), o => o.GpuLayerCount = 20, id: "phi-local"));
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<ILlmProvider>(), p => p.Id == "phi-local");
    }

    [Fact]
    public void IsAvailable_is_false_when_the_model_file_is_missing()
    {
        using var provider = new LocalProvider("local", new LocalModelOptions { ModelPath = MissingModel() }, new LyntaiOptions());
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void IsAvailable_is_true_when_the_model_file_exists()
    {
        var path = Path.Combine(ScratchDir(), $"present-{Guid.NewGuid():N}.gguf");
        File.WriteAllText(path, "placeholder — presence is all IsAvailable checks");
        try
        {
            using var provider = new LocalProvider("local", new LocalModelOptions { ModelPath = path }, new LyntaiOptions());
            Assert.True(provider.IsAvailable);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CompleteAsync_returns_Failed_not_throw_when_the_model_cannot_load()
    {
        // an absent model (and/or no native backend in the test run) must degrade to a Failed verdict,
        // never an escaping exception — that's what lets the router fall over to the next candidate
        using var provider = new LocalProvider("local", new LocalModelOptions { ModelPath = MissingModel() },
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(5) });

        var reply = await provider.CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
        Assert.Equal("", reply.Text);
    }

    [Fact]
    public async Task Router_skips_an_unavailable_local_candidate()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddLocalProvider(MissingModel())    // IsAvailable false → skipped by the router
            .DefaultCandidates("local"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>().CompleteAsync(Ask());

        Assert.NotEqual(LlmVerdict.Ok, reply.Verdict); // no live candidate remained
    }
}
