using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

/// <summary>The provider-agnostic tool-calling loop, driven by a scripted <see cref="FakeLlmClient"/>:
/// the model's JSON turns are queued, tools are fakes, so every branch (call → observation → final,
/// unknown tool, throwing tool, non-convergence, surfaced verdict, no-tools) is deterministic.</summary>
public class ToolLoopTests
{
    private static LyntaiOptions Options(int max = 8) => new() { ToolLoopMaxIterations = max };

    private static ToolLoop Loop(FakeLlmClient client, params ITool[] tools) =>
        new(client, new ToolRegistry(tools), Options());

    private static LlmRequest Ask(string prompt = "do it") => new() { Messages = [LlmMessage.User(prompt)] };

    private static FunctionTool Echo(string name = "echo") =>
        new(name, (args, _) => Task.FromResult($"observed:{args}"), "echoes its args");

    [Fact]
    public void Registry_finds_by_name_case_insensitively_and_first_wins_on_duplicate()
    {
        var first = Echo("dup");
        var registry = new ToolRegistry([first, Echo("other"), Echo("dup")]);

        Assert.Same(first, registry.Find("DUP"));       // case-insensitive
        Assert.Equal(2, registry.Tools.Count);          // the duplicate name was dropped
        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public async Task Calls_a_tool_then_returns_the_final_answer_recording_the_step()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{"x":1}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"all done"}""", LlmVerdict.Ok));

        var result = await Loop(client, Echo()).RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("all done", result.Answer);
        var step = Assert.Single(result.Steps);
        Assert.Equal("echo", step.Tool);
        Assert.Equal("""{"x":1}""", step.ArgumentsJson);
        Assert.Equal("""observed:{"x":1}""", step.Result);
    }

    [Fact]
    public async Task Feeds_the_observation_back_to_the_model()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"ok"}""", LlmVerdict.Ok));

        await Loop(client, Echo()).RunAsync(Ask());

        // second call must include the tool result as a fed-back message
        var secondCall = client.Calls[1];
        Assert.Contains(secondCall.Messages, m => m.Role == "user" && m.Content.Contains("observed:"));
        Assert.Contains(secondCall.Messages, m => m.Role == "system" && m.Content.Contains("echo")); // tool listed in the protocol prompt
    }

    [Fact]
    public async Task Unknown_tool_is_reported_back_not_thrown()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"nope","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"recovered"}""", LlmVerdict.Ok));

        var result = await Loop(client, Echo()).RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("recovered", result.Answer);
        Assert.Contains("unknown tool", result.Steps[0].Result);
    }

    [Fact]
    public async Task A_throwing_tool_becomes_an_error_observation()
    {
        var boom = new FunctionTool("boom", (_, _) => throw new InvalidOperationException("kaboom"));
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"boom","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"handled"}""", LlmVerdict.Ok));

        var result = await Loop(client, boom).RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("handled", result.Answer);
        Assert.Contains("error: kaboom", result.Steps[0].Result);
    }

    [Fact]
    public async Task Non_Ok_verdict_is_surfaced_without_further_tool_calls()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Refused, Detail: "policy"));

        var result = await Loop(client, Echo()).RunAsync(Ask());

        Assert.Equal(LlmVerdict.Refused, result.Verdict);
        Assert.Empty(result.Steps);
        Assert.Equal("policy", result.Detail);
    }

    [Fact]
    public async Task Does_not_converge_within_budget_returns_Failed()
    {
        var client = new FakeLlmClient();
        // never emits a "final" — always calls the tool again
        client.StreamScript = null;
        for (var i = 0; i < 10; i++)
            client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{}}""", LlmVerdict.Ok));

        var loop = new ToolLoop(client, new ToolRegistry([Echo()]), Options(max: 3));
        var result = await loop.RunAsync(Ask());

        Assert.Equal(LlmVerdict.Failed, result.Verdict);
        Assert.Equal(3, result.Steps.Count);            // exactly the budget
        Assert.Contains("did not converge", result.Detail);
    }

    [Fact]
    public async Task Budget_override_beats_the_configured_default()
    {
        var client = new FakeLlmClient();
        for (var i = 0; i < 10; i++)
            client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{}}""", LlmVerdict.Ok));

        var loop = new ToolLoop(client, new ToolRegistry([Echo()]), Options(max: 8));
        var result = await loop.RunAsync(Ask(), maxIterations: 2);

        Assert.Equal(2, result.Steps.Count);
    }

    [Fact]
    public async Task A_direct_JSON_answer_without_a_protocol_key_is_treated_as_final()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"result":"42"}""", LlmVerdict.Ok));

        var result = await Loop(client, Echo()).RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("""{"result":"42"}""", result.Answer);
        Assert.Empty(result.Steps);
    }

    // ---- TL1: per-run token usage aggregated onto the result ----------------------------------------

    [Fact]
    public async Task Aggregates_token_usage_across_every_call_prompt_path()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{}}""", LlmVerdict.Ok, new LlmUsage(10, 5, 1, 0.001)));
        client.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok, new LlmUsage(20, 8, 2, 0.002)));

        var result = await Loop(client, Echo()).RunAsync(Ask());

        Assert.NotNull(result.Usage);
        Assert.Equal(30, result.Usage!.InputTokens);
        Assert.Equal(13, result.Usage.OutputTokens);
        Assert.Equal(3, result.Usage.CacheReadTokens);
        Assert.Equal(0.003, result.Usage.CostUsd!.Value, 6);
    }

    [Fact]
    public async Task Aggregates_token_usage_across_every_call_native_path()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Ok, new LlmUsage(10, 5))
        { ToolCalls = [new LlmToolCall("c1", "echo", "{}")] });
        client.Replies.Enqueue(new LlmReply("done", LlmVerdict.Ok, new LlmUsage(20, 8)));

        var result = await NativeLoop(client, Echo()).RunAsync(Ask());

        Assert.Equal(30, result.Usage!.InputTokens);
        Assert.Equal(13, result.Usage.OutputTokens);
        Assert.Null(result.Usage.CostUsd); // neither reply reported a cost → null, not 0
    }

    [Fact]
    public async Task Usage_is_null_when_no_provider_reports_any()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok)); // no usage

        var result = await Loop(client, Echo()).RunAsync(Ask());

        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task Usage_is_surfaced_on_the_no_tools_single_completion()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("just answered", LlmVerdict.Ok, new LlmUsage(7, 3)));

        var result = await new ToolLoop(client, new ToolRegistry([]), Options()).RunAsync(Ask());

        Assert.Equal(7, result.Usage!.InputTokens);
        Assert.Equal(3, result.Usage.OutputTokens);
    }

    // ---- native tool-calling path (client.SupportsToolCalls == true) ---------------------------------

    private static ToolLoop NativeLoop(FakeLlmClient client, params ITool[] tools)
    {
        client.SupportsToolCallsResult = true;
        return new ToolLoop(client, new ToolRegistry(tools), Options());
    }

    [Fact]
    public async Task Native_executes_the_models_tool_calls_and_feeds_results_back()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Ok)
        { ToolCalls = [new LlmToolCall("call_1", "echo", """{"x":1}""")] });
        client.Replies.Enqueue(new LlmReply("all done", LlmVerdict.Ok)); // no tool calls → final

        var result = await NativeLoop(client, Echo()).RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("all done", result.Answer);
        var step = Assert.Single(result.Steps);
        Assert.Equal("echo", step.Tool);
        Assert.Equal("""observed:{"x":1}""", step.Result);

        // first turn sent the tool declarations; second turn carried the assistant-tool-call + tool-result
        Assert.NotNull(client.Calls[0].Tools);
        Assert.Contains(client.Calls[0].Tools!, t => t.Name == "echo");
        Assert.Contains(client.Calls[1].Messages, m => m.ToolCalls is { Count: > 0 });
        Assert.Contains(client.Calls[1].Messages, m => m.Role == "tool" && m.ToolCallId == "call_1");
        // native path sends no prompt-protocol system message
        Assert.DoesNotContain(client.Calls[0].Messages, m => m.Role == "system");
    }

    [Fact]
    public async Task Native_handles_parallel_tool_calls_in_one_turn()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Ok)
        {
            ToolCalls =
            [
                new LlmToolCall("call_1", "echo", """{"a":1}"""),
                new LlmToolCall("call_2", "echo", """{"b":2}"""),
            ],
        });
        client.Replies.Enqueue(new LlmReply("combined", LlmVerdict.Ok));

        var result = await NativeLoop(client, Echo()).RunAsync(Ask());

        Assert.Equal(2, result.Steps.Count);                          // both executed, order preserved
        Assert.Equal("""observed:{"a":1}""", result.Steps[0].Result);
        Assert.Equal("""observed:{"b":2}""", result.Steps[1].Result);
        var toolResults = client.Calls[1].Messages.Where(m => m.Role == "tool").ToList();
        Assert.Equal(["call_1", "call_2"], toolResults.Select(m => m.ToolCallId)); // one result per call id
    }

    [Fact]
    public async Task Native_unknown_tool_is_reported_back_not_thrown()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Ok)
        { ToolCalls = [new LlmToolCall("call_1", "nope", "{}")] });
        client.Replies.Enqueue(new LlmReply("recovered", LlmVerdict.Ok));

        var result = await NativeLoop(client, Echo()).RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("recovered", result.Answer);
        Assert.Contains("unknown tool", result.Steps[0].Result);
    }

    [Fact]
    public async Task Native_non_convergence_returns_Failed()
    {
        var client = new FakeLlmClient();
        for (var i = 0; i < 10; i++)
            client.Replies.Enqueue(new LlmReply("", LlmVerdict.Ok)
            { ToolCalls = [new LlmToolCall($"call_{i}", "echo", "{}")] });

        client.SupportsToolCallsResult = true;
        var result = await new ToolLoop(client, new ToolRegistry([Echo()]), Options(max: 3)).RunAsync(Ask());

        Assert.Equal(LlmVerdict.Failed, result.Verdict);
        Assert.Equal(3, result.Steps.Count);                          // one call per turn, budget 3
        Assert.Contains("did not converge", result.Detail);
    }

    [Fact]
    public async Task Native_surfaces_a_non_Ok_verdict()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.RateLimited, Detail: "slow down"));

        var result = await NativeLoop(client, Echo()).RunAsync(Ask());

        Assert.Equal(LlmVerdict.RateLimited, result.Verdict);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public async Task With_no_tools_registered_it_is_a_single_plain_completion()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("just answered", LlmVerdict.Ok));

        var loop = new ToolLoop(client, new ToolRegistry([]), Options());
        var result = await loop.RunAsync(Ask());

        Assert.True(result.Ok);
        Assert.Equal("just answered", result.Answer);
        Assert.Single(client.Calls);                    // one call, no protocol/JSON coercion
        Assert.DoesNotContain(client.Calls[0].Messages, m => m.Role == "system"); // no protocol prompt injected
    }

    // ---- TL2: live-progress StreamAsync ------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_prompt_path_yields_toolcall_result_text_then_terminal_in_order()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{"x":1}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"all done"}""", LlmVerdict.Ok));

        var events = await Loop(client, Echo()).StreamAsync(Ask()).ToListAsync();

        var call = events.OfType<ToolCall>().Single();
        Assert.Equal("echo", call.Name);
        Assert.Equal("""{"x":1}""", call.ArgumentsJson);
        var res = events.OfType<ToolResult>().Single();
        Assert.Contains("observed:", res.Content);
        Assert.False(res.IsError);
        Assert.Contains(events.OfType<TextDelta>(), t => t.Text == "all done");
        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal("all done", ended.FinalText);

        // strict ordering: ToolCall → its ToolResult → the single terminal
        Assert.True(events.IndexOf(call) < events.IndexOf(res));
        Assert.True(events.IndexOf(res) < events.FindIndex(e => e is SessionEnded));
    }

    [Fact]
    public async Task StreamAsync_native_path_yields_live_events()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Ok)
        { ToolCalls = [new LlmToolCall("call_1", "echo", """{"x":1}""")] });
        client.Replies.Enqueue(new LlmReply("all done", LlmVerdict.Ok));

        var events = await NativeLoop(client, Echo()).StreamAsync(Ask()).ToListAsync();

        var call = events.OfType<ToolCall>().Single();
        Assert.Equal("echo", call.Name);
        Assert.Equal("call_1", call.CallId);                 // native path carries the model's call id
        Assert.Equal("call_1", events.OfType<ToolResult>().Single().CallId);
        Assert.Contains(events.OfType<TextDelta>(), t => t.Text == "all done");
        Assert.Equal(LlmVerdict.Ok, events.OfType<SessionEnded>().Single().Verdict);
    }

    [Fact]
    public async Task StreamAsync_surfaces_a_non_ok_verdict_as_an_error_terminal()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("", LlmVerdict.Refused, Detail: "policy"));

        var events = await Loop(client, Echo()).StreamAsync(Ask()).ToListAsync();

        Assert.Empty(events.OfType<ToolCall>());
        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Refused, ended.Verdict);
        Assert.True(ended.IsError);
        Assert.Equal("policy", ended.Diagnostic);
    }

    [Fact]
    public async Task StreamAsync_emits_a_usage_final_when_a_provider_reports_usage()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{}}""", LlmVerdict.Ok, new LlmUsage(10, 5)));
        client.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok, new LlmUsage(20, 8)));

        var events = await Loop(client, Echo()).StreamAsync(Ask()).ToListAsync();

        var usage = events.OfType<UsageFinal>().Single();
        Assert.Equal(30, usage.InputTokens);
        Assert.Equal(13, usage.OutputTokens);
        // the UsageFinal precedes the terminal
        Assert.True(events.FindIndex(e => e is UsageFinal) < events.FindIndex(e => e is SessionEnded));
    }

    [Fact]
    public async Task StreamAsync_reports_a_throwing_tool_as_an_error_result()
    {
        var boom = new FunctionTool("boom", (_, _) => throw new InvalidOperationException("kaboom"));
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"boom","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"handled"}""", LlmVerdict.Ok));

        var events = await Loop(client, boom).StreamAsync(Ask()).ToListAsync();

        var res = events.OfType<ToolResult>().Single();
        Assert.True(res.IsError);
        Assert.Contains("error: kaboom", res.Content);
    }

    [Fact]
    public async Task Default_StreamAsync_folds_RunAsync_for_a_byo_implementation()
    {
        // A BYO IToolLoop that implements ONLY RunAsync still gets a functional (post-hoc) event stream via
        // the interface's default StreamAsync.
        IToolLoop byo = new RunOnlyLoop();

        var events = await byo.StreamAsync(Ask()).ToListAsync();

        Assert.Contains(events, e => e is ToolCall { Name: "t" });
        Assert.Contains(events, e => e is ToolResult { Content: "obs" });
        Assert.Contains(events, e => e is TextDelta { Text: "answer" });
        var ended = events.OfType<SessionEnded>().Single();
        Assert.Equal(LlmVerdict.Ok, ended.Verdict);
        Assert.Equal("answer", ended.FinalText);
    }

    private sealed class RunOnlyLoop : IToolLoop
    {
        public Task<ToolLoopResult> RunAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default)
            => Task.FromResult(new ToolLoopResult("answer", LlmVerdict.Ok, [new ToolStep("t", "{}", "obs")]));
    }

    // R2 — guards cover the tool loop: a denied term in tool ARGS or in a tool OBSERVATION is a jail
    // violation, not something that bypasses the rail because it never touched the initial/final gate.
    [Fact]
    public async Task Blocks_a_tool_call_whose_args_contain_a_denied_term()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{"x":"launch the nukes"}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"unreached"}""", LlmVerdict.Ok));
        var loop = new ToolLoop(client, new ToolRegistry([Echo()]), Options(),
            guards: new Lyntai.Guards.GuardRail([new Lyntai.Guards.DenylistGuard(["nukes"])]));

        var result = await loop.RunAsync(Ask());

        Assert.Equal(LlmVerdict.Refused, result.Verdict);
        Assert.Contains("nukes", result.Detail);
        Assert.Empty(result.Steps);       // the tool never executed
        Assert.Single(client.Calls);      // loop aborted before the next model turn
    }

    [Fact]
    public async Task Blocks_a_tool_observation_that_contains_a_denied_term()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"leak","arguments":{}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"unreached"}""", LlmVerdict.Ok));
        var leak = new FunctionTool("leak", (_, _) => Task.FromResult("here is the SECRET_KEY value"), "leaks");
        var loop = new ToolLoop(client, new ToolRegistry([leak]), Options(),
            guards: new Lyntai.Guards.GuardRail([new Lyntai.Guards.DenylistGuard(["SECRET_KEY"])]));

        var result = await loop.RunAsync(Ask());

        Assert.Equal(LlmVerdict.Refused, result.Verdict);
        Assert.Single(client.Calls);      // aborted — the observation is never fed back to the model
    }

    [Fact]
    public async Task Allows_a_clean_tool_call_when_a_guard_is_present()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"tool":"echo","arguments":{"x":1}}""", LlmVerdict.Ok));
        client.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok));
        var loop = new ToolLoop(client, new ToolRegistry([Echo()]), Options(),
            guards: new Lyntai.Guards.GuardRail([new Lyntai.Guards.DenylistGuard(["forbidden"])]));

        var result = await loop.RunAsync(Ask());

        Assert.True(result.Ok);            // nothing denied → the loop runs normally
        Assert.Equal("done", result.Answer);
        Assert.Single(result.Steps);
    }
}
