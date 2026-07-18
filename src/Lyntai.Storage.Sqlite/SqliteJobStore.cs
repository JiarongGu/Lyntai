using Dapper;
using Lyntai.Jobs;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite <see cref="IJobStore"/>. The claim is a SINGLE <c>UPDATE … RETURNING</c> (never
/// select-then-update) — under WAL + busy_timeout SQLite is single-writer, so two claimers can't grab the
/// same row (the second blocks then re-evaluates against committed state). Reclaim of a crashed worker is
/// folded into the claim predicate (stale lease). Mutating writes are fenced by <c>claimed_by</c> and
/// report rows-affected. Timestamps/id/status are TEXT (no new Dapper type handler → no process-global
/// registry collision; TEXT timestamps compare chronologically because the ISO format is sortable).
/// </summary>
public sealed class SqliteJobStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null) : IJobStore
{
    private const string Cols =
        "id, lane, type, payload, status, checkpoint, attempts, max_attempts, last_error, " +
        "available_at, claimed_at, claimed_by, created_at, updated_at, priority, cancel_requested, " +
        "progress, total, stage, step_log";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    // ReportStepAsync is a read-modify-write on step_log (no single-statement atomic append with the cap),
    // so serialize concurrent step reports for this store — matching InMemoryJobStore, whose append is under
    // its lock. (A single running job is owned by one worker via fencing, so per-store serialization suffices.)
    private readonly SemaphoreSlim _stepLock = new(1, 1);

