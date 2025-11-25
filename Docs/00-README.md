# DB Execution Plan Monitoring Service – Specification Pack

This folder contains a step-by-step specification for building a **C# execution plan monitoring service** using:

- .NET Worker Service / Windows Service
- **ADO.NET** (no ORM)
- Clean Architecture & SOLID principles
- SQL Server (primary target) with extension points for other RDBMS engines

Each file is intended to be consumed **in order** by a developer and/or coding AI so the system can be built while you learn.

## File Index (Build Order)

1. `01-problem-overview-and-goals.md` – What the service does and why.
2. `02-high-level-architecture.md` – Overall system design & boundaries.
3. `03-domain-model-and-ubiquitous-language.md` – Core concepts and models.
4. `04-database-integration-and-metadata-model.md` – How we talk to SQL Server.
5. `05-ado-net-data-access-layer.md` – Repositories and ADO.NET patterns.
6. `06-plan-collection-and-sampling-engine.md` – How plans are captured and stored.
7. `07-plan-analysis-and-regression-detection.md` – Detecting problems and regressions.
8. `08-alerting-and-remediation-workflows.md` – Notifications & auto-fix patterns.
9. `09-background-service-hosting-and-scheduling.md` – Worker service, scheduling, health.
10. `10-configuration-and-secrets-management.md` – Settings, connection strings, environments.
11. `11-logging-telemetry-and-auditing.md` – Observability requirements.
12. `12-security-and-safety-rails.md` – Guardrails to avoid breaking production.
13. `13-testing-strategy-and-demo-environment.md` – Unit, integration, and safe demo.
14. `14-deployment-operations-and-rollout-plan.md` – How to ship and operate this.
15. `15-learning-path-and-next-steps.md` – How to use this project as a learning tool.

## How to Use This Pack

1. **Start with 01**. Read it yourself, then paste it into your coding assistant and say:
   - “Create the folder structure and initial solution for this spec.”
2. Work **file by file**. After each spec file, ask the AI to generate:
   - Project structure changes
   - Interface & class stubs
   - Basic implementation
3. You focus on:
   - Understanding the *why* behind each design choice.
   - Writing or editing at least some part of the code manually.
4. Keep all code under source control (Git) so you can:
   - Review diffs
   - Roll back bad ideas
   - Learn by comparing your changes vs AI’s changes

## Tech Stack

- **Language:** C# (current LTS .NET)
- **Hosting:** Worker Service / Windows Service; container-friendly
- **DB:** SQL Server (initial), extensible provider model for others
- **Data Access:** ADO.NET (`SqlConnection`, `SqlCommand`, `SqlDataReader`)
- **Config:** `appsettings.json` + environment-specific overrides
- **Logging:** `ILogger<T>` with provider of your choice (Serilog, etc.)

Move on to `01-problem-overview-and-goals.md` next.
