# Configuration and Secrets Management Implementation

This document describes the implementation of Doc 10 - Configuration and Secrets Management for DbExecPlanMonitor.

## Overview

The configuration system provides:
- Strongly-typed configuration via Options pattern
- Environment-specific overrides (Development, Production)
- Feature flags for runtime behavior control
- Secrets management with multiple provider support
- Validation at startup using DataAnnotations

## Configuration Hierarchy

Configuration is loaded from multiple sources in priority order:

1. `appsettings.json` - Base configuration
2. `appsettings.{Environment}.json` - Environment overrides
3. Environment variables - Runtime overrides
4. Azure Key Vault - Production secrets (when configured)
5. User Secrets - Development secrets

## Key Configuration Classes

### MonitoringOptions (`Application/Configuration/MonitoringOptions.cs`)

Root configuration for the monitoring system:

```csharp
public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";
    
    public int DefaultSamplingIntervalSeconds { get; set; } = 60;
    public int RetentionDays { get; set; } = 90;
    public bool LogSqlQueries { get; set; } = false;
    public int MaxConcurrentConnections { get; set; } = 10;
    public FeatureFlagOptions FeatureFlags { get; set; } = new();
}
```

### FeatureFlagOptions

Controls runtime behavior and safety rails:

```csharp
public sealed class FeatureFlagOptions
{
    public bool EnablePlanCollection { get; set; } = true;
    public bool EnableAnalysis { get; set; } = true;
    public bool EnableBaselineRebuild { get; set; } = true;
    public bool EnableDailySummary { get; set; } = true;
    public bool EnableAlerting { get; set; } = true;
    public bool EnableRemediation { get; set; } = false;  // Safety: off by default
    public bool AllowProductionRemediation { get; set; } = false;  // Extra safety
    public bool RemediationDryRun { get; set; } = true;  // Logs but doesn't execute
    public bool PreferQueryStore { get; set; } = true;
    public bool EnableHealthChecks { get; set; } = true;
    public bool EnableDetailedErrors { get; set; } = false;
}
```

### InstanceConfig (`Application/Configuration/InstanceConfig.cs`)

Per-instance configuration with validation:

```csharp
public sealed class InstanceConfig : IValidatableObject
{
    [Required]
    public required string Id { get; set; }
    
    [Required]
    public required string DisplayName { get; set; }
    
    [Required]
    public required string ServerName { get; set; }
    
    public List<DatabaseConfig> Databases { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    
    // Production safety check
    public bool IsProduction => Tags.Any(t => 
        t.Equals("production", StringComparison.OrdinalIgnoreCase));
}
```

### JobScheduleOptions (`Application/Configuration/JobScheduleOptions.cs`)

Job scheduling configuration:

```csharp
public sealed class JobScheduleOptions
{
    public TimeSpan PlanCollectionInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan BaselineRebuildTimeOfDay { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan DailySummaryTimeOfDay { get; set; } = TimeSpan.FromHours(8);
}
```

## Secrets Management

### ISecretResolver (`Application/Configuration/SecretResolver.cs`)

Interface for resolving secrets from various sources:

```csharp
public interface ISecretResolver
{
    string? GetSecret(string key);
    string? GetConnectionString(string instanceId);
    bool HasSecret(string key);
}
```

### Connection String Resolution

Connection strings are resolved in order:
1. `ConnectionStrings:{InstanceId}` in configuration
2. Environment variable `ConnectionStrings__{InstanceId}`

**Never store production passwords in appsettings.json!**

### Environment Variables

Use double underscore (`__`) for nested configuration:

```bash
# Windows
set Monitoring__FeatureFlags__EnableRemediation=true

# Linux/Docker
export MONITORING__FEATUREFLAGS__ENABLEREMEDIATION=true
```

## Feature Flags

### IFeatureFlagProvider

Runtime access to feature flags:

