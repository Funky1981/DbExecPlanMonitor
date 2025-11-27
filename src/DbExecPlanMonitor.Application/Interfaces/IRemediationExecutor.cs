using DbExecPlanMonitor.Domain.Entities;

namespace DbExecPlanMonitor.Application.Interfaces;

/// <summary>
/// Executes remediation actions against SQL Server databases.
/// </summary>
/// <remarks>
/// <para>
/// This is a critical component with significant safety implications.
/// All executions must be:
/// <list type="bullet">
/// <item>Logged with full audit trail</item>
/// <item>Validated against safety rules</item>
/// <item>Executed with appropriate permissions</item>
/// <item>Monitored for success/failure</item>
/// </list>
/// </para>
/// <para>
/// <strong>Safety Hierarchy:</strong>
/// <list type="bullet">
/// <item>SafeToReview - No execution, just logging</item>
/// <item>LowRisk - Can be auto-executed if enabled (e.g., UPDATE STATISTICS)</item>
/// <item>RequiresReview - Manual review required before execution</item>
/// <item>RequiresApproval - Explicit approval workflow required</item>
/// <item>HighRisk - Should never be auto-executed</item>
/// </list>
/// </para>
/// </remarks>
public interface IRemediationExecutor
{
    /// <summary>
    /// Executes a remediation suggestion against the target database.
    /// </summary>
    /// <param name="suggestion">The suggestion to execute.</param>
    /// <param name="executedBy">The identity executing the action (for audit).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the execution.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the suggestion cannot be executed (no script, wrong safety level, etc.)
    /// </exception>
    Task<RemediationExecutionResult> ExecuteAsync(
        RemediationSuggestion suggestion,
        string executedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Validates whether a suggestion can be executed.
    /// </summary>
    /// <param name="suggestion">The suggestion to validate.</param>
    /// <returns>Validation result with any issues.</returns>
    RemediationValidationResult Validate(RemediationSuggestion suggestion);

    /// <summary>
    /// Checks if a suggestion type is eligible for auto-execution.
    /// </summary>
    /// <param name="suggestion">The suggestion to check.</param>
    /// <returns>True if the suggestion can be auto-executed based on current configuration.</returns>
    bool CanAutoExecute(RemediationSuggestion suggestion);
}

/// <summary>
/// Result of a remediation execution attempt.
/// </summary>
public sealed class RemediationExecutionResult
{
    /// <summary>
    /// Whether the execution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The suggestion that was executed.
    /// </summary>
    public required Guid SuggestionId { get; init; }

    /// <summary>
    /// When the execution started.
    /// </summary>
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>
    /// When the execution completed.
    /// </summary>
    public required DateTime CompletedAtUtc { get; init; }

    /// <summary>
    /// Duration of the execution.
    /// </summary>
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

    /// <summary>
    /// The script that was executed (for audit).
    /// </summary>
    public string? ExecutedScript { get; init; }

    /// <summary>
    /// Who executed the action.
    /// </summary>
    public required string ExecutedBy { get; init; }

    /// <summary>
    /// Result message (success or error details).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Exception details if execution failed.
    /// </summary>
    public string? ErrorDetails { get; init; }

    /// <summary>
    /// Number of rows affected (if applicable).
    /// </summary>
    public int? RowsAffected { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static RemediationExecutionResult Succeeded(
        Guid suggestionId,
        DateTime startedAt,
        string executedBy,
        string? script = null,
        string? message = null,
        int? rowsAffected = null)
    {
        return new RemediationExecutionResult
        {
            Success = true,
            SuggestionId = suggestionId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTime.UtcNow,
            ExecutedBy = executedBy,
            ExecutedScript = script,
            Message = message ?? "Executed successfully",
            RowsAffected = rowsAffected
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static RemediationExecutionResult Failed(
        Guid suggestionId,
        DateTime startedAt,
        string executedBy,
        string errorMessage,
        string? errorDetails = null,
        string? script = null)
    {
        return new RemediationExecutionResult
        {
            Success = false,
            SuggestionId = suggestionId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTime.UtcNow,
            ExecutedBy = executedBy,
            ExecutedScript = script,
            Message = errorMessage,
            ErrorDetails = errorDetails
        };
    }
}

/// <summary>
/// Result of validating a remediation suggestion.
/// </summary>
public sealed class RemediationValidationResult
{
    /// <summary>
    /// Whether the suggestion is valid for execution.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation issues found.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Warnings that don't prevent execution but should be noted.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static RemediationValidationResult Valid(params string[] warnings)
    {
        return new RemediationValidationResult
        {
            IsValid = true,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    public static RemediationValidationResult Invalid(params string[] issues)
    {
        return new RemediationValidationResult
        {
            IsValid = false,
            Issues = issues
        };
    }
}
