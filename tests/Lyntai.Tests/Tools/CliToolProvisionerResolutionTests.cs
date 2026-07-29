using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Tools;

/// <summary>
/// Two CLI providers that each run their OWN agent loop must be able to host tools side by side with
/// different dialects. The provisioner is therefore resolved KEYED by provider id, with the first
/// registration also serving as the unkeyed fallback (so a lone registration keeps working for any
/// provider that doesn't ask by key).
/// </summary>
public class CliToolProvisionerResolutionTests
{
    [Fact]
    public void Each_dialect_gets_its_own_keyed_provisioner()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddMcpToolHost(new StubDialect("claude-cli", "--claude-flag"))
            .AddMcpToolHost(new StubDialect("gemini-cli", "--gemini-flag"))
            .AddTool(_ => new FunctionTool("echo", (a, _) => Task.FromResult(a))));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetKeyedService<ICliToolProvisioner>("claude-cli"));
        Assert.NotNull(sp.GetKeyedService<ICliToolProvisioner>("gemini-cli"));
        Assert.NotSame(
            sp.GetKeyedService<ICliToolProvisioner>("claude-cli"),
            sp.GetKeyedService<ICliToolProvisioner>("gemini-cli"));
    }

    [Fact]
    public void First_registration_is_also_the_unkeyed_fallback()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddMcpToolHost(new StubDialect("claude-cli", "--claude-flag"))
            .AddMcpToolHost(new StubDialect("gemini-cli", "--gemini-flag")));
        using var sp = services.BuildServiceProvider();

        Assert.Same(sp.GetService<ICliToolProvisioner>(), sp.GetKeyedService<ICliToolProvisioner>("claude-cli"));
    }

    [Fact]
    public async Task Claude_provider_picks_its_own_dialect_when_another_is_registered_first()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddMcpToolHost(new StubDialect("gemini-cli", "--gemini-flag"))   // registered FIRST — owns the unkeyed slot
            .AddMcpToolHost(new ClaudeCliMcpDialect())
            .AddTool(_ => new FunctionTool("echo", (a, _) => Task.FromResult(a))));
        using var sp = services.BuildServiceProvider();

        var provisioner = sp.GetRequiredKeyedService<ICliToolProvisioner>("claude-cli");
        await using var session = await provisioner.ProvisionAsync();

        Assert.Contains("--mcp-config", session.ExtraArgs);      // the claude dialect, not gemini's
        Assert.DoesNotContain("--gemini-flag", session.ExtraArgs);
    }

    private sealed class StubDialect(string providerId, string flag) : IMcpCliDialect
    {
        public string ProviderId => providerId;

        public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext context, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<string>>([flag, context.Endpoint.Url]);
    }
}
