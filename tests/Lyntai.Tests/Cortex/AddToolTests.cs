using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Cortex;

/// <summary>The tool-loop wiring resolves out of <c>AddLyntai</c> with only a provider registered, and
/// <c>AddTool</c> feeds the registry through the DI collection (the variation point).</summary>
public class AddToolTests
{
    [Fact]
    public void Tool_loop_and_registry_resolve_with_no_tools_registered()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p")).DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IToolLoop>());
        Assert.Empty(sp.GetRequiredService<IToolRegistry>().Tools);
    }

    [Fact]
    public async Task AddTool_registers_tools_the_loop_can_call_end_to_end()
    {
        var provider = new FakeLlmProvider("p");
        provider.Replies.Enqueue(new LlmReply("""{"tool":"shout","arguments":{"s":"hi"}}""", LlmVerdict.Ok));
        provider.Replies.Enqueue(new LlmReply("""{"final":"HI"}""", LlmVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider)
            .AddTool(_ => new FunctionTool("shout", (args, _) => Task.FromResult(args.ToUpperInvariant())))
            .DefaultCandidates("p"));
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetRequiredService<IToolRegistry>().Tools, t => t.Name == "shout");

        var result = await sp.GetRequiredService<IToolLoop>()
            .RunAsync(new LlmRequest { Messages = [LlmMessage.User("shout hi")] });

        Assert.True(result.Ok);
        Assert.Equal("HI", result.Answer);
        Assert.Equal("shout", Assert.Single(result.Steps).Tool);
    }
}
