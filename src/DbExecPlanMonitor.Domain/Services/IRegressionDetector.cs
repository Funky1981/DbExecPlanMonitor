using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Domain.Services;

/// <summary>
/// Aggregated metrics for comparison against baseline.
/// Defined in Domain layer for use by regression detection.
/// </summary>
public sealed class AggregatedMetricsForAnalysis
{
    public required Guid FingerprintId { get; init; }
    public required TimeWindow Window { get; init; }
    public required int SampleCount { get; init; }
    public required long TotalExecutions { get; init; }

    // Duration metrics (microseconds)
    public required long AvgDurationUs { get; init; }
    public long? P50DurationUs { get; init; }
    public long? P95DurationUs { get; init; }
    public long? P99DurationUs { get; init; }

    // CPU metrics (microseconds)
    public required long AvgCpuTimeUs { get; init; }
    public long? P95CpuTimeUs { get; init; }

    // I/O metrics
    public required long AvgLogicalReads { get; init; }

    /// <summary>
    /// Convenience: P95 duration in milliseconds
    /// </summary>
    public double? P95DurationMs => P95DurationUs / 1000.0;

    /// <summary>
    /// Convenience: P95 CPU time in milliseconds
    /// </summary>
    public double? P95CpuTimeMs => P95CpuTimeUs / 1000.0;
}

/// <summary>
/// Domain service that detects performance regressions by comparing
/// current metrics against established baselines.
/// </summary>
/// <remarks>
/// This is a pure domain service - no I/O, no infrastructure dependencies.
/// It encapsulates the core regression detection algorithm.
/// </remarks>
public interface IRegressionDetector
{
    /// <summary>
    /// Analyzes recent samples against a baseline to detect regressions.
    /// </summary>
    /// <param name="baseline">The established performance baseline for a query</param>
    /// <param name="recentSamples">Recent performance samples to analyze</param>
    /// <param name="rules">Detection rules (thresholds) to apply</param>
    /// <returns>Zero or more regression events if thresholds are exceeded</returns>
    IReadOnlyList<RegressionEvent> DetectRegressions(
        PlanBaseline baseline,
        IReadOnlyList<MetricSample> recentSamples,
        RegressionDetectionRules rules);

    /// <summary>
    /// Analyzes a single aggregated metric snapshot against a baseline.
    /// Use this when samples are already aggregated.
    /// </summary>
    /// <param name="baseline">The established performance baseline</param>
    /// <param name="currentMetrics">Current aggregated metrics</param>
    /// <param name="rules">Detection rules to apply</param>
    /// <returns>A regression event if thresholds exceeded, null otherwise</returns>
    RegressionEvent? DetectRegression(
        PlanBaseline baseline,
        AggregatedMetricsForAnalysis currentMetrics,
        RegressionDetectionRules rules);
}

/// <summary>
/// Configurable thresholds for regression detection.
/// </summary>
public sealed class RegressionDetectionRules
{
    /// <summary>
    /// Percentage increase in P95 duration that triggers a regression.
    /// Example: 50 means current P95 must be 150% of baseline P95.
    /// </summary>
    public decimal DurationIncreaseThresholdPercent { get; init; } = 50;

    /// <summary>
    /// Percentage increase in P95 CPU time that triggers a regression.
    /// </summary>
    public decimal CpuIncreaseThresholdPercent { get; init; } = 50;

    /// <summary>
    /// Percentage increase in average logical reads that triggers a regression.
    /// </summary>
    public decimal LogicalReadsIncreaseThresholdPercent { get; init; } = 100;

    /// <summary>
    /// Minimum number of executions in the sample period to consider.
    /// Prevents false positives from low-volume queries.
    /// </summary>
    public int MinimumExecutions { get; init; } = 5;

    /// <summary>
    /// Minimum baseline sample count required before we trust the baseline.
    /// </summary>
    public int MinimumBaselineSamples { get; init; } = 10;

    /// <summary>
    /// Whether to require multiple metrics to regress (AND) or any metric (OR).
    /// </summary>
    public bool RequireMultipleMetrics { get; init; } = false;

    /// <summary>
    /// Calculate the threshold multiplier for a percentage increase.
    /// Example: 50% increase = 1.5 multiplier
    /// </summary>
    public static decimal ToMultiplier(decimal percentIncrease) => 1 + (percentIncrease / 100);
}

/// <summary>
/// A sample of metrics for regression analysis.
/// This is a simplified view used by the domain service.
/// </summary>
public sealed class MetricSample
{
    public required DateTime SampledAtUtc { get; init; }
    public required long ExecutionCount { get; init; }
    public required long AvgDurationUs { get; init; }
    public required long AvgCpuTimeUs { get; init; }
    public required long AvgLogicalReads { get; init; }
    public long? P95DurationUs { get; init; }
    public long? P95CpuTimeUs { get; init; }
}
