namespace BeyondImmersion.BannouService.Matchmaking.Helpers;

/// <summary>
/// Pure algorithmic functions for matchmaking.
/// Extracted from MatchmakingService for improved testability.
/// </summary>
internal interface IMatchmakingAlgorithm
{
    /// <summary>
    /// Tries to match tickets based on queue rules.
    /// </summary>
    /// <param name="tickets">List of available tickets to match.</param>
    /// <param name="queue">Queue configuration defining matching rules.</param>
    /// <param name="skillRange">Optional skill range restriction.</param>
    /// <returns>List of matched tickets, or null if no valid match found.</returns>
    List<TicketModel>? TryMatchTickets(List<TicketModel> tickets, QueueModel queue, int? skillRange);

    /// <summary>
    /// Checks if a ticket matches a query string.
    /// Supports simple key:value matching (e.g., "region:na", "skill:>1000").
    /// </summary>
    /// <param name="ticket">The ticket to check.</param>
    /// <param name="query">The query string to match against.</param>
    /// <returns>True if ticket matches query, false otherwise.</returns>
    bool MatchesQuery(TicketModel ticket, string query);

    /// <summary>
    /// Gets the current skill range based on queue configuration and intervals elapsed.
    /// </summary>
    /// <param name="queue">Queue configuration with skill expansion steps.</param>
    /// <param name="intervalsElapsed">Number of intervals since ticket creation.</param>
    /// <returns>Skill range, or null if unlimited.</returns>
    int? GetCurrentSkillRange(QueueModel? queue, int intervalsElapsed);
}
