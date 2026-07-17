using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddClaudeCliProvider` shows up right on the builder.
namespace Lyntai;

public static class ClaudeCliBuilderExtensions
{
    /// <summary>Register the `claude` CLI provider (id "claude-cli"). The spawned command honors
    /// <c>LYNTAI_PROVIDER_CMD</c> / <c>CLAUDE_CMD</c> env overrides (tests/e2e point these at the
    /// deterministic provider stub).</summary>
    public static LyntaiBuilder AddClaudeCliProvider(this LyntaiBuilder builder)
    {
        builder.AddProvider(sp => new ClaudeCliProvider(
            sp.GetRequiredService<ProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeCliProvider>>()));
        return builder;
    }
}
