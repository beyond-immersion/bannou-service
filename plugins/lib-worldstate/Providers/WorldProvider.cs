using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Worldstate.Providers;

/// <summary>
/// Provides game time, calendar, and season data for ABML expressions
/// via the <c>${world.*}</c> variable namespace.
/// </summary>
/// <remarks>
/// <para>Variables available:</para>
/// <list type="bullet">
///   <item><description><c>${world.time.hour}</c> - int: Current game hour (0-based)</description></item>
///   <item><description><c>${world.time.minute}</c> - int: Current game minute (0-based)</description></item>
///   <item><description><c>${world.time.period}</c> - string: Current day period code (e.g., "dawn", "night")</description></item>
///   <item><description><c>${world.time.is_day}</c> - bool: Whether the current period is daylight</description></item>
///   <item><description><c>${world.time.is_night}</c> - bool: Whether the current period is not daylight</description></item>
///   <item><description><c>${world.day}</c> - int: Current day of month (1-based)</description></item>
///   <item><description><c>${world.day_of_year}</c> - int: Current day of year (1-based)</description></item>
///   <item><description><c>${world.month}</c> - string: Current month code</description></item>
///   <item><description><c>${world.month_index}</c> - int: Current month index (0-based)</description></item>
///   <item><description><c>${world.season}</c> - string: Current season code</description></item>
///   <item><description><c>${world.season_index}</c> - int: Current season index (0-based)</description></item>
///   <item><description><c>${world.year}</c> - int: Current game year</description></item>
///   <item><description><c>${world.season_progress}</c> - float: Progress through current season (0.0 to 1.0)</description></item>
///   <item><description><c>${world.year_progress}</c> - float: Progress through current year (0.0 to 1.0)</description></item>
///   <item><description><c>${world.time_ratio}</c> - float: Current game-seconds per real-second ratio</description></item>
/// </list>
/// </remarks>
public sealed class WorldProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider instance for realms with no initialized clock.
    /// Returns null for all variable paths.
    /// </summary>
    public static WorldProvider Empty { get; } = new(null, null);

    private readonly RealmClockModel? _clock;
    private readonly CalendarTemplateModel? _calendar;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.World;

    /// <summary>
    /// Creates a new WorldProvider with the given clock and calendar data.
    /// Use <see cref="Empty"/> for realms without initialized clocks.
    /// </summary>
    /// <param name="clock">The realm clock state snapshot, or null for empty provider.</param>
    /// <param name="calendar">The calendar template, or null if unavailable.</param>
    internal WorldProvider(RealmClockModel? clock, CalendarTemplateModel? calendar)
    {
        _clock = clock;
        _calendar = calendar;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();
        if (_clock == null) return null;

        var segment = path[0];
        if (segment.Equals("time", StringComparison.OrdinalIgnoreCase))
            return path.Length > 1 ? GetTimeValue(path.Slice(1)) : GetTimeRoot();
        if (segment.Equals("day", StringComparison.OrdinalIgnoreCase))
            return _clock.DayOfMonth;
        if (segment.Equals("day_of_year", StringComparison.OrdinalIgnoreCase))
            return _clock.DayOfYear;
        if (segment.Equals("month", StringComparison.OrdinalIgnoreCase))
            return _clock.MonthCode;
        if (segment.Equals("month_index", StringComparison.OrdinalIgnoreCase))
            return _clock.MonthIndex;
        if (segment.Equals("season", StringComparison.OrdinalIgnoreCase))
            return _clock.Season;
        if (segment.Equals("season_index", StringComparison.OrdinalIgnoreCase))
            return _clock.SeasonIndex;
        if (segment.Equals("year", StringComparison.OrdinalIgnoreCase))
            return _clock.Year;
        if (segment.Equals("season_progress", StringComparison.OrdinalIgnoreCase))
            return _clock.SeasonProgress;
        if (segment.Equals("year_progress", StringComparison.OrdinalIgnoreCase))
            return ComputeYearProgress();
        if (segment.Equals("time_ratio", StringComparison.OrdinalIgnoreCase))
            return _clock.TimeRatio;

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        if (_clock == null) return null;

        return new Dictionary<string, object?>
        {
            ["time"] = GetTimeRoot(),
            ["day"] = _clock.DayOfMonth,
            ["day_of_year"] = _clock.DayOfYear,
            ["month"] = _clock.MonthCode,
            ["month_index"] = _clock.MonthIndex,
            ["season"] = _clock.Season,
            ["season_index"] = _clock.SeasonIndex,
            ["year"] = _clock.Year,
            ["season_progress"] = _clock.SeasonProgress,
            ["year_progress"] = ComputeYearProgress(),
            ["time_ratio"] = _clock.TimeRatio
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (_clock == null) return false;
        if (path.Length == 0) return true;

        var segment = path[0];
        if (segment.Equals("time", StringComparison.OrdinalIgnoreCase))
            return path.Length == 1 || (path.Length == 2 && IsValidTimePath(path[1]));
        if (segment.Equals("day", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("day_of_year", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("month", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("month_index", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("season", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("season_index", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("year", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("season_progress", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("year_progress", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("time_ratio", StringComparison.OrdinalIgnoreCase))
            return path.Length == 1;

        return false;
    }

    /// <summary>
    /// Gets a time sub-variable value.
    /// </summary>
    private object? GetTimeValue(ReadOnlySpan<string> path)
    {
        if (_clock == null || path.Length == 0) return null;

        var segment = path[0];
        if (segment.Equals("hour", StringComparison.OrdinalIgnoreCase))
            return _clock.Hour;
        if (segment.Equals("minute", StringComparison.OrdinalIgnoreCase))
            return _clock.Minute;
        if (segment.Equals("period", StringComparison.OrdinalIgnoreCase))
            return _clock.Period;
        if (segment.Equals("is_day", StringComparison.OrdinalIgnoreCase))
            return _clock.IsDaylight;
        if (segment.Equals("is_night", StringComparison.OrdinalIgnoreCase))
            return !_clock.IsDaylight;

        return null;
    }

    /// <summary>
    /// Gets all time sub-variables as a dictionary, or null if the clock is not initialized.
    /// </summary>
    private Dictionary<string, object?>? GetTimeRoot()
    {
        if (_clock == null)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["hour"] = _clock.Hour,
            ["minute"] = _clock.Minute,
            ["period"] = _clock.Period,
            ["is_day"] = _clock.IsDaylight,
            ["is_night"] = !_clock.IsDaylight
        };
    }

    /// <summary>
    /// Computes the year progress as a float from 0.0 to 1.0 based on the
    /// current day of year and total days per year from the calendar template.
    /// Returns null if clock or calendar data is unavailable (per IMPLEMENTATION TENETS: no sentinel values).
    /// </summary>
    private float? ComputeYearProgress()
    {
        if (_clock == null || _calendar == null || _calendar.DaysPerYear <= 0)
        {
            return null;
        }

        // DayOfYear is 1-based, so subtract 1 for 0-based progress
        return (float)(_clock.DayOfYear - 1) / _calendar.DaysPerYear;
    }

    /// <summary>
    /// Checks if a path segment is a valid time sub-path.
    /// </summary>
    private static bool IsValidTimePath(string segment)
    {
        return segment.Equals("hour", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("minute", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("period", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("is_day", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("is_night", StringComparison.OrdinalIgnoreCase);
    }
}
