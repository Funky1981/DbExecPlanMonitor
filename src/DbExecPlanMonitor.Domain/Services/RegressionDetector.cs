using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using DbExecPlanMonitor.Domain.ValueObjects;

namespace DbExecPlanMonitor.Domain.Services;

/// <summary>
/// Default implementation of regression detection logic.
/// Compares current metrics against baseline using configurable thresholds.
/// </summary>
public sealed class RegressionDetector : IRegressionDetector
{
    /// <inheritdoc />
    public IReadOnlyList<RegressionEvent> DetectRegressions(
        PlanBaseline baseline,
        IReadOnlyList<MetricSample> recentSamples,
        RegressionDetectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(recentSamples);
        ArgumentNullException.ThrowIfNull(rules);

        if (recentSamples.Count == 0)
            return [];

        // Aggregate recent samples
        var aggregated = AggregateSamples(baseline.FingerprintId, recentSamples);

        var regression = DetectRegression(baseline, aggregated, rules);
        
        return regression != null ? [regression] : [];
    }

    /// <inheritdoc />
    public RegressionEvent? DetectRegression(
        PlanBaseline baseline,
        AggregatedMetricsForAnalysis currentMetrics,
        RegressionDetectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(currentMetrics);
        ArgumentNullException.ThrowIfNull(rules);

        // Pre-condition checks
        if (!IsBaselineReliable(baseline, rules))
            return null;

        if (!HasSufficientExecutions(currentMetrics, rules))
            return null;

        // Check each metric for regression
        var regressions = new List<(string Metric, decimal BaselineValue, decimal CurrentValue, decimal PercentIncrease)>();

        // Duration regression check
        if (baseline.P95DurationUs.HasValue && currentMetrics.P95DurationUs.HasValue)
        {
            var increase = CalculatePercentIncrease(baseline.P95DurationUs.Value, currentMetrics.P95DurationUs.Value);
            if (increase >= rules.DurationIncreaseThresholdPercent)
            {
                regressions.Add(("P95Duration", baseline.P95DurationUs.Value, currentMetrics.P95DurationUs.Value, increase));
            }
        }

        // CPU regression check
        if (baseline.P95CpuTimeUs.HasValue && currentMetrics.P95CpuTimeUs.HasValue)
        {
            var increase = CalculatePercentIncrease(baseline.P95CpuTimeUs.Value, currentMetrics.P95CpuTimeUs.Value);
            if (increase >= rules.CpuIncreaseThresholdPercent)
            {
                regressions.Add(("P95CpuTime", baseline.P95CpuTimeUs.Value, currentMetrics.P95CpuTimeUs.Value, increase));
            }
        }

        // Logical reads regression check
        if (baseline.AvgLogicalReads > 0 && currentMetrics.AvgLogicalReads > 0)
        {
            var increase = CalculatePercentIncrease(baseline.AvgLogicalReads, currentMetrics.AvgLogicalReads);
            if (increase >= rules.LogicalReadsIncreaseThresholdPercent)
            {
                regressions.Add(("AvgLogicalReads", baseline.AvgLogicalReads, currentMetrics.AvgLogicalReads, increase));
            }
        }

        // Apply regression logic (AND vs OR)
        if (regressions.Count == 0)
            return null;

        if (rules.RequireMultipleMetrics && regressions.Count < 2)
            return null;

        // Create regression event
        var severity = DetermineSeverity(regressions);
        var primaryRegression = regressions.OrderByDescending(r => r.PercentIncrease).First();

        return new RegressionEvent
        {
            Id = Guid.NewGuid(),
            FingerprintId = baseline.FingerprintId,
            InstanceName = baseline.InstanceName,
            DatabaseName = baseline.DatabaseName,
            DetectedAtUtc = DateTime.UtcNow,
            Severity = severity,
            Status = RegressionStatus.New,
            
            // Baseline values
            BaselineP95DurationUs = baseline.P95DurationUs,
            BaselineP95CpuTimeUs = baseline.P95CpuTimeUs,
            BaselineAvgLogicalReads = baseline.AvgLogicalReads,
            
            // Current values
            CurrentP95DurationUs = currentMetrics.P95DurationUs,
            CurrentP95CpuTimeUs = currentMetrics.P95CpuTimeUs,
            CurrentAvgLogicalReads = currentMetrics.AvgLogicalReads,
            
            // Change calculations
            DurationChangePercent = baseline.P95DurationUs.HasValue && currentMetrics.P95DurationUs.HasValue
                ? CalculatePercentIncrease(baseline.P95DurationUs.Value, currentMetrics.P95DurationUs.Value)
                : null,
            CpuChangePercent = baseline.P95CpuTimeUs.HasValue && currentMetrics.P95CpuTimeUs.HasValue
                ? CalculatePercentIncrease(baseline.P95CpuTimeUs.Value, currentMetrics.P95CpuTimeUs.Value)
                : null,
            
            Description = BuildDescription(primaryRegression, regressions.Count),
            SampleWindowStart = currentMetrics.Window.StartUtc,
            SampleWindowEnd = currentMetrics.Window.EndUtc
        };
    }

