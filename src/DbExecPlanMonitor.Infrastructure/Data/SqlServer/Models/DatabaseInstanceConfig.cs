namespace DbExecPlanMonitor.Infrastructure.Data.SqlServer.Models;

/// <summary>
/// Configuration for a database instance connection.
/// Used to read from appsettings.json and construct connection strings.
/// </summary>
/// <remarks>
/// This class is part of the Options pattern.
/// It maps to "DatabaseInstances" section in configuration.
/// 
/// Example appsettings.json:
/// {
///   "DatabaseInstances": [
///     {
///       "Name": "Production-SQL01",
///       "ServerName": "sql01.example.com",
///       "Port": 1433,
///       "UseIntegratedSecurity": true,
///       "DatabaseNames": ["AppDb", "ReportDb"],
///       "SamplingIntervalSeconds": 60,
///       "IsEnabled": true
///     }
///   ]
/// }
/// </remarks>
public class DatabaseInstanceConfig
{
    /// <summary>
    /// Friendly name for the instance (for logging and display).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SQL Server hostname or IP address.
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// SQL Server port (default 1433).
    /// </summary>
    public int Port { get; set; } = 1433;

    /// <summary>
    /// Instance name if using named instances (e.g., "SQLEXPRESS").
    /// Leave empty for default instance.
    /// </summary>
    public string? InstanceName { get; set; }

    /// <summary>
    /// Use Windows Authentication (Integrated Security).
    /// </summary>
    public bool UseIntegratedSecurity { get; set; } = true;

    /// <summary>
    /// SQL Server username (if not using Integrated Security).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SQL Server password (if not using Integrated Security).
    /// Consider using secrets management instead of storing in config.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Application name to identify connections in sys.dm_exec_sessions.
    /// </summary>
    public string ApplicationName { get; set; } = "DbExecPlanMonitor";

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Command timeout in seconds for queries.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// List of database names to monitor on this instance.
    /// </summary>
    public List<string> DatabaseNames { get; set; } = new();

    /// <summary>
    /// How often to sample metrics (seconds).
    /// </summary>
    public int SamplingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Whether monitoring is enabled for this instance.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether to use Query Store if available.
    /// Falls back to DMVs if disabled or unavailable.
    /// </summary>
    public bool PreferQueryStore { get; set; } = true;

    /// <summary>
    /// Number of top queries to collect per sampling interval.
    /// </summary>
    public int TopQueriesCount { get; set; } = 50;

    /// <summary>
    /// Use encrypted connection.
    /// </summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>
    /// Trust server certificate (for self-signed certs in dev).
    /// </summary>
    public bool TrustServerCertificate { get; set; } = false;

    /// <summary>
    /// Tags for categorization (e.g., "production", "critical").
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Builds a connection string from this configuration.
    /// </summary>
    public string BuildConnectionString(string? databaseName = null)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = BuildDataSource(),
            IntegratedSecurity = UseIntegratedSecurity,
            ApplicationName = ApplicationName,
            ConnectTimeout = ConnectionTimeoutSeconds,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate
        };

        if (!UseIntegratedSecurity)
        {
            builder.UserID = Username;
            builder.Password = Password;
        }

        if (!string.IsNullOrEmpty(databaseName))
        {
            builder.InitialCatalog = databaseName;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Builds the Data Source value (server,port or server\instance).
    /// </summary>
    private string BuildDataSource()
    {
        if (!string.IsNullOrEmpty(InstanceName))
        {
            return $"{ServerName}\\{InstanceName}";
        }

        if (Port != 1433)
        {
            return $"{ServerName},{Port}";
        }

        return ServerName;
    }

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name is required");

        if (string.IsNullOrWhiteSpace(ServerName))
            errors.Add("ServerName is required");

        if (!UseIntegratedSecurity && string.IsNullOrWhiteSpace(Username))
            errors.Add("Username is required when not using Integrated Security");

        if (SamplingIntervalSeconds < 10)
            errors.Add("SamplingIntervalSeconds must be at least 10");

        if (TopQueriesCount < 1 || TopQueriesCount > 500)
            errors.Add("TopQueriesCount must be between 1 and 500");

        if (Port < 1 || Port > 65535)
            errors.Add("Port must be between 1 and 65535");

        return errors;
    }
}

/// <summary>
/// Root configuration section for all database instances.
/// </summary>
public class MonitoringConfiguration
{
    public const string SectionName = "Monitoring";

    /// <summary>
    /// List of database instances to monitor.
    /// </summary>
    public List<DatabaseInstanceConfig> DatabaseInstances { get; set; } = new();

    /// <summary>
    /// Global sampling interval override (seconds).
    /// Instance-level settings take precedence if specified.
    /// </summary>
    public int DefaultSamplingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// How many days of historical data to retain in our own storage.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Enable debug logging for SQL queries.
    /// </summary>
    public bool LogSqlQueries { get; set; } = false;

    /// <summary>
    /// Maximum concurrent connections across all instances.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 10;
}
