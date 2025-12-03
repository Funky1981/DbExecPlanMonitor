using DbExecPlanMonitor.Application.Configuration;
using DbExecPlanMonitor.Application.Services;
using DbExecPlanMonitor.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DbExecPlanMonitor.Infrastructure.Configuration;

/// <summary>
/// Extension methods for registering security services.
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers security options and the remediation guard service.
    /// </summary>
    public static IServiceCollection AddSecurityServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register SecurityOptions with validation
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register the remediation guard
        services.AddSingleton<IRemediationGuard, RemediationGuardService>();

        // Register TimeProvider if not already registered
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
