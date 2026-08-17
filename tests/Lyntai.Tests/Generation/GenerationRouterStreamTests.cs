using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The stream door, added in 3.0. Before it, <c>IGenerationStreamProvider</c> was a seam the
/// platform could not reach: the capability pre-filter was only ever asked about
/// <see cref="GenerationDelivery.Inline"/> and <see cref="GenerationDelivery.Job"/>, so a backend
/// advertising <see cref="GenerationDelivery.Stream"/> had to be driven directly and the contract shipped
/// unexercised.
///
/// <para>The invariants under test are NOT invented here — they are the two the LLM router measured
/// (<c>.claude/knowledge/llm-and-router.md</c> § Streaming), because falling back mid-stream duplicates
/// output in exactly the same way whether the bytes are tokens or audio. The third, the terminal-chunk
/// guarantee, is this door's own: a consumer's <c>await foreach</c> must never have to ask whether the loop
/// ended because the media finished or because the backend died.</para></summary>
public class GenerationRouterStreamTests
{
    private static GenerationRouter Router(params IGenerationProvider[] providers) => new(providers);

    private static GenerationRequest Speech() =>
        new() { Kind = GenerationKinds.Audio, Prompt = "read this aloud" };

    private static GenerationCandidate[] Candidates(params IGenerationProvider[] providers) =>
        [.. providers.Select(p => new GenerationCandidate(p.Id))];

    private static async Task<List<GenerationChunk>> Collect(IAsyncEnumerable<GenerationChunk> stream)
    {
        var chunks = new List<GenerationChunk>();
        await foreach (var chunk in stream) chunks.Add(chunk);
        return chunks;
    }

    /// <summary>Exactly one terminal chunk, and it is LAST — asserted on every path, because "the stream
    /// ended" and "the stream ended well" are the two things a raw enumerable cannot distinguish.</summary>
    private static GenerationChunk AssertOneTerminal(List<GenerationChunk> chunks)
    {
        var terminals = chunks.Where(c => c.Final || c.Error is not null).ToList();
        Assert.Single(terminals);
        Assert.Same(terminals[0], chunks[^1]);
        return terminals[0];
    }

    [Fact]
    public async Task It_streams_through_the_first_capable_candidate()
    {
        var tts = new FakeGenerationStreamProvider { Id = "tts" };

        var chunks = await Collect(Router(tts).StreamAsync(Candidates(tts), Speech()));

        var terminal = AssertOneTerminal(chunks);
        Assert.True(terminal.Final);
        Assert.Equal([1, 2, 3, 4], chunks.Where(c => c.Data is not null).SelectMany(c => c.Data!).ToArray());
        Assert.Equal(1.5, terminal.Usage?.Seconds);
    }

