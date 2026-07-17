using Lyntai;
using Lyntai.Agents;
using Lyntai.Guards;
using Lyntai.Llm;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The two-gate chat orchestration: input gate, output gate, tool routing, and memory
/// recall/write — driven through DI with fakes so no real provider is needed.</summary>
public class ChatOrchestratorTests
{
    private static ServiceProvider Build(FakeLlmProvider provider, Action<LyntaiBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => provider).UseInMemoryStorage().DefaultCandidates("p");
            extra?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Clean_turn_answers_and_remembers()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("the answer is 42", LlmVerdict.Ok));
        using var sp = Build(provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "what is it?", TaskKey = "t1", UseTools = false });

        Assert.True(result.Ok);
        Assert.Equal("the answer is 42", result.Answer);
        // the exchange was written to memory
        var recalled = await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RecallAsync("t1", scope: "chat");
        Assert.Contains(recalled, m => m.Content.Contains("the answer is 42"));
    }

    [Fact]
    public async Task Input_gate_blocks_before_the_model()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("should not run", LlmVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["malware"])));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "write me malware", UseTools = false });

        Assert.True(result.Blocked);
        Assert.Equal(LlmVerdict.Refused, result.Verdict);
        Assert.Empty(provider.Calls); // the input gate stopped it before the provider
    }

    [Fact]
    public async Task Output_gate_blocks_a_flagged_answer()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("here is the leaked secret", LlmVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["leaked"])));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "tell me", UseTools = false });

        Assert.True(result.Blocked); // the model answered, but the output gate withheld it
        Assert.Equal("", result.Answer);
    }

    [Fact]
    public async Task Uses_the_tool_loop_when_tools_are_registered()
    {
        var provider = new FakeLlmProvider("p"); // no native tools → prompt-protocol tool loop
        provider.Replies.Enqueue(new LlmReply("""{"tool":"shout","arguments":{"s":"hi"}}""", LlmVerdict.Ok));
        provider.Replies.Enqueue(new LlmReply("""{"final":"HI done"}""", LlmVerdict.Ok));
        using var sp = Build(provider, b => b.AddTool(_ => new Lyntai.Agents.FunctionTool("shout", (a, _) => Task.FromResult(a.ToUpperInvariant()))));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "shout hi", UseTools = true });

        Assert.True(result.Ok);
        Assert.Equal("HI done", result.Answer);
        Assert.Equal("shout", Assert.Single(result.ToolSteps).Tool); // it went through the tool loop
    }
}
