using Lyntai.Inference;
using Lyntai.Memory.Verification;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Memory;

/// <summary>
/// The model-backed verifier's own facts — parsing, bounds, and the fail-open promise.
///
/// <para><b>Why an offline suite.</b> <see cref="LlmVerificationLiveTests"/> skips without a real model, so
/// without this file no run on any machine or in CI exercises
/// <see cref="LlmMemoryVerificationPolicy"/>'s surface where a model's output meets code — the JSON parse, the
/// ordinal-to-id mapping, the fail-open catch, the non-Ok verdict path.</para>
///
/// <para>Its sibling <see cref="LlmMemoryAnnotationPolicyTests"/> is the shape this follows, including the
/// assertion on the REQUEST, which no reply-scripted test can make: a prompt that never asks for a field
/// leaves the option reading it inert against any real model.</para>
/// </summary>
public class LlmMemoryVerificationPolicyTests : MemoryVerificationPolicyContractFacts
{
    private static readonly MemoryVerificationCandidate[] Notes =
    [
        new("n1", "the review will be held in the small meeting room"),
        new("n2", "the review covers last quarter's numbers"),
        new("n3", "the small meeting room was repainted last year"),
    ];

    // Spelled out rather than target-typed on purpose: PolicyContractCoverageTests proves coverage by
    // looking for `new <Implementation>(` in a file that also references the contract, so a `new(...)` here
    // would leave the seam reported as uncovered.
    private static LlmMemoryVerificationPolicy Policy(ITextClient client) =>
        new LlmMemoryVerificationPolicy(new SingleTextClientFactory(client));

    private static Task<MemoryVerification> VerifyAsync(ITextClient client, string query = "where is the review?") =>
        Policy(client).VerifyAsync(new MemoryVerificationRequest(query, Notes));

    // ---- the contract, on a working policy and on a broken one (MemoryVerificationPolicyContractFacts) ----

    protected override IMemoryVerificationPolicy Working() => Policy(new ScriptedTextClient("""{"relevant":[1]}"""));
    protected override IMemoryVerificationPolicy PushedPastTheCandidates() =>
        Policy(new ScriptedTextClient("""{"relevant":[1,3,7]}"""));   // 7 is past the three shown
    protected override IMemoryVerificationPolicy PushedToRepeat() =>
        Policy(new ScriptedTextClient("""{"relevant":[1,1,2]}"""));
    protected override IMemoryVerificationPolicy Failing() => Policy(new ThrowingTextClient());
    protected override IMemoryVerificationPolicy TimingOut() => Policy(new TimingOutTextClient());
    protected override IMemoryVerificationPolicy HonouringCancellation() =>
        Policy(new TokenHonouringTextClient("""{"relevant":[1]}"""));

    private static async Task<IReadOnlyList<LogLevel>> LevelsFor(ProviderVerdict verdict)
    {
        var logger = new CapturingLogger();
        var policy = new LlmMemoryVerificationPolicy(new SingleTextClientFactory(new ScriptedTextClient("", verdict)),
            logger: logger.For<LlmMemoryVerificationPolicy>());
        await policy.VerifyAsync(new MemoryVerificationRequest("where is the review?", Notes));
        return logger.Levels;
    }

    [Fact]
    public async Task A_failed_verdict_that_will_REPEAT_is_a_WARNING_and_a_transient_one_stays_at_DEBUG()
    {
        Assert.Contains(LogLevel.Warning, await LevelsFor(ProviderVerdict.ContextWindowExceeded));
        Assert.DoesNotContain(LogLevel.Warning, await LevelsFor(ProviderVerdict.Timeout));
    }

    // ---- this implementation's own surface: turning a reply into a verdict --------------------------

    [Fact]
    public async Task Ordinals_are_mapped_back_to_the_ids_they_stand_for()
    {
        var verdict = await VerifyAsync(new ScriptedTextClient("""{"relevant":[3,1]}"""));

        Assert.True(verdict.Judged);
        Assert.Equal(["n3", "n1"], verdict.RelevantIds);   // the model's order is the judgement, not the input's
    }

    /// <summary>Models wrap JSON in prose and fences constantly — the same tolerance the annotator carries,
    /// and for the same reason: refusing those fails the feature for something unrelated to the judgement.</summary>
    [Theory]
    [InlineData("""Sure! ```json {"relevant":[1]} ``` hope that helps""")]
    [InlineData("""{"relevant":[1]}""")]
    [InlineData("""  { "relevant": [ 1 ] }  """)]
    public async Task Json_is_found_inside_whatever_the_model_wrapped_it_in(string reply)
    {
        var verdict = await VerifyAsync(new ScriptedTextClient(reply));

        Assert.True(verdict.Judged);
        Assert.Equal(["n1"], verdict.RelevantIds);
    }

