using System.Net;
using System.Text;

namespace Lyntai.Tests.Fakes;

/// <summary>Scripted HttpMessageHandler: each request pops the next response from the queue
/// (the last script repeats when the queue empties). Records every request body.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _scripts = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _last;

    public List<(Uri? Uri, string Body, string? Auth)> Requests { get; } = [];

    public StubHttpHandler Enqueue(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        _scripts.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });
        return this;
    }

    public StubHttpHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> script)
    {
        _scripts.Enqueue(script);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.RequestUri, body, request.Headers.Authorization?.ToString()));

        if (_scripts.Count > 0) _last = _scripts.Dequeue();
        if (_last is null) throw new InvalidOperationException("StubHttpHandler: nothing scripted");
        return _last(request);
    }
}
