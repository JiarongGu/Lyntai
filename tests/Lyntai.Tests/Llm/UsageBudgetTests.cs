using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Lyntai.Llm.Caching;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Llm;

/// <summary>Usage budgeting: the in-memory tracker's per-consumer + global accounting, the front-door
/// decorator's record-then-enforce (refuse over a cap without hitting a provider), and — with caching —
/// the composed order (cache outermost, so a cached hit is free and never counts toward the budget).</summary>
public class UsageBudgetTests
{
    private static LlmRequest Ask(string consumer = "default") =>
        new() { Messages = [LlmMessage.User("q")], Consumer = consumer };

    private static LlmReply Ok(double cost, long tokens = 0) =>
        new("ok", LlmVerdict.Ok, new LlmUsage(tokens, 0, CostUsd: cost));

    // ---- tracker -------------------------------------------------------------------------------------

    [Fact]
    public void Tracker_accumulates_per_consumer_and_globally()
    {
        var tracker = new InMemoryUsageTracker();
        tracker.Record("a", new LlmUsage(10, 5, CostUsd: 0.10));
        tracker.Record("a", new LlmUsage(20, 5, CostUsd: 0.20));
        tracker.Record("b", new LlmUsage(1, 1, CostUsd: 0.01));

        var a = tracker.Total("a");
        Assert.Equal(30, a.InputTokens);
        Assert.Equal(40, a.TotalTokens);
        Assert.Equal(0.30, a.CostUsd, 5);
        Assert.Equal(2, a.Calls);

        var global = tracker.Total();
        Assert.Equal(0.31, global.CostUsd, 5);
        Assert.Equal(3, global.Calls);
        Assert.Equal(UsageTotals.Empty, tracker.Total("never-seen"));
    }

    [Fact]
    public void Reset_of_one_consumer_subtracts_from_the_global_total()
    {
        var tracker = new InMemoryUsageTracker();
        tracker.Record("a", new LlmUsage(10, 0, CostUsd: 0.10));
        tracker.Record("b", new LlmUsage(20, 0, CostUsd: 0.20));

        tracker.Reset("a");

        Assert.Equal(UsageTotals.Empty, tracker.Total("a"));
        Assert.Equal(0.20, tracker.Total("b").CostUsd, 5);   // b intact
        Assert.Equal(0.20, tracker.Total().CostUsd, 5);      // global reduced by a's share
    }

    // ---- decorator -----------------------------------------------------------------------------------

    private static (BudgetedLlmClient client, FakeLlmClient inner, IUsageTracker tracker) Budgeted(Action<BudgetOptions> tune)
    {
        var inner = new FakeLlmClient();
        var options = new LyntaiOptions();
        tune(options.Budget);
        var tracker = new InMemoryUsageTracker();
        return (new BudgetedLlmClient(inner, tracker, options), inner, tracker);
    }

    [Fact]
    public async Task Records_reply_usage_into_the_tracker()
    {
        var (client, inner, tracker) = Budgeted(_ => { });
        inner.Replies.Enqueue(Ok(0.05, tokens: 42));
        await client.CompleteAsync(Ask());
        Assert.Equal(0.05, tracker.Total().CostUsd, 5);
        Assert.Equal(42, tracker.Total().InputTokens);
    }

