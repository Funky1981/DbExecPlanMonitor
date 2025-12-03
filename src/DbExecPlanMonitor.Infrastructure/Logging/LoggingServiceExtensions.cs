using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Logging;

namespace DbExecPlanMonitor.Infrastructure.Logging;

/// <summary>
/// Extension methods for registering logging and telemetry services.
/// </summary>
public static class LoggingServiceExtensions
{
    /// <summary>
    /// Adds telemetry and auditing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTelemetryAndAuditing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register telemetry service (logs metrics as structured events)
        services.AddSingleton<ITelemetryService, LoggingTelemetryService>();

        // Register remediation audit repository
        services.AddSingleton<IRemediationAuditRepository, SqlRemediationAuditRepository>();

        return services;
    }
}
