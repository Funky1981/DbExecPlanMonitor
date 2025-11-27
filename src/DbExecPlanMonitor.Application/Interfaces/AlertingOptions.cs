namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Configuration options for the alerting subsystem.
/// </summary>
public sealed class AlertingOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Alerting";

    /// <summary>
    /// Whether alerting is enabled globally.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum severity level to trigger alerts.
    /// Regressions below this level are logged but not alerted.
    /// </summary>
    public string MinimumSeverity { get; set; } = "Medium";

    /// <summary>
    /// Whether to send daily summary emails.
    /// </summary>
    public bool SendDailySummary { get; set; } = true;

    /// <summary>
    /// Time of day (UTC) to send daily summary (e.g., "08:00").
    /// </summary>
    public string DailySummaryTimeUtc { get; set; } = "08:00";

    /// <summary>
    /// Maximum number of hotspots to include in summaries.
    /// </summary>
    public int MaxHotspotsInSummary { get; set; } = 10;

    /// <summary>
    /// Cooldown period before re-alerting on the same regression.
    /// Prevents alert fatigue.
    /// </summary>
    public TimeSpan AlertCooldownPeriod { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Email channel configuration.
    /// </summary>
    public EmailChannelOptions? Email { get; set; }

    /// <summary>
    /// Microsoft Teams channel configuration.
    /// </summary>
    public TeamsChannelOptions? Teams { get; set; }

    /// <summary>
    /// Slack channel configuration.
    /// </summary>
    public SlackChannelOptions? Slack { get; set; }
}

/// <summary>
/// Configuration for email alerts.
/// </summary>
public sealed class EmailChannelOptions
{
    public const string SectionName = "Alerting:Email";

    /// <summary>
    /// Whether email alerts are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// SMTP server hostname.
    /// </summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port.
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Whether to use TLS/SSL.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// SMTP username (if authentication required).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SMTP password (if authentication required).
    /// Should be stored in secrets, not config files.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Sender email address.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name.
    /// </summary>
    public string? FromName { get; set; } = "DB Execution Plan Monitor";

    /// <summary>
    /// Recipient email addresses for alerts.
    /// </summary>
    public List<string> Recipients { get; set; } = new();
}

/// <summary>
/// Configuration for Microsoft Teams webhook alerts.
/// </summary>
public sealed class TeamsChannelOptions
{
    public const string SectionName = "Alerting:Teams";

    /// <summary>
    /// Whether Teams alerts are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Teams incoming webhook URL.
    /// Should be stored in secrets, not config files.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether to mention users/channels for critical alerts.
    /// </summary>
    public bool MentionOnCritical { get; set; } = true;

    /// <summary>
    /// User emails to mention on critical alerts (Teams format).
    /// </summary>
    public List<string> MentionEmails { get; set; } = new();
}

/// <summary>
/// Configuration for Slack webhook alerts.
/// </summary>
public sealed class SlackChannelOptions
{
    public const string SectionName = "Alerting:Slack";

    /// <summary>
    /// Whether Slack alerts are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Slack incoming webhook URL.
    /// Should be stored in secrets, not config files.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Slack channel to post to (overrides webhook default).
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>
    /// Username to display for the bot.
    /// </summary>
    public string Username { get; set; } = "DB Monitor";

    /// <summary>
    /// Emoji icon for the bot (e.g., ":database:").
    /// </summary>
    public string IconEmoji { get; set; } = ":chart_with_upwards_trend:";

    /// <summary>
    /// Whether to mention channel on critical alerts.
    /// </summary>
    public bool MentionOnCritical { get; set; } = true;
}
