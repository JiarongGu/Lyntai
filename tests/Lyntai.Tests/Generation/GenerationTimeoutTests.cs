using System.Text.Json;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Generation.Routing;
using Lyntai.Generation.Tools;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The per-call deadline the HTTP generation backends were missing (GEN11). The shims configure their
/// named client with <see cref="Timeout.InfiniteTimeSpan"/> on the grounds that "the per-call deadline owns
/// cancellation" — so a deadline has to exist, or a backend that accepts the connection and then stalls hangs
/// until the caller's token fires, and a background render with no cancel waits forever.
///
/// <para>Two things are pinned here beyond "it stops": a fired deadline is a <see cref="GenerationVerdict.Timeout"/>
/// RESULT (these backends are contractually fail-safe — a transport failure is a verdict, not a throw), while the
/// CALLER's own cancellation still propagates as an <see cref="OperationCanceledException"/>. A naive linked-token
/// implementation reports one as the other.</para></summary>
public class GenerationTimeoutTests
{
    /// <summary>A backend that accepts the connection and then never answers — the exact failure the deadline
    /// exists for. It honours the token it is given, so only a clock can end the call.</summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>A short BACKSTOP on the fixture client, so a regression in this area fails rather than
    /// stalls. Left at <see cref="HttpClient"/>'s 100-second default, a test whose own clock stopped working
    /// would still pass — a hundred times slower — and a suite that hangs teaches nothing. Every deadline
    /// asserted below is well under this, so the backstop is never the clock that fires; when it is, the
    /// <c>Detail</c> assertions name it (<c>GenerationDeadline</c> reports which clock won).</summary>
    private static readonly TimeSpan Backstop = TimeSpan.FromSeconds(5);

    private static Func<HttpClient> Stalling() => () => new HttpClient(new StallingHandler()) { Timeout = Backstop };

    private static GenerationRequest Ask(int? timeoutSeconds = null) =>
        new() { Kind = GenerationKinds.Image, Prompt = "a red square", TimeoutSeconds = timeoutSeconds };

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    // a budget no test can reach, so a test that ends did so for the reason it names — and if one ever does
    // reach it, Stalling()'s Backstop ends the call in seconds rather than letting the suite hang
    private static readonly TimeSpan Unreachable = TimeSpan.FromMinutes(30);

    // ---- there IS a bounded default (the bug: none at all) ----

    [Fact]
    public void Every_http_backend_defaults_to_a_generous_but_FINITE_budget()
    {
        // "no deadline" is the defect; an infinite default would reintroduce it silently. Generous because a
        // render legitimately runs for minutes — the 100s HttpClient default is what the shims rightly dropped.
        foreach (var (name, budget) in ((string, TimeSpan)[])
        [
            (nameof(OpenAiImageOptions), new OpenAiImageOptions { BaseUrl = "x" }.Timeout),
            (nameof(Automatic1111Options), new Automatic1111Options { BaseUrl = "x" }.Timeout),
            (nameof(ComfyUiOptions), new ComfyUiOptions { BaseUrl = "x" }.Timeout),
            (nameof(FalQueueOptions), new FalQueueOptions().Timeout),
        ])
        {
            Assert.True(budget > TimeSpan.FromMinutes(1), $"{name}: not generous enough ({budget})");
            Assert.True(budget < TimeSpan.FromHours(1), $"{name}: not a bound ({budget})");
        }
    }

    // ---- precedence: the request wins, the options default is the fallback ----

