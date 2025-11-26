using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;
using DbExecPlanMonitor.Infrastructure.Persistence;

namespace DbExecPlanMonitor.Infrastructure;

/// <summary>
/// Dependency injection extensions for Infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server monitoring infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<MonitoringConfiguration>(
            configuration.GetSection(MonitoringConfiguration.SectionName));

        // Register connection factory
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register providers (read from SQL Server DMVs/Query Store)
        services.AddSingleton<IPlanStatisticsProvider, DmvPlanStatisticsProvider>();
        services.AddSingleton<IPlanDetailsProvider, DmvPlanDetailsProvider>();

        return services;
    }

    /// <summary>
    /// Adds monitoring storage (persistence) services for storing our own data.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMonitoringStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind storage configuration
        services.Configure<MonitoringStorageOptions>(
            configuration.GetSection(MonitoringStorageOptions.SectionName));

        // Register repositories (write our own data)
        services.AddSingleton<IQueryFingerprintRepository, SqlQueryFingerprintRepository>();
        services.AddSingleton<IPlanMetricsRepository, SqlPlanMetricsRepository>();
        services.AddSingleton<IBaselineRepository, SqlBaselineRepository>();
        services.AddSingleton<IRegressionEventRepository, SqlRegressionEventRepository>();

        return services;
    }

    /// <summary>
    /// Validates the monitoring configuration at startup.
    /// </summary>
    public static IServiceCollection AddMonitoringValidation(
        this IServiceCollection services)
    {
        services.AddHostedService<MonitoringConfigurationValidator>();
        return services;
    }
}

/// <summary>
/// Validates configuration on startup.
/// </summary>
internal class MonitoringConfigurationValidator : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MonitoringConfigurationValidator> _logger;

    public MonitoringConfigurationValidator(
        IServiceProvider serviceProvider,
        ILogger<MonitoringConfigurationValidator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Validate configuration
        var config = _serviceProvider.GetRequiredService<IOptions<MonitoringConfiguration>>().Value;

        var allErrors = new List<string>();

        foreach (var instance in config.DatabaseInstances)
        {
            var errors = instance.Validate();
            if (errors.Count > 0)
            {
                allErrors.AddRange(errors.Select(e => $"[{instance.Name}] {e}"));
            }
        }

        if (allErrors.Count > 0)
        {
            _logger.LogError(
                "Monitoring configuration validation failed:\n{Errors}",
                string.Join("\n", allErrors));
        }
        else
        {
            _logger.LogInformation(
                "Monitoring configuration validated. {Count} instances configured.",
                config.DatabaseInstances.Count);
        }

        // Test connections if configured instances exist
        var connectionFactory = _serviceProvider.GetRequiredService<ISqlConnectionFactory>();
        var enabledInstances = connectionFactory.GetEnabledInstanceNames();

        foreach (var instanceName in enabledInstances)
        {
            var success = await connectionFactory.TestConnectionAsync(instanceName, stoppingToken);
            if (success)
            {
                _logger.LogInformation("Connection test successful for {Instance}", instanceName);
            }
            else
            {
                _logger.LogWarning("Connection test failed for {Instance}", instanceName);
            }
        }

        // This service only runs once at startup
        await Task.CompletedTask;
    }
}
