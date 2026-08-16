using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Providers.Local;

namespace Lyntai.Tests.Providers;

/// <summary>Which providers declare NATIVE tool-calling, and — more usefully — which deliberately do not.
///
/// <para><b>This exists because the roadmap asked for the opposite.</b> "Native tool-calling for the
/// ClaudeCli/Local providers (both stay on the prompt fallback)" sat under §9 as a low-value deferral until
/// 2026-08-16, when acting on it showed the request was misframed and would have made things WORSE.
/// <c>ILlmProvider.SupportsToolCalls</c> means "I return the model's calls on <c>LlmReply.ToolCalls</c> for
/// YOUR loop to execute". Flipping it true on a provider that cannot do that makes <c>ToolLoop</c> take the
/// native path, send tool declarations the backend ignores, and then wait for calls that never arrive — so
/// every agentic turn silently degrades to "the model answered in prose" and no tool ever runs.</para>
///
/// <para>The reasoning per backend is on each test. <c>docs/DECISIONS.md</c> D74 records the retirement.</para></summary>
public class NativeToolCallPostureTests
{
    [Fact]
    public void The_claude_CLI_does_NOT_declare_native_tool_calls_because_it_runs_its_own_loop()
    {
        // The CLI cannot hand an unexecuted tool call back: its whole tool surface is --allowedTools /
        // --disallowedTools / --mcp-config (verified against v2.1.220's --help), which are ways to GIVE it
        // tools, not to receive its calls. stream-json reports the tool_use blocks it already ran.
        //
        // The real need — an app's own ITools reachable by the CLI — is met by ICliToolProvisioner, which
        // stands up an in-process MCP server and passes --mcp-config (shipped 1.1). So the tools do run in
        // this process, with the host's guards applied; they simply are not shaped as LlmReply.ToolCalls.
        Assert.False(new ClaudeCliDialect().SupportsToolCalls);
    }

    [Fact]
    public void The_codex_CLI_does_not_either_for_the_same_structural_reason()
    {
        Assert.False(new CodexCliDialect().SupportsToolCalls);
    }

    [Fact]
    public void A_local_GGUF_model_does_NOT_declare_native_tool_calls_because_NATIVE_is_not_one_format()
    {
        // LocalProvider runs an arbitrary GGUF through llama.cpp, and tool-call syntax is per model FAMILY —
        // Llama's python_tag, Qwen's tool_call XML, Mistral's TOOL_CALLS, and more. There is no single
        // native format to parse, so declaring support would mean either picking one family (breaking every
        // other model a host might load) or shipping a per-family registry nobody can measure without
        // downloading each one. The prompt protocol is the model-AGNOSTIC answer, which is why it is not a
        // fallback here so much as the correct mechanism.
        var provider = new LocalProvider("local", new LocalModelOptions { ModelPath = "does-not-need-to-exist.gguf" }, new LyntaiOptions());

        Assert.False(((ILlmProvider)provider).SupportsToolCalls);
    }

    [Fact]
    public void Nor_does_either_declare_STREAMING_tool_calls_which_would_be_a_stronger_claim_still()
    {
        // The 3.0 streaming capability (D71) is separate and defaults false. A provider that cannot deliver
        // calls at all certainly cannot deliver them mid-stream, and answering yes here would send ToolLoop
        // down its streaming native path to wait for chunks that never come.
        var local = new LocalProvider("local", new LocalModelOptions { ModelPath = "x.gguf" }, new LyntaiOptions());

        Assert.False(((ILlmProvider)local).SupportsStreamingToolCalls);
    }
}
