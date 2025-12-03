using DbExecPlanMonitor.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Worker.HealthChecks;

/// <summary>
/// Health check that verifies connectivity to the monitoring storage database.
/// This database stores collected metrics, baselines, and regression events.
/// </summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<MonitoringStorageOptions> _options;
    private readonly ILogger<StorageHealthCheck> _logger;

    public StorageHealthCheck(
        IOptionsMonitor<MonitoringStorageOptions> options,
        ILogger<StorageHealthCheck> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;

        if (string.IsNullOrEmpty(options.ConnectionString))
        {
            return HealthCheckResult.Unhealthy(
                "Storage connection string is not configured");
        }

        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Verify we can query the database
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 10;
            await command.ExecuteScalarAsync(cancellationToken);

            // Check for required tables
            command.CommandText = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME IN ('QueryFingerprints', 'PlanMetrics', 'Baselines', 'RegressionEvents')";
            
            var tableCount = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);

            var data = new Dictionary<string, object>
            {
                ["database"] = connection.Database,
                ["server"] = connection.DataSource,
                ["required_tables_found"] = tableCount
            };

            if (tableCount < 4)
            {
                return HealthCheckResult.Degraded(
                    $"Storage database accessible but only {tableCount}/4 required tables found. " +
                    "Database schema may need initialization.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "Storage database is accessible and schema is valid",
                data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Storage health check failed");
            
            return HealthCheckResult.Unhealthy(
                $"Storage database is not accessible: {ex.Message}",
                ex);
        }
    }
}
