using DbExecPlanMonitor.Domain.Entities;
using DbExecPlanMonitor.Domain.Enums;
using DbExecPlanMonitor.Domain.Services;
using DbExecPlanMonitor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace DbExecPlanMonitor.Domain.Tests.Services;

/// <summary>
/// Unit tests for the RegressionDetector domain service.
/// Tests cover the core regression detection algorithm with various scenarios.
/// </summary>
public class RegressionDetectorTests
{
    private readonly RegressionDetector _sut = new();
    private readonly Guid _fingerprintId = Guid.NewGuid();

    #region DetectRegression with AggregatedMetricsForAnalysis

    [Fact]
    public void DetectRegression_WhenDurationExceedsThreshold_ReturnsRegressionEvent()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95DurationUs: 2000, totalExecutions: 10); // 100% increase
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50 // Threshold: 50%
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().NotBeNull();
        result!.FingerprintId.Should().Be(_fingerprintId);
        result.DurationChangePercent.Should().Be(100); // Doubled = 100% increase
        result.Severity.Should().Be(RegressionSeverity.Medium); // 100% = 2x
    }

    [Fact]
    public void DetectRegression_WhenDurationBelowThreshold_ReturnsNull()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95DurationUs: 1200, totalExecutions: 10); // 20% increase
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50 // Threshold: 50%
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DetectRegression_WhenCpuExceedsThreshold_ReturnsRegressionEvent()
    {
        // Arrange
        var baseline = CreateBaseline(p95CpuTimeUs: 500, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95CpuTimeUs: 1000, totalExecutions: 10); // 100% increase
        var rules = new RegressionDetectionRules
        {
            CpuIncreaseThresholdPercent = 50
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().NotBeNull();
        result!.CpuChangePercent.Should().Be(100);
    }

    [Fact]
    public void DetectRegression_WhenLogicalReadsExceedsThreshold_ReturnsRegressionEvent()
    {
        // Arrange
        var baseline = CreateBaseline(avgLogicalReads: 100, sampleCount: 15);
        var current = CreateAggregatedMetrics(avgLogicalReads: 350, totalExecutions: 10); // 250% increase
        var rules = new RegressionDetectionRules
        {
            LogicalReadsIncreaseThresholdPercent = 100
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void DetectRegression_WhenBaselineNotReliable_ReturnsNull()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 5); // Below minimum
        var current = CreateAggregatedMetrics(p95DurationUs: 5000, totalExecutions: 10);
        var rules = new RegressionDetectionRules
        {
            MinimumBaselineSamples = 10
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().BeNull("baseline has insufficient samples");
    }

    [Fact]
    public void DetectRegression_WhenInsufficientExecutions_ReturnsNull()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95DurationUs: 5000, totalExecutions: 3); // Below minimum
        var rules = new RegressionDetectionRules
        {
            MinimumExecutions = 5
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().BeNull("current metrics have insufficient executions");
    }

    [Fact]
    public void DetectRegression_WhenRequireMultipleMetrics_RequiresTwoRegressions()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, p95CpuTimeUs: 500, sampleCount: 15);
        var current = CreateAggregatedMetrics(
            p95DurationUs: 2000, // Duration regressed
            p95CpuTimeUs: 500,   // CPU did not regress
            totalExecutions: 10);
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50,
            CpuIncreaseThresholdPercent = 50,
            RequireMultipleMetrics = true
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().BeNull("only one metric regressed but RequireMultipleMetrics=true");
    }

    [Fact]
    public void DetectRegression_WhenRequireMultipleMetrics_AndMultipleRegress_ReturnsEvent()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, p95CpuTimeUs: 500, sampleCount: 15);
        var current = CreateAggregatedMetrics(
            p95DurationUs: 2000, // Duration regressed
            p95CpuTimeUs: 1000,  // CPU regressed too
            totalExecutions: 10);
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50,
            CpuIncreaseThresholdPercent = 50,
            RequireMultipleMetrics = true
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().NotBeNull("multiple metrics regressed");
    }

    #endregion

    #region Severity Determination

    [Theory]
    [InlineData(50, RegressionSeverity.Low)]      // 50% = 1.5x
    [InlineData(100, RegressionSeverity.Medium)]  // 100% = 2x
    [InlineData(200, RegressionSeverity.High)]    // 200% = 3x
    [InlineData(500, RegressionSeverity.Critical)] // 500% = 6x
    [InlineData(1000, RegressionSeverity.Critical)] // 1000% = 11x
    public void DetectRegression_SeverityDeterminedByPercentIncrease(
        int percentIncrease, RegressionSeverity expectedSeverity)
    {
        // Arrange
        var baselineValue = 1000L;
        var currentValue = baselineValue + (baselineValue * percentIncrease / 100);

        var baseline = CreateBaseline(p95DurationUs: baselineValue, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95DurationUs: currentValue, totalExecutions: 10);
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().NotBeNull();
        result!.Severity.Should().Be(expectedSeverity);
    }

    #endregion

    #region DetectRegressions with MetricSamples

    [Fact]
    public void DetectRegressions_WithEmptySamples_ReturnsEmptyList()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var samples = Array.Empty<MetricSample>();
        var rules = new RegressionDetectionRules();

        // Act
        var result = _sut.DetectRegressions(baseline, samples, rules);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectRegressions_AggregatesSamplesCorrectly()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var samples = new List<MetricSample>
        {
            CreateSample(avgDurationUs: 1500, executionCount: 5, p95DurationUs: 1800),
            CreateSample(avgDurationUs: 2000, executionCount: 5, p95DurationUs: 2200),
            CreateSample(avgDurationUs: 2500, executionCount: 5, p95DurationUs: 2600)
        };
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50,
            MinimumExecutions = 10
        };

        // Act
        var result = _sut.DetectRegressions(baseline, samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].FingerprintId.Should().Be(_fingerprintId);
    }

    [Fact]
    public void DetectRegressions_WithNullBaseline_ThrowsArgumentNullException()
    {
        // Arrange
        var samples = new List<MetricSample>();
        var rules = new RegressionDetectionRules();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _sut.DetectRegressions(null!, samples, rules));
    }

    [Fact]
    public void DetectRegressions_WithNullSamples_ThrowsArgumentNullException()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var rules = new RegressionDetectionRules();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _sut.DetectRegressions(baseline, null!, rules));
    }

    [Fact]
    public void DetectRegressions_WithNullRules_ThrowsArgumentNullException()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var samples = new List<MetricSample>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _sut.DetectRegressions(baseline, samples, null!));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void DetectRegression_WhenBaselineValueIsZero_HandlesGracefully()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 0, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95DurationUs: 1000, totalExecutions: 10);
        var rules = new RegressionDetectionRules();

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().BeNull("cannot calculate percentage increase from zero baseline");
    }

    [Fact]
    public void DetectRegression_WhenCurrentValueIsNull_SkipsMetric()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var current = CreateAggregatedMetrics(p95DurationUs: null, totalExecutions: 10);
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().BeNull("P95 duration is null in current metrics");
    }

    [Fact]
    public void DetectRegression_SetsCorrectTimeWindow()
    {
        // Arrange
        var baseline = CreateBaseline(p95DurationUs: 1000, sampleCount: 15);
        var startTime = DateTime.UtcNow.AddHours(-1);
        var endTime = DateTime.UtcNow;
        var current = CreateAggregatedMetrics(
            p95DurationUs: 2000, 
            totalExecutions: 10,
            windowStart: startTime,
            windowEnd: endTime);
        var rules = new RegressionDetectionRules
        {
            DurationIncreaseThresholdPercent = 50
        };

        // Act
        var result = _sut.DetectRegression(baseline, current, rules);

        // Assert
        result.Should().NotBeNull();
        result!.SampleWindowStart.Should().Be(startTime);
        result.SampleWindowEnd.Should().Be(endTime);
    }

    #endregion

    #region RegressionDetectionRules Tests

    [Theory]
    [InlineData(50, 1.5)]
    [InlineData(100, 2.0)]
    [InlineData(200, 3.0)]
    [InlineData(0, 1.0)]
    public void ToMultiplier_CalculatesCorrectly(decimal percentIncrease, decimal expectedMultiplier)
    {
        // Act
        var result = RegressionDetectionRules.ToMultiplier(percentIncrease);

        // Assert
        result.Should().Be(expectedMultiplier);
    }

    #endregion

    #region Helper Methods

    private PlanBaseline CreateBaseline(
        long? p95DurationUs = null,
        long? p95CpuTimeUs = null,
        long avgLogicalReads = 50,
        int sampleCount = 10)
    {
        return new PlanBaseline
        {
            Id = Guid.NewGuid(),
            FingerprintId = _fingerprintId,
            InstanceName = "TestInstance",
            DatabaseName = "TestDatabase",
            ComputedAtUtc = DateTime.UtcNow.AddDays(-1),
            WindowStartUtc = DateTime.UtcNow.AddDays(-8),
            WindowEndUtc = DateTime.UtcNow.AddDays(-1),
            SampleCount = sampleCount,
            TotalExecutions = sampleCount * 100,
            P95DurationUs = p95DurationUs,
            P95CpuTimeUs = p95CpuTimeUs,
            AvgLogicalReads = avgLogicalReads,
            AvgDurationUs = (p95DurationUs ?? 1000) / 2,
            AvgCpuTimeUs = (p95CpuTimeUs ?? 500) / 2
        };
    }

    private AggregatedMetricsForAnalysis CreateAggregatedMetrics(
        long? p95DurationUs = null,
        long? p95CpuTimeUs = null,
        long avgLogicalReads = 50,
        long totalExecutions = 10,
        DateTime? windowStart = null,
        DateTime? windowEnd = null)
    {
        var start = windowStart ?? DateTime.UtcNow.AddHours(-1);
        var end = windowEnd ?? DateTime.UtcNow;

        return new AggregatedMetricsForAnalysis
        {
            FingerprintId = _fingerprintId,
            Window = new TimeWindow(start, end),
            SampleCount = 10,
            TotalExecutions = totalExecutions,
            AvgDurationUs = (p95DurationUs ?? 1000) / 2,
            P95DurationUs = p95DurationUs,
            AvgCpuTimeUs = (p95CpuTimeUs ?? 500) / 2,
            P95CpuTimeUs = p95CpuTimeUs,
            AvgLogicalReads = avgLogicalReads
        };
    }

    private MetricSample CreateSample(
        long avgDurationUs = 1000,
        long executionCount = 10,
        long? p95DurationUs = null,
        long? p95CpuTimeUs = null)
    {
        return new MetricSample
        {
            SampledAtUtc = DateTime.UtcNow.AddMinutes(-Random.Shared.Next(1, 60)),
            ExecutionCount = executionCount,
            AvgDurationUs = avgDurationUs,
            AvgCpuTimeUs = avgDurationUs / 2,
            AvgLogicalReads = 50,
            P95DurationUs = p95DurationUs ?? avgDurationUs * 2,
            P95CpuTimeUs = p95CpuTimeUs
        };
    }

    #endregion
}
