namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Pure synchronous calendar math helper for game time calculations.
/// Performs all time advancement, period/season resolution, elapsed time computation,
/// and game-seconds decomposition without any I/O or async operations.
/// </summary>
internal class WorldstateTimeCalculator : IWorldstateTimeCalculator
{
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
        foreach (var period in calendar.DayPeriods)
        {
            if (hour >= period.StartHour && hour < period.EndHour)
            {
                return (period.Code, period.IsDaylight);
            }
        }

        // Calendars are validated before storage — if no period matches, data is corrupt.
        throw new InvalidOperationException(
            $"No day period covers hour {hour} in calendar template '{calendar.TemplateCode}'. This indicates corrupted calendar data.");
    }

    /// <inheritdoc />
    public (string code, int index) ResolveSeason(CalendarTemplateModel calendar, int monthIndex)
    {
        if (monthIndex < 0 || monthIndex >= calendar.Months.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monthIndex),
                $"Month index {monthIndex} is out of range for calendar template '{calendar.TemplateCode}' with {calendar.Months.Count} months.");
        }

        var month = calendar.Months[monthIndex];
        var seasonCode = month.SeasonCode;

        for (var i = 0; i < calendar.Seasons.Count; i++)
        {
            if (calendar.Seasons[i].Code == seasonCode)
            {
                return (seasonCode, calendar.Seasons[i].Ordinal);
            }
        }

        // Calendars are validated before storage — if no season matches, data is corrupt.
        throw new InvalidOperationException(
            $"Month at index {monthIndex} references season code '{seasonCode}' which does not exist in calendar template '{calendar.TemplateCode}'. This indicates corrupted calendar data.");
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
    private static void UpdateSeasonProgress(RealmClockModel clock, CalendarTemplateModel calendar)
    {
        var currentSeasonCode = clock.Season;

        // Count total days in this season and days elapsed within it
        var totalDaysInSeason = 0;
        var daysElapsedInSeason = 0;

        for (var i = 0; i < calendar.Months.Count; i++)
        {
            var month = calendar.Months[i];
            if (month.SeasonCode != currentSeasonCode)
            {
                continue;
            }

            // This month belongs to the current season
            totalDaysInSeason += month.DaysInMonth;

            if (i < clock.MonthIndex)
            {
                // Fully elapsed month in this season before the current month
                daysElapsedInSeason += month.DaysInMonth;
            }
            else if (i == clock.MonthIndex)
            {
                // Current month: count days elapsed within it (day 1 = 0 days elapsed)
                daysElapsedInSeason += clock.DayOfMonth - 1;
            }
            // Months after the current month in this season contribute to totalDaysInSeason
            // but not to daysElapsedInSeason
        }

        clock.SeasonProgress = totalDaysInSeason > 0
            ? (float)daysElapsedInSeason / totalDaysInSeason
            : 0.0f;
    }
}
