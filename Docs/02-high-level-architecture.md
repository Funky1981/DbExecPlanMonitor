# 02 – High-Level Architecture

## Architectural Style

We use a **Clean Architecture** / **Ports & Adapters** approach:

- **Core (Domain)**
  - Pure C# classes
  - No dependencies on ADO.NET, logging frameworks, or SQL types
- **Application Layer**
  - Use cases / services that orchestrate domain logic
  - Interfaces for infrastructure dependencies (repositories, notifiers, schedulers)
- **Infrastructure Layer**
  - Concrete implementations:
    - ADO.NET repositories
    - SQL Server plan collectors
    - Email/Teams/Slack notifiers
    - Logging and configuration
- **Host Layer**
  - .NET Worker Service that wires everything together
  - Dependency Injection configuration
  - Entry point for scheduling recurring jobs

## Logical Components

- **Plan Collector**
  - Talks to system DMVs (Dynamic Management Views) and/or Query Store.
  - Captures execution plans and metrics into our internal models.

- **Plan Store**
  - Persists:
    - Historical metrics per query “fingerprint”
    - Baseline stats
    - Detected regression events

- **Analysis Engine**
  - Compares current metrics against:
    - Baselines
    - Thresholds (absolute and relative)
  - Detects:
    - Regressions
    - Hotspots
    - Plan changes

- **Alerting & Remediation**
  - Builds human-readable alerts and summaries.
  - Sends notifications via configured channels.
  - Exposes remediation suggestions; optionally triggers scripts.

- **Scheduling & Orchestration**
  - Runs collectors & analysis on configurable intervals.
  - Handles backoff and failure scenarios.

## Project Structure (Proposed)

```text
src/
  DbExecPlanMonitor.sln

  DbExecPlanMonitor.Domain/
    (entities, value objects, enums, interfaces, domain services)

  DbExecPlanMonitor.Application/
    (use cases, DTOs, orchestrators, interfaces for infra)

  DbExecPlanMonitor.Infrastructure/
    Data/
      SqlServer/
        (ADO.NET repositories, plan collectors)
    Messaging/
      (email/Teams/Slack notifiers)
    Persistence/
      (our own internal storage, e.g., SQL tables, file store)
    Logging/
      (logging adapters)

  DbExecPlanMonitor.Worker/
    (Worker Service host, DI composition root, configuration, health checks)

tests/
  DbExecPlanMonitor.Domain.Tests/
  DbExecPlanMonitor.Application.Tests/
  DbExecPlanMonitor.Infrastructure.Tests/
```

## Dependencies Flow

- Domain → (depends on nothing)
- Application → Domain
- Infrastructure → Application + Domain (implements ports)
- Worker → all (wires up dependencies)

## Why ADO.NET?

- Matches environments where ORMs are not allowed or are discouraged.
- Greater control on:
  - Connection lifetimes
  - Commands and timeouts
  - Locking hints and query options
- Easier to align with existing DBA practices (stored procedures, DMVs, etc.).

## Key Patterns

- **Repository Pattern** for data access.
- **Strategy Pattern** for:
  - Different DB engines (SQL Server vs others).
  - Different alerting channels.
- **Template Method Pattern** for recurring jobs:
  - Boilerplate around logging, timing, and error handling.
- **Options Pattern** for configuration.

Next: see `03-domain-model-and-ubiquitous-language.md` to lock down our core concepts.