    [Fact]
    public void An_explicit_request_timeout_wins_over_the_options_default()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), GenerationDeadline.Resolve(5, Unreachable));
        Assert.Equal(Unreachable, GenerationDeadline.Resolve(null, Unreachable));
        Assert.Equal(Unreachable, GenerationDeadline.Resolve(0, Unreachable));    // 0/<=0 is not a budget
        Assert.Equal(Unreachable, GenerationDeadline.Resolve(-1, Unreachable));
    }

    [Fact]
    public async Task A_longer_per_request_budget_outlasts_a_short_options_default()
    {
        // deterministic in both directions: a timer can never fire EARLY, so a stalled call under a 60s
        // override can only end via the 150ms options default leaking through — the regression under test
        var provider = new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", Timeout = Short }, Stalling());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(Ask(timeoutSeconds: 60), cts.Token));
    }

    // ---- a fired deadline is a VERDICT, on every HTTP-calling entry point ----

    [Fact]
    public async Task OpenAi_images_reports_a_stalled_render_as_a_Timeout_verdict()
    {
        var provider = new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", Timeout = Unreachable }, Stalling());

        var result = await provider.GenerateAsync(Ask(timeoutSeconds: 1));

        Assert.Equal(GenerationVerdict.Timeout, result.Verdict);
        // and say WHICH clock ended it: the request's 1s budget, not the fixture client's backstop. Without
        // this the same assertion passes if request-budget precedence regresses — just five seconds later.
        Assert.Contains("timed out after 00:00:01", result.Detail);
    }

    [Fact]
    public async Task Automatic1111_reports_a_stalled_render_as_a_Timeout_verdict()
    {
        var provider = new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860", Timeout = Short }, Stalling());

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.Timeout, result.Verdict);
    }

    [Fact]
    public async Task A_probe_is_bounded_too_because_the_shim_client_has_no_timeout_of_its_own()
    {
        var openAi = await new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", Timeout = Short }, Stalling())
            .ProbeAsync();
        Assert.False(openAi.Available);

        var a1111 = await new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860", Timeout = Short }, Stalling())
            .ProbeAsync();
        Assert.False(a1111.Available);

        var comfy = await new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", Timeout = Short }, Stalling())
            .ProbeAsync();
        Assert.False(comfy.Available);
    }

    // ---- the queue backends: a deadline bounds ONE HTTP call, and a timed-out POLL keeps the render ----

    [Fact]
    public async Task A_stalled_submit_fails_rather_than_hanging_on_both_queue_backends()
    {
        var fal = await new FalQueueProvider(
            new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", Timeout = Short }, Stalling())
            .SubmitAsync(new GenerationRequest { Kind = GenerationKinds.Video, Prompt = "a wave" });
        Assert.Equal(GenerationOperationStatus.Failed, fal.Status);

        var comfy = await new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", Timeout = Short }, Stalling())
            .SubmitAsync(new GenerationRequest
            {
                Kind = GenerationKinds.Image,
                Options = new Dictionary<string, string> { ["workflow"] = "{}" },
            });
        Assert.Equal(GenerationOperationStatus.Failed, comfy.Status);
    }

    [Fact]
    public async Task A_timed_out_status_POLL_leaves_the_render_running_rather_than_abandoning_it()
    {
        // a slow status call is NO ANSWER, not a failed render — reading it as terminal would abandon a
        // submitted (and billed) generation that is merely still going
        var fal = await new FalQueueProvider(
            new FalQueueOptions { ApiKey = "k", Timeout = Short }, Stalling())
            .PollAsync("fal-ai/wan-t2v#abc");
        Assert.Equal(GenerationOperationStatus.Running, fal.Status);

        var comfy = await new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", Timeout = Short }, Stalling())
            .PollAsync("prompt-1");
        Assert.Equal(GenerationOperationStatus.Running, comfy.Status);
    }

    [Fact]
    public async Task A_timed_out_FETCH_is_a_Timeout_verdict_on_both_queue_backends()
    {
        var fal = await new FalQueueProvider(
            new FalQueueOptions { ApiKey = "k", Timeout = Short }, Stalling())
            .FetchAsync("fal-ai/wan-t2v#abc");
        Assert.Equal(GenerationVerdict.Timeout, fal.Verdict);

        var comfy = await new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", Timeout = Short }, Stalling())
            .FetchAsync("prompt-1");
        Assert.Equal(GenerationVerdict.Timeout, comfy.Verdict);
    }

    [Fact]
    public async Task A_BYO_clients_OWN_timeout_also_surfaces_as_a_verdict_rather_than_escaping()
    {
        // the shape the consumer who filed this is running today: their own client with an explicit finite
        // Timeout (180s there, 150ms here) instead of the shim's infinite one. HttpClient raises that as a
        // TaskCanceledException with the caller's token untouched — which used to escape GenerateAsync
        // uncaught, breaking the fail-safe contract for precisely the people who bounded their own client.
        var provider = new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", Timeout = Unreachable },
            () => new HttpClient(new StallingHandler()) { Timeout = Short });

        var result = await provider.GenerateAsync(Ask());      // no caller cancellation anywhere

        Assert.Equal(GenerationVerdict.Timeout, result.Verdict);
        Assert.Contains("HttpClient", result.Detail);          // says WHICH clock fired: the fix differs
    }

    // ---- a submit with no answer must not become a SECOND paid submission ----

    private static FalQueueProvider StalledFal() => new(
        new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", Timeout = Short }, Stalling());

    private static GenerationRequest Video() =>
        new() { Kind = GenerationKinds.Video, Prompt = "a wave" };

    [Fact]
    public async Task A_timed_out_submit_is_INCONCLUSIVE_rather_than_a_plain_failure()
    {
        // "the queue refused the job" and "the queue never answered" are both Failed today, and the router
        // advances on Failed — so without this flag a submit that may already be enqueued is submitted again
        // somewhere else, which is the duplicate paid render the whole checkpoint-first design exists to avoid
        var operation = await StalledFal().SubmitAsync(Video());

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.True(operation.Inconclusive);
        Assert.Contains("may still have been enqueued", operation.Detail);
    }

    [Fact]
    public async Task A_plain_submit_failure_is_NOT_inconclusive_so_fallback_still_works()
    {
        // the counterweight: an answered rejection is conclusive, and must keep advancing to the next backend
        var operation = await new FalQueueProvider(new FalQueueOptions { ApiKey = "k" }, Stalling())
            .SubmitAsync(Video());     // no model named anywhere: refused before any HTTP call

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.False(operation.Inconclusive);
    }

    [Fact]
    public async Task A_timed_out_submit_does_not_cause_a_SECOND_backend_to_be_submitted_to()
    {
        var second = new FakeGenerationJobProvider { Id = "fake-video" };
        var router = new GenerationRouter([StalledFal(), second]);

        var submission = await router.SubmitAsync(
            [new GenerationCandidate("fal"), new GenerationCandidate("fake-video")], Video());

        Assert.Equal(0, second.SubmitCalls);                       // the whole point: nobody pays twice
        Assert.Equal("fal", submission.ProviderId);                // and the caller learns WHO may hold it
        Assert.True(submission.Operation.Inconclusive);
    }

    [Fact]
    public async Task A_timed_out_submit_does_not_bench_the_backend()
    {
        // no answer is no evidence of ill health — benching on it would take a working backend out of rotation
        var tracker = new DeadHostTracker(threshold: 1);
        var router = new GenerationRouter([StalledFal(), new FakeGenerationJobProvider { Id = "fake-video" }],
            deadHosts: tracker);

        await router.SubmitAsync(
            [new GenerationCandidate("fal"), new GenerationCandidate("fake-video")], Video());

        Assert.False(tracker.IsDead("generation::fal"));
    }

    [Fact]
    public async Task The_agent_TOOL_names_the_backend_and_says_not_to_retry_an_inconclusive_submit()
    {
        // The agent path is the one with no human in it, and a model's default reaction to a tool error is to
        // call the tool again — which re-submits, which is the double charge everything above exists to stop.
        // So the observation must carry the backend the router deliberately kept, and must INSTRUCT rather
        // than merely inform.
        var options = new GenerationOptions();
        var router = new GenerationRouter([StalledFal(), new FakeGenerationJobProvider { Id = "fake-video" }]);
        var tool = new GenerationSubmitTool(router, options);

        var observation = JsonDocument.Parse(await tool.InvokeAsync(
            """{"kind":"video","prompt":"a wave","backends":["fal","fake-video"]}""")).RootElement;

        Assert.False(observation.GetProperty("ok").GetBoolean());
        var error = observation.GetProperty("error").GetString()!;
        Assert.Contains("fal", error);                       // WHICH backend may already hold it
        Assert.Contains("not", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retry", error, StringComparison.OrdinalIgnoreCase);
        // and the flag itself is on the observation, so a caller can branch without parsing prose
        Assert.True(observation.GetProperty("inconclusive").GetBoolean());
    }

    // ---- the caller's own cancellation is NOT a timeout ----

    [Fact]
    public async Task The_callers_cancellation_still_propagates_instead_of_becoming_a_Timeout_verdict()
    {
        // the subtle half: the deadline is generous and unreachable here, so anything that ends these calls
        // is the caller's token — which must surface as cancellation, never as a Timeout RESULT
        using var cts = new CancellationTokenSource(Short);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenAiImageProvider(
            new OpenAiImageOptions { BaseUrl = "https://example.invalid/v1", Timeout = Unreachable }, Stalling())
            .GenerateAsync(Ask(), cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860", Timeout = Unreachable }, Stalling())
            .GenerateAsync(Ask(), cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FalQueueProvider(
            new FalQueueOptions { ApiKey = "k", Timeout = Unreachable }, Stalling())
            .PollAsync("fal-ai/wan-t2v#abc", cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", Timeout = Unreachable }, Stalling())
            .ProbeAsync(cts.Token));
    }
}
