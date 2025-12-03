-- ============================================
-- Remediation Audit Table
-- ============================================
-- This table stores audit records for all remediation actions
-- (successful, failed, and dry-run).
--
-- Create this table in your monitoring database.
-- ============================================

IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'monitoring')
BEGIN
    EXEC('CREATE SCHEMA monitoring');
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'monitoring.RemediationAudit') AND type = 'U')
BEGIN
    CREATE TABLE monitoring.RemediationAudit (
        -- Primary key
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        
        -- Timestamp of the action
        Timestamp DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        
        -- Target identification
        InstanceName NVARCHAR(256) NOT NULL,
        DatabaseName NVARCHAR(256) NOT NULL,
        QueryFingerprint NVARCHAR(256) NOT NULL,
        QueryHash NVARCHAR(64) NULL,
        
        -- Related entities
        RegressionEventId UNIQUEIDENTIFIER NULL,
        RemediationSuggestionId UNIQUEIDENTIFIER NULL,
        
        -- Remediation details
        RemediationType NVARCHAR(100) NOT NULL,
        SqlStatement NVARCHAR(MAX) NOT NULL,
        
        -- Execution flags
        IsDryRun BIT NOT NULL DEFAULT 0,
        Success BIT NOT NULL,
        
        -- Error information
        ErrorMessage NVARCHAR(MAX) NULL,
        SqlErrorNumber INT NULL,
        
        -- Performance
        DurationMs FLOAT NULL,
        
        -- Audit trail
        InitiatedBy NVARCHAR(256) NULL,
        Notes NVARCHAR(MAX) NULL,
        MachineName NVARCHAR(256) NULL,
        ServiceVersion NVARCHAR(50) NULL,
        
        -- Indexing
        INDEX IX_RemediationAudit_Timestamp NONCLUSTERED (Timestamp DESC),
        INDEX IX_RemediationAudit_Instance NONCLUSTERED (InstanceName, DatabaseName, Timestamp DESC),
        INDEX IX_RemediationAudit_Fingerprint NONCLUSTERED (QueryFingerprint, Timestamp DESC),
        INDEX IX_RemediationAudit_Failures NONCLUSTERED (Success, IsDryRun, Timestamp DESC)
            WHERE Success = 0 AND IsDryRun = 0
    );
    
    PRINT 'Created table monitoring.RemediationAudit';
END
GO

-- Add retention policy (optional - delete records older than 1 year)
-- Uncomment and schedule as needed
/*
DELETE FROM monitoring.RemediationAudit
WHERE Timestamp < DATEADD(YEAR, -1, SYSDATETIMEOFFSET());
*/
