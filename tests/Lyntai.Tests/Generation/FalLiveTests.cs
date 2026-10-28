using System.Net;
using System.Net.Http.Headers;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The fal backend against fal's OWN queue (<c>queue.fal.run</c>, <c>Authorization: Key …</c>).
///
/// <para>Skipped without <c>FAL_KEY</c>. <b>Each test makes ONE submit and the pair costs about a third of a
/// cent</b> — the cheapest image model fal lists ($0.003 per megapixel, billed rounded up to one) at its
/// smallest preset, and a second request that fails before it renders (fal reports no cost, so what it bills is not measured) — so this is a
/// measurement to run on purpose, never a gate. The key is read from the
/// environment and goes nowhere but the <c>Authorization</c> header; the wire log below records each exchange's
/// method, URL, status and body, never a header.</para></summary>
public class FalLiveTests(ITestOutputHelper output)
{
    private static string? Key => Environment.GetEnvironmentVariable("FAL_KEY");

    private const string NoKey = "set FAL_KEY to a fal.ai API key (each test makes one submit, a fraction of a cent)";

    /// <summary>The cheapest image model on fal's price list: billed per megapixel, a few hundredths of a cent
    /// for the smallest preset.</summary>
    private const string Cheapest = "fal-ai/flux/schnell";

    private readonly List<string> _wire = [];

    private FalProvider Provider() => new(new FalOptions { ApiKey = Key }, () => new HttpClient(new WireLog(_wire)));

    /// <summary>An account with no balance is refused at the door (<c>403 User is locked … Exhausted balance</c>,
    /// pinned in <c>FalProviderTests</c>), which says nothing about the queue behind it: skip, never pass.</summary>
    private static void SkipWhenLocked(QueuedOperation submitted) =>
        Skip.If(submitted.Verdict == ProviderVerdict.AuthFailed &&
                submitted.Detail?.Contains("balance", StringComparison.OrdinalIgnoreCase) == true,
            "the fal account has no balance, so the queue was never reached: top up and re-run");

    private void Dump()
    {
        foreach (var line in _wire) output.WriteLine(line);
    }

    [SkippableFact]
    public async Task The_cheapest_image_model_renders_through_submit_poll_fetch_on_fals_own_queue()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), NoKey);
        var provider = Provider();

        var submitted = await provider.SubmitAsync(new MediaRequest
        {
            Kind = ProviderKinds.Image,
            Model = Cheapest,
            Prompt = "a red square on a white background",
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image_size"] = "square",     // the smallest preset: 512x512
                ["output_format"] = "png",
            },
        });
        try
        {
            SkipWhenLocked(submitted);
            Assert.True(submitted.Status == QueuedOperationStatus.Queued, $"{submitted.Verdict}: {submitted.Detail}");
            Assert.StartsWith($"{Cheapest}#", submitted.Id);

            var seen = new List<string>();
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            QueuedOperation polled;
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                polled = await provider.PollAsync(submitted.Id);
                seen.Add($"{polled.Status}");
                Assert.True(polled.Status != QueuedOperationStatus.Failed, polled.Detail);
            }
            while (polled.Status != QueuedOperationStatus.Succeeded && DateTime.UtcNow < deadline);
            output.WriteLine($"poll statuses: {string.Join(" ", seen)}");
            Assert.True(polled.Status == QueuedOperationStatus.Succeeded,
                $"the render did not finish inside the budget — last: {polled.Status} {polled.Detail}");

            var fetched = await provider.FetchAsync(submitted.Id);
            Assert.True(fetched.IsOk, $"{fetched.Verdict}: {fetched.Detail}");
            var artifact = Assert.Single(fetched.Artifacts);
            output.WriteLine($"artifact {artifact.MediaType} cost {fetched.Usage?.CostUsd?.ToString() ?? "none"}");
            Assert.Equal("image/png", artifact.MediaType);

            // the URL must serve the image, or it was no artifact at all (a CDN link, so no credential)
            using var cdn = new HttpClient();
            var png = await cdn.GetByteArrayAsync(artifact.Uri);
            Assert.Equal((512, 512), LivePng.Dimensions(png));

            // the sub-path question, measured on the finished request: the full model path in the status URL is
            // what this backend sent before, and fal's documentation says it answers 405
            var requestId = submitted.Id[(submitted.Id.LastIndexOf('#') + 1)..];
            using var raw = new HttpClient(new WireLog(_wire));
            using var message = new HttpRequestMessage(HttpMethod.Get,
                $"https://queue.fal.run/{Cheapest}/requests/{requestId}/status");
            message.Headers.Authorization = new AuthenticationHeaderValue("Key", Key);
            using var fullPath = await raw.SendAsync(message);
            output.WriteLine($"full model path in the status URL: {(int)fullPath.StatusCode}");
            Assert.NotEqual(HttpStatusCode.OK, fullPath.StatusCode);
        }
        finally { Dump(); }
    }

    [SkippableFact]
    public async Task A_request_whose_input_cannot_be_downloaded_is_accepted_then_fails_at_the_result_fetch()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), NoKey);
        var provider = Provider();

        // Redux takes one image_url and nothing else; a host that does not resolve cannot be downloaded from, so
        // the request is accepted, then fails before it renders
        var submitted = await provider.SubmitAsync(new MediaRequest
        {
            Kind = ProviderKinds.Image,
            Model = $"{Cheapest}/redux",
            Inputs = [MediaInput.FirstFrame(new Uri("https://no-such-host.invalid/in.png"), "image/png")],
        });
        try
        {
            SkipWhenLocked(submitted);
            output.WriteLine($"submit: {submitted.Status} {submitted.Verdict} {submitted.Detail}");
            Assert.True(submitted.Status == QueuedOperationStatus.Queued, $"{submitted.Verdict}: {submitted.Detail}");

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            QueuedOperation polled;
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                polled = await provider.PollAsync(submitted.Id);
            }
            while (polled.Status is QueuedOperationStatus.Queued or QueuedOperationStatus.Running &&
                   DateTime.UtcNow < deadline);
            output.WriteLine($"poll: {polled.Status} {polled.Detail}");

            var fetched = await provider.FetchAsync(submitted.Id);   // what the result of a failed request looks like
            output.WriteLine($"fetch: {fetched.Verdict} {fetched.Detail}");

            // MEASURED: fal reports COMPLETED with no `error` field, so the poll cannot see this failure, and the
            // result fetch answers 422 with a `detail` list naming the field and the kind
            Assert.Equal(QueuedOperationStatus.Succeeded, polled.Status);
            Assert.False(fetched.IsOk);
            Assert.Contains("422", fetched.Detail);
            Assert.Contains("file_download_error", fetched.Detail);
        }
        finally { Dump(); }
    }

    /// <summary>Records method, URL, status and body of every exchange — no request header, so no credential.</summary>
    private sealed class WireLog(List<string> lines) : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sent = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var response = await base.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            lines.Add($"{request.Method} {request.RequestUri} {sent}\n  -> {(int)response.StatusCode} " +
                      $"{(body.Length > 1500 ? body[..1500] + "…" : body)}");
            return response;
        }
    }
}
