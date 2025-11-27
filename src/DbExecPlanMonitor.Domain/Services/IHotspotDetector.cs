using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Domain.Services;

/// <summary>
/// Domain service that identifies resource-intensive queries (hotspots)
/// based on recent performance metrics.
/// </summary>
/// <remarks>
/// A hotspot is a query that consumes significant resources in the current window,
/// regardless of whether it's regressed from baseline. These are the "usual suspects"
/// for performance tuning.
/// </remarks>
public interface IHotspotDetector
{
    /// <summary>
    /// Identifies hotspot queries from a collection of metric samples.
    /// </summary>
    /// <param name="samples">Recent metric samples to analyze</param>
    /// <param name="rules">Detection rules defining thresholds</param>
    /// <returns>Ordered list of hotspots (most impactful first)</returns>
    IReadOnlyList<Hotspot> DetectHotspots(
        IReadOnlyList<HotspotMetricSample> samples,
        HotspotDetectionRules rules);
}

/// <summary>
/// Configurable rules for hotspot detection.
/// </summary>
public sealed class HotspotDetectionRules
{
    /// <summary>
    /// Maximum number of hotspots to return.
    /// </summary>
    public int TopN { get; init; } = 20;

    /// <summary>
    /// Minimum total CPU time (ms) across all executions to qualify.
    /// Filters out low-impact queries even if they're individually slow.
    /// </summary>
    public double MinTotalCpuMs { get; init; } = 1000;

    /// <summary>
    /// Minimum total elapsed time (ms) across all executions to qualify.
    /// </summary>
    public double MinTotalDurationMs { get; init; } = 5000;

    /// <summary>
    /// Minimum execution count to qualify.
    /// Filters out rarely-executed queries.
    /// </summary>
    public int MinExecutionCount { get; init; } = 10;

    /// <summary>
    /// Minimum average duration (ms) per execution to qualify.
    /// Filters out fast queries even if frequently executed.
    /// </summary>
    public double MinAvgDurationMs { get; init; } = 100;

    /// <summary>
    /// Which metric to use for ranking hotspots.
    /// </summary>
    public HotspotRankingMetric RankBy { get; init; } = HotspotRankingMetric.TotalCpuTime;

    /// <summary>
    /// Whether to include queries that already have regression events.
    /// Set to false to see "new" hotspots not already flagged.
    /// </summary>
    public bool IncludeQueriesWithRegressions { get; init; } = true;
}

/// <summary>
/// Metric used to rank hotspots.
/// </summary>
public enum HotspotRankingMetric
{
    /// <summary>
    /// Rank by total CPU time across all executions.
    /// </summary>
    TotalCpuTime,

    /// <summary>
    /// Rank by total elapsed time across all executions.
    /// </summary>
    TotalDuration,

    /// <summary>
    /// Rank by total logical reads across all executions.
    /// </summary>
    TotalLogicalReads,

    /// <summary>
    /// Rank by average duration per execution.
    /// </summary>
    AvgDuration,

    /// <summary>
    /// Rank by execution count (frequency).
    /// </summary>
    ExecutionCount
}

/// <summary>
/// Input sample for hotspot detection.
/// </summary>
public sealed class HotspotMetricSample
{
    public required Guid FingerprintId { get; init; }
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required string QueryTextSample { get; init; }

    public required long ExecutionCount { get; init; }
    public required double TotalCpuTimeMs { get; init; }
    public required double TotalDurationMs { get; init; }
    public required long TotalLogicalReads { get; init; }
    public required double AvgDurationMs { get; init; }
    public required double AvgCpuTimeMs { get; init; }

    public byte[]? PlanHash { get; init; }
    public bool HasActiveRegression { get; init; }
}

/// <summary>
/// A detected hotspot query.
/// </summary>
public sealed class Hotspot
{
    public required Guid FingerprintId { get; init; }
    public required string InstanceName { get; init; }
    public required string DatabaseName { get; init; }
    public required string QueryTextSample { get; init; }

    /// <summary>
    /// Rank position (1 = top hotspot).
    /// </summary>
    public required int Rank { get; init; }

    /// <summary>
    /// The metric used for ranking.
    /// </summary>
    public required HotspotRankingMetric RankedBy { get; init; }

    /// <summary>
    /// The value of the ranking metric.
    /// </summary>
    public required double RankingValue { get; init; }

    // Metrics
    public required long ExecutionCount { get; init; }
    public required double TotalCpuTimeMs { get; init; }
    public required double TotalDurationMs { get; init; }
    public required long TotalLogicalReads { get; init; }
    public required double AvgDurationMs { get; init; }
    public required double AvgCpuTimeMs { get; init; }

    /// <summary>
    /// Percentage of total resource usage this hotspot represents.
    /// </summary>
    public double PercentOfTotal { get; init; }

    /// <summary>
    /// Whether this query already has an active regression event.
    /// </summary>
    public bool HasActiveRegression { get; init; }

    public byte[]? PlanHash { get; init; }

    /// <summary>
    /// Time window the hotspot was detected in.
    /// </summary>
    public TimeWindow? DetectionWindow { get; init; }
}
