namespace DbExecPlanMonitor.Domain.Services;

/// <summary>
/// Default implementation of hotspot detection.
/// Identifies resource-intensive queries based on configurable thresholds.
/// </summary>
public sealed class HotspotDetector : IHotspotDetector
{
    /// <inheritdoc />
    public IReadOnlyList<Hotspot> DetectHotspots(
        IReadOnlyList<HotspotMetricSample> samples,
        HotspotDetectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(rules);

        if (samples.Count == 0)
            return [];

        // Filter samples that meet minimum thresholds
        var candidates = samples
            .Where(s => MeetsMinimumThresholds(s, rules))
            .Where(s => rules.IncludeQueriesWithRegressions || !s.HasActiveRegression)
            .ToList();

        if (candidates.Count == 0)
            return [];

        // Calculate totals for percentage calculations
        var totalCpu = candidates.Sum(s => s.TotalCpuTimeMs);
        var totalDuration = candidates.Sum(s => s.TotalDurationMs);
        var totalReads = candidates.Sum(s => s.TotalLogicalReads);

        // Sort by ranking metric and take top N
        var ranked = SortByRankingMetric(candidates, rules.RankBy)
            .Take(rules.TopN)
            .Select((sample, index) => CreateHotspot(
                sample, 
                index + 1, 
                rules.RankBy,
                totalCpu,
                totalDuration,
                totalReads))
            .ToList();

        return ranked;
    }

    /// <summary>
    /// Checks if a sample meets all minimum thresholds.
    /// </summary>
    private static bool MeetsMinimumThresholds(HotspotMetricSample sample, HotspotDetectionRules rules)
    {
        return sample.TotalCpuTimeMs >= rules.MinTotalCpuMs
            && sample.TotalDurationMs >= rules.MinTotalDurationMs
            && sample.ExecutionCount >= rules.MinExecutionCount
            && sample.AvgDurationMs >= rules.MinAvgDurationMs;
    }

    /// <summary>
    /// Sorts samples by the specified ranking metric.
    /// </summary>
    private static IEnumerable<HotspotMetricSample> SortByRankingMetric(
        IEnumerable<HotspotMetricSample> samples,
        HotspotRankingMetric rankBy)
    {
        return rankBy switch
        {
            HotspotRankingMetric.TotalCpuTime => samples.OrderByDescending(s => s.TotalCpuTimeMs),
            HotspotRankingMetric.TotalDuration => samples.OrderByDescending(s => s.TotalDurationMs),
            HotspotRankingMetric.TotalLogicalReads => samples.OrderByDescending(s => s.TotalLogicalReads),
            HotspotRankingMetric.AvgDuration => samples.OrderByDescending(s => s.AvgDurationMs),
            HotspotRankingMetric.ExecutionCount => samples.OrderByDescending(s => s.ExecutionCount),
            _ => samples.OrderByDescending(s => s.TotalCpuTimeMs)
        };
    }

    /// <summary>
    /// Creates a Hotspot from a sample with ranking information.
    /// </summary>
    private static Hotspot CreateHotspot(
        HotspotMetricSample sample,
        int rank,
        HotspotRankingMetric rankBy,
        double totalCpu,
        double totalDuration,
        long totalReads)
    {
        var rankingValue = GetRankingValue(sample, rankBy);
        var percentOfTotal = CalculatePercentOfTotal(sample, rankBy, totalCpu, totalDuration, totalReads);

        return new Hotspot
        {
            FingerprintId = sample.FingerprintId,
            InstanceName = sample.InstanceName,
            DatabaseName = sample.DatabaseName,
            QueryTextSample = sample.QueryTextSample,
            Rank = rank,
            RankedBy = rankBy,
            RankingValue = rankingValue,
            ExecutionCount = sample.ExecutionCount,
            TotalCpuTimeMs = sample.TotalCpuTimeMs,
            TotalDurationMs = sample.TotalDurationMs,
            TotalLogicalReads = sample.TotalLogicalReads,
            AvgDurationMs = sample.AvgDurationMs,
            AvgCpuTimeMs = sample.AvgCpuTimeMs,
            PercentOfTotal = percentOfTotal,
            HasActiveRegression = sample.HasActiveRegression,
            PlanHash = sample.PlanHash
        };
    }

    /// <summary>
    /// Gets the value of the ranking metric for a sample.
    /// </summary>
    private static double GetRankingValue(HotspotMetricSample sample, HotspotRankingMetric rankBy)
    {
        return rankBy switch
        {
            HotspotRankingMetric.TotalCpuTime => sample.TotalCpuTimeMs,
            HotspotRankingMetric.TotalDuration => sample.TotalDurationMs,
            HotspotRankingMetric.TotalLogicalReads => sample.TotalLogicalReads,
            HotspotRankingMetric.AvgDuration => sample.AvgDurationMs,
            HotspotRankingMetric.ExecutionCount => sample.ExecutionCount,
            _ => sample.TotalCpuTimeMs
        };
    }

    /// <summary>
    /// Calculates what percentage of total resources this sample represents.
    /// </summary>
    private static double CalculatePercentOfTotal(
        HotspotMetricSample sample,
        HotspotRankingMetric rankBy,
        double totalCpu,
        double totalDuration,
        long totalReads)
    {
        return rankBy switch
        {
            HotspotRankingMetric.TotalCpuTime when totalCpu > 0 
                => (sample.TotalCpuTimeMs / totalCpu) * 100,
            HotspotRankingMetric.TotalDuration when totalDuration > 0 
                => (sample.TotalDurationMs / totalDuration) * 100,
            HotspotRankingMetric.TotalLogicalReads when totalReads > 0 
                => ((double)sample.TotalLogicalReads / totalReads) * 100,
            _ => 0
        };
    }
}
