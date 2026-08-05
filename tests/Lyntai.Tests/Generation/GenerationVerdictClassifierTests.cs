using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>Media keeps its OWN verdict vocabulary, but NOT its own corpus of "what does this failure mean" —
/// that would be a second set of regexes to drift (the mistake <c>docs/DECISIONS.md</c> D27 exists to
/// prevent). The classifier maps transport/text failures through Core's shared classifier and translates the
/// answer.</summary>
// serialized with every other class that REGISTERS one: LlmVerdictClassifier.AddErrorTextMatcher mutates a
// PROCESS-WIDE list, so two registrants running in parallel would see each other's matchers. It does NOT
// protect the rest of the suite — every other collection keeps running and every FromErrorText call in it
// reads the same list — so a matcher registered here must answer for its OWN probe text and nothing else.
[Collection("verdict-matchers")]
public class GenerationVerdictClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, GenerationVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, GenerationVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.InternalServerError, GenerationVerdict.Failed)]
    public void An_http_failure_maps_to_a_media_verdict(HttpStatusCode status, GenerationVerdict expected)
    {
        Assert.Equal(expected, GenerationVerdictClassifier.FromHttpFailure(status, body: null));
    }

    [Fact]
    public void A_content_policy_refusal_surfaces_as_refused()
    {
        // image backends refuse prompts; shopping a refused prompt around backends is not the platform's call
        var verdict = GenerationVerdictClassifier.FromErrorText("Your request was rejected by our content policy");

        Assert.Equal(GenerationVerdict.Refused, verdict);
    }

    [Fact]
    public void A_rate_limit_phrased_in_prose_is_recognized()
    {
        Assert.Equal(GenerationVerdict.RateLimited, GenerationVerdictClassifier.FromErrorText("quota exceeded, try later"));
    }

    [Fact]
    public void An_unrecognized_failure_is_plain_Failed()
    {
        Assert.Equal(GenerationVerdict.Failed, GenerationVerdictClassifier.FromErrorText("something odd happened"));
    }

    [Fact]
    public void A_context_window_failure_lands_on_the_nearest_media_MEANING_a_capability_gap()
    {
        // the ONE member with no media counterpart. It collapsed to Failed until GenerationRouter learned to
        // report a blameless reason when nothing substantive failed (TASKS Part 40): "too long for THIS
        // backend" is a capability gap, and as Failed it took PenalizeAndAdvance, so repeated oversized
        // prompts benched a perfectly healthy backend. Reportability is what kept it there, and the router
        // now supplies it — which is why the order of those two changes was load-bearing (DECISIONS D43).
        var verdict = GenerationVerdictClassifier.FromErrorText("maximum context length exceeded");

        Assert.Equal(GenerationVerdict.Unsupported, verdict);
        Assert.Equal(GenerationFallbackAction.Advance, new GenerationRoutingPolicy().ActionFor(verdict));
    }

    [Fact]
    public void A_NotConfigured_verdict_from_the_shared_corpus_stays_NotConfigured()
    {
        // both domains now have this verdict and it means the same thing in each. Flattening it to Failed
        // would convert a blameless skip into a penalised failure on the way across the boundary.
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("no endpoint configured", StringComparison.OrdinalIgnoreCase)
                ? LlmVerdict.NotConfigured
                : null);

        Assert.Equal(GenerationVerdict.NotConfigured,
            GenerationVerdictClassifier.FromErrorText("no endpoint configured"));
    }

    [Fact]
    public void A_cancellation_style_exception_is_a_timeout()
    {
        Assert.Equal(GenerationVerdict.Timeout, GenerationVerdictClassifier.FromException(new OperationCanceledException()));
    }

    [Fact]
    public void An_Unsupported_verdict_from_the_shared_corpus_stays_Unsupported()
    {
        // a capability gap is not a fault. Flattening it to Failed on the way across the boundary turns the
        // policy's Advance into PenalizeAndAdvance, so repeated gaps bench a perfectly healthy backend.
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("this model cannot take an image input", StringComparison.OrdinalIgnoreCase)
                ? LlmVerdict.Unsupported
                : null);

        var verdict = GenerationVerdictClassifier.FromErrorText("this model cannot take an image input");

        Assert.Equal(GenerationVerdict.Unsupported, verdict);
        // the mapping only matters because of what routing does with it
        Assert.Equal(GenerationFallbackAction.Advance, new GenerationRoutingPolicy().ActionFor(verdict));
    }

    /// <summary>The mapping's POINT, end to end: a translated capability gap must not count toward the
    /// dead-host threshold. Asserted through the router rather than the policy table alone, because a
    /// translation that did not change the routing outcome would not have been worth changing.</summary>
    [Fact]
    public async Task A_translated_capability_gap_does_not_bench_a_healthy_backend()
    {
        using var _ = LlmVerdictClassifier.AddErrorTextMatcher(t =>
            t.Contains("cannot take an image input", StringComparison.OrdinalIgnoreCase)
                ? LlmVerdict.Unsupported
                : null);

        // what the backend would report: its own error text, classified through the shared corpus
        var translated = GenerationVerdictClassifier.FromErrorText("cannot take an image input");

        // one penalised failure is enough to bench, so the second run is the whole assertion
        var deadHosts = new DeadHostTracker(threshold: 1);
        var gap = new FakeGenerationProvider { Id = "a" };
        gap.Verdicts.Enqueue(translated);
        var working = new FakeGenerationProvider { Id = "b" };
        var router = new GenerationRouter([gap, working], deadHosts: deadHosts);
        GenerationCandidate[] candidates = [new("a"), new("b")];
        var request = new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" };

        var first = await router.GenerateAsync(candidates, request);
        var second = await router.GenerateAsync(candidates, request);

        Assert.True(first.IsOk);
        Assert.True(second.IsOk);
        Assert.Equal(2, gap.GenerateCalls);   // still in rotation: a capability gap is not evidence of ill health
    }

    /// <summary>The translation table's growth gate, and the table itself. C# cannot make a switch over an
    /// enum exhaustive — <c>(LlmVerdict)99</c> is a legal value, so a discard arm is mandatory and would
    /// silently swallow a newly added member, which is exactly how <see cref="LlmVerdict.Unsupported"/> came
    /// to be reported as <see cref="GenerationVerdict.Failed"/>. So the gate is this test.
    /// <para>It demands TWO things, because demanding only the first leaves a hole: a <b>row</b> here (which
    /// cannot be written without naming a media verdict — that is the decision), and an <b>arm</b> in
    /// <c>TryTranslate</c>. Without the second, a new member registered as <c>Failed</c> would pass on the
    /// discard's answer alone and the "the discard holds nothing but undefined values" invariant would be
    /// unguarded — the same silence this test exists to break. <c>TryTranslate</c> returns null for an
    /// unhandled member precisely so the two are distinguishable; the public path cannot tell them apart,
    /// which is why the gate reaches through <c>InternalsVisibleTo</c> for that half.</para>
    /// The same obligation <c>LlmVerdictExtensionsTests.Every_verdict_states_whether_it_is_transient</c>
    /// places on the call-site helpers and D38 places on the routing policy.</summary>
    [Fact]
    public void Every_llm_verdict_states_its_media_translation()
    {
        var expected = new Dictionary<LlmVerdict, GenerationVerdict>
        {
            [LlmVerdict.Ok] = GenerationVerdict.Ok,
            [LlmVerdict.RateLimited] = GenerationVerdict.RateLimited,
            [LlmVerdict.AuthFailed] = GenerationVerdict.AuthFailed,
            [LlmVerdict.Refused] = GenerationVerdict.Refused,
            [LlmVerdict.Timeout] = GenerationVerdict.Timeout,
            [LlmVerdict.NotConfigured] = GenerationVerdict.NotConfigured,  // blameless in both domains
            [LlmVerdict.Unsupported] = GenerationVerdict.Unsupported,      // ditto — a capability gap
            [LlmVerdict.Failed] = GenerationVerdict.Failed,
            // the only member with no media counterpart; the nearest MEANING is a capability gap, which the
            // router can now report as well as advance past blamelessly (DECISIONS D43 → TASKS Part 40)
            [LlmVerdict.ContextWindowExceeded] = GenerationVerdict.Unsupported,
        };

        Assert.Equal(Enum.GetValues<LlmVerdict>().OrderBy(v => v), expected.Keys.OrderBy(v => v));

        // the registered list is process-wide and the rest of the suite is NOT serialized against it, so
        // every matcher below answers for this probe text ALONE — a catch-all would hand its verdict to any
        // concurrently-running test that happens to classify an error body
        var probe = $"probe-{Guid.NewGuid():N}";

        foreach (var (llm, media) in expected)
        {
            // an ARM, not just a row: null here means the member fell through to the discard, which would
            // otherwise be indistinguishable from a deliberate mapping to Failed
            Assert.NotNull(GenerationVerdictClassifier.TryTranslate(llm));

            // and the whole way through the public path, since that is what a consumer actually calls
            using var scope = LlmVerdictClassifier.AddErrorTextMatcher(t => t == probe ? llm : null);
            Assert.Equal(media, GenerationVerdictClassifier.FromErrorText(probe));
        }

        // a value no build knows has no arm — and the public path still answers it conservatively rather
        // than throwing, because a classifier that can throw is worse than one that guesses Failed
        Assert.Null(GenerationVerdictClassifier.TryTranslate((LlmVerdict)9999));
        using var undefined = LlmVerdictClassifier.AddErrorTextMatcher(t => t == probe ? (LlmVerdict)9999 : null);
        Assert.Equal(GenerationVerdict.Failed, GenerationVerdictClassifier.FromErrorText(probe));
    }
}
