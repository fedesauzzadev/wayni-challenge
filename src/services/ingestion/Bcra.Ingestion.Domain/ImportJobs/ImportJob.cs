namespace Bcra.Ingestion.Domain.ImportJobs;

public sealed class ImportJob
{
    private static readonly Dictionary<ImportJobStatus, ImportJobStatus[]> AllowedTransitions = new()
    {
        [ImportJobStatus.Pending] = [ImportJobStatus.Loading, ImportJobStatus.Failed],
        [ImportJobStatus.Loading] = [ImportJobStatus.Aggregating, ImportJobStatus.Failed],
        [ImportJobStatus.Aggregating] = [ImportJobStatus.Completed, ImportJobStatus.Failed],
        [ImportJobStatus.Completed] = Array.Empty<ImportJobStatus>(),
        [ImportJobStatus.Failed] = Array.Empty<ImportJobStatus>()
    };

    public Guid Id { get; }
    public string Source { get; }
    public string? Etag { get; internal set; }
    public long Size { get; internal set; }
    public int? Periodo { get; internal set; }
    public ImportJobStatus Status { get; internal set; }
    public long BytesCommitted { get; internal set; }
    public long LinesProcessed { get; internal set; }
    public long LinesRejected { get; internal set; }
    public string? LeaseOwner { get; internal set; }
    public DateTimeOffset? HeartbeatAt { get; internal set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? StartedAtUtc { get; internal set; }
    public DateTimeOffset? FinishedAtUtc { get; internal set; }

    public bool IsTerminal => Status is ImportJobStatus.Completed or ImportJobStatus.Failed;

    public bool IsActive => Status is ImportJobStatus.Loading or ImportJobStatus.Aggregating;

    public bool IsClaimable(DateTimeOffset utcNow, TimeSpan leaseTimeout) =>
        Status == ImportJobStatus.Pending
        || HasExpiredLease(utcNow, leaseTimeout);

    public bool IsClaimable(TimeProvider clock, TimeSpan leaseTimeout) =>
        IsClaimable(clock.GetUtcNow(), leaseTimeout);

    public bool HasExpiredLease(DateTimeOffset utcNow, TimeSpan leaseTimeout) =>
        IsActive
        && (HeartbeatAt is null || utcNow - HeartbeatAt >= leaseTimeout);

    public void Claim(string leaseOwner, DateTimeOffset utcNow, TimeSpan leaseTimeout)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
            throw new ArgumentException("Lease owner cannot be empty.", nameof(leaseOwner));
        if (!IsClaimable(utcNow, leaseTimeout))
            throw new InvalidOperationException(
                $"Job '{Id}' in status {Status} cannot be claimed (lease owner '{LeaseOwner}', heartbeat {HeartbeatAt:o}).");

        if (Status == ImportJobStatus.Pending)
        {
            TransitionTo(ImportJobStatus.Loading);
            StartedAtUtc ??= utcNow;
        }

        LeaseOwner = leaseOwner;
        HeartbeatAt = utcNow;
    }

    public void RenewLease(string leaseOwner, DateTimeOffset utcNow)
    {
        EnsureStatus(ImportJobStatus.Loading, ImportJobStatus.Aggregating);
        if (LeaseOwner != leaseOwner)
            throw new InvalidOperationException($"Lease is owned by '{LeaseOwner}', not '{leaseOwner}'.");

        HeartbeatAt = utcNow;
    }

    public void ReleaseClaim()
    {
        if (IsTerminal)
            return;

        LeaseOwner = null;
        HeartbeatAt = null;
    }

    private ImportJob(Guid id, string source, ImportJobStatus status, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be empty.", nameof(source));

        Id = id;
        Source = source;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    public static ImportJob Create(string source, DateTimeOffset utcNow) =>
        new(Guid.NewGuid(), source, ImportJobStatus.Pending, utcNow);

    public static ImportJob Rehydrate(
        Guid id,
        string source,
        string? etag,
        long size,
        int? periodo,
        ImportJobStatus status,
        long bytesCommitted,
        long linesProcessed,
        long linesRejected,
        string? leaseOwner,
        DateTimeOffset? heartbeatAt,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? finishedAtUtc)
    {
        var job = new ImportJob(id, source, status, createdAtUtc)
        {
            Etag = etag,
            Size = size,
            Periodo = periodo,
            BytesCommitted = bytesCommitted,
            LinesProcessed = linesProcessed,
            LinesRejected = linesRejected,
            LeaseOwner = leaseOwner,
            HeartbeatAt = heartbeatAt,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = finishedAtUtc
        };
        return job;
    }

    public void SetSourceMetadata(long size, string? etag)
    {
        EnsureStatus(ImportJobStatus.Pending, ImportJobStatus.Loading);
        if (Size > 0 && Size != size)
            throw new InvalidOperationException("Source size changed; the file was modified.");
        if (!string.IsNullOrEmpty(Etag) && !string.IsNullOrEmpty(etag) && Etag != etag)
            throw new InvalidOperationException("Source ETag changed; the file was modified.");

        Size = size;
        Etag = etag ?? Etag;
    }

    public void SetPeriodo(int periodo)
    {
        if (Periodo is { } current && current != periodo)
            throw new InvalidOperationException($"Mixed periods in source: {current} vs {periodo}.");
        Periodo = periodo;
    }

    public void MarkLoading(DateTimeOffset utcNow)
    {
        TransitionTo(ImportJobStatus.Loading);
        StartedAtUtc ??= utcNow;
    }

    public void CommitBatch(long bytesCommitted, long batchLinesProcessed, long batchLinesRejected)
    {
        EnsureStatus(ImportJobStatus.Loading);
        if (bytesCommitted < BytesCommitted)
            throw new InvalidOperationException("bytes_committed cannot move backwards.");
        if (bytesCommitted > Size && Size > 0)
            throw new InvalidOperationException("bytes_committed cannot exceed the source size.");

        BytesCommitted = bytesCommitted;
        LinesProcessed += batchLinesProcessed;
        LinesRejected += batchLinesRejected;
    }

    public void MarkAggregating() => TransitionTo(ImportJobStatus.Aggregating);

    public void MarkCompleted(DateTimeOffset utcNow)
    {
        TransitionTo(ImportJobStatus.Completed);
        FinishedAtUtc = utcNow;
    }

    public void MarkFailed(DateTimeOffset utcNow)
    {
        if (IsTerminal)
            throw new InvalidOperationException($"Cannot fail a terminal job ({Status}).");

        Status = ImportJobStatus.Failed;
        FinishedAtUtc = utcNow;
    }

    private void TransitionTo(ImportJobStatus target)
    {
        if (!AllowedTransitions[Status].Contains(target))
            throw new InvalidOperationException($"Invalid status transition {Status} -> {target}.");
        Status = target;
    }

    private void EnsureStatus(params ImportJobStatus[] expected)
    {
        if (!expected.Contains(Status))
            throw new InvalidOperationException($"Operation requires status {string.Join(" or ", expected)} but was {Status}.");
    }
}
