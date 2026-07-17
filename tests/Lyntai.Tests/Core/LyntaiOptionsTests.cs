using Lyntai;
using Lyntai.Llm;

namespace Lyntai.Tests.Core;

public class LyntaiOptionsTests
{
    [Fact]
    public void Defaults_applied()
    {
        var options = new LyntaiOptions();

        Assert.Equal(TimeSpan.FromMinutes(2), options.ProviderTimeout);
        Assert.Equal(3, options.DeadHostThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DeadHostCooldown);
        Assert.Empty(options.DefaultCandidates);
        Assert.True(options.MemoryCapPerScope > 0);
    }

    [Fact]
    public void Env_override_beats_code_config()
    {
        var options = new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(100), DeadHostThreshold = 9 };
        var env = new Dictionary<string, string?>
        {
            ["LYNTAI_TIMEOUT_SECONDS"] = "7",
            ["LYNTAI_DEADHOST_THRESHOLD"] = "2",
            ["LYNTAI_DEADHOST_COOLDOWN_SECONDS"] = "45",
        };

        options.ApplyEnvOverrides(k => env.GetValueOrDefault(k));

        Assert.Equal(TimeSpan.FromSeconds(7), options.ProviderTimeout);
        Assert.Equal(2, options.DeadHostThreshold);
        Assert.Equal(TimeSpan.FromSeconds(45), options.DeadHostCooldown);
    }

    [Fact]
    public void Absent_env_leaves_config_untouched()
    {
        var options = new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(100) };

        options.ApplyEnvOverrides(_ => null);

        Assert.Equal(TimeSpan.FromSeconds(100), options.ProviderTimeout);
    }

    [Fact]
    public void Candidates_env_parses_provider_and_model_pairs()
    {
        var options = new LyntaiOptions();
        options.DefaultCandidates.Add(new LlmCandidate("code-configured"));

        options.ApplyEnvOverrides(k => k == "LYNTAI_DEFAULT_CANDIDATES" ? "claude-cli:sonnet, ollama" : null);

        Assert.Equal([new LlmCandidate("claude-cli", "sonnet"), new LlmCandidate("ollama")],
            options.DefaultCandidates);
    }

    [Fact]
    public void Model_resolution_request_beats_consumer_beats_default()
    {
        var options = new LyntaiOptions();
        options.DefaultModelByConsumer["default"] = "base-model";
        options.DefaultModelByConsumer["scoring"] = "judge-model";

        Assert.Equal("explicit", options.ResolveModel("scoring", "explicit"));
        Assert.Equal("judge-model", options.ResolveModel("scoring", null));
        Assert.Equal("base-model", options.ResolveModel("chat", null));
        Assert.Null(new LyntaiOptions().ResolveModel("chat", null));
    }
}
