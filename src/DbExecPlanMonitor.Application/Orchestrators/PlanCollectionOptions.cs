using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DbExecPlanMonitor.Application.Orchestrators;

/// <summary>
/// Configuration for plan collection behavior.
/// Loaded from appsettings.json or other configuration sources.
/// </summary>
public sealed class PlanCollectionOptions : IValidatableObject
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "PlanCollection";

    /// <summary>
    /// Interval between collection runs.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of top queries to collect per database.
    /// Queries are ranked by total elapsed time.
    /// Default: 50.
    /// </summary>
    public int TopNQueries { get; set; } = 50;

    /// <summary>
    /// Time window for looking back at query statistics.
    /// Default: 15 minutes.
    /// </summary>
    public TimeSpan LookbackWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Minimum execution count for a query to be considered.
    /// Filters out one-off queries.
    /// Default: 2.
    /// </summary>
    public int MinimumExecutionCount { get; set; } = 2;

    /// <summary>
    /// Minimum total elapsed time (in milliseconds) for a query to be considered.
    /// Filters out very fast queries that aren't worth monitoring.
    /// Default: 100ms.
    /// </summary>
    public double MinimumElapsedTimeMs { get; set; } = 100;

    /// <summary>
    /// Whether to prefer Query Store over DMVs when Query Store is available.
    /// Default: true.
    /// </summary>
    public bool PreferQueryStore { get; set; } = true;

    /// <summary>
    /// Maximum time to wait for a collection operation before timing out.
    /// Default: 2 minutes.
    /// </summary>
    public TimeSpan CollectionTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether to continue collecting from other databases when one fails.
    /// Default: true (fail gracefully).
    /// </summary>
    public bool ContinueOnDatabaseError { get; set; } = true;

    /// <summary>
    /// Whether to continue collecting from other instances when one fails.
    /// Default: true (fail gracefully).
    /// </summary>
    public bool ContinueOnInstanceError { get; set; } = true;

    /// <summary>
    /// Maximum degree of parallelism when collecting from multiple instances.
    /// Set to 1 for sequential processing.
    /// Default: 1 (sequential to avoid overwhelming the network/DB).
    /// </summary>
    public int MaxInstanceParallelism { get; set; } = 1;

    /// <summary>
    /// Maximum degree of parallelism when collecting from multiple databases within an instance.
    /// Set to 1 for sequential processing.
    /// Default: 1 (sequential).
    /// </summary>
    public int MaxDatabaseParallelism { get; set; } = 1;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CollectionInterval <= TimeSpan.Zero)
            yield return new ValidationResult("CollectionInterval must be greater than zero.", [nameof(CollectionInterval)]);

        if (TopNQueries <= 0)
            yield return new ValidationResult("TopNQueries must be greater than zero.", [nameof(TopNQueries)]);

        if (LookbackWindow <= TimeSpan.Zero)
            yield return new ValidationResult("LookbackWindow must be greater than zero.", [nameof(LookbackWindow)]);

        if (MinimumExecutionCount < 0)
            yield return new ValidationResult("MinimumExecutionCount cannot be negative.", [nameof(MinimumExecutionCount)]);

        if (MinimumElapsedTimeMs < 0)
            yield return new ValidationResult("MinimumElapsedTimeMs cannot be negative.", [nameof(MinimumElapsedTimeMs)]);

        if (CollectionTimeout <= TimeSpan.Zero)
            yield return new ValidationResult("CollectionTimeout must be greater than zero.", [nameof(CollectionTimeout)]);

        if (MaxInstanceParallelism <= 0)
            yield return new ValidationResult("MaxInstanceParallelism must be at least 1.", [nameof(MaxInstanceParallelism)]);

        if (MaxDatabaseParallelism <= 0)
            yield return new ValidationResult("MaxDatabaseParallelism must be at least 1.", [nameof(MaxDatabaseParallelism)]);
    }
}

/// <summary>
/// Configuration for a specific SQL Server instance to monitor.
/// </summary>
public sealed class MonitoredInstanceOptions
{
    /// <summary>
    /// Friendly name for this instance (used in logs and results).
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Connection string for this instance.
    /// Should use Windows Authentication or a dedicated monitoring account.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Whether this instance is enabled for monitoring.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Databases to monitor on this instance.
    /// If empty or null, all user databases will be monitored.
    /// </summary>
    public List<MonitoredDatabaseOptions>? Databases { get; set; }

    /// <summary>
    /// Database name patterns to exclude (supports wildcards).
    /// Applied when Databases is empty (auto-discovery mode).
    /// </summary>
    public List<string>? ExcludeDatabasePatterns { get; set; }

    /// <summary>
    /// Override TopNQueries for this instance.
    /// </summary>
    public int? TopNQueries { get; set; }

    /// <summary>
    /// Override LookbackWindow for this instance.
    /// </summary>
    public TimeSpan? LookbackWindow { get; set; }
}

/// <summary>
/// Configuration for a specific database to monitor.
/// </summary>
public sealed class MonitoredDatabaseOptions
{
    /// <summary>
    /// Database name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether this database is enabled for monitoring.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Override TopNQueries for this database.
    /// </summary>
    public int? TopNQueries { get; set; }

    /// <summary>
    /// Override LookbackWindow for this database.
    /// </summary>
    public TimeSpan? LookbackWindow { get; set; }

    /// <summary>
    /// Query patterns to exclude from collection (regex patterns).
    /// </summary>
    public List<string>? ExcludeQueryPatterns { get; set; }
}

/// <summary>
/// Root configuration containing all monitored instances.
/// </summary>
public sealed class MonitoringInstancesOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "MonitoringInstances";

    /// <summary>
    /// List of instances to monitor.
    /// </summary>
    public List<MonitoredInstanceOptions> Instances { get; set; } = [];
}
