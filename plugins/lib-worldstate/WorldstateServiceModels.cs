using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Internal data models for WorldstateService.
/// </summary>
/// <remarks>
/// <para>
/// Storage models for Redis and MySQL state stores. These map to the state store
/// schema defined in schemas/state-stores.yaml. Not exposed via the API.
/// </para>
/// </remarks>
public partial class WorldstateService
{
    // Partial class declaration signals model ownership.
}

/// <summary>
/// Internal storage model for a realm's current clock state.
/// Stored in worldstate-realm-clock (Redis).
/// Key pattern: realm:{realmId}
/// </summary>
internal class RealmClockModel
{
    /// <summary>Realm this clock belongs to.</summary>
    public Guid RealmId { get; set; }

    /// <summary>Game service this realm is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Calendar template code assigned to this realm.</summary>
    public string CalendarTemplateCode { get; set; } = string.Empty;

    /// <summary>Current game year.</summary>
    public int Year { get; set; }

    /// <summary>Current month index (0-based) within the year.</summary>
    public int MonthIndex { get; set; }

    /// <summary>Current month code (e.g., "frostmere").</summary>
    public string MonthCode { get; set; } = string.Empty;

    /// <summary>Current day of the month (1-based).</summary>
    public int DayOfMonth { get; set; }

    /// <summary>Current day of the year (1-based).</summary>
    public int DayOfYear { get; set; }

    /// <summary>Current game hour (0-based).</summary>
    public int Hour { get; set; }

    /// <summary>Current game minute (0-based).</summary>
    public int Minute { get; set; }

    /// <summary>Current day period code (e.g., "dawn", "night").</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Whether the current period is daylight.</summary>
    public bool IsDaylight { get; set; }

    /// <summary>Current season code (e.g., "winter").</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>Current season index (0-based) within the year.</summary>
    public int SeasonIndex { get; set; }

    /// <summary>Progress through the current season (0.0 to 1.0).</summary>
    public float SeasonProgress { get; set; }

    /// <summary>Total game-seconds elapsed since the realm epoch.</summary>
    public long TotalGameSecondsSinceEpoch { get; set; }

    /// <summary>Current game-seconds per real-second ratio.</summary>
    public float TimeRatio { get; set; }

    /// <summary>Real-world timestamp of the last clock advancement.</summary>
    public DateTimeOffset LastAdvancedRealTime { get; set; }

    /// <summary>Real-world timestamp marking the start of this realm's game time.</summary>
    public DateTimeOffset RealmEpoch { get; set; }

    /// <summary>Downtime handling policy for this realm.</summary>
    public DowntimePolicy DowntimePolicy { get; set; }

    /// <summary>Whether this realm clock is currently active (ratio > 0).</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Internal storage model for calendar template definitions.
/// Stored in worldstate-calendar (MySQL).
/// Key pattern: calendar:{gameServiceId}:{templateCode}
/// </summary>
internal class CalendarTemplateModel
{
    /// <summary>Unique template code within the game service.</summary>
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>Game service this calendar template belongs to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Number of game hours in one game day.</summary>
    public int GameHoursPerDay { get; set; }

    /// <summary>Ordered list of day period definitions.</summary>
    public List<DayPeriodDefinition> DayPeriods { get; set; } = new();

    /// <summary>Ordered list of month definitions.</summary>
    public List<MonthDefinition> Months { get; set; } = new();

    /// <summary>Ordered list of season definitions.</summary>
    public List<SeasonDefinition> Seasons { get; set; } = new();

    /// <summary>Total number of game days in one game year (computed from month day counts).</summary>
    public int DaysPerYear { get; set; }

    /// <summary>Total number of months in one game year.</summary>
    public int MonthsPerYear { get; set; }

    /// <summary>Total number of seasons in one game year.</summary>
    public int SeasonsPerYear { get; set; }

