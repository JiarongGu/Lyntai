using Lyntai;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The BYO process-execution seam: an app supplies its own <see cref="IProcessRunner"/> and
/// the claude-cli provider spawns through it — no real process involved.</summary>
public class ProcessRunnerSeamTests
{
    /// <summary>The shared fake configured with canned stream-json instead of spawning anything —
    /// proof that process execution is fully app-owned through the interface.</summary>
    private static FakeProcessRunner CannedRunner() => new(streamLines:
    [
        """{"type":"assistant","message":{"content":[{"type":"text","text":"streamed by a custom runner"}]}}""",
        """{"type":"result","result":"streamed by a custom runner","usage":{"input_tokens":5,"output_tokens":3}}""",
    ])
    {
        RunResult = new ProcessResult(0,
            """{"type":"assistant","message":{"content":[{"type":"text","text":"served by a custom runner"}]}}""" + "\n" +
            """{"type":"result","result":"served by a custom runner","usage":{"input_tokens":5,"output_tokens":3},"total_cost_usd":0.001}""",
            "", TimedOut: false),
    };

    [Fact]
    public async Task Custom_process_runner_drives_the_cli_provider()
    {
        var runner = CannedRunner();
        var provider = new ClaudeCliProvider(runner, new LyntaiOptions(), command: "claude");

        var reply = await provider.CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("served by a custom runner", reply.Text);
        Assert.Equal(5, reply.Usage!.InputTokens);
        Assert.Single(runner.Calls);
        Assert.Equal("hi", runner.Calls[0].Stdin); // the prompt went over stdin, as designed
    }

    [Fact]
    public void Custom_runner_makes_the_provider_available_regardless_of_local_path()
    {
        // a BYO runner resolves the command in ITS environment (sandbox/remote), so IsAvailable must
        // NOT depend on the host's local PATH — else the router would skip it and never call the runner
        var provider = new ClaudeCliProvider(CannedRunner(), new LyntaiOptions(), command: "claude");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public async Task Registered_custom_runner_wins_in_di()
    {
        var runner = CannedRunner();
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner); // app registers its own BEFORE AddLyntai
        services.AddLyntai(b => b.AddClaudeCliProvider().DefaultCandidates("claude-cli"));
        using var sp = services.BuildServiceProvider();

        // the TryAdd default must not shadow the app's runner
        Assert.Same(runner, sp.GetRequiredService<IProcessRunner>());

        var reply = await sp.GetRequiredService<ILlmClient>()
            .CompleteAsync(new LlmRequest { Messages = [LlmMessage.User("via di")] });
        Assert.Equal("served by a custom runner", reply.Text);
    }
}
