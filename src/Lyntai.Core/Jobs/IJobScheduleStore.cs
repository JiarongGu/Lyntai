using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Jobs;

/// <summary>The schedules added at run time. <see cref="JobScheduler"/> lists them on EVERY tick, after the schedules
/// registered at build time — which win a name clash — so a change takes effect on the next tick. App code adds,
/// replaces and removes through the rest. One optional registration: <c>AddJobScheduleStore()</c> for the shipped
/// <see cref="KeyValueJobScheduleStore"/>, <c>AddJobScheduleStore&lt;TStore&gt;()</c> for one of the app's own.
///
/// <para><b>An implementation of the app's own</b> whose schedules are edited elsewhere may throw
/// <see cref="NotSupportedException"/> from the three writes. The scheduler checks every schedule it lists and skips
/// an invalid one with a warning, so a store that does not validate cannot break a tick.</para></summary>
public interface IJobScheduleStore
{
    /// <summary>Every stored schedule. Called on every scheduler tick, so keep it cheap.</summary>
    Task<IReadOnlyList<JobSchedule>> ListAsync(CancellationToken ct = default);

    /// <summary>The schedule of that name, or null.</summary>
    Task<JobSchedule?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Add a schedule, or replace the one of the same name. A changed cron or interval re-anchors the
    /// schedule's next run rather than firing once more at the old slot.</summary>
    Task SetAsync(JobSchedule schedule, CancellationToken ct = default);

    /// <summary>Remove the schedule of that name; true when there was one. The shipped store also clears the
    /// scheduler's record of its next run; a store of the app's own does not, so a schedule it removes and re-adds
    /// with an unchanged trigger resumes its old slot.</summary>
    Task<bool> RemoveAsync(string name, CancellationToken ct = default);
}

/// <summary>The shipped <see cref="IJobScheduleStore"/>, over <see cref="IKeyValueStore"/>: one key per schedule, so
/// it works on every storage backend and two writers of different schedules never race. <see cref="SetAsync"/>
/// validates as <c>AddJobSchedule</c> does; an entry that no longer reads as a valid schedule is skipped with one
/// warning, never thrown.</summary>
/// <param name="store">Where the schedules are kept.</param>
/// <param name="logger">Where a skipped entry is reported.</param>
public sealed class KeyValueJobScheduleStore(IKeyValueStore store, ILogger<KeyValueJobScheduleStore>? logger = null)
    : IJobScheduleStore
{
    private readonly ILogger _logger = logger ?? NullLogger<KeyValueJobScheduleStore>.Instance;
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobSchedule>> ListAsync(CancellationToken ct = default)
    {
        var schedules = new List<JobSchedule>();
        foreach (var key in await store.ListKeysAsync(JobScheduleRules.DefinitionPrefix, ct).ConfigureAwait(false))
            if (await ReadAsync(key, ct).ConfigureAwait(false) is { } schedule) schedules.Add(schedule);
        return schedules;
    }

    /// <inheritdoc/>
    public Task<JobSchedule?> GetAsync(string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ReadAsync(JobScheduleRules.DefinitionKey(name), ct);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The name is blank, or the schedule sets both triggers or neither.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="FormatException">The cron does not parse.</exception>
    public Task SetAsync(JobSchedule schedule, CancellationToken ct = default)
    {
        JobScheduleRules.Validate(schedule);
        var json = new JsonObject
        {
            ["name"] = schedule.Name,
            ["lane"] = schedule.Lane,
            ["type"] = schedule.Type,
            ["payload"] = schedule.Payload,
            ["priority"] = schedule.Priority,
        };
        if (schedule.Cron is { } cron) json["cron"] = cron;
        else json["interval"] = schedule.Interval!.Value.ToString("c", CultureInfo.InvariantCulture);
        return store.SetAsync(JobScheduleRules.DefinitionKey(schedule.Name), json.ToJsonString(), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var existed = await store.GetAsync(JobScheduleRules.DefinitionKey(name), ct).ConfigureAwait(false) is not null;
        await store.DeleteAsync(JobScheduleRules.DefinitionKey(name), ct).ConfigureAwait(false);
        await store.DeleteAsync(JobScheduleRules.NextRunKey(name), ct).ConfigureAwait(false);
        await store.DeleteAsync(JobScheduleRules.TriggerKey(name), ct).ConfigureAwait(false);
        return existed;
    }

    private async Task<JobSchedule?> ReadAsync(string key, CancellationToken ct)
    {
        var raw = await store.GetAsync(key, ct).ConfigureAwait(false);
        if (raw is null) return null;
        try
        {
            var o = JsonNode.Parse(raw)!.AsObject();
            var schedule = new JobSchedule(
                Text(o, "name"), Text(o, "lane"), Text(o, "type"), Text(o, "payload"),
                o["interval"] is { } interval
                    ? TimeSpan.ParseExact(interval.GetValue<string>(), "c", CultureInfo.InvariantCulture)
                    : null,
                o["priority"]?.GetValue<int>() ?? 0,
                o["cron"]?.GetValue<string>());
            JobScheduleRules.Validate(schedule);
            return schedule;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException
                                      or ArgumentException or NullReferenceException or OverflowException)
        {
            if (_warned.TryAdd(key, 0))
                _logger.LogWarning(e, "job schedules: skipping '{Key}' — it does not read as a valid schedule", key);
            return null;
        }
    }

    private static string Text(JsonObject o, string name) =>
        o[name]?.GetValue<string>() ?? throw new FormatException($"no '{name}'");
}