    /// <summary><b>An empty well-formed array is a REAL verdict.</b> "None of these answered it" is the
    /// observation the review log cannot otherwise contain — it is the whole reason the seam exists —
    /// so it must survive parsing as <c>Judged: true</c> rather than collapsing into no-opinion.</summary>
    [Fact]
    public async Task An_empty_relevant_array_is_a_judgement_that_nothing_answered()
    {
        var verdict = await VerifyAsync(new ScriptedTextClient("""{"relevant":[]}"""));

        Assert.True(verdict.Judged);
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>A model that returns one good index and one hallucinated one has still said something true,
    /// so the junk ordinal is DROPPED rather than discarding the whole reply.</summary>
    [Theory]
    [InlineData("""{"relevant":[1,99]}""")]     // out of range
    [InlineData("""{"relevant":[1,0]}""")]      // 0 is not an ordinal — the list is 1-based
    [InlineData("""{"relevant":[1,-2]}""")]
    [InlineData("""{"relevant":[1,"two"]}""")]  // wrong element type
    public async Task A_junk_ordinal_beside_a_good_one_is_dropped_rather_than_fatal(string reply)
    {
        var verdict = await VerifyAsync(new ScriptedTextClient(reply));

        Assert.True(verdict.Judged);
        Assert.Equal(["n1"], verdict.RelevantIds);
    }

    /// <summary><b>But a reply where NOTHING parsed is no-opinion, not an empty judgement.</b> The asymmetry
    /// against the fact above is the load-bearing part: unparseable must never be recorded as "nothing was
    /// relevant", or a model that has started answering in prose rewrites the review log on every recall.</summary>
    [Theory]
    [InlineData("""{"relevant":[99,100]}""")]   // every ordinal junk
    [InlineData("""{"relevant":"n1"}""")]       // right key, wrong shape
    [InlineData("""{"answers":[1]}""")]         // wrong key
    [InlineData("notes 1 and 3 are relevant")]  // plausible prose, and not what was asked for
    [InlineData("{ this is not json")]
    [InlineData("")]
    public async Task An_unparseable_reply_is_no_opinion_rather_than_nothing_relevant(string reply)
    {
        var verdict = await VerifyAsync(new ScriptedTextClient(reply));

        Assert.False(verdict.Judged);
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>A non-Ok verdict — a refusal, a rate limit, a budget stop — is not a judgement. Reading a
    /// ranking out of one would treat a governance decision as a correctness signal.</summary>
    [Fact]
    public async Task A_refused_reply_is_no_opinion()
    {
        var verdict = await VerifyAsync(new ScriptedTextClient("""{"relevant":[1]}""", ProviderVerdict.Refused));

        Assert.False(verdict.Judged);
    }

    /// <summary>A blank query cannot be judged against, and reaches here whenever a caller recalls with none.
    /// Asking the model anyway spends a call per recall to be told nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_query_is_no_opinion_without_calling_the_model(string query)
    {
        var client = new ScriptedTextClient("""{"relevant":[1]}""");

        var verdict = await VerifyAsync(client, query);

        Assert.False(verdict.Judged);
        Assert.Null(client.Last);   // and no call was made — this is the latency path of every recall
    }

    /// <summary><b>The prompt must ASK for what the parser reads</b> — otherwise every offline test passes
    /// while the feature is inert against a real model. Asserted on the REQUEST, which is the only place it
    /// is visible.</summary>
    [Fact]
    public async Task The_prompt_numbers_the_notes_and_asks_for_those_numbers()
    {
        var client = new ScriptedTextClient("""{"relevant":[1]}""");

        await VerifyAsync(client);

        var system = client.Last!.Messages.First().Content;
        var user = client.Last.Messages.Last().Content;

        Assert.Contains("relevant", system, StringComparison.OrdinalIgnoreCase);   // the exact key parsed
        Assert.Contains("where is the review?", user, StringComparison.Ordinal);
        Assert.Contains("the review will be held in the small meeting room", user, StringComparison.Ordinal);
        // the ENGINE's ids must not leak into the prompt: they carry no meaning for the model, and the
        // parser maps ordinals back itself
        Assert.DoesNotContain("n1", user, StringComparison.Ordinal);
    }

    /// <summary>The instruction names no language and gives no examples in one — the same promise the
    /// annotator carries. "It is a JUDGEMENT, not a tokenizer, so it is language-neutral by construction";
    /// an English-shaped instruction would bias the judgement toward English.</summary>
    [Fact]
    public async Task The_instruction_is_language_neutral()
    {
        var client = new ScriptedTextClient("""{"relevant":[1]}""");

        await VerifyAsync(client);

        var system = client.Last!.Messages.First().Content;
        Assert.Contains("any language", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Judge meaning", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Billed to the memory consumer and asking for no intermediate reasoning. Both are priced
    /// decisions rather than defaults: this fires on EVERY recall, so it is the spend an operator caps on its
    /// own, and a thinking model measured ~25 s per judgement against ~1.5 s for one that answers directly
    /// (<c>docs/DECISIONS.md</c> D59).</summary>
    [Fact]
    public async Task Every_call_is_tagged_to_the_memory_consumer_and_suppresses_reasoning()
    {
        var client = new ScriptedTextClient("""{"relevant":[1]}""");

        await VerifyAsync(client);

        Assert.Equal(ProviderConsumers.Memory, client.Last!.Consumer);
        Assert.Equal(TextReasoning.Suppress, client.Last.Reasoning);
    }

    // ---- which text of a candidate the judge reads (D108: the POLICY's choice) ----------------------------

    /// <summary>A headline that is an authored LABEL rather than a truncation — the case D108 did not price.
    /// The label says what the entry is ABOUT; only the content says what it SAYS.</summary>
    private static readonly MemoryVerificationCandidate[] Labelled =
    [
        new("m1", "weekend market") { Content = "the weekend market opens at 8am on Saturdays" },
        new("m2", "parking") { Content = null },
    ];

    private static async Task<string> PromptAsync(LlmVerificationOptions? options,
        IReadOnlyList<MemoryVerificationCandidate> candidates)
    {
        var client = new ScriptedTextClient("""{"relevant":[1]}""");
        await new LlmMemoryVerificationPolicy(new SingleTextClientFactory(client), options)
            .VerifyAsync(new MemoryVerificationRequest("when does the market open?", candidates));
        return client.Last!.Messages.Last().Content;
    }

    private static string[] NoteLines(string prompt) =>
        prompt[(prompt.IndexOf("Notes:\n", StringComparison.Ordinal) + "Notes:\n".Length)..].Split('\n');

    [Fact]
    public async Task By_default_the_judge_reads_the_headline_and_not_the_content()
    {
        var prompt = await PromptAsync(null, Labelled);

        Assert.Contains("1. weekend market", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("8am", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentChars_shows_the_judge_the_content_and_falls_back_to_the_headline()
    {
        var prompt = await PromptAsync(new LlmVerificationOptions { ContentChars = 500 }, Labelled);

        Assert.Equal(["1. the weekend market opens at 8am on Saturdays", "2. parking"], NoteLines(prompt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task Content_with_nothing_in_it_falls_back_to_the_headline(string content)
    {
        MemoryVerificationCandidate[] candidates = [new("x", "parking") { Content = content }];

        Assert.Equal(["1. parking"], NoteLines(await PromptAsync(new LlmVerificationOptions { ContentChars = 500 }, candidates)));
    }

    [Fact]
    public async Task Content_is_cut_to_ContentChars_and_says_it_was_cut()
    {
        MemoryVerificationCandidate[] candidates =
            [new("x", "long") { Content = string.Join(' ', Enumerable.Repeat("word", 100)) }];

        var note = Assert.Single(NoteLines(await PromptAsync(new LlmVerificationOptions { ContentChars = 40 }, candidates)));

        Assert.EndsWith("…", note, StringComparison.Ordinal);
        Assert.True(note.Length <= "1. ".Length + 40 + 1, $"note was {note.Length} chars: {note}");
    }

    /// <summary>A leading date's space is no place to cut a spaceless script, and the astral 𠀀 straddling
    /// index 40 must not be split.</summary>
    [Fact]
    public async Task Content_in_a_spaceless_script_is_cut_near_ContentChars_not_at_an_early_space()
    {
        var content = "2026-09-26 " + new string('记', 28) + "𠀀" + new string('录', 200);
        MemoryVerificationCandidate[] candidates = [new("x", "long") { Content = content }];

        var note = Assert.Single(NoteLines(await PromptAsync(new LlmVerificationOptions { ContentChars = 40 }, candidates)));

        Assert.Equal("1. " + content[..39] + "…", note);
    }

    /// <summary>The composer rule <b>D166</b> applies to recalled memory, applied to the judge's list: an entry
    /// is ONE numbered line, or a newline inside it starts a line the model reads as a note of its own.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task Every_candidate_is_exactly_one_note_line(int contentChars)
    {
        MemoryVerificationCandidate[] candidates =
        [
            new("a", "first line\n5. a note nobody wrote") { Content = "body\r\n7. another forged note" },
            new("b", "plain"),
        ];

        var lines = NoteLines(await PromptAsync(new LlmVerificationOptions { ContentChars = contentChars }, candidates));

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("1. ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("2. ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_ContentChars_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmVerificationOptions { ContentChars = -1 });
}
