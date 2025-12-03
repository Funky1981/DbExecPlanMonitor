using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker.Scheduling;

/// <summary>
/// Validates SchedulingOptions to ensure all values are within acceptable bounds.
/// Prevents invalid configuration from causing runtime failures.
/// </summary>
public sealed class SchedulingOptionsValidator : IValidateOptions<SchedulingOptions>
{
    /// <summary>
    /// Minimum allowed interval for any scheduled job.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum allowed interval for any scheduled job.
    /// </summary>
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum backoff duration.
    /// </summary>
    private static readonly TimeSpan MinimumBackoff = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum backoff duration.
    /// </summary>
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, SchedulingOptions options)
    {
        var failures = new List<string>();

        // Validate collection interval
        if (options.CollectionEnabled)
        {
            if (options.CollectionInterval <= TimeSpan.Zero)
            {
                failures.Add($"{nameof(options.CollectionInterval)} must be positive. Got: {options.CollectionInterval}");
            }
            else if (options.CollectionInterval < MinimumInterval)
            {
                failures.Add($"{nameof(options.CollectionInterval)} must be at least {MinimumInterval}. Got: {options.CollectionInterval}");
            }
            else if (options.CollectionInterval > MaximumInterval)
            {
                failures.Add($"{nameof(options.CollectionInterval)} must not exceed {MaximumInterval}. Got: {options.CollectionInterval}");
            }

            if (options.CollectionStartupDelay < TimeSpan.Zero)
            {
                failures.Add($"{nameof(options.CollectionStartupDelay)} cannot be negative. Got: {options.CollectionStartupDelay}");
            }
        }

        // Validate analysis interval
        if (options.AnalysisEnabled)
        {
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

            if (options.AnalysisStartupDelay < TimeSpan.Zero)
            {
                failures.Add($"{nameof(options.AnalysisStartupDelay)} cannot be negative. Got: {options.AnalysisStartupDelay}");
            }
        }

        // Validate daily task times (must be valid time of day: 0-24 hours)
        if (options.BaselineRebuildEnabled)
        {
            if (options.BaselineRebuildTimeOfDay < TimeSpan.Zero || options.BaselineRebuildTimeOfDay >= TimeSpan.FromHours(24))
            {
                failures.Add($"{nameof(options.BaselineRebuildTimeOfDay)} must be between 00:00 and 23:59:59. Got: {options.BaselineRebuildTimeOfDay}");
            }
        }

        if (options.DailySummaryEnabled)
        {
            if (options.DailySummaryTimeOfDay < TimeSpan.Zero || options.DailySummaryTimeOfDay >= TimeSpan.FromHours(24))
            {
                failures.Add($"{nameof(options.DailySummaryTimeOfDay)} must be between 00:00 and 23:59:59. Got: {options.DailySummaryTimeOfDay}");
            }
        }

        // Validate MaxConsecutiveFailures
        if (options.MaxConsecutiveFailures <= 0)
        {
            failures.Add($"{nameof(options.MaxConsecutiveFailures)} must be a positive integer. Got: {options.MaxConsecutiveFailures}");
        }
        else if (options.MaxConsecutiveFailures > 100)
        {
            failures.Add($"{nameof(options.MaxConsecutiveFailures)} should not exceed 100. Got: {options.MaxConsecutiveFailures}");
        }

        // Validate failure backoff
        if (options.FailureBackoff <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.FailureBackoff)} must be positive. Got: {options.FailureBackoff}");
        }
        else if (options.FailureBackoff < MinimumBackoff)
        {
            failures.Add($"{nameof(options.FailureBackoff)} must be at least {MinimumBackoff}. Got: {options.FailureBackoff}");
        }
        else if (options.FailureBackoff > MaximumBackoff)
        {
            failures.Add($"{nameof(options.FailureBackoff)} must not exceed {MaximumBackoff}. Got: {options.FailureBackoff}");
        }

        // Validate max failure backoff
        if (options.MaxFailureBackoff <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.MaxFailureBackoff)} must be positive. Got: {options.MaxFailureBackoff}");
        }
        else if (options.MaxFailureBackoff < options.FailureBackoff)
        {
            failures.Add($"{nameof(options.MaxFailureBackoff)} ({options.MaxFailureBackoff}) must be >= {nameof(options.FailureBackoff)} ({options.FailureBackoff})");
        }
        else if (options.MaxFailureBackoff > MaximumBackoff)
        {
            failures.Add($"{nameof(options.MaxFailureBackoff)} must not exceed {MaximumBackoff}. Got: {options.MaxFailureBackoff}");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
