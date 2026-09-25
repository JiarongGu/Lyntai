using Lyntai.Inference;
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
public class FalProviderTests
{
    private static (FalProvider Provider, StubHttpHandler Http) Provider(FalOptions? options = null)
    {
        var handler = new StubHttpHandler();
        return (new FalProvider(
            options ?? new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" },
            () => new HttpClient(handler, disposeHandler: false)), handler);
    }

    private static MediaRequest Ask() => new() { Kind = ProviderKinds.Video, Prompt = "a cat surfing" };

    [Fact]
    public void It_declares_a_job_backend_across_the_kinds_the_host_configured()
    {
        var (provider, _) = Provider();

        Assert.Equal("fal", provider.Id);
        Assert.Equal([ProviderOperation.Queued], provider.Capabilities.Operations);
        Assert.Contains(ProviderKinds.Video, provider.Capabilities.Produces);
        Assert.IsAssignableFrom<IMediaJobProvider>(provider);
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

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
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
    [InlineData("IN_QUEUE", QueuedOperationStatus.Queued)]
    [InlineData("IN_PROGRESS", QueuedOperationStatus.Running)]
    [InlineData("COMPLETED", QueuedOperationStatus.Succeeded)]
    [InlineData("SOMETHING_NEW", QueuedOperationStatus.Running)]   // unknown != failed
    public async Task Poll_maps_queue_status_onto_the_operation_states_the_job_handler_branches_on(
        string status, QueuedOperationStatus expected)
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
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" };
        options.StatusVocabulary["ENQUEUED"] = QueuedOperationStatus.Queued;      // extend
        options.StatusVocabulary["COMPLETED"] = QueuedOperationStatus.Running;    // redefine
        var (provider, http) = Provider(options);

        http.Enqueue(HttpStatusCode.OK, """{"status":"ENQUEUED"}""");
        Assert.Equal(QueuedOperationStatus.Queued, (await provider.PollAsync("fal-ai/wan-t2v#req-1")).Status);

        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");
        Assert.Equal(QueuedOperationStatus.Running, (await provider.PollAsync("fal-ai/wan-t2v#req-1")).Status);
    }

    [Fact]
    public async Task A_status_the_host_has_NOT_mapped_still_keeps_the_render_alive()
    {
        // The override must not weaken the rule that matters most: an unknown state is never terminal, so a
        // host who maps two of three states cannot lose a render by omission.
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" };
        options.StatusVocabulary.Clear();
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-1");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
        Assert.Contains("unrecognised status", operation.Detail);
    }

