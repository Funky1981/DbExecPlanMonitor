namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Configuration for job scheduling.
/// </summary>
/// <remarks>
/// Provides centralized configuration for all background jobs.
/// Maps to the "Monitoring:Jobs" or "Scheduling" section in appsettings.json.
/// 
/// Individual jobs can be enabled/disabled via feature flags.
/// </remarks>
public sealed class JobScheduleOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Scheduling";

    /// <summary>
    /// Interval between plan collection runs.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan PlanCollectionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Startup delay before first plan collection run.
    /// Allows other services to initialize.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan PlanCollectionStartupDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Interval between analysis runs (regression/hotspot detection).
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Startup delay before first analysis run.
    /// Should be after plan collection to have data to analyze.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan AnalysisStartupDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Time of day (UTC) to rebuild baselines.
    /// Default: 2:00 AM.
    /// </summary>
    public TimeSpan BaselineRebuildTimeOfDay { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Time of day (UTC) to send daily summaries.
    /// Default: 8:00 AM.
    /// </summary>
    public TimeSpan DailySummaryTimeOfDay { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Maximum consecutive failures before backing off.
    /// Default: 5.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Initial backoff delay after a failure.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum backoff delay.
    /// Default: 10 minutes.
    /// </summary>
    public TimeSpan MaxFailureBackoff { get; set; } = TimeSpan.FromMinutes(10);
}
