using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DbExecPlanMonitor.Domain.Services;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Configuration options for the analysis engine.
/// </summary>
public sealed class AnalysisOptions : IValidatableObject
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Analysis";

    /// <summary>
    /// Time window for recent metrics (regression detection).
    /// Default: 15 minutes.
    /// </summary>
    public TimeSpan RecentWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Time window for hotspot detection.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan HotspotWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often to run analysis.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to check for auto-resolutions.
    /// Default: 15 minutes.
    /// </summary>
    public TimeSpan AutoResolutionCheckInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Number of days to use for baseline computation.
    /// Default: 7 days.
    /// </summary>
    public int BaselineLookbackDays { get; set; } = 7;

    /// <summary>
    /// Maximum age of a baseline before refresh is needed.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan BaselineMaxAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum samples required for a reliable baseline.
    /// Default: 10.
    /// </summary>
    public int MinimumBaselineSamples { get; set; } = 10;

    /// <summary>
    /// Regression detection rules.
    /// </summary>
    public RegressionDetectionRules RegressionRules { get; set; } = new();

    /// <summary>
    /// Hotspot detection rules.
    /// </summary>
    public HotspotDetectionRules HotspotRules { get; set; } = new();

    /// <summary>
    /// Whether to automatically resolve regressions when performance returns to normal.
    /// Default: true.
    /// </summary>
    public bool EnableAutoResolution { get; set; } = true;

    /// <summary>
    /// Whether to continue analyzing other databases when one fails.
    /// Default: true.
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RecentWindow <= TimeSpan.Zero)
            yield return new ValidationResult("RecentWindow must be greater than zero.", [nameof(RecentWindow)]);

        if (HotspotWindow <= TimeSpan.Zero)
            yield return new ValidationResult("HotspotWindow must be greater than zero.", [nameof(HotspotWindow)]);

        if (AnalysisInterval <= TimeSpan.Zero)
            yield return new ValidationResult("AnalysisInterval must be greater than zero.", [nameof(AnalysisInterval)]);

        if (AutoResolutionCheckInterval <= TimeSpan.Zero)
            yield return new ValidationResult("AutoResolutionCheckInterval must be greater than zero.", [nameof(AutoResolutionCheckInterval)]);

        if (BaselineLookbackDays <= 0)
            yield return new ValidationResult("BaselineLookbackDays must be greater than zero.", [nameof(BaselineLookbackDays)]);

        if (BaselineMaxAge <= TimeSpan.Zero)
            yield return new ValidationResult("BaselineMaxAge must be greater than zero.", [nameof(BaselineMaxAge)]);

        if (MinimumBaselineSamples <= 0)
            yield return new ValidationResult("MinimumBaselineSamples must be greater than zero.", [nameof(MinimumBaselineSamples)]);
    }
}
