using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Hotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Coordinates sending alerts across all configured channels.
/// </summary>
/// <remarks>
/// <para>
/// The alert orchestrator acts as a facade over multiple alert channels,
/// handling:
/// <list type="bullet">
/// <item>Routing alerts to all enabled channels</item>
/// <item>Filtering by severity threshold</item>
/// <item>Cooldown management to prevent alert fatigue</item>
/// <item>Error isolation (one channel failure doesn't affect others)</item>
/// </list>
/// </para>
/// </remarks>
public interface IAlertOrchestrator
{
    /// <summary>
    /// Sends regression alerts to all configured channels.
    /// </summary>
    Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct = default);

    /// <summary>
    /// Sends hotspot summary to all configured channels.
    /// </summary>
    Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default);

    /// <summary>
    /// Sends daily summary to all configured channels.
    /// </summary>
    Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity to all configured channels.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> TestAllChannelsAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation of the alert orchestrator.
/// </summary>
public sealed class AlertOrchestrator : IAlertOrchestrator
{
    private readonly IEnumerable<IAlertChannel> _channels;
    private readonly IOptionsMonitor<AlertingOptions> _options;
    private readonly ILogger<AlertOrchestrator> _logger;

    // Track last alert time per regression to implement cooldown
    private readonly Dictionary<Guid, DateTime> _lastAlertTimes = new();
    private readonly object _cooldownLock = new();

    public AlertOrchestrator(
        IEnumerable<IAlertChannel> channels,
        IOptionsMonitor<AlertingOptions> options,
        ILogger<AlertOrchestrator> logger)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        
        if (!options.Enabled)
        {
            _logger.LogDebug("Alerting is disabled, skipping regression alerts");
            return;
        }

        // Filter by severity threshold
        var minimumSeverity = ParseSeverity(options.MinimumSeverity);
        var filteredRegressions = regressions
            .Where(r => r.Severity >= minimumSeverity)
            .Where(r => !IsInCooldown(r.Id, options.AlertCooldownPeriod))
            .ToList();

        if (!filteredRegressions.Any())
        {
            _logger.LogDebug("No regressions to alert after filtering");
            return;
        }

        _logger.LogInformation(
            "Sending {Count} regression alert(s) to {ChannelCount} channel(s)",
            filteredRegressions.Count,
            _channels.Count(c => c.IsEnabled));

        // Record alert times for cooldown
        foreach (var regression in filteredRegressions)
        {
            RecordAlertTime(regression.Id);
        }

        // Send to all enabled channels
        var enabledChannels = _channels.Where(c => c.IsEnabled).ToList();
        var tasks = enabledChannels.Select(channel =>
            SendToChannelSafeAsync(
                channel,
                () => channel.SendRegressionAlertsAsync(filteredRegressions, ct),
                "regression alerts",
                ct));

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        
        if (!options.Enabled)
        {
            _logger.LogDebug("Alerting is disabled, skipping hotspot summary");
            return;
        }

        var hotspotList = hotspots.Take(options.MaxHotspotsInSummary).ToList();

        if (!hotspotList.Any())
        {
            _logger.LogDebug("No hotspots to report");
            return;
        }

        _logger.LogInformation(
            "Sending hotspot summary ({Count} hotspots) to {ChannelCount} channel(s)",
            hotspotList.Count,
            _channels.Count(c => c.IsEnabled));

        var enabledChannels = _channels.Where(c => c.IsEnabled).ToList();
        var tasks = enabledChannels.Select(channel =>
            SendToChannelSafeAsync(
                channel,
                () => channel.SendHotspotSummaryAsync(hotspotList, ct),
                "hotspot summary",
                ct));

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        
        if (!options.Enabled || !options.SendDailySummary)
        {
            _logger.LogDebug("Daily summary is disabled");
            return;
        }

        _logger.LogInformation(
            "Sending daily summary for {Date} to {ChannelCount} channel(s)",
            summary.Date,
            _channels.Count(c => c.IsEnabled));

        var enabledChannels = _channels.Where(c => c.IsEnabled).ToList();
        var tasks = enabledChannels.Select(channel =>
            SendToChannelSafeAsync(
                channel,
                () => channel.SendDailySummaryAsync(summary, ct),
                "daily summary",
                ct));

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, bool>> TestAllChannelsAsync(
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, bool>();

        foreach (var channel in _channels)
        {
            try
            {
                var success = await channel.TestConnectionAsync(ct);
                results[channel.ChannelName] = success;

                _logger.LogInformation(
                    "Channel {ChannelName} test: {Result}",
                    channel.ChannelName,
                    success ? "Success" : "Failed");
            }
            catch (Exception ex)
            {
                results[channel.ChannelName] = false;
                _logger.LogError(ex,
                    "Channel {ChannelName} test failed with exception",
                    channel.ChannelName);
            }
        }

        return results;
    }

    private async Task SendToChannelSafeAsync(
        IAlertChannel channel,
        Func<Task> sendAction,
        string alertType,
        CancellationToken ct)
    {
        try
        {
            await sendAction();
            _logger.LogDebug(
                "Successfully sent {AlertType} to {ChannelName}",
                alertType,
                channel.ChannelName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Sending {AlertType} to {ChannelName} was cancelled",
                alertType,
                channel.ChannelName);
        }
        catch (Exception ex)
        {
            // Log but don't rethrow - one channel failure shouldn't affect others
            _logger.LogError(ex,
                "Failed to send {AlertType} to {ChannelName}",
                alertType,
                channel.ChannelName);
        }
    }

    private bool IsInCooldown(Guid regressionId, TimeSpan cooldownPeriod)
    {
        lock (_cooldownLock)
        {
            if (_lastAlertTimes.TryGetValue(regressionId, out var lastAlertTime))
            {
                var elapsed = DateTime.UtcNow - lastAlertTime;
                if (elapsed < cooldownPeriod)
                {
                    _logger.LogDebug(
                        "Regression {RegressionId} is in cooldown ({Remaining} remaining)",
                        regressionId,
                        cooldownPeriod - elapsed);
                    return true;
                }
            }
            return false;
        }
    }

    private void RecordAlertTime(Guid regressionId)
    {
        lock (_cooldownLock)
        {
            _lastAlertTimes[regressionId] = DateTime.UtcNow;

            // Clean up old entries periodically
            if (_lastAlertTimes.Count > 1000)
            {
                var cutoff = DateTime.UtcNow.AddDays(-1);
                var oldEntries = _lastAlertTimes
                    .Where(kv => kv.Value < cutoff)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in oldEntries)
                {
                    _lastAlertTimes.Remove(key);
                }
            }
        }
    }

    private static RegressionSeverity ParseSeverity(string severity)
    {
        return Enum.TryParse<RegressionSeverity>(severity, ignoreCase: true, out var result)
            ? result
            : RegressionSeverity.Medium;
    }
}
