using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.Basic;

/// <summary>A CLI door's <see cref="IAgentAuthSource"/>: the same status and login commands the inference provider drives
/// (<see cref="ICliBackend.AuthStatusArgs"/>, <see cref="ICliBackend.TryBuildLoginArgs"/>, <see cref="ICliBackend.ParseAuthStatus"/>),
/// spawned under an account's own home. The status is the inference engine's own answer, so the two readings of one program can
/// never differ; only the login is its own, because the engine returns its output at the end and a flow that prints a code and
/// then waits needs each line while it waits.</summary>
internal sealed class CliAuthSource(
    ICliBackend backend,
    AgentAccountMapping mapping,
    string providerId,
    string? agent,
    IProcessRunner runner,
    LyntaiOptions options,
    ILogger? logger,
    string? command,
    IReadOnlyDictionary<string, string?>? environment) : IAgentAuthSource
{
    public Task<ProviderAuthStatus> StatusAsync(AgentAccount? account = null, CancellationToken ct = default)
    {
        if (!TryEnvironment(account, out var env, out var refusal))
            return Task.FromResult(new ProviderAuthStatus(false, Detail: refusal));
        return new CliProviderEngine(backend, runner, options, logger, command, null, env).StatusAsync(ct);
    }

    public async Task<ProviderAuthResult> LoginAsync(
        AgentAccount? account = null, ProviderLoginRequest? request = null, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        if (!TryEnvironment(account, out var env, out var refusal)) return new ProviderAuthResult(false, Detail: refusal);
        if (!backend.TryBuildLoginArgs(request, out var loginArgs, out refusal)) return new ProviderAuthResult(false, Detail: refusal);

        var (exe, prefix) = CliCommand.Resolve(command, backend);
        var lines = new List<string>();
        var label = $"{backend.Id} login";
        try
        {
            // a login prints a URL or a code and then goes SILENT while a person acts, so both clocks are the one budget
            var cwd = CliProviderEngine.NeutralWorkingDirectory;
            Directory.CreateDirectory(cwd);
            await foreach (var line in runner.StreamLinesAsync(exe, [.. prefix, .. loginArgs], null,
                               backend.LoginTimeout, backend.LoginTimeout, cwd, env, ct).ConfigureAwait(false))
            {
                lines.Add(line);
                onOutput?.Invoke(line);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ProcessTimeoutException) { return new ProviderAuthResult(false, Detail: $"{label} did not complete within {backend.LoginTimeout}"); }
        catch (ProcessRunException e) { return new ProviderAuthResult(false, Detail: $"exit {e.ExitCode}: {e.StdErrTail}"); }
        catch (Exception e) { return new ProviderAuthResult(false, Detail: $"{label} failed: {e.Message}"); }

        // the state it left behind, read as a status is read: what the command claimed is not the authority
        var status = await StatusAsync(account, ct).ConfigureAwait(false);
        var output = string.Join('\n', lines).Trim();
        return new ProviderAuthResult(true, status, output.Length > 0 ? output : null);
    }

    private bool TryEnvironment(AgentAccount? account, out IReadOnlyDictionary<string, string?>? env, out string? refusal)
    {
        env = environment;
        refusal = null;
        if (account is null) return true;

        if (!string.Equals(account.Provider, providerId, StringComparison.Ordinal)
            && !string.Equals(account.Provider, agent, StringComparison.Ordinal))
        {
            refusal = $"account '{account.Name}' belongs to '{account.Provider}', not to provider '{providerId}'"
                + (agent is null ? "" : $" or its agent '{agent}'");
            return false;
        }

        if (!AgentAccounts.TryMap(account, mapping, out var accountEnvironment, out refusal)) return false;
        env = AgentAccounts.Merge(environment, accountEnvironment);
        return true;
    }
}
