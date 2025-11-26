namespace DbExecPlanMonitor.Domain.Entities;

/// <summary>
/// Represents the expected/baseline performance for a query.
/// Used to detect regressions when current metrics deviate significantly.
/// </summary>
/// <remarks>
/// A baseline captures "this is how this query should perform under normal conditions."
/// 
/// Baselines can be:
/// - Auto-generated: Calculated from the first N samples after the query stabilizes
/// - Manually set: DBA explicitly says "this is acceptable performance"
/// - Updated: When a legitimate change improves performance, update the baseline
/// 
/// Regression detection = Current metrics vs Baseline, with configurable thresholds.
/// </remarks>
public class PlanBaseline
{
    /// <summary>
    /// Unique identifier for this baseline.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Reference to the query fingerprint this baseline is for.
    /// </summary>
    public Guid QueryFingerprintId { get; private set; }

    /// <summary>
    /// Navigation property to the query fingerprint.
    /// </summary>
    public QueryFingerprint QueryFingerprint { get; private set; } = null!;

    /// <summary>
    /// Reference to the execution plan this baseline was captured from.
    /// This is the "known good" plan.
    /// </summary>
    public Guid BaselinePlanSnapshotId { get; private set; }

    /// <summary>
    /// Navigation property to the baseline plan.
    /// </summary>
    public ExecutionPlanSnapshot BaselinePlanSnapshot { get; private set; } = null!;

    /// <summary>
    /// The baseline average CPU time in milliseconds.
    /// </summary>
    public double BaselineAvgCpuTimeMs { get; private set; }

    /// <summary>
    /// The baseline average duration/elapsed time in milliseconds.
    /// </summary>
    public double BaselineAvgDurationMs { get; private set; }

    /// <summary>
    /// The baseline average logical reads.
    /// </summary>
    public double BaselineAvgLogicalReads { get; private set; }

    /// <summary>
    /// The baseline average rows returned.
    /// </summary>
    public double BaselineAvgRowsReturned { get; private set; }

    /// <summary>
    /// The baseline average physical reads (optional).
    /// </summary>
    public double? BaselineAvgPhysicalReads { get; private set; }

    /// <summary>
    /// The baseline average memory grant (optional).
    /// </summary>
    public double? BaselineAvgMemoryGrantKb { get; private set; }

    /// <summary>
    /// Threshold multiplier for CPU time regression detection.
    /// Default 2.0 means "alert if CPU time is 2x baseline."
    /// </summary>
    public double CpuTimeThresholdMultiplier { get; private set; }

    /// <summary>
    /// Threshold multiplier for duration regression detection.
    /// </summary>
    public double DurationThresholdMultiplier { get; private set; }

    /// <summary>
    /// Threshold multiplier for logical reads regression detection.
    /// </summary>
    public double LogicalReadsThresholdMultiplier { get; private set; }

    /// <summary>
    /// Minimum number of samples required before comparing to baseline.
    /// Prevents false alerts from single-sample spikes.
    /// </summary>
    public int MinimumSamplesForComparison { get; private set; }

    /// <summary>
    /// When this baseline was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// When this baseline was last updated.
    /// </summary>
    public DateTime? LastUpdatedAtUtc { get; private set; }

    /// <summary>
    /// How this baseline was established.
    /// </summary>
    public BaselineSource Source { get; private set; }

    /// <summary>
    /// Whether this baseline is currently active.
    /// Inactive baselines are kept for history but not used for comparison.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Optional notes about this baseline.
    /// </summary>
    public string? Notes { get; private set; }

    // Private constructor for EF Core
    private PlanBaseline() { }

