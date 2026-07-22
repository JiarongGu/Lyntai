using System.Text;
using System.Text.Json;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Jobs;

/// <summary>Payload for the memory-prune job: which task to prune (null = all tasks) and the age cutoff
/// (null = reap only expired entries). JSON build/parse is manual (JsonDocument / Utf8JsonWriter) so Core
/// stays AOT/trim-clean — no reflection serializer.</summary>
internal sealed record MemoryPruneRequest(string? TaskKey = null, double? OlderThanSeconds = null)
{
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (TaskKey is not null) w.WriteString("TaskKey", TaskKey);
            if (OlderThanSeconds is { } s) w.WriteNumber("OlderThanSeconds", s);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static MemoryPruneRequest Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return new MemoryPruneRequest();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new MemoryPruneRequest();
            var taskKey = root.TryGetProperty("TaskKey", out var tk) && tk.ValueKind == JsonValueKind.String ? tk.GetString() : null;
            double? older = root.TryGetProperty("OlderThanSeconds", out var os) && os.ValueKind == JsonValueKind.Number ? os.GetDouble() : null;
            return new MemoryPruneRequest(taskKey, older);
        }
        catch (JsonException)
        {
            return new MemoryPruneRequest();
        }
    }
}

/// <summary>Durable-job handler that reaps expired (and, when a cutoff is given, aged-out) memory via
/// <see cref="IMemoryStore.PruneAsync"/> — the opt-in background GC for cold/expired entries that on-write
/// eviction can't reach (a cold <c>(taskKey, scope)</c> is never re-evicted). Registered by
/// <c>AddMemoryPruneJob</c>; the app owns the pump. Idempotent (deleting already-gone rows is a no-op), so
/// the at-least-once job contract is satisfied.</summary>
internal sealed class MemoryPruneJobHandler(IMemoryStore memory, ILogger<MemoryPruneJobHandler>? logger = null) : IJobHandler
{
    /// <summary>The durable-job <see cref="IJobHandler.Type"/> / <see cref="JobSpec.Type"/> for prune jobs.</summary>
    public const string JobType = "lyntai.memory.prune";

    private readonly ILogger _logger = logger ?? NullLogger<MemoryPruneJobHandler>.Instance;

    public string Type => JobType;

    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        var req = MemoryPruneRequest.Parse(ctx.Payload);
        var taskKey = string.IsNullOrEmpty(req.TaskKey) ? null : req.TaskKey;
        var olderThan = req.OlderThanSeconds is > 0 ? TimeSpan.FromSeconds(req.OlderThanSeconds.Value) : (TimeSpan?)null;

        var removed = await memory.PruneAsync(taskKey, olderThan, ct).ConfigureAwait(false);
        _logger.LogInformation("memory-prune reaped {Count} entries (taskKey={TaskKey}, olderThan={OlderThan})",
            removed, taskKey ?? "*", olderThan);
        return JobOutcome.Complete;
    }
}
