# Doc 08: Alerting and Remediation Workflows

This document covers the implementation of alerting channels and remediation execution capabilities for the DB Exec Plan Monitor service.

## Overview

Doc 08 implements a flexible alerting system using the Strategy pattern for pluggable notification channels, combined with remediation suggestion and execution capabilities for automated or semi-automated performance issue resolution.

## Architecture

### Design Patterns

- **Strategy Pattern**: `IAlertChannel` allows pluggable alert destinations (Teams, Slack, Email, etc.)
- **Factory Methods**: `RemediationExecutionResult` and `RemediationValidationResult` use static factory methods for object creation
- **Options Pattern**: All channels use `IOptionsMonitor<T>` for runtime configuration updates
- **Facade Pattern**: `AlertOrchestrator` coordinates multiple alert channels with cooldown management

### Layer Responsibilities

| Layer | Components | Purpose |
|-------|------------|---------|
| Domain | `IRemediationAdvisor`, `RemediationAdvisor`, `RemediationSuggestionDto` | Business logic for generating remediation suggestions |
| Application | `IAlertChannel`, `IRemediationExecutor`, `AlertOrchestrator`, Configuration Options | Interfaces and orchestration |
| Infrastructure | Alert channel implementations, `SqlRemediationExecutor` | External integrations and SQL execution |

## Components Created

### Application Layer (`DbExecPlanMonitor.Application`)

#### `Interfaces/IAlertChannel.cs`
Strategy interface for pluggable alert channels.

```csharp
public interface IAlertChannel
{
    string ChannelName { get; }
    bool IsEnabled { get; }
    
    Task SendRegressionAlertsAsync(IEnumerable<RegressionEvent> regressions, CancellationToken ct = default);
    Task SendHotspotSummaryAsync(IEnumerable<Hotspot> hotspots, CancellationToken ct = default);
    Task SendDailySummaryAsync(DailySummary summary, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
```

Also includes:
- `DailySummary` DTO with `DatabasesAnalyzed`, `QueriesAnalyzed`, `NewRegressions`, `ResolvedRegressions`, `TopHotspots`
- `HealthStatus` enum: `Healthy`, `Warning`, `Critical`, `Unknown`

#### `Interfaces/AlertingOptions.cs`
Configuration options for alerting channels.

| Class | Purpose |
|-------|---------|
| `AlertingOptions` | Master configuration with cooldown settings |
| `EmailChannelOptions` | SMTP server, credentials, recipients |
| `TeamsChannelOptions` | Webhook URL, mention settings |
| `SlackChannelOptions` | Webhook URL, channel, mention on critical |
| `RemediationOptions` | Auto-execution settings, allowed types |
| `RemediationExecutorOptions` | Timeouts, dry-run mode, safety levels |

#### `Interfaces/IRemediationExecutor.cs`
Interface for executing remediation actions against SQL Server.

```csharp
public interface IRemediationExecutor
{
    Task<RemediationExecutionResult> ExecuteAsync(
        RemediationSuggestion suggestion,
        string executedBy,
        CancellationToken ct = default);
        
    RemediationValidationResult Validate(RemediationSuggestion suggestion);
    bool CanAutoExecute(RemediationSuggestion suggestion);
}
```

Also includes:
- `RemediationExecutionResult` with static factories `Succeeded()` and `Failed()`
- `RemediationValidationResult` with static factories `Valid()` and `Invalid()`

#### `Orchestrators/AlertOrchestrator.cs`
Facade that coordinates multiple alert channels with intelligent cooldown management.

Features:
- Routes regressions to all enabled channels
- Implements cooldown to prevent alert fatigue
- Aggregates channel test results
- Provides channel status enumeration

### Domain Layer (`DbExecPlanMonitor.Domain`)

#### `Services/IRemediationAdvisor.cs`
Domain service interface for generating remediation suggestions.

```csharp
public interface IRemediationAdvisor
{
    IReadOnlyList<RemediationSuggestionDto> Suggest(RegressionEvent regression);
}
```

Also includes `RemediationSuggestionDto` with:
- `Type`: Type of remediation (UpdateStatistics, CreateIndex, etc.)
- `Description`: Human-readable description
- `Rationale`: Why this remediation is suggested
- `ActionScript`: SQL script to execute
- `SafetyLevel`: Safe, RequiresReview, or ManualOnly
- `Priority`: Suggested execution priority

#### `Services/RemediationAdvisor.cs`
Implementation that analyzes regression patterns and suggests appropriate remediations.

Suggestions by regression type:
| Condition | Suggested Remediation |
|-----------|----------------------|
| Plan change detected | Review execution plan, consider plan guides |
| Duration increase > 100% | Update statistics |
| CPU increase > 100% | Check for missing indexes, query tuning |
| High severity | Escalate to DBA review |

### Infrastructure Layer (`DbExecPlanMonitor.Infrastructure`)

#### `Messaging/LogOnlyAlertChannel.cs`
Development/testing channel that logs all alerts without external dependencies.

#### `Messaging/TeamsAlertChannel.cs`
Microsoft Teams integration using Adaptive Cards via incoming webhooks.

Features:
- Rich formatting with color-coded severity
- FactSet display for regression details
- Connection testing capability

#### `Messaging/SlackAlertChannel.cs`
Slack integration using incoming webhooks with attachments.

Features:
- Color-coded attachments by severity
- `@channel` mentions on critical alerts (configurable)
- Structured field display

#### `Messaging/EmailAlertChannel.cs`
SMTP-based email notifications with HTML formatting.

Features:
- HTML tables with severity color coding
- Supports multiple recipients
- Optional SSL/TLS encryption
- Credential authentication

#### `Data/SqlRemediationExecutor.cs`
Executes remediation scripts against SQL Server with full safety validation.

