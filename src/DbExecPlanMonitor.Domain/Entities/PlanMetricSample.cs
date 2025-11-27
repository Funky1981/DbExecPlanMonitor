namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// A point-in-time sample of performance metrics for an execution plan.
/// Collected periodically to track how a plan performs over time.
/// </summary>
/// <remarks>
/// These metrics come from SQL Server DMVs (sys.dm_exec_query_stats) or
/// Query Store (sys.query_store_runtime_stats).
/// 
/// By collecting samples over time, we can:
/// - Detect performance degradation (regression)
/// - Identify periodic patterns (slow during month-end)
/// - Compare different plans for the same query
/// </remarks>
public class PlanMetricSample
{
    /// <summary>
    /// Unique identifier for this sample.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the execution plan this sample belongs to.
    /// </summary>
    public Guid ExecutionPlanSnapshotId { get; private set; }

    /// <summary>
    /// Navigation property to the parent plan snapshot.
    /// </summary>
    public ExecutionPlanSnapshot ExecutionPlanSnapshot { get; private set; } = null!;

    /// <summary>
    /// When this sample was collected.
    /// </summary>
    public DateTime SampledAtUtc { get; private set; }

    /// <summary>
    /// Number of executions in this sample window.
    /// High execution count = high-impact query.
    /// </summary>
    public long ExecutionCount { get; private set; }

    /// <summary>
    /// Average CPU time in milliseconds per execution.
    /// This is actual CPU work, not wall-clock time.
    /// </summary>
    public double AvgCpuTimeMs { get; private set; }

    /// <summary>
    /// Minimum CPU time observed in this window.
    /// Useful for understanding the "best case" performance.
    /// </summary>
    public double MinCpuTimeMs { get; private set; }

    /// <summary>
    /// Maximum CPU time observed in this window.
    /// Spikes here often indicate parameter sniffing issues.
    /// </summary>
    public double MaxCpuTimeMs { get; private set; }

    /// <summary>
    /// Average elapsed/duration time in milliseconds.
    /// This is wall-clock time - includes waits, blocking, etc.
    /// </summary>
    public double AvgDurationMs { get; private set; }

    /// <summary>
    /// Minimum duration observed.
    /// </summary>
    public double MinDurationMs { get; private set; }

    /// <summary>
    /// Maximum duration observed.
    /// Large gap between min and max suggests inconsistent performance.
    /// </summary>
    public double MaxDurationMs { get; private set; }

    /// <summary>
    /// Average logical reads (8KB pages read from buffer pool).
    /// This is the primary I/O metric - memory reads.
    /// </summary>
    public double AvgLogicalReads { get; private set; }

    /// <summary>
    /// Average physical reads (8KB pages read from disk).
    /// High physical reads = data not cached, hitting disk.
    /// </summary>
    public double AvgPhysicalReads { get; private set; }

    /// <summary>
    /// Average number of rows returned per execution.
    /// </summary>
    public double AvgRowsReturned { get; private set; }

    /// <summary>
    /// Average memory grant in KB (if applicable).
    /// Sorts and hash operations need memory grants.
    /// </summary>
    public double? AvgMemoryGrantKb { get; private set; }

    /// <summary>
    /// Average writes (for INSERT/UPDATE/DELETE operations).
    /// </summary>
    public double AvgWrites { get; private set; }

    /// <summary>
    /// Total CPU time across all executions in this sample.
    /// ExecutionCount * AvgCpuTimeMs = TotalCpuTimeMs
    /// </summary>
    public double TotalCpuTimeMs { get; private set; }

    /// <summary>
    /// Total duration across all executions.
    /// </summary>
    public double TotalDurationMs { get; private set; }

    /// <summary>
    /// Total logical reads across all executions.
    /// </summary>
    public double TotalLogicalReads { get; private set; }

    // Private constructor for EF Core
    private PlanMetricSample() { }

