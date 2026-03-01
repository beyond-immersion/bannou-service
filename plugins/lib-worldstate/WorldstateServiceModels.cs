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
