using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Cli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The two invariants the STREAMED engine path shares with the buffered one and used to miss: an
/// absolute wall-clock backstop underneath the inactivity window, and the router's commit gate
/// (<c>Kind == Content &amp;&amp; Text.Length &gt; 0</c>) deciding what counts as delivered content.
///
/// Both were shipped defects a consumer could not see at compile time — a chatty child that never finished
/// streamed forever, and one zero-content event marked a stream answered — so each is pinned by a test that
/// FAILS rather than one that would have to be re-derived from the code.
///
/// The rest of the generic engine contract lives in <see cref="CliProviderEngineTests"/>; the long-running
/// agent sessions are deliberately NOT covered here, because they drive the process runner directly and must
/// keep no wall clock at all.</summary>
public class CliProviderEngineStreamBoundsTests
{
    private static CliProviderEngine Engine(FakeProcessRunner runner, LyntaiOptions? options = null) =>
        new(new FakeCliDialect(), runner, options ?? new LyntaiOptions(), command: "fakecli");

    private static LlmRequest Ask(string prompt = "hello", string consumer = "default") =>
        new() { Messages = [LlmMessage.User(prompt)], Consumer = consumer };

    /// <summary>Drain a stream under a HARD budget, so a regression arrives as a failed assertion rather
    /// than as a hung <c>verify</c> run (`pitfalls.md`: a test that hangs on the failure it detects is worse
    /// than no test — the next person bisects the harness instead of reading the failure).</summary>
    private static async Task<List<LlmChunk>> DrainAsync(CliProviderEngine engine, LlmRequest req)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var chunks = new List<LlmChunk>();
        await foreach (var chunk in engine.StreamAsync(req, cts.Token))
            chunks.Add(chunk);
        Assert.False(cts.IsCancellationRequested, "the stream did not finish within the test's budget");
        return chunks;
    }

    // ── the absolute backstop (CLI-STREAM-CEILING) ───────────────────────────

    [Fact]
    public async Task A_streamed_completion_carries_the_absolute_backstop_and_not_just_the_inactivity_window()
    {
        // Regression: the streamed path passed `inactivityTimeout` ONLY. Inactivity alone cannot see the
        // failure a backstop exists for — a child that prints often enough to re-arm the window but never
        // finishes re-arms it forever, so the call had no upper bound at all.
        var options = new LyntaiOptions
        {
            ProviderTimeout = TimeSpan.FromMinutes(2),
            MaxProviderTimeout = TimeSpan.FromMinutes(30),
        };
        var runner = new FakeProcessRunner(["text:hi", "result:hi"]);

        await DrainAsync(Engine(runner, options), Ask());

        Assert.Equal(TimeSpan.FromMinutes(2), runner.LastInactivityTimeout);
        Assert.Equal(TimeSpan.FromMinutes(30), runner.LastMaxDuration);
    }

    [Fact]
    public async Task The_streamed_backstop_never_falls_BELOW_the_inactivity_window()
    {
        // An app-configured consumer budget is trusted, never clamped (LyntaiOptions.ResolveTimeout), so a
        // budget above the ceiling must RAISE the backstop rather than the reverse: a ceiling underneath the
        // window would kill every call of that consumer at the ceiling instead of bounding a runaway one.
        // Same arithmetic as CompleteAsync, deliberately — one rule, both paths.
        var options = new LyntaiOptions { MaxProviderTimeout = TimeSpan.FromMinutes(30) };
        options.TimeoutByConsumer["long-agent"] = TimeSpan.FromMinutes(45);
        var runner = new FakeProcessRunner(["result:hi"]);

        await DrainAsync(Engine(runner, options), Ask(consumer: "long-agent"));

        Assert.Equal(TimeSpan.FromMinutes(45), runner.LastInactivityTimeout);
        Assert.Equal(TimeSpan.FromMinutes(45), runner.LastMaxDuration);
    }

    // ── the commit gate (CLI-EMPTY-CONTENT) ──────────────────────────────────

    [Fact]
    public async Task An_EMPTY_content_event_is_not_delivered_content_so_the_stream_ends_in_an_error()
    {
        // Regression: the engine counted Content EVENTS rather than their length, so a zero-content event
        // marked the stream answered — it ended `Final` (a successful EMPTY answer the router would never
        // fall over from), and through LlmRouter a zero-content FIRST chunk committed the stream and
        // disabled fallback outright. The dialect's "text:" line parses to Content("").
        var runner = new FakeProcessRunner(["text:"]);

        var chunks = await DrainAsync(Engine(runner), Ask());

        Assert.DoesNotContain(chunks, c => c.Kind == LlmChunkKind.Content);
        Assert.Equal(LlmChunkKind.Error, Assert.Single(chunks).Kind);
        Assert.Equal(LlmVerdict.Failed, chunks[^1].Verdict);
    }

    [Fact]
    public async Task An_EMPTY_content_event_does_not_swallow_the_answer_reported_on_the_result_line()
    {
        // The sharper half of the same bug: "did anything arrive?" also gates the result-only delivery, so
        // ONE empty content event dropped the entire answer of a stream that reports its text on the
        // terminal result line — the consumer got Content("") then Final, with the answer nowhere.
        var runner = new FakeProcessRunner(["text:", "result:the answer"]);

        var chunks = await DrainAsync(Engine(runner), Ask());

        Assert.Equal(["the answer"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task Content_that_is_only_WHITESPACE_still_counts_as_delivered()
    {
        // The guard against over-fixing: the gate is LENGTH, not IsNullOrWhiteSpace. A space between two
        // tokens is real output a model emitted, and treating it as nothing would corrupt the answer of any
        // backend that chunks on token boundaries.
        var runner = new FakeProcessRunner(["text: "]);

        var chunks = await DrainAsync(Engine(runner), Ask());

        Assert.Equal([" "], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
    }
}
