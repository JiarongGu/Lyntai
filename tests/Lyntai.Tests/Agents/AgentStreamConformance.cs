using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>One turn the stream rules are held to, said once for every door and rendered onto each door's wire by its
/// <see cref="DoorScript"/>.</summary>
public enum StreamCase
{
    /// <summary>An answer, a tool step, an answer, the ending.</summary>
    Clean,

    /// <summary>The agent's ending, and a failing one printed after it.</summary>
    SecondEnding,

    /// <summary>The agent says its own turn failed, after answering and before any tool.</summary>
    FailedTerminal,

    /// <summary>The process dies before it has shown anything.</summary>
    DiesBeforeAnyEvent,

    /// <summary>The process dies after the door answered.</summary>
    DiesAfterAnswer,

    /// <summary>The process dies after a tool step began.</summary>
    DiesAfterToolCall,

    /// <summary>A tool's output is a limit sentence, and the turn ends well.</summary>
    ToolPrintsLimit,

    /// <summary>A tool's output is a limit sentence, and the process then dies for some other reason.</summary>
    ToolPrintsLimitThenDies,

    /// <summary>The caller cancels at the first event of a turn that would have ended well.</summary>
    Cancelled,
}

/// <summary>What one case runs over: the process stand-in (CLI, protocol and host doors) and the endpoint handler
/// (the HTTP door). Each case reads one and ignores the other.</summary>
internal sealed record TurnScript(FakeProcessRunner Runner, HttpMessageHandler Http);

/// <summary>A door's wire, as the one thing the stream rules need from it: how to say each <see cref="StreamCase"/> in
/// the words the door reads. A host's own provider is held to the rules by adding one of these and nothing else.</summary>
internal abstract class DoorScript
{
    public abstract TurnScript Build(StreamCase turn);
}

/// <summary>A door that reads lines from a process, the wire of each case as the pieces a turn is built from.</summary>
internal sealed class LineWire(
    string Command,
    string[] Answered,
    string[] Tool,
    Func<string, string[]> Output,
    string[] Ok,
    string[] Failed,
    string[] LaterEnding,
    string[] More) : DoorScript
{
    public override TurnScript Build(StreamCase turn)
    {
        var boom = new ProcessRunException(Command, 1, "boom");
        var output = Output(StreamRules.LimitSentence);
        var (lines, dies) = turn switch
        {
            StreamCase.Clean => Said([.. Answered, .. Tool, .. Output("fine"), .. Ok]),
            StreamCase.SecondEnding => Said([.. Answered, .. Ok, .. LaterEnding]),
            StreamCase.FailedTerminal => Said([.. Answered, .. Failed]),
            StreamCase.DiesBeforeAnyEvent => Dies([]),
            StreamCase.DiesAfterAnswer => Dies(Answered),
            StreamCase.DiesAfterToolCall => Dies([.. Answered, .. Tool]),
            StreamCase.ToolPrintsLimit => Said([.. Answered, .. Tool, .. output, .. Ok]),
            StreamCase.ToolPrintsLimitThenDies => Dies([.. Answered, .. Tool, .. output]),
            StreamCase.Cancelled => Said([.. Answered, .. More, .. Ok]),
            _ => throw new ArgumentOutOfRangeException(nameof(turn), turn, null),
        };
        return new TurnScript(new FakeProcessRunner(lines, dies ? boom : null), new StubHttpHandler());

        static (string[], bool) Said(string[] lines) => (lines, false);
        static (string[], bool) Dies(string[] lines) => (lines, true);
    }
}

/// <summary>The door over a chat-completions endpoint: the same cases as scripted responses, one per request.</summary>
internal sealed class HttpWire : DoorScript
{
    private static string Sse(params string[] deltas) =>
        string.Concat(deltas.Select(d => $"data: {d}\n\n")) + "data: [DONE]\n\n";

