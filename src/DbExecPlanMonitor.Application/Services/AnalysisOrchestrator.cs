using System.Diagnostics;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Orchestrators;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using DbExecPlanMonitor.Domain.Services;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainHotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Default implementation of the analysis orchestrator.
/// Coordinates regression and hotspot detection across databases.
/// </summary>
public sealed class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly IOptionsMonitor<AnalysisOptions> _options;
    private readonly IOptionsMonitor<MonitoringInstancesOptions> _instancesOptions;
    private readonly IRegressionDetector _regressionDetector;
    private readonly IHotspotDetector _hotspotDetector;
    private readonly IBaselineService _baselineService;
    private readonly IBaselineRepository _baselineRepository;
    private readonly IPlanMetricsRepository _metricsRepository;
    private readonly IQueryFingerprintRepository _fingerprintRepository;
    private readonly IRegressionEventRepository _regressionRepository;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        IOptionsMonitor<AnalysisOptions> options,
        IOptionsMonitor<MonitoringInstancesOptions> instancesOptions,
        IRegressionDetector regressionDetector,
        IHotspotDetector hotspotDetector,
        IBaselineService baselineService,
        IBaselineRepository baselineRepository,
        IPlanMetricsRepository metricsRepository,
        IQueryFingerprintRepository fingerprintRepository,
        IRegressionEventRepository regressionRepository,
        ILogger<AnalysisOrchestrator> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _instancesOptions = instancesOptions ?? throw new ArgumentNullException(nameof(instancesOptions));
        _regressionDetector = regressionDetector ?? throw new ArgumentNullException(nameof(regressionDetector));
        _hotspotDetector = hotspotDetector ?? throw new ArgumentNullException(nameof(hotspotDetector));
        _baselineService = baselineService ?? throw new ArgumentNullException(nameof(baselineService));
        _baselineRepository = baselineRepository ?? throw new ArgumentNullException(nameof(baselineRepository));
        _metricsRepository = metricsRepository ?? throw new ArgumentNullException(nameof(metricsRepository));
        _fingerprintRepository = fingerprintRepository ?? throw new ArgumentNullException(nameof(fingerprintRepository));
        _regressionRepository = regressionRepository ?? throw new ArgumentNullException(nameof(regressionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AnalysisRunSummary> AnalyzeAllAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new List<DatabaseAnalysisResult>();

        var enabledInstances = _instancesOptions.CurrentValue.Instances
            .Where(i => i.Enabled)
            .ToList();

        _logger.LogInformation(
            "Starting analysis run for {InstanceCount} instances",
            enabledInstances.Count);

        foreach (var instance in enabledInstances)
        {
            if (ct.IsCancellationRequested)
                break;

            var databases = instance.Databases?
                .Where(d => d.Enabled)
                .Select(d => d.Name)
                .ToList() ?? [];

            foreach (var dbName in databases)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var result = await AnalyzeDatabaseAsync(instance.Name, dbName, ct);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error analyzing {Instance}/{Database}",
                        instance.Name, dbName);

                    results.Add(new DatabaseAnalysisResult
                    {
                        InstanceName = instance.Name,
                        DatabaseName = dbName,
                        AnalyzedAtUtc = DateTime.UtcNow,
                        Duration = TimeSpan.Zero,
                        Error = ex.Message
                    });
                }
            }
        }

        stopwatch.Stop();

        var summary = new AnalysisRunSummary
        {
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTime.UtcNow,
            Duration = stopwatch.Elapsed,
            DatabaseResults = results
        };

        _logger.LogInformation(
            "Analysis run completed: {Databases} databases, {Regressions} regressions, " +
            "{Hotspots} hotspots in {Duration}ms",
            summary.TotalDatabasesAnalyzed,
            summary.TotalRegressionsDetected,
            summary.TotalHotspotsDetected,
            stopwatch.ElapsedMilliseconds);

        return summary;
    }

    /// <inheritdoc />
    public async Task<DatabaseAnalysisResult> AnalyzeDatabaseAsync(
        string instanceName,
        string databaseName,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var regressions = new List<RegressionEvent>();
        var hotspots = new List<DomainHotspot>();
        var fingerprintsAnalyzed = 0;

        _logger.LogDebug("Analyzing {Instance}/{Database}", instanceName, databaseName);

        try
        {
            var options = _options.CurrentValue;
            var lookbackWindow = new TimeWindow(
                DateTime.UtcNow.Subtract(options.RecentWindow),
                DateTime.UtcNow);

            // Get fingerprints for this database
            var fingerprints = await _fingerprintRepository.GetByDatabaseAsync(databaseName, ct);

            foreach (var fingerprint in fingerprints)
            {
                if (ct.IsCancellationRequested)
                    break;

                fingerprintsAnalyzed++;

                // Get baseline and recent samples
                var baseline = await _baselineRepository.GetActiveByFingerprintIdAsync(fingerprint.Id, ct);
                if (baseline == null)
                {
                    // No baseline, skip regression detection but include in hotspot
                    continue;
                }

                // Get aggregated metrics for recent window
                var aggregated = await _metricsRepository.GetAggregatedMetricsAsync(
                    fingerprint.Id, lookbackWindow, ct);

                if (aggregated == null || aggregated.SampleCount == 0)
                    continue;

                // Map to domain format for regression detection
                var currentMetrics = new AggregatedMetricsForAnalysis
                {
                    FingerprintId = fingerprint.Id,
                    Window = lookbackWindow,
                    SampleCount = aggregated.SampleCount,
                    TotalExecutions = aggregated.TotalExecutions,
                    AvgDurationUs = aggregated.AvgDurationUs,
                    P95DurationUs = aggregated.P95DurationUs,
                    AvgCpuTimeUs = aggregated.AvgCpuTimeUs,
                    P95CpuTimeUs = aggregated.P95CpuTimeUs,
                    AvgLogicalReads = aggregated.AvgLogicalReads
                };

                // Detect regression
                var regression = _regressionDetector.DetectRegression(
                    baseline,
                    currentMetrics,
                    options.RegressionRules);

                if (regression != null)
                {
                    // Check if we already have an active regression for this fingerprint
                    var existingRegression = await _regressionRepository.GetActiveByFingerprintIdAsync(
                        fingerprint.Id, ct);

                    if (existingRegression == null)
                    {
                        await _regressionRepository.SaveAsync(regression, ct);
                        regressions.Add(regression);

                        _logger.LogWarning(
                            "Regression detected for {Instance}/{Database}: {Description}",
                            instanceName, databaseName, regression.Description);
                    }
                }
            }

            // Detect hotspots
            var hotspotResult = await DetectHotspotsAsync(instanceName, databaseName, ct);
            hotspots.AddRange(hotspotResult.Hotspots);

            stopwatch.Stop();

            return new DatabaseAnalysisResult
            {
                InstanceName = instanceName,
                DatabaseName = databaseName,
                AnalyzedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                FingerprintsAnalyzed = fingerprintsAnalyzed,
                RegressionsDetected = regressions.Count,
                HotspotsDetected = hotspots.Count,
                Regressions = regressions,
                Hotspots = hotspots
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error analyzing {Instance}/{Database}", instanceName, databaseName);

            return new DatabaseAnalysisResult
            {
                InstanceName = instanceName,
                DatabaseName = databaseName,
                AnalyzedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                FingerprintsAnalyzed = fingerprintsAnalyzed,
                RegressionsDetected = regressions.Count,
                HotspotsDetected = 0,
                Regressions = regressions,
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<HotspotAnalysisResult> DetectHotspotsAsync(
        string instanceName,
        string databaseName,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var options = _options.CurrentValue;
            var lookbackWindow = new TimeWindow(
                DateTime.UtcNow.Subtract(options.HotspotWindow),
                DateTime.UtcNow);

            // Get latest samples per fingerprint
            var samples = await _metricsRepository.GetLatestSamplesPerFingerprintAsync(
                databaseName,
                options.HotspotRules.TopN * 2, // Get extra to account for filtering
                ct);

            // Map to hotspot input format
            var hotspotSamples = new List<HotspotMetricSample>();
            foreach (var sample in samples)
            {
                var fingerprint = await _fingerprintRepository.GetByIdAsync(sample.FingerprintId, ct);
                if (fingerprint == null) continue;

                var hasActiveRegression = await _regressionRepository.GetActiveByFingerprintIdAsync(
                    sample.FingerprintId, ct) != null;

                hotspotSamples.Add(new HotspotMetricSample
                {
                    FingerprintId = sample.FingerprintId,
                    InstanceName = instanceName,
                    DatabaseName = databaseName,
                    QueryTextSample = fingerprint.QueryTextSample,
                    ExecutionCount = sample.ExecutionCount,
                    TotalCpuTimeMs = sample.TotalCpuTimeUs / 1000.0,
                    TotalDurationMs = sample.TotalDurationUs / 1000.0,
                    TotalLogicalReads = sample.TotalLogicalReads,
                    AvgDurationMs = sample.AvgDurationMs,
                    AvgCpuTimeMs = sample.AvgCpuTimeMs,
                    PlanHash = sample.PlanHash,
                    HasActiveRegression = hasActiveRegression
                });
            }

            // Detect hotspots
            var hotspots = _hotspotDetector.DetectHotspots(hotspotSamples, options.HotspotRules);

            stopwatch.Stop();

            return new HotspotAnalysisResult
            {
                InstanceName = instanceName,
                DatabaseName = databaseName,
                AnalyzedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                Hotspots = hotspots
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error detecting hotspots for {Instance}/{Database}",
                instanceName, databaseName);

            return new HotspotAnalysisResult
            {
                InstanceName = instanceName,
                DatabaseName = databaseName,
                AnalyzedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed,
                Hotspots = [],
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<int> CheckForAutoResolutionsAsync(CancellationToken ct = default)
    {
        var autoResolved = 0;

        try
        {
            var options = _options.CurrentValue;
            var lookbackWindow = new TimeWindow(
                DateTime.UtcNow.Subtract(options.RecentWindow),
                DateTime.UtcNow);

            // Get active regressions
            var activeRegressions = await _regressionRepository.GetActiveAsync(ct);

            foreach (var regression in activeRegressions)
            {
                if (ct.IsCancellationRequested)
                    break;

                // Get baseline
                var baseline = await _baselineRepository.GetActiveByFingerprintIdAsync(
                    regression.FingerprintId, ct);

                if (baseline == null)
                    continue;

                // Get current metrics
                var aggregated = await _metricsRepository.GetAggregatedMetricsAsync(
                    regression.FingerprintId, lookbackWindow, ct);

                if (aggregated == null)
                    continue;

                // Check if performance has returned to baseline
                if (IsPerformanceNormal(baseline, aggregated, options.RegressionRules))
                {
                    regression.AutoResolve();
                    await _regressionRepository.UpdateAsync(regression, ct);
                    autoResolved++;

                    _logger.LogInformation(
                        "Auto-resolved regression {RegressionId} for fingerprint {FingerprintId}",
                        regression.Id, regression.FingerprintId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for auto-resolutions");
        }

        return autoResolved;
    }

    /// <summary>
    /// Checks if current performance is within acceptable range of baseline.
    /// </summary>
    private static bool IsPerformanceNormal(
        PlanBaseline baseline,
        Application.Interfaces.AggregatedMetrics current,
        RegressionDetectionRules rules)
    {
        // Performance is "normal" if it's within 20% of baseline
        // (less strict than the regression threshold)
        const decimal normalThreshold = 20m;

        if (baseline.P95DurationUs.HasValue && current.P95DurationUs.HasValue)
        {
            var increase = CalculatePercentIncrease(
                baseline.P95DurationUs.Value,
                current.P95DurationUs.Value);

            if (increase <= normalThreshold)
                return true;
        }

        return false;
    }

    private static decimal CalculatePercentIncrease(long baseline, long current)
    {
        if (baseline <= 0) return 0;
        return ((decimal)(current - baseline) / baseline) * 100;
    }
}
