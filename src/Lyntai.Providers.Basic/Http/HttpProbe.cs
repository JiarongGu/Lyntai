using System.Net;
using System.Text.Json;
using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>The probe every HTTP backend in this package answers with: one GET of the server's model listing,
/// which generates nothing, read as reachable, key accepted, and what the server lists.
/// <para>A 404 is REACHABLE, not down: some embedding and rerank servers serve no listing route, and reaching
/// them is the whole answer there is. A configured model the listing omits leaves the backend available —
/// llama-server serves whatever is loaded and lists its own alias — and the detail says so.</para></summary>
internal static class HttpProbe
{
    /// <param name="httpFactory">The per-call client source.</param>
    /// <param name="disposeHttpClient">Whether Lyntai owns, and so disposes, the client.</param>
    /// <param name="timeout">The deadline for the one request.</param>
    /// <param name="endpoint">The listing route.</param>
    /// <param name="authenticate">Applies the backend's auth convention to the request.</param>
    /// <param name="hasCredentials">Whether a key was sent — what separates "refused" from "none".</param>
    /// <param name="configuredModel">The registration's model, or null.</param>
    /// <param name="read">Reads the listed names out of a 2xx body.</param>
    /// <param name="matches">Whether a listed name (first) is the configured model (second).</param>
    /// <param name="ct">The caller's cancellation, which propagates.</param>
    public static async Task<ProviderProbeResult> RunAsync(
        Func<HttpClient> httpFactory, bool disposeHttpClient, TimeSpan timeout, Uri endpoint,
        Action<HttpRequestMessage> authenticate, bool hasCredentials, string? configuredModel,
        Func<JsonElement, IEnumerable<string>> read, Func<string, string, bool> matches, CancellationToken ct)
    {
        using var owned = disposeHttpClient ? httpFactory() : null;
        var http = owned ?? httpFactory();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        authenticate(request);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);
            using var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(false, hasCredentials
                    ? $"the server refused the key (HTTP {(int)response.StatusCode})"
                    : $"no key is configured, and the server wants one (HTTP {(int)response.StatusCode})");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(true, $"reachable; the server lists no models ({endpoint.AbsolutePath} answered 404)");
            if (!response.IsSuccessStatusCode)
            {
                var error = await HttpBody.SafeRead(response, deadline.Token).ConfigureAwait(false);
                return new(false, $"HTTP {(int)response.StatusCode} {HttpBody.Head(error)}");
            }

            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return Listed(Parse(body, read), configuredModel, matches);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new(false, $"no response from {endpoint} within {timeout}");
        }
        catch (HttpRequestException ex)
        {
            return new(false, $"not reachable at {endpoint}: {ex.Message}");
        }
    }

    /// <summary>The names in a listing, or null when the body is not the JSON the reader expects.</summary>
    private static IReadOnlyList<string>? Parse(string body, Func<JsonElement, IEnumerable<string>> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return [.. read(doc.RootElement)];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static ProviderProbeResult Listed(IReadOnlyList<string>? models, string? configured,
        Func<string, string, bool> matches)
    {
        if (models is null) return new(true, "reachable; the model listing could not be read");
        if (models.Count == 0) return new(true, "reachable; the server lists no models") { Models = models };
        if (string.IsNullOrWhiteSpace(configured))
            return new(true, $"lists {models.Count} model(s)") { Models = models };

        var served = models.FirstOrDefault(m => matches(m, configured));
        return served is not null
            ? new(true, $"lists {models.Count} model(s), including the configured one", Model: served) { Models = models }
            : new(true, $"the configured model '{configured}' is not among the {models.Count} listed — a server "
                + "that serves whatever is loaded ignores the name") { Models = models };
    }
}