    [Fact]
    public async Task A_host_can_retarget_or_disable_the_cost_field()
    {
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", CostFields = ["billing_usd"] };
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
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", CostFields = [] };
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"cost":9.99,"video":{"url":"https://x.invalid/a.mp4"}}""");

        var fetched = await provider.FetchAsync("fal-ai/wan-t2v#req-1");

        Assert.Null(fetched.Usage?.CostUsd);
    }

    [Fact]
    public async Task A_COMPLETED_status_carrying_an_error_is_a_FAILED_render()
    {
        // fal's documentation reports a failed request as COMPLETED plus `error` / `error_type`; read as
        // Succeeded, the job fetched, got a 4xx and reported "succeeded but its artifacts could not be fetched"
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK,
            """{"status":"COMPLETED","error":"NSFW content detected","error_type":"content_policy_violation"}""");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-1");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("NSFW content detected", operation.Detail);
        Assert.Contains("content_policy_violation", operation.Detail);
    }

    [Fact]
    public async Task A_COMPLETED_status_with_a_null_error_still_succeeds()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED","error":null}""");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-1");

        Assert.Equal(QueuedOperationStatus.Succeeded, operation.Status);
    }

    [Fact]
    public async Task A_host_can_retarget_or_disable_the_error_field()
    {
        var retargeted = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", ErrorField = "failure" };
        var (provider, http) = Provider(retargeted);
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED","failure":"out of memory"}""");
        Assert.Equal(QueuedOperationStatus.Failed, (await provider.PollAsync("fal-ai/wan-t2v#req-1")).Status);

        var disabled = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", ErrorField = "" };
        (provider, http) = Provider(disabled);
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED","error":"ignored"}""");
        Assert.Equal(QueuedOperationStatus.Succeeded, (await provider.PollAsync("fal-ai/wan-t2v#req-1")).Status);
    }

    [Fact]
    public async Task The_auth_scheme_is_settable()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v", AuthScheme = "Bearer" });
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"r"}""");

        await provider.SubmitAsync(Ask());

        Assert.Equal("Bearer k", http.Requests[0].Auth);
    }

    [Fact]
    public async Task Query_parameters_ride_on_every_call_beside_the_webhook()
    {
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" };
        options.QueryParameters["_subdomain"] = "queue";
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-1"}""");
        http.Enqueue(HttpStatusCode.OK, """{"status":"IN_QUEUE"}""");
        http.Enqueue(HttpStatusCode.OK, """{"video":{"url":"https://cdn.invalid/a.mp4"}}""");
        http.Enqueue(HttpStatusCode.OK, "{}");

        await provider.SubmitAsync(Ask() with
        {
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["webhook"] = "https://app.invalid/h" },
        });
        await provider.PollAsync("fal-ai/wan-t2v#req-1");
        await provider.FetchAsync("fal-ai/wan-t2v#req-1");
        await provider.CancelAsync("fal-ai/wan-t2v#req-1");

        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v?_subdomain=queue&fal_webhook=https%3A%2F%2Fapp.invalid%2Fh",
            http.Requests[0].Uri?.AbsoluteUri);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-1/status?_subdomain=queue", http.Requests[1].Uri?.AbsoluteUri);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-1?_subdomain=queue", http.Requests[2].Uri?.AbsoluteUri);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-1/cancel?_subdomain=queue", http.Requests[3].Uri?.AbsoluteUri);
    }

    [Fact]
    public async Task The_hugging_face_router_route_is_configuration_alone()
    {
        // the free way to reach fal's own queue wire: a Hugging Face token, Bearer, and `?_subdomain=queue`
        var options = new FalOptions
        {
            BaseUrl = "https://router.huggingface.co/fal-ai",
            ApiKey = "hf_token",
            AuthScheme = "Bearer",
            Model = "fal-ai/krea-2/turbo",
        };
        options.QueryParameters["_subdomain"] = "queue";
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-9"}""");

        var operation = await provider.SubmitAsync(Ask() with { Kind = ProviderKinds.Image });

        Assert.Equal("fal-ai/krea-2/turbo#req-9", operation.Id);
        Assert.Equal("https://router.huggingface.co/fal-ai/fal-ai/krea-2/turbo?_subdomain=queue",
            http.Requests[0].Uri?.AbsoluteUri);
        Assert.Equal("Bearer hf_token", http.Requests[0].Auth);
    }

    [Fact]
    public async Task A_transport_failure_while_polling_keeps_the_render_alive_rather_than_abandoning_it()
    {
        // a 500 or a dropped connection says nothing about the render — reporting Failed here would abandon
        // work that is still running and already paid for
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, "{\"detail\":\"upstream hiccup\"}");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
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

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
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

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
    }

    [Fact]
    public async Task An_unconfigured_backend_polling_is_TERMINAL_rather_than_polled_forever()
    {
        // The worst shape of the same bug: an operator rotates the key out of configuration and every
        // in-flight durable render polls "not configured: BaseUrl and ApiKey are both required" forever.
        var (provider, _) = Provider(new FalOptions { BaseUrl = "https://queue.fal.run", ApiKey = null });

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
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
        var artifacts = FalProvider.ReadArtifacts(
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
        var (provider, http) = Provider(new FalOptions { Model = "m" });

        var operation = await provider.SubmitAsync(Ask());
        var probe = await provider.ProbeAsync();

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("not configured", operation.Detail);
        Assert.False(probe.Available);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_request_with_no_model_says_where_to_put_one()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k" });

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
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
            Inputs = [MediaInput.FirstFrame(new byte[] { 1, 2, 3 }, "image/png")],
        });

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
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
            Inputs = [MediaInput.FirstFrame(new Uri("https://cdn.invalid/first.png"), "image/png")],
        });

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.Contains("\"image_url\"", http.Requests[0].Body);  // the first-frame field, not input_image_url
        Assert.Contains("cdn.invalid/first.png", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_second_input_is_refused_rather_than_dropped()
    {
        // fal maps ONE input to one field, so a second — a chained artifact after a stage's own reference —
        // would be sent nowhere while the render is billed as if it had been
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask() with
        {
            Inputs =
            [
                MediaInput.FirstFrame(new Uri("https://cdn.invalid/first.png"), "image/png"),
                MediaInput.Reference(new Uri("https://cdn.invalid/style.png"), "image/png"),
            ],
        });

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_bytes_input_beside_a_uri_input_is_refused_rather_than_dropped()
    {
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask() with
        {
            Inputs =
            [
                MediaInput.FirstFrame(new Uri("https://cdn.invalid/first.png"), "image/png"),
                MediaInput.Reference(new byte[] { 1, 2, 3 }, "image/png"),
            ],
        });

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_input_in_a_role_fal_has_no_field_for_is_refused()
    {
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask() with
        {
            Inputs = [MediaInput.Voice(new Uri("https://cdn.invalid/voice.wav"), "audio/wav")],
        });

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Contains("voice", operation.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_model_with_stray_slashes_polls_the_same_path_it_submitted_to()
    {
        // the submit trimmed the model while the operation id kept it raw, so poll/fetch/cancel built
        // `…//fal-ai/wan-t2v/requests/…` for a model configured with a leading or trailing slash
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "/fal-ai/wan-t2v/" });
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-1"}""");
        http.Enqueue(HttpStatusCode.OK, """{"status":"IN_PROGRESS"}""");

        var submitted = await provider.SubmitAsync(Ask());
        await provider.PollAsync(submitted.Id);

        Assert.Equal("fal-ai/wan-t2v#req-1", submitted.Id);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-1/status", http.Requests[1].Uri?.ToString());
    }

    [Fact]
    public async Task A_malformed_artifact_url_is_a_result_rather_than_a_throw()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"video":{"url":"http://[::1"}}""");

        var result = await provider.FetchAsync("fal-ai/wan-t2v#req-1");

        Assert.True(result.IsOk);
        Assert.Equal("application/octet-stream", result.Artifacts[0].MediaType);
    }

    [Fact]
    public void The_advertised_kinds_follow_the_options_after_construction()
    {
        // the registration keeps the options instance a host may change later; the router filters on this
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" };
        var (provider, _) = Provider(options);

        options.Produces = [ProviderKinds.Audio];

        Assert.Equal([ProviderKinds.Audio], provider.Capabilities.Produces);
    }

    [Fact]
    public async Task Inline_generation_is_declined_because_the_queue_is_asynchronous()
    {
        var (provider, _) = Provider();

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(ProviderVerdict.Unsupported, result.Verdict);
    }

    [Fact]
    public async Task A_malformed_operation_id_fails_loudly_rather_than_calling_a_wrong_url()
    {
        var (provider, http) = Provider();

        var operation = await provider.PollAsync("no-separator-here");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("malformed", operation.Detail);
        Assert.Empty(http.Requests);
    }
}
