using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The HTTP door: Lyntai's own tool loop over one pinned chat-completions endpoint, as an agent. The
/// endpoint is a scripted handler, so no key or model is needed. The captures are SOURCE-DERIVED: the OpenAI-shaped
/// bodies and streams <c>HttpModelProviderTests</c> already pins, and the headers and envelopes the HTTP failure
/// reader's own tests read.</summary>
public class HttpAgentSessionTests
{
    private const string Url = "https://api.example.test";

    private static string Sse(params string[] deltas) =>
        string.Concat(deltas.Select(d => $"data: {d}\n\n")) + "data: [DONE]\n\n";

    private static string Text(string text) =>
        Sse(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = text } }) }.ToJsonString(),
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""");

    // a turn with no tool in its roster is one buffered completion, not a stream
    private static string Completion(string text) =>
        new JsonObject { ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text }, ["finish_reason"] = "stop",
        }) }.ToJsonString();

    private static string Call(string id, string name, string args) =>
        Sse(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["delta"] = new JsonObject
                    {
                        ["tool_calls"] = new JsonArray(new JsonObject
                        {
                            ["index"] = 0, ["id"] = id,
                            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args },
                        }),
                    },
                }),
            }.ToJsonString(),
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");

    private static HttpResponseMessage Failure(HttpStatusCode status, string body, string? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    private static ITool Tool(string name, Func<string, string>? run = null) =>
        new FunctionTool(name, (args, _) => Task.FromResult(run?.Invoke(args) ?? "ok"), $"the {name} tool", """{"type":"object"}""");

    private static HttpAgentSession Session(
        HttpMessageHandler handler, IEnumerable<ITool>? tools = null, TimeProvider? time = null,
        TimeSpan? timeout = null, IEnumerable<IToolApprover>? approvers = null) =>
        new("http-agent",
            new HttpModelOptions { BaseUrl = Url, ApiKey = "test-key", Model = "default-model" },
            () => new HttpClient(handler, disposeHandler: false), disposeHttpClient: false,
            new ToolRegistry(tools ?? []),
            new LyntaiOptions { ProviderTimeout = timeout ?? TimeSpan.FromSeconds(30) },
            [.. approvers ?? []], time: time);

    private static AgentSessionOptions Turn(string prompt = "go") =>
        new() { Prompt = prompt, ToolPolicy = AgentToolPolicy.Write };

    private static async Task<List<AgentStreamEvent>> Run(IAgentSession session, AgentSessionOptions? turn = null) =>
        await session.StreamAsync(turn ?? Turn()).ToListAsync();

    private static SessionEnded Ended(IEnumerable<AgentStreamEvent> events) =>
        Assert.IsType<SessionEnded>(Assert.Single(events, e => e is SessionEnded));

    private static JsonNode Body(StubHttpHandler handler, int request) => JsonNode.Parse(handler.Requests[request].Body)!;

    // ── the descriptor ───────────────────────────────────────────────────────

    [Fact]
    public void The_door_states_every_ability_and_says_the_turns_content_goes_to_the_endpoint()
    {
        var door = Assert.Single(HttpAgentDoor.Describe("http-agent").Doors);

        Assert.Equal(AgentDoorKinds.Http, door.Kind);
        Assert.Equal(AgentAbilities.All.Order(StringComparer.Ordinal), door.Abilities.Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.Equal(AgentSupport.Unsupported, door.SupportFor(AgentAbilities.Resume));
        Assert.Equal(AgentSupport.Supported, door.SupportFor(AgentAbilities.TypedFailures));
        Assert.Equal(AgentSupport.Unknown, door.SupportFor(AgentAbilities.Model));
        Assert.All(door.Abilities, a => Assert.Equal(AgentAbilitySource.Declared, a.Source));
        Assert.Contains(door.Abilities, a => a.Evidence!.Contains("sends its prompt", StringComparison.Ordinal)
                                             && a.Evidence.Contains("endpoint", StringComparison.Ordinal));
    }

    // ── a turn ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_turn_sends_the_system_prompt_then_the_prompt_to_the_model_it_names_and_ends_once()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Completion("hello"));

        var events = await Run(Session(handler), Turn("say hi") with { SystemPrompt = "be brief", Model = "turn-model" });

        Assert.Contains(events, e => e is TextDelta { Text: "hello" });
        Assert.DoesNotContain(events, e => e is SessionStarted);
        var ended = Ended(events);
        Assert.Equal((ProviderVerdict.Ok, false), (ended.Verdict, ended.IsError));
        Assert.Null(ended.Failure);
        Assert.IsType<SessionEnded>(events[^1]);

        var body = Body(handler, 0);
        Assert.Equal("turn-model", body["model"]!.GetValue<string>());
        Assert.Equal(["system", "user"], body["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal("say hi", body["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_turn_naming_no_model_runs_on_the_registrations_own()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Completion("hi"));

        await Run(Session(handler));

        Assert.Equal("default-model", Body(handler, 0)["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_tool_round_trip_is_streamed_as_events_and_every_request_goes_to_the_one_endpoint()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, Call("call_1", "lookup", """{"q":"x"}"""), "text/event-stream")
            .Enqueue(HttpStatusCode.OK, Text("done"), "text/event-stream");

        var events = await Run(Session(handler, [Tool("lookup", args => $"found {args}")]));

        var call = Assert.Single(events.OfType<ToolCall>());
        Assert.Equal(("lookup", "call_1"), (call.Name, call.CallId));
        var result = Assert.Single(events.OfType<ToolResult>());
        Assert.Equal(("call_1", false), (result.CallId, result.IsError));
        Assert.Contains("found", result.Content);
        Assert.True(Ended(events).Verdict == ProviderVerdict.Ok);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(new Uri(Url + "/v1/chat/completions"), r.Uri));
    }

    [Fact]
    public async Task A_read_only_turn_offers_no_tool_and_a_write_turn_offers_the_registered_ones_less_what_it_denies()
    {
        var tools = new[] { Tool("lookup"), Tool("Delete") };
        var readOnly = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Completion("a"));
        var write = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Text("b"), "text/event-stream");
        var denied = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Text("c"), "text/event-stream");

        await Run(Session(readOnly, tools), Turn() with { ToolPolicy = AgentToolPolicy.ReadOnly });
        await Run(Session(write, tools));
        await Run(Session(denied, tools), Turn() with { DisallowedTools = ["delete"] });

        Assert.Null(Body(readOnly, 0)["tools"]);
        Assert.Equal(["lookup", "Delete"], Names(Body(write, 0)));
        Assert.Equal(["lookup"], Names(Body(denied, 0)));

        static string[] Names(JsonNode body) =>
            [.. body["tools"]!.AsArray().Select(t => t!["function"]!["name"]!.GetValue<string>())];
    }

    // ── refused before any call ──────────────────────────────────────────────

    [Fact]
    public async Task An_option_the_door_cannot_honour_is_refused_before_any_call()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Completion("never"));
        var session = Session(handler);
        var account = new AgentAccount { Provider = "http-agent", Name = "work", Home = "/home" };

        foreach (var (turn, ability) in new[]
                 {
                     (Turn() with { ResumeToken = "conv-1" }, AgentAbilities.Resume),
                     (Turn() with { McpServers = [new AgentMcpServer { Name = "app", Command = "app-mcp" }] }, AgentAbilities.McpServers),
                     (Turn() with { Account = account }, AgentAbilities.Accounts),
                 })
        {
            var ended = Ended(await Run(session, turn));
            Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
            Assert.Equal(AgentProvider.OptionRefusedSubtype, ended.Subtype);
            Assert.Contains(ability, ended.Diagnostic);
            Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        }

        Assert.Empty(handler.Requests);
    }

    // ── a failed turn, read from the response's own facts ────────────────────

    [Fact]
    public async Task A_429_with_Retry_After_is_a_limit_that_says_when_and_is_not_asked_again()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpHandler().Enqueue(_ => Failure(HttpStatusCode.TooManyRequests,
            """{"error":{"code":"rate_limit_exceeded","message":"Too many requests"}}""", retryAfter: "120"));

        var ended = Ended(await Run(Session(handler, time: clock)));

        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        Assert.True(ended.IsError);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RestrictionCause.Rate, failure.Restriction!.Cause);
        Assert.Equal(RestrictionRecovery.RetryNotBefore(clock.GetUtcNow().AddSeconds(120)) with { Stated = new StatedRecoveryParts(StatedTimeShape.Wait) { Wait = TimeSpan.FromSeconds(120) } },
            failure.Restriction.Recovery);
        Assert.Equal(AgentDelivery.Taken, failure.Delivery);
        Assert.Single(handler.Requests);   // one pinned route, no fallback and no retry of its own
    }

    [Fact]
    public async Task A_429_without_Retry_After_states_no_recovery_and_invents_none()
    {
        var handler = new StubHttpHandler().Enqueue(_ => Failure(HttpStatusCode.TooManyRequests,
            """{"error":{"code":"rate_limit_exceeded","message":"Too many requests"}}"""));

        var failure = Ended(await Run(Session(handler))).Failure!;

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RecoveryKind.Unknown, failure.Restriction!.Recovery.Kind);
    }

    [Fact]
    public async Task A_402_is_a_credit_limit_a_person_must_refill()
    {
        var handler = new StubHttpHandler().Enqueue(_ => Failure(HttpStatusCode.PaymentRequired,
            """{"error":{"message":"Insufficient credits"}}"""));

        var ended = Ended(await Run(Session(handler)));

        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        Assert.Equal(RestrictionCause.Credit, ended.Failure!.Restriction!.Cause);
        Assert.Equal(RecoveryKind.OwnerAction, ended.Failure.Restriction.Recovery.Kind);
    }

    [Fact]
    public async Task A_rejected_key_is_an_access_failure()
    {
        var handler = new StubHttpHandler().Enqueue(_ => Failure(HttpStatusCode.Unauthorized,
            """{"error":{"code":"invalid_api_key","message":"Incorrect API key"}}"""));

        var ended = Ended(await Run(Session(handler)));

        Assert.Equal((ProviderVerdict.AuthFailed, AgentFailureKind.Access), (ended.Verdict, ended.Failure!.Kind));
    }

    [Fact]
    public async Task An_overloaded_endpoint_is_transient_and_the_door_never_sends_the_turn_to_another()
    {
        var handler = new StubHttpHandler().Enqueue(_ => Failure((HttpStatusCode)503, """{"error":{"message":"upstream down"}}"""));

        var ended = Ended(await Run(Session(handler)));

        Assert.Equal((ProviderVerdict.Failed, AgentFailureKind.Transient), (ended.Verdict, ended.Failure!.Kind));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_error_a_gateway_reports_in_band_under_a_200_is_read_as_the_failure_it_says()
    {
        const string envelope = """{"error":{"code":"rate_limit_exceeded","message":"Rate limit exceeded"}}""";
        var buffered = new StubHttpHandler().Enqueue(_ => Failure(HttpStatusCode.OK, envelope, retryAfter: "30"));
        var streamed = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Sse(envelope), "text/event-stream");
        var clock = new ManualTimeProvider();

        var whole = Ended(await Run(Session(buffered, time: clock)));
        var lines = Ended(await Run(Session(streamed, [Tool("lookup")])));

        Assert.Equal((ProviderVerdict.RateLimited, AgentFailureKind.Limit), (whole.Verdict, whole.Failure!.Kind));
        Assert.Equal((RecoveryKind.RetryNotBefore, clock.GetUtcNow().AddSeconds(30)),
            (whole.Failure.Restriction!.Recovery.Kind, whole.Failure.Restriction.Recovery.At!.Value));
        Assert.Equal((ProviderVerdict.RateLimited, AgentFailureKind.Limit), (lines.Verdict, lines.Failure!.Kind));
    }

    [Fact]
    public async Task A_limit_a_reply_merely_quotes_is_an_answer_and_never_classifies()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            Completion("The server said: 429 rate limit exceeded, quota exceeded."));

        var ended = Ended(await Run(Session(handler)));

        Assert.False(ended.IsError);
        Assert.Null(ended.Failure);
    }

    // ── delivery ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_connection_that_never_opened_is_never_sent_and_a_wait_that_ran_out_is_uncertain()
    {
        var never = Ended(await Run(Session(new ConnectionRefusedHandler())));
        var silent = Ended(await Run(Session(new StallingHandler(), timeout: TimeSpan.FromMilliseconds(150))));

        Assert.Equal((ProviderVerdict.Failed, AgentFailureKind.Transient, AgentDelivery.NeverSent),
            (never.Verdict, never.Failure!.Kind, never.Failure.Delivery));
        Assert.Equal((ProviderVerdict.Timeout, AgentFailureKind.Transient, AgentDelivery.Uncertain),
            (silent.Verdict, silent.Failure!.Kind, silent.Failure.Delivery));
    }

    [Fact]
    public async Task A_failure_after_a_tool_ran_is_uncertain_because_the_tool_may_have_acted()
    {
        var ran = 0;
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, Call("call_1", "lookup", "{}"), "text/event-stream")
            .Enqueue(_ => Failure((HttpStatusCode)502, "bad gateway"));

        var events = await Run(Session(handler, [Tool("lookup", _ => { ran++; return "ok"; })]));

        var ended = Ended(events);
        Assert.Equal(1, ran);
        Assert.Equal((AgentFailureKind.Transient, AgentDelivery.Uncertain), (ended.Failure!.Kind, ended.Failure.Delivery));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_loop_that_never_converges_ends_failed_with_no_field_to_type_it_by()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, Call("c", "lookup", "{}"), "text/event-stream");
        var session = Session(handler, [Tool("lookup")]);

        var ended = Ended(await Run(session));

        Assert.True(ended.IsError);
        Assert.Equal("no-failure-fields", ended.Failure!.By);
        Assert.Equal(AgentDelivery.Uncertain, ended.Failure.Delivery);
    }

    // ── cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_cancelled_call_ends_the_turn_as_a_stop_with_no_terminal_and_no_failure()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StallingHandler();
        var seen = new List<AgentStreamEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(150));
            await foreach (var e in Session(handler).StreamAsync(Turn(), cts.Token)) seen.Add(e);
        });

        Assert.Empty(seen);
        Assert.Single(handler.Seen);
    }

    // ── composition ──────────────────────────────────────────────────────────

    [Fact]
    public void Registering_the_door_adds_a_provider_by_id_a_keyed_session_and_no_unkeyed_one()
    {
        var handler = new StubHttpHandler();
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddHttpAgentSession("http-agent", o => { o.BaseUrl = Url; o.ApiKey = "k"; },
            _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<AgentProviderRegistry>().Get("http-agent");

        Assert.Equal(AgentDoorKinds.Http, Assert.Single(provider.Describe().Doors).Kind);
        Assert.NotNull(sp.GetRequiredKeyedService<IAgentSession>("http-agent"));
        Assert.Null(sp.GetService<IAgentSession>());
    }

    [Fact]
    public async Task A_turn_through_the_provider_runs_over_the_registered_tools_and_endpoint()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, Call("call_1", "lookup", "{}"), "text/event-stream")
            .Enqueue(HttpStatusCode.OK, Text("done"), "text/event-stream");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddTool(_ => Tool("lookup"))
            .AddHttpAgentSession("http-agent", o => { o.BaseUrl = Url; o.ApiKey = "k"; },
                _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        var events = await Run(sp.GetRequiredService<AgentProviderRegistry>().Get("http-agent").Open("http"));

        Assert.Single(events.OfType<ToolResult>());
        Assert.False(Ended(events).IsError);
        Assert.Equal("Bearer k", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task The_retry_budget_is_the_retry_layers_and_a_refused_connection_is_the_one_failure_it_may_resend()
    {
        var handler = new ConnectionRefusedHandler();
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddAgentRetry(o => { o.MaxRetries = 1; o.InitialDelay = TimeSpan.Zero; o.MaxDelay = TimeSpan.Zero; })
            .AddHttpAgentSession("http-agent", o => { o.BaseUrl = Url; o.ApiKey = "k"; },
                _ => new HttpClient(handler, disposeHandler: false)));
        using var sp = services.BuildServiceProvider();

        var events = await Run(sp.GetRequiredService<AgentProviderRegistry>().Get("http-agent").Open("http"));

        Assert.Equal(2, handler.Calls);   // the first attempt and the one retry, nothing hidden beneath
        Assert.Equal(AgentDelivery.NeverSent, Ended(events).Failure!.Delivery);
    }

    [Fact]
    public void A_registration_that_does_not_produce_text_is_refused()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddLyntai(b =>
            b.AddHttpAgentSession("http-agent", o => o.Produces = ProviderKinds.Vector)));
    }

    private sealed class ConnectionRefusedHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused");
        }
    }
}
