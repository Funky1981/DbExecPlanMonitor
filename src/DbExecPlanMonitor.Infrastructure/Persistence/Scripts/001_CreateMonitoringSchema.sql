-- ============================================================================
-- Database: DbExecPlanMonitor
-- Script: 001_CreateMonitoringSchema.sql
-- Purpose: Creates the monitoring schema and tables for storing collected
--          metrics, baselines, and regression events.
-- 
-- This script is idempotent - it can be run multiple times safely.
-- ============================================================================

-- Create schema if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'monitoring')
BEGIN
    EXEC('CREATE SCHEMA monitoring');
END
GO

-- ============================================================================
-- Table: monitoring.QueryFingerprint
-- Purpose: Stores normalized query identities for grouping executions
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'QueryFingerprint' AND schema_id = SCHEMA_ID('monitoring'))
BEGIN
    CREATE TABLE monitoring.QueryFingerprint
    (
        Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID(),
        QueryHash       VARBINARY(32)       NOT NULL,
        QueryTextSample NVARCHAR(MAX)       NOT NULL,
        DatabaseName    NVARCHAR(128)       NOT NULL,
        FirstSeenUtc    DATETIME2(3)        NOT NULL DEFAULT SYSUTCDATETIME(),
        LastSeenUtc     DATETIME2(3)        NOT NULL DEFAULT SYSUTCDATETIME(),
        
        CONSTRAINT PK_QueryFingerprint PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_QueryFingerprint_Hash UNIQUE (QueryHash)
    );

    -- Index for lookups by database
    CREATE NONCLUSTERED INDEX IX_QueryFingerprint_Database 
        ON monitoring.QueryFingerprint (DatabaseName) 
        INCLUDE (QueryHash, LastSeenUtc);

    -- Index for finding active fingerprints
    CREATE NONCLUSTERED INDEX IX_QueryFingerprint_LastSeen 
        ON monitoring.QueryFingerprint (LastSeenUtc DESC) 
        INCLUDE (DatabaseName, QueryHash);
END
GO

-- ============================================================================
-- Table: monitoring.PlanMetricSample
-- Purpose: Stores point-in-time performance metrics for queries
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlanMetricSample' AND schema_id = SCHEMA_ID('monitoring'))
BEGIN
    CREATE TABLE monitoring.PlanMetricSample
    (
        Id                  UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID(),
        FingerprintId       UNIQUEIDENTIFIER    NOT NULL,
        InstanceName        NVARCHAR(256)       NOT NULL,
        DatabaseName        NVARCHAR(128)       NOT NULL,
        SampledAtUtc        DATETIME2(3)        NOT NULL,
        
        -- Plan identification
        PlanHash            VARBINARY(32)       NULL,
        QueryStoreQueryId   BIGINT              NULL,
        QueryStorePlanId    BIGINT              NULL,
        
        -- Execution counts
        ExecutionCount      BIGINT              NOT NULL,
        ExecutionCountDelta BIGINT              NOT NULL DEFAULT 0,
        
        -- CPU metrics (microseconds)
        TotalCpuTimeUs      BIGINT              NOT NULL,
        AvgCpuTimeUs        BIGINT              NOT NULL,
        MinCpuTimeUs        BIGINT              NULL,
        MaxCpuTimeUs        BIGINT              NULL,
        
        -- Duration metrics (microseconds)
        TotalDurationUs     BIGINT              NOT NULL,
        AvgDurationUs       BIGINT              NOT NULL,
        MinDurationUs       BIGINT              NULL,
        MaxDurationUs       BIGINT              NULL,
        
        -- I/O metrics
        TotalLogicalReads   BIGINT              NOT NULL,
        AvgLogicalReads     BIGINT              NOT NULL,
        TotalLogicalWrites  BIGINT              NOT NULL DEFAULT 0,
        TotalPhysicalReads  BIGINT              NOT NULL DEFAULT 0,
        
        -- Memory metrics
        AvgMemoryGrantKb    BIGINT              NULL,
        MaxMemoryGrantKb    BIGINT              NULL,
        AvgSpillsKb         BIGINT              NULL,
        
        CONSTRAINT PK_PlanMetricSample PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_PlanMetricSample_Fingerprint 
            FOREIGN KEY (FingerprintId) REFERENCES monitoring.QueryFingerprint(Id)
    );

    -- Partition-friendly index for time-based queries
    CREATE NONCLUSTERED INDEX IX_PlanMetricSample_SampledAt 
        ON monitoring.PlanMetricSample (SampledAtUtc DESC) 
        INCLUDE (FingerprintId, InstanceName, AvgDurationUs, AvgCpuTimeUs);

    -- Index for fingerprint-based lookups
    CREATE NONCLUSTERED INDEX IX_PlanMetricSample_Fingerprint 
        ON monitoring.PlanMetricSample (FingerprintId, SampledAtUtc DESC);

    -- Index for instance-based queries
    CREATE NONCLUSTERED INDEX IX_PlanMetricSample_Instance 
        ON monitoring.PlanMetricSample (InstanceName, SampledAtUtc DESC) 
        INCLUDE (DatabaseName, FingerprintId);
