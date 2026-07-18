using Dapper;
using Lyntai.Jobs;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// PostgreSQL <see cref="IJobStore"/>. The claim uses <c>FOR UPDATE SKIP LOCKED</c> (the canonical
/// work-queue pattern — concurrent claimers skip each other's locked rows, so each gets a distinct job
/// with no contention), as a single <c>UPDATE … RETURNING</c>. Reclaim of a crashed worker is folded into
/// the claim predicate (stale lease); mutating writes are fenced by <c>claimed_by</c> and report
/// rows-affected. Mirrors <c>SqliteJobStore</c>; <c>timestamptz</c> compares as real timestamps.
/// </summary>
public sealed class PostgresJobStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null) : IJobStore
{
    private const string Cols =
        "id, lane, type, payload, status, checkpoint, attempts, max_attempts, last_error, " +
        "available_at, claimed_at, claimed_by, created_at, updated_at, priority, cancel_requested, " +
        "progress, total, stage, step_log";

    // same columns, qualified for the UPDATE … FROM … RETURNING (id is otherwise ambiguous with `pick`)
    private const string JCols =
        "j.id, j.lane, j.type, j.payload, j.status, j.checkpoint, j.attempts, j.max_attempts, j.last_error, " +
        "j.available_at, j.claimed_at, j.claimed_by, j.created_at, j.updated_at, j.priority, j.cancel_requested, " +
        "j.progress, j.total, j.stage, j.step_log";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default)
    {
        var now = _clock();
        var id = Guid.NewGuid();
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO lyntai_job ({Cols})
            VALUES (@id, @lane, @type, @payload, 'Pending', NULL, 0, @maxAttempts, NULL, @availableAt, NULL, NULL, @now, @now, @priority, FALSE, 0, 0, NULL, NULL)
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
            UPDATE lyntai_job j
            SET status='Running', claimed_by=@workerId, claimed_at=@now, attempts=attempts+1, updated_at=@now
            FROM (
                SELECT id FROM lyntai_job
                WHERE lane=@lane
                  AND ((status='Pending' AND available_at<=@now)
                    OR (status='Running' AND claimed_at<@staleBefore))
                ORDER BY priority DESC, available_at, id
                FOR UPDATE SKIP LOCKED LIMIT 1) pick
            WHERE j.id = pick.id
            RETURNING {JCols}
            """, new { lane, workerId, now, staleBefore }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default) =>
        Fenced("SET checkpoint=@checkpoint, claimed_at=@now, updated_at=@now", id, workerId, ct, new { checkpoint });

    public Task<bool> ReportProgressAsync(Guid id, string workerId, int done, int total, string? stage, CancellationToken ct = default) =>
        Fenced("SET progress=@done, total=@total, stage=@stage, updated_at=@now", id, workerId, ct, new { done, total, stage });

    public async Task<bool> ReportStepAsync(Guid id, string workerId, string message, CancellationToken ct = default)
    {
        // read-modify-write the capped JSON step log; the fenced write drops it if the lease was lost
        // (a single job is reported on by its one owning worker, so no self-race on the read)
        var current = await GetAsync(id, ct).ConfigureAwait(false);
        if (current is null || current.Status != JobStatus.Running || current.ClaimedBy != workerId) return false;
        var stepLog = JobStepLog.Append(current.StepLog, message, _clock());
        return await Fenced("SET step_log=@stepLog, updated_at=@now", id, workerId, ct, new { stepLog }).ConfigureAwait(false);
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
                cancel_requested=FALSE, updated_at=@now
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
            "UPDATE lyntai_job SET cancel_requested=TRUE, updated_at=@now WHERE id=@id AND status='Running'",
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
            WHERE (@status::text IS NULL OR status=@status) AND (@lane::text IS NULL OR lane=@lane)
            ORDER BY created_at DESC, id DESC LIMIT @limit
            """, new { status = status?.ToString(), lane, limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToRecord())];
    }

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
