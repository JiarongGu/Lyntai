using Lyntai.Inference;
using Lyntai;
using Lyntai.Inference.Cli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The two invariants the STREAMED engine path shares with the buffered one: an absolute wall-clock
/// backstop underneath the inactivity window, and the router's commit gate
/// (<c>Kind == Content &amp;&amp; Text.Length &gt; 0</c>) deciding what counts as delivered content.
/// Neither is visible at compile time: without them a chatty child that never finishes streams forever, and
/// one zero-content event marks a stream answered.
///
/// The rest of the generic engine contract lives in <see cref="CliProviderEngineTests"/>; the long-running
/// agent sessions are deliberately NOT covered here, because they drive the process runner directly and must
/// keep no wall clock at all.</summary>
public class CliProviderEngineStreamBoundsTests
{
    private static CliProviderEngine Engine(FakeProcessRunner runner, LyntaiOptions? options = null) =>
        new(new FakeCliBackend(), runner, options ?? new LyntaiOptions(), command: "fakecli");

    private static TextRequest Ask(string prompt = "hello", string consumer = "default") =>
        new() { Messages = [TextMessage.User(prompt)], Consumer = consumer };

    /// <summary>Drain a stream under a HARD budget, so a regression arrives as a failed assertion rather
    /// than as a hung <c>verify</c> run (`pitfalls.md`: a test that hangs on the failure it detects is worse
    /// than no test — the next person bisects the harness instead of reading the failure).</summary>
    private static async Task<List<TextChunk>> DrainAsync(CliProviderEngine engine, TextRequest req)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var chunks = new List<TextChunk>();
        await foreach (var chunk in engine.StreamAsync(req, cts.Token))
            chunks.Add(chunk);
        Assert.False(cts.IsCancellationRequested, "the stream did not finish within the test's budget");
        return chunks;
    }

    // ── the absolute backstop ────────────────────────────────────────────────

    [Fact]
    public async Task A_streamed_completion_carries_the_absolute_backstop_and_not_just_the_inactivity_window()
    {
        // `inactivityTimeout` alone cannot see the failure a backstop exists for: a child that prints often
        // enough to re-arm the window but never finishes re-arms it forever, leaving the call no upper bound.
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

    // ── the commit gate ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_EMPTY_content_event_is_not_delivered_content_so_the_stream_ends_in_an_error()
    {
        // Content counts by LENGTH, not by EVENT: a zero-content event that marked the stream answered would
        // end it `Final` (a successful EMPTY answer the router never falls over from), and through TextRouter
        // a zero-content FIRST chunk would commit the stream and disable fallback outright. The dialect's
        // "text:" line parses to Content("").
        var runner = new FakeProcessRunner(["text:"]);

        var chunks = await DrainAsync(Engine(runner), Ask());

        Assert.DoesNotContain(chunks, c => c.Kind == TextChunkKind.Content);
        Assert.Equal(TextChunkKind.Error, Assert.Single(chunks).Kind);
        Assert.Equal(ProviderVerdict.Failed, chunks[^1].Verdict);
    }

    [Fact]
    public async Task An_EMPTY_content_event_does_not_swallow_the_answer_reported_on_the_result_line()
    {
        // The sharper half: "did anything arrive?" also gates the result-only delivery, so counting ONE empty
        // content event would drop the entire answer of a stream that reports its text on the terminal result
        // line — Content("") then Final, with the answer nowhere.
        var runner = new FakeProcessRunner(["text:", "result:the answer"]);

        var chunks = await DrainAsync(Engine(runner), Ask());

        Assert.Equal(["the answer"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task Content_that_is_only_WHITESPACE_still_counts_as_delivered()
    {
        // The guard against over-correcting: the gate is LENGTH, not IsNullOrWhiteSpace. A space between two
        // tokens is real output a model emitted, and treating it as nothing would corrupt the answer of any
        // backend that chunks on token boundaries.
        var runner = new FakeProcessRunner(["text: "]);

        var chunks = await DrainAsync(Engine(runner), Ask());

        Assert.Equal([" "], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }
}