END
GO

-- ============================================================================
-- Table: monitoring.Baseline
-- Purpose: Stores calculated performance baselines for regression detection
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Baseline' AND schema_id = SCHEMA_ID('monitoring'))
BEGIN
    CREATE TABLE monitoring.Baseline
    (
        Id                  UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID(),
        FingerprintId       UNIQUEIDENTIFIER    NOT NULL,
        DatabaseName        NVARCHAR(128)       NOT NULL,
        
        -- Baseline period
        BaselineStartUtc    DATETIME2(3)        NOT NULL,
        BaselineEndUtc      DATETIME2(3)        NOT NULL,
        CreatedAtUtc        DATETIME2(3)        NOT NULL DEFAULT SYSUTCDATETIME(),
        
        -- Sample info
        SampleCount         INT                 NOT NULL,
        TotalExecutions     BIGINT              NOT NULL,
        
        -- Duration baseline (microseconds)
        MedianDurationUs    BIGINT              NOT NULL,
        P95DurationUs       BIGINT              NOT NULL,
        P99DurationUs       BIGINT              NOT NULL,
        AvgDurationUs       BIGINT              NOT NULL,
        DurationStdDev      FLOAT               NULL,
        
        -- CPU baseline (microseconds)
        MedianCpuTimeUs     BIGINT              NOT NULL,
        P95CpuTimeUs        BIGINT              NOT NULL,
        AvgCpuTimeUs        BIGINT              NOT NULL,
        
        -- I/O baseline
        MedianLogicalReads  BIGINT              NOT NULL,
        P95LogicalReads     BIGINT              NOT NULL,
        AvgLogicalReads     BIGINT              NOT NULL,
        
        -- Plan info
        ExpectedPlanHash    VARBINARY(32)       NULL,
        
        -- Metadata
        Notes               NVARCHAR(500)       NULL,
        IsActive            BIT                 NOT NULL DEFAULT 1,
        
        CONSTRAINT PK_Baseline PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Baseline_Fingerprint 
            FOREIGN KEY (FingerprintId) REFERENCES monitoring.QueryFingerprint(Id)
    );

    -- Only one active baseline per fingerprint (filtered unique index)
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Baseline_ActiveFingerprint 
        ON monitoring.Baseline (FingerprintId) 
        WHERE IsActive = 1;

    -- Index for finding baselines by database
    CREATE NONCLUSTERED INDEX IX_Baseline_Database 
        ON monitoring.Baseline (DatabaseName) 
        WHERE IsActive = 1;

    -- Index for finding stale baselines
    CREATE NONCLUSTERED INDEX IX_Baseline_CreatedAt 
        ON monitoring.Baseline (CreatedAtUtc) 
        WHERE IsActive = 1;
END
GO

