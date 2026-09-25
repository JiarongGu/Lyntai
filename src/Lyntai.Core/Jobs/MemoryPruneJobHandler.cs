using System.Text.Json;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Jobs;

/// <summary>Payload for the memory-prune job: which task to prune (null = all tasks) and the age cutoff
/// (null = remove only expired entries). JSON build/parse is manual (JsonDocument / Utf8JsonWriter) so Core
/// stays AOT/trim-clean — no reflection serializer.</summary>
internal sealed record MemoryPruneRequest(string? TaskKey = null, double? OlderThanSeconds = null)
{
    public string ToJson() => JsonExtract.WriteObject(w =>
    {
        if (TaskKey is not null) w.WriteString("TaskKey", TaskKey);
        if (OlderThanSeconds is { } s) w.WriteNumber("OlderThanSeconds", s);
    });

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

/// <summary>Durable-job handler that removes expired (and, when a cutoff is given, aged-out) memory — the
/// opt-in background GC for cold material that on-write eviction never revisits. Registered by
/// <c>AddMemoryPruneJob</c>; the app owns the pump. Idempotent (deleting already-gone rows is a no-op), so the
/// at-least-once job contract is satisfied.
/// <para>Visits the keyword <see cref="IMemoryStore"/> and, for a job naming a task, every registered
/// <see cref="IMemoryEngine"/> that can prune (<see cref="IPrunableMemory"/>). An engine prunes within ONE task,
/// so an all-tasks job reaches the keyword store only. An engine that refuses the prune is logged and skipped,
/// so one blend cannot cost every other engine its prune.</para></summary>
internal sealed class MemoryPruneJobHandler(
    IMemoryStore? memory = null,
    IEnumerable<IMemoryEngine>? engines = null,
    ILogger<MemoryPruneJobHandler>? logger = null) : IJobHandler
{
    /// <summary>The durable-job <see cref="IJobHandler.Type"/> / <see cref="JobSpec.Type"/> for prune jobs.</summary>
    public const string JobType = "lyntai.memory.prune";

    private readonly ILogger _logger = logger ?? NullLogger<MemoryPruneJobHandler>.Instance;
    private readonly IReadOnlyList<IMemoryEngine> _engines = [.. engines ?? []];

    public string Type => JobType;

    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct = default)
    {
        var req = MemoryPruneRequest.Parse(ctx.Payload);
        var taskKey = string.IsNullOrEmpty(req.TaskKey) ? null : req.TaskKey;
        var olderThan = req.OlderThanSeconds is > 0 ? TimeSpan.FromSeconds(req.OlderThanSeconds.Value) : (TimeSpan?)null;

        var removed = memory is null ? 0 : await memory.PruneAsync(taskKey, olderThan, ct).ConfigureAwait(false);
        removed += await PruneEnginesAsync(taskKey, olderThan, ct).ConfigureAwait(false);

        _logger.LogInformation("memory-prune removed {Count} entries (taskKey={TaskKey}, olderThan={OlderThan})",
            removed, taskKey ?? "*", olderThan);
        return JobOutcome.Complete;
    }

    private async Task<int> PruneEnginesAsync(string? taskKey, TimeSpan? olderThan, CancellationToken ct)
    {
        var prunable = _engines.Where(e => e is IPrunableMemory).ToList();
        if (prunable.Count == 0) return 0;
        if (taskKey is null)
        {
            _logger.LogInformation(
                "memory-prune skipped {Count} engine(s) — an engine prunes within one task, and this job names " +
                "none: {Engines}", prunable.Count, string.Join(", ", prunable.Select(e => e.Name)));
            return 0;
        }

        var removed = 0;
        foreach (var engine in prunable)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                removed += await ((IPrunableMemory)engine)
                    .PruneAsync(taskKey, olderThan: olderThan, ct: ct).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "memory-prune skipped engine {Engine}: it cannot prune", engine.Name);
            }
        }
        return removed;
    }
}
