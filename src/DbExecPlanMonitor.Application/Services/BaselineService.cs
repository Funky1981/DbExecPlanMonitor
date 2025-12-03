using System.Diagnostics;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Default implementation of the baseline service.
/// Computes baselines from historical samples using statistical aggregation.
/// </summary>
public sealed class BaselineService : IBaselineService
{
    private readonly IBaselineRepository _baselineRepository;
    private readonly IPlanMetricsRepository _metricsRepository;
    private readonly IQueryFingerprintRepository _fingerprintRepository;
    private readonly ILogger<BaselineService> _logger;

    public BaselineService(
        IBaselineRepository baselineRepository,
        IPlanMetricsRepository metricsRepository,
        IQueryFingerprintRepository fingerprintRepository,
        ILogger<BaselineService> logger)
    {
        _baselineRepository = baselineRepository ?? throw new ArgumentNullException(nameof(baselineRepository));
        _metricsRepository = metricsRepository ?? throw new ArgumentNullException(nameof(metricsRepository));
        _fingerprintRepository = fingerprintRepository ?? throw new ArgumentNullException(nameof(fingerprintRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PlanBaseline?> ComputeBaselineAsync(
        Guid fingerprintId,
        int lookbackDays = 7,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Computing baseline for fingerprint {FingerprintId} with {LookbackDays} day lookback",
            fingerprintId, lookbackDays);

        // Get fingerprint info
        var fingerprint = await _fingerprintRepository.GetByIdAsync(fingerprintId, ct);
        if (fingerprint == null)
        {
            _logger.LogWarning("Fingerprint {FingerprintId} not found", fingerprintId);
            return null;
        }

        // Define the time window
        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.AddDays(-lookbackDays);
        var window = new TimeWindow(startUtc, endUtc);

        // Get aggregated metrics from the repository
        var aggregated = await _metricsRepository.GetAggregatedMetricsAsync(fingerprintId, window, ct);
        if (aggregated == null || aggregated.SampleCount < 3)
        {
            _logger.LogDebug("Insufficient samples for fingerprint {FingerprintId}: {Count} samples",
                fingerprintId, aggregated?.SampleCount ?? 0);
            return null;
        }

        // Create new baseline
        var baseline = new PlanBaseline
        {
            Id = Guid.NewGuid(),
            FingerprintId = fingerprintId,
            InstanceName = fingerprint.InstanceName, // Use instance from fingerprint
            DatabaseName = fingerprint.DatabaseName,
            ComputedAtUtc = DateTime.UtcNow,
            WindowStartUtc = startUtc,
            WindowEndUtc = endUtc,
            SampleCount = aggregated.SampleCount,
            TotalExecutions = aggregated.TotalExecutions,
            
            // Duration metrics
            MedianDurationUs = aggregated.P50DurationUs ?? aggregated.AvgDurationUs,
            P95DurationUs = aggregated.P95DurationUs,
            P99DurationUs = aggregated.P99DurationUs,
            AvgDurationUs = aggregated.AvgDurationUs,
            MinDurationUs = aggregated.MinDurationUs,
            MaxDurationUs = aggregated.MaxDurationUs,
            
            // CPU metrics
            MedianCpuTimeUs = aggregated.AvgCpuTimeUs, // Using avg as proxy for median
            P95CpuTimeUs = aggregated.P95CpuTimeUs,
            AvgCpuTimeUs = aggregated.AvgCpuTimeUs,
            
            // I/O metrics
            AvgLogicalReads = aggregated.AvgLogicalReads,
            MaxLogicalReads = aggregated.MaxLogicalReads,
            
            // Variance
            DurationStdDev = aggregated.DurationStdDev,
            
            IsActive = true
        };

        // Supersede old baseline and save new one
        await _baselineRepository.SupersedeActiveBaselineAsync(fingerprintId, ct);
        await _baselineRepository.SaveAsync(baseline, ct);

        _logger.LogInformation(
            "Created baseline for fingerprint {FingerprintId}: {SampleCount} samples, " +
            "P95 duration {P95DurationMs:F1}ms",
            fingerprintId, baseline.SampleCount, baseline.P95DurationMs ?? 0);

        return baseline;
    }

    /// <inheritdoc />
    public async Task<BaselineComputationResult> ComputeBaselinesForDatabaseAsync(
        string instanceName,
        string databaseName,
        int lookbackDays = 7,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var processed = 0;
        var created = 0;
        var updated = 0;
        var skipped = 0;

        _logger.LogInformation(
            "Starting baseline computation for {Instance}/{Database}",
            instanceName, databaseName);

        try
        {
            // Get all fingerprints for this database
            var fingerprints = await _fingerprintRepository.GetByDatabaseAsync(databaseName, ct);

            foreach (var fingerprint in fingerprints)
            {
                if (ct.IsCancellationRequested)
                    break;

                processed++;

                try
                {
                    var existingBaseline = await _baselineRepository.GetActiveByFingerprintIdAsync(
                        fingerprint.Id, ct);

                    var baseline = await ComputeBaselineAsync(fingerprint.Id, lookbackDays, ct);

                    if (baseline == null)
                    {
                        skipped++;
                    }
                    else if (existingBaseline == null)
                    {
                        created++;
                    }
                    else
                    {
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Fingerprint {fingerprint.Id}: {ex.Message}");
                    _logger.LogWarning(ex,
                        "Error computing baseline for fingerprint {FingerprintId}",
                        fingerprint.Id);
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Database error: {ex.Message}");
            _logger.LogError(ex,
                "Error during baseline computation for {Instance}/{Database}",
                instanceName, databaseName);
        }

        stopwatch.Stop();

        var result = new BaselineComputationResult
        {
            InstanceName = instanceName,
            DatabaseName = databaseName,
            ComputedAtUtc = DateTime.UtcNow,
            Duration = stopwatch.Elapsed,
            FingerprintsProcessed = processed,
            BaselinesCreated = created,
            BaselinesUpdated = updated,
            Skipped = skipped,
            Errors = errors.Count,
            ErrorMessages = errors.Count > 0 ? errors : null
        };

        _logger.LogInformation(
            "Baseline computation completed for {Instance}/{Database}: " +
            "{Created} created, {Updated} updated, {Skipped} skipped, {Errors} errors in {Duration}ms",
            instanceName, databaseName, created, updated, skipped, errors.Count,
            stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <inheritdoc />
    public async Task<PlanBaseline?> GetActiveBaselineAsync(
        Guid fingerprintId,
        CancellationToken ct = default)
    {
        return await _baselineRepository.GetActiveByFingerprintIdAsync(fingerprintId, ct);
    }

    /// <inheritdoc />
    public async Task<bool> NeedsRefreshAsync(
        Guid fingerprintId,
        TimeSpan maxAge,
        CancellationToken ct = default)
    {
        var baseline = await _baselineRepository.GetActiveByFingerprintIdAsync(fingerprintId, ct);
        
        if (baseline == null)
            return true;

        var age = DateTime.UtcNow - baseline.ComputedAtUtc;
        return age > maxAge;
    }
}
