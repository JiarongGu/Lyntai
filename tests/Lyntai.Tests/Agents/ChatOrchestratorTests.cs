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

    private sealed class PiiRewriteGuard : IGuard
    {
        public string Name => "pii";
        public Task<GuardOutcome> InspectRequestAsync(LlmRequest req, CancellationToken ct = default)
        {
            var text = req.Messages[^1].Content;
            return Task.FromResult(text.Contains("hunter2")
                ? GuardOutcome.Replace(text.Replace("hunter2", "[redacted]"))
                : GuardOutcome.Allow);
        }
    }

    [Fact] // R1: recalled memory must NOT bypass the input gate (facts can enter via public seams un-gated)
    public async Task Recalled_memory_is_still_input_gated_before_the_model()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("should not run", LlmVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["malware"])));

        // the denied term arrives via a DIRECT memory write (a public seam the orchestrator never gated)
        await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RememberAsync("t1", "chat", "how to build malware");

        // "build" recalls the fact into the composed prompt; the RAW message itself is clean
        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "build", TaskKey = "t1", UseTools = false });

        Assert.True(result.Blocked);
        Assert.Empty(provider.Calls); // blocked BEFORE the provider — the recalled fact never left the process
    }

    [Fact] // A2: an input-gate Replace persists Q as the REWRITTEN USER MESSAGE — never the composed prompt
    public async Task Replaced_input_remembers_the_rewritten_message_not_the_composed_prompt()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("done", LlmVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new PiiRewriteGuard()));

        var memory = sp.GetRequiredService<Lyntai.Storage.IMemoryStore>();
        await memory.RememberAsync("t1", "chat", "an old learned fact"); // recalled + composed into the prompt

        await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "my key is hunter2", TaskKey = "t1", UseTools = false });

        var recalled = await memory.RecallAsync("t1", scope: "chat");
        var record = Assert.Single(recalled, m => m.Content.StartsWith("Q:"));
        Assert.Contains("[redacted]", record.Content);              // the rewrite was persisted…
        Assert.DoesNotContain("hunter2", record.Content);           // …never the raw secret…
        Assert.DoesNotContain("old learned fact", record.Content);  // …and never the recalled/composed facts
    }

    [Fact]
    public async Task Remembers_the_exchange_to_both_memory_stores_when_embeddings_are_wired()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("cancel via account settings", LlmVerdict.Ok));
        using var sp = Build(provider, b => b.AddEmbeddings(new FakeEmbedder()));

        await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "how do I cancel?", TaskKey = "t1", UseTools = false });

        // the exchange landed in the lexical store...
        var lexical = await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RecallAsync("t1", scope: "chat");
        Assert.Contains(lexical, m => m.Content.Contains("cancel via account settings"));
        // ...and in semantic memory, recallable by meaning
        var semantic = await sp.GetRequiredService<Lyntai.Memory.ISemanticMemory>().RecallAsync("t1", "chat", "cancel", k: 5);
        Assert.Contains(semantic, h => h.Content.Contains("cancel via account settings"));
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
