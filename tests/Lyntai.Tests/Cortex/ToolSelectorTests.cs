using Lyntai.Inference;
using Lyntai.Agents;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Cortex;

/// <summary>Bounding the tool roster BEFORE the model sees it.
///
/// <para>The measured problem (<c>docs/memory-measurements.md</c> §5): a 4B invokes a tool on 90-95% of
/// requests nothing on the roster serves, and two preamble rewrites in opposite directions moved that by
/// NOTHING — so wording is not the lever and narrowing the roster is what is left. The measured
/// feasibility: a model-free vector backend picks the right tool from 35 options 81.5% of the time.</para>
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

    private static TextRequest Ask(string prompt) => new() { Messages = [TextMessage.User(prompt)] };

    private static VectorToolSelector Selector(int limit) =>
        new([new FakeVectorProvider()], new ToolSelectorOptions { Limit = limit });

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
        var client = Answering();
        var selector = new ThrowingSelector();
        var loop = new ToolLoop(client, new ToolRegistry([Weather, Stocks, Recipes]),
            new LyntaiOptions(), logger: null, guards: null, selector: selector);

        var result = await loop.RunAsync(Ask("what is the weather"));

        Assert.True(result.Ok);
        Assert.Equal(3, selector.RosterSize);   // it was asked about the whole roster…
        var roster = RosterShown(client);        // …and the model was shown the whole roster
        Assert.Contains("stocks", roster, StringComparison.Ordinal);
        Assert.Contains("recipes", roster, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_loop_shows_the_model_ONLY_what_the_selector_chose()
    {
        var client = Answering();
        var loop = new ToolLoop(client, new ToolRegistry([Weather, Stocks, Recipes]),
            new LyntaiOptions(), logger: null, guards: null, selector: new FixedSelector(Stocks));

        await loop.RunAsync(Ask("what is the weather"));

        var roster = RosterShown(client);
        Assert.Contains("stocks", roster, StringComparison.Ordinal);
        Assert.DoesNotContain("recipes", roster, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_selector_that_chooses_NOTHING_leaves_the_roster_whole()
    {
        var client = Answering();
        var loop = new ToolLoop(client, new ToolRegistry([Weather, Stocks, Recipes]),
            new LyntaiOptions(), logger: null, guards: null, selector: new FixedSelector());

        await loop.RunAsync(Ask("what is the weather"));

        var roster = RosterShown(client);
        Assert.Contains("stocks", roster, StringComparison.Ordinal);
        Assert.Contains("recipes", roster, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddVectorToolSelector_narrows_the_loop_the_container_builds()
    {
        var chat = new FakeTextProvider("chat");
        chat.Replies.Enqueue(new TextResponse("""{"final":"done"}""", ProviderVerdict.Ok));
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => chat)
            .AddProvider(_ => new FakeVectorProvider(), FakeVectorProvider.Declared)
            .UseDefaultCandidates("chat")
            .AddTool(_ => Weather).AddTool(_ => Stocks).AddTool(_ => Recipes)
            .AddVectorToolSelector(o => o.Limit = 1));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<VectorToolSelector>(sp.GetRequiredService<IToolSelector>());
        await sp.GetRequiredService<IToolLoop>().RunAsync(Ask("what is the weather forecast rain today"));

        var roster = string.Join("\n", chat.Calls[0].Messages.Select(m => m.Content));
        Assert.DoesNotContain("stocks", roster, StringComparison.Ordinal);   // Limit = 1 reached the selector
        Assert.DoesNotContain("recipes", roster, StringComparison.Ordinal);
    }

    [Fact]
    public void A_selector_registered_BEFORE_AddVectorToolSelector_wins()
    {
        var mine = new FixedSelector(Stocks);
        var services = new ServiceCollection();
        services.AddSingleton<IToolSelector>(mine);
        services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("p")).AddVectorToolSelector());
        using var sp = services.BuildServiceProvider();

        Assert.Same(mine, sp.GetRequiredService<IToolSelector>());
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

    private static FakeTextClient Answering()
    {
        var client = new FakeTextClient();
        client.Replies.Enqueue(new TextResponse("""{"final":"done"}""", ProviderVerdict.Ok));
        return client;
    }

    /// <summary>Everything the model was shown on its first turn — the prompt protocol lists the roster in its
    /// system message.</summary>
    private static string RosterShown(FakeTextClient client) =>
        string.Join("\n", client.Calls[0].Messages.Select(m => m.Content));

    private sealed class ThrowingSelector : IToolSelector
    {
        public int RosterSize { get; private set; }

        public Task<IReadOnlyList<ITool>> SelectAsync(
            TextRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default)
        {
            RosterSize = tools.Count;
            throw new InvalidOperationException("selector is down");
        }
    }

    private sealed class FixedSelector(params ITool[] chosen) : IToolSelector
    {
        public Task<IReadOnlyList<ITool>> SelectAsync(
            TextRequest request, IReadOnlyList<ITool> tools, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ITool>>(chosen);
    }
}