    private static string Text(string text) =>
        Sse(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = text } }) }.ToJsonString(),
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""");

    private static string Call() =>
        Sse(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["delta"] = new JsonObject
                    {
                        ["tool_calls"] = new JsonArray(new JsonObject
                        {
                            ["index"] = 0, ["id"] = "call_1",
                            ["function"] = new JsonObject { ["name"] = StreamRules.ToolName, ["arguments"] = "{}" },
                        }),
                    },
                }),
            }.ToJsonString(),
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");

    private static HttpResponseMessage Failure(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Envelope = """{"error":{"code":"rate_limit_exceeded","message":"late"}}""";

    public override TurnScript Build(StreamCase turn)
    {
        var handler = new StubHttpHandler();
        switch (turn)
        {
            case StreamCase.Clean:
            case StreamCase.ToolPrintsLimit:
            case StreamCase.Cancelled:
                handler.Enqueue(HttpStatusCode.OK, Call(), "text/event-stream").Enqueue(HttpStatusCode.OK, Text("done"), "text/event-stream");
                break;
            case StreamCase.SecondEnding:
                handler.Enqueue(HttpStatusCode.OK, Call(), "text/event-stream")
                    .Enqueue(HttpStatusCode.OK, Text("done") + $"data: {Envelope}\n\n", "text/event-stream");
                break;
            case StreamCase.FailedTerminal:
                handler.Enqueue(_ => Failure((HttpStatusCode)503, """{"error":{"message":"upstream down"}}"""));
                break;
            case StreamCase.DiesBeforeAnyEvent:
                return new TurnScript(new FakeProcessRunner(), new RefusedHandler());
            case StreamCase.DiesAfterAnswer:
                handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new BreaksAfter(Encoding.UTF8.GetBytes(
                        """data: {"choices":[{"delta":{"content":"half"}}]}""" + "\n\n"))),
                });
                break;
            case StreamCase.DiesAfterToolCall:
            case StreamCase.ToolPrintsLimitThenDies:
                handler.Enqueue(HttpStatusCode.OK, Call(), "text/event-stream").Enqueue(_ => Failure((HttpStatusCode)502, "bad gateway"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(turn), turn, null);
        }
        return new TurnScript(new FakeProcessRunner(), handler);
    }

    private sealed class RefusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused");
    }

    /// <summary>A body that delivers its bytes and then fails, as a connection dropped mid-answer does.</summary>
    private sealed class BreaksAfter(byte[] bytes) : Stream
    {
        private int _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= bytes.Length) throw new IOException("connection reset");
            var n = Math.Min(count, bytes.Length - _read);
            Array.Copy(bytes, _read, buffer, offset, n);
            _read += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Every wire a door of this repository speaks, and the one place a script is found for a registered
/// provider.</summary>
internal static class DoorScripts
{
    private static string Json(string text) => JsonValue.Create(text)!.ToJsonString();

    private static readonly LineWire Claude = new("claude",
        Answered:
        [
            """{"type":"system","session_id":"s-1","model":"m"}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"half"}}}""",
        ],
        Tool: ["""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{}}]}}"""],
        Output: text => ["""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":TEXT,"is_error":false}]}}""".Replace("TEXT", Json(text))],
        Ok: ["""{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s-1"}"""],
        Failed: ["""{"type":"result","subtype":"success","is_error":true,"api_error_status":529,"result":"API Error: 529 overloaded_error"}"""],
        LaterEnding: ["""{"type":"result","result":"","session_id":"s-1","is_error":true,"subtype":"error_during_execution"}"""],
        More:
        [
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"more"}}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"and more"}}}""",
        ]);

    private static readonly LineWire Codex = new("codex",
        Answered:
        [
            """{"type":"thread.started","thread_id":"s-1"}""",
            """{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"half"}}""",
        ],
        Tool: ["""{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"x","status":"in_progress"}}"""],
        Output: text => ["""{"type":"item.completed","item":{"id":"c1","type":"command_execution","command":"x","aggregated_output":TEXT,"exit_code":0,"status":"completed"}}""".Replace("TEXT", Json(text))],
        Ok: ["""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}"""],
        Failed: ["""{"type":"turn.failed","error":{"message":"Selected model is at capacity. Please try a different model."}}"""],
        LaterEnding: ["""{"type":"turn.failed","error":{"message":"late"}}"""],
        More:
        [
            """{"type":"item.completed","item":{"id":"b","type":"agent_message","text":"more"}}""",
            """{"type":"item.completed","item":{"id":"c","type":"agent_message","text":"and more"}}""",
        ]);

    private static string Update(string kind, string body) =>
        """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s1","update":{"sessionUpdate":"KIND",BODY}}}""".Replace("KIND", kind).Replace("BODY", body[1..^1]);

    private static string Said(string text) =>
        Update("agent_message_chunk", """{"content":{"type":"text","text":TEXT}}""".Replace("TEXT", Json(text)));

    private const string PromptFailed =
        """{"jsonrpc":"2.0","id":3,"error":{"code":-32603,"message":"Internal error: turn failure","data":{"errorKind":"server_error"}}}""";

    private static readonly LineWire Acp = new("acp-agent-cmd",
        Answered:
        [
            """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"agentCapabilities":{"loadSession":true,"sessionCapabilities":{"resume":{}}},"authMethods":[]}}""",
            """{"jsonrpc":"2.0","id":2,"result":{"sessionId":"s1"}}""",
            Said("half"),
        ],
        Tool: [Update("tool_call", """{"toolCallId":"c1","title":"Run","kind":"execute","status":"in_progress"}""")],
        Output: text => [Update("tool_call_update", """{"toolCallId":"c1","status":"completed","content":[{"type":"content","content":{"type":"text","text":TEXT}}]}""".Replace("TEXT", Json(text)))],
        Ok: ["""{"jsonrpc":"2.0","id":3,"result":{"stopReason":"end_turn"}}"""],
        Failed: [PromptFailed],
        LaterEnding: [PromptFailed],
        More: [Said("more"), Said("and more")]);

    /// <summary>The wire of <see cref="DialectStandIn"/>: a word and the rest of the line.</summary>
    internal static readonly LineWire StandIn = new("stand-in",
        Answered: ["start s-1", "say half"],
        Tool: ["tool lookup"],
        Output: text => [$"output {text}"],
        Ok: ["end done"],
        Failed: ["fail overloaded"],
        LaterEnding: ["fail late"],
        More: ["say more", "say and more"]);

    private static readonly Dictionary<string, DoorScript> ById = new(StringComparer.Ordinal)
    {
        ["claude-cli"] = Claude,
        ["codex-cli"] = Codex,
        ["acp-agent"] = Acp,
        ["http-agent"] = new HttpWire(),
        [DialectStandIn.Id] = StandIn,
    };

    /// <summary>The script of a registered provider; a provider with none is a failure, since a door the theory cannot
    /// drive is a door it does not hold.</summary>
    public static DoorScript For(string providerId) =>
        ById.TryGetValue(providerId, out var script)
            ? script
            : throw new Xunit.Sdk.XunitException(
                $"No stream script for provider '{providerId}': add its wire to DoorScripts, or the rules hold nothing of it.");
}

