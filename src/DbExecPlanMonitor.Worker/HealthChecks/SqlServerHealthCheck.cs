using DbExecPlanMonitor.Application.Orchestrators;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker.HealthChecks;

/// <summary>
/// Health check that verifies connectivity to all configured SQL Server instances.
/// Used for readiness probes in containerized environments.
/// </summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IOptionsMonitor<MonitoringInstancesOptions> _options;
    private readonly ILogger<SqlServerHealthCheck> _logger;

    public SqlServerHealthCheck(
        ISqlConnectionFactory connectionFactory,
        IOptionsMonitor<MonitoringInstancesOptions> options,
        ILogger<SqlServerHealthCheck> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var instances = _options.CurrentValue.Instances?
            .Where(i => i.Enabled)
            .ToList() ?? [];

        if (!instances.Any())
        {
            return HealthCheckResult.Degraded(
                "No SQL Server instances configured",
                data: new Dictionary<string, object>
                {
                    ["configured_instances"] = 0
                });
        }

        var results = new Dictionary<string, object>();
        var failedInstances = new List<string>();
        var successfulInstances = new List<string>();

        foreach (var instance in instances)
        {
            try
            {
                var canConnect = await _connectionFactory.TestConnectionAsync(
                    instance.Name, 
                    cancellationToken);

                if (canConnect)
                {
                    successfulInstances.Add(instance.Name);
                    results[$"instance_{instance.Name}"] = "Connected";
                }
                else
                {
                    failedInstances.Add(instance.Name);
                    results[$"instance_{instance.Name}"] = "Failed";
                }
            }
            catch (Exception ex)
            {
                failedInstances.Add(instance.Name);
                results[$"instance_{instance.Name}"] = $"Error: {ex.Message}";
                _logger.LogWarning(ex, 
                    "Health check failed for instance {Instance}", 
                    instance.Name);
            }
        }

        results["total_instances"] = instances.Count;
        results["successful"] = successfulInstances.Count;
        results["failed"] = failedInstances.Count;

        if (failedInstances.Count == 0)
        {
            return HealthCheckResult.Healthy(
                $"All {instances.Count} SQL Server instance(s) are accessible",
                results);
        }

        if (failedInstances.Count == instances.Count)
        {
            return HealthCheckResult.Unhealthy(
                $"All {instances.Count} SQL Server instance(s) are inaccessible",
                data: results);
        }

        return HealthCheckResult.Degraded(
            $"{failedInstances.Count}/{instances.Count} SQL Server instance(s) are inaccessible: " +
            string.Join(", ", failedInstances),
            data: results);
    }
}
