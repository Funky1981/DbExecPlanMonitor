using System.Net;
using System.Net.Mail;
using System.Text;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Hotspot = DbExecPlanMonitor.Domain.Services.Hotspot;

namespace DbExecPlanMonitor.Infrastructure.Messaging;

/// <summary>
/// Alert channel that sends notifications via SMTP email.
/// </summary>
public sealed class EmailAlertChannel : IAlertChannel
{
    private readonly IOptionsMonitor<EmailChannelOptions> _options;
    private readonly ILogger<EmailAlertChannel> _logger;

    public EmailAlertChannel(
        IOptionsMonitor<EmailChannelOptions> options,
        ILogger<EmailAlertChannel> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ChannelName => "Email";

    public bool IsEnabled => _options.CurrentValue.Enabled && 
                             !string.IsNullOrEmpty(_options.CurrentValue.SmtpHost) &&
                             _options.CurrentValue.Recipients.Any();

    public async Task SendRegressionAlertsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct = default)
    {
        var regressionList = regressions.ToList();
        if (!regressionList.Any()) return;

        var options = _options.CurrentValue;
        var criticalCount = regressionList.Count(r => r.Severity == RegressionSeverity.Critical);
        var highCount = regressionList.Count(r => r.Severity == RegressionSeverity.High);

        var subject = criticalCount > 0
            ? $"[CRITICAL] {criticalCount} Critical Regression(s) - DB Exec Plan Monitor"
            : highCount > 0
                ? $"[HIGH] {highCount} High Severity Regression(s) - DB Exec Plan Monitor"
                : $"[ALERT] {regressionList.Count} Performance Regression(s) - DB Exec Plan Monitor";

        var body = BuildRegressionEmailBody(regressionList);
        await SendEmailAsync(subject, body, ct);
    }

    public async Task SendHotspotSummaryAsync(
        IEnumerable<Hotspot> hotspots,
        CancellationToken ct = default)
    {
        var hotspotList = hotspots.ToList();
        if (!hotspotList.Any()) return;

        var subject = $"Performance Hotspots Summary - DB Exec Plan Monitor";
        var body = BuildHotspotEmailBody(hotspotList);
        await SendEmailAsync(subject, body, ct);
    }

