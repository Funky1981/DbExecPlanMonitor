using DbExecPlanMonitor.Domain.Services;
using FluentAssertions;
using Xunit;

namespace DbExecPlanMonitor.Domain.Tests.Services;

/// <summary>
/// Unit tests for the HotspotDetector domain service.
/// Tests cover hotspot identification, ranking, and filtering logic.
/// </summary>
public class HotspotDetectorTests
{
    private readonly HotspotDetector _sut = new();

    #region Basic Detection

    [Fact]
    public void DetectHotspots_WithEmptySamples_ReturnsEmptyList()
    {
        // Arrange
        var samples = Array.Empty<HotspotMetricSample>();
        var rules = new HotspotDetectionRules();

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectHotspots_ReturnsSamplesRankedByCpu()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalCpuTimeMs: 5000, executionCount: 100),
            CreateSample(totalCpuTimeMs: 10000, executionCount: 100),
            CreateSample(totalCpuTimeMs: 2000, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.TotalCpuTime,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(3);
        result[0].TotalCpuTimeMs.Should().Be(10000);
        result[0].Rank.Should().Be(1);
        result[1].TotalCpuTimeMs.Should().Be(5000);
        result[1].Rank.Should().Be(2);
        result[2].TotalCpuTimeMs.Should().Be(2000);
        result[2].Rank.Should().Be(3);
    }

