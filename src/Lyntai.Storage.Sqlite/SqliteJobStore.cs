using Dapper;
using Lyntai.Jobs;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite <see cref="IJobStore"/>. The claim is a SINGLE <c>UPDATE … RETURNING</c> (never
/// select-then-update) — under WAL + busy_timeout SQLite is single-writer, so two claimers can't grab the
/// same row (the second blocks then re-evaluates against committed state). Reclaim of a crashed worker is
/// folded into the claim predicate (stale lease). All transitions/fencing execute the SHARED
/// <see cref="JobStoreSql"/> statements (booleans bound as parameters), so the state machine can't drift
/// from the Postgres store; only the claim SQL is dialect-specific. Timestamps/id/status are TEXT (no new
/// Dapper type handler → no process-global registry collision; TEXT timestamps compare chronologically
/// because the ISO format is sortable).
/// </summary>
public sealed class SqliteJobStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null, int stepLogCap = JobStepLog.DefaultCap) : IJobStore
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    // ReportStepAsync is a read-modify-write on step_log (no single-statement atomic append with the cap),
    // so serialize concurrent step reports for this store — matching InMemoryJobStore, whose append is under
    // its lock. (A single running job is owned by one worker via fencing, so per-store serialization suffices.)
    private readonly SemaphoreSlim _stepLock = new(1, 1);

    public async Task<Guid> EnqueueAsync(JobSpec spec, CancellationToken ct = default)
    {
        var now = _clock();
        var id = Guid.NewGuid();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(JobStoreSql.Insert, new
        {
            id = id.ToString(), lane = spec.Lane, type = spec.Type, payload = spec.Payload,
            maxAttempts = spec.MaxAttempts ?? JobSpec.DefaultMaxAttempts, availableAt = spec.AvailableAt ?? now, now, priority = spec.Priority,
            f = false, partitionKey = spec.PartitionKey,
        }, cancellationToken: ct)).ConfigureAwait(false);
        return id;
    }

    public async Task<JobRecord?> ClaimNextAsync(string lane, string workerId, TimeSpan lease, CancellationToken ct = default)
    {
        var now = _clock();
        var staleBefore = now - lease;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // candidate predicate is the SHARED dialect-neutral text; only this single-writer
        // UPDATE…RETURNING locking frame is SQLite-specific
        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition($"""
            UPDATE lyntai_job
            SET status='Running', claimed_by=@workerId, claimed_at=@now, attempts=attempts+1, updated_at=@now
            WHERE id = (
                SELECT id FROM lyntai_job c
                WHERE {JobStoreSql.ClaimCandidateWhere}
                ORDER BY c.priority DESC, c.available_at, c.id LIMIT 1)
            RETURNING {JobStoreSql.Cols}
            """, new { lane, workerId, now, staleBefore }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public Task<bool> SaveCheckpointAsync(Guid id, string workerId, string checkpoint, CancellationToken ct = default) =>
        Fenced(JobStoreSql.SetCheckpoint, id, workerId, ct, new { checkpoint });

    public Task<bool> ReportProgressAsync(Guid id, string workerId, int done, int total, string? stage, CancellationToken ct = default) =>
        Fenced(JobStoreSql.SetProgress, id, workerId, ct, new { done, total, stage });

    public async Task<bool> ReportStepAsync(Guid id, string workerId, string message, CancellationToken ct = default)
    {
        // read-modify-write the capped JSON step log under _stepLock so concurrent reports don't clobber each
        // other; the fenced write drops it if the lease was lost
        await _stepLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await GetAsync(id, ct).ConfigureAwait(false);
            if (current is null || current.Status != JobStatus.Running || current.ClaimedBy != workerId) return false;
            var stepLog = JobStepLog.Append(current.StepLog, message, _clock(), stepLogCap);
            return await Fenced(JobStoreSql.SetStepLog, id, workerId, ct, new { stepLog }).ConfigureAwait(false);
        }
        finally { _stepLock.Release(); }
    }

    public Task<bool> CompleteAsync(Guid id, string workerId, CancellationToken ct = default) =>
        Fenced(JobStoreSql.SetSucceeded, id, workerId, ct);

    public Task<bool> FailAsync(Guid id, string workerId, string error, DateTimeOffset? retryAt = null, CancellationToken ct = default) =>
        retryAt is { } at
            ? Fenced(JobStoreSql.SetFailedRetry, id, workerId, ct, new { error, retryAt = at })
            : Fenced(JobStoreSql.SetFailedTerminal, id, workerId, ct, new { error });

    public Task<bool> DeadLetterAsync(Guid id, string workerId, string error, CancellationToken ct = default) =>
        Fenced(JobStoreSql.SetDead, id, workerId, ct, new { error });

    public Task<bool> ReplayAsync(Guid id, CancellationToken ct = default) =>
        Transition(JobStoreSql.Replay, id, ct, new { f = false });

    public Task<bool> PauseAsync(Guid id, CancellationToken ct = default) =>
        Transition(JobStoreSql.Pause, id, ct);

    public Task<bool> ResumeAsync(Guid id, CancellationToken ct = default) =>
        Transition(JobStoreSql.Resume, id, ct);

    public Task<bool> RequestCancelAsync(Guid id, CancellationToken ct = default) =>
        Transition(JobStoreSql.RequestCancel, id, ct, new { t = true });

    public Task<bool> CancelRunningAsync(Guid id, string workerId, CancellationToken ct = default) =>
        Fenced(JobStoreSql.SetCancelled, id, workerId, ct);

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default) =>
        Transition(JobStoreSql.CancelPending, id, ct);

    public async Task<int> CountRunningAsync(string lane, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            JobStoreSql.CountRunning, new { lane }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ActiveLanesAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var lanes = await conn.QueryAsync<string>(new CommandDefinition(
            JobStoreSql.ActiveLanes, cancellationToken: ct)).ConfigureAwait(false);
        return [.. lanes];
    }

    public async Task<JobRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition(
            JobStoreSql.GetById, new { id = id.ToString() }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<JobRecord>> ListAsync(JobStatus? status = null, string? lane = null, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<JobRow>(new CommandDefinition($"""
            SELECT {JobStoreSql.Cols} FROM lyntai_job
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
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE lyntai_job {setClause} {JobStoreSql.FenceWhere}",
            p, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    /// <summary>An unfenced (admin/API) transition — a full shared statement gated by its own status predicate.</summary>
    private async Task<bool> Transition(string sql, Guid id, CancellationToken ct, object? extra = null)
    {
        var p = new DynamicParameters(new { id = id.ToString(), now = _clock() });
        if (extra is not null) p.AddDynamicParams(extra);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var n = await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }
}