    [Fact]
    public async Task A_backend_that_is_not_stream_capable_is_never_offered_the_request()
    {
        // The capability pre-filter is the whole reason media routing is separate from LLM routing. An
        // inline-only image backend must not be asked to stream, whatever the candidate list says.
        var image = new FakeGenerationProvider { Id = "image" };
        var tts = new FakeGenerationStreamProvider { Id = "tts" };

        var chunks = await Collect(Router(image, tts).StreamAsync(Candidates(image, tts), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(0, image.GenerateCalls);
    }

    // ---- invariant 1: no fallback after commit -------------------------------------------------------

    [Fact]
    public async Task A_failure_BEFORE_any_data_falls_over_to_the_next_candidate()
    {
        var broken = new ScriptedStreamProvider
        {
            Id = "broken",
            Script = [GenerationChunk.Failure(GenerationVerdict.Failed, "backend fell over")],
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(broken, healthy).StreamAsync(Candidates(broken, healthy), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(1, healthy.StreamCalls);
    }

    [Fact]
    public async Task A_failure_AFTER_data_is_final_and_the_next_candidate_is_never_tried()
    {
        // THE INVARIANT. The caller already holds bytes this router cannot take back; a second backend would
        // splice two renders into one stream. The failure passes through and the stream ends.
        var half = new ScriptedStreamProvider
        {
            Id = "half",
            Script =
            [
                GenerationChunk.Content([1, 2], "audio/mpeg"),
                GenerationChunk.Failure(GenerationVerdict.Failed, "died mid-render"),
            ],
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(half, healthy).StreamAsync(Candidates(half, healthy), Speech()));

        var terminal = AssertOneTerminal(chunks);
        Assert.Equal(GenerationVerdict.Failed, terminal.Error);
        Assert.Equal("died mid-render", terminal.Detail);
        Assert.Equal(0, healthy.StreamCalls);
        Assert.Equal([1, 2], chunks[0].Data);
    }

    [Fact]
    public async Task A_THROW_after_data_is_also_final_rather_than_falling_over()
    {
        var half = new ScriptedStreamProvider
        {
            Id = "half",
            Script = [GenerationChunk.Content([1])],
            Throws = new InvalidOperationException("socket died mid-stream"),
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(half, healthy).StreamAsync(Candidates(half, healthy), Speech()));

        Assert.NotNull(AssertOneTerminal(chunks).Error);
        Assert.Equal(0, healthy.StreamCalls);
    }

    [Fact]
    public async Task A_connection_class_THROW_before_any_data_falls_over_like_any_other_pre_commit_failure()
    {
        // The submit door's NeverReachedTheBackend filter exists for billing ambiguity and is documented as
        // submit-only; on this door a refused connection before the first byte is the ordinary pre-commit
        // failure the contract says advances. Letting it propagate raw skips the healthy candidate entirely.
        var unreachable = new ScriptedStreamProvider
        {
            Id = "unreachable",
            Script = [],
            Throws = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused),
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(unreachable, healthy).StreamAsync(
            Candidates(unreachable, healthy), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(1, healthy.StreamCalls);
        Assert.Equal([9], chunks.Where(c => c.Data is not null).SelectMany(c => c.Data!).ToArray());
    }

    // ---- invariant 2: only real data commits ---------------------------------------------------------

    [Fact]
    public async Task A_metadata_only_first_chunk_does_NOT_commit_so_a_later_failure_still_falls_over()
    {
        // The router is the trust boundary. A backend may open with a MediaType announcement carrying no
        // bytes; treating that as commit would strand the caller on a backend that never produced anything.
        var announcer = new ScriptedStreamProvider
        {
            Id = "announcer",
            Script =
            [
                new GenerationChunk(MediaType: "audio/mpeg"),
                GenerationChunk.Failure(GenerationVerdict.Failed, "never got going"),
            ],
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(announcer, healthy).StreamAsync(
            Candidates(announcer, healthy), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(1, healthy.StreamCalls);
    }

    [Fact]
    public async Task An_EMPTY_data_chunk_does_not_commit_either()
    {
        var empty = new ScriptedStreamProvider
        {
            Id = "empty",
            Script = [GenerationChunk.Content([]), GenerationChunk.Failure(GenerationVerdict.Failed, "nothing")],
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(empty, healthy).StreamAsync(Candidates(empty, healthy), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(1, healthy.StreamCalls);
    }

    // ---- the terminal-chunk guarantee ----------------------------------------------------------------

    [Fact]
    public async Task A_stream_that_just_STOPS_after_data_is_closed_with_a_synthesized_completion()
    {
        var truncated = new ScriptedStreamProvider
        {
            Id = "truncated",
            Script = [GenerationChunk.Content([1, 2])],   // no Final, no Error — it simply ends
        };

        var chunks = await Collect(Router(truncated).StreamAsync(Candidates(truncated), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal([1, 2], chunks[0].Data);
    }

    [Fact]
    public async Task A_stream_that_produces_NOTHING_is_a_failure_and_falls_over()
    {
        var silent = new ScriptedStreamProvider { Id = "silent", Script = [] };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(silent, healthy).StreamAsync(Candidates(silent, healthy), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(1, healthy.StreamCalls);
    }

    [Fact]
    public async Task Every_candidate_failing_still_yields_exactly_one_terminal_chunk()
    {
        var a = new ScriptedStreamProvider { Id = "a", Script = [GenerationChunk.Failure(GenerationVerdict.Failed, "a died")] };
        var b = new ScriptedStreamProvider { Id = "b", Script = [GenerationChunk.Failure(GenerationVerdict.Failed, "b died")] };

        var chunks = await Collect(Router(a, b).StreamAsync(Candidates(a, b), Speech()));

        var terminal = AssertOneTerminal(chunks);
        Assert.Equal(GenerationVerdict.Failed, terminal.Error);
        Assert.Equal("a died", terminal.Detail);   // the FIRST substantive failure, as the inline door reports
    }

    [Fact]
    public async Task No_capable_candidate_yields_one_terminal_chunk_naming_the_delivery()
    {
        var image = new FakeGenerationProvider { Id = "image" };

        var chunks = await Collect(Router(image).StreamAsync(Candidates(image), Speech()));

        var terminal = AssertOneTerminal(chunks);
        Assert.Equal(GenerationVerdict.Unsupported, terminal.Error);
        Assert.Contains("Stream", terminal.Detail);
    }

    // ---- the trust boundary --------------------------------------------------------------------------

    [Fact]
    public async Task A_backend_advertising_Stream_without_implementing_the_seam_is_advanced_past()
    {
        // A BYO backend can ship this shape. Casting on trust would throw InvalidCastException out of a
        // contract whose whole point is "a verdict, never a throw".
        var liar = new LyingStreamProvider { Id = "liar" };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(liar, healthy).StreamAsync(Candidates(liar, healthy), Speech()));

        Assert.True(AssertOneTerminal(chunks).Final);
        Assert.Equal(1, healthy.StreamCalls);
    }

    [Fact]
    public async Task A_lying_backend_ALONE_reports_its_own_reason_rather_than_a_synthetic_one()
    {
        var liar = new LyingStreamProvider { Id = "liar" };

        var chunks = await Collect(Router(liar).StreamAsync(Candidates(liar), Speech()));

        var terminal = AssertOneTerminal(chunks);
        Assert.Equal(GenerationVerdict.Unsupported, terminal.Error);
        Assert.Contains("does not implement", terminal.Detail);
    }

    // ---- policy and cancellation ---------------------------------------------------------------------

    [Fact]
    public async Task A_content_refusal_SURFACES_rather_than_being_shopped_to_the_next_backend()
    {
        // Same rule the inline door follows: another backend is likely to refuse too, and quietly shopping a
        // refused prompt around is not the platform's call to make.
        var refuser = new ScriptedStreamProvider
        {
            Id = "refuser",
            Script = [GenerationChunk.Failure(GenerationVerdict.Refused, "content policy")],
        };
        var healthy = new ScriptedStreamProvider
        {
            Id = "healthy",
            Script = [GenerationChunk.Content([9]), GenerationChunk.Completed()],
        };

        var chunks = await Collect(Router(refuser, healthy).StreamAsync(
            Candidates(refuser, healthy), Speech()));

        Assert.Equal(GenerationVerdict.Refused, AssertOneTerminal(chunks).Error);
        Assert.Equal(0, healthy.StreamCalls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_becoming_a_backend_failure()
    {
        // A caller who gave up is not a failed backend, and must not cost the backend a dead-host strike.
        var tts = new ScriptedStreamProvider
        {
            Id = "tts",
            Script = [GenerationChunk.Content([1]), GenerationChunk.Content([2]), GenerationChunk.Completed()],
        };
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in Router(tts).StreamAsync(Candidates(tts), Speech(), cts.Token))
            {
                if (chunk.Data is not null) await cts.CancelAsync();
            }
        });
    }
}
