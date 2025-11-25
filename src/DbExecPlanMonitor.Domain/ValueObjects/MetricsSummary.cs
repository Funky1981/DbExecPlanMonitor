namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// A summary of performance metrics at a point in time.
/// Used for comparisons, averaging, and display.
/// </summary>
public sealed class MetricsSummary : IEquatable<MetricsSummary>
{
    /// <summary>
    /// Average CPU time in milliseconds.
    /// </summary>
    public double AvgCpuTimeMs { get; }

    /// <summary>
    /// Average duration in milliseconds.
    /// </summary>
    public double AvgDurationMs { get; }

    /// <summary>
    /// Average logical reads.
    /// </summary>
    public double AvgLogicalReads { get; }

    /// <summary>
    /// Average physical reads.
    /// </summary>
    public double AvgPhysicalReads { get; }

    /// <summary>
    /// Average rows returned.
    /// </summary>
    public double AvgRowsReturned { get; }

    /// <summary>
    /// Total execution count.
    /// </summary>
    public long ExecutionCount { get; }

    /// <summary>
    /// Creates a new metrics summary.
    /// </summary>
    public MetricsSummary(
        double avgCpuTimeMs,
        double avgDurationMs,
        double avgLogicalReads,
        double avgPhysicalReads,
        double avgRowsReturned,
        long executionCount)
    {
        AvgCpuTimeMs = avgCpuTimeMs;
        AvgDurationMs = avgDurationMs;
        AvgLogicalReads = avgLogicalReads;
        AvgPhysicalReads = avgPhysicalReads;
        AvgRowsReturned = avgRowsReturned;
        ExecutionCount = executionCount;
    }

    /// <summary>
    /// Empty/zero metrics summary.
    /// </summary>
    public static MetricsSummary Empty => new(0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Calculates the ratio of this summary to another (for regression detection).
    /// </summary>
    public MetricsRatio CompareTo(MetricsSummary baseline)
    {
        return new MetricsRatio(
            cpuTimeRatio: baseline.AvgCpuTimeMs > 0 ? AvgCpuTimeMs / baseline.AvgCpuTimeMs : 0,
            durationRatio: baseline.AvgDurationMs > 0 ? AvgDurationMs / baseline.AvgDurationMs : 0,
            logicalReadsRatio: baseline.AvgLogicalReads > 0 ? AvgLogicalReads / baseline.AvgLogicalReads : 0,
            physicalReadsRatio: baseline.AvgPhysicalReads > 0 ? AvgPhysicalReads / baseline.AvgPhysicalReads : 0
        );
    }

    /// <summary>
    /// Creates a weighted average of multiple summaries.
    /// </summary>
    public static MetricsSummary WeightedAverage(IEnumerable<MetricsSummary> summaries)
    {
        var list = summaries.ToList();
        if (!list.Any())
            return Empty;

        var totalExecutions = list.Sum(s => s.ExecutionCount);
        if (totalExecutions == 0)
            return Empty;

        return new MetricsSummary(
            avgCpuTimeMs: list.Sum(s => s.AvgCpuTimeMs * s.ExecutionCount) / totalExecutions,
            avgDurationMs: list.Sum(s => s.AvgDurationMs * s.ExecutionCount) / totalExecutions,
            avgLogicalReads: list.Sum(s => s.AvgLogicalReads * s.ExecutionCount) / totalExecutions,
            avgPhysicalReads: list.Sum(s => s.AvgPhysicalReads * s.ExecutionCount) / totalExecutions,
            avgRowsReturned: list.Sum(s => s.AvgRowsReturned * s.ExecutionCount) / totalExecutions,
            executionCount: totalExecutions
        );
    }

    public override string ToString()
    {
        return $"CPU: {AvgCpuTimeMs:N1}ms, Duration: {AvgDurationMs:N1}ms, Reads: {AvgLogicalReads:N0}, Execs: {ExecutionCount:N0}";
    }

    public bool Equals(MetricsSummary? other)
    {
        if (other is null) return false;
        return AvgCpuTimeMs == other.AvgCpuTimeMs
            && AvgDurationMs == other.AvgDurationMs
            && AvgLogicalReads == other.AvgLogicalReads
            && AvgPhysicalReads == other.AvgPhysicalReads
            && AvgRowsReturned == other.AvgRowsReturned
            && ExecutionCount == other.ExecutionCount;
    }

    public override bool Equals(object? obj) => Equals(obj as MetricsSummary);

    public override int GetHashCode() => HashCode.Combine(
        AvgCpuTimeMs, AvgDurationMs, AvgLogicalReads, AvgPhysicalReads, AvgRowsReturned, ExecutionCount);
}

/// <summary>
/// Ratio of current metrics to baseline metrics.
/// Values > 1.0 mean current is worse than baseline.
/// </summary>
public sealed class MetricsRatio : IEquatable<MetricsRatio>
{
    /// <summary>
    /// CPU time ratio (current / baseline).
    /// </summary>
    public double CpuTimeRatio { get; }

