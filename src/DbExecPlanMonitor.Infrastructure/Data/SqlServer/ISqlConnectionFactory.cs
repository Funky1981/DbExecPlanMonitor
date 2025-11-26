using Microsoft.Data.SqlClient;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;

namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer;

/// <summary>
/// Creates SQL connections for monitoring operations.
/// </summary>
/// <remarks>
/// All database access goes through this factory to ensure:
/// - Consistent connection strings from configuration
/// - Proper timeout configuration
/// - Connection pooling (ADO.NET default)
/// - Centralized connection management
/// 
/// Connections are created using named instances from configuration,
/// allowing the same codebase to work across different environments.
/// </remarks>
public interface ISqlConnectionFactory
{
    /// <summary>
    /// Creates an open connection to the specified database instance.
    /// Connects to the default database (usually master).
    /// </summary>
    /// <param name="instanceName">The configured instance name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open SQL connection.</returns>
    Task<SqlConnection> CreateConnectionAsync(
        string instanceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an open connection to a specific database on an instance.
    /// </summary>
    /// <param name="instanceName">The configured instance name.</param>
    /// <param name="databaseName">The database to connect to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open SQL connection.</returns>
    Task<SqlConnection> CreateConnectionForDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to a database instance.
    /// </summary>
    /// <param name="instanceName">The configured instance name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection succeeds, false otherwise.</returns>
    Task<bool> TestConnectionAsync(
        string instanceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all enabled instance names from configuration.
    /// </summary>
    /// <returns>List of instance names that are enabled.</returns>
    IReadOnlyList<string> GetEnabledInstanceNames();

    /// <summary>
    /// Gets configuration for a specific instance.
    /// </summary>
    /// <param name="instanceName">The instance name.</param>
    /// <returns>The configuration, or null if not found.</returns>
    DatabaseInstanceConfig? GetInstanceConfig(string instanceName);
}
