using DbExecPlanMonitor.Domain.Services;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Validates AnalysisOptions to ensure all values are within acceptable bounds.
/// Prevents invalid configuration from causing runtime failures or misleading analysis.
/// </summary>
public sealed class AnalysisOptionsValidator : IValidateOptions<AnalysisOptions>
{
    /// <summary>
    /// Minimum allowed window for recent metrics.
    /// </summary>
    private static readonly TimeSpan MinimumWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum allowed window.
    /// </summary>
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Minimum analysis interval.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum analysis interval.
    /// </summary>
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(24);

    public ValidateOptionsResult Validate(string? name, AnalysisOptions options)
    {
        var failures = new List<string>();

        // Validate RecentWindow
        if (options.RecentWindow <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.RecentWindow)} must be positive. Got: {options.RecentWindow}");
        }
        else if (options.RecentWindow < MinimumWindow)
        {
            failures.Add($"{nameof(options.RecentWindow)} must be at least {MinimumWindow}. Got: {options.RecentWindow}");
        }
        else if (options.RecentWindow > MaximumWindow)
        {
            failures.Add($"{nameof(options.RecentWindow)} should not exceed {MaximumWindow}. Got: {options.RecentWindow}");
        }

        // Validate HotspotWindow
        if (options.HotspotWindow <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.HotspotWindow)} must be positive. Got: {options.HotspotWindow}");
        }
        else if (options.HotspotWindow < MinimumWindow)
        {
            failures.Add($"{nameof(options.HotspotWindow)} must be at least {MinimumWindow}. Got: {options.HotspotWindow}");
        }
        else if (options.HotspotWindow > MaximumWindow)
        {
            failures.Add($"{nameof(options.HotspotWindow)} should not exceed {MaximumWindow}. Got: {options.HotspotWindow}");
        }

        // Validate AnalysisInterval
        if (options.AnalysisInterval <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.AnalysisInterval)} must be positive. Got: {options.AnalysisInterval}");
        }
        else if (options.AnalysisInterval < MinimumInterval)
        {
            failures.Add($"{nameof(options.AnalysisInterval)} must be at least {MinimumInterval}. Got: {options.AnalysisInterval}");
        }
        else if (options.AnalysisInterval > MaximumInterval)
        {
            failures.Add($"{nameof(options.AnalysisInterval)} must not exceed {MaximumInterval}. Got: {options.AnalysisInterval}");
        }

        // Validate AutoResolutionCheckInterval
        if (options.AutoResolutionCheckInterval <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.AutoResolutionCheckInterval)} must be positive. Got: {options.AutoResolutionCheckInterval}");
        }
        else if (options.AutoResolutionCheckInterval < MinimumInterval)
        {
            failures.Add($"{nameof(options.AutoResolutionCheckInterval)} must be at least {MinimumInterval}. Got: {options.AutoResolutionCheckInterval}");
        }

        // Validate BaselineLookbackDays
        if (options.BaselineLookbackDays <= 0)
        {
            failures.Add($"{nameof(options.BaselineLookbackDays)} must be positive. Got: {options.BaselineLookbackDays}");
        }
        else if (options.BaselineLookbackDays > 365)
        {
            failures.Add($"{nameof(options.BaselineLookbackDays)} should not exceed 365. Got: {options.BaselineLookbackDays}");
        }

        // Validate BaselineMaxAge
        if (options.BaselineMaxAge <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.BaselineMaxAge)} must be positive. Got: {options.BaselineMaxAge}");
        }
        else if (options.BaselineMaxAge < TimeSpan.FromHours(1))
        {
            failures.Add($"{nameof(options.BaselineMaxAge)} must be at least 1 hour. Got: {options.BaselineMaxAge}");
        }

        // Validate MinimumBaselineSamples
        if (options.MinimumBaselineSamples <= 0)
        {
            failures.Add($"{nameof(options.MinimumBaselineSamples)} must be positive. Got: {options.MinimumBaselineSamples}");
        }
        else if (options.MinimumBaselineSamples < 3)
        {
            failures.Add($"{nameof(options.MinimumBaselineSamples)} should be at least 3 for statistical significance. Got: {options.MinimumBaselineSamples}");
        }

        // Validate RegressionRules
        ValidateRegressionRules(options.RegressionRules, failures);

        // Validate HotspotRules
        ValidateHotspotRules(options.HotspotRules, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateRegressionRules(RegressionDetectionRules rules, List<string> failures)
    {
        if (rules.DurationIncreaseThresholdPercent <= 0)
        {
            failures.Add($"RegressionRules.{nameof(rules.DurationIncreaseThresholdPercent)} must be positive. Got: {rules.DurationIncreaseThresholdPercent}");
        }

        if (rules.CpuIncreaseThresholdPercent <= 0)
        {
            failures.Add($"RegressionRules.{nameof(rules.CpuIncreaseThresholdPercent)} must be positive. Got: {rules.CpuIncreaseThresholdPercent}");
        }

        if (rules.LogicalReadsIncreaseThresholdPercent <= 0)
        {
            failures.Add($"RegressionRules.{nameof(rules.LogicalReadsIncreaseThresholdPercent)} must be positive. Got: {rules.LogicalReadsIncreaseThresholdPercent}");
        }

        if (rules.MinimumExecutions <= 0)
        {
            failures.Add($"RegressionRules.{nameof(rules.MinimumExecutions)} must be positive. Got: {rules.MinimumExecutions}");
        }

        if (rules.MinimumBaselineSamples <= 0)
        {
            failures.Add($"RegressionRules.{nameof(rules.MinimumBaselineSamples)} must be positive. Got: {rules.MinimumBaselineSamples}");
        }
    }

    private static void ValidateHotspotRules(HotspotDetectionRules rules, List<string> failures)
    {
        if (rules.TopN <= 0)
        {
            failures.Add($"HotspotRules.{nameof(rules.TopN)} must be positive. Got: {rules.TopN}");
        }
        else if (rules.TopN > 100)
        {
            failures.Add($"HotspotRules.{nameof(rules.TopN)} should not exceed 100. Got: {rules.TopN}");
        }

        if (rules.MinExecutionCount < 0)
        {
            failures.Add($"HotspotRules.{nameof(rules.MinExecutionCount)} cannot be negative. Got: {rules.MinExecutionCount}");
        }

        if (rules.MinTotalCpuMs < 0)
        {
            failures.Add($"HotspotRules.{nameof(rules.MinTotalCpuMs)} cannot be negative. Got: {rules.MinTotalCpuMs}");
        }

        if (rules.MinAvgDurationMs < 0)
        {
            failures.Add($"HotspotRules.{nameof(rules.MinAvgDurationMs)} cannot be negative. Got: {rules.MinAvgDurationMs}");
        }

        if (rules.MinTotalDurationMs < 0)
        {
            failures.Add($"HotspotRules.{nameof(rules.MinTotalDurationMs)} cannot be negative. Got: {rules.MinTotalDurationMs}");
        }
    }
}
