using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lyntai.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// An ephemeral, localhost-only HTTP MCP server exposing the given <see cref="ITool"/>s as MCP tools. Started
/// on an OS-assigned port and stopped on dispose — it lives only for the duration of one CLI invocation, so
/// nothing is exposed beyond that call. Provider-neutral: which CLI connects, and how it is told to, is the
/// <see cref="IMcpCliDialect"/>'s business.
/// </summary>
/// <remarks>
/// <para>Hosted on <see cref="HttpListener"/> (BCL) rather than ASP.NET Core, deliberately. The MCP protocol
/// itself lives in <c>ModelContextProtocol.Core</c> — <see cref="StreamableHttpServerTransport"/> works on
/// plain <see cref="Stream"/>s — so the ASP.NET package only ever supplied Kestrel routing glue. Dropping it
/// removes a framework reference on <c>Microsoft.AspNetCore.App</c>, which a console or desktop consumer has no
/// reason to acquire just to let a CLI call its tools (<c>docs/DECISIONS.md</c> D31).</para>
/// <para><see cref="HttpListener"/> is the right tool for exactly this shape: a loopback-only, short-lived
/// endpoint. It needs no URL ACL (and no elevation) for <c>127.0.0.1</c>, and is implemented in managed code on
/// non-Windows platforms. It would be the wrong choice for an internet-facing server — which this never is.</para>
/// </remarks>
internal sealed class McpToolHost : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly StreamableHttpServerTransport _transport;
    private readonly McpServer _server;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _accepting;
    private readonly Task _serving;

    private McpToolHost(
        HttpListener listener, StreamableHttpServerTransport transport, McpServer server, string url, string authToken)
    {
        _listener = listener;
        _transport = transport;
        _server = server;
        Url = url;

        _serving = server.RunAsync(_cts.Token);
        _accepting = AcceptLoopAsync(authToken, _cts.Token);
    }

    /// <summary>The endpoint the CLI's MCP client connects to, e.g. <c>http://127.0.0.1:PORT/mcp</c>.</summary>
    public string Url { get; }

    /// <summary>The single path served. Everything else on the port is a 404 — the host owns an entire
    /// ephemeral port, so there is nothing else to route.</summary>
    private const string Path = "/mcp";

    /// <summary>Start the host. <paramref name="authToken"/> is required as a bearer token on every
    /// request — the endpoint EXECUTES the app's tools, so even on loopback another local process must
    /// not be able to invoke them.</summary>
    public static async Task<McpToolHost> StartAsync(
        IReadOnlyList<ITool> tools, string authToken, McpToolHostOptions? options = null, CancellationToken ct = default)
    {
        options ??= new McpToolHostOptions();
        ct.ThrowIfCancellationRequested();

        var (host, port) = ResolveBinding(options.BindAddress);
        var listener = new HttpListener();
        // the whole port is ours, so listen at the root and route in code: an HttpListener prefix of
        // "/mcp/" would NOT match a request for "/mcp", which is the URL the CLI is handed
        listener.Prefixes.Add($"http://{host}:{port}/");

        StreamableHttpServerTransport? transport = null;
        McpServer? server = null;
        try
        {
            listener.Start();

            // A session id is part of the Streamable HTTP contract: the server returns it on the initialize
            // response and the client echoes it on every later request. Omitting it leaves the client waiting
            // through initialization (measured: "Initialization timed out"), so it is set explicitly rather
            // than left to chance.
            transport = new StreamableHttpServerTransport(NullLoggerFactory.Instance)
            {
                SessionId = Guid.NewGuid().ToString("N"),
            };
            var serverOptions = new McpServerOptions
            {
                ServerInfo = new Implementation { Name = options.ServerName, Version = "1.0.0" },
                ToolCollection = [],
            };
            foreach (var tool in tools)
                serverOptions.ToolCollection.Add(McpServerTool.Create(new ToolFunction(tool)));

            server = McpServer.Create(transport, serverOptions, NullLoggerFactory.Instance, serviceProvider: null);

            var self = new McpToolHost(listener, transport, server, $"http://{host}:{port}{Path}", authToken);

            // A message pump that dies immediately would otherwise present as a client that hangs through
            // initialization — the worst possible symptom to debug. Surface it as a start-time failure instead.
            await Task.Yield();
            if (self._serving.IsFaulted)
            {
                await self.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("the MCP server stopped immediately", self._serving.Exception);
            }
            return self;
        }
        catch
        {
            // never leak a started listener / half-built server
            server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            transport?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (listener.IsListening) listener.Stop();
            listener.Close();
            throw;
        }
    }

    /// <summary>Turn the configured bind address into (host, port). <see cref="HttpListener"/> cannot bind
    /// port 0, so an OS-assigned port is obtained by briefly opening a socket on 0 and reusing the number it
    /// hands back. There is a theoretical race with another process claiming that port in between; on loopback,
    /// for a process about to bind it immediately, it is not a practical concern — and a collision surfaces
    /// loudly as a failed <see cref="HttpListener.Start"/> rather than silently.</summary>
    private static (string Host, int Port) ResolveBinding(string bindAddress)
    {
        var uri = new Uri(bindAddress);
        var host = uri.Host;
        if (uri.Port != 0) return (host, uri.Port);

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return (host, port);
    }

    private async Task AcceptLoopAsync(string authToken, CancellationToken ct)
    {
        var expected = $"Bearer {authToken}";
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested || !_listener.IsListening)
            {
                return;   // disposed mid-wait: an expected shutdown, not a fault
            }

            // Each request runs on its own task, and that is not an optimisation — it is required. A
            // Streamable HTTP client opens a LONG-LIVED GET SSE stream and issues POSTs while it is open, so
            // handling requests sequentially deadlocks: the GET never returns, and the POST that carries
            // `initialize` is never accepted. (Measured: the client reported "Initialization timed out" while a
            // raw single POST to the same host answered correctly.)
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(context, expected, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    TryFail(context);   // one bad request must not take the host down
                }
            }, CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, string expectedAuthorization, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        if (!string.Equals(request.Headers["Authorization"], expectedAuthorization, StringComparison.Ordinal))
        {
            response.StatusCode = (int)HttpStatusCode.Unauthorized;
            response.Close();
            return;
        }

        if (!string.Equals(request.Url?.AbsolutePath.TrimEnd('/'), Path, StringComparison.Ordinal))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        switch (request.HttpMethod)
        {
            case "POST":
                await HandlePostAsync(request, response, ct).ConfigureAwait(false);
                return;

            case "GET":
                // the optional standalone SSE stream for server-initiated messages
                BeginEventStream(response);
                await _transport.HandleGetRequestAsync(response.OutputStream, ct).ConfigureAwait(false);
                response.Close();
                return;

            case "DELETE":
                // session termination; this host dies with the call that spawned it, so acknowledge and move on
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;

            default:
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                response.Close();
                return;
        }
    }

    private async Task HandlePostAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        JsonRpcMessage? message;
        try
        {
            message = (JsonRpcMessage?)await JsonSerializer.DeserializeAsync(
                request.InputStream, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)), ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.Close();
            return;
        }

        // A NOTIFICATION expects no reply, so it must be acknowledged with 202 and an empty body — the status
        // has to be chosen BEFORE the transport can write, which is why the request kind is inspected here
        // rather than inferred from whether anything came back. (The client sends notifications/initialized
        // right after the handshake, so getting this wrong breaks the very first exchange.)
        if (message is JsonRpcNotification)
        {
            await _transport.HandlePostRequestAsync(message, Stream.Null, ct).ConfigureAwait(false);
            response.StatusCode = (int)HttpStatusCode.Accepted;
            response.Close();
            return;
        }

        BeginEventStream(response);
        await _transport.HandlePostRequestAsync(message, response.OutputStream, ct).ConfigureAwait(false);
        response.Close();
    }

    /// <summary>Commit a Streamable-HTTP SSE response: chunked, unbuffered, so each frame the transport writes
    /// reaches the client as it is produced rather than at the end.</summary>
    private void BeginEventStream(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        // the client reads this off the initialize response and echoes it on every later request
        if (_transport.SessionId is { Length: > 0 } sessionId) response.Headers["Mcp-Session-Id"] = sessionId;
        response.SendChunked = true;
    }

    private static void TryFail(HttpListenerContext context)
    {
        try
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
        }
        catch
        {
            // the response may already be committed/closed — nothing useful left to do
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try { if (_listener.IsListening) _listener.Stop(); } catch { /* best-effort */ }
        _listener.Close();

        // both loops end on cancellation or on the listener closing under them
        foreach (var task in (Task[])[_accepting, _serving])
            try { await task.ConfigureAwait(false); } catch { /* shutdown */ }

        await _server.DisposeAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
