using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Jobs;

/// <summary>One reported step of a running job — a timestamped progress message, with the code and arguments a
/// reader can localize it by when the reporter gave them (<see cref="JobMessage"/>).</summary>
public sealed record JobStep(DateTimeOffset At, string Message)
{
    /// <summary>What the step is, when reported as a coded <see cref="JobMessage"/>; null for a plain line.</summary>
    public string? Code { get; init; }

    /// <summary>The values the localized form fills in; null for a plain line.</summary>
    public IReadOnlyDictionary<string, string>? Arguments { get; init; }
}

/// <summary>
/// The persisted step log of a job — a JSON array of <see cref="JobStep"/>s, appended by
/// <c>IJobStore.ReportStepAsync</c> and stored in the <c>step_log</c> column. A pure (no-I/O) helper so
/// all three storage backends share one format and the app can parse a job's <c>StepLog</c> the same way.
/// The log is capped to the most recent <see cref="DefaultCap"/> entries so a long-running job can't grow
/// the row unbounded. A plain step is <c>{"at","msg"}</c>; a coded one adds <c>code</c> and <c>args</c>.
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
            var message = JobMessageJson.Read(msg, o);
            steps.Add(new JobStep(at, msg) { Code = message.Code, Arguments = message.Arguments });
        }
        return steps;
    }

    /// <summary>Append a step to the stored log, capping to the most recent <paramref name="cap"/> entries,
    /// and return the new JSON to persist.</summary>
    public static string Append(string? json, string message, DateTimeOffset at, int cap = DefaultCap) =>
        Append(json, new JobMessage(message), at, cap);

    /// <summary>Append a coded step — its text, code and arguments — capping as the plain overload does.</summary>
    public static string Append(string? json, JobMessage message, DateTimeOffset at, int cap = DefaultCap)
    {
        ArgumentNullException.ThrowIfNull(message);
        var steps = Parse(json).ToList();
        steps.Add(new JobStep(at, message.Text) { Code = message.Code, Arguments = message.Arguments });
        if (steps.Count > cap) steps.RemoveRange(0, steps.Count - cap);

        // build via the params constructor (not Add<T>, which warns on non-primitive JsonValue creation)
        var nodes = new JsonNode?[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var entry = new JsonObject { ["at"] = steps[i].At, ["msg"] = steps[i].Message };
            JobMessageJson.Fill(entry, new JobMessage(steps[i].Message) { Code = steps[i].Code, Arguments = steps[i].Arguments });
            nodes[i] = entry;
        }
        return new JsonArray(nodes).ToJsonString();
    }
}
