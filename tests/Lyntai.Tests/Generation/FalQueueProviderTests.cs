using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The fal.ai queue backend. Deliberately THIN coverage: this backend's wire format is documented
/// rather than measured (no API key to call), so exhaustive tests here would only prove our code consistent
/// with a guess. What is pinned is what would break silently and cost money — the operation-id encoding a
/// resumed job depends on, the status mapping the job handler branches on, and "no artifacts" never becoming a
/// fake success. The vendor shape itself gets confirmed the first time it runs for real.</summary>
public class FalQueueProviderTests
{
    private static (FalQueueProvider Provider, StubHttpHandler Http) Provider(FalQueueOptions? options = null)
    {
        var handler = new StubHttpHandler();
        return (new FalQueueProvider(
            options ?? new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" },
            () => new HttpClient(handler, disposeHandler: false)), handler);
    }

    private static GenerationRequest Ask() => new() { Kind = GenerationKinds.Video, Prompt = "a cat surfing" };

    [Fact]
    public void It_declares_a_job_backend_across_the_kinds_the_host_configured()
    {
        var (provider, _) = Provider();

        Assert.Equal("fal", provider.Id);
        Assert.Equal([GenerationDelivery.Job], provider.Capabilities.Deliveries);
        Assert.Contains(GenerationKinds.Video, provider.Capabilities.Kinds);
        Assert.IsAssignableFrom<IGenerationJobProvider>(provider);
        Assert.Empty(provider.Capabilities.Models);   // hundreds, and they change without us
    }

