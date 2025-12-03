using DbExecPlanMonitor.Application.Configuration;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Application.Logging;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using DbExecPlanMonitor.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Application.Services;

/// <summary>
/// Implementation of IRemediationGuard that enforces safety rails based on configuration.
/// </summary>
public class RemediationGuardService : IRemediationGuard
{
    private readonly IOptionsMonitor<SecurityOptions> _securityOptions;
    private readonly IRemediationAuditRepository _auditRepository;
    private readonly ILogger<RemediationGuardService> _logger;
    private readonly TimeProvider _timeProvider;

    // System databases that are always excluded from remediation
    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "msdb", "model", "tempdb", "resource"
    };

    public RemediationGuardService(
        IOptionsMonitor<SecurityOptions> securityOptions,
        IRemediationAuditRepository auditRepository,
        ILogger<RemediationGuardService> logger,
        TimeProvider? timeProvider = null)
    {
        _securityOptions = securityOptions ?? throw new ArgumentNullException(nameof(securityOptions));
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public MonitoringMode CurrentMode => _securityOptions.CurrentValue.Mode;

    /// <inheritdoc />
    public EnvironmentType CurrentEnvironment => _securityOptions.CurrentValue.Environment;

    /// <inheritdoc />
    public bool IsRemediationEnabled => _securityOptions.CurrentValue.EnableRemediation;

    /// <inheritdoc />
    public bool IsDryRunMode => _securityOptions.CurrentValue.DryRunMode;

    /// <inheritdoc />
    public async Task<RemediationGuardResult> CheckAsync(
        string instanceName,
        string databaseName,
        RemediationType remediationType,
        RemediationRiskLevel riskLevel,
        CancellationToken cancellationToken = default)
    {
        var options = _securityOptions.CurrentValue;

        _logger.LogDebug(
            LogEventIds.RemediationCheckStarted,
            "Checking remediation permission: Instance={Instance}, Database={Database}, Type={RemediationType}, Risk={RiskLevel}, Mode={Mode}",
            instanceName, databaseName, remediationType, riskLevel, options.Mode);

        // Check 1: Global kill switch
        if (!options.EnableRemediation)
        {
            return Deny("Remediation is globally disabled via EnableRemediation=false",
                "Enable remediation in configuration to allow remediation actions");
        }

        // Check 2: ReadOnly mode blocks all remediations
        if (options.Mode == MonitoringMode.ReadOnly)
        {
            return Deny("Monitoring mode is ReadOnly - no remediation actions permitted",
                "Change mode to SuggestRemediation or AutoApplyLowRisk to enable remediation");
        }

        // Check 3: System database protection
        if (SystemDatabases.Contains(databaseName))
        {
            return Deny($"Database '{databaseName}' is a system database and is excluded from remediation");
        }

        // Check 4: Excluded databases
        if (options.ExcludedDatabases.Any(db => 
            string.Equals(db, databaseName, StringComparison.OrdinalIgnoreCase)))
        {
            return Deny($"Database '{databaseName}' is in the excluded databases list");
        }

        // Check 5: Risk level vs mode
        if (options.Mode == MonitoringMode.SuggestRemediation)
        {
            return Deny("Mode is SuggestRemediation - remediation scripts are logged but not executed",
                "Review the remediation suggestion and execute manually, or change mode to AutoApplyLowRisk");
        }

        // Check 6: AutoApplyLowRisk - only low-risk remediations are auto-applied
        if (options.Mode == MonitoringMode.AutoApplyLowRisk)
        {
            if (riskLevel > RemediationRiskLevel.Low)
            {
                return Deny(
                    $"Risk level {riskLevel} exceeds Low threshold for AutoApplyLowRisk mode",
                    "This remediation requires manual approval. Review and apply manually.");
            }
        }

        // Check 7: Rate limiting - check remediations in the last hour
        var rateLimitResult = await CheckRateLimitAsync(instanceName, options.MaxRemediationsPerHour, cancellationToken);
        if (!rateLimitResult.IsPermitted)
        {
            return rateLimitResult;
        }

        // Check 8: Maintenance window (if required)
        if (options.ProtectiveChecks.RequireMaintenanceWindow)
        {
            var maintenanceResult = CheckMaintenanceWindow(options.ProtectiveChecks);
            if (!maintenanceResult.IsPermitted)
            {
                return maintenanceResult;
            }
        }

        // Check 9: Approval threshold
        if (riskLevel >= options.ApprovalThreshold)
        {
            return Deny(
                $"Risk level {riskLevel} requires explicit approval (threshold: {options.ApprovalThreshold})",
                "Submit for approval through the change management process");
        }

        // All checks passed
        var isDryRun = options.DryRunMode;
        var reason = isDryRun
            ? "Remediation permitted (DRY-RUN mode - will simulate but not execute)"
            : "Remediation permitted - all safety checks passed";

        _logger.LogInformation(
            LogEventIds.RemediationCheckPassed,
            "Remediation check passed: Instance={Instance}, Database={Database}, Type={RemediationType}, DryRun={DryRun}",
            instanceName, databaseName, remediationType, isDryRun);

        return RemediationGuardResult.Permitted(reason, isDryRun);
    }

    private async Task<RemediationGuardResult> CheckRateLimitAsync(
        string instanceName,
        int maxPerHour,
        CancellationToken cancellationToken)
    {
        try
        {
            var oneHourAgo = _timeProvider.GetUtcNow().AddHours(-1);
            var recentRemediations = await _auditRepository.GetRecentAsync(
                instanceName,
                TimeSpan.FromHours(1),
                cancellationToken);

            // Only count successful (non-dry-run) executions
            var executedCount = recentRemediations.Count(r => 
                r.Success && !r.IsDryRun && r.Timestamp >= oneHourAgo);

            if (executedCount >= maxPerHour)
            {
                return Deny(
                    $"Rate limit exceeded: {executedCount} remediations in the last hour (max: {maxPerHour})",
                    $"Wait until the rate limit window resets, or increase MaxRemediationsPerHour");
            }

            return RemediationGuardResult.Permitted($"Rate limit OK: {executedCount}/{maxPerHour} remediations in last hour");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                LogEventIds.RemediationCheckFailed,
                ex,
                "Failed to check rate limit, denying remediation as a safety measure");

            return Deny("Unable to verify rate limit - denying as a safety measure");
        }
    }

    private RemediationGuardResult CheckMaintenanceWindow(ProtectiveChecksOptions checks)
    {
        var currentHour = _timeProvider.GetUtcNow().Hour;
        var startHour = checks.MaintenanceWindowStartHour;
        var endHour = checks.MaintenanceWindowEndHour;

        bool inWindow;
        if (startHour <= endHour)
        {
            // Same-day window (e.g., 2 AM to 6 AM)
            inWindow = currentHour >= startHour && currentHour < endHour;
        }
        else
        {
            // Cross-midnight window (e.g., 22:00 to 4:00)
            inWindow = currentHour >= startHour || currentHour < endHour;
        }

        if (!inWindow)
        {
            return Deny(
                $"Outside maintenance window ({startHour:D2}:00 - {endHour:D2}:00 UTC)",
                "Wait for the maintenance window or disable RequireMaintenanceWindow");
        }

        return RemediationGuardResult.Permitted($"Within maintenance window ({startHour:D2}:00 - {endHour:D2}:00 UTC)");
    }

    private RemediationGuardResult Deny(string reason, string? suggestedAlternative = null)
    {
        _logger.LogWarning(
            LogEventIds.RemediationDenied,
            "Remediation denied: {Reason}",
            reason);

        return RemediationGuardResult.Denied(reason, suggestedAlternative);
    }
}
