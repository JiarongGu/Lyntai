using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Agents;

/// <summary>OPT-IN runs of the usage read against the REAL <c>claude</c> and <c>codex</c> binaries, each under an
/// EMPTY, signed-out home that this test makes and removes. No sign-in is started and no real account home is read:
/// the credentials the host process holds are removed from the child, so what comes back is the signed-out answer
/// and what is checked is that the reader recognises it, that the version gate answers on the real version string,
/// and that the side effects the descriptor declares are real (the server writes into the home it was given).
/// Runs only when <c>LYNTAI_LIVE_AGENT_USAGE</c> is set AND the binary is on PATH; otherwise each reports as SKIPPED.
/// A SIGNED-IN answer is not exercised here — that needs an account, which no test may touch.
///
/// Enable:  set LYNTAI_LIVE_AGENT_USAGE=1
/// </summary>
public class AgentUsageLiveTests
{
    private static bool Live => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_AGENT_USAGE"));

    private static readonly Dictionary<string, string?> NoInheritedCredentials = new()
    {
        ["ANTHROPIC_API_KEY"] = null, ["ANTHROPIC_AUTH_TOKEN"] = null, ["CLAUDE_CODE_OAUTH_TOKEN"] = null,
        ["OPENAI_API_KEY"] = null, ["CODEX_API_KEY"] = null,
    };

    // the standing environment points the agent at a throwaway home too, so even the version question
    // (asked of no account) cannot touch the home of whoever runs the suite
    private static CliUsageSource Source(IUsageDialect dialect, bool gate, ScratchDir standingHome) => new(
        dialect, new ProcessRunner(), new AgentUsageReadingOptions { Enabled = true, Timeout = TimeSpan.FromSeconds(90) },
        logger: null, command: null,
        new Dictionary<string, string?>(NoInheritedCredentials) { [dialect.HomeVariable] = standingHome.Path },
        requireValidatedVersion: gate);

    private static AgentAccount Account(string provider, ScratchDir home) =>
        new() { Provider = provider, Name = "empty", Home = home.Path };

    private static void SkipUnlessRunnable(string binary)
    {
        Skip.IfNot(Live, "LYNTAI_LIVE_AGENT_USAGE not set");
        Skip.IfNot(ProcessRunner.CommandExists(binary), $"no {binary} CLI on PATH");
    }

    [SkippableFact]
    public async Task Real_claude_version_gate_reads_the_installed_version_and_starts_a_server_only_if_it_is_validated()
    {
        SkipUnlessRunnable("claude");
        using var home = new ScratchDir("usage-claude-gate");
        using var standing = new ScratchDir("usage-claude-gate-standing");

        var reading = await Source(new ClaudeUsageDialect("claude-cli"), gate: true, standing).ReadAllowanceAsync(Account("claude-cli", home));

        Assert.Contains(reading.UnknownReason, new[] { AgentAllowanceUnknown.VersionNotValidated, AgentAllowanceUnknown.NotReported });
        if (reading.UnknownReason == AgentAllowanceUnknown.VersionNotValidated)
            Assert.Empty(Directory.EnumerateFileSystemEntries(home.Path)); // no server was started under the home
    }

    [SkippableFact]
    public async Task Real_claude_server_under_an_empty_home_answers_not_reported_and_writes_into_the_home()
    {
        SkipUnlessRunnable("claude");
        using var home = new ScratchDir("usage-claude-server");
        using var standing = new ScratchDir("usage-claude-server-standing");

        var reading = await Source(new ClaudeUsageDialect("claude-cli"), gate: false, standing).ReadAllowanceAsync(Account("claude-cli", home));

        Assert.Equal(AgentAllowanceUnknown.NotReported, reading.UnknownReason);
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(home.Path)); // the declared ChangesConfiguration is real
    }

    [SkippableFact]
    public async Task Real_codex_version_gate_reads_the_installed_version_and_starts_a_server_only_if_it_is_validated()
    {
        SkipUnlessRunnable("codex");
        using var home = new ScratchDir("usage-codex-gate");
        using var standing = new ScratchDir("usage-codex-gate-standing");

        var reading = await Source(new CodexUsageDialect("codex-cli"), gate: true, standing).ReadAllowanceAsync(Account("codex-cli", home));

        Assert.Contains(reading.UnknownReason, new[] { AgentAllowanceUnknown.VersionNotValidated, AgentAllowanceUnknown.NotSignedIn });
        if (reading.UnknownReason == AgentAllowanceUnknown.VersionNotValidated)
            Assert.Empty(Directory.EnumerateFileSystemEntries(home.Path));
    }

    [SkippableFact]
    public async Task Real_codex_server_under_an_empty_home_answers_not_signed_in_and_writes_into_the_home()
    {
        SkipUnlessRunnable("codex");
        using var home = new ScratchDir("usage-codex-server");
        using var standing = new ScratchDir("usage-codex-server-standing");

        var reading = await Source(new CodexUsageDialect("codex-cli"), gate: false, standing).ReadAllowanceAsync(Account("codex-cli", home));

        Assert.Equal(AgentAllowanceUnknown.NotSignedIn, reading.UnknownReason);
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(home.Path));
    }
}
