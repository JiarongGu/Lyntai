using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Jobs;

/// <summary>One reported step of a running job — a timestamped human-readable progress message.</summary>
public sealed record JobStep(DateTimeOffset At, string Message);

/// <summary>
/// The persisted step log of a job — a JSON array of <see cref="JobStep"/>s, appended by
/// <c>IJobStore.ReportStepAsync</c> and stored in the <c>step_log</c> column. A pure (no-I/O) helper so
/// all three storage backends share one format and the app can parse a job's <c>StepLog</c> the same way.
/// The log is capped to the most recent <see cref="DefaultCap"/> entries so a long-running job can't grow
/// the row unbounded.
/// </summary>
public static class JobStepLog
{
    /// <summary>Default max retained steps (oldest dropped past this).</summary>
    public const int DefaultCap = 200;

    /// <summary>Parse a stored step-log JSON into steps (oldest first). Returns empty on null/blank/malformed.</summary>
    public static IReadOnlyList<JobStep> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        JsonArray arr;
        try { arr = JsonNode.Parse(json) as JsonArray ?? []; }
        catch (JsonException) { return []; }

        var steps = new List<JobStep>(arr.Count);
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var msg = o["msg"]?.GetValue<string>();
            if (msg is null) continue;
            var at = o["at"]?.GetValue<DateTimeOffset>() ?? default;
            steps.Add(new JobStep(at, msg));
        }
        return steps;
    }

    /// <summary>Append a step to the stored log, capping to the most recent <paramref name="cap"/> entries,
    /// and return the new JSON to persist.</summary>
    public static string Append(string? json, string message, DateTimeOffset at, int cap = DefaultCap)
    {
        var steps = Parse(json).ToList();
        steps.Add(new JobStep(at, message));
        if (steps.Count > cap) steps.RemoveRange(0, steps.Count - cap);

        // build via the params constructor (not Add<T>, which warns on non-primitive JsonValue creation)
        var nodes = new JsonNode?[steps.Count];
        for (var i = 0; i < steps.Count; i++)
            nodes[i] = new JsonObject { ["at"] = steps[i].At, ["msg"] = steps[i].Message };
        return new JsonArray(nodes).ToJsonString();
    }
}
