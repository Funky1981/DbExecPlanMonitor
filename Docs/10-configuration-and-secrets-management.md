# 10 – Configuration and Secrets Management

This file defines how configuration and secrets are handled.

## Configuration Sources

Use .NET configuration system:

- `appsettings.json` (base)
- `appsettings.{Environment}.json` (overrides)
- Environment variables
- Key Vault / secrets store (for production)

### Example `appsettings.json` Shape (Conceptual)

```jsonc
{
  "Monitoring": {
    "Instances": [
      {
        "Id": "Prod-Sql-01",
        "DisplayName": "Production SQL 01",
        "ServerName": "prod-sql-01",
        "Databases": [
          {
            "Name": "MainDb",
            "Enabled": true,
            "Collection": {
              "TopN": 50,
              "WindowMinutes": 15,
              "MinExecutionCount": 10
            },
            "RegressionRules": {
              "DurationIncreaseThresholdPercent": 150,
              "CpuIncreaseThresholdPercent": 150,
              "MinimumExecutions": 20
            }
          }
        ]
      }
    ],
    "Jobs": {
      "PlanCollectionIntervalMinutes": 5,
      "AnalysisIntervalMinutes": 5,
      "BaselineRebuildHourUtc": 1
    }
  }
}
```

## Options Pattern

Bind to strongly-typed options classes:

- `MonitoringOptions`
- `InstanceConfig`
- `DatabaseConfig`
- `JobScheduleOptions`

Use the Options pattern:

```csharp
services.Configure<MonitoringOptions>(configuration.GetSection("Monitoring"));
```

Application layer should depend on these options interfaces, not raw config.

## Secrets

**Never** store passwords in `appsettings.json` in production.

Use:

- Environment variables, or
- Secret manager / Key Vault

Connection strings should be resolved from:

- Named secrets:
  - e.g., `ConnectionStrings__Prod-Sql-01-MainDb`

Infra layer `ISqlConnectionFactory` takes:

- `IOptions<MonitoringOptions>`
- `IConfiguration` or a secret provider to build connection strings.

## Feature Flags

For safety, use feature flags to control:

- Enabling new jobs.
- Enabling remediation execution.
- Enabling specific DB instances/environments.

These flags can live in config or in a feature flag service.

Next: see `11-logging-telemetry-and-auditing.md` for observability requirements.
