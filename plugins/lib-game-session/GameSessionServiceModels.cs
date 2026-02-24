using BeyondImmersion.Bannou.Core;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Internal data models for GameSessionService.
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
public partial class GameSessionService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal model for storing game session data in the state store.
/// Separates storage from API response format.
/// </summary>
internal class GameSessionModel
{
    public Guid SessionId { get; set; }
    public string GameType { get; set; } = "generic";
    public string? SessionName { get; set; }
    public SessionStatus Status { get; set; }
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }
    public bool IsPrivate { get; set; }
    public Guid? Owner { get; set; }
    public List<GamePlayer> Players { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public object? GameSettings { get; set; }

    /// <summary>
    /// Type of session - lobby (persistent) or matchmade (time-limited with reservations).
    /// </summary>
    public SessionType SessionType { get; set; } = SessionType.Lobby;

    /// <summary>
    /// For matchmade sessions - list of player reservations.
    /// </summary>
    public List<ReservationModel> Reservations { get; set; } = new();

    /// <summary>
    /// For matchmade sessions - when reservations expire.
    /// </summary>
    public DateTimeOffset? ReservationExpiresAt { get; set; }
}

/// <summary>
/// Internal model for storing reservation data.
/// </summary>
internal class ReservationModel
{
    /// <summary>
    /// Account ID this reservation is for.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// One-time use token for claiming this reservation.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// When this reservation was created.
    /// </summary>
    public DateTimeOffset ReservedAt { get; set; }

    /// <summary>
    /// Whether this reservation has been claimed.
    /// </summary>
    public bool Claimed { get; set; }

    /// <summary>
    /// When this reservation was claimed.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }
}

/// <summary>
/// Model for tracking subscriber sessions in distributed state.
/// Stores which WebSocket sessions are connected for each subscribed account.
/// </summary>
internal class SubscriberSessionsModel
{
    /// <summary>
    /// Account ID for this subscriber.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Set of active WebSocket session IDs for this account.
    /// </summary>
    public HashSet<Guid> SessionIds { get; set; } = new();

    /// <summary>
    /// When this record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Minimal session model for cleanup service.
/// Only includes fields needed for reservation cleanup.
/// </summary>
internal class CleanupSessionModel
{
    public Guid SessionId { get; set; }
    public SessionType SessionType { get; set; } = SessionType.Lobby;
    public List<CleanupReservationModel> Reservations { get; set; } = new();
    public DateTimeOffset? ReservationExpiresAt { get; set; }
    public List<CleanupPlayerModel> Players { get; set; } = new();
}

/// <summary>
/// Minimal reservation model for cleanup service.
/// </summary>
internal class CleanupReservationModel
{
    public Guid AccountId { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>
/// Minimal player model for cleanup service.
/// </summary>
internal class CleanupPlayerModel
{
    public Guid AccountId { get; set; }
    public Guid? WebSocketSessionId { get; set; }
}