    /// <summary>
    /// Creates a new baseline. Called by QueryFingerprint.SetBaseline().
    /// </summary>
    internal PlanBaseline(
        QueryFingerprint fingerprint,
        ExecutionPlanSnapshot planSnapshot,
        double avgCpuTimeMs,
        double avgDurationMs,
        double avgLogicalReads,
        double avgRowsReturned)
    {
        if (fingerprint == null)
            throw new ArgumentNullException(nameof(fingerprint));
        if (planSnapshot == null)
            throw new ArgumentNullException(nameof(planSnapshot));

        Id = Guid.NewGuid();
        QueryFingerprintId = fingerprint.Id;
        QueryFingerprint = fingerprint;
        BaselinePlanSnapshotId = planSnapshot.Id;
        BaselinePlanSnapshot = planSnapshot;

        BaselineAvgCpuTimeMs = avgCpuTimeMs;
        BaselineAvgDurationMs = avgDurationMs;
        BaselineAvgLogicalReads = avgLogicalReads;
        BaselineAvgRowsReturned = avgRowsReturned;

        // Default thresholds - can be tuned per query
        CpuTimeThresholdMultiplier = 2.0;      // Alert if 2x slower
        DurationThresholdMultiplier = 2.0;
        LogicalReadsThresholdMultiplier = 3.0; // More tolerant for reads (can vary with data)
        MinimumSamplesForComparison = 3;       // Need 3 samples before alerting

        CreatedAtUtc = DateTime.UtcNow;
        Source = BaselineSource.AutoGenerated;
        IsActive = true;
    }

    /// <summary>
    /// Updates the baseline metrics.
    /// Use when performance legitimately improved and you want to raise the bar.
    /// </summary>
    public void UpdateMetrics(
        double avgCpuTimeMs,
        double avgDurationMs,
        double avgLogicalReads,
        double avgRowsReturned,
        ExecutionPlanSnapshot? newPlanSnapshot = null)
    {
        BaselineAvgCpuTimeMs = avgCpuTimeMs;
        BaselineAvgDurationMs = avgDurationMs;
        BaselineAvgLogicalReads = avgLogicalReads;
        BaselineAvgRowsReturned = avgRowsReturned;
        LastUpdatedAtUtc = DateTime.UtcNow;

        if (newPlanSnapshot != null)
        {
            BaselinePlanSnapshotId = newPlanSnapshot.Id;
            BaselinePlanSnapshot = newPlanSnapshot;
        }
    }

    /// <summary>
    /// Configures the regression detection thresholds.
    /// </summary>
    public void ConfigureThresholds(
        double cpuTimeMultiplier,
        double durationMultiplier,
        double logicalReadsMultiplier,
        int minimumSamples = 3)
    {
        if (cpuTimeMultiplier < 1.1)
            throw new ArgumentException("CPU time threshold must be at least 1.1x", nameof(cpuTimeMultiplier));
        if (durationMultiplier < 1.1)
            throw new ArgumentException("Duration threshold must be at least 1.1x", nameof(durationMultiplier));
        if (logicalReadsMultiplier < 1.1)
            throw new ArgumentException("Logical reads threshold must be at least 1.1x", nameof(logicalReadsMultiplier));
        if (minimumSamples < 1)
            throw new ArgumentException("Minimum samples must be at least 1", nameof(minimumSamples));

        CpuTimeThresholdMultiplier = cpuTimeMultiplier;
        DurationThresholdMultiplier = durationMultiplier;
        LogicalReadsThresholdMultiplier = logicalReadsMultiplier;
        MinimumSamplesForComparison = minimumSamples;
        LastUpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if a metric sample represents a regression.
    /// </summary>
    public RegressionCheckResult CheckForRegression(PlanMetricSample sample)
    {
        if (sample == null)
            throw new ArgumentNullException(nameof(sample));

        var result = new RegressionCheckResult();

        // Check CPU time
        if (BaselineAvgCpuTimeMs > 0)
        {
            var cpuRatio = sample.AvgCpuTimeMs / BaselineAvgCpuTimeMs;
            if (cpuRatio >= CpuTimeThresholdMultiplier)
            {
                result.IsCpuTimeRegression = true;
                result.CpuTimeRatio = cpuRatio;
            }
        }

        // Check duration
        if (BaselineAvgDurationMs > 0)
        {
            var durationRatio = sample.AvgDurationMs / BaselineAvgDurationMs;
            if (durationRatio >= DurationThresholdMultiplier)
            {
                result.IsDurationRegression = true;
                result.DurationRatio = durationRatio;
            }
        }

        // Check logical reads
        if (BaselineAvgLogicalReads > 0)
        {
            var readsRatio = sample.AvgLogicalReads / BaselineAvgLogicalReads;
            if (readsRatio >= LogicalReadsThresholdMultiplier)
            {
                result.IsLogicalReadsRegression = true;
                result.LogicalReadsRatio = readsRatio;
            }
        }

        // Check if the plan changed
        result.IsPlanChange = sample.ExecutionPlanSnapshotId != BaselinePlanSnapshotId;

        return result;
    }

    /// <summary>
    /// Marks this baseline as manually verified by a DBA.
    /// </summary>
    public void MarkAsManuallySet(string? notes = null)
    {
        Source = BaselineSource.ManuallySet;
        LastUpdatedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes;
        }
    }