    /// <summary>Optional era label definitions for year-range naming.</summary>
    public List<EraLabel>? EraLabels { get; set; }
}

/// <summary>
/// Internal storage model for per-realm time ratio change history.
/// Stored in worldstate-ratio-history (MySQL).
/// Key pattern: ratio:{realmId}
/// </summary>
internal class TimeRatioHistoryModel
{
    /// <summary>Realm this ratio history belongs to.</summary>
    public Guid RealmId { get; set; }

    /// <summary>Ordered list of ratio segments (append-only, old segments never modified).</summary>
    public List<TimeRatioSegmentEntry> Segments { get; set; } = new();
}

/// <summary>
/// A single time ratio segment in a realm's ratio history.
/// Records when a ratio was active and why it was set.
/// </summary>
internal class TimeRatioSegmentEntry
{
    /// <summary>Real-world timestamp when this segment started.</summary>
    public DateTimeOffset SegmentStartRealTime { get; set; }

    /// <summary>Game-seconds per real-second during this segment.</summary>
    public float TimeRatio { get; set; }

    /// <summary>Why the time ratio was changed to this value.</summary>
    public TimeRatioChangeReason Reason { get; set; }
}

/// <summary>
/// Durable per-realm worldstate configuration.
/// Stored in worldstate-calendar (MySQL).
/// Key pattern: realm-config:{realmId}
/// </summary>
internal class RealmWorldstateConfigModel
{
    /// <summary>Realm this configuration belongs to.</summary>
    public Guid RealmId { get; set; }

    /// <summary>Game service this realm is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Calendar template code assigned to this realm.</summary>
    public string CalendarTemplateCode { get; set; } = string.Empty;

    /// <summary>Game-seconds per real-second ratio for this realm.</summary>
    public float TimeRatio { get; set; }

    /// <summary>Downtime handling policy.</summary>
    public DowntimePolicy DowntimePolicy { get; set; }

    /// <summary>Real-world timestamp marking the start of this realm's game time.</summary>
    public DateTimeOffset RealmEpoch { get; set; }
}

/// <summary>
/// Static helper for publishing boundary events from both the service and the worker.
/// Extracted to eliminate duplication between WorldstateService.PublishBoundaryEventsAsync
/// and WorldstateClockWorkerService.PublishWorkerBoundaryEventsAsync.
/// </summary>
internal static class WorldstateBoundaryEventPublisher
{
    /// <summary>
    /// Publishes boundary events (hour, period, day, month, season, year) based on the
    /// list of boundary crossings detected during clock advancement. Int boundary values
    /// (hour, day, year) throw on null per IMPLEMENTATION TENETS (no sentinel values).
    /// </summary>
    /// <param name="realmId">The realm whose clock crossed boundaries.</param>
    /// <param name="snapshot">The current game time snapshot after advancement.</param>
    /// <param name="clock">The current clock state (for year on season events).</param>
    /// <param name="boundaries">The list of boundary crossings to publish events for.</param>
    /// <param name="isCatchUp">Whether these boundaries were crossed during catch-up processing.</param>
    /// <param name="messageBus">The message bus for event publishing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task PublishBoundaryEventsAsync(
        Guid realmId,
        GameTimeSnapshot snapshot,
        RealmClockModel clock,
        List<BoundaryCrossing> boundaries,
        bool isCatchUp,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        foreach (var boundary in boundaries)
        {
            switch (boundary.Type)
            {
                case BoundaryType.Hour:
                    await messageBus.TryPublishAsync("worldstate.hour-changed", new WorldstateHourChangedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        PreviousHour = boundary.PreviousIntValue
                            ?? throw new InvalidOperationException("Hour boundary crossing must have PreviousIntValue set"),
                        CurrentHour = boundary.NewIntValue
                            ?? throw new InvalidOperationException("Hour boundary crossing must have NewIntValue set"),
                        CurrentGameTime = snapshot
                    }, cancellationToken: cancellationToken);
                    break;

                case BoundaryType.Period:
                    await messageBus.TryPublishAsync("worldstate.period-changed", new WorldstatePeriodChangedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        PreviousPeriod = boundary.PreviousStringValue
                            ?? throw new InvalidOperationException("Period boundary crossing must have PreviousStringValue set"),
                        CurrentPeriod = boundary.NewStringValue
                            ?? throw new InvalidOperationException("Period boundary crossing must have NewStringValue set"),
                        CurrentGameTime = snapshot
                    }, cancellationToken: cancellationToken);
                    break;

