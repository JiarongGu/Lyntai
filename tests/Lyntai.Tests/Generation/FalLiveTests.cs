using System.Net;
using System.Net.Http.Headers;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The fal backend against fal's OWN queue (<c>queue.fal.run</c>, <c>Authorization: Key …</c>).
///
/// <para>Skipped without <c>FAL_KEY</c>. <b>A run is twenty to forty submits at about a third of a cent each</b> —
/// the cheapest image model fal lists ($0.003 per megapixel, billed rounded up to one) at its smallest preset, some
/// of them cancelled or failing before they render (fal reports no cost, so what a failure bills is not measured) —
/// <b>and the video test, which needs <c>FAL_LIVE_VIDEO=1</c> as well, is one more at $0.02</b>. So this is a
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

    private readonly HashSet<string> _distinct = [];

    private FalProvider Provider(bool distinctOnly = false) =>
        new(new FalOptions { ApiKey = Key }, () => new HttpClient(new WireLog(_wire, distinctOnly ? _distinct : null)));

    /// <summary>One client for every call, so a connection is reused: a fresh TLS handshake a call is slower than
    /// a small render, which is gone from the queue before a status read or a cancel can reach it.</summary>
    private FalProvider KeptAlive(bool distinctOnly = false)
    {
        var client = new HttpClient(new WireLog(_wire, distinctOnly ? _distinct : null));
        return new FalProvider(new FalOptions { ApiKey = Key }, () => client, disposeHttpClient: false);
    }

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

    /// <summary>A poll loop: every <paramref name="every"/> until the operation leaves Queued and Running or the
    /// <paramref name="budget"/> ends, handing each answer to <paramref name="seen"/>.</summary>
    private static async Task<QueuedOperation> PollToEnd(FalProvider provider, string id, TimeSpan every,
        TimeSpan budget, Action<QueuedOperation>? seen = null)
    {
        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            var polled = await provider.PollAsync(id);   // the first read is immediate: a fast render is gone by the second
            seen?.Invoke(polled);
            if (polled.Status is not (QueuedOperationStatus.Queued or QueuedOperationStatus.Running) ||
                DateTime.UtcNow >= deadline) return polled;
            await Task.Delay(every);
        }
    }

    private static MediaRequest Square(string prompt, params (string Key, string Value)[] options) => new()
    {
        Kind = ProviderKinds.Image,
        Model = Cheapest,
        Prompt = prompt,
        Options = options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase),
    };

    [SkippableFact]
    public async Task A_status_read_moves_from_IN_QUEUE_with_a_position_through_IN_PROGRESS_to_COMPLETED()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), NoKey);
        var provider = KeptAlive(distinctOnly: true);
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();

        try
        {
            // eight at once, each read the moment it is submitted: a small render is gone from the queue in under a
            // second, so a status read catches a request in each state only if it is not late
            var ends = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                var submitted = await provider.SubmitAsync(Square("a red square on a white background",
                    ("image_size", "square"), ("num_inference_steps", "4")));
                SkipWhenLocked(submitted);
                Assert.True(submitted.Status == QueuedOperationStatus.Queued, $"{submitted.Verdict}: {submitted.Detail}");
                return await PollToEnd(provider, submitted.Id, TimeSpan.FromMilliseconds(50), TimeSpan.FromMinutes(2),
                    polled => seen.Add($"{polled.Status}|{polled.Detail}"));
            }));
            output.WriteLine($"distinct (status|detail) answers: {string.Join(", ", seen.Distinct().Order())}");

            Assert.All(ends, e => Assert.Equal(QueuedOperationStatus.Succeeded, e.Status));
            Assert.Contains(seen, s => s.StartsWith("Running|"));   // IN_PROGRESS maps to Running
            // IN_QUEUE on a status read, with its queue_position, is a race against a warm worker: reported, never asserted
            output.WriteLine(seen.Any(s => s.StartsWith("Queued|queue position "))
                ? "a status read caught IN_QUEUE with its position"
                : "no status read caught IN_QUEUE: every render started first");
        }
        finally { Dump(); }
    }

    [SkippableFact]
    public async Task A_cancel_is_a_request_that_lands_on_a_queued_render_as_a_COMPLETED_status_carrying_an_error()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), NoKey);
        var provider = KeptAlive();

        try
        {
            // a cancel of a render that already finished is refused, and still reports Running: only a poll tells
            var done = await provider.SubmitAsync(Square("a yellow star on a white background",
                ("image_size", "square"), ("num_inference_steps", "4")));
            SkipWhenLocked(done);
            var finished = await PollToEnd(provider, done.Id, TimeSpan.FromMilliseconds(300), TimeSpan.FromMinutes(1));
            Assert.Equal(QueuedOperationStatus.Succeeded, finished.Status);
            var late = await provider.CancelAsync(done.Id);
            output.WriteLine($"cancel of a finished request: {late.Status}: {late.Detail}");
            Assert.Equal(QueuedOperationStatus.Running, late.Status);
            Assert.Contains("400", late.Detail);

            // a lone render starts before its cancel arrives, so eight go in at once and each is cancelled the moment
            // it is submitted: whether one is still queued is a race against a warm worker, so up to three rounds
            QueuedOperation? landed = null;
            for (var round = 1; round <= 3 && landed is null; round++)
            {
                var cancels = new System.Collections.Concurrent.ConcurrentBag<string>();
                var ends = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
                {
                    var submitted = await provider.SubmitAsync(Square("a blue circle on a white background",
                        ("image_size", "square"), ("num_inference_steps", "4")));
                    Assert.True(submitted.Status == QueuedOperationStatus.Queued, $"{submitted.Verdict}: {submitted.Detail}");
                    var cancelled = await provider.CancelAsync(submitted.Id);
                    Assert.Equal(QueuedOperationStatus.Running, cancelled.Status);   // a request, never a stop
                    cancels.Add(cancelled.Detail!);
                    return await PollToEnd(provider, submitted.Id, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1));
                }));
                output.WriteLine($"round {round}: cancel answers {string.Join(" | ", cancels.Distinct())}");
                Assert.Contains(cancels, c => c.StartsWith("cancellation requested (CANCELLATION_REQUESTED)"));
                output.WriteLine($"round {round}: after a cancel of each: {string.Join(" | ", ends.Select(e => $"{e.Status} {e.Detail}"))}");
                landed = ends.FirstOrDefault(e => e.Status == QueuedOperationStatus.Failed);
            }
            Skip.If(landed is null, "no cancel landed in three rounds: every render started before its cancel arrived");

            Assert.Contains("client_cancelled", landed!.Detail);   // ErrorField and ErrorTypeField, on the status document
            var fetched = await provider.FetchAsync(landed.Id);
            output.WriteLine($"fetch after a landed cancel: {fetched.Verdict}: {fetched.Detail}");
            Assert.False(fetched.IsOk);
            Assert.Contains("499", fetched.Detail);
        }
        finally { Dump(); }
    }

    [SkippableFact]
    public async Task An_object_valued_field_is_sent_as_an_object_when_named_and_an_unnamed_one_fails_at_the_fetch()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), NoKey);
        var named = new FalOptions { ApiKey = Key, JsonFields = ["image_size"] };
        var provider = new FalProvider(named, () => new HttpClient(new WireLog(_wire)));
        try
        {
            var submitted = await provider.SubmitAsync(Square("a green triangle on a white background",
                ("image_size", """{"width":512,"height":384}"""), ("num_inference_steps", "4"), ("output_format", "png")));
            SkipWhenLocked(submitted);
            Assert.True(submitted.Status == QueuedOperationStatus.Queued, $"{submitted.Verdict}: {submitted.Detail}");
            var end = await PollToEnd(provider, submitted.Id, TimeSpan.FromMilliseconds(300), TimeSpan.FromMinutes(1));
            Assert.Equal(QueuedOperationStatus.Succeeded, end.Status);
            var fetched = await provider.FetchAsync(submitted.Id);
            Assert.True(fetched.IsOk, $"{fetched.Verdict}: {fetched.Detail}");
            using var cdn = new HttpClient();
            Assert.Equal((512, 384), LivePng.Dimensions(await cdn.GetByteArrayAsync(Assert.Single(fetched.Artifacts).Uri)));

            // the same value as a string, with the field unnamed: accepted, then refused at the fetch
            var control = await Provider().SubmitAsync(Square("a green triangle on a white background",
                ("image_size", """{"width":512,"height":384}"""), ("num_inference_steps", "4")));
            Assert.True(control.Status == QueuedOperationStatus.Queued, $"{control.Verdict}: {control.Detail}");
            var controlEnd = await PollToEnd(Provider(), control.Id, TimeSpan.FromMilliseconds(300), TimeSpan.FromMinutes(1));
            var controlFetch = await Provider().FetchAsync(control.Id);
            output.WriteLine($"control: poll {controlEnd.Status}; fetch {controlFetch.Verdict}: {controlFetch.Detail}");
            Assert.False(controlFetch.IsOk);
            Assert.Contains("model_attributes_type", controlFetch.Detail);
        }
        finally { Dump(); }
    }

    /// <summary>The cheapest video model on fal's price list: <b>US$0.02 a video</b> by its list price (fal's
    /// pricing API, read before the run), one submit. Gated apart from the image tests because it is six times
    /// their cost: set <c>FAL_LIVE_VIDEO=1</c> as well.</summary>
    private const string CheapestVideo = "fal-ai/ltx-video";

    [SkippableFact]
    public async Task The_cheapest_video_model_renders_through_the_same_queue_and_its_result_is_a_video_artifact()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), NoKey);
        Skip.If(Environment.GetEnvironmentVariable("FAL_LIVE_VIDEO") != "1",
            "set FAL_LIVE_VIDEO=1 as well: one video submit, US$0.02 by fal's list price");
        var provider = Provider(distinctOnly: true);

        var submitted = await provider.SubmitAsync(new MediaRequest
        {
            Kind = ProviderKinds.Video,
            Model = CheapestVideo,
            Prompt = "a paper boat drifting on a calm pond at dawn",
        });
        try
        {
            SkipWhenLocked(submitted);
            Assert.True(submitted.Status == QueuedOperationStatus.Queued, $"{submitted.Verdict}: {submitted.Detail}");

            var sequence = new List<string>();
            var started = DateTime.UtcNow;
            var end = await PollToEnd(provider, submitted.Id, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(10), polled =>
            {
                var tag = $"{polled.Status}|{polled.Detail}";
                if (sequence.Count == 0 || sequence[^1] != tag) sequence.Add($"{(DateTime.UtcNow - started).TotalSeconds:0}s {tag}");
            });
            output.WriteLine($"poll sequence: {string.Join(" ; ", sequence)}");
            Assert.Equal(QueuedOperationStatus.Succeeded, end.Status);

            var fetched = await provider.FetchAsync(submitted.Id);
            output.WriteLine($"fetch: {fetched.Verdict} {string.Join(", ", fetched.Artifacts.Select(a => $"{a.MediaType} {a.Uri}"))} cost {fetched.Usage?.CostUsd?.ToString() ?? "none"}");
            Assert.True(fetched.IsOk, $"{fetched.Verdict}: {fetched.Detail}");
            var artifact = Assert.Single(fetched.Artifacts);
            Assert.StartsWith("video/", artifact.MediaType);
            using var cdn = new HttpClient();
            using var head = await cdn.SendAsync(new HttpRequestMessage(HttpMethod.Head, artifact.Uri));
            output.WriteLine($"artifact HEAD: {(int)head.StatusCode} {head.Content.Headers.ContentType} {head.Content.Headers.ContentLength} bytes");
            Assert.True(head.IsSuccessStatusCode);
        }
        finally { Dump(); }
    }

    /// <summary>Records method, URL, status and body of every exchange — no request header, so no credential.
    /// <paramref name="distinct"/>, when given, keeps one line per method, status and opening of the body, so a
    /// poll loop logs each state once; it is shared because the provider makes a handler per call.</summary>
    private sealed class WireLog(List<string> lines, HashSet<string>? distinct = null) : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sent = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var response = await base.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var line = $"{request.Method} {request.RequestUri} {sent}\n  -> {(int)response.StatusCode} " +
                       $"{(body.Length > 1500 ? body[..1500] + "…" : body)}";
            lock (lines)
            {
                var key = $"{request.Method} {(int)response.StatusCode} {body[..Math.Min(40, body.Length)]}";
                if (distinct is null || distinct.Add(key)) lines.Add(line);
            }
            return response;
        }
    }
}
