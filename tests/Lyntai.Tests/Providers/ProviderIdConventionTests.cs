using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>A provider's default id is its BACKEND's name, and every registration can be given another — so
/// two installs of one CLI (two accounts, two portable copies) are two candidates rather than one the
/// first-wins router can never get past.</summary>
public class ProviderIdConventionTests
{
    private static ServiceProvider Compose(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(new FakeProcessRunner());
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_llamasharp_default_id_is_the_backend_name()
    {
        using var sp = Compose(b => b.AddLlamaSharpProvider("does-not-need-to-exist.gguf"));

        Assert.Equal("llamasharp", Assert.Single(sp.GetServices<IModelProvider>()).Id);
    }

    [Fact]
    public void Two_claude_registrations_are_two_candidates()
    {
        using var sp = Compose(b => b
            .AddClaudeCliProvider()
            .AddClaudeCliProvider(command: "claude-portable", id: "claude-work"));

        Assert.Equal(["claude-cli", "claude-work"], sp.GetServices<IModelProvider>().Select(p => p.Id));
    }

    [Fact]
    public void Two_codex_registrations_are_two_candidates()
    {
        using var sp = Compose(b => b
            .AddCodexCliProvider()
            .AddCodexCliProvider(command: "codex-portable", id: "codex-work"));

        Assert.Equal(["codex-cli", "codex-work"], sp.GetServices<IModelProvider>().Select(p => p.Id));
    }

    [Fact]
    public void An_agent_session_is_keyed_by_its_registration_id()
    {
        using var sp = Compose(b => b
            .AddClaudeCliAgentSession(id: "claude-work")
            .AddCodexCliAgentSession(id: "codex-work"));

        Assert.IsType<ClaudeAgentSession>(sp.GetRequiredKeyedService<IAgentSession>("claude-work"));
        Assert.IsType<CodexAgentSession>(sp.GetRequiredKeyedService<IAgentSession>("codex-work"));
    }

    [Fact]
    public void A_blank_id_is_refused_at_registration()
    {
        Assert.Throws<ArgumentException>(() => Compose(b => b.AddClaudeCliProvider(id: " ")));
        Assert.Throws<ArgumentException>(() => Compose(b => b.AddCodexCliProvider(id: "")));
    }

    [Fact]
    public void A_provisioner_keyed_on_a_registrations_own_id_wins_and_the_backends_serves_the_rest()
    {
        var own = new Provisioner();
        var shared = new Provisioner();
        var unkeyed = new Provisioner();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICliToolProvisioner>("claude-work", own);
        services.AddKeyedSingleton<ICliToolProvisioner>(ClaudeCliProvider.ProviderId, shared);
        services.AddSingleton<ICliToolProvisioner>(unkeyed);
        using var sp = services.BuildServiceProvider();

        Assert.Same(own, Resolve(sp, "claude-work"));
        Assert.Same(shared, Resolve(sp, "claude-home"));
        Assert.Same(shared, Resolve(sp, ClaudeCliProvider.ProviderId));
        using var bare = new ServiceCollection().AddSingleton<ICliToolProvisioner>(unkeyed).BuildServiceProvider();
        Assert.Same(unkeyed, Resolve(bare, "claude-home"));

        static ICliToolProvisioner? Resolve(IServiceProvider sp, string id) =>
            Lyntai.Providers.Basic.CliComposition.Provisioner(sp, id, ClaudeCliProvider.ProviderId);
    }

    [Fact]
    public void A_connector_can_be_keyed_on_one_registration()
    {
        Assert.Equal(ClaudeCliProvider.ProviderId, new ClaudeCliMcpConnector().ProviderId);
        Assert.Equal("claude-work", new ClaudeCliMcpConnector("claude-work").ProviderId);
    }

    private sealed class Provisioner : ICliToolProvisioner
    {
        public Task<CliToolSession> ProvisionAsync(CancellationToken ct = default) =>
            Task.FromResult(new CliToolSession([]));
    }
}
