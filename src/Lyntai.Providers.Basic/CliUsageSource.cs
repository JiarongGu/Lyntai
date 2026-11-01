using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Basic;

/// <summary>What is specific to one vendor's usage read: the question it is asked, the versions whose answer has
/// been validated, and the reading of that answer. Every vendor mechanic lives here, behind
/// <see cref="CliUsageSource"/>, so the host sees none of it.</summary>
internal interface IUsageDialect
{
    string ProviderId { get; }

    /// <summary>The variable the agent reads its configuration home from; the account's home is set in it.</summary>
    string HomeVariable { get; }

    ICliBackend Backend { get; }

    /// <summary>The arguments that make the binary print its version and exit.</summary>
    IReadOnlyList<string> VersionArgs { get; }

    /// <summary>The version in what the binary printed, or null when it printed none this reader recognises.</summary>
    string? ParseVersion(string output);

    /// <summary>The versions whose real usage response has been validated against <see cref="Read"/>, exactly.</summary>
    IReadOnlySet<string> ValidatedVersions { get; }

    /// <summary>The arguments that start the vendor's server on stdio.</summary>
    IReadOnlyList<string> ServerArgs { get; }

    /// <summary>The exchange, in order, for a client of this name and version; a dialect whose protocol carries no
    /// client ignores both. The last step is the usage question, and its answer is <see cref="IsAnswer"/>.</summary>
    IReadOnlyList<UsageStep> StepsFor(string clientName, string clientVersion);

    /// <summary>Whether a stdout line is the answer to the usage question, which ends the exchange.</summary>
    bool IsAnswer(string line);

    /// <summary>The unknown reason when <paramref name="answer"/>, the answer to a step before the last, is an error,
    /// else null. The usage question's own answer is read by <see cref="Read"/>.</summary>
    string? FailureOf(string answer);

    /// <summary>The reading of the lines the exchange saw. Never throws: an answer it does not recognise is unknown.</summary>
    AgentAllowanceReading Read(IReadOnlyList<string> lines, DateTimeOffset measuredAt);
}

/// <summary>One step of a usage exchange: the lines written, then the stdout line that answers them. A notification
/// goes in the same step as the request after it, since nothing answers a notification.</summary>
internal sealed record UsageStep(IReadOnlyList<string> Writes, Func<string, bool> IsAnswerTo);

