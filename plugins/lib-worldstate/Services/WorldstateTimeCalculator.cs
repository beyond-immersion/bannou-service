using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Precomputed lookup tables for a calendar template, built once on first use
/// and cached by template code. Eliminates repeated linear scans during
/// hour-by-hour clock advancement.
/// </summary>
internal class CalendarLookup
{
    /// <summary>Maps hour → (period code, isDaylight). Array indexed by hour (0..GameHoursPerDay-1).</summary>
    public (string Code, bool IsDaylight)[] HourToPeriod { get; }

    /// <summary>Maps month index → (season code, season ordinal).</summary>
    public (string SeasonCode, int SeasonOrdinal)[] MonthToSeason { get; }

    /// <summary>Maps season code → total days in that season.</summary>
    public Dictionary<string, int> TotalDaysPerSeason { get; }

    /// <summary>Maps season code → cumulative days elapsed before each month that belongs to that season.
    /// Key: season code, Value: list of (monthIndex, cumulativeDaysBefore) for months in that season.</summary>
    public Dictionary<string, List<(int MonthIndex, int CumulativeDaysBefore)>> SeasonMonthOffsets { get; }

    /// <summary>
    /// Builds precomputed lookup tables from a calendar template.
    /// </summary>
    public CalendarLookup(CalendarTemplateModel calendar)
    {
        // Hour → Period lookup (array indexed by hour)
        HourToPeriod = new (string, bool)[calendar.GameHoursPerDay];
        foreach (var period in calendar.DayPeriods)
        {
            for (var h = period.StartHour; h < period.EndHour && h < calendar.GameHoursPerDay; h++)
            {
                HourToPeriod[h] = (period.Code, period.IsDaylight);
            }
        }

        // Month → Season lookup (array indexed by month index)
        MonthToSeason = new (string, int)[calendar.Months.Count];
        var seasonOrdinalMap = new Dictionary<string, int>(calendar.Seasons.Count);
        foreach (var season in calendar.Seasons)
        {
            seasonOrdinalMap[season.Code] = season.Ordinal;
        }

        for (var i = 0; i < calendar.Months.Count; i++)
        {
            var seasonCode = calendar.Months[i].SeasonCode;
            MonthToSeason[i] = (seasonCode, seasonOrdinalMap.GetValueOrDefault(seasonCode, 0));
        }

        // Season totals and month offsets for season progress computation
        TotalDaysPerSeason = new Dictionary<string, int>();
        SeasonMonthOffsets = new Dictionary<string, List<(int, int)>>();

        foreach (var season in calendar.Seasons)
        {
            TotalDaysPerSeason[season.Code] = 0;
            SeasonMonthOffsets[season.Code] = new List<(int, int)>();
        }

        for (var i = 0; i < calendar.Months.Count; i++)
        {
            var month = calendar.Months[i];
            var sc = month.SeasonCode;
            if (!TotalDaysPerSeason.ContainsKey(sc))
            {
                continue;
            }

            SeasonMonthOffsets[sc].Add((i, TotalDaysPerSeason[sc]));
            TotalDaysPerSeason[sc] += month.DaysInMonth;
        }
    }
}

/// <summary>
/// Pure synchronous calendar math helper for game time calculations.
/// Performs all time advancement, period/season resolution, elapsed time computation,
/// and game-seconds decomposition without any I/O or async operations.
/// Caches precomputed lookup tables per calendar template for O(1) period/season resolution.
/// </summary>
internal class WorldstateTimeCalculator : IWorldstateTimeCalculator
{
    private readonly ConcurrentDictionary<string, CalendarLookup> _lookupCache = new();

    /// <summary>
    /// Gets or creates the precomputed lookup tables for a calendar template.
    /// Thread-safe via ConcurrentDictionary; the factory may run multiple times
    /// for the same key under contention but all produce identical results.
    /// </summary>
    private CalendarLookup GetLookup(CalendarTemplateModel calendar)
    {
        var cacheKey = $"{calendar.GameServiceId}:{calendar.TemplateCode}";
        return _lookupCache.GetOrAdd(cacheKey, _ => new CalendarLookup(calendar));
    }
    /// <inheritdoc />
    public List<BoundaryCrossing> AdvanceGameTime(RealmClockModel clock, CalendarTemplateModel calendar, long gameSeconds)
    {
        if (gameSeconds <= 0)
        {
            return new List<BoundaryCrossing>();
        }

        var boundaries = new List<BoundaryCrossing>();
        var minutesPerHour = 60;
        var secondsPerMinute = 60;

        // Decompose game-seconds into whole minutes (sub-minute seconds are lost at clock
        // display granularity but preserved in TotalGameSecondsSinceEpoch for precise queries)
        var totalWholeMinutes = gameSeconds / secondsPerMinute;

        // Add whole minutes to current minute and compute carry-over to hours
        var newMinute = clock.Minute + (int)(totalWholeMinutes % minutesPerHour);
        var totalHoursToAdd = totalWholeMinutes / minutesPerHour;
        if (newMinute >= minutesPerHour)
        {
            totalHoursToAdd++;
            newMinute -= minutesPerHour;
        }

        clock.Minute = newMinute;

        // Advance by whole hours (detects all hour/period/day/month/season/year boundaries)
        if (totalHoursToAdd > 0)
        {
            AdvanceByHours(clock, calendar, totalHoursToAdd, boundaries);
        }

        // Update total game-seconds since epoch (precise, not rounded to minutes)
        clock.TotalGameSecondsSinceEpoch += gameSeconds;

        return boundaries;
    }

