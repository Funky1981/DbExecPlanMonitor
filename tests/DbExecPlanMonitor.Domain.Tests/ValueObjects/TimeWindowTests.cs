using DbExecPlanMonitor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace DbExecPlanMonitor.Domain.Tests.ValueObjects;

/// <summary>
/// Unit tests for the TimeWindow value object.
/// </summary>
public class TimeWindowTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidTimes_CreatesTimeWindow()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;

        // Act
        var window = new TimeWindow(start, end);

        // Assert
        window.StartUtc.Should().Be(start);
        window.EndUtc.Should().Be(end);
    }

    [Fact]
    public void Constructor_WithEndBeforeStart_ThrowsArgumentException()
    {
        // Arrange
        var start = DateTime.UtcNow;
        var end = DateTime.UtcNow.AddHours(-1);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TimeWindow(start, end));
    }

    [Fact]
    public void Constructor_WithEqualTimes_CreatesZeroDurationWindow()
    {
        // Arrange
        var time = DateTime.UtcNow;

        // Act
        var window = new TimeWindow(time, time);

        // Assert
        window.Duration.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region Duration Tests

    [Fact]
    public void Duration_ReturnsCorrectTimeSpan()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var window = new TimeWindow(start, end);

        // Act
        var duration = window.Duration;

        // Assert
        duration.Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Last_CreatesWindowWithCorrectDuration()
    {
        // Act
        var window = TimeWindow.Last(TimeSpan.FromHours(3));

        // Assert
        window.Duration.Should().BeCloseTo(TimeSpan.FromHours(3), TimeSpan.FromSeconds(1));
        window.EndUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LastHours_CreatesWindowWithCorrectDuration()
    {
        // Act
        var window = TimeWindow.LastHours(4);

        // Assert
        window.Duration.Should().BeCloseTo(TimeSpan.FromHours(4), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LastMinutes_CreatesWindowWithCorrectDuration()
    {
        // Act
        var window = TimeWindow.LastMinutes(30);

        // Assert
        window.Duration.Should().BeCloseTo(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LastDays_CreatesWindowWithCorrectDuration()
    {
        // Act
        var window = TimeWindow.LastDays(7);

        // Assert
        window.Duration.Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Contains_WhenTimestampWithinWindow_ReturnsTrue()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var middle = DateTime.UtcNow.AddHours(-1);
        var window = new TimeWindow(start, end);

        // Act
        var result = window.Contains(middle);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_WhenTimestampAtStart_ReturnsTrue()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var window = new TimeWindow(start, end);

        // Act
        var result = window.Contains(start);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_WhenTimestampAtEnd_ReturnsTrue()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var window = new TimeWindow(start, end);

        // Act
        var result = window.Contains(end);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_WhenTimestampBeforeWindow_ReturnsFalse()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var before = DateTime.UtcNow.AddHours(-3);
        var window = new TimeWindow(start, end);

        // Act
        var result = window.Contains(before);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_WhenTimestampAfterWindow_ReturnsFalse()
    {
        // Arrange
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var after = DateTime.UtcNow.AddHours(1);
        var window = new TimeWindow(start, end);

        // Act
        var result = window.Contains(after);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Equals_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var window1 = new TimeWindow(start, end);
        var window2 = new TimeWindow(start, end);

        // Act & Assert
        window1.Equals(window2).Should().BeTrue();
        (window1 == window2).Should().BeTrue();
        (window1 != window2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithDifferentStart_ReturnsFalse()
    {
        // Arrange
        var start1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var start2 = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var window1 = new TimeWindow(start1, end);
        var window2 = new TimeWindow(start2, end);

        // Act & Assert
        window1.Equals(window2).Should().BeFalse();
        (window1 == window2).Should().BeFalse();
        (window1 != window2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentEnd_ReturnsFalse()
    {
        // Arrange
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end1 = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var end2 = new DateTime(2024, 1, 2, 1, 0, 0, DateTimeKind.Utc);
        var window1 = new TimeWindow(start, end1);
        var window2 = new TimeWindow(start, end2);

        // Act & Assert
        window1.Equals(window2).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ForEqualWindows_ReturnsSameValue()
    {
        // Arrange
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var window1 = new TimeWindow(start, end);
        var window2 = new TimeWindow(start, end);

        // Act & Assert
        window1.GetHashCode().Should().Be(window2.GetHashCode());
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var window = new TimeWindow(start, end);

        // Act
        var result = window.ToString();

        // Assert
        result.Should().Contain("2024-01-01");
        result.Should().Contain("2024-01-02");
    }

    #endregion
}