    [Fact]
    public async Task Refuses_the_next_call_once_the_global_cost_cap_is_reached()
    {
        var (client, inner, _) = Budgeted(b => b.MaxCostUsd = 0.10);
        inner.Replies.Enqueue(Ok(0.12)); // one call crosses the soft cap

        var first = await client.CompleteAsync(Ask());
        var second = await client.CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Ok, first.Verdict);       // the crossing call still ran
        Assert.Equal(LlmVerdict.Refused, second.Verdict); // the next is refused
        Assert.Contains("cost budget", second.Detail);
        Assert.Single(inner.Calls);                        // the provider was NOT hit for the refused call
    }

    [Fact]
    public async Task Refuses_once_the_global_token_cap_is_reached()
    {
        var (client, inner, _) = Budgeted(b => b.MaxTokens = 100);
        inner.Replies.Enqueue(new LlmReply("ok", LlmVerdict.Ok, new LlmUsage(80, 40))); // 120 > 100

        await client.CompleteAsync(Ask());
        var second = await client.CompleteAsync(Ask());

        Assert.Equal(LlmVerdict.Refused, second.Verdict);
        Assert.Contains("token budget", second.Detail);
    }

    [Fact]
    public async Task Per_consumer_cap_only_gates_that_consumer()
    {
        var (client, inner, _) = Budgeted(b => b.PerConsumer["greedy"] = new ConsumerBudget(MaxCostUsd: 0.10));
        inner.Replies.Enqueue(Ok(0.12));                  // greedy's first call crosses its cap
        inner.Replies.Enqueue(Ok(0.50));                  // thrifty's call

        await client.CompleteAsync(Ask("greedy"));
        var greedy = await client.CompleteAsync(Ask("greedy"));
        var thrifty = await client.CompleteAsync(Ask("thrifty"));

        Assert.Equal(LlmVerdict.Refused, greedy.Verdict);  // greedy is capped
        Assert.Equal(LlmVerdict.Ok, thrifty.Verdict);      // a different consumer is unaffected
    }

    [Fact]
    public async Task Streaming_refuses_over_budget_and_records_final_usage()
    {
        var (client, inner, tracker) = Budgeted(b => b.MaxCostUsd = 1.0);
        inner.StreamScript = _ => [LlmChunk.Content("hi"), LlmChunk.Final(new LlmUsage(5, 5, CostUsd: 0.25))];

        var chunks = new List<LlmChunk>();
        await foreach (var c in client.StreamAsync(Ask())) chunks.Add(c);
        Assert.Equal(0.25, tracker.Total().CostUsd, 5);   // usage recorded from the Final chunk
        Assert.DoesNotContain(chunks, c => c.Kind == LlmChunkKind.Error);

        tracker.Record("default", new LlmUsage(0, 0, CostUsd: 1.0)); // push over the cap
        var over = new List<LlmChunk>();
        await foreach (var c in client.StreamAsync(Ask())) over.Add(c);
        var only = Assert.Single(over);
        Assert.Equal(LlmChunkKind.Error, only.Kind);
        Assert.Equal(LlmVerdict.Refused, only.Verdict);
    }

    [Fact]
    public async Task SupportsToolCalls_delegates_to_the_inner_client()
    {
        var (client, inner, _) = Budgeted(_ => { });
        inner.SupportsToolCallsResult = true;
        Assert.True(client.SupportsToolCalls(Ask()));
    }

    // ---- DI + composition with the cache -------------------------------------------------------------

    [Fact]
    public async Task Budget_and_cache_compose_with_the_cache_outermost_so_hits_are_free()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(Ok(0.04)); // exactly one scripted reply; a real call costs 0.04
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddUsageBudget(o => o.MaxCostUsd = 1.0) // added first
            .AddResponseCache()                       // added second — but folds OUTERMOST by order
            .DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<ILlmClient>();
        // the always-on refusal screen is the outermost layer; the cache is the outermost GOVERNANCE
        // decorator inside it (proven behaviorally below: a hit reaches the provider 0 extra times and never
        // re-counts toward the budget)
        Assert.IsType<RefusalScreeningLlmClient>(client);

        var req = new LlmRequest { Messages = [LlmMessage.User("hi")] };
        await client.CompleteAsync(req); // miss → provider hit, cost recorded
        await client.CompleteAsync(req); // hit → served from cache, must NOT re-count toward the budget

        Assert.Single(provider.Calls);                                  // provider reached once
        Assert.Equal(0.04, sp.GetRequiredService<IUsageTracker>().Total().CostUsd, 5); // counted once, not twice
    }
}