    /// <inheritdoc />
    public (string code, bool isDaylight) ResolveDayPeriod(CalendarTemplateModel calendar, int hour)
    {
        var lookup = GetLookup(calendar);

        if (hour < 0 || hour >= lookup.HourToPeriod.Length)
        {
            throw new InvalidOperationException(
                $"No day period covers hour {hour} in calendar template '{calendar.TemplateCode}'. This indicates corrupted calendar data.");
        }

        var (code, isDaylight) = lookup.HourToPeriod[hour];
        if (code == null)
        {
            // Slot was never populated — gap in DayPeriod definitions
            throw new InvalidOperationException(
                $"No day period covers hour {hour} in calendar template '{calendar.TemplateCode}'. This indicates corrupted calendar data.");
        }

        return (code, isDaylight);
    }

    /// <inheritdoc />
    public (string code, int index) ResolveSeason(CalendarTemplateModel calendar, int monthIndex)
    {
        var lookup = GetLookup(calendar);

        if (monthIndex < 0 || monthIndex >= lookup.MonthToSeason.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monthIndex),
                $"Month index {monthIndex} is out of range for calendar template '{calendar.TemplateCode}' with {calendar.Months.Count} months.");
        }

        return (lookup.MonthToSeason[monthIndex].SeasonCode, lookup.MonthToSeason[monthIndex].SeasonOrdinal);
    }

    /// <inheritdoc />
    public long ComputeElapsedGameTime(List<TimeRatioSegmentEntry> segments, DateTimeOffset from, DateTimeOffset to)
    {
        if (segments.Count == 0 || from >= to)
        {
            return 0;
        }

        var totalGameSeconds = 0.0;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            // Determine the real-time end of this segment (start of next segment, or 'to')
            var segmentEnd = (i + 1 < segments.Count)
                ? segments[i + 1].SegmentStartRealTime
                : to;

            // Clamp segment to [from, to] range
            var effectiveStart = segment.SegmentStartRealTime < from ? from : segment.SegmentStartRealTime;
            var effectiveEnd = segmentEnd > to ? to : segmentEnd;

            // Skip if this segment doesn't overlap [from, to]
            if (effectiveStart >= effectiveEnd)
            {
                continue;
            }

            // Integrate: game-seconds = real-seconds * ratio
            var realSeconds = (effectiveEnd - effectiveStart).TotalSeconds;
            totalGameSeconds += realSeconds * segment.TimeRatio;
        }

        return (long)totalGameSeconds;
    }

    /// <inheritdoc />
    public (int days, int hours, int minutes) DecomposeGameSeconds(CalendarTemplateModel calendar, long gameSeconds)
    {
        var gameMinutesPerHour = 60;
        var gameSecondsPerMinute = 60;
        var gameSecondsPerHour = gameMinutesPerHour * gameSecondsPerMinute;
        var gameSecondsPerDay = (long)calendar.GameHoursPerDay * gameSecondsPerHour;

        var days = (int)(gameSeconds / gameSecondsPerDay);
        var remainder = gameSeconds % gameSecondsPerDay;
        var hours = (int)(remainder / gameSecondsPerHour);
        remainder %= gameSecondsPerHour;
        var minutes = (int)(remainder / gameSecondsPerMinute);

        return (days, hours, minutes);
    }

    /// <summary>
    /// Advances the clock by the specified number of whole game hours, detecting
    /// and recording all boundary crossings (hour, period, day, month, season, year).
    /// </summary>
    /// <param name="clock">The clock state to advance (modified in-place).</param>
    /// <param name="calendar">The calendar template defining time structure.</param>
    /// <param name="hoursToAdvance">Total number of whole hours to advance.</param>
    /// <param name="boundaries">List to append boundary crossings to.</param>
    private void AdvanceByHours(
        RealmClockModel clock,
        CalendarTemplateModel calendar,
        long hoursToAdvance,
        List<BoundaryCrossing> boundaries)
    {
        var daysCrossed = 0;
        var monthsCrossed = 0;
        var yearsCrossed = 0;
        var previousDayOfMonth = clock.DayOfMonth;
        var previousMonthCode = clock.MonthCode;
        var previousSeason = clock.Season;
        var previousYear = clock.Year;

        for (long h = 0; h < hoursToAdvance; h++)
        {
            var hourBefore = clock.Hour;
            clock.Hour++;

            if (clock.Hour >= calendar.GameHoursPerDay)
            {
                // Day boundary crossed
                clock.Hour = 0;
                daysCrossed++;
                AdvanceDay(clock, calendar, ref monthsCrossed, ref yearsCrossed);
            }

            // Detect hour boundary
            boundaries.Add(new BoundaryCrossing
            {
                Type = BoundaryType.Hour,
                PreviousIntValue = hourBefore,
                NewIntValue = clock.Hour
            });

            // Detect period boundary
            var (newPeriodCode, newIsDaylight) = ResolveDayPeriod(calendar, clock.Hour);
            if (newPeriodCode != clock.Period)
            {
                boundaries.Add(new BoundaryCrossing
                {
                    Type = BoundaryType.Period,
                    PreviousStringValue = clock.Period,
                    NewStringValue = newPeriodCode
                });
                clock.Period = newPeriodCode;
                clock.IsDaylight = newIsDaylight;
            }
        }

        // Add aggregate day/month/season/year boundaries
        if (daysCrossed > 0)
        {
            boundaries.Add(new BoundaryCrossing
            {
                Type = BoundaryType.Day,
                PreviousIntValue = previousDayOfMonth,
                NewIntValue = clock.DayOfMonth,
                CountCrossed = daysCrossed
            });
        }

        if (monthsCrossed > 0)
        {
            boundaries.Add(new BoundaryCrossing
            {
                Type = BoundaryType.Month,
                PreviousStringValue = previousMonthCode,
                NewStringValue = clock.MonthCode,
                CountCrossed = monthsCrossed
            });
        }

        if (clock.Season != previousSeason)
        {
            boundaries.Add(new BoundaryCrossing
            {
                Type = BoundaryType.Season,
                PreviousStringValue = previousSeason,
                NewStringValue = clock.Season
            });
        }

        if (yearsCrossed > 0)
        {
            boundaries.Add(new BoundaryCrossing
            {
                Type = BoundaryType.Year,
                PreviousIntValue = previousYear,
                NewIntValue = clock.Year,
                CountCrossed = yearsCrossed
            });
        }

        // Update season progress
        UpdateSeasonProgress(clock, calendar);
    }

    /// <summary>
    /// Advances the clock by one day, handling month, season, and year rollovers.
    /// </summary>
    /// <param name="clock">The clock state to advance (modified in-place).</param>
    /// <param name="calendar">The calendar template defining month/year structure.</param>
    /// <param name="monthsCrossed">Running count of months crossed (incremented on rollover).</param>
    /// <param name="yearsCrossed">Running count of years crossed (incremented on rollover).</param>
    private void AdvanceDay(
        RealmClockModel clock,
        CalendarTemplateModel calendar,
        ref int monthsCrossed,
        ref int yearsCrossed)
    {
        clock.DayOfMonth++;
        clock.DayOfYear++;

        var currentMonth = calendar.Months[clock.MonthIndex];
        if (clock.DayOfMonth > currentMonth.DaysInMonth)
        {
            // Month boundary crossed
            clock.DayOfMonth = 1;
            clock.MonthIndex++;
            monthsCrossed++;

            if (clock.MonthIndex >= calendar.MonthsPerYear)
            {
                // Year boundary crossed
                clock.MonthIndex = 0;
                clock.DayOfYear = 1;
                clock.Year++;
                yearsCrossed++;
            }

            // Update month-derived fields
            clock.MonthCode = calendar.Months[clock.MonthIndex].Code;

            // Update season
            var (seasonCode, seasonIndex) = ResolveSeason(calendar, clock.MonthIndex);
            clock.Season = seasonCode;
            clock.SeasonIndex = seasonIndex;
        }
    }

    /// <summary>
    /// Recomputes the season progress (0.0 to 1.0) based on the current day within
    /// the current season's month span.
    /// </summary>
    /// <param name="clock">The clock state to update (modified in-place).</param>
    /// <param name="calendar">The calendar template defining season/month structure.</param>
    private void UpdateSeasonProgress(RealmClockModel clock, CalendarTemplateModel calendar)
    {
        var lookup = GetLookup(calendar);
        var currentSeasonCode = clock.Season;

        if (!lookup.TotalDaysPerSeason.TryGetValue(currentSeasonCode, out var totalDaysInSeason)
            || totalDaysInSeason <= 0)
        {
            clock.SeasonProgress = 0.0f;
            return;
        }

        // Compute days elapsed using precomputed month offsets for this season
        var daysElapsedInSeason = 0;
        var offsets = lookup.SeasonMonthOffsets[currentSeasonCode];
        foreach (var (monthIndex, cumulativeDaysBefore) in offsets)
        {
            if (monthIndex < clock.MonthIndex)
            {
                // Fully elapsed month — use its cumulative offset + its full day count
                // (cumulativeDaysBefore of the NEXT entry would give us this, but we can
                // also compute it as the difference. Simpler: just use the calendar data.)
                daysElapsedInSeason = cumulativeDaysBefore + calendar.Months[monthIndex].DaysInMonth;
            }
            else if (monthIndex == clock.MonthIndex)
            {
                // Current month: cumulative days before it + days elapsed within it
                daysElapsedInSeason = cumulativeDaysBefore + (clock.DayOfMonth - 1);
                break;
            }
            else
            {
                // Past the current month — no more elapsed days
                break;
            }
        }

        clock.SeasonProgress = (float)daysElapsedInSeason / totalDaysInSeason;
    }
}
