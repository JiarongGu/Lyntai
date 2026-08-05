using Lyntai;
using Lyntai.Agents;
using Lyntai.Guards;
using Lyntai.Llm;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>What a chat turn cost. <see cref="ToolLoopResult.Usage"/> has always carried the loop's summed
/// token/cost figure, but <see cref="ChatResult"/> had nowhere to put it — so <see cref="ChatOrchestrator"/>
/// dropped it (and the plain-completion path's <see cref="LlmReply.Usage"/> with it), and a chat consumer had
/// to wrap <see cref="ILlmClient"/> in its own front-door decorator to learn the number the loop had already
/// computed. These pin <see cref="ChatResult.Usage"/> on every exit that reached a provider — and pin it null
/// on the one that never did.</summary>
public class ChatResultUsageTests
{
    private static ServiceProvider Build(FakeLlmProvider provider, Action<LyntaiBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => provider).UseInMemoryStorage().UseDefaultCandidates("p");
            extra?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact] // the headline: the tool loop's summed usage reaches the caller instead of being discarded
    public async Task Tool_loop_usage_is_surfaced_on_the_chat_result()
    {
        var provider = new FakeLlmProvider("p"); // no native tools → prompt-protocol tool loop, two turns
        provider.Replies.Enqueue(new LlmReply("""{"tool":"shout","arguments":{"s":"hi"}}""", LlmVerdict.Ok, new LlmUsage(10, 5)));
        provider.Replies.Enqueue(new LlmReply("""{"final":"HI done"}""", LlmVerdict.Ok, new LlmUsage(7, 3, 2, 0.25)));
        using var sp = Build(provider, b => b.AddTool(_ => new FunctionTool("shout", (a, _) => Task.FromResult(a.ToUpperInvariant()))));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "shout hi", UseTools = true });

        Assert.True(result.Ok);
        Assert.NotNull(result.Usage);                       // was null: the orchestrator discarded it
        Assert.Equal(17, result.Usage!.InputTokens);        // BOTH loop turns, summed — not just the last
        Assert.Equal(8, result.Usage.OutputTokens);
        Assert.Equal(2, result.Usage.CacheReadTokens);
        Assert.Equal(0.25, result.Usage.CostUsd!.Value, 6); // summed when any turn reported a cost
    }

    [Fact] // the no-tools path discarded the reply's usage the same way
    public async Task Plain_completion_usage_is_surfaced_on_the_chat_result()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("the answer is 42", LlmVerdict.Ok, new LlmUsage(12, 4)));
        using var sp = Build(provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "what is it?", UseTools = false });

        Assert.True(result.Ok);
        Assert.Equal(12, result.Usage!.InputTokens);
        Assert.Equal(4, result.Usage.OutputTokens);
    }

    [Fact] // a provider that surfaces no tokens (a CLI one) must not become a misleading all-zero figure
    public async Task Usage_stays_null_when_no_provider_reported_any()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("no tokens here", LlmVerdict.Ok));
        using var sp = Build(provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "hello", UseTools = false });

        Assert.True(result.Ok);
        Assert.Null(result.Usage);
    }

    [Fact] // a failed turn still SPENT the tokens — the figure is what the turn cost, not what it returned
    public async Task Usage_is_surfaced_on_a_non_ok_verdict()
    {
        var provider = new FakeLlmProvider("p");
        // Refused is FallbackAction.Surface under the default RoutingPolicy — no retry, no next candidate —
        // so the reply the orchestrator sees is exactly this one, usage and all.
        provider.Replies.Enqueue(new LlmReply("", LlmVerdict.Refused, new LlmUsage(9, 0), "policy"));
        using var sp = Build(provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "something disallowed", UseTools = false });

        Assert.False(result.Ok);
        Assert.Equal(LlmVerdict.Refused, result.Verdict);
        Assert.Equal(9, result.Usage!.InputTokens);
    }

    [Fact] // the input gate returns BEFORE any provider call — there is no figure, and zero would be a lie
    public async Task Usage_stays_null_when_the_input_gate_blocked_before_the_model()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("should not run", LlmVerdict.Ok, new LlmUsage(99, 99)));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["malware"])));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "write me malware", UseTools = false });

        Assert.True(result.Blocked);
        Assert.Empty(provider.Calls); // never reached a provider…
        Assert.Null(result.Usage);    // …so there is nothing to report
    }

    [Fact] // …but an OUTPUT-gate block already spent the tokens, so it reports them
    public async Task Usage_is_surfaced_when_the_output_gate_blocked_the_answer()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("here is the leaked secret", LlmVerdict.Ok, new LlmUsage(6, 11)));
        using var sp = Build(provider, b => b.AddGuard(_ => new DenylistGuard(["leaked"])));

        var result = await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "tell me", UseTools = false });

        Assert.True(result.Blocked);
        Assert.Equal("", result.Answer);
        Assert.Equal(11, result.Usage!.OutputTokens);
    }
}
