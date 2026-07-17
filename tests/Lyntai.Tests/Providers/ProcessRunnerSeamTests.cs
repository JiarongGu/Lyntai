using System.Runtime.CompilerServices;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>The BYO process-execution seam: an app supplies its own <see cref="IProcessRunner"/> and
/// the claude-cli provider spawns through it — no real process involved.</summary>
public class ProcessRunnerSeamTests
{
    /// <summary>A fake runner that returns canned stream-json instead of spawning anything — proof
    /// that process execution is fully app-owned through the interface.</summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<(string Command, IReadOnlyList<string> Args, string? Stdin)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? timeout = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null, CancellationToken ct = default)
        {
            Calls.Add((command, args, stdin));
            const string streamJson =
                """{"type":"assistant","message":{"content":[{"type":"text","text":"served by a custom runner"}]}}""" + "\n" +
                """{"type":"result","result":"served by a custom runner","usage":{"input_tokens":5,"output_tokens":3},"total_cost_usd":0.001}""";
            return Task.FromResult(new ProcessResult(0, streamJson, "", TimedOut: false));
        }

        public async IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args,
            string? stdin = null, TimeSpan? timeout = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls.Add((command, args, stdin));
            yield return """{"type":"assistant","message":{"content":[{"type":"text","text":"streamed by a custom runner"}]}}""";
            yield return """{"type":"result","result":"streamed by a custom runner","usage":{"input_tokens":5,"output_tokens":3}}""";
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Custom_process_runner_drives_the_cli_provider()
    {
        var runner = new FakeProcessRunner();
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
        var provider = new ClaudeCliProvider(new FakeProcessRunner(), new LyntaiOptions(), command: "claude");
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public async Task Registered_custom_runner_wins_in_di()
    {
        var runner = new FakeProcessRunner();
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
