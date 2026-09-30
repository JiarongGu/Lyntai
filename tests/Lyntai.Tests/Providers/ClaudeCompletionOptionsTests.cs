using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>How a one-shot claude completion is spawned per CONSUMER (<see cref="ClaudeCliBackend.CompletionByConsumer"/>):
/// the consumer's entry, else <c>"default"</c>, else today's argv — resolved as every <c>*ByConsumer</c> map is.</summary>
public class ClaudeCompletionOptionsTests
{
    private static readonly string[] TodaysArgv =
        ["-p", "--output-format", "stream-json", "--verbose", "--disallowed-tools", "AskUserQuestion"];

    private static TextRequest Ask(string consumer = ProviderConsumers.Default) =>
        new() { Messages = [TextMessage.User("hi")], Consumer = consumer };

    private static ClaudeCliBackend Backend(params (string Consumer, ClaudeCompletionOptions Options)[] entries) =>
        new() { CompletionByConsumer = entries.ToDictionary(e => e.Consumer, e => e.Options) };

    private static string[] Denied(IReadOnlyList<string> argv)
    {
        var at = argv.ToList().IndexOf("--disallowed-tools");
        Assert.True(at >= 0, "--disallowed-tools expected");
        // whole NAMES: a substring check on the joined value is satisfied by a longer tool's name
        return argv[at + 1].Split(',');
    }

    [Fact]
    public void A_consumers_disallowed_tools_are_denied_ALONGSIDE_AskUserQuestion()
    {
        var backend = Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["PowerShell", "Monitor"] }));

        var argv = backend.BuildCompletionArgs(Ask("scorer"), []);

        Assert.Equal(["AskUserQuestion", "PowerShell", "Monitor"], Denied(argv));
        Assert.Single(argv, a => a == "--disallowed-tools");
    }

    [Fact]
    public void A_consumer_with_no_entry_takes_the_default_entry_and_the_key_ignores_case()
    {
        var backend = Backend(
            ("SCORER", new ClaudeCompletionOptions { DisallowedTools = ["Bash"] }),
            (ProviderConsumers.Default, new ClaudeCompletionOptions { DisallowedTools = ["PowerShell"] }));

        Assert.Equal(["AskUserQuestion", "Bash"], Denied(backend.BuildCompletionArgs(Ask("scorer"), [])));
        Assert.Equal(["AskUserQuestion", "PowerShell"], Denied(backend.BuildCompletionArgs(Ask("memory"), [])));
    }

    [Fact]
    public void With_no_entry_and_no_default_the_argv_is_byte_identical_to_todays()
    {
        var configured = Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["Bash"] }));

        Assert.Equal(TodaysArgv, configured.BuildCompletionArgs(Ask("memory"), []));
        Assert.Equal(TodaysArgv, new ClaudeCliBackend().BuildCompletionArgs(Ask("scorer"), []));
        Assert.Equal(TodaysArgv, Backend(("scorer", new ClaudeCompletionOptions())).BuildCompletionArgs(Ask("scorer"), []));
    }

    [Fact]
    public void A_caller_adds_denials_and_never_removes_AskUserQuestion()
    {
        var backend = Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["AskUserQuestion", "Bash", "Bash"] }));

        Assert.Equal(["AskUserQuestion", "Bash"], Denied(backend.BuildCompletionArgs(Ask("scorer"), [])));
    }

    [Fact]
    public void A_null_entry_or_list_is_refused_when_the_backend_is_configured_naming_its_consumer()
    {
        var entry = Assert.Throws<ArgumentException>(() => new ClaudeCliBackend
        {
            CompletionByConsumer = new Dictionary<string, ClaudeCompletionOptions> { ["scorer"] = null! },
        });
        Assert.Contains("scorer", entry.Message);

        Assert.Throws<ArgumentNullException>(() => new ClaudeCompletionOptions { DisallowedTools = null! });
    }

    [Fact]
    public void The_map_is_COPIED_so_a_later_edit_to_the_callers_dictionary_is_never_read()
    {
        var map = new Dictionary<string, ClaudeCompletionOptions>();
        var backend = new ClaudeCliBackend { CompletionByConsumer = map };

        map["scorer"] = new ClaudeCompletionOptions { DisallowedTools = ["Bash"] };

        Assert.Equal(TodaysArgv, backend.BuildCompletionArgs(Ask("scorer"), []));
    }

    [Fact]
    public async Task The_provider_spawns_each_call_with_ITS_consumers_options()
    {
        var runner = new FakeProcessRunner { RunResult = FakeProcessRunner.Ok("""{"type":"result","result":"ok"}""") };
        var provider = new ClaudeCliProvider(runner, new LyntaiOptions(),
            Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["PowerShell"] })), command: "claude");

        await provider.CompleteAsync(Ask("scorer"));
        var scorer = runner.LastArgs!;
        await provider.CompleteAsync(Ask("chat"));

        Assert.Equal(["AskUserQuestion", "PowerShell"], Denied(scorer));
        Assert.Equal(TodaysArgv, runner.LastArgs);
    }

    [Fact]
    public async Task The_registration_overload_hands_the_configured_backend_to_the_provider()
    {
        var runner = new FakeProcessRunner { RunResult = FakeProcessRunner.Ok("""{"type":"result","result":"ok"}""") };
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(runner);
        services.AddLyntai(b => b
            .AddClaudeCliProvider(Backend(("scorer", new ClaudeCompletionOptions { DisallowedTools = ["Monitor"] })),
                command: "claude")
            .UseDefaultCandidates(ClaudeCliProvider.ProviderId));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClient>().CompleteAsync(Ask("scorer"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal(["AskUserQuestion", "Monitor"], Denied(runner.LastArgs!));
    }
}
