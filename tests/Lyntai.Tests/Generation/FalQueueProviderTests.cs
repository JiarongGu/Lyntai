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