/// <summary>An <see cref="IAgentUsageSource"/> over a CLI agent's own server. One start, one question: the version is
/// read first, and only a version whose response is validated has its server started at all, under the folder the
/// host names or else a fresh empty one so the vendor loads no project's extensions. The process is ended on every
/// path out. The library keeps nothing the answer carries beyond the windows and the plan: no account id, no token.</summary>
internal sealed class CliUsageSource(
    IUsageDialect dialect,
    IProcessRunner runner,
    AgentUsageReadingOptions? options,
    ILogger? logger,
    string? command,
    IReadOnlyDictionary<string, string?>? environment,
    TimeProvider? time = null,
    bool requireValidatedVersion = true,
    string? agent = null) : IAgentUsageSource
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<AgentAllowanceReading> ReadAllowanceAsync(AgentAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (options is not { Enabled: true }) return Unknown(AgentAllowanceUnknown.Disabled);
        ct.ThrowIfCancellationRequested();

        if (!(string.Equals(account.Provider, dialect.ProviderId, StringComparison.Ordinal)
                || string.Equals(account.Provider, agent, StringComparison.Ordinal))
            || !AgentAccounts.TryMap(account, new AgentAccountMapping(dialect.HomeVariable), out var accountEnvironment, out _))
            return Unknown(AgentAllowanceUnknown.AccountRefused);

        if (!TryChooseWorkingDirectory(account, out var named)) return Unknown(AgentAllowanceUnknown.AccountRefused);

        var (exe, prefix) = CliCommand.Resolve(command, dialect.Backend);

        if (requireValidatedVersion)
        {
            var version = await ReadVersionAsync(exe, prefix, ct).ConfigureAwait(false);
            if (version.Failed is { } failed) return Unknown(failed);
            if (!dialect.ValidatedVersions.Contains(version.Value!)) return Unknown(AgentAllowanceUnknown.VersionNotValidated);
        }

        var workDir = named ?? Path.Combine(Path.GetTempPath(), $"lyntai-usage-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(workDir);
            var (lines, outcome) = await ExchangeAsync(exe, [.. prefix, .. dialect.ServerArgs], workDir,
                AgentAccounts.Merge(environment, accountEnvironment), options.Timeout, ct).ConfigureAwait(false);
            return outcome is null ? dialect.Read(lines, _time.GetUtcNow()) : Unknown(outcome);
        }
        catch (NotSupportedException)
        {
            return Unknown(AgentAllowanceUnknown.RunnerUnsupported);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("{Provider} usage read could not start its server: {Error}", dialect.ProviderId, ex.GetType().Name);
            return Unknown(AgentAllowanceUnknown.SpawnFailed);
        }
        finally
        {
            // only the folder this read made: a named one is the host's
            if (named is null) await RemoveAsync(workDir).ConfigureAwait(false);
        }
    }

    // The host's folder for this account, or null for a fresh one. False when what it names cannot be started in,
    // which is refused before any spawn, the version probe included.
    private bool TryChooseWorkingDirectory(AgentAccount account, out string? named)
    {
        named = null;
        if (options?.WorkingDirectory is not { } choose) return true;
        try
        {
            named = choose(account);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("{Provider} usage read: the host's working directory choice failed: {Error}", dialect.ProviderId, ex.GetType().Name);
            return false;
        }
        return named is null || (Path.IsPathFullyQualified(named) && !named.Any(char.IsControl));
    }

    // The server was just killed and Windows holds its working directory until the process is gone, so the first
    // attempts can fail; a scratch folder left after the last one is the OS temp sweep's.
    private static async Task RemoveAsync(string directory)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    // Writes each step only once the answer to the one before has arrived, and keeps stdin OPEN until the last answer:
    // codex's server ends the moment stdin closes, before it answers. A line pushed between answers is kept and read
    // past. Leaving the loop ends the child (the runner kills its tree when the stream is abandoned, times out or is
    // cancelled), and completing the channel releases the writer.
    private async Task<(List<string> Lines, string? Unknown)> ExchangeAsync(
        string exe, IReadOnlyList<string> args, string workDir, IReadOnlyDictionary<string, string?>? env,
        TimeSpan timeout, CancellationToken ct)
    {
        var steps = dialect.StepsFor(options!.ClientName, options.ClientVersion);
        var requests = Channel.CreateUnbounded<string>();
        var step = 0;
        foreach (var write in steps[step].Writes) requests.Writer.TryWrite(write);
        var lines = new List<string>();
        try
        {
            await foreach (var line in runner.ExchangeLinesAsync(exe, args, requests.Reader.ReadAllAsync(ct),
                               maxDuration: timeout, workingDirectory: workDir, environment: env, ct: ct).ConfigureAwait(false))
            {
                lines.Add(line);
                if (!steps[step].IsAnswerTo(line)) continue;
                if (step == steps.Count - 1) return (lines, null);
                if (dialect.FailureOf(line) is { } failure) return (lines, failure);
                step++;
                foreach (var write in steps[step].Writes) requests.Writer.TryWrite(write);
            }
            return (lines, AgentAllowanceUnknown.NoAnswer);
        }
        catch (ProcessTimeoutException) { return (lines, AgentAllowanceUnknown.TimedOut); }
        catch (ProcessRunException) { return (lines, AgentAllowanceUnknown.NoAnswer); }
        finally { requests.Writer.TryComplete(); }
    }

    private async Task<(string? Value, string? Failed)> ReadVersionAsync(
        string exe, IReadOnlyList<string> prefix, CancellationToken ct)
    {
        try
        {
            // the standing environment only: the version question is asked of no account's home
            var result = await runner.RunAsync(exe, [.. prefix, .. dialect.VersionArgs], inactivityTimeout: VersionTimeout,
                maxDuration: VersionTimeout, environment: environment, ct: ct).ConfigureAwait(false);
            if (!result.Success) return (null, AgentAllowanceUnknown.VersionUnreadable);
            return dialect.ParseVersion(result.StdOut) is { } version ? (version, null) : (null, AgentAllowanceUnknown.VersionUnreadable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("{Provider} usage read could not ask the binary its version: {Error}", dialect.ProviderId, ex.GetType().Name);
            return (null, AgentAllowanceUnknown.SpawnFailed);
        }
    }

    private AgentAllowanceReading Unknown(string reason) => AgentAllowanceReading.Unknown(reason, _time.GetUtcNow());
}
