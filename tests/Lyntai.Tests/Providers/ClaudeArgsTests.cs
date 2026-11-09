using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The claude CLI's own argv vocabulary (<see cref="ClaudeArgs"/>) plus the SHARED prompt
/// flattening it relies on (<see cref="CliPrompt"/>, which every CLI dialect gets from
/// <see cref="CliBackendBase"/> — a CLI takes one blob of text, so nothing about that is
/// claude-specific).</summary>
public class ClaudeArgsTests
{
    [Fact]
    public void Argv_has_the_static_shape()
    {
        var args = ClaudeArgs.Build(model: null);

        Assert.Equal(["-p", "--output-format", "stream-json", "--verbose", "--disallowed-tools", "AskUserQuestion"], args);
    }

    [Fact]
    public void Model_is_appended_when_set()
    {
        var args = ClaudeArgs.Build("sonnet");

        Assert.Equal(["--model", "sonnet"], args.TakeLast(2));
    }

    [Fact]
    public async Task Prompt_never_lands_in_argv()
    {
        const string prompt = "tell me a secret\nwith a newline & | metachars";
        var runner = new FakeProcessRunner();
        var provider = new ClaudeCliProvider(runner, new LyntaiOptions(), command: "claude");

        await provider.CompleteAsync(new TextRequest { Messages = [TextMessage.User(prompt)] });

        Assert.DoesNotContain(runner.LastArgs!, a => a.Contains("secret", StringComparison.Ordinal));
        Assert.Equal(prompt, runner.LastStdin);   // it travels via stdin instead
    }

    [Fact]
    public void Multi_message_prompts_are_role_labeled()
    {
        var req = new TextRequest
        {
            Messages = [TextMessage.System("be brief"), TextMessage.User("hi")],
        };

        var prompt = CliPrompt.Flatten(req);

        Assert.Contains("[system]\nbe brief", prompt);
        Assert.Contains("[user]\nhi", prompt);
    }

    /// <summary>A replayed tool loop keeps what it said: the assistant turn lists its calls with their ids, and a
    /// result names the call it answers (the steward's ruling, <c>docs/DECISIONS.md</c> D337).</summary>
    [Fact]
    public void Tool_calls_and_results_are_rendered_with_their_ids()
    {
        var req = new TextRequest
        {
            Messages =
            [
                TextMessage.User("weather in Paris?"),
                TextMessage.AssistantToolCalls([new TextToolCall("call_1", "get_weather", """{"city":"Paris"}""")]),
                TextMessage.ToolResult("call_1", "12 C"),
            ],
        };

        var prompt = CliPrompt.Flatten(req);

        Assert.Contains("""[call call_1] get_weather {"city":"Paris"}""", prompt);
        Assert.Contains("[tool call_1]\n12 C", prompt);
        Assert.DoesNotContain("[assistant]\n\n\n", prompt);
    }

    [Fact]
    public void Json_schema_appends_the_structured_output_instruction()
    {
        var req = new TextRequest
        {
            Messages = [TextMessage.User("score it")],
            JsonSchema = """{"type":"object"}""",
        };

        var prompt = CliPrompt.Flatten(req);

        Assert.Contains("single JSON object", prompt);
        Assert.Contains("""{"type":"object"}""", prompt);
    }
}
