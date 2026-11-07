using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Providers.Acp;

/// <summary>How the ACP door reads a failure: the JSON-RPC code ACP defines, then what the door's
/// <see cref="IAcpExtensionReader"/>s read from the agent's own fields, then the agent's words only where no field
/// said, and never the agent's message text as a classification of its own. What the two agents measured put in
/// those fields is in <see cref="AcpExtensionReaders"/>.</summary>
internal static class AcpFailure
{
    /// <summary>The JSON-RPC code ACP gives an agent that wants a sign-in first.</summary>
    public const int AuthRequiredCode = -32000;

    /// <summary>A JSON-RPC error read as a failure.</summary>
    /// <param name="error">The response's <c>error</c> object.</param>
    /// <param name="extensions">The readers, and the failures they held from the turn's updates.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    /// <param name="words">The door's words for it, for the terminal's diagnostic.</param>
    public static AgentFailure FromError(JsonElement error, AcpExtensionSet extensions, TimeProvider time, out string words)
    {
        var code = WireJson.Int32(error, "code");
        var message = WireJson.String(error, "message") ?? (error.ValueKind == JsonValueKind.String ? error.GetString() : null);
        var data = WireJson.Object(error, "data") ?? default;
        var detail = WireJson.String(data, "message");
        words = Words(message, detail);

        var readings = new List<AgentFailure?>(extensions.Held);
        readings.AddRange(extensions.ReadError(error));
        // the code ACP defines for an agent that wants a sign-in first states exactly that
        readings.Add(code == AuthRequiredCode ? StreamJsonFailure.Access($"acp.error.code={code}", AccessReason.SignInAgain) : null);
        return StreamJsonFailure.Merge(readings, ProviderVerdictClassifier.ReadAgentFailure(words, time), $"acp.error.code={code}");
    }

    /// <summary>A turn the agent ended normally that says, as a field, that it failed.</summary>
    /// <param name="stated">What the readers stated; at least one.</param>
    /// <param name="extensions">The readers, and the failures they held from the turn's updates.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    /// <param name="words">The first words a reader gave, for the terminal's diagnostic.</param>
    public static AgentFailure FromStatedEnd(
        IReadOnlyList<AcpStatedFailure> stated, AcpExtensionSet extensions, TimeProvider time, out string? words)
    {
        words = stated.Select(s => s.Words).FirstOrDefault(w => !string.IsNullOrEmpty(w));
        var readings = new List<AgentFailure?>(extensions.Held);
        readings.AddRange(stated.Select(s => s.Failure));
        return StreamJsonFailure.Merge(readings, ProviderVerdictClassifier.ReadAgentFailure(words, time), stated[0].By);
    }

    public static ProviderVerdict VerdictOf(AgentFailure failure, string? words) =>
        ProviderVerdictClassifier.FromAgentFailure(failure, words);

    private static string Words(string? message, string? detail) =>
        (message, detail) switch
        {
            (null or "", _) => detail ?? "",
            (_, null or "") => message,
            _ => message.Contains(detail, StringComparison.Ordinal) ? message : $"{message}: {detail}",
        };
}
