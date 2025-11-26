namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// Represents a time window for analysis (start to end time).
/// Immutable value object.
/// </summary>
/// <remarks>
/// Used throughout the system to define analysis periods:
/// - "Top queries in the last hour"
/// - "Samples between 9am and 5pm"
/// - "Baseline calculated over this 7-day window"
/// </remarks>
public sealed class TimeWindow : IEquatable<TimeWindow>
{
    /// <summary>
    /// Start of the time window (inclusive).
    /// </summary>
    public DateTime StartUtc { get; }

    /// <summary>
    /// End of the time window (inclusive).
    /// </summary>
    public DateTime EndUtc { get; }

    /// <summary>
    /// Duration of the time window.
    /// </summary>
    public TimeSpan Duration => EndUtc - StartUtc;

    /// <summary>
    /// Creates a new time window.
    /// </summary>
    public TimeWindow(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc < startUtc)
            throw new ArgumentException("End time must be after start time.", nameof(endUtc));

        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    /// <summary>
    /// Creates a time window ending now, going back the specified duration.
    /// </summary>
    public static TimeWindow LastMinutes(int minutes)
    {
        var end = DateTime.UtcNow;
        var start = end.AddMinutes(-minutes);
        return new TimeWindow(start, end);
    }

    /// <summary>
    /// Creates a time window ending now, going back the specified duration.
    /// </summary>
    public static TimeWindow LastHours(int hours)
    {
        var end = DateTime.UtcNow;
        var start = end.AddHours(-hours);
        return new TimeWindow(start, end);
    }

    /// <summary>
    /// Creates a time window ending now, going back the specified duration.
    /// </summary>
    public static TimeWindow LastDays(int days)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-days);
        return new TimeWindow(start, end);
    }

    /// <summary>
    /// Creates a time window from a duration ending now.
    /// </summary>
    public static TimeWindow FromDuration(TimeSpan duration)
    {
        var end = DateTime.UtcNow;
        var start = end - duration;
        return new TimeWindow(start, end);
    }

    /// <summary>
    /// Checks if a timestamp falls within this window.
    /// </summary>
    public bool Contains(DateTime timestampUtc)
    {
        return timestampUtc >= StartUtc && timestampUtc <= EndUtc;
    }

    /// <summary>
    /// Checks if this window overlaps with another.
    /// </summary>
    public bool Overlaps(TimeWindow other)
    {
        return StartUtc < other.EndUtc && EndUtc > other.StartUtc;
    }

    /// <summary>
    /// Extends this window by adding time to start and/or end.
    /// </summary>
    public TimeWindow Extend(TimeSpan beforeStart, TimeSpan afterEnd)
    {
        return new TimeWindow(StartUtc - beforeStart, EndUtc + afterEnd);
    }

    /// <summary>
    /// Shifts the entire window by a duration.
    /// </summary>
    public TimeWindow Shift(TimeSpan offset)
    {
        return new TimeWindow(StartUtc + offset, EndUtc + offset);
    }

    public override string ToString()
    {
        return $"{StartUtc:u} to {EndUtc:u} ({Duration.TotalHours:N1}h)";
    }

    public bool Equals(TimeWindow? other)
    {
        if (other is null) return false;
        return StartUtc == other.StartUtc && EndUtc == other.EndUtc;
    }

    public override bool Equals(object? obj) => Equals(obj as TimeWindow);

    public override int GetHashCode() => HashCode.Combine(StartUtc, EndUtc);

    public static bool operator ==(TimeWindow? left, TimeWindow? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(TimeWindow? left, TimeWindow? right) => !(left == right);
}
