using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Hotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Infrastructure.Messaging;

/// <summary>
/// Alert channel that sends notifications via Slack incoming webhook.
/// </summary>
public sealed class SlackAlertChannel : IAlertChannel
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<SlackChannelOptions> _options;
    private readonly ILogger<SlackAlertChannel> _logger;

    public SlackAlertChannel(
        HttpClient httpClient,
        IOptionsMonitor<SlackChannelOptions> options,
        ILogger<SlackAlertChannel> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ChannelName => "Slack";

    public bool IsEnabled => _options.CurrentValue.Enabled && 
                             !string.IsNullOrEmpty(_options.CurrentValue.WebhookUrl);

    public async Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct = default)
    {
        var regressionList = regressions.ToList();
        if (!regressionList.Any()) return;

        var payload = CreateRegressionPayload(regressionList);
        await SendPayloadAsync(payload, ct);
    }

    public async Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default)
    {
        var hotspotList = hotspots.ToList();
        if (!hotspotList.Any()) return;

        var payload = CreateHotspotPayload(hotspotList);
        await SendPayloadAsync(payload, ct);
    }

    public async Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default)
    {
        var payload = CreateDailySummaryPayload(summary);
        await SendPayloadAsync(payload, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                text = "✅ DB Exec Plan Monitor - Connection Test Successful",
                attachments = new[]
                {
                    new
                    {
                        color = "good",
                        text = $"Test message sent at {DateTime.UtcNow:u}"
                    }
                }
            };

            await SendPayloadAsync(payload, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slack connection test failed");
            return false;
        }
    }

    private object CreateRegressionPayload(List<RegressionEvent> regressions)
    {
        var options = _options.CurrentValue;
        var criticalCount = regressions.Count(r => r.Severity == RegressionSeverity.Critical);
        var highCount = regressions.Count(r => r.Severity == RegressionSeverity.High);

        var color = criticalCount > 0 ? "danger" : (highCount > 0 ? "warning" : "#439FE0");
        var emoji = criticalCount > 0 ? ":red_circle:" : ":warning:";

        var text = new StringBuilder();
        text.AppendLine($"{emoji} *{regressions.Count} Performance Regression(s) Detected*");
        
        if (criticalCount > 0 && options.MentionOnCritical)
        {
            text.AppendLine("<!channel> Critical alert!");
        }

        var fields = regressions.Take(10).Select(r => new
        {
            title = $"[{r.Severity}] {r.InstanceName}/{r.DatabaseName}",
            value = $"Duration: {r.DurationChangePercent:+#;-#;0}%, CPU: {r.CpuChangePercent:+#;-#;0}%",
            @short = true
        }).ToList();

        return new
        {
            text = text.ToString(),
            attachments = new[]
            {
                new
                {
                    color,
                    fields,
                    footer = $"DB Exec Plan Monitor | {DateTime.UtcNow:u}"
                }
            }
        };
    }

    private object CreateHotspotPayload(List<Hotspot> hotspots)
    {
        var fields = hotspots.Take(10).Select(h => new
        {
            title = $"#{h.Rank} {h.InstanceName}/{h.DatabaseName}",
            value = $"{h.ExecutionCount:N0} execs | {h.AvgDurationMs:N2}ms avg | {h.AvgCpuTimeMs:N2}ms CPU",
            @short = false
        }).ToList();

        return new
        {
            text = $":fire: *Top {hotspots.Count} Performance Hotspots*",
            attachments = new[]
            {
                new
                {
                    color = "#FF6B35",
                    fields,
                    footer = $"DB Exec Plan Monitor | {DateTime.UtcNow:u}"
                }
            }
        };
    }

    private object CreateDailySummaryPayload(DailySummary summary)
    {
        var (emoji, color) = summary.OverallHealth switch
        {
            HealthStatus.Healthy => (":white_check_mark:", "good"),
            HealthStatus.Warning => (":warning:", "warning"),
            HealthStatus.Critical => (":red_circle:", "danger"),
            _ => (":question:", "#808080")
        };

        var fields = new List<object>
        {
            new { title = "Status", value = summary.OverallHealth.ToString(), @short = true },
            new { title = "New Regressions", value = summary.NewRegressions.Count.ToString(), @short = true },
            new { title = "Resolved", value = summary.ResolvedRegressions.Count.ToString(), @short = true },
            new { title = "Databases", value = summary.DatabasesAnalyzed.ToString("N0"), @short = true },
            new { title = "Queries", value = summary.QueriesAnalyzed.ToString("N0"), @short = true },
            new { title = "Hotspots", value = summary.TopHotspots.Count.ToString(), @short = true }
        };

        return new
        {
            text = $"{emoji} *Daily Summary - {summary.Date:yyyy-MM-dd}*",
            attachments = new[]
            {
                new
                {
                    color,
                    fields,
                    footer = $"DB Exec Plan Monitor | {DateTime.UtcNow:u}"
                }
            }
        };
    }

    private async Task SendPayloadAsync(object payload, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(options.WebhookUrl, content, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Slack alert sent successfully");
    }
}