    public async Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default)
    {
        var now = _clock();
        var id = Guid.NewGuid();
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO lyntai_job ({Cols})
            VALUES (@id, @lane, @type, @payload, 'Pending', NULL, 0, @maxAttempts, NULL, @availableAt, NULL, NULL, @now, @now, @priority, 0, 0, 0, NULL, NULL)
            """, new
        {
            id = id.ToString(), lane = spec.Lane, type = spec.Type, payload = spec.Payload,
            maxAttempts = spec.MaxAttempts ?? 3, availableAt = spec.AvailableAt ?? now, now, priority = spec.Priority,
        }, cancellationToken: ct)).ConfigureAwait(false);
        return id;
    }

    public async Task<JobRecord?> ClaimNextAsync(string lane, string workerId, TimeSpan lease, CancellationToken ct = default)
    {
        var now = _clock();
        var staleBefore = now - lease;
        using var conn = factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition($"""
            UPDATE lyntai_job
            SET status='Running', claimed_by=@workerId, claimed_at=@now, attempts=attempts+1, updated_at=@now
            WHERE id = (
                SELECT id FROM lyntai_job
                WHERE lane=@lane
                  AND ((status='Pending' AND available_at<=@now)
                    OR (status='Running' AND claimed_at<@staleBefore))
                ORDER BY priority DESC, available_at, id LIMIT 1)
            RETURNING {Cols}
            """, new { lane, workerId, now, staleBefore }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default) =>
        Fenced("SET checkpoint=@checkpoint, claimed_at=@now, updated_at=@now", id, workerId, ct, new { checkpoint });

    public Task<bool> ReportProgressAsync(Guid id, string workerId, int done, int total, string? stage, CancellationToken ct = default) =>
        Fenced("SET progress=@done, total=@total, stage=@stage, updated_at=@now", id, workerId, ct, new { done, total, stage });

    public async Task<bool> ReportStepAsync(Guid id, string workerId, string message, CancellationToken ct = default)
    {
        // read-modify-write the capped JSON step log under _stepLock so concurrent reports don't clobber each
        // other; the fenced write drops it if the lease was lost
        await _stepLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await GetAsync(id, ct).ConfigureAwait(false);
            if (current is null || current.Status != JobStatus.Running || current.ClaimedBy != workerId) return false;
            var stepLog = JobStepLog.Append(current.StepLog, message, _clock());
            return await Fenced("SET step_log=@stepLog, updated_at=@now", id, workerId, ct, new { stepLog }).ConfigureAwait(false);
        }
        finally { _stepLock.Release(); }
    }

    public Task<bool> CompleteAsync(Guid id, string workerId, CancellationToken ct = default) =>
        Fenced("SET status='Succeeded', updated_at=@now", id, workerId, ct);

    public Task<bool> FailAsync(Guid id, string workerId, string error, DateTimeOffset? retryAt = null, CancellationToken ct = default) =>
        retryAt is { } at
            ? Fenced("SET status='Pending', available_at=@retryAt, last_error=@error, claimed_by=NULL, claimed_at=NULL, updated_at=@now", id, workerId, ct, new { error, retryAt = at })
            : Fenced("SET status='Failed', last_error=@error, updated_at=@now", id, workerId, ct, new { error });

    public Task<bool> DeadLetterAsync(Guid id, string workerId, string error, CancellationToken ct = default) =>
        Fenced("SET status='Dead', last_error=@error, updated_at=@now", id, workerId, ct, new { error });

    public async Task<bool> ReplayAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_job
            SET status='Pending', attempts=0, last_error=NULL, available_at=@now, claimed_by=NULL, claimed_at=NULL,
                cancel_requested=0, updated_at=@now
            WHERE id=@id AND status IN ('Dead','Failed')
            """, new { id = id.ToString(), now }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<bool> PauseAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_job SET status='Paused', updated_at=@now WHERE id=@id AND status='Pending'",
            new { id = id.ToString(), now }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<bool> ResumeAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_job SET status='Pending', updated_at=@now WHERE id=@id AND status='Paused'",
            new { id = id.ToString(), now }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<bool> RequestCancelAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_job SET cancel_requested=1, updated_at=@now WHERE id=@id AND status='Running'",
            new { id = id.ToString(), now }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public Task<bool> CancelRunningAsync(Guid id, string workerId, CancellationToken ct = default) =>
        Fenced("SET status='Cancelled', updated_at=@now", id, workerId, ct);

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_job SET status='Cancelled', updated_at=@now WHERE id=@id AND status='Pending'",
            new { id = id.ToString(), now }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<int> CountRunningAsync(string lane, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM lyntai_job WHERE lane=@lane AND status='Running'",
            new { lane }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ActiveLanesAsync(CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var lanes = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT lane FROM lyntai_job WHERE status IN ('Pending','Running') ORDER BY lane",
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. lanes];
    }

    public async Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_job WHERE id=@id", new { id = id.ToString() }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_job
            WHERE (@status IS NULL OR status=@status) AND (@lane IS NULL OR lane=@lane)
            ORDER BY created_at DESC, id DESC LIMIT @limit
            """, new { status = status?.ToString(), lane, limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToRecord())];
    }

    /// <summary>A fenced write: only lands while THIS worker holds the Running claim; returns rows-affected>0.</summary>
    private async Task<bool> Fenced(string setClause, Guid id, string workerId, CancellationToken ct, object? extra = null)
    {
        var now = _clock();
        var p = new DynamicParameters(new { id = id.ToString(), workerId, now });
        if (extra is not null) p.AddDynamicParams(extra);
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE lyntai_job {setClause} WHERE id=@id AND claimed_by=@workerId AND status='Running'",
            p, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Lane { get; set; } = "";
        public string Type { get; set; } = "";
        public string Payload { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Checkpoint { get; set; }
        public long Attempts { get; set; }
        public long MaxAttempts { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset AvailableAt { get; set; }
        public DateTimeOffset? ClaimedAt { get; set; }
        public string? ClaimedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public long Priority { get; set; }
        public bool CancelRequested { get; set; }
        public long Progress { get; set; }
        public long Total { get; set; }
        public string? Stage { get; set; }
        public string? StepLog { get; set; }

        public JobRecord ToRecord() => new(Guid.Parse(Id), Lane, Type, Payload, Enum.Parse<JobStatus>(Status),
            Checkpoint, (int)Attempts, (int)MaxAttempts, LastError, AvailableAt, ClaimedAt, ClaimedBy, CreatedAt, UpdatedAt, (int)Priority, CancelRequested,
            (int)Progress, (int)Total, Stage, StepLog);
    }
}
