using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;

namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// Factory for creating SQL Server connections with proper configuration.
/// Implements connection management with logging and validation.
/// </summary>
public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly ILogger<SqlConnectionFactory> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly Dictionary<string, DatabaseInstanceConfig> _instanceConfigs;

    public SqlConnectionFactory(
        IOptions<MonitoringConfiguration> options,
        ILogger<SqlConnectionFactory> logger)
    {
        _config = options.Value;
        _logger = logger;

        // Index instance configs by name for quick lookup
        _instanceConfigs = _config.DatabaseInstances
            .Where(i => i.IsEnabled)
            .ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates an open connection to the specified database instance.
    /// </summary>
    public async Task<SqlConnection> CreateConnectionAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        if (!_instanceConfigs.TryGetValue(instanceName, out var config))
        {
            throw new ArgumentException(
                $"Database instance '{instanceName}' not found in configuration or is disabled.",
                nameof(instanceName));
        }

        return await CreateConnectionFromConfigAsync(config, null, cancellationToken);
    }

    /// <summary>
    /// Creates an open connection to a specific database on an instance.
    /// </summary>
    public async Task<SqlConnection> CreateConnectionForDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        if (!_instanceConfigs.TryGetValue(instanceName, out var config))
        {
            throw new ArgumentException(
                $"Database instance '{instanceName}' not found in configuration or is disabled.",
                nameof(instanceName));
        }

        // Validate that this database is in the monitored list
        if (!config.DatabaseNames.Contains(databaseName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Database {DatabaseName} is not in the configured list for instance {InstanceName}. " +
                "Proceeding anyway, but this may indicate a configuration issue.",
                databaseName, instanceName);
        }

        return await CreateConnectionFromConfigAsync(config, databaseName, cancellationToken);
    }

    /// <summary>
    /// Tests connectivity to a database instance.
    /// Returns true if connection succeeds, false otherwise.
    /// </summary>
    public async Task<bool> TestConnectionAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await CreateConnectionAsync(instanceName, cancellationToken);

            // Execute a simple query to verify the connection works
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 10;

            await command.ExecuteScalarAsync(cancellationToken);

            _logger.LogDebug(
                "Connection test successful for instance {InstanceName}",
                instanceName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Connection test failed for instance {InstanceName}",
                instanceName);

            return false;
        }
    }

    /// <summary>
    /// Gets all enabled instance names.
    /// </summary>
    public IReadOnlyList<string> GetEnabledInstanceNames()
    {
        return _instanceConfigs.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets configuration for an instance.
    /// </summary>
    public DatabaseInstanceConfig? GetInstanceConfig(string instanceName)
    {
        return _instanceConfigs.TryGetValue(instanceName, out var config) ? config : null;
    }

    /// <summary>
    /// Creates and opens a connection from the given configuration.
    /// </summary>
    private async Task<SqlConnection> CreateConnectionFromConfigAsync(
        DatabaseInstanceConfig config,
        string? databaseName,
        CancellationToken cancellationToken)
    {
        var connectionString = config.BuildConnectionString(databaseName);

        // Log without exposing credentials
        if (_config.LogSqlQueries)
        {
            var safeConnectionInfo = $"Server={config.ServerName}, Database={databaseName ?? "default"}";
            _logger.LogDebug("Creating connection: {ConnectionInfo}", safeConnectionInfo);
        }

        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            _logger.LogDebug(
                "Opened connection to {InstanceName}/{Database} (ServerVersion: {Version})",
                config.Name,
                databaseName ?? "default",
                connection.ServerVersion);

            return connection;
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect to {InstanceName}/{Database}. SqlError: {ErrorNumber}, State: {State}",
                config.Name,
                databaseName ?? "default",
                ex.Number,
                ex.State);

            await connection.DisposeAsync();
            throw;
        }
        catch (Exception)
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
