using System.Diagnostics;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Application.Orchestrators;

/// <summary>
/// Orchestrates the collection of execution plan metrics from SQL Server instances.
/// Coordinates between data providers, fingerprinting, and storage.
/// </summary>
public sealed class PlanCollectionOrchestrator : IPlanCollectionOrchestrator
{
    private readonly IOptionsMonitor<PlanCollectionOptions> _options;
    private readonly IOptionsMonitor<MonitoringInstancesOptions> _instancesOptions;
    private readonly IPlanStatisticsProvider _statisticsProvider;
    private readonly IQueryFingerprintService _fingerprintService;
    private readonly IQueryFingerprintRepository _fingerprintRepository;
    private readonly IPlanMetricsRepository _metricsRepository;
    private readonly ILogger<PlanCollectionOrchestrator> _logger;

    public PlanCollectionOrchestrator(
        IOptionsMonitor<PlanCollectionOptions> options,
        IOptionsMonitor<MonitoringInstancesOptions> instancesOptions,
        IPlanStatisticsProvider statisticsProvider,
        IQueryFingerprintService fingerprintService,
        IQueryFingerprintRepository fingerprintRepository,
        IPlanMetricsRepository metricsRepository,
        ILogger<PlanCollectionOrchestrator> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _instancesOptions = instancesOptions ?? throw new ArgumentNullException(nameof(instancesOptions));
        _statisticsProvider = statisticsProvider ?? throw new ArgumentNullException(nameof(statisticsProvider));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _fingerprintRepository = fingerprintRepository ?? throw new ArgumentNullException(nameof(fingerprintRepository));
        _metricsRepository = metricsRepository ?? throw new ArgumentNullException(nameof(metricsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CollectionRunSummary> CollectAllAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var instanceResults = new List<InstanceCollectionResult>();

        var enabledInstances = _instancesOptions.CurrentValue.Instances
            .Where(i => i.Enabled)
            .ToList();

        _logger.LogInformation(
            "Starting collection run for {InstanceCount} enabled instance(s)",
            enabledInstances.Count);

        foreach (var instanceConfig in enabledInstances)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Collection cancelled");
                break;
            }

            try
            {
                var result = await CollectInstanceInternalAsync(instanceConfig, ct);
                instanceResults.Add(result);
            }
            catch (Exception ex) when (_options.CurrentValue.ContinueOnInstanceError)
            {
                _logger.LogError(ex,
                    "Error collecting from instance {InstanceName}, continuing with other instances",
                    instanceConfig.Name);

                instanceResults.Add(new InstanceCollectionResult
                {
                    InstanceName = instanceConfig.Name,
                    StartedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    Duration = TimeSpan.Zero,
                    DatabaseResults = [],
                    Error = ex.Message
                });
            }
        }

        stopwatch.Stop();
        var completedAt = DateTime.UtcNow;

        var summary = new CollectionRunSummary
        {
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            Duration = stopwatch.Elapsed,
            InstanceResults = instanceResults
        };

        _logger.LogInformation(
            "Collection run completed in {Duration}ms: {SuccessInstances}/{TotalInstances} instances, " +
            "{QueriesCollected} queries collected, {SamplesSaved} samples saved",
            stopwatch.ElapsedMilliseconds,
            summary.SuccessfulInstances,
            summary.TotalInstances,
            summary.TotalQueriesCollected,
            summary.TotalSamplesSaved);

        return summary;
    }

    /// <inheritdoc />
    public async Task<InstanceCollectionResult> CollectInstanceAsync(
        string instanceName,
        CancellationToken ct = default)
    {
        var instanceConfig = _instancesOptions.CurrentValue.Instances
            .FirstOrDefault(i => i.Name.Equals(instanceName, StringComparison.OrdinalIgnoreCase));

        if (instanceConfig == null)
        {
            throw new ArgumentException($"Instance '{instanceName}' not found in configuration", nameof(instanceName));
        }

        return await CollectInstanceInternalAsync(instanceConfig, ct);
    }

