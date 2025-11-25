# 15 – Learning Path and Next Steps

This file is about **how you (and an AI helper) can learn from building this project**.

## Learning Objectives

By building this system, you will:

- Deepen understanding of:
  - SQL Server DMVs & Query Store.
  - Execution plans and performance metrics.
- Practice:
  - Clean Architecture in a real service.
  - ADO.NET with a proper abstraction layer.
  - Background worker patterns in .NET.
- Gain confidence in:
  - Operational thinking (alerts, safety rails).
  - Talking about DB performance in interviews/at work.

## Suggested Learning Flow

1. **Read 01–03**
   - Make sure the core problem and domain model make sense.
   - Be able to explain:
     - “What is a query fingerprint?”
     - “What is a regression event?”

2. **Set Up Demo DB**
   - Install SQL Server Developer Edition or container.
   - Create a sample DB and run workload scripts.

3. **Build the Skeleton**
   - Use 02, 04, 05, 09 to:
     - Create solution & projects.
     - Add DI and Worker Service host.
     - Define domain entities and interfaces.

4. **Implement Plan Collection**
   - Work through 04 & 06.
   - Implement:
     - `IPlanStatisticsProvider` for SQL Server using ADO.NET.
     - `PlanCollectionService` use case.

5. **Implement Analysis**
   - Use 07.
   - Implement:
     - Baseline logic.
     - Regression & hotspot detection.
   - Add tests for these.

6. **Add Alerting**
   - Use 08 & 11.
   - Start with:
     - Log-only alerts.
   - Later:
     - Email/Teams/Slack channels.

7. **Harden with Config & Safety**
   - Use 10 & 12.
   - Make sure:
     - Read-only vs remediation modes are enforced.
     - Config is environment-aware.

8. **Polish, Test, Deploy**
   - Use 13 & 14.
   - Deploy to a non-prod environment and observe.

## Using an AI Assistant

For each spec file:

1. Paste the file into your AI coding assistant.
2. Ask:
   - “Given this spec, generate the necessary interfaces and class stubs.”
3. Then you:
   - Review the code.
   - Adjust naming and design as needed.
   - Implement at least some parts manually.

The **goal isn’t** to have the AI do everything.
The goal is to **pair-program** with the AI so:
- You keep control over design decisions.
- You understand the system deeply.

---

At this point, you have a complete step-by-step specification.
Start with `00-README.md` and work through the files in order while you build.
