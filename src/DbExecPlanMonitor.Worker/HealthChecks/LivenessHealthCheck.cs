using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DbExecPlanMonitor.Worker.HealthChecks;

/// <summary>
/// Simple liveness health check that always returns healthy.
/// Used for Kubernetes liveness probes to verify the process is running.
/// </summary>
/// <remarks>
/// <para>
/// Liveness checks should be fast and simple. They only verify that the
/// application process is alive and can respond to requests.
/// </para>
/// <para>
/// For more detailed checks (database connectivity, etc.), use readiness checks.
/// </para>
/// </remarks>
public sealed class LivenessHealthCheck : IHealthCheck
{
    private readonly ILogger<LivenessHealthCheck> _logger;
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public LivenessHealthCheck(ILogger<LivenessHealthCheck> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var uptime = DateTime.UtcNow - StartTime;

        var data = new Dictionary<string, object>
        {
            ["started_at"] = StartTime.ToString("o"),
            ["uptime_seconds"] = uptime.TotalSeconds,
            ["uptime_formatted"] = FormatUptime(uptime)
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Service is alive (uptime: {FormatUptime(uptime)})",
            data));
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        if (uptime.TotalMinutes >= 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }
}