    /// <summary>
    /// Creates a new metric sample. Called by ExecutionPlanSnapshot.AddMetricSample().
    /// </summary>
    internal PlanMetricSample(
        ExecutionPlanSnapshot planSnapshot,
        long executionCount,
        double avgCpuTimeMs,
        double avgDurationMs,
        double avgLogicalReads,
        double avgPhysicalReads,
        double avgRowsReturned,
        double? avgMemoryGrantKb = null)
    {
        if (planSnapshot == null)
            throw new ArgumentNullException(nameof(planSnapshot));

        Id = Guid.NewGuid();
        ExecutionPlanSnapshotId = planSnapshot.Id;
        ExecutionPlanSnapshot = planSnapshot;
        SampledAtUtc = DateTime.UtcNow;

        ExecutionCount = executionCount;
        AvgCpuTimeMs = avgCpuTimeMs;
        AvgDurationMs = avgDurationMs;
        AvgLogicalReads = avgLogicalReads;
        AvgPhysicalReads = avgPhysicalReads;
        AvgRowsReturned = avgRowsReturned;
        AvgMemoryGrantKb = avgMemoryGrantKb;
        AvgWrites = 0; // Will be set via SetDetailedMetrics if available

        // Set min/max to avg initially - will be updated if detailed stats available
        MinCpuTimeMs = avgCpuTimeMs;
        MaxCpuTimeMs = avgCpuTimeMs;
        MinDurationMs = avgDurationMs;
        MaxDurationMs = avgDurationMs;

        // Calculate totals
        TotalCpuTimeMs = executionCount * avgCpuTimeMs;
        TotalDurationMs = executionCount * avgDurationMs;
        TotalLogicalReads = executionCount * avgLogicalReads;
    }

    /// <summary>
    /// Updates with more detailed min/max metrics if available.
    /// Query Store provides these; DMVs may not.
    /// </summary>
    public void SetDetailedMetrics(
        double minCpuTimeMs,
        double maxCpuTimeMs,
        double minDurationMs,
        double maxDurationMs,
        double avgWrites)
    {
        MinCpuTimeMs = minCpuTimeMs;
        MaxCpuTimeMs = maxCpuTimeMs;
        MinDurationMs = minDurationMs;
        MaxDurationMs = maxDurationMs;
        AvgWrites = avgWrites;
    }

    /// <summary>
    /// Calculates the CPU time variance (spread between min and max).
    /// High variance often indicates parameter sniffing.
    /// </summary>
    public double GetCpuTimeVariance()
    {
        if (MinCpuTimeMs == 0)
            return 0;

        return (MaxCpuTimeMs - MinCpuTimeMs) / MinCpuTimeMs;
    }

    /// <summary>
    /// Calculates the duration variance.
    /// </summary>
    public double GetDurationVariance()
    {
        if (MinDurationMs == 0)
            return 0;

        return (MaxDurationMs - MinDurationMs) / MinDurationMs;
    }

    /// <summary>
    /// Calculates the ratio of duration to CPU time.
    /// Ratio > 1 means time spent waiting (I/O, locks, etc.)
    /// Ratio ≈ 1 means CPU-bound query.
    /// </summary>
    public double GetWaitRatio()
    {
        if (AvgCpuTimeMs == 0)
            return 0;

        return AvgDurationMs / AvgCpuTimeMs;
    }

    /// <summary>
    /// Checks if this sample shows concerning variance.
    /// High variance often indicates parameter sniffing issues.
    /// </summary>
    public bool HasHighVariance(double threshold = 5.0)
    {
        // If max is more than 5x the min, that's concerning
        return GetCpuTimeVariance() > threshold || GetDurationVariance() > threshold;
    }

    /// <summary>
    /// Checks if this sample shows high wait time.
    /// Duration >> CPU time means blocking, I/O waits, etc.
    /// </summary>
    public bool HasHighWaitTime(double threshold = 3.0)
    {
        // If duration is more than 3x CPU time, lots of waiting
        return GetWaitRatio() > threshold;
    }

    /// <summary>
    /// Compares this sample to a baseline and calculates deviation.
    /// Returns the factor by which this sample exceeds the baseline.
    /// </summary>
    public double CompareToBaseline(PlanBaseline baseline, MetricType metric)
    {
        if (baseline == null)
            return 0;

        return metric switch
        {
            MetricType.CpuTime => baseline.AvgCpuTimeUs > 0 
                ? (AvgCpuTimeMs * 1000) / baseline.AvgCpuTimeUs 
                : 0,
            MetricType.Duration => baseline.AvgDurationUs > 0 
                ? (AvgDurationMs * 1000) / baseline.AvgDurationUs 
                : 0,
            MetricType.LogicalReads => baseline.AvgLogicalReads > 0 
                ? AvgLogicalReads / baseline.AvgLogicalReads 
                : 0,
            _ => 0
        };
    }
}

/// <summary>
/// The type of metric being compared.
/// </summary>
public enum MetricType
{
    CpuTime,
    Duration,
    LogicalReads,
    PhysicalReads,
    RowsReturned
}
