using System.Net;
using Lyntai.Agents;
using Lyntai.Tools.Mcp.Hosting;

namespace Lyntai.Tests.Tools;

/// <summary>The host's bearer check accepts EXACTLY the minted token. The compare is constant-time, which a
/// unit test cannot observe; what these pin is that the byte compare gives the right answer — a near miss,
/// a prefix, a case change and an absent header are all 401, and the real token gets past the gate (a 404 on
/// a path the host does not serve is the proof).</summary>
public class McpToolHostAuthTests
{
    private const string Token = "the-real-token-0123456789";

    [Theory]
    [InlineData("Bearer the-real-token-0123456788")]   // same length, last character differs
    [InlineData("Bearer the-real-token")]              // a prefix
    [InlineData("Bearer THE-REAL-TOKEN-0123456789")]   // case differs
    [InlineData("the-real-token-0123456789")]          // no scheme
    [InlineData("Bearer the-real-token-01234567890")]  // one byte longer
    public async Task Anything_but_the_exact_token_is_refused(string authorization)
    {
        var status = await StatusFor(authorization);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task No_authorization_header_is_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusFor(null));
    }

    [Fact]
    public async Task The_exact_token_passes_the_gate()
    {
        // past the gate the path is checked next, so a path the host does not serve answers 404, not 401
        Assert.Equal(HttpStatusCode.NotFound, await StatusFor($"Bearer {Token}"));
    }

    private static async Task<HttpStatusCode> StatusFor(string? authorization)
    {
        ITool echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
        await using var host = await McpToolHost.StartAsync([echo], Token);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, host.Url.Replace("/mcp", "/elsewhere"));
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }
}
