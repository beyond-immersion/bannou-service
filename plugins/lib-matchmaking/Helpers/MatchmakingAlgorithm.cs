namespace BeyondImmersion.BannouService.Matchmaking.Helpers;

/// <summary>
/// Pure algorithmic functions for matchmaking.
/// Extracted from MatchmakingService for improved testability.
/// </summary>
internal class MatchmakingAlgorithm : IMatchmakingAlgorithm
{
    /// <inheritdoc/>
    public List<TicketModel>? TryMatchTickets(List<TicketModel> tickets, QueueModel queue, int? skillRange)
    {
        if (tickets.Count < queue.MinCount)
            return null;

        // Sort by creation time (FIFO)
        var sortedTickets = tickets.OrderBy(t => t.CreatedAt).ToList();

        // Simple matching: take first N that fit within skill range
        var matched = new List<TicketModel>();
        TicketModel? anchor = null;

        foreach (var ticket in sortedTickets)
        {
            if (anchor == null)
            {
                anchor = ticket;
                matched.Add(ticket);
                continue;
            }

            // Check skill range
            if (skillRange.HasValue && queue.UseSkillRating &&
                anchor.SkillRating.HasValue && ticket.SkillRating.HasValue)
            {
                var diff = Math.Abs(anchor.SkillRating.Value - ticket.SkillRating.Value);
                if (diff > skillRange.Value)
                    continue;
            }

            // Check query match (simplified - full Lucene would need a parser library)
            if (!string.IsNullOrEmpty(anchor.Query))
            {
                if (!MatchesQuery(ticket, anchor.Query))
                    continue;
            }

            if (!string.IsNullOrEmpty(ticket.Query))
            {
                if (!MatchesQuery(anchor, ticket.Query))
                    continue;
            }

            matched.Add(ticket);

            // Check if we have enough
            if (matched.Count >= queue.MaxCount)
                break;

            // Check count multiple
            if (matched.Count >= queue.MinCount && matched.Count % queue.CountMultiple == 0)
            {
                // We have a valid match size
                break;
            }
        }

        // Final validation
        if (matched.Count >= queue.MinCount && matched.Count % queue.CountMultiple == 0)
        {
            return matched;
        }

        return null;
    }

    /// <inheritdoc/>
    public bool MatchesQuery(TicketModel ticket, string query)
    {
        // Simple key:value matching
        // e.g., "region:na" matches if StringProperties["region"] == "na"
        // e.g., "skill:>1000" matches if NumericProperties["skill"] > 1000

        try
        {
            var parts = query.Split(new[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var colonIdx = part.IndexOf(':');
                if (colonIdx <= 0) continue;

                var key = part.Substring(0, colonIdx);
                var value = part.Substring(colonIdx + 1);

                // Check string property
                if (ticket.StringProperties.TryGetValue(key, out var strVal))
                {
                    if (!strVal.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                // Check numeric property
                else if (ticket.NumericProperties.TryGetValue(key, out var numVal))
                {
                    if (value.StartsWith(">"))
                    {
                        if (double.TryParse(value.Substring(1), out var threshold))
                        {
                            if (numVal <= threshold) return false;
                        }
                    }
                    else if (value.StartsWith("<"))
                    {
                        if (double.TryParse(value.Substring(1), out var threshold))
                        {
                            if (numVal >= threshold) return false;
                        }
                    }
                    else if (double.TryParse(value, out var exact))
                    {
                        if (Math.Abs(numVal - exact) > 0.001) return false;
                    }
                }
            }
            return true;
        }
        catch
        {
            return true; // On parse error, allow the match
        }
    }

    /// <inheritdoc/>
    public int? GetCurrentSkillRange(QueueModel? queue, int intervalsElapsed)
    {
        if (queue?.SkillExpansion == null || queue.SkillExpansion.Count == 0)
            return null;

        // Find the applicable expansion step
        var applicableStep = queue.SkillExpansion
            .Where(s => s.Intervals <= intervalsElapsed)
            .OrderByDescending(s => s.Intervals)
            .FirstOrDefault();

        return applicableStep?.Range;
    }
}