    [Fact]
    public async Task A_submission_authorizes_with_Key_and_returns_an_operation_id_carrying_the_model()
    {
        // the model is embedded because the queue's status/result URLs need it, while a RESUMED job hands back
        // only an operation id — losing the model would strand a paid render
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-123","status":"IN_QUEUE"}""");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(GenerationOperationStatus.Queued, operation.Status);
        Assert.Equal("fal-ai/wan-t2v#req-123", operation.Id);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v", http.Requests[0].Uri?.ToString());
        Assert.Equal("Key k", http.Requests[0].Auth);
        Assert.Contains("a cat surfing", http.Requests[0].Body);
    }

    [Fact]
    public async Task Options_pass_through_as_model_input_and_a_webhook_stays_out_of_the_body()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"r"}""");

        await provider.SubmitAsync(Ask() with
        {
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["duration"] = "5",
                ["webhook"] = "https://app.invalid/hook",
            },
        });

        Assert.Contains("\"duration\":\"5\"", http.Requests[0].Body);
        Assert.DoesNotContain("webhook", http.Requests[0].Body);          // it's a URL, not an input
        Assert.Contains("fal_webhook=https%3A%2F%2Fapp.invalid%2Fhook", http.Requests[0].Uri?.Query);
    }

    [Theory]
    [InlineData("IN_QUEUE", GenerationOperationStatus.Queued)]
    [InlineData("IN_PROGRESS", GenerationOperationStatus.Running)]
    [InlineData("COMPLETED", GenerationOperationStatus.Succeeded)]
    [InlineData("SOMETHING_NEW", GenerationOperationStatus.Running)]   // unknown != failed
    public async Task Poll_maps_queue_status_onto_the_operation_states_the_job_handler_branches_on(
        string status, GenerationOperationStatus expected)
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, $"{{\"status\":\"{status}\"}}");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(expected, operation.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-123/status", http.Requests[0].Uri?.ToString());
    }

    // ---- the unmeasured surface is CORRECTABLE without a library release ------------------------------
    //
    // This backend's wire format is documented, not measured — nobody here has a key to call it. The
    // defaults are therefore a reading of vendor docs, and the host who first runs it for real is the one
    // who finds out. What these pin is that finding out is CHEAP: every mapping that could be wrong is an
    // option, so the fix is a configuration edit rather than an upstream release. That is what makes
    // "leave it unmeasured" a defensible position instead of a deferred bug.

    [Fact]
    public async Task A_host_that_learns_the_real_status_vocabulary_can_correct_it_in_configuration()
    {
        var options = new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" };
        options.StatusVocabulary["ENQUEUED"] = GenerationOperationStatus.Queued;      // extend
        options.StatusVocabulary["COMPLETED"] = GenerationOperationStatus.Running;    // redefine
        var (provider, http) = Provider(options);

        http.Enqueue(HttpStatusCode.OK, """{"status":"ENQUEUED"}""");
        Assert.Equal(GenerationOperationStatus.Queued, (await provider.PollAsync("fal-ai/wan-t2v#req-1")).Status);

        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");
        Assert.Equal(GenerationOperationStatus.Running, (await provider.PollAsync("fal-ai/wan-t2v#req-1")).Status);
    }

    [Fact]
    public async Task A_status_the_host_has_NOT_mapped_still_keeps_the_render_alive()
    {
        // The override must not weaken the rule that matters most: an unknown state is never terminal, so a
        // host who maps two of three states cannot lose a render by omission.
        var options = new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" };
        options.StatusVocabulary.Clear();
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-1");

        Assert.Equal(GenerationOperationStatus.Running, operation.Status);
        Assert.Contains("unrecognised status", operation.Detail);
    }

    [Fact]
    public async Task A_host_can_retarget_or_disable_the_cost_field()
    {
        var options = new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", CostFields = ["billing_usd"] };
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"billing_usd":0.42,"video":{"url":"https://x.invalid/a.mp4"}}""");

        var fetched = await provider.FetchAsync("fal-ai/wan-t2v#req-1");

        Assert.Equal(0.42, fetched.Usage?.CostUsd);
    }

    [Fact]
    public async Task An_empty_cost_field_list_reports_no_cost_rather_than_a_wrong_one()
    {
        // Honest when a deployment knows the shipped names are wrong: no number beats a number that is not
        // the price, because the budget decorator SPENDS against whatever this reports.
        var options = new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", CostFields = [] };
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"cost":9.99,"video":{"url":"https://x.invalid/a.mp4"}}""");

        var fetched = await provider.FetchAsync("fal-ai/wan-t2v#req-1");

        Assert.Null(fetched.Usage?.CostUsd);
    }

    [Fact]
    public async Task A_transport_failure_while_polling_keeps_the_render_alive_rather_than_abandoning_it()
    {
        // a 500 or a dropped connection says nothing about the render — reporting Failed here would abandon
        // work that is still running and already paid for
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, "{\"detail\":\"upstream hiccup\"}");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(GenerationOperationStatus.Running, operation.Status);
        Assert.Contains("hiccup", operation.Detail);
    }

    [Fact]
    public async Task A_4xx_while_polling_is_TERMINAL_rather_than_polled_forever()
    {
        // The sibling of the test above, and the case it was silently covering. Every GetAsync failure —
        // 4xx, 5xx, unconfigured, transport — was reported as Running, so an id fal will never resolve was
        // polled every 15 seconds for the life of the job: never dead-lettered, never failed, never
        // completed, with the reason sitting in Detail where nothing acts on it. ComfyUiProvider reasons
        // about exactly this case and answers the opposite way, in writing: "A 4xx or an unconfigured
        // BaseUrl IS terminal — that id will never resolve, and polling it forever strands the job."
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.NotFound, "{\"detail\":\"request not found\"}");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("not found", operation.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]   // fal rate-limits its queue endpoints
    [InlineData(HttpStatusCode.Unauthorized)]      // a key rotation in flight
    [InlineData(HttpStatusCode.Forbidden)]         // a WAF/CDN challenge
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_retryable_status_while_polling_keeps_an_already_paid_render_alive(HttpStatusCode status)
    {
        // Round 2 of this review caught the first version classifying "terminal unless 5xx", copied from
        // ComfyUiProvider — where it is right, because ComfyUI is a loopback server that never rate-limits.
        // fal is a hosted, paid, rate-limiting API: under that rule ONE 429 dead-lettered a render that was
        // still running and already billed, because the job handler turns Failed into JobOutcome.Fail.
        // Only a 404 (this id will never resolve) and the unconfigured pre-check are terminal now.
        var (provider, http) = Provider();
        http.Enqueue(status, "{\"detail\":\"slow down\"}");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(GenerationOperationStatus.Running, operation.Status);
    }

    [Fact]
    public async Task An_unconfigured_backend_polling_is_TERMINAL_rather_than_polled_forever()
    {
        // The worst shape of the same bug: an operator rotates the key out of configuration and every
        // in-flight durable render polls "not configured: BaseUrl and ApiKey are both required" forever.
        var (provider, _) = Provider(new FalQueueOptions { BaseUrl = "https://queue.fal.run", ApiKey = null });

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("not configured", operation.Detail);
    }

    [Fact]
    public async Task A_fetch_finds_the_output_url_whatever_the_model_calls_it()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK,
            """{"video":{"url":"https://cdn.invalid/out.mp4","content_type":"video/mp4"},"cost":0.42}""");

        var result = await provider.FetchAsync("fal-ai/wan-t2v#req-123");

        Assert.True(result.IsOk);
        Assert.Equal("video/mp4", result.Artifacts[0].MediaType);
        Assert.Equal("https://cdn.invalid/out.mp4", result.Artifacts[0].Uri);
        Assert.Equal(0.42, result.Usage?.CostUsd);        // reported, never inferred from a rate card
    }

    [Fact]
    public void The_artifact_reader_handles_an_array_shape_and_infers_type_from_the_extension()
    {
        var artifacts = FalQueueProvider.ReadArtifacts(
            """{"images":[{"url":"https://cdn.invalid/a.png"},{"url":"https://cdn.invalid/b.jpg?x=1"}]}""");

        Assert.Equal(2, artifacts.Count);
        Assert.Equal("image/png", artifacts[0].MediaType);
        Assert.Equal("image/jpeg", artifacts[1].MediaType);   // query string ignored
    }

    [Fact]
    public async Task An_unrecognised_result_is_a_failure_not_an_empty_success()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"something":"unexpected"}""");

        var result = await provider.FetchAsync("fal-ai/wan-t2v#req-123");

        Assert.False(result.IsOk);
        Assert.Contains("no artifacts", result.Detail);
    }

    [Fact]
    public async Task No_api_key_is_NOT_CONFIGURED_and_never_calls_out()
    {
        var (provider, http) = Provider(new FalQueueOptions { Model = "m" });

        var operation = await provider.SubmitAsync(Ask());
        var probe = await provider.ProbeAsync();

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("not configured", operation.Detail);
        Assert.False(probe.Available);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_request_with_no_model_says_where_to_put_one()
    {
        var (provider, http) = Provider(new FalQueueOptions { ApiKey = "k" });

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("no model", operation.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_bytes_only_input_is_refused_rather_than_dropped_and_billed()
    {
        // the expensive shape: fal reads input media from a URL, so dropping a bytes-only input submitted —
        // and billed — a text-to-video render against a caller who asked for image→video, and the result
        // came back plausible. Refusing before the POST is the only honest answer.
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask() with
        {
            Inputs = [GenerationInput.FirstFrame(new byte[] { 1, 2, 3 }, "image/png")],
        });

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("URL", operation.Detail);
        Assert.Empty(http.Requests);          // nothing was submitted, so nothing was billed
    }

    [Fact]
    public async Task An_input_carrying_a_uri_still_submits()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-123","status":"IN_QUEUE"}""");

        var operation = await provider.SubmitAsync(Ask() with
        {
            Inputs = [GenerationInput.FirstFrame(new Uri("https://cdn.invalid/first.png"), "image/png")],
        });

        Assert.Equal(GenerationOperationStatus.Queued, operation.Status);
        Assert.Contains("\"image_url\"", http.Requests[0].Body);  // the first-frame field, not input_image_url
        Assert.Contains("cdn.invalid/first.png", http.Requests[0].Body);
    }

    [Fact]
    public async Task Inline_generation_is_declined_because_the_queue_is_asynchronous()
    {
        var (provider, _) = Provider();

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
    }

    [Fact]
    public async Task A_malformed_operation_id_fails_loudly_rather_than_calling_a_wrong_url()
    {
        var (provider, http) = Provider();

        var operation = await provider.PollAsync("no-separator-here");

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("malformed", operation.Detail);
        Assert.Empty(http.Requests);
    }
}
