using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DbExecPlanMonitor.Application.Configuration;

namespace DbExecPlanMonitor.Infrastructure.Configuration;

/// <summary>
/// Extension methods for registering configuration services.
/// </summary>
public static class ConfigurationServiceExtensions
{
    /// <summary>
    /// Adds all configuration services with validation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMonitoringConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register secret resolver
        services.AddSingleton<ISecretResolver, ConfigurationSecretResolver>();

        // Register and validate MonitoringOptions
        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .ValidateWithDataAnnotations()
            .ValidateOnStart();

        // Register FeatureFlagOptions (nested under Monitoring)
        services.AddOptions<FeatureFlagOptions>()
            .Bind(configuration.GetSection(FeatureFlagOptions.SectionName))
            .ValidateWithDataAnnotations()
            .ValidateOnStart();

        // Register JobScheduleOptions
        services.AddOptions<JobScheduleOptions>()
            .Bind(configuration.GetSection(JobScheduleOptions.SectionName))
            .ValidateWithDataAnnotations()
            .ValidateOnStart();

        // Register a provider for runtime feature flag access
        services.AddSingleton<IFeatureFlagProvider, OptionsFeatureFlagProvider>();

        return services;
    }

    /// <summary>
    /// Adds instance configuration with validation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInstanceConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Instances are bound from Monitoring:Instances
        services.Configure<List<InstanceConfig>>(
            configuration.GetSection($"{MonitoringOptions.SectionName}:Instances"));

        return services;
    }
}
