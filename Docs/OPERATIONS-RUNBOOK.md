# DbExecPlanMonitor Operations Runbook

This document provides operational guidance for deploying, configuring, and maintaining the Database Execution Plan Monitor service.

## Table of Contents

1. [Deployment Options](#deployment-options)
2. [Configuration Reference](#configuration-reference)
3. [Adding/Removing Monitored Databases](#addingremoving-monitored-databases)
4. [Adjusting Detection Thresholds](#adjusting-detection-thresholds)
5. [Maintenance Mode](#maintenance-mode)
6. [Troubleshooting](#troubleshooting)
7. [Rollout Strategy](#rollout-strategy)

---

## Deployment Options

### Option 1: Windows Service

```powershell
# Install
.\scripts\Install-WindowsService.ps1 -Environment Production

# Manage
Start-Service DbExecPlanMonitor
Stop-Service DbExecPlanMonitor
Get-Service DbExecPlanMonitor

# Uninstall
.\scripts\Uninstall-WindowsService.ps1 -RemoveFiles
```

### Option 2: Linux systemd

```bash
# Install
sudo ./scripts/install-linux-service.sh Production

# Manage
sudo systemctl start dbexecplanmonitor
sudo systemctl stop dbexecplanmonitor
sudo systemctl status dbexecplanmonitor

# View logs
sudo journalctl -u dbexecplanmonitor -f
```

### Option 3: Docker

```bash
# Development with SQL Server
docker-compose up -d

# Production
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d

# View logs
docker logs -f dbmonitor-worker
```

### Option 4: Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dbexecplanmonitor
spec:
  replicas: 1
  selector:
    matchLabels:
      app: dbexecplanmonitor
  template:
    metadata:
      labels:
        app: dbexecplanmonitor
    spec:
      containers:
      - name: monitor
        image: ghcr.io/funky1981/dbexecplanmonitor:latest
        env:
        - name: DOTNET_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__MonitoringDatabase
          valueFrom:
            secretKeyRef:
              name: db-secrets
              key: connection-string
```

---

## Configuration Reference

### Environment-Specific Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Base configuration (all environments) |
| `appsettings.Development.json` | Development overrides |
| `appsettings.Staging.json` | Staging overrides |
| `appsettings.Production.json` | Production overrides |

### Key Configuration Sections

#### Connection Strings
```json
{
  "ConnectionStrings": {
    "MonitoringDatabase": "Server=...;Database=...;..."
  }
}
```

#### Security Settings
```json
{
  "Monitoring": {
    "Security": {
      "Mode": "ReadOnly",           // ReadOnly | SuggestRemediation | AutoApplyLowRisk
      "EnableRemediation": false,   // Global kill switch
      "DryRunMode": true,           // Simulate without executing
      "Environment": "Production",
      "MaxRemediationsPerHour": 5,
      "ExcludedDatabases": ["CriticalDB"]
    }
  }
}
```

#### Monitored Instances
```json
{
  "Monitoring": {
    "Instances": [
      {
        "Name": "ProductionSQL01",
        "ConnectionString": "Server=ProductionSQL01;...",
        "Enabled": true,
        "Databases": [
          { "Name": "AppDatabase", "Enabled": true },
          { "Name": "ReportingDB", "Enabled": true }
        ]
      }
    ]
  }
}
```

---

## Adding/Removing Monitored Databases

### Add a New Instance

1. Add to `appsettings.{Environment}.json`:
```json
{
  "Monitoring": {
    "Instances": [
      {
        "Name": "NewSQLServer",
        "ConnectionString": "Server=NewSQLServer;Integrated Security=true;TrustServerCertificate=true;",
        "Enabled": true,
        "Databases": [
          { "Name": "NewDatabase", "Enabled": true }
        ]
      }
    ]
  }
}
```

2. Restart the service or wait for configuration reload.

### Disable a Database Temporarily

```json
{
  "Databases": [
    { "Name": "MaintenanceDB", "Enabled": false }
  ]
}
```

### Remove an Instance

Remove the entry from configuration and restart.

---

## Adjusting Detection Thresholds

### Regression Detection

```json
{
  "Monitoring": {
    "RegressionDetection": {
      "DurationIncreaseThresholdPercent": 50,    // 50% = 1.5x slowdown
      "CpuIncreaseThresholdPercent": 50,
      "LogicalReadsIncreaseThresholdPercent": 100,
      "MinimumExecutions": 5,                     // Ignore low-volume queries
      "MinimumBaselineSamples": 10               // Wait for stable baseline
    }
  }
}
```

### Hotspot Detection

```json
{
  "Monitoring": {
    "HotspotDetection": {
      "TopN": 20,
      "MinTotalCpuMs": 1000,
      "MinTotalDurationMs": 5000,
      "MinExecutionCount": 10,
      "MinAvgDurationMs": 100
    }
  }
}
```

---

## Maintenance Mode

### Temporary Disable (Keep Service Running)

```json
{
  "Monitoring": {
    "Security": {
      "Mode": "ReadOnly",
      "EnableRemediation": false
    }
  }
}
```

### Stop Collection for Specific Database

```json
{
  "Databases": [
    { "Name": "MaintenanceTarget", "Enabled": false }
  ]
}
```

### Full Stop

```powershell
# Windows
Stop-Service DbExecPlanMonitor

# Linux
sudo systemctl stop dbexecplanmonitor

# Docker
docker-compose stop monitor
```

---

## Troubleshooting

### Service Cannot Connect to Database

**Symptoms:**
- Errors in logs: "Connection failed", "Login failed"
- No data being collected

**Resolution:**
1. Verify connection string in configuration
2. Test connectivity:
   ```powershell
   Test-NetConnection -ComputerName SQLServer -Port 1433
   ```
3. Verify SQL login has required permissions:
   ```sql
   -- Minimum permissions needed
   GRANT VIEW SERVER STATE TO [MonitoringLogin];
   GRANT VIEW DATABASE STATE TO [MonitoringLogin];
   ```

### Alert Spike (Possible False Positives)

**Symptoms:**
- Sudden increase in regression alerts
- Many queries flagged simultaneously

**Possible Causes:**
- Database maintenance just completed (stats updated, indexes rebuilt)
- Server restart (cold cache)
- Workload pattern change

**Resolution:**
1. Check if maintenance ran recently
2. Temporarily increase thresholds:
   ```json
   {
     "RegressionDetection": {
       "DurationIncreaseThresholdPercent": 100
     }
   }
   ```
3. Wait for baselines to stabilize (24-48 hours)
4. Consider excluding noisy queries

### Service Causing Load Issues

**Symptoms:**
- Increased CPU/IO on monitored server during collection
- DMV queries appearing in workload

**Resolution:**
1. Increase collection interval:
   ```json
   {
     "Scheduling": {
       "CollectionIntervalMinutes": 10
     }
   }
   ```
2. Reduce scope (fewer databases/queries)
3. Schedule collection during off-peak hours:
   ```json
   {
     "Scheduling": {
       "ActiveHoursStart": 8,
       "ActiveHoursEnd": 18
     }
   }
   ```

### View Logs

```powershell
# Windows - File logs
Get-Content C:\Services\DbExecPlanMonitor\logs\*.log -Tail 100

# Windows - Event Viewer
Get-EventLog -LogName Application -Source DbExecPlanMonitor -Newest 50

# Linux
sudo journalctl -u dbexecplanmonitor -n 100

# Docker
docker logs dbmonitor-worker --tail 100
```

---

## Rollout Strategy

### Phase 1: Development/Test (Week 1-2)

- [ ] Deploy to dev environment
- [ ] Connect to test database with sample workload
- [ ] Verify collection is working
- [ ] Confirm alerts are logged (not sent)
- [ ] Adjust thresholds for test workload

### Phase 2: Staging (Week 3-4)

- [ ] Deploy to staging with production-like data
- [ ] Enable alert notifications (test channel)
- [ ] Tune thresholds to reduce noise
- [ ] Verify no performance impact on monitored DB
- [ ] Document baseline alert volume

### Phase 3: Production Read-Only (Week 5-8)

- [ ] Deploy to production
- [ ] Set `Mode = ReadOnly`
- [ ] Set `EnableRemediation = false`
- [ ] Monitor for 2-4 weeks
- [ ] Review and acknowledge alerts
- [ ] Build confidence in detection accuracy

### Phase 4: Suggest Remediation (Week 9+)

- [ ] Set `Mode = SuggestRemediation`
- [ ] Review suggested remediations
- [ ] Manually apply approved suggestions
- [ ] Track success rate

### Phase 5: Semi-Auto (Optional)

- [ ] If organization approves
- [ ] Set `Mode = AutoApplyLowRisk`
- [ ] Enable only low-risk remediations
- [ ] Require approval for medium/high risk
- [ ] Monitor closely via audit logs

---

## Health Checks

### Service Status

```powershell
# Windows
Get-Service DbExecPlanMonitor

# Linux
systemctl status dbexecplanmonitor
```

### Metrics to Monitor

| Metric | Healthy Range | Action if Out of Range |
|--------|--------------|------------------------|
| Collection Success Rate | > 99% | Check connectivity |
| Alert Volume | < 10/hour | Tune thresholds |
| Service Memory | < 500MB | Check for leaks |
| Collection Duration | < 30s | Reduce scope |

---

## Contact and Escalation

| Issue | Contact |
|-------|---------|
| Configuration changes | DBA Team |
| Alert investigation | DBA Team |
| Service issues | Platform Team |
| Security concerns | Security Team |