Features:
- Validates safety level before execution
- Checks for dangerous SQL patterns (DROP, DELETE, TRUNCATE)
- Uses factory to connect to correct instance/database
- Full audit logging
- Configurable command timeout
- Dry-run mode for testing

Safety validation includes:
- ActionSafetyLevel check (Safe, RequiresReview, ManualOnly)
- SQL pattern analysis for dangerous commands
- Script emptiness validation

### DI Registration

`ServiceCollectionExtensions.cs` extended with:

```csharp
// Add alerting services
services.AddAlerting(configuration);

// Add remediation services
services.AddRemediation(configuration);
```

## Configuration

### appsettings.json Example

```json
{
  "Alerting": {
    "CooldownMinutes": 15,
    "MaxAlertsPerHour": 10,
    "Email": {
      "Enabled": false,
      "SmtpHost": "smtp.company.com",
      "SmtpPort": 587,
      "UseSsl": true,
      "Username": "",
      "Password": "",
      "FromAddress": "dbmonitor@company.com",
      "Recipients": ["dba-team@company.com"]
    },
    "Teams": {
      "Enabled": true,
      "WebhookUrl": "https://company.webhook.office.com/...",
      "MentionOnCritical": true
    },
    "Slack": {
      "Enabled": false,
      "WebhookUrl": "https://hooks.slack.com/services/...",
      "Channel": "#db-alerts",
      "MentionOnCritical": true
    }
  },
  "Remediation": {
    "AutoExecutionEnabled": false,
    "AllowedAutoTypes": ["UpdateStatistics"],
    "Executor": {
      "CommandTimeoutSeconds": 300,
      "DryRunMode": true,
      "AllowedSafetyLevels": ["Safe"]
    }
  }
}
```

## Safety Considerations

### ActionSafetyLevel Enum

| Level | Description | Auto-Executable |
|-------|-------------|-----------------|
| `Safe` | Low-risk operations like UPDATE STATISTICS | Yes (if enabled) |
| `RequiresReview` | Needs DBA review before execution | No |
| `ManualOnly` | Must be executed manually by DBA | No |

### Blocked SQL Patterns

The executor blocks scripts containing:
- `DROP` (tables, indexes, databases)
- `DELETE` without WHERE (data loss risk)
- `TRUNCATE` (data loss)
- `ALTER DATABASE`
- `SHUTDOWN`

## Usage Example

```csharp
// In a background service or controller
public class AlertingService
{
    private readonly AlertOrchestrator _alertOrchestrator;
    private readonly IRemediationAdvisor _advisor;
    private readonly IRemediationExecutor _executor;
    
    public async Task ProcessRegressionsAsync(
        IEnumerable<RegressionEvent> regressions,
        CancellationToken ct)
    {
        // Send alerts for new regressions
        await _alertOrchestrator.SendRegressionAlertsAsync(regressions, ct);
        
        // Generate and potentially execute remediations
        foreach (var regression in regressions)
        {
            var suggestions = _advisor.Suggest(regression);
            
            foreach (var suggestion in suggestions.Where(s => 
                _executor.CanAutoExecute(suggestion.ToEntity(regression))))
            {
                var result = await _executor.ExecuteAsync(
                    suggestion.ToEntity(regression),
                    "AutoRemediation",
                    ct);
                    
                if (!result.Success)
                {
                    _logger.LogWarning("Remediation failed: {Message}", result.Message);
                }
            }
        }
    }
}
```

## Testing

### Unit Testing Alert Channels

```csharp
[Fact]
public async Task TeamsChannel_SendsAdaptiveCard_OnRegression()
{
    var mockHttp = new Mock<HttpClient>();
    var options = Options.Create(new TeamsChannelOptions 
    { 
        Enabled = true, 
        WebhookUrl = "https://test.webhook" 
    });
    
    var channel = new TeamsAlertChannel(mockHttp.Object, options, _logger);
    
    await channel.SendRegressionAlertsAsync(new[] { _testRegression });
    
    // Verify HTTP call was made with correct payload
}
```

### Integration Testing

```csharp
[Fact]
public async Task EmailChannel_TestConnection_ReturnsTrue_WhenSmtpValid()
{
    var channel = _serviceProvider.GetRequiredService<EmailAlertChannel>();
    
    var result = await channel.TestConnectionAsync();
    
    Assert.True(result);
}
```

## Files Created

| File | Purpose |
|------|---------|
| `Application/Interfaces/IAlertChannel.cs` | Strategy interface + DTOs |
| `Application/Interfaces/AlertingOptions.cs` | Configuration options |
| `Application/Interfaces/IRemediationExecutor.cs` | Execution interface + result types |
| `Application/Orchestrators/AlertOrchestrator.cs` | Channel coordination facade |
| `Domain/Services/IRemediationAdvisor.cs` | Domain interface + suggestion DTO |
| `Domain/Services/RemediationAdvisor.cs` | Suggestion generation logic |
| `Infrastructure/Messaging/LogOnlyAlertChannel.cs` | Logging-only channel |
| `Infrastructure/Messaging/TeamsAlertChannel.cs` | Teams webhook integration |
| `Infrastructure/Messaging/SlackAlertChannel.cs` | Slack webhook integration |
| `Infrastructure/Messaging/EmailAlertChannel.cs` | SMTP email integration |
| `Infrastructure/Data/SqlRemediationExecutor.cs` | SQL execution with safety checks |
| `Infrastructure/ServiceCollectionExtensions.cs` | DI registration (updated) |

## Next Steps

- **Doc 09**: Background Service Hosting and Scheduling
- **Doc 10**: Configuration and Secrets Management
- **Doc 11**: Logging, Telemetry, and Auditing
