using Lyntai.Jobs;

namespace Lyntai.Storage.InMemory;

/// <summary>In-memory <see cref="IJobStore"/>. Claim/lease/fencing semantics mirror the SQL backends,
/// under one lock. (Durable across restarts only insofar as the process lives — for tests and ephemeral
/// use; use SQLite/Postgres for real durability.)</summary>
public sealed class InMemoryJobStore(Func<DateTimeOffset>? clock = null, int stepLogCap = JobStepLog.DefaultCap) : IJobStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, JobRecord> _jobs = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default)
    {
        var now = _clock();
        var id = Guid.NewGuid();
        var rec = new JobRecord(id, spec.Lane, spec.Type, spec.Payload, JobStatus.Pending, Checkpoint: null,
            Attempts: 0, MaxAttempts: spec.MaxAttempts ?? 3, LastError: null,
            AvailableAt: spec.AvailableAt ?? now, ClaimedAt: null, ClaimedBy: null, CreatedAt: now, UpdatedAt: now,
            Priority: spec.Priority);
        lock (_lock) _jobs[id] = rec;
        return Task.FromResult(id);
    }

    public Task<JobRecord?> ClaimNextAsync(string lane, string workerId, TimeSpan lease, CancellationToken ct = default)
    {
        var now = _clock();
        var staleBefore = now - lease;
        lock (_lock)
        {
            var candidate = _jobs.Values
                .Where(j => j.Lane == lane &&
                    ((j.Status == JobStatus.Pending && j.AvailableAt <= now) ||
                     (j.Status == JobStatus.Running && j.ClaimedAt is { } ca && ca < staleBefore)))
                // tiebreak by id to match the SQL stores' `ORDER BY … available_at, id` (id is a TEXT
                // Guid there, so compare the string form ordinally, not the Guid's own byte order)
                .OrderByDescending(j => j.Priority).ThenBy(j => j.AvailableAt)
                .ThenBy(j => j.Id.ToString(), StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is null) return Task.FromResult<JobRecord?>(null);

            var claimed = candidate with
            {
                Status = JobStatus.Running, ClaimedBy = workerId, ClaimedAt = now,
                Attempts = candidate.Attempts + 1, UpdatedAt = now,
            };
            _jobs[claimed.Id] = claimed;
            return Task.FromResult<JobRecord?>(claimed);
        }
    }

    public Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = j with { Checkpoint = checkpoint, ClaimedAt = now, UpdatedAt = now }; // renew lease
            return Task.FromResult(true);
        }
    }

    public Task<bool> ReportProgressAsync(Guid id, string workerId, int done, int total, string? stage, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = j with { Progress = done, Total = total, Stage = stage, UpdatedAt = now }; // NOT a lease renewal
            return Task.FromResult(true);
        }
    }

    public Task<bool> ReportStepAsync(Guid id, string workerId, string message, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = j with { StepLog = JobStepLog.Append(j.StepLog, message, now, stepLogCap), UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> CompleteAsync(Guid id, string workerId, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = j with { Status = JobStatus.Succeeded, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> FailAsync(Guid id, string workerId, string error, DateTimeOffset? retryAt = null, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = retryAt is { } at
                ? j with { Status = JobStatus.Pending, AvailableAt = at, LastError = error, ClaimedBy = null, ClaimedAt = null, UpdatedAt = now }
                : j with { Status = JobStatus.Failed, LastError = error, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeadLetterAsync(Guid id, string workerId, string error, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = j with { Status = JobStatus.Dead, LastError = error, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> ReplayAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var j) || j.Status is not (JobStatus.Dead or JobStatus.Failed))
                return Task.FromResult(false);
            _jobs[id] = j with
            {
                Status = JobStatus.Pending, Attempts = 0, LastError = null, AvailableAt = now,
                ClaimedBy = null, ClaimedAt = null, UpdatedAt = now, CancelRequested = false,
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var j) || j.Status != JobStatus.Pending) return Task.FromResult(false);
            _jobs[id] = j with { Status = JobStatus.Cancelled, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> PauseAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var j) || j.Status != JobStatus.Pending) return Task.FromResult(false);
            _jobs[id] = j with { Status = JobStatus.Paused, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> ResumeAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var j) || j.Status != JobStatus.Paused) return Task.FromResult(false);
            _jobs[id] = j with { Status = JobStatus.Pending, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> RequestCancelAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!_jobs.TryGetValue(id, out var j) || j.Status != JobStatus.Running) return Task.FromResult(false);
            _jobs[id] = j with { CancelRequested = true, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> CancelRunningAsync(Guid id, string workerId, CancellationToken ct = default)
    {
        var now = _clock();
        lock (_lock)
        {
            if (!Owned(id, workerId, out var j)) return Task.FromResult(false);
            _jobs[id] = j with { Status = JobStatus.Cancelled, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<int> CountRunningAsync(string lane, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_jobs.Values.Count(j => j.Lane == lane && j.Status == JobStatus.Running));
    }

    public Task<IReadOnlyList<string>> ActiveLanesAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<string> lanes =
                [.. _jobs.Values.Where(j => j.Status is JobStatus.Pending or JobStatus.Running)
                    .Select(j => j.Lane).Distinct().OrderBy(l => l, StringComparer.Ordinal)];
            return Task.FromResult(lanes);
        }
    }

    public Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_jobs.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<JobRecord> result =
            [
                .. _jobs.Values
                    .Where(j => (status is null || j.Status == status) && (lane is null || j.Lane == lane))
                    // ordinal Id tiebreak to match the SQL stores' TEXT `id` ordering (and ClaimNextAsync) —
                    // ThenByDescending(j.Id) sorts by Guid.CompareTo (per-field), a different order
                    .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id.ToString(), StringComparer.Ordinal)
                    .Take(limit)
            ];
            return Task.FromResult(result);
        }
    }

    // fencing: a mutating write only lands if THIS worker still holds the Running claim
    private bool Owned(Guid id, string workerId, out JobRecord job)
    {
        job = default!;
        if (!_jobs.TryGetValue(id, out var j) || j.Status != JobStatus.Running || j.ClaimedBy != workerId) return false;
        job = j;
        return true;
    }
}