    /// <inheritdoc />
    public async Task<DatabaseCollectionResult> CollectDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken ct = default)
    {
        var instanceConfig = _instancesOptions.CurrentValue.Instances
            .FirstOrDefault(i => i.Name.Equals(instanceName, StringComparison.OrdinalIgnoreCase));

        if (instanceConfig == null)
        {
            throw new ArgumentException($"Instance '{instanceName}' not found in configuration", nameof(instanceName));
        }

        var dbConfig = instanceConfig.Databases?
            .FirstOrDefault(d => d.Name.Equals(databaseName, StringComparison.OrdinalIgnoreCase));

        // If no explicit config, create a default one
        dbConfig ??= new MonitoredDatabaseOptions { Name = databaseName };

        return await CollectDatabaseInternalAsync(instanceConfig, dbConfig, ct);
    }

    private async Task<InstanceCollectionResult> CollectInstanceInternalAsync(
        MonitoredInstanceOptions instanceConfig,
        CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var databaseResults = new List<DatabaseCollectionResult>();

        _logger.LogDebug("Collecting from instance {InstanceName}", instanceConfig.Name);

        try
        {
            var databasesToCollect = await GetDatabasesToCollectAsync(instanceConfig, ct);

            foreach (var dbConfig in databasesToCollect)
            {
                if (ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Collection cancelled for instance {InstanceName}", instanceConfig.Name);
                    break;
                }

                try
                {
                    var result = await CollectDatabaseInternalAsync(instanceConfig, dbConfig, ct);
                    databaseResults.Add(result);
                }
                catch (Exception ex) when (_options.CurrentValue.ContinueOnDatabaseError)
                {
                    _logger.LogError(ex,
                        "Error collecting from database {DatabaseName} on {InstanceName}, continuing",
                        dbConfig.Name,
                        instanceConfig.Name);

                    databaseResults.Add(new DatabaseCollectionResult
                    {
                        InstanceName = instanceConfig.Name,
                        DatabaseName = dbConfig.Name,
                        StartedAtUtc = DateTime.UtcNow,
                        CompletedAtUtc = DateTime.UtcNow,
                        Duration = TimeSpan.Zero,
                        Error = ex.Message
                    });
                }
            }

            stopwatch.Stop();

            return new InstanceCollectionResult
            {
                InstanceName = instanceConfig.Name,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                DatabaseResults = databaseResults
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to collect from instance {InstanceName}", instanceConfig.Name);

            return new InstanceCollectionResult
            {
                InstanceName = instanceConfig.Name,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                DatabaseResults = databaseResults,
                Error = ex.Message
            };
        }
    }

    private async Task<DatabaseCollectionResult> CollectDatabaseInternalAsync(
        MonitoredInstanceOptions instanceConfig,
        MonitoredDatabaseOptions dbConfig,
        CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // Resolve effective configuration (database → instance → global)
        var effectiveTopN = dbConfig.TopNQueries 
            ?? instanceConfig.TopNQueries 
            ?? _options.CurrentValue.TopNQueries;
        
        var effectiveLookback = dbConfig.LookbackWindow 
            ?? instanceConfig.LookbackWindow 
            ?? _options.CurrentValue.LookbackWindow;

        var sampledWindow = new TimeWindow(
            DateTime.UtcNow.Subtract(effectiveLookback),
            DateTime.UtcNow);

        _logger.LogDebug(
            "Collecting top {TopN} queries from {InstanceName}/{DatabaseName} (lookback: {Lookback})",
            effectiveTopN,
            instanceConfig.Name,
            dbConfig.Name,
            effectiveLookback);

        try
        {
            // 1. Fetch statistics from SQL Server
            var statistics = await _statisticsProvider.GetTopQueriesByElapsedTimeAsync(
                instanceConfig.ConnectionString,
                dbConfig.Name,
                effectiveTopN,
                sampledWindow,
                ct);

            var statisticsList = statistics.ToList();
            var queriesCollected = statisticsList.Count;
            var newFingerprints = 0;
            var samplesSaved = 0;
            var usedQueryStore = false; // TODO: Detect from provider

            _logger.LogDebug(
                "Retrieved {QueryCount} queries from {InstanceName}/{DatabaseName}",
                queriesCollected,
                instanceConfig.Name,
                dbConfig.Name);

            // 2. Process each query
            foreach (var stat in statisticsList)
            {
                // Create or lookup fingerprint
                var fingerprint = stat.QueryHash != null
                    ? _fingerprintService.CreateFingerprintFromHash(stat.QueryHash, stat.SqlText)
                    : _fingerprintService.CreateFingerprint(stat.SqlText);

                // Upsert fingerprint to get/create ID
                var fingerprintResult = await _fingerprintRepository.UpsertAsync(
                    instanceConfig.Name,
                    dbConfig.Name,
                    fingerprint.Hash,
                    fingerprint.SampleText,
                    fingerprint.NormalizedText ?? fingerprint.SampleText,
                    ct);

                if (fingerprintResult.IsNew)
                {
                    newFingerprints++;
                }

                // Create and save metrics sample
                // Convert milliseconds to microseconds for storage
                var metrics = new PlanMetricSampleRecord
                {
                    FingerprintId = fingerprintResult.Id,
                    InstanceName = instanceConfig.Name,
                    DatabaseName = dbConfig.Name,
                    SampledAtUtc = DateTime.UtcNow,
                    ExecutionCount = stat.ExecutionCount,
                    TotalCpuTimeUs = (long)(stat.TotalCpuTimeMs * 1000),
                    AvgCpuTimeUs = (long)(stat.AvgCpuTimeMs * 1000),
                    TotalDurationUs = (long)(stat.TotalElapsedTimeMs * 1000),
                    AvgDurationUs = (long)(stat.AvgElapsedTimeMs * 1000),
                    TotalLogicalReads = stat.TotalLogicalReads,
                    AvgLogicalReads = (long)stat.AvgLogicalReads,
                    TotalLogicalWrites = stat.TotalLogicalWrites,
                    TotalPhysicalReads = stat.TotalPhysicalReads,
                    // Populate plan details from provider
                    PlanHash = stat.PlanHandle // PlanHandle can serve as plan identifier
                };

                await _metricsRepository.SaveSampleAsync(instanceConfig.Name, metrics, ct);
                samplesSaved++;
            }

            stopwatch.Stop();

            _logger.LogDebug(
                "Completed {InstanceName}/{DatabaseName}: {NewFingerprints} new fingerprints, " +
                "{SamplesSaved} samples saved in {Duration}ms",
                instanceConfig.Name,
                dbConfig.Name,
                newFingerprints,
                samplesSaved,
                stopwatch.ElapsedMilliseconds);

            return new DatabaseCollectionResult
            {
                InstanceName = instanceConfig.Name,
                DatabaseName = dbConfig.Name,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                QueriesCollected = queriesCollected,
                NewFingerprintsCreated = newFingerprints,
                SamplesSaved = samplesSaved,
                UsedQueryStore = usedQueryStore,
                SampledWindow = sampledWindow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Failed to collect from {InstanceName}/{DatabaseName}",
                instanceConfig.Name,
                dbConfig.Name);

            return new DatabaseCollectionResult
            {
                InstanceName = instanceConfig.Name,
                DatabaseName = dbConfig.Name,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Resolves which databases to collect from, either from explicit config or auto-discovery.
    /// </summary>
    private Task<IReadOnlyList<MonitoredDatabaseOptions>> GetDatabasesToCollectAsync(
        MonitoredInstanceOptions instanceConfig,
        CancellationToken ct)
    {
        // If databases are explicitly configured, use those
        if (instanceConfig.Databases?.Any() == true)
        {
            return Task.FromResult<IReadOnlyList<MonitoredDatabaseOptions>>(
                instanceConfig.Databases
                    .Where(d => d.Enabled)
                    .ToList());
        }

        // Otherwise, auto-discover user databases
        // TODO: Implement database discovery via sys.databases
        _logger.LogWarning(
            "No databases configured for {InstanceName}. " +
            "Database auto-discovery not yet implemented.",
            instanceConfig.Name);

        return Task.FromResult<IReadOnlyList<MonitoredDatabaseOptions>>([]);
    }
}