    [Fact]
    public void DetectHotspots_RespectsTopNLimit()
    {
        // Arrange
        var samples = Enumerable.Range(1, 50)
            .Select(i => CreateSample(totalCpuTimeMs: i * 1000, executionCount: 100))
            .ToList();
        var rules = new HotspotDetectionRules
        {
            TopN = 10,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(10);
        result[0].TotalCpuTimeMs.Should().Be(50000); // Highest first
    }

    #endregion

    #region Ranking Metrics

    [Theory]
    [InlineData(HotspotRankingMetric.TotalCpuTime)]
    [InlineData(HotspotRankingMetric.TotalDuration)]
    [InlineData(HotspotRankingMetric.TotalLogicalReads)]
    [InlineData(HotspotRankingMetric.AvgDuration)]
    [InlineData(HotspotRankingMetric.ExecutionCount)]
    public void DetectHotspots_SetsCorrectRankByMetric(HotspotRankingMetric metric)
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalCpuTimeMs: 5000, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = metric,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].RankedBy.Should().Be(metric);
    }

    [Fact]
    public void DetectHotspots_RanksByTotalDuration()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalDurationMs: 1000, executionCount: 100),
            CreateSample(totalDurationMs: 5000, executionCount: 100),
            CreateSample(totalDurationMs: 3000, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.TotalDuration,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].TotalDurationMs.Should().Be(5000);
        result[1].TotalDurationMs.Should().Be(3000);
        result[2].TotalDurationMs.Should().Be(1000);
    }

    [Fact]
    public void DetectHotspots_RanksByExecutionCount()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(executionCount: 50),
            CreateSample(executionCount: 200),
            CreateSample(executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.ExecutionCount,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].ExecutionCount.Should().Be(200);
        result[1].ExecutionCount.Should().Be(100);
        result[2].ExecutionCount.Should().Be(50);
    }

    [Fact]
    public void DetectHotspots_RanksByTotalLogicalReads()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalLogicalReads: 10000, executionCount: 100),
            CreateSample(totalLogicalReads: 50000, executionCount: 100),
            CreateSample(totalLogicalReads: 25000, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.TotalLogicalReads,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].TotalLogicalReads.Should().Be(50000);
        result[1].TotalLogicalReads.Should().Be(25000);
        result[2].TotalLogicalReads.Should().Be(10000);
    }

    [Fact]
    public void DetectHotspots_RanksByAvgDuration()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(avgDurationMs: 100, executionCount: 100),
            CreateSample(avgDurationMs: 500, executionCount: 100),
            CreateSample(avgDurationMs: 250, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.AvgDuration,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].AvgDurationMs.Should().Be(500);
        result[1].AvgDurationMs.Should().Be(250);
        result[2].AvgDurationMs.Should().Be(100);
    }

    #endregion

    #region Filtering

    [Fact]
    public void DetectHotspots_FiltersOutLowCpuQueries()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalCpuTimeMs: 500, executionCount: 100),  // Below threshold
            CreateSample(totalCpuTimeMs: 2000, executionCount: 100) // Above threshold
        };
        var rules = new HotspotDetectionRules
        {
            MinTotalCpuMs = 1000,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].TotalCpuTimeMs.Should().Be(2000);
    }

    [Fact]
    public void DetectHotspots_FiltersOutLowDurationQueries()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalDurationMs: 1000, executionCount: 100),  // Below threshold
            CreateSample(totalDurationMs: 10000, executionCount: 100) // Above threshold
        };
        var rules = new HotspotDetectionRules
        {
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 5000,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].TotalDurationMs.Should().Be(10000);
    }

    [Fact]
    public void DetectHotspots_FiltersOutLowExecutionCountQueries()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(executionCount: 5),   // Below threshold
            CreateSample(executionCount: 50)   // Above threshold
        };
        var rules = new HotspotDetectionRules
        {
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 10,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].ExecutionCount.Should().Be(50);
    }

    [Fact]
    public void DetectHotspots_FiltersOutLowAvgDurationQueries()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(avgDurationMs: 50, executionCount: 100),  // Below threshold
            CreateSample(avgDurationMs: 200, executionCount: 100)  // Above threshold
        };
        var rules = new HotspotDetectionRules
        {
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 100
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].AvgDurationMs.Should().Be(200);
    }

    [Fact]
    public void DetectHotspots_ExcludesQueriesWithActiveRegressions_WhenConfigured()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(hasActiveRegression: true, executionCount: 100),
            CreateSample(hasActiveRegression: false, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            IncludeQueriesWithRegressions = false,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        result[0].HasActiveRegression.Should().BeFalse();
    }

    [Fact]
    public void DetectHotspots_IncludesQueriesWithActiveRegressions_ByDefault()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(hasActiveRegression: true, executionCount: 100),
            CreateSample(hasActiveRegression: false, executionCount: 100)
        };
        var rules = new HotspotDetectionRules
        {
            IncludeQueriesWithRegressions = true,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(2);
    }

    #endregion

    #region Percentage Calculations

    [Fact]
    public void DetectHotspots_CalculatesCorrectPercentOfTotal_ForCpu()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalCpuTimeMs: 3000, executionCount: 100), // 30%
            CreateSample(totalCpuTimeMs: 7000, executionCount: 100)  // 70%
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.TotalCpuTime,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].PercentOfTotal.Should().BeApproximately(70, 0.01);
        result[1].PercentOfTotal.Should().BeApproximately(30, 0.01);
    }

    [Fact]
    public void DetectHotspots_CalculatesCorrectPercentOfTotal_ForDuration()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalDurationMs: 2000, executionCount: 100), // 20%
            CreateSample(totalDurationMs: 8000, executionCount: 100)  // 80%
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.TotalDuration,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].PercentOfTotal.Should().BeApproximately(80, 0.01);
        result[1].PercentOfTotal.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void DetectHotspots_CalculatesCorrectPercentOfTotal_ForLogicalReads()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>
        {
            CreateSample(totalLogicalReads: 4000, executionCount: 100), // 40%
            CreateSample(totalLogicalReads: 6000, executionCount: 100)  // 60%
        };
        var rules = new HotspotDetectionRules
        {
            RankBy = HotspotRankingMetric.TotalLogicalReads,
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result[0].PercentOfTotal.Should().BeApproximately(60, 0.01);
        result[1].PercentOfTotal.Should().BeApproximately(40, 0.01);
    }

    #endregion

    #region Null Parameter Handling

    [Fact]
    public void DetectHotspots_WithNullSamples_ThrowsArgumentNullException()
    {
        // Arrange
        var rules = new HotspotDetectionRules();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _sut.DetectHotspots(null!, rules));
    }

    [Fact]
    public void DetectHotspots_WithNullRules_ThrowsArgumentNullException()
    {
        // Arrange
        var samples = new List<HotspotMetricSample>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _sut.DetectHotspots(samples, null!));
    }

    #endregion

    #region Hotspot Properties

    [Fact]
    public void DetectHotspots_CopiesAllPropertiesFromSample()
    {
        // Arrange
        var fingerprintId = Guid.NewGuid();
        var planHash = new byte[] { 0x01, 0x02, 0x03 };
        var samples = new List<HotspotMetricSample>
        {
            new HotspotMetricSample
            {
                FingerprintId = fingerprintId,
                InstanceName = "TestInstance",
                DatabaseName = "TestDB",
                QueryTextSample = "SELECT * FROM Users",
                ExecutionCount = 100,
                TotalCpuTimeMs = 5000,
                TotalDurationMs = 10000,
                TotalLogicalReads = 50000,
                AvgDurationMs = 100,
                AvgCpuTimeMs = 50,
                PlanHash = planHash,
                HasActiveRegression = true
            }
        };
        var rules = new HotspotDetectionRules
        {
            MinTotalCpuMs = 0,
            MinTotalDurationMs = 0,
            MinExecutionCount = 0,
            MinAvgDurationMs = 0
        };

        // Act
        var result = _sut.DetectHotspots(samples, rules);

        // Assert
        result.Should().HaveCount(1);
        var hotspot = result[0];
        hotspot.FingerprintId.Should().Be(fingerprintId);
        hotspot.InstanceName.Should().Be("TestInstance");
        hotspot.DatabaseName.Should().Be("TestDB");
        hotspot.QueryTextSample.Should().Be("SELECT * FROM Users");
        hotspot.ExecutionCount.Should().Be(100);
        hotspot.TotalCpuTimeMs.Should().Be(5000);
        hotspot.TotalDurationMs.Should().Be(10000);
        hotspot.TotalLogicalReads.Should().Be(50000);
        hotspot.AvgDurationMs.Should().Be(100);
        hotspot.AvgCpuTimeMs.Should().Be(50);
        hotspot.PlanHash.Should().BeEquivalentTo(planHash);
        hotspot.HasActiveRegression.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static HotspotMetricSample CreateSample(
        double totalCpuTimeMs = 5000,
        double totalDurationMs = 10000,
        long totalLogicalReads = 50000,
        long executionCount = 100,
        double avgDurationMs = 150,
        double avgCpuTimeMs = 50,
        bool hasActiveRegression = false)
    {
        return new HotspotMetricSample
        {
            FingerprintId = Guid.NewGuid(),
            InstanceName = "TestInstance",
            DatabaseName = "TestDB",
            QueryTextSample = "SELECT * FROM TestTable",
            ExecutionCount = executionCount,
            TotalCpuTimeMs = totalCpuTimeMs,
            TotalDurationMs = totalDurationMs,
            TotalLogicalReads = totalLogicalReads,
            AvgDurationMs = avgDurationMs,
            AvgCpuTimeMs = avgCpuTimeMs,
            HasActiveRegression = hasActiveRegression
        };
    }

    #endregion
}
