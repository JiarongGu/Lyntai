using System.Net;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The fal.ai queue's CANCEL path — the one URL segment that used to be a hardcoded literal while
/// <see cref="FalQueueOptions"/> promised that every segment was settable.
///
/// The promise is not decoration: this backend's wire format is documented rather than measured, so a host that
/// discovers a segment has moved retargets it in options instead of waiting for a Lyntai release. A literal in
/// the middle of that story leaves exactly one call — the one that stops a render already costing money — with
/// no repair short of a release. These pin the default (so the retargeting seam cannot quietly change the URL a
/// working host already calls) and the override (so the seam actually reaches the cancel path).</summary>
public class FalQueueCancelSegmentTests
{
    private static (FalQueueProvider Provider, StubHttpHandler Http) Provider(FalQueueOptions? options = null)
    {
        var handler = new StubHttpHandler();
        return (new FalQueueProvider(
            options ?? new FalQueueOptions { ApiKey = "k", Model = "fal-ai/wan-t2v" },
            () => new HttpClient(handler, disposeHandler: false)), handler);
    }

    [Fact]
    public void The_cancel_segment_defaults_to_the_documented_literal()
    {
        Assert.Equal("cancel", new FalQueueOptions().CancelSegment);
    }

    [Fact]
    public async Task A_cancel_calls_the_default_segment_on_the_requests_path()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.CancelAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(GenerationOperationStatus.Cancelled, operation.Status);
        Assert.Equal("https://queue.fal.run/fal-ai/wan-t2v/requests/req-123/cancel",
            http.Requests[0].Uri?.ToString());
        Assert.Equal("Key k", http.Requests[0].Auth);
    }

    [Fact]
    public async Task A_host_can_retarget_the_cancel_segment_the_way_it_retargets_status_and_requests()
    {
        // the whole point of the settable segments: a moved path is repairable by the host, not by a release
        var (provider, http) = Provider(new FalQueueOptions
        {
            ApiKey = "k",
            Model = "fal-ai/wan-t2v",
            RequestsSegment = "req",
            StatusSegment = "state",
            CancelSegment = "abort",
        });
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.CancelAsync("fal-ai/wan-t2v#req-123");

        Assert.Equal(GenerationOperationStatus.Cancelled, operation.Status);
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

        Assert.Equal(GenerationOperationStatus.Running, operation.Status);
        Assert.Contains("409", operation.Detail);
    }

    [Fact]
    public async Task A_malformed_operation_id_never_calls_a_cancel_url_at_all()
    {
        var (provider, http) = Provider();

        var operation = await provider.CancelAsync("no-separator-here");

        Assert.Equal(GenerationOperationStatus.Failed, operation.Status);
        Assert.Contains("malformed", operation.Detail);
        Assert.Empty(http.Requests);
    }
}
