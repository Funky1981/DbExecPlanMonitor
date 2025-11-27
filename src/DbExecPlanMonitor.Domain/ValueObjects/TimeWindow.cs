namespace DbExecPlanMonitor.Domain.ValueObjects;

/// <summary>
/// Represents a time range for filtering and analysis.
/// Immutable value object.
/// </summary>
public readonly struct TimeWindow
{
    /// <summary>
    /// Start of the time window (UTC).
    /// </summary>
    public DateTime StartUtc { get; }

    /// <summary>
    /// End of the time window (UTC).
    /// </summary>
    public DateTime EndUtc { get; }

    /// <summary>
    /// Creates a new time window.
    /// </summary>
    public TimeWindow(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc < startUtc)
            throw new ArgumentException("End time must be after start time", nameof(endUtc));

        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    /// <summary>
    /// Duration of the window.
    /// </summary>
    public TimeSpan Duration => EndUtc - StartUtc;

    /// <summary>
    /// Creates a window from now going back the specified duration.
    /// </summary>
    public static TimeWindow Last(TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        return new TimeWindow(now - duration, now);
    }

    /// <summary>
    /// Creates a window for the last N hours.
    /// </summary>
    public static TimeWindow LastHours(int hours) => Last(TimeSpan.FromHours(hours));

    /// <summary>
    /// Creates a window for the last N minutes.
    /// </summary>
    public static TimeWindow LastMinutes(int minutes) => Last(TimeSpan.FromMinutes(minutes));

    /// <summary>
    /// Creates a window for the last N days.
    /// </summary>
    public static TimeWindow LastDays(int days) => Last(TimeSpan.FromDays(days));

    /// <summary>
    /// Checks if a timestamp falls within this window.
    /// </summary>
    public bool Contains(DateTime timestampUtc) =>
        timestampUtc >= StartUtc && timestampUtc <= EndUtc;

    public override string ToString() => $"[{StartUtc:u} - {EndUtc:u}]";

    public override bool Equals(object? obj) =>
        obj is TimeWindow other && StartUtc == other.StartUtc && EndUtc == other.EndUtc;

    public override int GetHashCode() => HashCode.Combine(StartUtc, EndUtc);

    public static bool operator ==(TimeWindow left, TimeWindow right) => left.Equals(right);
    public static bool operator !=(TimeWindow left, TimeWindow right) => !left.Equals(right);
}