    public async Task SendDailySummaryAsync(
        DailySummary summary,
        CancellationToken ct = default)
    {
        var subject = $"Daily Summary ({summary.Date:yyyy-MM-dd}) - {summary.OverallHealth} - DB Exec Plan Monitor";
        var body = BuildDailySummaryEmailBody(summary);
        await SendEmailAsync(subject, body, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var subject = "Connection Test - DB Exec Plan Monitor";
            var body = $"<html><body><p>Test email sent at {DateTime.UtcNow:u}</p></body></html>";
            await SendEmailAsync(subject, body, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email connection test failed");
            return false;
        }
    }

    private string BuildRegressionEmailBody(List<RegressionEvent> regressions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><head><style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.AppendLine("th { background-color: #4CAF50; color: white; }");
        sb.AppendLine(".critical { background-color: #ffebee; }");
        sb.AppendLine(".high { background-color: #fff3e0; }");
        sb.AppendLine(".medium { background-color: #fffde7; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h2>Performance Regression Alert</h2>");
        sb.AppendLine($"<p>Detected at: {DateTime.UtcNow:u}</p>");
        sb.AppendLine($"<p>Total regressions: {regressions.Count}</p>");

        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Severity</th><th>Instance/Database</th><th>Duration Change</th><th>CPU Change</th><th>Status</th></tr>");

        foreach (var r in regressions)
        {
            var rowClass = r.Severity switch
            {
                RegressionSeverity.Critical => "critical",
                RegressionSeverity.High => "high",
                _ => ""
            };

            sb.AppendLine($"<tr class=\"{rowClass}\">");
            sb.AppendLine($"<td>{r.Severity}</td>");
            sb.AppendLine($"<td>{r.InstanceName}/{r.DatabaseName}</td>");
            sb.AppendLine($"<td>{r.DurationChangePercent:+#;-#;0}%</td>");
            sb.AppendLine($"<td>{r.CpuChangePercent:+#;-#;0}%</td>");
            sb.AppendLine($"<td>{r.Status}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<p><em>Sent by DB Exec Plan Monitor</em></p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private string BuildHotspotEmailBody(List<Hotspot> hotspots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><head><style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.AppendLine("th { background-color: #ff6b35; color: white; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h2>Performance Hotspots</h2>");
        sb.AppendLine($"<p>Top {hotspots.Count} resource-intensive queries</p>");

        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Rank</th><th>Instance/Database</th><th>Executions</th><th>Avg Duration</th><th>Avg CPU</th><th>Ranked By</th></tr>");

        foreach (var h in hotspots)
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>#{h.Rank}</td>");
            sb.AppendLine($"<td>{h.InstanceName}/{h.DatabaseName}</td>");
            sb.AppendLine($"<td>{h.ExecutionCount:N0}</td>");
            sb.AppendLine($"<td>{h.AvgDurationMs:N2}ms</td>");
            sb.AppendLine($"<td>{h.AvgCpuTimeMs:N2}ms</td>");
            sb.AppendLine($"<td>{h.RankedBy}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<p><em>Sent by DB Exec Plan Monitor</em></p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private string BuildDailySummaryEmailBody(DailySummary summary)
    {
        var statusColor = summary.OverallHealth switch
        {
            HealthStatus.Healthy => "#4caf50",
            HealthStatus.Warning => "#ff9800",
            HealthStatus.Critical => "#f44336",
            _ => "#9e9e9e"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<html><head><style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; }");
        sb.AppendLine("table { border-collapse: collapse; margin-bottom: 20px; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; }");
        sb.AppendLine("th { background-color: #2196F3; color: white; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h2>Daily Summary - {summary.Date:yyyy-MM-dd}</h2>");
        sb.AppendLine($"<p style=\"color: {statusColor}; font-weight: bold;\">Status: {summary.OverallHealth}</p>");

        sb.AppendLine("<h3>Overview</h3>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td>New Regressions</td><td>{summary.NewRegressions.Count}</td></tr>");
        sb.AppendLine($"<tr><td>Resolved Regressions</td><td>{summary.ResolvedRegressions.Count}</td></tr>");
        sb.AppendLine($"<tr><td>Databases Analyzed</td><td>{summary.DatabasesAnalyzed:N0}</td></tr>");
        sb.AppendLine($"<tr><td>Queries Analyzed</td><td>{summary.QueriesAnalyzed:N0}</td></tr>");
        sb.AppendLine($"<tr><td>Top Hotspots</td><td>{summary.TopHotspots.Count}</td></tr>");
        sb.AppendLine("</table>");

        if (summary.NewRegressions.Any())
        {
            sb.AppendLine("<h3>New Regressions</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Severity</th><th>Instance/Database</th><th>Duration Change</th><th>CPU Change</th></tr>");
            foreach (var r in summary.NewRegressions)
            {
                sb.AppendLine($"<tr><td>{r.Severity}</td><td>{r.InstanceName}/{r.DatabaseName}</td>");
                sb.AppendLine($"<td>{r.DurationChangePercent:+#;-#;0}%</td><td>{r.CpuChangePercent:+#;-#;0}%</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        if (summary.TopHotspots.Any())
        {
            sb.AppendLine("<h3>Top Hotspots</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Rank</th><th>Instance/Database</th><th>Executions</th><th>Avg Duration</th></tr>");
            foreach (var h in summary.TopHotspots.Take(5))
            {
                sb.AppendLine($"<tr><td>#{h.Rank}</td><td>{h.InstanceName}/{h.DatabaseName}</td>");
                sb.AppendLine($"<td>{h.ExecutionCount:N0}</td><td>{h.AvgDurationMs:N2}ms</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<p><em>Sent by DB Exec Plan Monitor</em></p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private async Task SendEmailAsync(string subject, string body, CancellationToken ct)
    {
        var options = _options.CurrentValue;

        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        {
            EnableSsl = options.UseSsl,
            Credentials = !string.IsNullOrEmpty(options.Username)
                ? new NetworkCredential(options.Username, options.Password)
                : null
        };

        using var message = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName ?? "DB Exec Plan Monitor"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        foreach (var recipient in options.Recipients)
        {
            message.To.Add(recipient);
        }

        await client.SendMailAsync(message, ct);
        _logger.LogDebug("Email sent successfully to {RecipientCount} recipient(s)", options.Recipients.Count);
    }
}
