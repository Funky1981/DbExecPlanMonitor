using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Extension methods for options validation.
/// </summary>
public static class OptionsBuilderValidationExtensions
{
    /// <summary>
    /// Adds DataAnnotations validation to options.
    /// </summary>
    public static OptionsBuilder<TOptions> ValidateWithDataAnnotations<TOptions>(
        this OptionsBuilder<TOptions> optionsBuilder)
        where TOptions : class
    {
        optionsBuilder.Services.Add(
            ServiceDescriptor.Singleton<IValidateOptions<TOptions>>(
                new DataAnnotationsValidateOptions<TOptions> { Name = optionsBuilder.Name }));

        return optionsBuilder;
    }
}

/// <summary>
/// Provides access to feature flags at runtime.
/// </summary>
public interface IFeatureFlagProvider
{
    /// <summary>
    /// Gets the current feature flag settings.
    /// </summary>
    FeatureFlagOptions Flags { get; }

    /// <summary>
    /// Checks if a specific feature is enabled.
    /// </summary>
    bool IsEnabled(string featureName);

    /// <summary>
    /// Checks if remediation is allowed for an instance.
    /// </summary>
    bool IsRemediationAllowed(InstanceConfig instance);
}

/// <summary>
/// Implementation using IOptionsMonitor for live updates.
/// </summary>
public sealed class OptionsFeatureFlagProvider : IFeatureFlagProvider
{
    private readonly IOptionsMonitor<FeatureFlagOptions> _options;

    /// <summary>
    /// Initializes a new instance of the OptionsFeatureFlagProvider.
    /// </summary>
    public OptionsFeatureFlagProvider(IOptionsMonitor<FeatureFlagOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public FeatureFlagOptions Flags => _options.CurrentValue;

    /// <inheritdoc />
    public bool IsEnabled(string featureName)
    {
        var flags = Flags;

        return featureName.ToLowerInvariant() switch
        {
            "plancollection" => flags.EnablePlanCollection,
            "analysis" => flags.EnableAnalysis,
            "baselinerebuild" => flags.EnableBaselineRebuild,
            "dailysummary" => flags.EnableDailySummary,
            "alerting" => flags.EnableAlerting,
            "remediation" => flags.EnableRemediation,
            "healthchecks" => flags.EnableHealthChecks,
            "querystore" => flags.PreferQueryStore,
            _ => false
        };
    }

    /// <inheritdoc />
    public bool IsRemediationAllowed(InstanceConfig instance)
    {
        var flags = Flags;

        // Remediation must be globally enabled
        if (!flags.EnableRemediation)
        {
            return false;
        }

        // If dry-run, allow (nothing will actually execute)
        if (flags.RemediationDryRun)
        {
            return true;
        }

        // Check production safety
        if (instance.IsProduction && !flags.AllowProductionRemediation)
        {
            return false;
        }

        return true;
    }
}
