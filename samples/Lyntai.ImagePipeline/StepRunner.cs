using System.Diagnostics;
using Lyntai.Generation.Providers;
using Lyntai.Inference;

namespace Lyntai.ImagePipeline;

/// <summary>One step's result: every image the graph saved, by node id, and what the run cost.</summary>
public sealed record StepRun(IReadOnlyDictionary<string, byte[]> ImagesByNode, double Seconds, GpuUse? Gpu);

/// <summary>Submits a filled request to <see cref="ComfyUiProvider"/>, polls it to the end, fetches the view URIs it
/// answers and downloads each image — the host's side of a queued backend. A failure at any point throws with the
/// provider's own words.</summary>
public sealed class StepRunner(
    ComfyUiProvider provider, HttpClient http, TimeSpan? pollEvery = null, TimeSpan? budget = null, bool measureGpu = true)
{
    private readonly TimeSpan _pollEvery = pollEvery ?? TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan _budget = budget ?? TimeSpan.FromMinutes(20);

    public async Task<StepRun> RunAsync(MediaRequest request, CancellationToken ct = default)
    {
        using var gpu = measureGpu ? GpuSampler.TryStart() : null;
        var clock = Stopwatch.StartNew();

        var submitted = await provider.SubmitAsync(request, ct);
        if (submitted.Status != QueuedOperationStatus.Queued)
            throw new InvalidOperationException($"submit {submitted.Status}: {submitted.Detail}");

        var deadline = DateTime.UtcNow + _budget;
        QueuedOperation polled;
        do
        {
            await Task.Delay(_pollEvery, ct);
            polled = await provider.PollAsync(submitted.Id, ct);
            if (polled.Status is QueuedOperationStatus.Failed or QueuedOperationStatus.Cancelled)
                throw new InvalidOperationException($"the run {polled.Status}: {polled.Detail}");
        }
        while (polled.Status != QueuedOperationStatus.Succeeded && DateTime.UtcNow < deadline);
        if (polled.Status != QueuedOperationStatus.Succeeded)
            throw new TimeoutException($"not finished inside {_budget}: {polled.Status} {polled.Detail}");
        var seconds = clock.Elapsed.TotalSeconds;

        var fetched = await provider.FetchAsync(submitted.Id, ct);
        if (!fetched.IsOk) throw new InvalidOperationException($"fetch {fetched.Verdict}: {fetched.Detail}");

        var images = new Dictionary<string, byte[]>();
        foreach (var artifact in fetched.Artifacts.Where(a => a.MediaType == "image/png"))
        {
            var node = artifact.Metadata?["node"] ?? throw new InvalidOperationException("an artifact carries no node id");
            images[node] = artifact.Data ?? await http.GetByteArrayAsync(artifact.Uri, ct);
        }
        return new StepRun(images, seconds, gpu?.Stop());
    }
}

/// <summary>The GPU's memory use while a step ran, in MiB, as <c>nvidia-smi</c> reports the whole card — other
/// processes' memory included, so <see cref="Resident"/> (what was in use before the step) is kept beside the peak.</summary>
public sealed record GpuUse(long Resident, long Peak);

/// <summary>Polls <c>nvidia-smi</c> every 200 ms for the card's used memory. Best effort: no <c>nvidia-smi</c>, no reading.</summary>
public sealed class GpuSampler : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task<long>? _peak;
    private long _resident = -1;

    public static GpuSampler? TryStart()
    {
        var sampler = new GpuSampler();
        sampler._resident = Read();
        if (sampler._resident < 0) return null;
        sampler._peak = Task.Run(() =>
        {
            var peak = sampler._resident;
            while (!sampler._stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, Read());
                try { Task.Delay(200, sampler._stop.Token).Wait(); } catch (AggregateException) { }
            }
            return peak;
        });
        return sampler;
    }

    public GpuUse Stop()
    {
        _stop.Cancel();
        return new GpuUse(_resident, Math.Max(_peak!.Result, Read()));
    }

    public void Dispose() => _stop.Dispose();

    /// <summary>Used memory in MiB, or -1 when it cannot be read.</summary>
    private static long Read()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            });
            var line = process?.StandardOutput.ReadLine();
            process?.WaitForExit(2000);
            return long.TryParse(line?.Trim(), out var used) ? used : -1;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }
}
