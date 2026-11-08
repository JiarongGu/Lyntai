using Lyntai.Inference;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Guards;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The two-gate chat orchestration: input gate, output gate, tool routing, and memory
/// recall/write — driven through DI with fakes so no real provider is needed.</summary>
public class ChatOrchestratorTests
{
    private static ServiceProvider Build(FakeTextProvider provider, Action<LyntaiBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => provider).UseInMemoryStorage().UseDefaultCandidates("p");
            extra?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Clean_turn_answers_and_remembers()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("the answer is 42", ProviderVerdict.Ok));
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
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("should not run", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["malware"])));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "write me malware", UseTools = false });

        Assert.True(result.Blocked);
        Assert.Equal(ProviderVerdict.Refused, result.Verdict);
        Assert.Empty(provider.Calls); // the input gate stopped it before the provider
    }

    [Fact]
    public async Task Output_gate_blocks_a_flagged_answer()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("here is the leaked secret", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["leaked"])));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "tell me", UseTools = false });

        Assert.True(result.Blocked); // the model answered, but the output gate withheld it
        Assert.Equal("", result.Answer);
    }

    private sealed class PiiRewriteGuard : IGuard
    {
        public string Name => "pii";
        public Task<GuardOutcome> InspectRequestAsync(TextRequest req, CancellationToken ct = default)
        {
            var text = req.Messages[^1].Content;
            return Task.FromResult(text.Contains("hunter2")
                ? GuardOutcome.Replace(text.Replace("hunter2", "[redacted]"))
                : GuardOutcome.Allow);
        }
    }

    [Fact] // Recalled memory must NOT bypass the input gate (facts can enter via public seams un-gated)
    public async Task Recalled_memory_is_still_input_gated_before_the_model()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("should not run", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["malware"])));

        // the denied term arrives via a DIRECT memory write (a public seam the orchestrator never gated)
        await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RememberAsync("t1", "chat", "how to build malware");

        // "build" recalls the fact into the composed prompt; the RAW message itself is clean
        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "build", TaskKey = "t1", UseTools = false });

        Assert.True(result.Blocked);
        Assert.Empty(provider.Calls); // blocked BEFORE the provider — the recalled fact never left the process
    }

    [Fact] // An input-gate Replace persists Q as the REWRITTEN USER MESSAGE — never the composed prompt
    public async Task Replaced_input_remembers_the_rewritten_message_not_the_composed_prompt()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("done", ProviderVerdict.Ok));
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

    private sealed class RedactingGuard(string secret) : IGuard
    {
        public string Name => "redact";

        public Task<GuardOutcome> InspectRequestAsync(TextRequest req, CancellationToken ct = default)
        {
            var text = req.Messages[^1].Content;
            return Task.FromResult(text.Contains(secret, StringComparison.Ordinal)
                ? GuardOutcome.Replace(text.Replace(secret, "[redacted]", StringComparison.Ordinal))
                : GuardOutcome.Allow);
        }

        public Task<GuardOutcome> InspectResponseAsync(TextResponse reply, CancellationToken ct = default) =>
            Task.FromResult(reply.Text.Contains(secret, StringComparison.Ordinal)
                ? GuardOutcome.Replace(reply.Text.Replace(secret, "[redacted]", StringComparison.Ordinal))
                : GuardOutcome.Allow);
    }

    [Fact] // gate 1b: a Replace on the COMPOSED prompt redacts what the model sees, and nothing recalled is re-persisted
    public async Task A_recalled_fact_the_composed_gate_redacts_is_redacted_for_the_model_and_never_re_stored()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("done", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new RedactingGuard("hunter2")));
        var memory = sp.GetRequiredService<Lyntai.Storage.IMemoryStore>();
        await memory.RememberAsync("t1", "chat", "how to build with hunter2");

        // the raw message is clean, so gate 1a allows it; only the composed prompt carries the secret
        await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "build", TaskKey = "t1", UseTools = false });

        var seen = provider.Calls.Single().Messages[^1].Content;
        Assert.Contains("[redacted]", seen);
        Assert.DoesNotContain("hunter2", seen);
        var record = Assert.Single(await memory.RecallAsync("t1", scope: "chat"), m => m.Content.StartsWith("Q:"));
        Assert.Equal("Q: build\nA: done", record.Content);
    }

    [Fact] // gate 2: a Replace on the answer is what the caller gets AND what is remembered
    public async Task An_answer_the_output_gate_redacts_is_returned_and_remembered_redacted()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("the code is hunter2", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddGuard(_ => new RedactingGuard("hunter2")));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "what is the code?", TaskKey = "t1", UseTools = false });

        Assert.True(result.Ok);
        Assert.Equal("the code is [redacted]", result.Answer);
        var record = Assert.Single(
            await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RecallAsync("t1", scope: "chat"));
        Assert.Equal("Q: what is the code?\nA: the code is [redacted]", record.Content);
    }

    [Fact]
    public async Task Remember_false_answers_and_writes_nothing()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("the answer", ProviderVerdict.Ok));
        using var sp = Build(provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "a question", TaskKey = "t1", UseTools = false, Remember = false });

        Assert.Equal("the answer", result.Answer);
        Assert.Empty(await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RecallAsync("t1", scope: "chat"));
    }

    [Fact] // no TaskKey = stateless: nothing recalled into the prompt, nothing written, even with Remember on
    public async Task A_turn_with_no_task_key_neither_recalls_nor_remembers()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("the answer", ProviderVerdict.Ok));
        using var sp = Build(provider);
        var memory = sp.GetRequiredService<Lyntai.Storage.IMemoryStore>();
        await memory.RememberAsync("t1", "chat", "a fact about build");

        await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "build", UseTools = false });

        Assert.Equal("build", provider.Calls.Single().Messages[^1].Content);
        var held = Assert.Single(await memory.RecallAsync("t1", scope: "chat"));
        Assert.Equal("a fact about build", held.Content);
    }

    [Fact] // a turn whose provider did not answer is not an exchange worth remembering
    public async Task A_non_Ok_verdict_returns_it_with_no_answer_and_remembers_nothing()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "down"));
        using var sp = Build(provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "a question", TaskKey = "t1", UseTools = false });

        Assert.False(result.Ok);
        Assert.NotEqual(ProviderVerdict.Ok, result.Verdict);
        Assert.Equal("", result.Answer);
        Assert.Empty(await sp.GetRequiredService<Lyntai.Storage.IMemoryStore>().RecallAsync("t1", scope: "chat"));
    }

    [Fact]
    public async Task Remembers_the_exchange_to_both_memory_stores_when_embeddings_are_wired()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse("cancel via account settings", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddProvider(_ => new FakeVectorProvider(), FakeVectorProvider.Declared));

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
        var provider = new FakeTextProvider("p"); // no native tools → prompt-protocol tool loop
        provider.Replies.Enqueue(new TextResponse("""{"tool":"shout","arguments":{"s":"hi"}}""", ProviderVerdict.Ok));
        provider.Replies.Enqueue(new TextResponse("""{"final":"HI done"}""", ProviderVerdict.Ok));
        using var sp = Build(provider, b => b.AddTool(_ => new Lyntai.Agents.FunctionTool("shout", (a, _) => Task.FromResult(a.ToUpperInvariant()))));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "shout hi", UseTools = true });

        Assert.True(result.Ok);
        Assert.Equal("HI done", result.Answer);
        Assert.Equal("shout", Assert.Single(result.ToolSteps).Tool); // it went through the tool loop
    }
}
