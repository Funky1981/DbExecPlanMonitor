# 13 – Testing Strategy and Demo Environment

This file describes how we test the system and how you can **learn safely** without touching real production.

## Testing Pyramid

1. **Unit Tests**
   - Domain and Application layers:
     - `IRegressionDetector` logic
     - `IHotspotDetector` logic
     - Baseline computation
   - No DB required.

2. **Integration Tests**
   - Infra layer:
     - ADO.NET repositories
     - Plan statistics provider
   - Requires a **test SQL Server** instance with:
     - Sample workload
     - DMVs / Query Store populated.

3. **End-to-End Tests**
   - Full service running against:
     - Local or test DB.
   - Validate:
     - Plans collected
     - Regressions detected
     - Alerts logged/sent (can use fake alert channel).

## Demo Environment for Learning

Create a **local playground**:

- SQL Server Developer Edition on your machine or container.
- A sample database with some:
  - Large tables
  - Badly indexed queries

Run a **load script** or small app that:

- Executes a variety of queries repeatedly.
- Introduces regressions intentionally (e.g., drop an index).

Use this environment to:

- See DMVs / Query Store in action.
- Validate that the monitoring service:
  - Sees regressions.
  - Raises expected alerts.

## Testing Guidelines

- Use **dependency injection** everywhere to:
  - Swap real implementations with fakes in tests.
- Use **in-memory** implementations of:
  - Alert channels (capture alerts in memory).
  - Repositories (for unit tests).

- For integration tests:
  - Use a dedicated test schema / DB.
  - Ensure tests clean up after themselves.

Next: see `14-deployment-operations-and-rollout-plan.md` for how to ship this into real environments.
