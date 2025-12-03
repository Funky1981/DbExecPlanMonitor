using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Application.Orchestrators;

/// <summary>
/// Validates PlanCollectionOptions to ensure all values are within acceptable bounds.
/// Prevents invalid configuration from causing runtime failures or misleading behavior.
/// </summary>
public sealed class PlanCollectionOptionsValidator : IValidateOptions<PlanCollectionOptions>
{
    /// <summary>
    /// Minimum allowed collection interval.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum allowed collection interval.
    /// </summary>
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum lookback window.
    /// </summary>
    private static readonly TimeSpan MinimumLookback = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum lookback window.
    /// </summary>
    private static readonly TimeSpan MaximumLookback = TimeSpan.FromDays(7);

    public ValidateOptionsResult Validate(string? name, PlanCollectionOptions options)
    {
        var failures = new List<string>();

        // Validate CollectionInterval
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

        // Validate TopNQueries
        if (options.TopNQueries <= 0)
        {
            failures.Add($"{nameof(options.TopNQueries)} must be positive. Got: {options.TopNQueries}");
        }
        else if (options.TopNQueries > 1000)
        {
            failures.Add($"{nameof(options.TopNQueries)} should not exceed 1000 to avoid performance issues. Got: {options.TopNQueries}");
        }

        // Validate LookbackWindow
        if (options.LookbackWindow <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.LookbackWindow)} must be positive. Got: {options.LookbackWindow}");
        }
        else if (options.LookbackWindow < MinimumLookback)
        {
            failures.Add($"{nameof(options.LookbackWindow)} must be at least {MinimumLookback}. Got: {options.LookbackWindow}");
        }
        else if (options.LookbackWindow > MaximumLookback)
        {
            failures.Add($"{nameof(options.LookbackWindow)} should not exceed {MaximumLookback}. Got: {options.LookbackWindow}");
        }

        // Validate MinimumExecutionCount
        if (options.MinimumExecutionCount < 0)
        {
            failures.Add($"{nameof(options.MinimumExecutionCount)} cannot be negative. Got: {options.MinimumExecutionCount}");
        }

        // Validate MinimumElapsedTimeMs
        if (options.MinimumElapsedTimeMs < 0)
        {
            failures.Add($"{nameof(options.MinimumElapsedTimeMs)} cannot be negative. Got: {options.MinimumElapsedTimeMs}");
        }

        // Validate CollectionTimeout
        if (options.CollectionTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.CollectionTimeout)} must be positive. Got: {options.CollectionTimeout}");
        }
        else if (options.CollectionTimeout < TimeSpan.FromSeconds(10))
        {
            failures.Add($"{nameof(options.CollectionTimeout)} must be at least 10 seconds. Got: {options.CollectionTimeout}");
        }
        else if (options.CollectionTimeout > TimeSpan.FromMinutes(30))
        {
            failures.Add($"{nameof(options.CollectionTimeout)} should not exceed 30 minutes. Got: {options.CollectionTimeout}");
        }

        // Validate parallelism settings
        if (options.MaxInstanceParallelism <= 0)
        {
            failures.Add($"{nameof(options.MaxInstanceParallelism)} must be positive. Got: {options.MaxInstanceParallelism}");
        }
        else if (options.MaxInstanceParallelism > 16)
        {
            failures.Add($"{nameof(options.MaxInstanceParallelism)} should not exceed 16 to avoid overwhelming the network. Got: {options.MaxInstanceParallelism}");
        }

        if (options.MaxDatabaseParallelism <= 0)
        {
            failures.Add($"{nameof(options.MaxDatabaseParallelism)} must be positive. Got: {options.MaxDatabaseParallelism}");
        }
        else if (options.MaxDatabaseParallelism > 8)
        {
            failures.Add($"{nameof(options.MaxDatabaseParallelism)} should not exceed 8 to avoid overwhelming a single instance. Got: {options.MaxDatabaseParallelism}");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
