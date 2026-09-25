using System.Text;
using System.Text.Json.Nodes;
using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>What one buffered JSON POST came back as: the body, or the verdict it failed with.</summary>
/// <param name="Body">The response body; null when the call failed.</param>
/// <param name="Verdict"><see cref="ProviderVerdict.Ok"/> with a body, else why there is none.</param>
/// <param name="Detail">The failure's detail; null on success.</param>
internal readonly record struct HttpJsonReply(string? Body, ProviderVerdict Verdict, string? Detail);

/// <summary>The buffered JSON POST every HTTP surface in this package makes — the chat engine, the vector
/// transport and the rerank transport — with ONE policy for how it fails:
/// <list type="bullet">
/// <item>a non-2xx status classified by <see cref="ProviderVerdictClassifier.FromHttpFailure(System.Net.HttpStatusCode, string, bool)"/>,
/// credentials-aware (a 401 to a call that carried no key is NotConfigured, not AuthFailed);</item>
/// <item>the deadline (one <c>CancelAfter</c> over the one request) as
/// <see cref="ProviderVerdict.Timeout"/>, the caller's own cancellation propagating;</item>
/// <item>a transport throw (socket, DNS, TLS) as <see cref="ProviderVerdict.Failed"/> carrying its message —
/// a verdict, never an exception escaping the provider.</item>
/// </list></summary>
internal static class HttpJsonCall
{
    /// <summary>A POST of <paramref name="payload"/> as UTF-8 JSON (no BOM), with the auth convention
    /// applied.</summary>
    public static HttpRequestMessage Post(Uri endpoint, JsonObject payload, string? apiKey, bool azureConventions)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        HttpEndpoint.ApplyAuth(request, apiKey, azureConventions);
        return request;
    }

    /// <summary>Send <paramref name="request"/> under <paramref name="timeout"/> and read the body, or answer
    /// the failure as a verdict. The request is disposed.</summary>
    /// <param name="http">The client.</param>
    /// <param name="request">The POST.</param>
    /// <param name="timeout">The deadline for this one request.</param>
    /// <param name="hasCredentials">Whether the call carried credentials.</param>
    /// <param name="id">The provider id, leading every detail.</param>
    /// <param name="what">The surface, for the detail (<c>embeddings</c>, <c>rerank</c>); null for chat.</param>
    /// <param name="ct">The caller's cancellation, which propagates.</param>
    public static async Task<HttpJsonReply> SendAsync(HttpClient http, HttpRequestMessage request,
        TimeSpan timeout, bool hasCredentials, string id, string? what, CancellationToken ct)
    {
        var surface = what is null ? "" : what + " ";
        using (request)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);
                using var response = await http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await HttpBody.SafeRead(response, timeoutCts.Token).ConfigureAwait(false);
                    return new(null,
                        ProviderVerdictClassifier.FromHttpFailure(response.StatusCode, errorBody, hasCredentials),
                        $"{id}: {surface}HTTP {(int)response.StatusCode} {HttpBody.Head(errorBody)}");
                }
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                return new(body, ProviderVerdict.Ok, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                return new(null, ProviderVerdict.Timeout, $"{id}: no {surface}response within {timeout}");
            }
            catch (HttpRequestException ex)
            {
                return new(null, ProviderVerdict.Failed, $"{id}: {ex.Message}");
            }
        }
    }
}
