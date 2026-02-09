namespace BeyondImmersion.BannouService.Matchmaking;

/// <summary>
/// Internal data models for MatchmakingService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class MatchmakingService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal model for queue storage.
/// </summary>
internal class QueueModel
{
    public string QueueId { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string SessionGameType { get; set; } = "generic";
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
    public int CountMultiple { get; set; } = 1;
    public int IntervalSeconds { get; set; } = 15;
    public int MaxIntervals { get; set; } = 6;
    public List<SkillExpansionStepModel>? SkillExpansion { get; set; }
    public PartySkillAggregation PartySkillAggregation { get; set; } = PartySkillAggregation.Average;
    public List<double>? PartySkillWeights { get; set; }
    public int? PartyMaxSize { get; set; }
    public bool AllowConcurrent { get; set; } = true;
    public string? ExclusiveGroup { get; set; }
    public bool UseSkillRating { get; set; } = true;
    public string? RatingCategory { get; set; }
    public bool StartWhenMinimumReached { get; set; }
    public bool RequiresRegistration { get; set; }
    public bool TournamentIdRequired { get; set; }
    public int MatchAcceptTimeoutSeconds { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Stats
    public double AverageWaitSeconds { get; set; }
    public double? MedianWaitSeconds { get; set; }
    public int MatchesFormedLastHour { get; set; }
    public double? TimeoutRatePercent { get; set; }
    public double? CancelRatePercent { get; set; }
}

internal class SkillExpansionStepModel
{
    public int Intervals { get; set; }
    public int? Range { get; set; }
}

/// <summary>
/// Internal model for ticket storage.
/// </summary>
internal class TicketModel
{
    public Guid TicketId { get; set; }
    public string QueueId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public Guid WebSocketSessionId { get; set; }
    public Guid? PartyId { get; set; }
    public List<PartyMemberModel>? PartyMembers { get; set; }
    public Dictionary<string, string> StringProperties { get; set; } = new();
    public Dictionary<string, double> NumericProperties { get; set; } = new();
    public string? Query { get; set; }
    public Guid? TournamentId { get; set; }
    public double? SkillRating { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Searching;
    public DateTimeOffset CreatedAt { get; set; }
    public int IntervalsElapsed { get; set; }
    public Guid? MatchId { get; set; }
}

internal class PartyMemberModel
{
    public Guid AccountId { get; set; }
    public Guid WebSocketSessionId { get; set; }
    public double? SkillRating { get; set; }
}

/// <summary>
/// Internal model for match storage.
/// </summary>
internal class MatchModel
{
    public Guid MatchId { get; set; }
    public string QueueId { get; set; } = string.Empty;
    public List<MatchedTicketModel> MatchedTickets { get; set; } = new();
    public int PlayerCount { get; set; }
    public double? AverageSkillRating { get; set; }
    public double? SkillRatingSpread { get; set; }
    public DateTimeOffset AcceptDeadline { get; set; }
    public List<Guid> AcceptedPlayers { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? GameSessionId { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Pending;
}

/// <summary>
/// Match lifecycle status.
/// </summary>
internal enum MatchStatus
{
    Pending,
    Accepted,
    Cancelled,
    Completed
}

internal class MatchedTicketModel
{
    public Guid TicketId { get; set; }
    public Guid AccountId { get; set; }
    public Guid WebSocketSessionId { get; set; }
    public Guid? PartyId { get; set; }
    public double? SkillRating { get; set; }
    public double WaitTimeSeconds { get; set; }
}

/// <summary>
/// Wrapper for pending match storage (value types can't be stored directly).
/// </summary>
internal class PendingMatchWrapper
{
    public Guid MatchId { get; set; }
}
