using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Domain.Interfaces;

/// <summary>
/// Detects performance regressions by comparing current metrics against baselines.
/// </summary>
/// <remarks>
/// This is a pure domain service - no I/O, no database access.
/// It operates on domain entities passed to it.
/// 
/// The implementation will contain the regression detection algorithms:
/// - Threshold-based comparison
/// - Statistical deviation detection
/// - Plan change detection
/// </remarks>
public interface IRegressionDetector
{
    /// <summary>
    /// Analyzes a query's recent metrics against its baseline to detect regressions.
    /// </summary>
    /// <param name="fingerprint">The query fingerprint to analyze.</param>
    /// <param name="recentSamples">Recent metric samples to compare against baseline.</param>
    /// <returns>A regression event if detected, null otherwise.</returns>
    RegressionEvent? DetectRegression(
        QueryFingerprint fingerprint,
        IEnumerable<PlanMetricSample> recentSamples);

    /// <summary>
    /// Analyzes multiple queries for regressions in batch.
    /// More efficient than calling DetectRegression for each query.
    /// </summary>
    /// <param name="fingerprints">The query fingerprints to analyze (with loaded baselines/samples).</param>
    /// <returns>List of detected regression events.</returns>
    IReadOnlyList<RegressionEvent> DetectRegressions(
        IEnumerable<QueryFingerprint> fingerprints);

    /// <summary>
    /// Checks if a specific metric sample represents a regression.
    /// Quick check without creating a full RegressionEvent.
    /// </summary>
    /// <param name="baseline">The baseline to compare against.</param>
    /// <param name="sample">The current sample.</param>
    /// <returns>Result indicating if regression occurred and severity.</returns>
    RegressionCheckResult CheckSample(
        PlanBaseline baseline,
        PlanMetricSample sample);

    /// <summary>
    /// Determines if a plan change should be flagged as a potential regression.
    /// Not all plan changes are bad - this evaluates if the new plan is worse.
    /// </summary>
    /// <param name="fingerprint">The query fingerprint.</param>
    /// <param name="previousPlan">The previous execution plan.</param>
    /// <param name="currentPlan">The current/new execution plan.</param>
    /// <returns>True if the plan change appears to be a regression.</returns>
    bool IsPlanChangeRegression(
        QueryFingerprint fingerprint,
        ExecutionPlanSnapshot previousPlan,
        ExecutionPlanSnapshot currentPlan);
}

/// <summary>
/// Configuration options for regression detection.
/// </summary>
public class RegressionDetectionOptions
{
    /// <summary>
    /// Multiplier for CPU time to trigger regression.
    /// Default 2.0 means alert if 2x slower.
    /// </summary>
    public double CpuTimeThreshold { get; set; } = 2.0;

    /// <summary>
    /// Multiplier for duration to trigger regression.
    /// </summary>
    public double DurationThreshold { get; set; } = 2.0;

    /// <summary>
    /// Multiplier for logical reads to trigger regression.
    /// </summary>
    public double LogicalReadsThreshold { get; set; } = 3.0;

    /// <summary>
    /// Minimum number of samples before declaring regression.
    /// Prevents false positives from single spikes.
    /// </summary>
    public int MinimumSamplesRequired { get; set; } = 3;

    /// <summary>
    /// Whether to always flag plan changes, even without metric regression.
    /// </summary>
    public bool FlagAllPlanChanges { get; set; } = false;

    /// <summary>
    /// Time window to consider for "recent" samples.
    /// </summary>
    public TimeSpan RecentSampleWindow { get; set; } = TimeSpan.FromHours(1);
}
