using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Orchestrators;
using DbExecPlanMonitor.Application.Services;
using DbExecPlanMonitor.Domain.Services;
using DbExecPlanMonitor.Infrastructure.Data;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;
using DbExecPlanMonitor.Infrastructure.Messaging;
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
        services.AddSingleton<ICumulativeMetricsSnapshotRepository, SqlCumulativeMetricsSnapshotRepository>();

        return services;
    }

    /// <summary>
    /// Adds plan collection and orchestration services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlanCollection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind collection configuration
        services.Configure<PlanCollectionOptions>(
            configuration.GetSection(PlanCollectionOptions.SectionName));
        services.Configure<MonitoringInstancesOptions>(
            configuration.GetSection(MonitoringInstancesOptions.SectionName));

        // Register services
        services.AddSingleton<IQueryFingerprintService, QueryFingerprintService>();
        services.AddSingleton<IPlanCollectionOrchestrator, PlanCollectionOrchestrator>();

        return services;
    }

    /// <summary>
    /// Adds analysis services (regression detection, hotspots, baselines).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAnalysis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind analysis configuration
        services.Configure<AnalysisOptions>(
            configuration.GetSection(AnalysisOptions.SectionName));

        // Register domain services
        services.AddSingleton<IRegressionDetector, RegressionDetector>();
        services.AddSingleton<IHotspotDetector, HotspotDetector>();

        // Register application services
        services.AddSingleton<IBaselineService, BaselineService>();
        services.AddSingleton<IAnalysisOrchestrator, AnalysisOrchestrator>();

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

    /// <summary>
    /// Adds alerting services (channels and orchestrator).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAlerting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind alerting configuration
        services.Configure<AlertingOptions>(
            configuration.GetSection(AlertingOptions.SectionName));
        services.Configure<EmailChannelOptions>(
            configuration.GetSection(EmailChannelOptions.SectionName));
        services.Configure<TeamsChannelOptions>(
            configuration.GetSection(TeamsChannelOptions.SectionName));
        services.Configure<SlackChannelOptions>(
            configuration.GetSection(SlackChannelOptions.SectionName));

        // Register alert channels (all channels are registered, but each checks IsEnabled)
        services.AddSingleton<IAlertChannel, LogOnlyAlertChannel>();
        services.AddSingleton<IAlertChannel, EmailAlertChannel>();
        services.AddSingleton<IAlertChannel, TeamsAlertChannel>();
        services.AddSingleton<IAlertChannel, SlackAlertChannel>();

        // Register orchestrator
        services.AddSingleton<IAlertOrchestrator, AlertOrchestrator>();

        return services;
    }

    /// <summary>
    /// Adds remediation services (advisor and executor).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRemediation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind remediation configuration
        services.Configure<RemediationOptions>(
            configuration.GetSection(RemediationOptions.SectionName));
        services.Configure<RemediationExecutorOptions>(
            configuration.GetSection(RemediationExecutorOptions.SectionName));

        // Register domain service
        services.AddSingleton<IRemediationAdvisor, RemediationAdvisor>();

        // Register infrastructure executor
        services.AddSingleton<IRemediationExecutor, SqlRemediationExecutor>();

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
