using DbExecPlanMonitor.Application.Configuration;
using DbExecPlanMonitor.Application.Services;
using DbExecPlanMonitor.Application.Tests.Fakes;
using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DbExecPlanMonitor.Application.Tests.Services;

/// <summary>
/// Unit tests for the RemediationGuardService.
/// Tests cover all safety rails and protection mechanisms.
/// </summary>
public class RemediationGuardServiceTests
{
    private readonly InMemoryRemediationAuditRepository _auditRepository = new();
    private readonly ILogger<RemediationGuardService> _logger = NullLogger<RemediationGuardService>.Instance;

    #region Global Kill Switch

    [Fact]
    public async Task CheckAsync_WhenRemediationDisabled_DeniesRemediation()
    {
        // Arrange
        var options = CreateOptions(enableRemediation: false);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB", 
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("globally disabled");
    }

    #endregion

    #region Monitoring Mode

    [Fact]
    public async Task CheckAsync_WhenModeIsReadOnly_DeniesRemediation()
    {
        // Arrange
        var options = CreateOptions(mode: MonitoringMode.ReadOnly);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("ReadOnly");
    }

    [Fact]
    public async Task CheckAsync_WhenModeIsSuggestRemediation_DeniesExecution()
    {
        // Arrange
        var options = CreateOptions(mode: MonitoringMode.SuggestRemediation);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("SuggestRemediation");
    }

