using Lyntai.Llm;
using Lyntai.Llm.Cli;

namespace Lyntai.Tests.Fakes;

/// <summary>A minimal <see cref="ICliProviderDialect"/> for exercising <see cref="CliProviderEngine"/>
/// without any real CLI's vocabulary. Everything a dialect can vary is settable here, so an engine test
/// can pin the GENERIC contract (prompt delivery, unsupported capabilities, clocks) rather than whatever
/// the claude dialect happens to do.
///
/// Its line protocol is deliberately trivial: <c>text:…</c> → content, <c>result:…</c> → the terminal
/// result, anything else → ignored.</summary>
public sealed class FakeCliDialect : CliProviderDialectBase
{
    public override string Id => IdValue;
    public string IdValue { get; set; } = "fake-cli";

    public override string DefaultCommand => DefaultCommandValue;
    public string DefaultCommandValue { get; set; } = "fakecli";

    public override IReadOnlyList<string> CommandEnvironmentVariables => EnvironmentVariables;
    public IReadOnlyList<string> EnvironmentVariables { get; set; } = ["LYNTAI_PROVIDER_CMD", "FAKE_CLI_CMD"];

    public override CliPromptDelivery PromptDelivery => Delivery;
    public CliPromptDelivery Delivery { get; set; } = CliPromptDelivery.Stdin;

    public override TimeSpan MaintenanceTimeout => MaintenanceClock;
    public TimeSpan MaintenanceClock { get; set; } = TimeSpan.FromSeconds(30);

    public override TimeSpan LoginTimeout => LoginClock;
    public TimeSpan LoginClock { get; set; } = TimeSpan.FromMinutes(10);

    public override IReadOnlyList<string>? VersionArgs => VersionArgsValue;
    public IReadOnlyList<string>? VersionArgsValue { get; set; } = ["--version"];

    public override IReadOnlyList<string>? UpdateArgs => UpdateArgsValue;
    public IReadOnlyList<string>? UpdateArgsValue { get; set; }

    public override IReadOnlyList<string>? AuthStatusArgs => AuthStatusArgsValue;
    public IReadOnlyList<string>? AuthStatusArgsValue { get; set; }

    public override IReadOnlyList<string>? LogoutArgs => LogoutArgsValue;
    public IReadOnlyList<string>? LogoutArgsValue { get; set; }

    /// <summary>Set to make login/install supported; the args are used verbatim.</summary>
    public IReadOnlyList<string>? LoginArgsValue { get; set; }
    public IReadOnlyList<string>? InstallArgsValue { get; set; }

    /// <summary>Appends the tool-host args, like the claude dialect and unlike the codex one — this fake
    /// stands in for an ordinary options-terminated CLI.</summary>
    public override IReadOnlyList<string> BuildCompletionArgs(
        LlmRequest request, IReadOnlyList<string> toolHostArgs) =>
        request.Model is { Length: > 0 } model
            ? ["run", "--model", model, .. toolHostArgs]
            : ["run", .. toolHostArgs];

    public override CliOutputEvent ParseLine(string line) => line switch
    {
        var l when l.StartsWith("text:") => CliOutputEvent.Content(l["text:".Length..]),
        var l when l.StartsWith("result:") => CliOutputEvent.Result(l["result:".Length..], new LlmUsage(1, 2)),
        var l when l.StartsWith("fail:") => CliOutputEvent.Failure(l["fail:".Length..]),
        _ => CliOutputEvent.Ignored,
    };

    public override ProviderAuthStatus? ParseAuthStatus(string output) =>
        output.Contains("signed-in") ? new ProviderAuthStatus(true, "fake", "someone@example.invalid") : null;

    public override bool TryBuildLoginArgs(ProviderLoginRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        if (LoginArgsValue is null) return base.TryBuildLoginArgs(request, out args, out refusal);
        args = LoginArgsValue;
        refusal = null;
        return true;
    }

    public override bool TryBuildInstallArgs(ProviderInstallRequest? request, out IReadOnlyList<string> args, out string? refusal)
    {
        if (InstallArgsValue is null) return base.TryBuildInstallArgs(request, out args, out refusal);
        args = InstallArgsValue;
        refusal = null;
        return true;
    }
}
