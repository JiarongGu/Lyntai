using Lyntai.Llm;
using Lyntai.Providers.Local;

namespace Lyntai.Tests.Providers;

/// <summary>
/// OPT-IN live integration against a real local GGUF model via LLamaSharp — proves end-to-end
/// in-process inference, not just wiring. Runs only when <c>LYNTAI_LIVE_LLAMA</c> is set AND
/// <c>LYNTAI_LLAMA_MODEL</c> points at an existing .gguf file (and the test host has an
/// <c>LLamaSharp.Backend.*</c> available); otherwise it returns early (a no-op pass) so the default
/// run stays fast, deterministic, and native-dependency-free. xUnit v2 has no dynamic
/// <c>Assert.Skip</c>, hence the early-return.
///
/// Enable:  set LYNTAI_LIVE_LLAMA=1  and  LYNTAI_LLAMA_MODEL=&lt;path-to&gt;.gguf
/// (with an LLamaSharp.Backend.* referenced by / visible to the test host).
/// </summary>
public class LocalProviderLiveTests
{
    private static string? ModelPath => Environment.GetEnvironmentVariable("LYNTAI_LLAMA_MODEL");

    private static bool Live =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_LLAMA"))
        && !string.IsNullOrEmpty(ModelPath) && File.Exists(ModelPath);

    private static LocalProvider Provider() => new("local",
        new LocalModelOptions { ModelPath = ModelPath!, ContextSize = 2048, MaxTokens = 48 },
        new LyntaiOptions { ProviderTimeout = TimeSpan.FromMinutes(5) }); // CPU generation can be slow

    private static LlmRequest Ask(string prompt) => new()
    {
        Messages = [LlmMessage.User(prompt)],
        MaxTokens = 48,
        Temperature = 0,
    };

    [Fact]
    public async Task Completion_against_a_real_local_model_returns_ok_with_text()
    {
        if (!Live) return;

        using var provider = Provider();
        var reply = await provider.CompleteAsync(Ask("Reply with exactly one word: pong"));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.NotEqual("", reply.Text);
    }

    [Fact]
    public async Task Streaming_against_a_real_local_model_yields_content_then_final()
    {
        if (!Live) return;

        using var provider = Provider();
        var chunks = new List<LlmChunk>();
        await foreach (var c in provider.StreamAsync(Ask("List three colors, comma separated.")))
            chunks.Add(c);

        Assert.Contains(chunks, c => c.Kind == LlmChunkKind.Content && c.Text.Length > 0);
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
    }
}