    [Fact]
    public async Task CheckAsync_WhenModeIsAutoApplyLowRisk_AllowsLowRiskRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            approvalThreshold: RemediationRiskLevel.Medium);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenModeIsAutoApplyLowRisk_DeniesMediumRiskRemediation()
    {
        // Arrange
        var options = CreateOptions(mode: MonitoringMode.AutoApplyLowRisk);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.ForcePlan, RemediationRiskLevel.Medium);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("exceeds Low threshold");
    }

    [Fact]
    public async Task CheckAsync_WhenModeIsAutoApplyLowRisk_DeniesHighRiskRemediation()
    {
        // Arrange
        var options = CreateOptions(mode: MonitoringMode.AutoApplyLowRisk);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.DropIndex, RemediationRiskLevel.High);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("exceeds Low threshold");
    }

    #endregion

    #region System Database Protection

    [Theory]
    [InlineData("master")]
    [InlineData("msdb")]
    [InlineData("model")]
    [InlineData("tempdb")]
    [InlineData("resource")]
    [InlineData("MASTER")] // Case insensitive
    [InlineData("TempDB")]  // Mixed case
    public async Task CheckAsync_ForSystemDatabase_DeniesRemediation(string databaseName)
    {
        // Arrange
        var options = CreateOptions(mode: MonitoringMode.AutoApplyLowRisk);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", databaseName,
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("system database");
    }

    #endregion

    #region Excluded Databases

    [Fact]
    public async Task CheckAsync_ForExcludedDatabase_DeniesRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            excludedDatabases: new List<string> { "ProductionCritical", "FinanceDB" });
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "ProductionCritical",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("excluded databases list");
    }

    [Fact]
    public async Task CheckAsync_ForExcludedDatabase_IsCaseInsensitive()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            excludedDatabases: new List<string> { "ProductionCritical" });
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "PRODUCTIONCRITICAL",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
    }

    #endregion

    #region Rate Limiting

    [Fact]
    public async Task CheckAsync_WhenRateLimitExceeded_DeniesRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            maxRemediationsPerHour: 3);
        
        // Add 3 successful remediations in the last hour
        for (int i = 0; i < 3; i++)
        {
            _auditRepository.AddRecord(CreateAuditRecord(
                success: true,
                isDryRun: false,
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-30)));
        }
        
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("Rate limit exceeded");
    }

    [Fact]
    public async Task CheckAsync_WhenDryRunsDoNotCountAgainstRateLimit_AllowsRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            maxRemediationsPerHour: 3);
        
        // Add 3 dry-run remediations (should not count against limit)
        for (int i = 0; i < 3; i++)
        {
            _auditRepository.AddRecord(CreateAuditRecord(
                success: true,
                isDryRun: true,
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-30)));
        }
        
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenFailedRemediationsDoNotCountAgainstRateLimit_AllowsRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            maxRemediationsPerHour: 3);
        
        // Add 3 failed remediations (should not count against limit)
        for (int i = 0; i < 3; i++)
        {
            _auditRepository.AddRecord(CreateAuditRecord(
                success: false,
                isDryRun: false,
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-30)));
        }
        
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenOldRemediationsDoNotCountAgainstRateLimit_AllowsRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            maxRemediationsPerHour: 3);
        
        // Add 3 successful remediations more than 1 hour ago
        for (int i = 0; i < 3; i++)
        {
            _auditRepository.AddRecord(CreateAuditRecord(
                success: true,
                isDryRun: false,
                timestamp: DateTimeOffset.UtcNow.AddHours(-2)));
        }
        
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    #endregion

    #region Maintenance Window

    [Fact]
    public async Task CheckAsync_WhenOutsideMaintenanceWindow_DeniesRemediation()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero); // Noon UTC
        var timeProvider = CreateTimeProvider(fixedTime);
        
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            requireMaintenanceWindow: true,
            maintenanceWindowStart: 2,  // 2 AM
            maintenanceWindowEnd: 6);    // 6 AM
        
        var sut = CreateSut(options, timeProvider);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeFalse();
        result.Reason.Should().Contain("Outside maintenance window");
    }

    [Fact]
    public async Task CheckAsync_WhenInsideMaintenanceWindow_AllowsRemediation()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero); // 3 AM UTC
        var timeProvider = CreateTimeProvider(fixedTime);
        
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            requireMaintenanceWindow: true,
            maintenanceWindowStart: 2,  // 2 AM
            maintenanceWindowEnd: 6);    // 6 AM
        
        var sut = CreateSut(options, timeProvider);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenCrossMidnightMaintenanceWindow_AllowsRemediation()
    {
        // Arrange - window from 22:00 to 4:00
        var fixedTime = new DateTimeOffset(2024, 1, 1, 23, 0, 0, TimeSpan.Zero); // 11 PM UTC
        var timeProvider = CreateTimeProvider(fixedTime);
        
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            requireMaintenanceWindow: true,
            maintenanceWindowStart: 22, // 10 PM
            maintenanceWindowEnd: 4);    // 4 AM
        
        var sut = CreateSut(options, timeProvider);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_WhenMaintenanceWindowNotRequired_IgnoresWindow()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero); // Noon UTC
        var timeProvider = CreateTimeProvider(fixedTime);
        
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            requireMaintenanceWindow: false);
        
        var sut = CreateSut(options, timeProvider);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    #endregion

    #region Approval Threshold

    [Fact]
    public async Task CheckAsync_WhenRiskExceedsApprovalThreshold_DeniesRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            approvalThreshold: RemediationRiskLevel.Medium);
        var sut = CreateSut(options);

        // Act - Medium risk equals threshold, should be denied
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.ForcePlan, RemediationRiskLevel.Medium);

        // Assert - AutoApplyLowRisk will deny Medium first, but if that passed, 
        // approval threshold would also deny it
        result.IsPermitted.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_WhenRiskBelowApprovalThreshold_AllowsRemediation()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            approvalThreshold: RemediationRiskLevel.High); // Threshold is High
        var sut = CreateSut(options);

        // Act - Low risk is below High threshold
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
    }

    #endregion

    #region Dry Run Mode

    [Fact]
    public async Task CheckAsync_WhenDryRunEnabled_IndicatesInResult()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            dryRunMode: true);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
        result.IsDryRun.Should().BeTrue();
        result.Reason.Should().Contain("DRY-RUN");
    }

    [Fact]
    public async Task CheckAsync_WhenDryRunDisabled_IndicatesInResult()
    {
        // Arrange
        var options = CreateOptions(
            mode: MonitoringMode.AutoApplyLowRisk,
            dryRunMode: false);
        var sut = CreateSut(options);

        // Act
        var result = await sut.CheckAsync("TestInstance", "TestDB",
            RemediationType.CreateIndex, RemediationRiskLevel.Low);

        // Assert
        result.IsPermitted.Should().BeTrue();
        result.IsDryRun.Should().BeFalse();
    }

    #endregion

    #region Property Accessors

    [Fact]
    public void CurrentMode_ReturnsConfiguredMode()
    {
        // Arrange
        var options = CreateOptions(mode: MonitoringMode.SuggestRemediation);
        var sut = CreateSut(options);

        // Assert
        sut.CurrentMode.Should().Be(MonitoringMode.SuggestRemediation);
    }

    [Fact]
    public void CurrentEnvironment_ReturnsConfiguredEnvironment()
    {
        // Arrange
        var options = CreateOptions(environment: EnvironmentType.Staging);
        var sut = CreateSut(options);

        // Assert
        sut.CurrentEnvironment.Should().Be(EnvironmentType.Staging);
    }

    [Fact]
    public void IsRemediationEnabled_ReturnsConfiguredValue()
    {
        // Arrange
        var options = CreateOptions(enableRemediation: true);
        var sut = CreateSut(options);

        // Assert
        sut.IsRemediationEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsDryRunMode_ReturnsConfiguredValue()
    {
        // Arrange
        var options = CreateOptions(dryRunMode: true);
        var sut = CreateSut(options);

        // Assert
        sut.IsDryRunMode.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private RemediationGuardService CreateSut(
        IOptionsMonitor<SecurityOptions> options,
        TimeProvider? timeProvider = null)
    {
        return new RemediationGuardService(
            options,
            _auditRepository,
            _logger,
            timeProvider ?? TimeProvider.System);
    }

    private IOptionsMonitor<SecurityOptions> CreateOptions(
        MonitoringMode mode = MonitoringMode.AutoApplyLowRisk,
        EnvironmentType environment = EnvironmentType.Development,
        bool enableRemediation = true,
        bool dryRunMode = false,
        int maxRemediationsPerHour = 10,
        List<string>? excludedDatabases = null,
        RemediationRiskLevel approvalThreshold = RemediationRiskLevel.High,
        bool requireMaintenanceWindow = false,
        int maintenanceWindowStart = 2,
        int maintenanceWindowEnd = 6)
    {
        var securityOptions = new SecurityOptions
        {
            Mode = mode,
            Environment = environment,
            EnableRemediation = enableRemediation,
            DryRunMode = dryRunMode,
            MaxRemediationsPerHour = maxRemediationsPerHour,
            ExcludedDatabases = excludedDatabases ?? new List<string>(),
            ApprovalThreshold = approvalThreshold,
            ProtectiveChecks = new ProtectiveChecksOptions
            {
                RequireMaintenanceWindow = requireMaintenanceWindow,
                MaintenanceWindowStartHour = maintenanceWindowStart,
                MaintenanceWindowEndHour = maintenanceWindowEnd
            }
        };

        var monitor = Substitute.For<IOptionsMonitor<SecurityOptions>>();
        monitor.CurrentValue.Returns(securityOptions);
        return monitor;
    }

    private RemediationAuditRecord CreateAuditRecord(
        bool success = true,
        bool isDryRun = false,
        DateTimeOffset? timestamp = null)
    {
        return new RemediationAuditRecord
        {
            InstanceName = "TestInstance",
            DatabaseName = "TestDB",
            QueryFingerprint = "TestFingerprint",
            RemediationType = "CreateIndex",
            SqlStatement = "CREATE INDEX IX_Test ON dbo.Test(Column)",
            Success = success,
            IsDryRun = isDryRun,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow
        };
    }

    private TimeProvider CreateTimeProvider(DateTimeOffset fixedTime)
    {
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(fixedTime);
        return timeProvider;
    }

    #endregion
}
