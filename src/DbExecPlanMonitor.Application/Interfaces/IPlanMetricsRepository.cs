using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Repository interface for persisting and retrieving plan metric samples.
/// Samples are point-in-time snapshots of query performance metrics collected
/// from SQL Server DMVs or Query Store.
/// </summary>
public interface IPlanMetricsRepository
{
    /// <summary>
    /// Saves a batch of metric samples to the persistence store.
    /// Uses bulk insert for efficiency when saving many samples at once.
    /// </summary>
    /// <param name="instanceName">The database instance identifier</param>
    /// <param name="samples">The metric samples to save</param>
    /// <param name="ct">Cancellation token</param>
    Task SaveSamplesAsync(
        string instanceName,
        IEnumerable<PlanMetricSampleRecord> samples,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a single metric sample to the persistence store.
    /// </summary>
    Task SaveSampleAsync(
        string instanceName,
        PlanMetricSampleRecord sample,
        CancellationToken ct = default);

    /// <summary>
    /// Gets recent samples for a specific query fingerprint within a time window.
    /// </summary>
    Task<IReadOnlyList<PlanMetricSampleRecord>> GetSamplesForFingerprintAsync(
        Guid fingerprintId,
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Gets recent samples for a specific database instance within a time window.
    /// </summary>
    Task<IReadOnlyList<PlanMetricSampleRecord>> GetSamplesForInstanceAsync(
        string instanceName,
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the most recent sample for each fingerprint in a database.
    /// Useful for building a current "state of the world" view.
    /// </summary>
    /// <param name="databaseName">The database name to filter by</param>
    /// <param name="topN">Maximum number of fingerprints to return</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<PlanMetricSampleRecord>> GetLatestSamplesPerFingerprintAsync(
        string databaseName,
        int topN = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the most recent sample for each fingerprint in a database within a time window.
    /// Prevents returning stale data when collection has been paused.
    /// </summary>
    /// <param name="databaseName">The database name to filter by</param>
    /// <param name="window">Time window to filter samples (only samples within window are considered)</param>
    /// <param name="topN">Maximum number of fingerprints to return</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<PlanMetricSampleRecord>> GetLatestSamplesPerFingerprintAsync(
        string databaseName,
        TimeWindow window,
        int topN = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Gets aggregated statistics for a fingerprint over a time window.
    /// Returns min, max, avg, P50, P95 for each metric.
    /// </summary>
    Task<AggregatedMetrics?> GetAggregatedMetricsAsync(
        Guid fingerprintId,
        TimeWindow window,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes samples older than the specified retention period.
    /// Called periodically to manage storage growth.
    /// </summary>
    /// <param name="olderThan">Delete samples before this date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of samples deleted</returns>
    Task<int> PurgeSamplesOlderThanAsync(DateTime olderThan, CancellationToken ct = default);
}

/// <summary>
/// Represents a point-in-time sample of query metrics.
/// These are collected periodically from SQL Server and stored for analysis.
/// </summary>
public sealed class PlanMetricSampleRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FingerprintId { get; init; }
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required DateTime SampledAtUtc { get; init; }
    
    // Plan identification
    public byte[]? PlanHash { get; init; }
    public long? QueryStoreQueryId { get; init; }
    public long? QueryStorePlanId { get; init; }
    
    // Execution counts
    public required long ExecutionCount { get; init; }
    public long ExecutionCountDelta { get; init; }
    
    // CPU metrics (microseconds)
    public required long TotalCpuTimeUs { get; init; }
    public required long AvgCpuTimeUs { get; init; }
    public long? MinCpuTimeUs { get; init; }
    public long? MaxCpuTimeUs { get; init; }
    
    // Duration metrics (microseconds)
    public required long TotalDurationUs { get; init; }
    public required long AvgDurationUs { get; init; }
    public long? MinDurationUs { get; init; }
    public long? MaxDurationUs { get; init; }
    
    // I/O metrics
    public required long TotalLogicalReads { get; init; }
    public required long AvgLogicalReads { get; init; }
    public long TotalLogicalWrites { get; init; }
    public long TotalPhysicalReads { get; init; }
    
    // Memory metrics (when available)
    public long? AvgMemoryGrantKb { get; init; }
    public long? MaxMemoryGrantKb { get; init; }
    public long? AvgSpillsKb { get; init; }

    /// <summary>
    /// Calculates average duration in milliseconds for easier human reading.
    /// </summary>
    public double AvgDurationMs => AvgDurationUs / 1000.0;

    /// <summary>
    /// Calculates average CPU time in milliseconds for easier human reading.
    /// </summary>
    public double AvgCpuTimeMs => AvgCpuTimeUs / 1000.0;
}

/// <summary>
/// Aggregated statistics for a query over a time period.
/// Used for baseline comparison and trend analysis.
/// </summary>
public sealed class AggregatedMetrics
{
    public required Guid FingerprintId { get; init; }
    public required TimeWindow Window { get; init; }
    public required int SampleCount { get; init; }
    public required long TotalExecutions { get; init; }
    
    // Duration aggregates (microseconds)
    public required long MinDurationUs { get; init; }
    public required long MaxDurationUs { get; init; }
    public required long AvgDurationUs { get; init; }
    public long? P50DurationUs { get; init; }
    public long? P95DurationUs { get; init; }
    public long? P99DurationUs { get; init; }
    
    // CPU aggregates (microseconds)
    public required long MinCpuTimeUs { get; init; }
    public required long MaxCpuTimeUs { get; init; }
    public required long AvgCpuTimeUs { get; init; }
    public long? P95CpuTimeUs { get; init; }
    
    // I/O aggregates
    public required long MinLogicalReads { get; init; }
    public required long MaxLogicalReads { get; init; }
    public required long AvgLogicalReads { get; init; }
    
    /// <summary>
    /// Standard deviation of duration, useful for detecting inconsistent query performance.
    /// </summary>
    public double? DurationStdDev { get; init; }
}
