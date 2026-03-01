namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Pure synchronous calendar math helper for game time calculations.
/// All methods are deterministic computations with no I/O or state store access.
/// </summary>
internal interface IWorldstateTimeCalculator
{
    /// <summary>
    /// Advances a realm clock by the specified number of game-seconds, updating all
    /// calendar fields (year, month, day, hour, minute, period, season) and returning
    /// a list of boundaries crossed during the advancement.
    /// </summary>
    /// <param name="clock">The current clock state to advance. Modified in-place.</param>
    /// <param name="calendar">The calendar template defining time structure.</param>
    /// <param name="gameSeconds">Number of game-seconds to advance.</param>
    /// <returns>A list of boundary events crossed during advancement.</returns>
    List<BoundaryCrossing> AdvanceGameTime(RealmClockModel clock, CalendarTemplateModel calendar, long gameSeconds);

    /// <summary>
    /// Resolves the day period for a given game hour based on the calendar template's
    /// day period definitions.
    /// </summary>
    /// <param name="calendar">The calendar template with day period definitions.</param>
    /// <param name="hour">The game hour to resolve (0-based).</param>
    /// <returns>A tuple of (period code, isDaylight) for the matching day period.</returns>
    (string code, bool isDaylight) ResolveDayPeriod(CalendarTemplateModel calendar, int hour);

    /// <summary>
    /// Resolves the season for a given month index based on the calendar template's
    /// month-to-season mapping.
    /// </summary>
    /// <param name="calendar">The calendar template with month and season definitions.</param>
    /// <param name="monthIndex">The month index (0-based) to resolve.</param>
    /// <returns>A tuple of (season code, season index) for the month's season.</returns>
    (string code, int index) ResolveSeason(CalendarTemplateModel calendar, int monthIndex);

    /// <summary>
    /// Computes the total elapsed game-seconds between two real-world timestamps
    /// using piecewise integration across time ratio segments.
    /// </summary>
    /// <param name="segments">The ordered list of ratio segments (each with start time and ratio).</param>
    /// <param name="from">The start of the real-world time range.</param>
    /// <param name="to">The end of the real-world time range.</param>
    /// <returns>Total game-seconds elapsed between from and to.</returns>
    long ComputeElapsedGameTime(List<TimeRatioSegmentEntry> segments, DateTimeOffset from, DateTimeOffset to);

    /// <summary>
    /// Decomposes a total game-seconds value into calendar units (days, hours, minutes)
    /// based on the calendar template's hours-per-day configuration.
    /// </summary>
    /// <param name="calendar">The calendar template defining hours per day.</param>
    /// <param name="gameSeconds">Total game-seconds to decompose.</param>
    /// <returns>A tuple of (days, hours, minutes) representing the decomposed time.</returns>
    (int days, int hours, int minutes) DecomposeGameSeconds(CalendarTemplateModel calendar, long gameSeconds);
}

/// <summary>
/// Represents a boundary crossing detected during game time advancement.
/// Used to determine which boundary events to publish.
/// Uses typed properties per IMPLEMENTATION TENETS: int values for numeric boundaries
/// (hour, day, year) and string values for code-based boundaries (period, month, season).
/// </summary>
internal class BoundaryCrossing
{
    /// <summary>The type of boundary that was crossed.</summary>
    public BoundaryType Type { get; set; }

    /// <summary>The previous numeric value before the boundary crossing (hour, day-of-month, year).</summary>
    public int? PreviousIntValue { get; set; }

    /// <summary>The new numeric value after the boundary crossing (hour, day-of-month, year).</summary>
    public int? NewIntValue { get; set; }

    /// <summary>The previous string code before the boundary crossing (period, month, season).</summary>
    public string? PreviousStringValue { get; set; }

    /// <summary>The new string code after the boundary crossing (period, month, season).</summary>
    public string? NewStringValue { get; set; }

    /// <summary>
    /// Number of units crossed (for day, month, year boundaries during catch-up scenarios).
    /// Always 1 for hour and period boundaries.
    /// </summary>
    public int CountCrossed { get; set; } = 1;
}

/// <summary>
/// Types of temporal boundaries that can be crossed during game time advancement.
/// </summary>
internal enum BoundaryType
{
    /// <summary>An hour boundary was crossed.</summary>
    Hour,

    /// <summary>A day period boundary was crossed (e.g., dawn to morning).</summary>
    Period,

    /// <summary>A day boundary was crossed.</summary>
    Day,

    /// <summary>A month boundary was crossed.</summary>
    Month,

    /// <summary>A season boundary was crossed.</summary>
    Season,

    /// <summary>A year boundary was crossed.</summary>
    Year
}
