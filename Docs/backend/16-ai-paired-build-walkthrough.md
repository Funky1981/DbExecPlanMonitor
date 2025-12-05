# 16 – AI-Paired Build Walkthrough

This guide turns the specification pack into a **step-by-step build you can follow alongside an AI helper** (e.g., GitHub Copilot or another coding assistant). Each stage tells you *what you do* and *what to ask the AI*, so you stay in control while the AI accelerates the repetitive parts.

## Before You Start
- Open the repo in VS Code and sign in to your AI assistant (Copilot/Copilot Chat).
- Set chat scope to **Workspace** so the assistant reads files you mention.
- Keep the spec files open as you work; paste short excerpts when prompting to focus the AI.

## Stage 1 – Spin Up the Solution Shell
- **You do:** Create the solution and projects following `02-high-level-architecture.md` (API contracts, worker, shared kernel). Wire dependency injection and configuration loading.
- **Ask AI:** "Read 02-high-level-architecture.md and propose the initial solution structure with project names and references. Generate the `Program.cs` skeleton for the worker with hosted services and DI registrations."

## Stage 2 – Model the Domain and Contracts
- **You do:** Read `03-domain-model-and-ubiquitous-language.md` and draft the domain entities (plan fingerprint, sample, regression event) plus interfaces in the Application layer.
- **Ask AI:** "From 03-domain-model-and-ubiquitous-language.md, generate the C# record/class definitions and repository/service interfaces. Keep the code framework-agnostic and avoid infrastructure details."

## Stage 3 – Wire Database Access Safely
- **You do:** Follow `04-database-integration-and-metadata-model.md` and `05-ado-net-data-access-layer.md` to create Db schemas, metadata tables, and ADO.NET repository implementations.
- **Ask AI:** "Given the schema in 04 and the patterns in 05, write an ADO.NET repository for plan samples using parameterized queries, connection factory abstraction, and resilience (retry/timeout). Include unit-testable abstractions."

## Stage 4 – Plan Collection Engine
- **You do:** Use `06-plan-collection-and-sampling-engine.md` to implement collectors and schedulers that sample execution plans and statistics.
- **Ask AI:** "From 06, generate a hosted service that schedules plan collection, uses batching, and respects collection budgets. Include logging hooks and cancellation support."

## Stage 5 – Analysis & Regression Detection
- **You do:** Read `07-plan-analysis-and-regression-detection.md` to implement detection rules and scoring.
- **Ask AI:** "Implement the regression detection pipeline described in 07: ingest new samples, compare to baselines, compute risk scores, and persist regression events. Provide pure functions where possible for testability."

## Stage 6 – Alerting and Remediation
- **You do:** Implement notification channels and safe remediation from `08-alerting-and-remediation-workflows.md`.
- **Ask AI:** "Using 08, create an alert dispatcher with channel abstractions (email/webhook/Teams) and a remediation executor that enforces safety rails. Add unit tests for the decision logic."

## Stage 7 – Background Hosting and Scheduling
- **You do:** Configure worker hosting, health checks, and scheduling per `09-background-service-hosting-and-scheduling.md`.
- **Ask AI:** "Generate background worker configuration: health endpoints, graceful shutdown, schedule jitter, and observable metrics hooks as outlined in 09."

## Stage 8 – Configuration, Secrets, and Safety Rails
- **You do:** Apply `10-configuration-and-secrets-management.md` and `12-security-and-safety-rails.md` to lock down secrets, RBAC, and query guardrails.
- **Ask AI:** "Produce configuration bindings and validation for the settings in 10, and instrument safety checks from 12 (max row counts, allow lists, dry-run modes)."

## Stage 9 – Observability
- **You do:** Implement logging, tracing, metrics, and auditing using `11-logging-telemetry-and-auditing.md`.
- **Ask AI:** "Add structured logging and OpenTelemetry tracing per 11. Show me how to emit metrics around collection latency, detection latency, and alert throughput."

## Stage 10 – Testing and Demo Environment
- **You do:** Build the tests and demo per `13-testing-strategy-and-demo-environment.md`.
- **Ask AI:** "Generate unit tests for the regression detector and repository abstractions, plus integration test scaffolding for the demo database described in 13."

## Stage 11 – Deployment and Operations
- **You do:** Follow `14-deployment-operations-and-rollout-plan.md` for deployment, runbooks, and safety checks.
- **Ask AI:** "Draft CI pipeline steps and deployment manifests aligned to 14. Include smoke tests and rollback/feature flag steps."

## Stage 12 – Learning Loop
- After each stage, write a short retrospective: what you understood, what felt risky, and what you want the AI to clarify.
- Use Copilot Chat for explanations: "Explain why we use query fingerprints instead of plan handles," or "How does the sampling budget in 06 prevent overload?"

## Prompting Tips
- Keep prompts scoped: reference the specific spec file and the class or function you’re editing.
- Prefer **"Show me the code"** prompts when you need scaffolding, and **"Explain"** prompts when you’re unsure why a pattern exists.
- Accept suggestions selectively; edit or re-prompt until the code matches the spec and your understanding.

## Outcome
By following the numbered stages and pairing with an AI, you’ll build the system while **actively learning the architecture, domain, and operational discipline** instead of handing the work over to the assistant.
