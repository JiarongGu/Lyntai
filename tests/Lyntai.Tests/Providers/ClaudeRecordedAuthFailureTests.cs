using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>What the Claude CLI prints for a key it refuses, RECORDED: <c>claude -p hi --output-format stream-json --verbose</c>
/// on 2.1.325 with an invalid <c>ANTHROPIC_API_KEY</c> and an empty <c>CLAUDE_CONFIG_DIR</c> (2026-11-15). The refusal is in band:
/// an assistant line flagged <c>error</c> and an <c>is_error</c> result carrying <c>api_error_status</c> 401, under a
/// <c>success</c> subtype. Stderr was EMPTY and the process exited 1, so the exit must not decide the verdict over the fields.
/// The init line is cut to its session id (the recorded one names the working directory); the other two are as printed.</summary>
public class ClaudeRecordedAuthFailureTests
{
    private static readonly string[] Recorded =
    [
        """{"type":"system","subtype":"init","session_id":"d37b15cb-903f-4cac-8cee-8c283a33cb64","model":"claude-sonnet"}""",
        """{"type":"assistant","message":{"diagnostics":null,"id":"f82b77b5-9693-4177-8af1-61bdae9e1b02","container":null,"model":"<synthetic>","role":"assistant","stop_details":null,"stop_reason":"stop_sequence","stop_sequence":"","type":"message","usage":{"output_tokens_details":null,"input_tokens":0,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"server_tool_use":{"web_search_requests":0,"web_fetch_requests":0},"service_tier":null,"cache_creation":{"ephemeral_1h_input_tokens":0,"ephemeral_5m_input_tokens":0},"inference_geo":null,"iterations":null,"speed":null,"fallback_credit":null},"content":[{"type":"text","text":"Invalid API key · Fix external API key"}],"context_management":null},"parent_tool_use_id":null,"session_id":"d37b15cb-903f-4cac-8cee-8c283a33cb64","uuid":"e806f912-55a5-4c53-8938-d6618ef28264","timestamp":"2026-11-14T16:35:06.494Z","error":"authentication_failed","request_id":"req_011Ch39MnyXZ2FErN8K8HLWb","is_api_error_message":true,"api_error_status":401,"api_error_type":"authentication_error","api_error_origin":"first_party"}""",
        """{"duration_api_ms":0,"stop_reason":"stop_sequence","session_id":"d37b15cb-903f-4cac-8cee-8c283a33cb64","total_cost_usd":0,"usage":{"output_tokens_details":{"thinking_tokens":0},"input_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":0,"server_tool_use":{"web_search_requests":0,"web_fetch_requests":0},"service_tier":"standard","cache_creation":{"ephemeral_1h_input_tokens":0,"ephemeral_5m_input_tokens":0},"inference_geo":"","iterations":[],"speed":"standard","fallback_credit":null},"modelUsage":{},"permission_denials":[],"terminal_reason":"api_error","fast_mode_state":"off","fast_mode_disabled_reason":"sdk_opt_in_required","subagent_stats":{"spawned":0,"requested":{"background":0,"foreground":0,"unset":0},"started_in_background":0,"max_depth":0,"spawned_by_subagents":0,"completed":0,"failed":0,"killed":{"parent":0,"user":0,"system":0},"refused":{"depth_limit":0,"concurrency_limit":0,"budget":0},"by_type":{}},"safety_stops":0,"is_error":true,"num_turns":1,"subtype":"success","api_error_status":401,"api_error_type":"authentication_error","api_error_origin":"first_party","result":"Invalid API key · Fix external API key","type":"result","duration_ms":1414,"uuid":"8ca09af4-3f56-4d37-842e-2649e577ba2b","queued_turn_count":0,"result_index":0,"model_usage_since_last_result":{}}""",
    ];

    private static TextRequest Ask() => new() { Messages = [TextMessage.User("hi")] };

    [Fact]
    public async Task The_agent_door_reads_the_refused_key_from_the_fields_whatever_the_exit()
    {
        var session = new ClaudeAgentSession(
            new FakeProcessRunner(Recorded, new ProcessRunException("claude", 1, "")), new LyntaiOptions(), command: "claude");

        var ended = (await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync()).OfType<SessionEnded>().Single();

        Assert.Equal(ProviderVerdict.AuthFailed, ended.Verdict);
        Assert.Equal(AgentFailureKind.Access, ended.Failure!.Kind);
        Assert.Equal("claude.api_error_status=401", ended.Failure.By);
    }

    [Fact]
    public async Task The_one_shot_provider_reads_the_refused_key_from_the_fields_whatever_the_exit()
    {
        var provider = new ClaudeCliProvider(
            new FakeProcessRunner { RunResult = new ProcessResult(1, string.Join("\n", Recorded), "") }, new LyntaiOptions(), command: "claude");

        var reply = await provider.CompleteAsync(Ask());

        Assert.Equal(ProviderVerdict.AuthFailed, reply.Verdict);
        Assert.Contains("Invalid API key", reply.Detail);
    }

    [Fact]
    public async Task The_one_shot_stream_reads_the_refused_key_from_the_fields_whatever_the_exit()
    {
        var provider = new ClaudeCliProvider(
            new FakeProcessRunner(Recorded, new ProcessRunException("claude", 1, "")), new LyntaiOptions(), command: "claude");

        var chunks = await provider.StreamAsync(Ask()).ToListAsync();

        Assert.DoesNotContain(chunks, c => c.Kind == TextChunkKind.Content);
        Assert.Equal(TextChunkKind.Error, chunks[^1].Kind);
        Assert.Equal(ProviderVerdict.AuthFailed, chunks[^1].Verdict);
    }
}
