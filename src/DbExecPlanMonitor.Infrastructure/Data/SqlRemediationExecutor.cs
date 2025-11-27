using System.Data;
using DbExecPlanMonitor.Application.Interfaces;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Infrastructure.Data.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbExecPlanMonitor.Infrastructure.Data;

/// <summary>
/// Configuration for the remediation executor.
/// </summary>
public sealed class RemediationExecutorOptions
{
    public const string SectionName = "RemediationExecutor";

    /// <summary>
    /// Whether to allow auto-execution of safe remediations.
    /// </summary>
    public bool AllowAutoExecution { get; set; } = false;

    /// <summary>
    /// Timeout in seconds for remediation scripts.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Remediation types that are safe to auto-execute.
    /// </summary>
    public List<string> AutoExecuteTypes { get; set; } = new()
    {
        "ForcePlan",
        "UpdateStatistics",
        "ClearPlanCache"
    };

    /// <summary>
    /// Keywords that make a script unsafe regardless of type.
    /// </summary>
    public List<string> DangerousKeywords { get; set; } = new()
    {
        "DROP",
        "TRUNCATE",
        "DELETE",
        "xp_cmdshell",
        "sp_configure",
        "SHUTDOWN"
    };
}

/// <summary>
/// Executes remediation suggestions against SQL Server databases.
/// </summary>
public sealed class SqlRemediationExecutor : IRemediationExecutor
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IOptionsMonitor<RemediationExecutorOptions> _options;
    private readonly ILogger<SqlRemediationExecutor> _logger;

    public SqlRemediationExecutor(
        ISqlConnectionFactory connectionFactory,
        IOptionsMonitor<RemediationExecutorOptions> options,
        ILogger<SqlRemediationExecutor> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RemediationExecutionResult> ExecuteAsync(
        RemediationSuggestion suggestion,
        string executedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentException.ThrowIfNullOrWhiteSpace(executedBy);

        var startTime = DateTime.UtcNow;

        // Validate before executing
        var validation = Validate(suggestion);
        if (!validation.IsValid)
        {
            return RemediationExecutionResult.Failed(
                suggestion.Id,
                startTime,
                executedBy,
                $"Validation failed: {string.Join("; ", validation.Issues)}");
        }

        var options = _options.CurrentValue;

        try
        {
            _logger.LogInformation(
                "Executing remediation {SuggestionId} of type {Type} by {ExecutedBy}",
                suggestion.Id,
                suggestion.Type,
                executedBy);

            // Get connection for the target database
            var regression = suggestion.RegressionEvent;
            using var connection = await _connectionFactory.CreateConnectionForDatabaseAsync(
                regression.InstanceName,
                regression.DatabaseName,
                ct);

            using var command = connection.CreateCommand();
            command.CommandText = suggestion.ActionScript!;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = options.CommandTimeoutSeconds;

            var rowsAffected = await command.ExecuteNonQueryAsync(ct);

            _logger.LogInformation(
                "Remediation {SuggestionId} executed successfully, {RowsAffected} rows affected",
                suggestion.Id,
                rowsAffected);

            return RemediationExecutionResult.Succeeded(
                suggestion.Id,
                startTime,
                executedBy,
                suggestion.ActionScript,
                "Remediation executed successfully",
                rowsAffected);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "Remediation {SuggestionId} failed with SQL error {ErrorNumber}: {Message}",
                suggestion.Id,
                ex.Number,
                ex.Message);

            return RemediationExecutionResult.Failed(
                suggestion.Id,
                startTime,
                executedBy,
                $"SQL Error {ex.Number}: {ex.Message}",
                ex.ToString(),
                suggestion.ActionScript);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Remediation {SuggestionId} failed with error: {Message}",
                suggestion.Id,
                ex.Message);

            return RemediationExecutionResult.Failed(
                suggestion.Id,
                startTime,
                executedBy,
                ex.Message,
                ex.ToString(),
                suggestion.ActionScript);
        }
    }

    /// <inheritdoc />
    public RemediationValidationResult Validate(RemediationSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        var issues = new List<string>();
        var warnings = new List<string>();
        var options = _options.CurrentValue;

        // Must have an action script
        if (string.IsNullOrWhiteSpace(suggestion.ActionScript))
        {
            return RemediationValidationResult.Invalid("Suggestion has no action script to execute");
        }

        // Check for dangerous keywords
        var script = suggestion.ActionScript.ToUpperInvariant();
        foreach (var keyword in options.DangerousKeywords)
        {
            if (script.Contains(keyword.ToUpperInvariant()))
            {
                issues.Add($"Script contains dangerous keyword: {keyword}");
            }
        }

        if (issues.Count > 0)
        {
            return RemediationValidationResult.Invalid(issues.ToArray());
        }

        // Check safety level - add as warnings
        if (suggestion.SafetyLevel == ActionSafetyLevel.ManualOnly)
        {
            warnings.Add("This suggestion is marked for manual execution only");
        }

        if (suggestion.SafetyLevel == ActionSafetyLevel.RequiresReview)
        {
            warnings.Add("This suggestion requires human review before execution");
        }

        // Check if suggestion was already applied
        if (suggestion.WasApplied)
        {
            warnings.Add($"This suggestion was already applied on {suggestion.AppliedAtUtc:u}");
        }

        // Check if suggestion was dismissed
        if (suggestion.WasDismissed)
        {
            return RemediationValidationResult.Invalid($"This suggestion was dismissed: {suggestion.DismissalReason}");
        }

        return warnings.Count > 0
            ? RemediationValidationResult.Valid(warnings.ToArray())
            : RemediationValidationResult.Valid();
    }

    /// <inheritdoc />
    public bool CanAutoExecute(RemediationSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        var options = _options.CurrentValue;

        // Auto-execution must be enabled
        if (!options.AllowAutoExecution)
            return false;

        // Safety level must be Safe
        if (suggestion.SafetyLevel != ActionSafetyLevel.Safe)
            return false;

        // Type must be in the allowed list
        if (!options.AutoExecuteTypes.Contains(suggestion.Type.ToString()))
            return false;

        // Must have a script
        if (string.IsNullOrWhiteSpace(suggestion.ActionScript))
            return false;

        // Validation must pass
        var validation = Validate(suggestion);
        return validation.IsValid;
    }
}
