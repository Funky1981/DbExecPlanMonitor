namespace DbExecPlanMonitor.Worker.Scheduling;

/// <summary>
/// Configuration options for the scheduling of background jobs.
/// </summary>
public sealed class SchedulingOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Scheduling";

    /// <summary>
    /// Whether plan collection is enabled.
    /// </summary>
    public bool CollectionEnabled { get; set; } = true;

    /// <summary>
    /// Interval between plan collection runs.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initial delay before first collection run after service start.
    /// Allows other services to initialize.
    /// </summary>
    public TimeSpan CollectionStartupDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether analysis (regression/hotspot detection) is enabled.
    /// </summary>
    public bool AnalysisEnabled { get; set; } = true;

    /// <summary>
    /// Interval between analysis runs.
    /// Default: 5 minutes (runs after collection).
    /// </summary>
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initial delay before first analysis run.
    /// Should be after first collection completes.
    /// </summary>
    public TimeSpan AnalysisStartupDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether daily baseline rebuild is enabled.
    /// </summary>
    public bool BaselineRebuildEnabled { get; set; } = true;

    /// <summary>
    /// Time of day to run baseline rebuild (UTC).
    /// Default: 2:00 AM UTC.
    /// </summary>
    public TimeSpan BaselineRebuildTimeOfDay { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Whether daily summary reporting is enabled.
    /// </summary>
    public bool DailySummaryEnabled { get; set; } = true;

    /// <summary>
    /// Time of day to send daily summary (UTC).
    /// Default: 8:00 AM UTC.
    /// </summary>
    public TimeSpan DailySummaryTimeOfDay { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Maximum number of consecutive failures before pausing a job.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Backoff duration after a job failure.
    /// </summary>
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum backoff duration after repeated failures.
    /// </summary>
    public TimeSpan MaxFailureBackoff { get; set; } = TimeSpan.FromMinutes(10);
}