    /// <summary>
    /// Deactivates this baseline (keeps for history).
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        LastUpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Reactivates this baseline.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
        LastUpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a note about this baseline.
    /// </summary>
    public void AddNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return;

        Notes = string.IsNullOrWhiteSpace(Notes)
            ? $"[{DateTime.UtcNow:u}] {note}"
            : $"{Notes}\n[{DateTime.UtcNow:u}] {note}";
        LastUpdatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// How the baseline was established.
/// </summary>
public enum BaselineSource
{
    /// <summary>
    /// Automatically calculated from initial samples.
    /// </summary>
    AutoGenerated,

    /// <summary>
    /// Manually set by a DBA.
    /// </summary>
    ManuallySet,

    /// <summary>
    /// Imported from Query Store recommendations.
    /// </summary>
    QueryStoreImport,

    /// <summary>
    /// Copied from another environment (e.g., production baseline applied to staging).
    /// </summary>
    CopiedFromEnvironment
}

/// <summary>
/// Result of checking a sample against a baseline.
/// </summary>
public class RegressionCheckResult
{
    /// <summary>
    /// Whether CPU time exceeded the threshold.
    /// </summary>
    public bool IsCpuTimeRegression { get; set; }

    /// <summary>
    /// The ratio of current CPU time to baseline (e.g., 3.5 = 3.5x slower).
    /// </summary>
    public double CpuTimeRatio { get; set; }

    /// <summary>
    /// Whether duration exceeded the threshold.
    /// </summary>
    public bool IsDurationRegression { get; set; }

    /// <summary>
    /// The ratio of current duration to baseline.
    /// </summary>
    public double DurationRatio { get; set; }

    /// <summary>
    /// Whether logical reads exceeded the threshold.
    /// </summary>
    public bool IsLogicalReadsRegression { get; set; }

    /// <summary>
    /// The ratio of current reads to baseline.
    /// </summary>
    public double LogicalReadsRatio { get; set; }

    /// <summary>
    /// Whether the execution plan changed from baseline.
    /// Plan changes are often the root cause of regressions.
    /// </summary>
    public bool IsPlanChange { get; set; }

    /// <summary>
    /// Whether ANY regression was detected.
    /// </summary>
    public bool HasRegression => IsCpuTimeRegression || IsDurationRegression || IsLogicalReadsRegression;

    /// <summary>
    /// Gets the most significant regression ratio.
    /// </summary>
    public double MaxRegressionRatio => Math.Max(Math.Max(CpuTimeRatio, DurationRatio), LogicalReadsRatio);

    /// <summary>
    /// Gets a summary description of the regression.
    /// </summary>
    public string GetSummary()
    {
        if (!HasRegression)
            return "No regression detected";

        var parts = new List<string>();

        if (IsCpuTimeRegression)
            parts.Add($"CPU {CpuTimeRatio:F1}x");
        if (IsDurationRegression)
            parts.Add($"Duration {DurationRatio:F1}x");
        if (IsLogicalReadsRegression)
            parts.Add($"Reads {LogicalReadsRatio:F1}x");
        if (IsPlanChange)
            parts.Add("Plan changed");

        return string.Join(", ", parts);
    }
}
