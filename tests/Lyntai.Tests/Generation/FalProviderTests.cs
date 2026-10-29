using Lyntai.Inference;
using System.Net;
using System.Text.Json;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The fal.ai queue backend. What is pinned is what would break silently and cost money — the
/// operation-id encoding a resumed job depends on, the status mapping the job handler branches on, and "no
/// artifacts" never becoming a fake success. The wire a <see cref="FalLiveTests"/> run MEASURED (an image
/// request's submit, status and result, the answer to an input fal could not download, a status read in each
/// state, a cancel, a video model and an object-valued field) is replayed verbatim below under "recorded from
/// fal"; the rest of the surface — a status vocabulary beyond fal's three, another failure fal reports on the
/// status document — is still this library's reading, and the tests pin that, never that fal answers so.</summary>
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
    public async Task A_locked_account_is_an_AUTH_failure_carrying_fals_own_words()
    {
        // MEASURED against queue.fal.run with a real key on an account with no balance: a submit answers
        // 403 {"detail":"<words>"}. The status says nothing retryable, so it must bench the host (AuthFailed)
        // instead of leaving the router to guess from the words
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.Forbidden,
            """{"detail":"User is locked. Reason: Exhausted balance. Top up your balance at fal.ai/dashboard/billing."}""");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.AuthFailed, operation.Verdict);
        Assert.False(operation.Inconclusive);   // fal answered, so nothing was enqueued
        Assert.Contains("Exhausted balance", operation.Detail);
        Assert.Equal("Key k", http.Requests[0].Auth);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderVerdict.AuthFailed)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderVerdict.RateLimited)]
    public async Task A_refused_submit_is_classified_by_its_status(HttpStatusCode status, ProviderVerdict expected)
    {
        var (provider, http) = Provider();
        http.Enqueue(status, """{"detail":"no"}""");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(expected, operation.Verdict);
    }

    [Fact]
    public async Task A_model_with_a_sub_path_is_submitted_under_its_full_path_and_polled_under_owner_and_app()
    {
        // fal's queue documentation: status, result and cancel URLs "use only the owner and the app", and one
        // carrying the endpoint path returns 405. A live run measured the status and result under owner and app
        // (and the full path's 405); the cancel URL is the submit answer's cancel_url, never called
        // (docs/generation.md §3)
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" });
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-1"}""");
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");
        http.Enqueue(HttpStatusCode.OK, """{"images":[{"url":"https://cdn.invalid/a.png"}]}""");
        http.Enqueue(HttpStatusCode.Accepted, """{"status":"CANCELLATION_REQUESTED"}""");

        var submitted = await provider.SubmitAsync(Ask());
        await provider.PollAsync(submitted.Id);
        await provider.FetchAsync(submitted.Id);
        await provider.CancelAsync(submitted.Id);

        Assert.Equal("fal-ai/flux/schnell#req-1", submitted.Id);   // the id keeps the whole model
        Assert.Equal("https://queue.fal.run/fal-ai/flux/schnell", http.Requests[0].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/req-1/status", http.Requests[1].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/req-1", http.Requests[2].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/req-1/cancel", http.Requests[3].Uri?.ToString());
    }

    // ---- recorded from fal: queue.fal.run under `Key`, fal-ai/flux/schnell, 512x512 ---------------------
    // The bodies are fal's own answers, ids and URLs as received (no header, no credential).

    private const string RecordedRequest = "01a18432-eb46-7373-9386-b2db8577fa11";

    [Fact]
    public async Task Recorded_from_fal_a_submit_answers_IN_QUEUE_with_a_request_id_and_the_owner_and_app_urls()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" });
        http.Enqueue(HttpStatusCode.OK, $$$"""
            {"status":"IN_QUEUE","request_id":"{{{RecordedRequest}}}","response_url":"https://queue.fal.run/fal-ai/flux/requests/{{{RecordedRequest}}}","status_url":"https://queue.fal.run/fal-ai/flux/requests/{{{RecordedRequest}}}/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/{{{RecordedRequest}}}/cancel","logs":null,"metrics":{},"queue_position":0,"user_queue_position":0,"user_queued_count":1}
            """);

        var submitted = await provider.SubmitAsync(Ask());

        Assert.Equal(QueuedOperationStatus.Queued, submitted.Status);
        Assert.Equal($"fal-ai/flux/schnell#{RecordedRequest}", submitted.Id);
    }

    [Fact]
    public async Task Recorded_from_fal_a_finished_request_polls_Succeeded_and_its_result_is_an_image_artifact_with_no_cost()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" });
        var id = $"fal-ai/flux/schnell#{RecordedRequest}";
        http.Enqueue(HttpStatusCode.OK, $$$"""
            {"status":"COMPLETED","request_id":"{{{RecordedRequest}}}","response_url":"https://queue.fal.run/fal-ai/flux/requests/{{{RecordedRequest}}}","status_url":"https://queue.fal.run/fal-ai/flux/requests/{{{RecordedRequest}}}/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/{{{RecordedRequest}}}/cancel","logs":null,"metrics":{"inference_time":1.676999807357788}}
            """);
        http.Enqueue(HttpStatusCode.OK, """
            {"images":[{"url":"https://v3b.fal.media/files/b/0ab03c49/1Ps_riequ46wVCFP2MKC1.png","width":512,"height":512,"content_type":"image/png"}],"timings":{"inference":0.8004623689921573},"seed":1123371702,"has_nsfw_concepts":[false],"prompt":"a red square on a white background"}
            """);

        var polled = await provider.PollAsync(id);
        var fetched = await provider.FetchAsync(id);

        Assert.Equal(QueuedOperationStatus.Succeeded, polled.Status);
        Assert.Equal($"https://queue.fal.run/fal-ai/flux/requests/{RecordedRequest}/status", http.Requests[0].Uri?.ToString());
        Assert.Equal($"https://queue.fal.run/fal-ai/flux/requests/{RecordedRequest}", http.Requests[1].Uri?.ToString());
        var artifact = Assert.Single(fetched.Artifacts);
        Assert.Equal("image/png", artifact.MediaType);
        Assert.Equal("https://v3b.fal.media/files/b/0ab03c49/1Ps_riequ46wVCFP2MKC1.png", artifact.Uri);
        // fal's status and result carry NO price, so none is reported (timings are seconds, not dollars)
        Assert.Null(fetched.Usage?.CostUsd);
    }

    [Fact]
    public async Task Recorded_from_fal_an_input_it_cannot_download_polls_Succeeded_and_fails_only_at_the_fetch()
    {
        // fal accepted the request, reported COMPLETED with NO error field, and answered the result fetch 422 with
        // a `detail` list. FalOptions.ErrorField therefore cannot see this failure at the poll: the job fetches,
        // and the fetch's own failure is what it reports
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell/redux" });
        const string id = "fal-ai/flux/schnell/redux#01a18433-5a8a-7651-bfe6-a4e747e24f8f";
        http.Enqueue(HttpStatusCode.OK, """
            {"status":"COMPLETED","request_id":"01a18433-5a8a-7651-bfe6-a4e747e24f8f","response_url":"https://queue.fal.run/fal-ai/flux/requests/01a18433-5a8a-7651-bfe6-a4e747e24f8f","status_url":"https://queue.fal.run/fal-ai/flux/requests/01a18433-5a8a-7651-bfe6-a4e747e24f8f/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/01a18433-5a8a-7651-bfe6-a4e747e24f8f/cancel","logs":null,"metrics":{"inference_time":0.2369999885559082}}
            """);
        http.Enqueue(HttpStatusCode.UnprocessableEntity, """
            {"detail":[{"loc":["body","image_url"],"msg":"Failed to download the file. Please check if the URL is accessible and try again.","type":"file_download_error","url":"https://docs.fal.ai/errors#file_download_error","input":"https://no-such-host.invalid/in.png"}]}
            """);

        var polled = await provider.PollAsync(id);
        var fetched = await provider.FetchAsync(id);

        Assert.Equal(QueuedOperationStatus.Succeeded, polled.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/01a18433-5a8a-7651-bfe6-a4e747e24f8f/status", http.Requests[0].Uri?.ToString());
        Assert.False(fetched.IsOk);
        Assert.Equal(ProviderVerdict.Failed, fetched.Verdict);
        Assert.Contains("422", fetched.Detail);
        Assert.Contains("file_download_error", fetched.Detail);
    }

    [Fact]
    public async Task Recorded_from_fal_the_full_model_path_in_a_status_url_answers_405_and_would_poll_forever()
    {
        // what a host that sets RequestPaths to the full path gets on this model: an empty 405, and a poll treats
        // anything but a 404 as still running (the cost of a wrong path, which the owner-and-app default avoids)
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" };
        options.RequestPaths["fal-ai/flux/schnell"] = "fal-ai/flux/schnell";
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.MethodNotAllowed, "");

        var polled = await provider.PollAsync($"fal-ai/flux/schnell#{RecordedRequest}");

        Assert.Equal(QueuedOperationStatus.Running, polled.Status);
        Assert.Equal($"https://queue.fal.run/fal-ai/flux/schnell/requests/{RecordedRequest}/status", http.Requests[0].Uri?.ToString());
    }

    // ---- recorded from fal, the third run: status reads, a cancel, a video model, an object-valued field ----

    [Fact]
    public async Task Recorded_from_fal_a_status_read_is_202_IN_QUEUE_with_its_position_then_202_IN_PROGRESS_then_200_COMPLETED()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" });
        const string queued = "01a186e8-a911-7fe3-80e6-83a39e388f77";
        const string running = "01a186e8-9f8e-7720-ad10-a52d47dbc052";
        http.Enqueue(HttpStatusCode.Accepted, $$$"""
            {"status":"IN_QUEUE","request_id":"{{{queued}}}","response_url":"https://queue.fal.run/fal-ai/flux/requests/{{{queued}}}","status_url":"https://queue.fal.run/fal-ai/flux/requests/{{{queued}}}/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/{{{queued}}}/cancel","logs":null,"metrics":{},"queue_position":0}
            """);
        http.Enqueue(HttpStatusCode.Accepted, $$$"""
            {"status":"IN_PROGRESS","request_id":"{{{running}}}","response_url":"https://queue.fal.run/fal-ai/flux/requests/{{{running}}}","status_url":"https://queue.fal.run/fal-ai/flux/requests/{{{running}}}/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/{{{running}}}/cancel","logs":null,"metrics":{}}
            """);

        var waiting = await provider.PollAsync($"fal-ai/flux/schnell#{queued}");
        var rendering = await provider.PollAsync($"fal-ai/flux/schnell#{running}");

        Assert.Equal(QueuedOperationStatus.Queued, waiting.Status);
        Assert.Equal("queue position 0", waiting.Detail);
        Assert.Equal(QueuedOperationStatus.Running, rendering.Status);
        Assert.Null(rendering.Detail);   // the position is on the IN_QUEUE document only
    }

    [Fact]
    public async Task Recorded_from_fal_a_cancel_that_lands_polls_COMPLETED_carrying_an_error_and_fetches_499()
    {
        // fal answers the cancel 202 and reports the request COMPLETED with `error` and `error_type` on the status
        // document itself, so ErrorField and ErrorTypeField fire: the poll is Failed, naming the kind
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" });
        const string id = "fal-ai/flux/schnell#01a186e8-efb8-7ca0-b90b-ebf6ffce2314";
        http.Enqueue(HttpStatusCode.Accepted, """{"status":"CANCELLATION_REQUESTED"}""");
        http.Enqueue(HttpStatusCode.OK, """
            {"status":"COMPLETED","request_id":"01a186e8-efb8-7ca0-b90b-ebf6ffce2314","response_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e8-efb8-7ca0-b90b-ebf6ffce2314","status_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e8-efb8-7ca0-b90b-ebf6ffce2314/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e8-efb8-7ca0-b90b-ebf6ffce2314/cancel","logs":null,"metrics":{"inference_time":0.0},"error":"Request cancelled by the client.","error_type":"client_cancelled"}
            """);
        http.Enqueue((HttpStatusCode)499, """{"detail":"Request cancelled by the client.","error_type":"client_cancelled"}""");

        var cancelled = await provider.CancelAsync(id);
        var polled = await provider.PollAsync(id);
        var fetched = await provider.FetchAsync(id);

        Assert.Equal(QueuedOperationStatus.Running, cancelled.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/01a186e8-efb8-7ca0-b90b-ebf6ffce2314/cancel", http.Requests[0].Uri?.ToString());
        Assert.Equal(QueuedOperationStatus.Failed, polled.Status);
        Assert.Contains("client_cancelled", polled.Detail);
        Assert.Contains("Request cancelled by the client.", polled.Detail);
        Assert.False(fetched.IsOk);
        Assert.Contains("499", fetched.Detail);
    }

    [Fact]
    public async Task Recorded_from_fal_a_cancel_of_a_finished_request_is_400_ALREADY_COMPLETED_and_the_poll_says_Succeeded()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" });
        const string id = "fal-ai/flux/schnell#01a186e8-efbd-72e2-8a8f-0faea892deda";
        http.Enqueue(HttpStatusCode.BadRequest, """{"status":"ALREADY_COMPLETED"}""");
        http.Enqueue(HttpStatusCode.OK, """
            {"status":"COMPLETED","request_id":"01a186e8-efbd-72e2-8a8f-0faea892deda","response_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e8-efbd-72e2-8a8f-0faea892deda","status_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e8-efbd-72e2-8a8f-0faea892deda/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e8-efbd-72e2-8a8f-0faea892deda/cancel","logs":null,"metrics":{"inference_time":0.625}}
            """);

        var cancelled = await provider.CancelAsync(id);
        var polled = await provider.PollAsync(id);

        Assert.Equal(QueuedOperationStatus.Running, cancelled.Status);   // a rejected cancel says nothing of how it ended
        Assert.Contains("400", cancelled.Detail);
        Assert.Equal(QueuedOperationStatus.Succeeded, polled.Status);
    }

    [Fact]
    public async Task Recorded_from_fal_a_video_model_polls_under_owner_and_app_and_its_result_is_a_video_artifact_with_no_cost()
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k", Model = "fal-ai/ltx-video" });
        const string id = "fal-ai/ltx-video#01a186ef-e123-7c23-aec5-6632b3107826";
        http.Enqueue(HttpStatusCode.Accepted, """
            {"status":"IN_PROGRESS","request_id":"01a186ef-e123-7c23-aec5-6632b3107826","response_url":"https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826","status_url":"https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826/status","cancel_url":"https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826/cancel","logs":null,"metrics":{}}
            """);
        http.Enqueue(HttpStatusCode.OK, """
            {"status":"COMPLETED","request_id":"01a186ef-e123-7c23-aec5-6632b3107826","response_url":"https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826","status_url":"https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826/status","cancel_url":"https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826/cancel","logs":null,"metrics":{"inference_time":13.463000059127808}}
            """);
        http.Enqueue(HttpStatusCode.OK, """
            {"video":{"url":"https://v3b.fal.media/files/b/0ab04e3c/0vILv0Rc7tsU4nzrdYHAY.mp4","content_type":"video/mp4","file_name":"0vILv0Rc7tsU4nzrdYHAY.mp4","file_size":1014724},"seed":262130060}
            """);

        var running = await provider.PollAsync(id);
        var done = await provider.PollAsync(id);
        var fetched = await provider.FetchAsync(id);

        Assert.Equal(QueuedOperationStatus.Running, running.Status);
        Assert.Equal(QueuedOperationStatus.Succeeded, done.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/ltx-video/requests/01a186ef-e123-7c23-aec5-6632b3107826/status", http.Requests[0].Uri?.ToString());
        var artifact = Assert.Single(fetched.Artifacts);
        Assert.Equal("video/mp4", artifact.MediaType);
        Assert.Equal("https://v3b.fal.media/files/b/0ab04e3c/0vILv0Rc7tsU4nzrdYHAY.mp4", artifact.Uri);
        Assert.Null(fetched.Usage?.CostUsd);
    }

    [Fact]
    public async Task Recorded_from_fal_an_object_valued_field_sent_as_a_string_is_accepted_and_refused_at_the_fetch()
    {
        // what a host that does not name image_size in JsonFields gets: fal accepts the submit and polls COMPLETED,
        // then answers the fetch 422 because the field is not an object
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" };
        var (provider, http) = Provider(options);
        const string id = "fal-ai/flux/schnell#01a186e9-c2d4-71d1-9d8b-086f39a9f902";
        http.Enqueue(HttpStatusCode.OK, """
            {"status":"COMPLETED","request_id":"01a186e9-c2d4-71d1-9d8b-086f39a9f902","response_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e9-c2d4-71d1-9d8b-086f39a9f902","status_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e9-c2d4-71d1-9d8b-086f39a9f902/status","cancel_url":"https://queue.fal.run/fal-ai/flux/requests/01a186e9-c2d4-71d1-9d8b-086f39a9f902/cancel","logs":null,"metrics":{"inference_time":0.0410001277923584}}
            """);
        http.Enqueue(HttpStatusCode.UnprocessableEntity, """
            {"detail":[{"type":"model_attributes_type","loc":["body","image_size","ImageSize"],"msg":"Input should be a valid dictionary or object to extract fields from","input":"{\"width\":512,\"height\":384}"},{"type":"literal_error","loc":["body","image_size","literal['square_hd','square','portrait_4_3','portrait_16_9','landscape_4_3','landscape_16_9']"],"msg":"Input should be 'square_hd', 'square', 'portrait_4_3', 'portrait_16_9', 'landscape_4_3' or 'landscape_16_9'","input":"{\"width\":512,\"height\":384}","ctx":{"expected":"'square_hd', 'square', 'portrait_4_3', 'portrait_16_9', 'landscape_4_3' or 'landscape_16_9'"}}]}
            """);

        var polled = await provider.PollAsync(id);
        var fetched = await provider.FetchAsync(id);

        Assert.Equal(QueuedOperationStatus.Succeeded, polled.Status);
        Assert.False(fetched.IsOk);
        Assert.Contains("model_attributes_type", fetched.Detail);
    }

    [Theory]
    [InlineData("fal-ai/wan-t2v", "fal-ai/wan-t2v")]                       // owner and app
    [InlineData("fal-ai/flux/schnell", "fal-ai/flux")]                     // a sub-path: the first two segments
    [InlineData("fal-ai/flux/dev/image-to-image", "fal-ai/flux")]
    [InlineData("single", "single")]                                       // a model that has no app at all
    public async Task Without_an_override_the_request_path_is_the_owner_and_app_byte_for_byte(string model, string under)
    {
        var (provider, http) = Provider(new FalOptions { ApiKey = "k" });
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");
        http.Enqueue(HttpStatusCode.OK, """{"images":[{"url":"https://cdn.invalid/a.png"}]}""");
        http.Enqueue(HttpStatusCode.Accepted, """{"status":"CANCELLATION_REQUESTED"}""");
        var id = $"{model}#req-1";

        await provider.PollAsync(id);
        await provider.FetchAsync(id);
        await provider.CancelAsync(id);

        Assert.Equal($"https://queue.fal.run/{under}/requests/req-1/status", http.Requests[0].Uri?.ToString());
        Assert.Equal($"https://queue.fal.run/{under}/requests/req-1", http.Requests[1].Uri?.ToString());
        Assert.Equal($"https://queue.fal.run/{under}/requests/req-1/cancel", http.Requests[2].Uri?.ToString());
    }

    [Fact]
    public async Task A_host_whose_model_fal_addresses_differently_sets_its_request_path_and_the_submit_is_unchanged()
    {
        var options = new FalOptions { ApiKey = "k", Model = "fal-ai/flux/schnell" };
        options.RequestPaths["fal-ai/flux/schnell"] = "/fal-ai/flux/schnell/";   // stray slashes are trimmed
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"req-1"}""");
        http.Enqueue(HttpStatusCode.OK, """{"status":"COMPLETED"}""");
        http.Enqueue(HttpStatusCode.OK, """{"images":[{"url":"https://cdn.invalid/a.png"}]}""");
        http.Enqueue(HttpStatusCode.Accepted, """{"status":"CANCELLATION_REQUESTED"}""");

        var submitted = await provider.SubmitAsync(Ask());
        await provider.PollAsync(submitted.Id);
        await provider.FetchAsync(submitted.Id);
        await provider.CancelAsync(submitted.Id);

        Assert.Equal("fal-ai/flux/schnell#req-1", submitted.Id);
        Assert.Equal("https://queue.fal.run/fal-ai/flux/schnell", http.Requests[0].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/schnell/requests/req-1/status", http.Requests[1].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/schnell/requests/req-1", http.Requests[2].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/schnell/requests/req-1/cancel", http.Requests[3].Uri?.ToString());
    }

    [Fact]
    public async Task A_request_path_override_is_per_model_and_a_blank_one_is_no_override()
    {
        var options = new FalOptions { ApiKey = "k" };
        options.RequestPaths["FAL-AI/flux/schnell"] = "owner/app/extra";   // keys match case-insensitively
        options.RequestPaths["fal-ai/flux/dev"] = "  ";
        var (provider, http) = Provider(options);
        http.Enqueue(HttpStatusCode.OK, """{"status":"IN_QUEUE"}""");

        await provider.PollAsync("fal-ai/flux/schnell#r");
        await provider.PollAsync("fal-ai/flux/dev#r");
        await provider.PollAsync("fal-ai/flux/pro#r");

        Assert.Equal("https://queue.fal.run/owner/app/extra/requests/r/status", http.Requests[0].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/r/status", http.Requests[1].Uri?.ToString());
        Assert.Equal("https://queue.fal.run/fal-ai/flux/requests/r/status", http.Requests[2].Uri?.ToString());
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

    private static FalOptions WithJsonFields(params string[] fields) =>
        new() { ApiKey = "k", Model = "fal-ai/flux/schnell", JsonFields = [.. fields] };

    private static MediaRequest AskWith(params (string Key, string Value)[] options) => Ask() with
    {
        Options = options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public async Task A_field_named_as_JSON_is_sent_as_an_object_when_its_value_is_one()
    {
        var (provider, http) = Provider(WithJsonFields("image_size"));
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"r"}""");

        await provider.SubmitAsync(AskWith(("image_size", """{"width":1024,"height":768}""")));

        Assert.Equal("""{"prompt":"a cat surfing","image_size":{"width":1024,"height":768}}""", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_field_named_as_JSON_is_sent_as_an_array_or_a_bare_scalar_when_its_value_is_one()
    {
        var (provider, http) = Provider(WithJsonFields("LORAS", "guidance"));   // matched case-insensitively
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"r"}""");

        await provider.SubmitAsync(AskWith(("loras", """[{"path":"a","scale":0.5},"b"]"""), ("guidance", "7.5")));

        Assert.Equal("""{"prompt":"a cat surfing","loras":[{"path":"a","scale":0.5},"b"],"guidance":7.5}""",
            http.Requests[0].Body);
    }

    [Fact]
    public async Task An_option_not_named_as_JSON_is_still_a_string_even_when_it_looks_like_JSON()
    {
        var (provider, http) = Provider(WithJsonFields("image_size"));
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"r"}""");

        await provider.SubmitAsync(AskWith(("duration", "5"), ("style", """{"a":1}""")));

        // read back rather than compared as text: the writer's escaping of the inner quotes is its own
        using var body = JsonDocument.Parse(http.Requests[0].Body);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("duration").ValueKind);
        Assert.Equal("5", body.RootElement.GetProperty("duration").GetString());
        Assert.Equal("""{"a":1}""", body.RootElement.GetProperty("style").GetString());
    }

    [Fact]
    public async Task With_no_JSON_fields_named_every_option_is_a_string_as_before()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"request_id":"r"}""");

        await provider.SubmitAsync(AskWith(("image_size", """{"width":1,"height":2}""")));

        using var body = JsonDocument.Parse(http.Requests[0].Body);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("image_size").ValueKind);
        Assert.Equal("""{"width":1,"height":2}""", body.RootElement.GetProperty("image_size").GetString());
    }

    [Theory]
    [InlineData("square_hd")]            // a preset name where an object was asked for
    [InlineData("{\"width\":")]          // truncated
    [InlineData("")]                     // nothing
    [InlineData("{} {}")]                // trailing content
    public async Task A_JSON_field_whose_value_is_not_JSON_is_refused_before_any_call(string value)
    {
        var (provider, http) = Provider(WithJsonFields("image_size"));

        var submitted = await provider.SubmitAsync(AskWith(("image_size", value)));

        Assert.Equal(QueuedOperationStatus.Failed, submitted.Status);
        Assert.Equal(ProviderVerdict.Unsupported, submitted.Verdict);
        Assert.Contains("image_size", submitted.Detail);
        Assert.Contains("not valid JSON", submitted.Detail);
        Assert.Empty(http.Requests);
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
    // Past the shapes recorded above, this backend's wire format is documented, not measured. The
    // defaults there are a reading of vendor docs, and the host who first runs them for real is the one who
    // finds out. What these pin is that finding out is CHEAP: every mapping that could be wrong is an
    // option, so the fix is a configuration edit rather than an upstream release.

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
        // Succeeded, the job fetches, gets a 4xx and reports "succeeded but its artifacts could not be fetched"
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
        // The sibling of the test above: not every GetAsync failure is Running. Reported as Running, an id fal
        // will never resolve is polled every 15 seconds for the life of the job — never dead-lettered, never
        // failed, never completed, with the reason sitting in Detail where nothing acts on it. ComfyUiProvider
        // states the rule: "A 4xx or an unconfigured BaseUrl IS terminal — that id will never resolve, and
        // polling it forever strands the job."
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
        // Not "terminal unless 5xx", which is right for ComfyUiProvider (a loopback server that never
        // rate-limits) and wrong here: fal is a hosted, paid, rate-limiting API, and the job handler turns
        // Failed into JobOutcome.Fail, so ONE 429 would dead-letter a render still running and already billed.
        // Only a 404 (this id will never resolve) and the unconfigured pre-check are terminal.
        var (provider, http) = Provider();
        http.Enqueue(status, "{\"detail\":\"slow down\"}");

        var operation = await provider.PollAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
    }

    [Fact]
    public async Task An_unconfigured_backend_polling_is_TERMINAL_rather_than_polled_forever()
    {
        // The worst shape of the same failure: an operator rotates the key out of configuration and every
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
        // the expensive shape: fal reads input media from a URL, so dropping a bytes-only input would submit —
        // and bill — a text-to-video render against a caller who asked for image→video, and the result
        // would come back plausible. Refusing before the POST is the only honest answer.
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
        // the submit trims the model, so the operation id must too: kept raw, poll/fetch/cancel build
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

    // ---- cancel: every URL segment is settable, the one that stops a paid render included ----------------

    [Fact]
    public async Task A_cancel_calls_the_default_segment_on_the_requests_path()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.CancelAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Cancelled, operation.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-123/cancel",
            http.Requests[0].Uri?.ToString());
        Assert.Equal("Key k", http.Requests[0].Auth);
    }

    [Fact]
    public async Task A_host_can_retarget_the_cancel_segment_the_way_it_retargets_status_and_requests()
    {
        // the whole point of the settable segments: a moved path is repairable by the host, not by a release
        var (provider, http) = Provider(new FalOptions
        {
            ApiKey = "k",
            Model = "fal-ai/wan-t2v",
            RequestsSegment = "req",
            StatusSegment = "state",
            CancelSegment = "abort",
        });
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.CancelAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Cancelled, operation.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/req/req-123/abort", http.Requests[0].Uri?.ToString());
    }

    [Fact]
    public async Task A_rejected_cancel_still_reports_the_render_as_running_rather_than_pretending_it_stopped()
    {
        // a render already in flight may not be cancellable; reporting Cancelled would strand a paid generation
        // that is still producing artifacts
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.Conflict, "{\"detail\":\"already running\"}");

        var operation = await provider.CancelAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
        Assert.Contains("409", operation.Detail);
    }

    [Fact]
    public async Task A_202_cancellation_REQUEST_reports_the_render_still_running()
    {
        // fal documents its cancel as `202 {"status":"CANCELLATION_REQUESTED"}` — a request, not a confirmed
        // stop: the render may still finish and be billed, so only polling says how it ended
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.Accepted, """{"status":"CANCELLATION_REQUESTED"}""");

        var operation = await provider.CancelAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
        Assert.Contains("CANCELLATION_REQUESTED", operation.Detail);
    }

    [Fact]
    public async Task A_malformed_operation_id_never_calls_a_cancel_url_at_all()
    {
        var (provider, http) = Provider();

        var operation = await provider.CancelAsync("no-separator-here");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("malformed", operation.Detail);
        Assert.Empty(http.Requests);
    }
}
