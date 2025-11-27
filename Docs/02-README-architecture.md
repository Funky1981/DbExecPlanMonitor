# 02-README: High-Level Architecture

## 📚 Summary

This document establishes **how** we structure the solution using Clean Architecture and SOLID principles. It defines the layers, their responsibilities, and the dependency flow.

---

## 🏗️ Architectural Style: Clean Architecture

We use a **Clean Architecture** / **Ports & Adapters** approach:

```
┌─────────────────────────────────────────────────────────────┐
│                      Worker (Host)                          │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                   Infrastructure                        │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │                  Application                      │  │ │
│  │  │  ┌────────────────────────────────────────────┐  │  │ │
│  │  │  │                 Domain                     │  │  │ │
│  │  │  │         (No external dependencies)         │  │  │ │
│  │  │  └────────────────────────────────────────────┘  │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 📦 Layer Responsibilities

### 1. Domain Layer (`DbExecPlanMonitor.Domain`)
**The heart of the application - pure business logic**

| What It Contains | What It Does NOT Contain |
|------------------|--------------------------|
| Entity classes | Database/ADO.NET references |
| Value objects | Logging frameworks |
| Enums | SQL types |
| Domain interfaces | Configuration concerns |
| Domain services | External service references |

**Key Principle**: Domain depends on **nothing**. It's pure C# with no external dependencies.

### 2. Application Layer (`DbExecPlanMonitor.Application`)
**Orchestrates domain logic and defines ports**

| Contains | Purpose |
|----------|---------|
| Use Cases | Implement specific business operations |
| Orchestrators | Coordinate multiple domain services |
| DTOs | Data transfer between layers |
| Port Interfaces | Contracts for infrastructure to implement |

**Key Principle**: Depends only on Domain. Defines interfaces (ports) that Infrastructure implements.

### 3. Infrastructure Layer (`DbExecPlanMonitor.Infrastructure`)
**Implements adapters for external concerns**

| Folder | Purpose |
|--------|---------|
| `Data/SqlServer/` | ADO.NET repositories, plan collectors |
| `Messaging/` | Email/Teams/Slack notifiers |
| `Persistence/` | Internal storage (metrics DB, file store) |
| `Logging/` | Logging adapters |

**Key Principle**: Implements port interfaces from Application layer. Has all the "dirty" external dependencies.

### 4. Worker Layer (`DbExecPlanMonitor.Worker`)
**The composition root and entry point**

| Responsibility | Description |
|----------------|-------------|
| DI Configuration | Wires all layers together |
| Scheduling | Runs collectors on intervals |
| Configuration | Loads appsettings.json |
| Health Checks | Operational monitoring |

---

## 🔄 Logical Components

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Plan Collector │────▶│   Plan Store    │────▶│ Analysis Engine │
│  (DMVs/QS)      │     │  (History DB)   │     │ (Regression/    │
│                 │     │                 │     │  Hotspot)       │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
                                                         ▼
                                               ┌─────────────────┐
                                               │ Alerting &      │
                                               │ Remediation     │
                                               └─────────────────┘
```

| Component | Responsibility |
|-----------|---------------|
| **Plan Collector** | Talks to SQL Server DMVs and/or Query Store. Captures execution plans and metrics |
| **Plan Store** | Persists historical metrics, baselines, and regression events |
| **Analysis Engine** | Compares current vs baseline. Detects regressions and hotspots |
| **Alerting & Remediation** | Sends notifications. Exposes remediation suggestions |

---

## 📂 Project Structure

```text
src/
  DbExecPlanMonitor.sln

  DbExecPlanMonitor.Domain/
  ├── Entities/           # Business entities
  ├── ValueObjects/       # Immutable value types
  ├── Enums/              # Domain enumerations
  ├── Interfaces/         # Domain service contracts
  └── Services/           # Pure domain logic

  DbExecPlanMonitor.Application/
  ├── UseCases/           # Business operations
  ├── Orchestrators/      # Multi-service coordination
  ├── DTOs/               # Data transfer objects
  └── Interfaces/         # Port definitions (for infra)

  DbExecPlanMonitor.Infrastructure/
  ├── Data/
  │   └── SqlServer/      # ADO.NET implementations
  ├── Messaging/          # Alert channels
  ├── Persistence/        # Internal storage
  └── Logging/            # Logging adapters

  DbExecPlanMonitor.Worker/
  ├── Program.cs          # Entry point, DI setup
  ├── MonitoringWorker.cs # Background service
  └── appsettings.json    # Configuration

tests/
  DbExecPlanMonitor.Domain.Tests/
  DbExecPlanMonitor.Application.Tests/
  DbExecPlanMonitor.Infrastructure.Tests/
```

---

## ➡️ Dependency Flow

```
Domain ← Application ← Infrastructure ← Worker
  ↑          ↑              ↑             ↑
  │          │              │             │
  │          │              │             └── References: All projects
  │          │              │
  │          │              └── References: Application, Domain
  │          │
  │          └── References: Domain only
  │
  └── References: Nothing (pure C#)
```

**The Dependency Rule**: Dependencies point **inward**. Outer layers know about inner layers, never the reverse.

---

## 🔧 Why ADO.NET (Not Entity Framework)?

| Reason | Explanation |
|--------|-------------|
| **Environment Requirements** | Many enterprises prohibit ORMs |
| **Connection Control** | Precise control over connection lifetimes |
| **Command Control** | Fine-grained control over timeouts and hints |
| **DBA Alignment** | Works naturally with stored procedures and DMVs |
| **No Abstraction Leakage** | We're already working at the SQL level with DMVs |

---

## 📁 Files Implemented

At this stage, we have the basic project structure:

| Project | Key Files | Status |
|---------|-----------|--------|
| `DbExecPlanMonitor.Worker` | `Program.cs`, `MonitoringWorker.cs` | ✅ Created |
| `DbExecPlanMonitor.Worker` | `appsettings.json` | ✅ Created |
| All Projects | `.csproj` files | ✅ Created |

### Worker/Program.cs
```csharp
// Entry point with Serilog configuration
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = Host.CreateApplicationBuilder(args);

// Configure structured logging
builder.Services.AddSerilog((services, loggerConfig) => { ... });

// Register services
builder.Services.AddSqlServerMonitoring(builder.Configuration);
builder.Services.AddHostedService<MonitoringWorker>();

// Windows Service support
builder.Services.AddWindowsService(options => 
{
    options.ServiceName = "DbExecPlanMonitor";
});
```

### Worker/MonitoringWorker.cs
```csharp
public class MonitoringWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Starting monitoring cycle");
                
                // TODO: Call orchestrator for:
                // 1. Collect execution plans
                // 2. Analyze for regressions
                // 3. Send alerts
                // 4. Store metrics
                
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during monitoring cycle");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Backoff
            }
        }
    }
}
```

---

## ➡️ Next Steps

With architecture established, proceed to:
- **[03-domain-model-and-ubiquitous-language.md](03-domain-model-and-ubiquitous-language.md)** - Define business concepts and entities