                case BoundaryType.Day:
                    await messageBus.TryPublishAsync("worldstate.day-changed", new WorldstateDayChangedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        PreviousDay = boundary.PreviousIntValue
                            ?? throw new InvalidOperationException("Day boundary crossing must have PreviousIntValue set"),
                        CurrentDay = boundary.NewIntValue
                            ?? throw new InvalidOperationException("Day boundary crossing must have NewIntValue set"),
                        DaysCrossed = boundary.CountCrossed,
                        IsCatchUp = isCatchUp,
                        CurrentGameTime = snapshot
                    }, cancellationToken: cancellationToken);
                    break;

                case BoundaryType.Month:
                    await messageBus.TryPublishAsync("worldstate.month-changed", new WorldstateMonthChangedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        PreviousMonth = boundary.PreviousStringValue
                            ?? throw new InvalidOperationException("Month boundary crossing must have PreviousStringValue set"),
                        CurrentMonth = boundary.NewStringValue
                            ?? throw new InvalidOperationException("Month boundary crossing must have NewStringValue set"),
                        MonthsCrossed = boundary.CountCrossed,
                        IsCatchUp = isCatchUp,
                        CurrentGameTime = snapshot
                    }, cancellationToken: cancellationToken);
                    break;

                case BoundaryType.Season:
                    await messageBus.TryPublishAsync("worldstate.season-changed", new WorldstateSeasonChangedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        PreviousSeason = boundary.PreviousStringValue
                            ?? throw new InvalidOperationException("Season boundary crossing must have PreviousStringValue set"),
                        CurrentSeason = boundary.NewStringValue
                            ?? throw new InvalidOperationException("Season boundary crossing must have NewStringValue set"),
                        CurrentYear = clock.Year,
                        IsCatchUp = isCatchUp,
                        CurrentGameTime = snapshot
                    }, cancellationToken: cancellationToken);
                    break;

                case BoundaryType.Year:
                    await messageBus.TryPublishAsync("worldstate.year-changed", new WorldstateYearChangedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        PreviousYear = boundary.PreviousIntValue
                            ?? throw new InvalidOperationException("Year boundary crossing must have PreviousIntValue set"),
                        CurrentYear = boundary.NewIntValue
                            ?? throw new InvalidOperationException("Year boundary crossing must have NewIntValue set"),
                        YearsCrossed = boundary.CountCrossed,
                        IsCatchUp = isCatchUp,
                        CurrentGameTime = snapshot
                    }, cancellationToken: cancellationToken);
                    break;
            }
        }
    }

    /// <summary>
    /// Maps a clock storage model to the API game time snapshot response type.
    /// Shared between service and worker for consistent snapshot creation.
    /// </summary>
    /// <param name="clock">The internal clock storage model.</param>
    /// <returns>The game time snapshot response.</returns>
    internal static GameTimeSnapshot MapClockToSnapshot(RealmClockModel clock)
    {
        return new GameTimeSnapshot
        {
            RealmId = clock.RealmId,
            GameServiceId = clock.GameServiceId,
            Year = clock.Year,
            MonthIndex = clock.MonthIndex,
            MonthCode = clock.MonthCode,
            DayOfMonth = clock.DayOfMonth,
            DayOfYear = clock.DayOfYear,
            Hour = clock.Hour,
            Minute = clock.Minute,
            Period = clock.Period,
            IsDaylight = clock.IsDaylight,
            Season = clock.Season,
            SeasonIndex = clock.SeasonIndex,
            SeasonProgress = clock.SeasonProgress,
            TotalGameSecondsSinceEpoch = clock.TotalGameSecondsSinceEpoch,
            TimeRatio = clock.TimeRatio,
            Timestamp = clock.LastAdvancedRealTime
        };
    }
}
