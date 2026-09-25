using System.Text.Json.Nodes;
using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>One parsed streaming line, whichever wire produced it. <c>IsFinal</c> is the wire's own
/// end-of-answer signal (Ollama's <c>done:true</c>, an SSE <c>finish_reason</c>); whether it TERMINATES the
/// stream is <see cref="IHttpChatWire.EndsStreamOnFinal"/>'s call, because SSE sends usage after it.</summary>
internal readonly record struct HttpStreamLine(
    string? Text,
    TextUsage? Usage,
    bool IsFinal,
    string? FinishReason,
    IReadOnlyList<ToolCallDelta>? ToolCalls)
{
    /// <summary>The error the line reported IN BAND (<see cref="HttpBody.InBandError(System.Text.Json.JsonElement)"/>),
    /// read off the document the wire already parsed — also on a line the wire otherwise skips.</summary>
    public string? InBandError { get; init; }
}

/// <summary>The backend-specific half of an HTTP chat backend: where one completion is POSTed, how the
/// request body is shaped, and how the response — buffered or streamed — is read. Everything that is the
/// SAME for every HTTP chat backend (status→verdict mapping, the retry-once on a malformed body, the
/// in-band-error precedence, the inactivity clock, exactly one terminal chunk) lives once in
/// <see cref="HttpChatEngine"/>, which is the same composition the CLI providers use one directory over.</summary>
internal interface IHttpChatWire
{
    /// <summary>The absolute chat endpoint one completion is POSTed to.</summary>
    Uri Endpoint { get; }

    /// <summary>The configured model, used when the request pins none.</summary>
    string? DefaultModel { get; }

    /// <summary>The configured key, or null for a keyless endpoint. Whether one was given is what separates
    /// "not set up yet" (<see cref="ProviderVerdict.NotConfigured"/>) from "your key was rejected"
    /// (<see cref="ProviderVerdict.AuthFailed"/>) when the server answers 401/403.</summary>
    string? ApiKey { get; }

    /// <summary>Whether the key also travels in Azure's <c>api-key</c> header
    /// (<see cref="HttpEndpoint.ApplyAuth"/>).</summary>
    bool AzureConventions { get; }

    /// <summary>Whether a line reporting <see cref="HttpStreamLine.IsFinal"/> ENDS the stream. True for
    /// NDJSON (Ollama's <c>done:true</c> is the last line); false for SSE, which runs on to its
    /// <c>[DONE]</c> sentinel so the trailing usage chunk — sent AFTER the finish reason, with an EMPTY
    /// choices array — is still read.</summary>
    bool EndsStreamOnFinal { get; }

    /// <summary>The request body for one completion.</summary>
    JsonObject BuildPayload(TextRequest req, string model, bool stream);

    /// <summary>Read one buffered response body. True when the body is a well-formed reply — even with
    /// empty content (a content-filtered 200 has exactly that shape); verdicts are the engine's job.</summary>
    bool TryExtract(string body, out string text, out TextUsage? usage, out string? finishReason,
        out IReadOnlyList<TextToolCall>? toolCalls);

    /// <summary>Parse one streaming line (already stripped of any SSE <c>data:</c> prefix). A malformed or
    /// unrecognized line is the default value — skipped, never a throw.</summary>
    HttpStreamLine ParseStreamLine(string payload);
}