-- ============================================================================
-- Table: monitoring.RegressionEvent
-- Purpose: Stores detected performance regressions for alerting and tracking
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RegressionEvent' AND schema_id = SCHEMA_ID('monitoring'))
BEGIN
    CREATE TABLE monitoring.RegressionEvent
    (
        Id                  UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID(),
        FingerprintId       UNIQUEIDENTIFIER    NOT NULL,
        InstanceName        NVARCHAR(256)       NOT NULL,
        DatabaseName        NVARCHAR(128)       NOT NULL,
        DetectedAtUtc       DATETIME2(3)        NOT NULL DEFAULT SYSUTCDATETIME(),
        
        -- Regression details
        RegressionType      TINYINT             NOT NULL,  -- 0=Metric, 1=PlanChange, 2=Both
        MetricName          NVARCHAR(50)        NOT NULL,
        BaselineValue       BIGINT              NOT NULL,
        CurrentValue        BIGINT              NOT NULL,
        ChangePercent       FLOAT               NOT NULL,
        ThresholdPercent    FLOAT               NOT NULL,
        Severity            TINYINT             NOT NULL,  -- 0=Low, 1=Medium, 2=High, 3=Critical
        
        -- Query context
        QueryTextSample     NVARCHAR(MAX)       NULL,
        BaselinePlanHash    VARBINARY(32)       NULL,
        CurrentPlanHash     VARBINARY(32)       NULL,
        IsPlanChange        BIT                 NOT NULL DEFAULT 0,
        
        -- Workflow status
        Status              TINYINT             NOT NULL DEFAULT 0,  -- 0=New, 1=Ack, 2=Resolved, 3=Dismissed
        AcknowledgedAtUtc   DATETIME2(3)        NULL,
        AcknowledgedBy      NVARCHAR(256)       NULL,
        ResolvedAtUtc       DATETIME2(3)        NULL,
        ResolvedBy          NVARCHAR(256)       NULL,
        Notes               NVARCHAR(MAX)       NULL,
        
        CONSTRAINT PK_RegressionEvent PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RegressionEvent_Fingerprint 
            FOREIGN KEY (FingerprintId) REFERENCES monitoring.QueryFingerprint(Id)
    );

    -- Index for recent events
    CREATE NONCLUSTERED INDEX IX_RegressionEvent_DetectedAt 
        ON monitoring.RegressionEvent (DetectedAtUtc DESC) 
        INCLUDE (FingerprintId, Severity, Status);

    -- Index for unacknowledged events (hot path for alerting)
    CREATE NONCLUSTERED INDEX IX_RegressionEvent_Unacknowledged 
        ON monitoring.RegressionEvent (Severity DESC, DetectedAtUtc DESC) 
        WHERE Status = 0;

    -- Index for fingerprint history
    CREATE NONCLUSTERED INDEX IX_RegressionEvent_Fingerprint 
        ON monitoring.RegressionEvent (FingerprintId, DetectedAtUtc DESC);

    -- Index for instance-based queries
    CREATE NONCLUSTERED INDEX IX_RegressionEvent_Instance 
        ON monitoring.RegressionEvent (InstanceName, DetectedAtUtc DESC) 
        INCLUDE (DatabaseName, Severity, Status);
END
GO

-- ============================================================================
-- Stored Procedure: monitoring.usp_GetOrCreateFingerprint
-- Purpose: Atomically gets or creates a fingerprint (upsert pattern)
-- ============================================================================
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_GetOrCreateFingerprint' AND schema_id = SCHEMA_ID('monitoring'))
    DROP PROCEDURE monitoring.usp_GetOrCreateFingerprint;
GO

CREATE PROCEDURE monitoring.usp_GetOrCreateFingerprint
    @QueryHash       VARBINARY(32),
    @QueryTextSample NVARCHAR(MAX),
    @DatabaseName    NVARCHAR(128),
    @FingerprintId   UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Try to get existing fingerprint first (most common case)
    SELECT @FingerprintId = Id 
    FROM monitoring.QueryFingerprint 
    WHERE QueryHash = @QueryHash;
    
    IF @FingerprintId IS NOT NULL
    BEGIN
        -- Update last seen timestamp
        UPDATE monitoring.QueryFingerprint 
        SET LastSeenUtc = SYSUTCDATETIME() 
        WHERE Id = @FingerprintId;
        RETURN;
    END
    
    -- Fingerprint doesn't exist, create it
    SET @FingerprintId = NEWID();
    
    BEGIN TRY
        INSERT INTO monitoring.QueryFingerprint (Id, QueryHash, QueryTextSample, DatabaseName)
        VALUES (@FingerprintId, @QueryHash, @QueryTextSample, @DatabaseName);
    END TRY
    BEGIN CATCH
        -- Handle race condition - another process may have inserted
        IF ERROR_NUMBER() = 2627 -- Unique constraint violation
        BEGIN
            SELECT @FingerprintId = Id 
            FROM monitoring.QueryFingerprint 
            WHERE QueryHash = @QueryHash;
            
            UPDATE monitoring.QueryFingerprint 
            SET LastSeenUtc = SYSUTCDATETIME() 
            WHERE Id = @FingerprintId;
        END
        ELSE
            THROW;
    END CATCH
END
GO

-- ============================================================================
-- Stored Procedure: monitoring.usp_PurgeSamples
-- Purpose: Deletes old samples to manage storage
-- ============================================================================
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_PurgeSamples' AND schema_id = SCHEMA_ID('monitoring'))
    DROP PROCEDURE monitoring.usp_PurgeSamples;
GO

