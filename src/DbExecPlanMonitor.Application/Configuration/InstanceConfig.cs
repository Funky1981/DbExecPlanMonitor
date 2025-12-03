using System.ComponentModel.DataAnnotations;

namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Configuration for a database instance to monitor.
/// </summary>
/// <remarks>
/// Represents a SQL Server instance with its connection details
/// and monitoring-specific settings. Connection strings should
/// NOT be stored here directly - use secrets management.
/// 
/// Connection string format:
/// - Use ConnectionStringName to reference a named connection string
/// - Or use environment variables: ConnectionStrings__MyInstance
/// </remarks>
public sealed class InstanceConfig : IValidatableObject
{
    /// <summary>
    /// Unique identifier for this instance.
    /// Used to reference the instance in logs and metrics.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Id { get; set; }

    /// <summary>
    /// Display name for the instance.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// SQL Server hostname or IP address.
    /// </summary>
    [Required]
    public required string ServerName { get; set; }

    /// <summary>
    /// SQL Server port (default 1433).
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 1433;

    /// <summary>
    /// Named instance (e.g., "SQLEXPRESS").
    /// Leave null for default instance.
    /// </summary>
    public string? InstanceName { get; set; }

    /// <summary>
    /// Whether this instance is enabled for monitoring.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Name of the connection string in configuration.
    /// The actual connection string should be in secrets.
    /// </summary>
    public string? ConnectionStringName { get; set; }

    /// <summary>
    /// Use Windows/Integrated Authentication.
    /// </summary>
    public bool UseIntegratedSecurity { get; set; } = true;

    /// <summary>
    /// Application name for connection identification.
    /// </summary>
    public string ApplicationName { get; set; } = "DbExecPlanMonitor";

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    [Range(5, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Command timeout in seconds.
    /// </summary>
    [Range(10, 600)]
    public int CommandTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Whether to encrypt the connection.
    /// </summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>
    /// Trust server certificate (dev environments only).
    /// </summary>
    public bool TrustServerCertificate { get; set; } = false;

    /// <summary>
    /// Whether to prefer Query Store over DMVs.
    /// </summary>
    public bool PreferQueryStore { get; set; } = true;

    /// <summary>
    /// Databases to monitor on this instance.
    /// </summary>
    public List<DatabaseConfig> Databases { get; set; } = [];

    /// <summary>
    /// Database name patterns to exclude (wildcards supported).
    /// </summary>
    public List<string> ExcludeDatabasePatterns { get; set; } = ["tempdb", "model", "msdb"];

    /// <summary>
    /// Tags for categorization (e.g., "production", "critical").
    /// Used for feature flag decisions.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Override for top N queries to collect.
    /// </summary>
    public int? TopNQueries { get; set; }

    /// <summary>
    /// Override for lookback window.
    /// </summary>
    public TimeSpan? LookbackWindow { get; set; }

    /// <summary>
    /// Whether this instance is tagged as production.
    /// </summary>
    public bool IsProduction =>
        Tags.Any(t => t.Equals("production", StringComparison.OrdinalIgnoreCase) ||
                      t.Equals("prod", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UseIntegratedSecurity && string.IsNullOrEmpty(ConnectionStringName))
        {
            yield return new ValidationResult(
                "ConnectionStringName is required when not using Integrated Security",
                new[] { nameof(ConnectionStringName) });
        }
    }
}

/// <summary>
/// Configuration for a specific database to monitor.
/// </summary>
public sealed class DatabaseConfig : IValidatableObject
{
    /// <summary>
    /// Database name.
    /// </summary>
    [Required]
    public required string Name { get; set; }

    /// <summary>
    /// Whether monitoring is enabled for this database.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Collection-specific settings for this database.
    /// </summary>
    public CollectionSettings? Collection { get; set; }

    /// <summary>
    /// Regression detection rules for this database.
    /// </summary>
    public RegressionRules? RegressionRules { get; set; }

    /// <summary>
    /// Query patterns to exclude from collection (regex).
    /// </summary>
    public List<string> ExcludeQueryPatterns { get; set; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Collection is not null)
        {
            if (Collection.TopN < 1 || Collection.TopN > 500)
            {
                yield return new ValidationResult(
                    "TopN must be between 1 and 500",
                    new[] { nameof(Collection.TopN) });
            }
        }
    }
}

/// <summary>
/// Collection settings for a database.
/// </summary>
public sealed class CollectionSettings
{
    /// <summary>
    /// Number of top queries to collect.
    /// </summary>
    public int TopN { get; set; } = 50;

    /// <summary>
    /// Lookback window in minutes.
    /// </summary>
    public int WindowMinutes { get; set; } = 15;

    /// <summary>
    /// Minimum execution count to be considered.
    /// </summary>
    public int MinExecutionCount { get; set; } = 10;

    /// <summary>
    /// Minimum elapsed time in milliseconds.
    /// </summary>
    public double MinElapsedTimeMs { get; set; } = 100;
}

/// <summary>
/// Regression detection rules for a database.
/// </summary>
public sealed class RegressionRules
{
    /// <summary>
    /// Threshold percentage for duration increase.
    /// Default: 150% (2.5x baseline).
    /// </summary>
    public double DurationIncreaseThresholdPercent { get; set; } = 150;

    /// <summary>
    /// Threshold percentage for CPU increase.
    /// Default: 150% (2.5x baseline).
    /// </summary>
    public double CpuIncreaseThresholdPercent { get; set; } = 150;

    /// <summary>
    /// Minimum executions for a query to be evaluated.
    /// </summary>
    public int MinimumExecutions { get; set; } = 20;

    /// <summary>
    /// Minimum baseline data points required.
    /// </summary>
    public int MinimumBaselinePoints { get; set; } = 5;
}