```csharp
public interface IFeatureFlagProvider
{
    FeatureFlagOptions Flags { get; }
    bool IsEnabled(string featureName);
    bool IsRemediationAllowed(InstanceConfig instance);
}
```

### Usage in Services

```csharp
public class SomeService
{
    private readonly IFeatureFlagProvider _features;
    
    public async Task DoWork(InstanceConfig instance)
    {
        if (!_features.IsEnabled("remediation"))
            return;
            
        if (!_features.IsRemediationAllowed(instance))
            return;
            
        // Safe to proceed
    }
}
```

### Safety Rails for Remediation

1. `EnableRemediation` - Global kill switch (default: false)
2. `AllowProductionRemediation` - Production environments (default: false)
3. `RemediationDryRun` - Log only, don't execute (default: true)
4. Instance tags - "production" tag triggers extra checks

## Validation

### DataAnnotations Validation

Configuration classes support standard DataAnnotations:

```csharp
[Required]
[StringLength(100, MinimumLength = 1)]
public required string Id { get; set; }

[Range(1, 65535)]
public int Port { get; set; } = 1433;
```

### IValidatableObject

For complex validation logic:

```csharp
public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
{
    if (!UseIntegratedSecurity && string.IsNullOrEmpty(ConnectionStringName))
    {
        yield return new ValidationResult(
            "ConnectionStringName required when not using Integrated Security",
            new[] { nameof(ConnectionStringName) });
    }
}
```

### Startup Validation

Options are validated at startup via `.ValidateOnStart()`:

```csharp
services.AddOptions<MonitoringOptions>()
    .Bind(configuration.GetSection(MonitoringOptions.SectionName))
    .ValidateWithDataAnnotations()
    .ValidateOnStart();  // Fails fast if invalid
```

## Environment-Specific Configuration

### Development (`appsettings.Development.json`)

```json
{
  "Monitoring": {
    "LogSqlQueries": true,
    "FeatureFlags": {
      "EnableDetailedErrors": true
    }
  },
  "Scheduling": {
    "CollectionInterval": "00:01:00"  // Faster for testing
  }
}
```

### Production (`appsettings.Production.json`)

```json
{
  "Monitoring": {
    "LogSqlQueries": false,
    "FeatureFlags": {
      "EnableDetailedErrors": false,
      "EnableRemediation": false,
      "AllowProductionRemediation": false
    }
  },
  "Scheduling": {
    "CollectionInterval": "00:05:00",
    "FailureBackoff": "00:01:00"
  }
}
```

## Registration

In `Program.cs`:

```csharp
// Register configuration services with validation
builder.Services.AddMonitoringConfiguration(builder.Configuration);
builder.Services.AddInstanceConfiguration(builder.Configuration);
```

## Files Created

| File | Purpose |
|------|---------|
| `Application/Configuration/MonitoringOptions.cs` | Root monitoring options with feature flags |
| `Application/Configuration/InstanceConfig.cs` | Per-instance configuration |
| `Application/Configuration/JobScheduleOptions.cs` | Job scheduling configuration |
| `Application/Configuration/SecretResolver.cs` | Secrets resolution interface and implementation |
| `Application/Configuration/OptionsValidation.cs` | DataAnnotations validation support |
| `Application/Configuration/ConfigurationServiceExtensions.cs` | Feature flag provider |
| `Infrastructure/Configuration/ConfigurationServiceExtensions.cs` | DI registration |
| `Worker/appsettings.Development.json` | Development overrides |
| `Worker/appsettings.Production.json` | Production configuration |

## Best Practices

1. **Never commit secrets** - Use environment variables or Key Vault
2. **Validate early** - Use `.ValidateOnStart()` to fail fast
3. **Use feature flags** - Control behavior without deployments
4. **Tag production instances** - Enable additional safety checks
5. **Default to safe** - Remediation disabled by default
6. **Use IOptionsMonitor<T>** - Get live configuration updates
