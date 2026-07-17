using Lyntai.Llm;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Tests.Providers;

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
    public void Prompt_never_lands_in_argv()
    {
        const string prompt = "tell me a secret\nwith a newline & | metachars";
        var req = new LlmRequest { Messages = [LlmMessage.User(prompt)] };

        var args = ClaudeArgs.Build(req.Model);

        Assert.DoesNotContain(args, a => a.Contains("secret"));
        Assert.Equal(prompt, ClaudeArgs.BuildPrompt(req)); // it travels via stdin instead
    }

    [Fact]
    public void Multi_message_prompts_are_role_labeled()
    {
        var req = new LlmRequest
        {
            Messages = [LlmMessage.System("be brief"), LlmMessage.User("hi")],
        };

        var prompt = ClaudeArgs.BuildPrompt(req);

        Assert.Contains("[system]\nbe brief", prompt);
        Assert.Contains("[user]\nhi", prompt);
    }

    [Fact]
    public void Json_schema_appends_the_structured_output_instruction()
    {
        var req = new LlmRequest
        {
            Messages = [LlmMessage.User("score it")],
            JsonSchema = """{"type":"object"}""",
        };

        var prompt = ClaudeArgs.BuildPrompt(req);

        Assert.Contains("single JSON object", prompt);
        Assert.Contains("""{"type":"object"}""", prompt);
    }
}