    /// <summary>
    /// Duration ratio.
    /// </summary>
    public double DurationRatio { get; }

    /// <summary>
    /// Logical reads ratio.
    /// </summary>
    public double LogicalReadsRatio { get; }

    /// <summary>
    /// Physical reads ratio.
    /// </summary>
    public double PhysicalReadsRatio { get; }

    /// <summary>
    /// The maximum ratio across all metrics.
    /// </summary>
    public double MaxRatio => Math.Max(
        Math.Max(CpuTimeRatio, DurationRatio),
        Math.Max(LogicalReadsRatio, PhysicalReadsRatio));

    /// <summary>
    /// Creates a new metrics ratio.
    /// </summary>
    public MetricsRatio(
        double cpuTimeRatio,
        double durationRatio,
        double logicalReadsRatio,
        double physicalReadsRatio)
    {
        CpuTimeRatio = cpuTimeRatio;
        DurationRatio = durationRatio;
        LogicalReadsRatio = logicalReadsRatio;
        PhysicalReadsRatio = physicalReadsRatio;
    }

    /// <summary>
    /// Checks if any ratio exceeds the given thresholds.
    /// </summary>
    public bool ExceedsThresholds(ThresholdConfiguration thresholds)
    {
        return thresholds.ExceedsThreshold(MetricType.CpuTime, CpuTimeRatio)
            || thresholds.ExceedsThreshold(MetricType.Duration, DurationRatio)
            || thresholds.ExceedsThreshold(MetricType.LogicalReads, LogicalReadsRatio)
            || thresholds.ExceedsThreshold(MetricType.PhysicalReads, PhysicalReadsRatio);
    }

    /// <summary>
    /// Gets a summary of which metrics are regressed.
    /// </summary>
    public string GetRegressionSummary(ThresholdConfiguration thresholds)
    {
        var parts = new List<string>();

        if (thresholds.ExceedsThreshold(MetricType.CpuTime, CpuTimeRatio))
            parts.Add($"CPU {CpuTimeRatio:F1}x");
        if (thresholds.ExceedsThreshold(MetricType.Duration, DurationRatio))
            parts.Add($"Duration {DurationRatio:F1}x");
        if (thresholds.ExceedsThreshold(MetricType.LogicalReads, LogicalReadsRatio))
            parts.Add($"Reads {LogicalReadsRatio:F1}x");
        if (thresholds.ExceedsThreshold(MetricType.PhysicalReads, PhysicalReadsRatio))
            parts.Add($"Physical {PhysicalReadsRatio:F1}x");

        return parts.Any() ? string.Join(", ", parts) : "No regression";
    }

    public override string ToString()
    {
        return $"CPU: {CpuTimeRatio:F2}x, Duration: {DurationRatio:F2}x, Reads: {LogicalReadsRatio:F2}x";
    }

    public bool Equals(MetricsRatio? other)
    {
        if (other is null) return false;
        return CpuTimeRatio == other.CpuTimeRatio
            && DurationRatio == other.DurationRatio
            && LogicalReadsRatio == other.LogicalReadsRatio
            && PhysicalReadsRatio == other.PhysicalReadsRatio;
    }

    public override bool Equals(object? obj) => Equals(obj as MetricsRatio);

    public override int GetHashCode() => HashCode.Combine(
        CpuTimeRatio, DurationRatio, LogicalReadsRatio, PhysicalReadsRatio);
}
