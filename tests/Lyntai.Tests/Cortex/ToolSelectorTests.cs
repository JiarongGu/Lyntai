using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

/// <summary>Bounding the tool roster BEFORE the model sees it.
///
/// <para>The measured problem (<c>docs/memory-measurements.md</c> §5): a 4B invokes a tool on 90-95% of
/// requests nothing on the roster serves, and two preamble rewrites in opposite directions moved that by
/// NOTHING — so wording is not the lever and narrowing the roster is what is left. The measured
/// feasibility: a model-free embedder picks the right tool from 35 options 81.5% of the time.</para>
///
/// <para><b>The failure that matters here is dropping the RIGHT tool</b>, so these pin the fail-open path
/// as hard as the happy one: a selector that throws, or asks for nothing, leaves the roster whole.</para>
/// </summary>
public class ToolSelectorTests
{
    private static FunctionTool Tool(string name, string description) =>
        new(name, (args, _) => Task.FromResult($"observed:{args}"), description);

    private static readonly FunctionTool Weather =
        Tool("weather", "report the weather forecast rain temperature for a city");

    private static readonly FunctionTool Stocks =
        Tool("stocks", "look up a stock market share price ticker quote");

    private static readonly FunctionTool Recipes =
        Tool("recipes", "find a cooking recipe ingredients oven bake dinner");

    private static LlmRequest Ask(string prompt) => new() { Messages = [LlmMessage.User(prompt)] };

    private static EmbeddingToolSelector Selector(int limit) =>
        new(new FakeEmbedder(), new ToolSelectorOptions { Limit = limit });

    [Fact]
    public async Task Narrows_the_roster_to_the_LIMIT_keeping_what_the_request_is_about()
    {
        var selected = await Selector(1).SelectAsync(
            Ask("what is the weather forecast rain today"), [Weather, Stocks, Recipes]);

        Assert.Equal(["weather"], selected.Select(t => t.Name));
    }

    [Fact]
    public async Task A_roster_already_within_the_limit_is_returned_WHOLE_and_in_its_original_order()
    {
        // Order is not incidental: it is the order the protocol's system prompt lists the tools in, and
        // reordering a roster that needed no narrowing would silently change what the prompt says.
        var selected = await Selector(5).SelectAsync(Ask("anything"), [Weather, Stocks, Recipes]);

        Assert.Equal(["weather", "stocks", "recipes"], selected.Select(t => t.Name));
    }

    [Fact]
    public async Task A_selector_that_THROWS_leaves_the_roster_whole_rather_than_dropping_the_answer()
    {
        // Fail-open, like every other model-backed seam here. A selector is an optimisation; a broken one
        // must cost tokens, never the tool the request actually needed.
        var loop = new ToolLoop(Answering(), new ToolRegistry([Weather, Stocks, Recipes]),
            new LyntaiOptions(), logger: null, guards: null, selector: new ThrowingSelector());

        var result = await loop.RunAsync(Ask("what is the weather"));

        Assert.True(result.Ok);
        Assert.Equal(3, ThrowingSelector.LastRosterSize);
    }

    [Fact]
    public async Task A_limit_of_zero_or_less_narrows_NOTHING_so_a_misconfiguration_cannot_blind_the_loop()
    {
        foreach (var limit in new[] { 0, -1 })
        {
            var selected = await Selector(limit).SelectAsync(Ask("weather"), [Weather, Stocks, Recipes]);
            Assert.Equal(3, selected.Count);
        }
    }

    [Fact]
    public async Task With_NO_selector_registered_the_loop_sees_every_tool_exactly_as_before()
    {
        var client = Answering();
        var loop = new ToolLoop(client, new ToolRegistry([Weather, Stocks, Recipes]), new LyntaiOptions());

        await loop.RunAsync(Ask("what is the weather"));

        // the prompt protocol lists the roster in its system message, so all three names must appear
        var prompt = string.Join("\n", client.Calls[0].Messages.Select(m => m.Content));
        Assert.Contains("weather", prompt, StringComparison.Ordinal);
        Assert.Contains("stocks", prompt, StringComparison.Ordinal);
        Assert.Contains("recipes", prompt, StringComparison.Ordinal);
    }

    private static FakeLlmClient Answering()
    {
        var client = new FakeLlmClient();
        client.Replies.Enqueue(new LlmReply("""{"final":"done"}""", LlmVerdict.Ok));
        return client;
    }

    private sealed class ThrowingSelector : IToolSelector
    {
        internal static int LastRosterSize { get; private set; }

        public Task<IReadOnlyList<ITool>> SelectAsync(
            LlmRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default)
        {
            LastRosterSize = tools.Count;
            throw new InvalidOperationException("selector is down");
        }
    }
}
