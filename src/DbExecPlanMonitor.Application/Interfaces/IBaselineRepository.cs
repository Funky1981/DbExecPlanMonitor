using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Repository interface for managing query baselines.
/// A baseline represents the "normal" performance of a query during a stable period.
/// Baselines are used as the reference point for detecting regressions.
/// </summary>
public interface IBaselineRepository
{
    /// <summary>
    /// Saves or updates a baseline for a query fingerprint.
    /// If a baseline already exists for this fingerprint, it is replaced.
    /// </summary>
    Task SaveBaselineAsync(BaselineRecord baseline, CancellationToken ct = default);

    /// <summary>
    /// Gets the current baseline for a query fingerprint.
    /// Returns null if no baseline has been established.
    /// </summary>
    Task<BaselineRecord?> GetBaselineAsync(Guid fingerprintId, CancellationToken ct = default);

    /// <summary>
    /// Gets all baselines for a specific database.
    /// </summary>
    Task<IReadOnlyList<BaselineRecord>> GetBaselinesForDatabaseAsync(
        string databaseName,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all baselines that are older than the specified age.
    /// Stale baselines may need to be refreshed.
    /// </summary>
    Task<IReadOnlyList<BaselineRecord>> GetStaleBaselinesAsync(
        TimeSpan maxAge,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a baseline for a fingerprint.
    /// </summary>
    Task DeleteBaselineAsync(Guid fingerprintId, CancellationToken ct = default);

    /// <summary>
    /// Checks if a baseline exists for a fingerprint.
    /// </summary>
    Task<bool> ExistsAsync(Guid fingerprintId, CancellationToken ct = default);

    /// <summary>
    /// Bulk saves multiple baselines in a single transaction.
    /// Used when recalculating baselines for many queries at once.
    /// </summary>
    Task SaveBaselinesAsync(
        IEnumerable<BaselineRecord> baselines,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a PlanBaseline entity (domain entity version).
    /// </summary>
    Task SaveAsync(Domain.Entities.PlanBaseline baseline, CancellationToken ct = default);

    /// <summary>
    /// Gets the active baseline for a fingerprint (domain entity version).
    /// </summary>
    Task<Domain.Entities.PlanBaseline?> GetActiveByFingerprintIdAsync(
        Guid fingerprintId,
        CancellationToken ct = default);

    /// <summary>
    /// Supersedes (deactivates) the current active baseline for a fingerprint.
    /// </summary>
    Task SupersedeActiveBaselineAsync(Guid fingerprintId, CancellationToken ct = default);
}

/// <summary>
/// Represents a performance baseline for a query.
/// The baseline captures "normal" performance metrics over a stable period.
/// </summary>
public sealed class BaselineRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FingerprintId { get; init; }
    public required string DatabaseName { get; init; }
    
    /// <summary>
    /// The time window over which this baseline was calculated.
    /// </summary>
    public required DateTime BaselineStartUtc { get; init; }
    public required DateTime BaselineEndUtc { get; init; }
    
    /// <summary>
    /// When this baseline was created or last updated.
    /// </summary>
    public required DateTime CreatedAtUtc { get; init; }
    
    /// <summary>
    /// Number of samples used to calculate this baseline.
    /// More samples = more confident baseline.
    /// </summary>
    public required int SampleCount { get; init; }
    
    /// <summary>
    /// Total executions observed during the baseline period.
    /// </summary>
    public required long TotalExecutions { get; init; }
    
    // Duration baseline metrics (microseconds)
    public required long MedianDurationUs { get; init; }
    public required long P95DurationUs { get; init; }
    public required long P99DurationUs { get; init; }
    public required long AvgDurationUs { get; init; }
    public double DurationStdDev { get; init; }
    
    // CPU baseline metrics (microseconds)
    public required long MedianCpuTimeUs { get; init; }
    public required long P95CpuTimeUs { get; init; }
    public required long AvgCpuTimeUs { get; init; }
    
    // I/O baseline metrics
    public required long MedianLogicalReads { get; init; }
    public required long P95LogicalReads { get; init; }
    public required long AvgLogicalReads { get; init; }
    
    // Plan information at baseline time
    public byte[]? ExpectedPlanHash { get; init; }
    
    /// <summary>
    /// Optional notes about this baseline (e.g., "Post-index optimization baseline").
    /// </summary>
    public string? Notes { get; init; }
    
    /// <summary>
    /// Whether this baseline is considered stable and active for comparison.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// The duration of the baseline period.
    /// </summary>
    public TimeSpan BaselinePeriod => BaselineEndUtc - BaselineStartUtc;

    /// <summary>
    /// How old this baseline is.
    /// </summary>
    public TimeSpan Age => DateTime.UtcNow - CreatedAtUtc;

    /// <summary>
    /// Checks if a given duration (in microseconds) exceeds the P95 baseline.
    /// </summary>
    public bool IsDurationRegressionVsP95(long durationUs) => 
        durationUs > P95DurationUs;

    /// <summary>
    /// Calculates the percentage change from baseline median duration.
    /// Positive = regression, Negative = improvement.
    /// </summary>
    public double CalculateDurationChangePercent(long currentDurationUs)
    {
        if (MedianDurationUs == 0) return 0;
        return ((double)(currentDurationUs - MedianDurationUs) / MedianDurationUs) * 100;
    }

    /// <summary>
    /// Calculates the percentage change from baseline median CPU time.
    /// </summary>
    public double CalculateCpuChangePercent(long currentCpuTimeUs)
    {
        if (MedianCpuTimeUs == 0) return 0;
        return ((double)(currentCpuTimeUs - MedianCpuTimeUs) / MedianCpuTimeUs) * 100;
    }
}
