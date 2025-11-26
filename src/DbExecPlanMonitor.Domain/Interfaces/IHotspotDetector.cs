using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Domain.Interfaces;

/// <summary>
/// Identifies resource-intensive queries (hotspots) from metric data.
/// </summary>
/// <remarks>
/// This is a pure domain service - no I/O, no database access.
/// 
/// Hotspot detection answers: "What's consuming the most resources right now?"
/// Unlike regression detection (comparison to baseline), this is about
/// absolute resource consumption.
/// </remarks>
public interface IHotspotDetector
{
    /// <summary>
    /// Identifies the top N queries by a specific metric.
    /// </summary>
    /// <param name="database">The database to analyze.</param>
    /// <param name="samples">Recent metric samples for all queries.</param>
    /// <param name="metricType">Which metric to rank by.</param>
    /// <param name="topN">How many top queries to return.</param>
    /// <param name="windowStart">Start of the analysis window.</param>
    /// <param name="windowEnd">End of the analysis window.</param>
    /// <returns>List of hotspots, ranked by the specified metric.</returns>
    IReadOnlyList<Hotspot> DetectHotspots(
        MonitoredDatabase database,
        IEnumerable<PlanMetricSample> samples,
        HotspotMetricType metricType,
        int topN,
        DateTime windowStart,
        DateTime windowEnd);

    /// <summary>
    /// Detects hotspots across multiple metric types.
    /// A query appearing in multiple lists is more concerning.
    /// </summary>
    /// <param name="database">The database to analyze.</param>
    /// <param name="samples">Recent metric samples.</param>
    /// <param name="options">Detection options.</param>
    /// <returns>Consolidated list of hotspots with their rankings.</returns>
    IReadOnlyList<Hotspot> DetectHotspots(
        MonitoredDatabase database,
        IEnumerable<PlanMetricSample> samples,
        HotspotDetectionOptions options);

    /// <summary>
    /// Calculates resource consumption summary for a database.
    /// Provides the denominator for percentage calculations.
    /// </summary>
    /// <param name="samples">All metric samples in the window.</param>
    /// <returns>Summary of total resource consumption.</returns>
    ResourceConsumptionSummary CalculateResourceSummary(
        IEnumerable<PlanMetricSample> samples);

    /// <summary>
    /// Determines if a query should be flagged as a hotspot based on thresholds.
    /// </summary>
    /// <param name="fingerprint">The query to evaluate.</param>
    /// <param name="samples">Recent samples for this query.</param>
    /// <param name="totalResources">Total resources across all queries.</param>
    /// <param name="options">Detection options with thresholds.</param>
    /// <returns>True if this query meets hotspot criteria.</returns>
    bool IsHotspot(
        QueryFingerprint fingerprint,
        IEnumerable<PlanMetricSample> samples,
        ResourceConsumptionSummary totalResources,
        HotspotDetectionOptions options);
}

/// <summary>
/// Configuration options for hotspot detection.
/// </summary>
public class HotspotDetectionOptions
{
    /// <summary>
    /// How many top queries to identify per metric.
    /// </summary>
    public int TopN { get; set; } = 10;

    /// <summary>
    /// Time window for analysis.
    /// </summary>
    public TimeSpan AnalysisWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Minimum percentage of total resources to be considered a hotspot.
    /// Default 5% means a query must use at least 5% of total CPU/reads/etc.
    /// </summary>
    public double MinimumPercentageOfTotal { get; set; } = 0.05;

    /// <summary>
    /// Which metrics to analyze.
    /// </summary>
    public IReadOnlyList<HotspotMetricType> MetricsToAnalyze { get; set; } = new[]
    {
        HotspotMetricType.TotalCpuTime,
        HotspotMetricType.TotalLogicalReads,
        HotspotMetricType.TotalDuration
    };

    /// <summary>
    /// Minimum execution count for a query to be considered.
    /// Filters out rare queries that happened to be expensive once.
    /// </summary>
    public int MinimumExecutionCount { get; set; } = 10;

    /// <summary>
    /// Whether to exclude queries marked as "expected" hotspots.
    /// </summary>
    public bool ExcludeExpectedHotspots { get; set; } = true;
}

/// <summary>
/// Summary of total resource consumption in an analysis window.
/// Used as denominator for percentage calculations.
/// </summary>
public class ResourceConsumptionSummary
{
    /// <summary>
    /// Total CPU time across all queries (ms).
    /// </summary>
    public double TotalCpuTimeMs { get; set; }

    /// <summary>
    /// Total duration across all queries (ms).
    /// </summary>
    public double TotalDurationMs { get; set; }

    /// <summary>
    /// Total logical reads across all queries.
    /// </summary>
    public double TotalLogicalReads { get; set; }

    /// <summary>
    /// Total physical reads across all queries.
    /// </summary>
    public double TotalPhysicalReads { get; set; }

    /// <summary>
    /// Total memory grants across all queries (KB).
    /// </summary>
    public double TotalMemoryGrantKb { get; set; }

    /// <summary>
    /// Total execution count across all queries.
    /// </summary>
    public long TotalExecutionCount { get; set; }

    /// <summary>
    /// Number of distinct queries analyzed.
    /// </summary>
    public int QueryCount { get; set; }

    /// <summary>
    /// Start of the analysis window.
    /// </summary>
    public DateTime WindowStartUtc { get; set; }

    /// <summary>
    /// End of the analysis window.
    /// </summary>
    public DateTime WindowEndUtc { get; set; }

    /// <summary>
    /// Calculates what percentage a given value is of the total for a metric.
    /// </summary>
    public double GetPercentage(HotspotMetricType metricType, double value)
    {
        var total = metricType switch
        {
            HotspotMetricType.TotalCpuTime => TotalCpuTimeMs,
            HotspotMetricType.TotalDuration => TotalDurationMs,
            HotspotMetricType.TotalLogicalReads => TotalLogicalReads,
            HotspotMetricType.TotalPhysicalReads => TotalPhysicalReads,
            HotspotMetricType.TotalMemoryGrant => TotalMemoryGrantKb,
            HotspotMetricType.ExecutionCount => TotalExecutionCount,
            _ => 0
        };

        return total > 0 ? value / total : 0;
    }
}
