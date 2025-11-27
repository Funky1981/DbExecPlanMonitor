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
/// Alert channel that sends notifications via Microsoft Teams incoming webhook.
/// </summary>
public sealed class TeamsAlertChannel : IAlertChannel
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<TeamsChannelOptions> _options;
    private readonly ILogger<TeamsAlertChannel> _logger;

    public TeamsAlertChannel(
        HttpClient httpClient,
        IOptionsMonitor<TeamsChannelOptions> options,
        ILogger<TeamsAlertChannel> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ChannelName => "Teams";

    public bool IsEnabled => _options.CurrentValue.Enabled && 
                             !string.IsNullOrEmpty(_options.CurrentValue.WebhookUrl);

    public async Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        var regressionList = regressions.ToList();

        if (!regressionList.Any()) return;

        var card = CreateAdaptiveCard(regressionList, options);
        await SendCardAsync(card, ct);
    }

    public async Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default)
    {
        var hotspotList = hotspots.ToList();
        if (!hotspotList.Any()) return;

        var card = CreateHotspotCard(hotspotList);
        await SendCardAsync(card, ct);
    }

    public async Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default)
    {
        var card = CreateDailySummaryCard(summary);
        await SendCardAsync(card, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var testCard = new
            {
                type = "message",
                attachments = new[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new
                        {
                            type = "AdaptiveCard",
                            version = "1.4",
                            body = new object[]
                            {
                                new { type = "TextBlock", text = "✅ DB Exec Plan Monitor - Connection Test", weight = "bolder" },
                                new { type = "TextBlock", text = $"Test message sent at {DateTime.UtcNow:u}" }
                            }
                        }
                    }
                }
            };

            await SendCardAsync(testCard, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teams connection test failed");
            return false;
        }
    }

    private object CreateAdaptiveCard(List<RegressionEvent> regressions, TeamsChannelOptions options)
    {
        var criticalCount = regressions.Count(r => r.Severity == RegressionSeverity.Critical);
        var highCount = regressions.Count(r => r.Severity == RegressionSeverity.High);
        
        var color = criticalCount > 0 ? "attention" : (highCount > 0 ? "warning" : "accent");
        var title = criticalCount > 0 
            ? $"🔴 {criticalCount} Critical Regression(s) Detected!"
            : $"⚠️ {regressions.Count} Performance Regression(s) Detected";

        var facts = regressions.Take(10).Select(r => new
        {
            title = $"[{r.Severity}] {r.InstanceName}/{r.DatabaseName}",
            value = $"Duration: {r.DurationChangePercent:+#;-#;0}%, CPU: {r.CpuChangePercent:+#;-#;0}%"
        }).ToList();

        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new { type = "TextBlock", text = title, weight = "bolder", size = "large", color },
                            new { type = "TextBlock", text = $"Detected at {DateTime.UtcNow:u}", isSubtle = true },
                            new { type = "FactSet", facts }
                        }
                    }
                }
            }
        };
    }

    private object CreateHotspotCard(List<Hotspot> hotspots)
    {
        var facts = hotspots.Take(10).Select(h => new
        {
            title = $"#{h.Rank} {h.InstanceName}/{h.DatabaseName}",
            value = $"{h.ExecutionCount:N0} execs, {h.AvgDurationMs:N2}ms avg"
        }).ToList();

        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new { type = "TextBlock", text = $"🔥 Top {hotspots.Count} Performance Hotspots", weight = "bolder", size = "large" },
                            new { type = "FactSet", facts }
                        }
                    }
                }
            }
        };
    }

    private object CreateDailySummaryCard(DailySummary summary)
    {
        var statusEmoji = summary.OverallHealth switch
        {
            HealthStatus.Healthy => "✅",
            HealthStatus.Warning => "⚠️",
            HealthStatus.Critical => "🔴",
            _ => "❓"
        };

        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new { type = "TextBlock", text = $"{statusEmoji} Daily Summary - {summary.Date:yyyy-MM-dd}", weight = "bolder", size = "large" },
                            new
                            {
                                type = "FactSet",
                                facts = new[]
                                {
                                    new { title = "Status", value = summary.OverallHealth.ToString() },
                                    new { title = "New Regressions", value = summary.NewRegressions.Count.ToString() },
                                    new { title = "Resolved", value = summary.ResolvedRegressions.Count.ToString() },
                                    new { title = "Databases", value = summary.DatabasesAnalyzed.ToString("N0") },
                                    new { title = "Queries", value = summary.QueriesAnalyzed.ToString("N0") }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private async Task SendCardAsync(object card, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var json = JsonSerializer.Serialize(card);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(options.WebhookUrl, content, ct);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Teams alert sent successfully");
    }
}