    /// <summary>
    /// Checks if the baseline has enough samples to be reliable.
    /// </summary>
    private static bool IsBaselineReliable(PlanBaseline baseline, RegressionDetectionRules rules)
    {
        return baseline.SampleCount >= rules.MinimumBaselineSamples;
    }

    /// <summary>
    /// Checks if current metrics have sufficient executions.
    /// </summary>
    private static bool HasSufficientExecutions(AggregatedMetricsForAnalysis metrics, RegressionDetectionRules rules)
    {
        return metrics.TotalExecutions >= rules.MinimumExecutions;
    }

    /// <summary>
    /// Calculates percentage increase from baseline to current.
    /// </summary>
    private static decimal CalculatePercentIncrease(long baseline, long current)
    {
        if (baseline <= 0) return 0;
        return ((decimal)(current - baseline) / baseline) * 100;
    }

    /// <summary>
    /// Determines severity based on the magnitude of regressions.
    /// </summary>
    private static RegressionSeverity DetermineSeverity(
        List<(string Metric, decimal BaselineValue, decimal CurrentValue, decimal PercentIncrease)> regressions)
    {
        var maxIncrease = regressions.Max(r => r.PercentIncrease);

        return maxIncrease switch
        {
            >= 500 => RegressionSeverity.Critical,  // 6x or more
            >= 200 => RegressionSeverity.High,      // 3x or more
            >= 100 => RegressionSeverity.Medium,    // 2x or more
            _ => RegressionSeverity.Low
        };
    }

    /// <summary>
    /// Builds a human-readable description of the regression.
    /// </summary>
    private static string BuildDescription(
        (string Metric, decimal BaselineValue, decimal CurrentValue, decimal PercentIncrease) primary,
        int totalRegressions)
    {
        var desc = $"{primary.Metric} increased by {primary.PercentIncrease:F0}% " +
                   $"(baseline: {FormatMetricValue(primary.Metric, primary.BaselineValue)}, " +
                   $"current: {FormatMetricValue(primary.Metric, primary.CurrentValue)})";

        if (totalRegressions > 1)
        {
            desc += $" and {totalRegressions - 1} other metric(s)";
        }

        return desc;
    }

    /// <summary>
    /// Formats a metric value for human readability.
    /// </summary>
    private static string FormatMetricValue(string metric, decimal value)
    {
        if (metric.Contains("Duration") || metric.Contains("Cpu"))
        {
            // Microseconds to milliseconds
            return $"{value / 1000:F1}ms";
        }
        
        return value.ToString("N0");
    }

    /// <summary>
    /// Aggregates multiple samples into a single metric summary.
    /// </summary>
    private static AggregatedMetricsForAnalysis AggregateSamples(Guid fingerprintId, IReadOnlyList<MetricSample> samples)
    {
        var totalExecutions = samples.Sum(s => s.ExecutionCount);
        var avgDuration = (long)samples.Average(s => s.AvgDurationUs);
        var avgCpu = (long)samples.Average(s => s.AvgCpuTimeUs);
        var avgReads = (long)samples.Average(s => s.AvgLogicalReads);

        // Calculate P95 from available P95 values, or estimate from avg
        var p95Durations = samples.Where(s => s.P95DurationUs.HasValue).Select(s => s.P95DurationUs!.Value).ToList();
        var p95Duration = p95Durations.Count > 0 
            ? (long?)p95Durations.OrderByDescending(x => x).Skip((int)(p95Durations.Count * 0.05)).FirstOrDefault()
            : null;

        var p95CpuTimes = samples.Where(s => s.P95CpuTimeUs.HasValue).Select(s => s.P95CpuTimeUs!.Value).ToList();
        var p95Cpu = p95CpuTimes.Count > 0
            ? (long?)p95CpuTimes.OrderByDescending(x => x).Skip((int)(p95CpuTimes.Count * 0.05)).FirstOrDefault()
            : null;

        var minTime = samples.Min(s => s.SampledAtUtc);
        var maxTime = samples.Max(s => s.SampledAtUtc);

        return new AggregatedMetricsForAnalysis
        {
            FingerprintId = fingerprintId,
            Window = new TimeWindow(minTime, maxTime),
            SampleCount = samples.Count,
            TotalExecutions = totalExecutions,
            AvgDurationUs = avgDuration,
            P95DurationUs = p95Duration,
            AvgCpuTimeUs = avgCpu,
            P95CpuTimeUs = p95Cpu,
            AvgLogicalReads = avgReads
        };
    }
}