/// <summary>The rules every provider's stream keeps, each as what a turn of one <see cref="StreamCase"/> must show.
/// The expectations are read from the events and the door's own descriptor, never from the door's id.</summary>
internal static class StreamRules
{
    public const string LimitSentence = "You've hit your usage limit. Try again at 4:05 PM.";
    public const string ToolName = "lookup";

    /// <summary>The tool the HTTP door runs: its output is the limit sentence a tool may print.</summary>
    public static ITool LimitTool { get; } = new FunctionTool(
        ToolName, (_, _) => Task.FromResult(LimitSentence), "a tool that prints a limit sentence", """{"type":"object"}""");

    /// <summary>Run one case against one door and assert every rule it holds.</summary>
    /// <param name="compose">A registry over the case's stand-ins.</param>
    public static async Task Hold(
        Func<TurnScript, ServiceProvider> compose, string providerId, string doorId, DoorScript script, StreamCase turn)
    {
        var who = $"{providerId}/{doorId}/{turn}";
        var wire = script.Build(turn);
        using var sp = compose(wire);
        var provider = sp.GetRequiredService<AgentProviderRegistry>().Get(providerId);
        var typed = provider.Describe().Doors.Single(d => d.Id == doorId).SupportFor(AgentAbilities.TypedFailures) == AgentSupport.Supported;
        var options = new AgentSessionOptions { Prompt = "go", ToolPolicy = AgentToolPolicy.Write };
        var session = provider.Open(doorId);

        if (turn == StreamCase.Cancelled)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var seen = new List<AgentStreamEvent>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var e in session.StreamAsync(options, cts.Token))
                {
                    seen.Add(e);
                    cts.Cancel();
                }
            });
            Assert.True(!seen.OfType<SessionEnded>().Any(), $"{who}: a cancelled turn was ended by a terminal");
            return;
        }

        var events = await session.StreamAsync(options).ToListAsync();

        // exactly one terminal, and the first ending the agent printed is the one that stands
        var endings = events.OfType<SessionEnded>().ToList();
        Assert.True(endings.Count == 1, $"{who}: {endings.Count} terminals, not one");
        var ended = endings[0];

        var failing = turn is StreamCase.FailedTerminal or StreamCase.DiesBeforeAnyEvent or StreamCase.DiesAfterAnswer
            or StreamCase.DiesAfterToolCall or StreamCase.ToolPrintsLimitThenDies;
        Assert.True(ended.IsError == failing, $"{who}: the turn {(failing ? "failed" : "ended well")} but its terminal says IsError={ended.IsError}");
        if (!failing)
        {
            Assert.True(ended.Verdict == ProviderVerdict.Ok && ended.Failure is null,
                $"{who}: a turn that ended well carries verdict {ended.Verdict} and failure {ended.Failure}");
            return;
        }

        // a failed terminal carries a Failure where the door says it states them, and what the turn had shown says
        // whether the prompt was delivered
        if (!typed) return;
        var failure = ended.Failure;
        Assert.True(failure is not null, $"{who}: a failed terminal carries no Failure although the door states typed-failures");
        var expected = turn switch
        {
            StreamCase.DiesBeforeAnyEvent => AgentDelivery.NeverSent,
            StreamCase.DiesAfterToolCall or StreamCase.ToolPrintsLimitThenDies => AgentDelivery.Uncertain,
            _ => AgentDelivery.Taken,
        };
        Assert.True(failure!.Delivery == expected, $"{who}: delivery {failure.Delivery}, expected {expected}");

        // the sentence a tool printed is never read as the turn's failure
        if (turn == StreamCase.ToolPrintsLimitThenDies)
            Assert.True(failure.Kind != AgentFailureKind.Limit && ended.Verdict != ProviderVerdict.RateLimited && failure.Restriction is null,
                $"{who}: a limit sentence a tool printed classified the turn as {failure.Kind}/{ended.Verdict}");
    }
}