CREATE PROCEDURE monitoring.usp_PurgeSamples
    @OlderThan   DATETIME2(3),
    @BatchSize   INT = 10000,
    @DeletedCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @DeletedCount = 0;
    
    DECLARE @Deleted INT = 1;
    
    -- Delete in batches to avoid long locks
    WHILE @Deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) 
        FROM monitoring.PlanMetricSample 
        WHERE SampledAtUtc < @OlderThan;
        
        SET @Deleted = @@ROWCOUNT;
        SET @DeletedCount = @DeletedCount + @Deleted;
        
        -- Brief pause between batches to let other queries run
        IF @Deleted = @BatchSize
            WAITFOR DELAY '00:00:00.100';
    END
END
GO

-- ============================================================================
-- Stored Procedure: monitoring.usp_GetRegressionSummary
-- Purpose: Returns summary statistics for regression events
-- ============================================================================
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_GetRegressionSummary' AND schema_id = SCHEMA_ID('monitoring'))
    DROP PROCEDURE monitoring.usp_GetRegressionSummary;
GO

CREATE PROCEDURE monitoring.usp_GetRegressionSummary
    @StartUtc DATETIME2(3),
    @EndUtc   DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        COUNT(*) AS TotalEvents,
        SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) AS NewEvents,
        SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) AS AcknowledgedEvents,
        SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS ResolvedEvents,
        SUM(CASE WHEN Severity = 3 THEN 1 ELSE 0 END) AS CriticalEvents,
        SUM(CASE WHEN Severity = 2 THEN 1 ELSE 0 END) AS HighEvents,
        SUM(CASE WHEN Severity = 1 THEN 1 ELSE 0 END) AS MediumEvents,
        SUM(CASE WHEN Severity = 0 THEN 1 ELSE 0 END) AS LowEvents,
        COUNT(DISTINCT FingerprintId) AS UniqueQueriesAffected,
        COUNT(DISTINCT DatabaseName) AS UniqueDatabasesAffected
    FROM monitoring.RegressionEvent
    WHERE DetectedAtUtc BETWEEN @StartUtc AND @EndUtc;
END
GO

-- ============================================================================
-- Stored Procedure: monitoring.usp_UpsertQueryFingerprint
-- Purpose: Atomically upserts a fingerprint with instance/database context
--          and returns whether it was newly created
-- ============================================================================
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_UpsertQueryFingerprint' AND schema_id = SCHEMA_ID('monitoring'))
    DROP PROCEDURE monitoring.usp_UpsertQueryFingerprint;
GO

CREATE PROCEDURE monitoring.usp_UpsertQueryFingerprint
    @InstanceName    NVARCHAR(256),
    @DatabaseName    NVARCHAR(128),
    @QueryHash       VARBINARY(32),
    @SampleText      NVARCHAR(MAX),
    @NormalizedText  NVARCHAR(MAX),
    @FingerprintId   UNIQUEIDENTIFIER OUTPUT,
    @IsNew           BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IsNew = 0;
    
    -- Try to get existing fingerprint first (most common case)
    SELECT @FingerprintId = Id 
    FROM monitoring.QueryFingerprint 
    WHERE QueryHash = @QueryHash;
    
    IF @FingerprintId IS NOT NULL
    BEGIN
        -- Update last seen timestamp
        UPDATE monitoring.QueryFingerprint 
        SET LastSeenUtc = SYSUTCDATETIME() 
        WHERE Id = @FingerprintId;
        RETURN;
    END
    
    -- Fingerprint doesn't exist, create it
    SET @FingerprintId = NEWID();
    SET @IsNew = 1;
    
    BEGIN TRY
        INSERT INTO monitoring.QueryFingerprint (Id, QueryHash, QueryTextSample, DatabaseName)
        VALUES (@FingerprintId, @QueryHash, @SampleText, @DatabaseName);
        
        -- Note: @InstanceName and @NormalizedText are available for future
        -- enhancement when we add those columns to the table
    END TRY
    BEGIN CATCH
        -- Handle race condition - another process may have inserted
        IF ERROR_NUMBER() = 2627 -- Unique constraint violation
        BEGIN
            SET @IsNew = 0;
            SELECT @FingerprintId = Id 
            FROM monitoring.QueryFingerprint 
            WHERE QueryHash = @QueryHash;
            
            UPDATE monitoring.QueryFingerprint 
            SET LastSeenUtc = SYSUTCDATETIME() 
            WHERE Id = @FingerprintId;
        END
        ELSE
            THROW;
    END CATCH
END
GO

PRINT 'Monitoring schema and tables created successfully.';
GO
