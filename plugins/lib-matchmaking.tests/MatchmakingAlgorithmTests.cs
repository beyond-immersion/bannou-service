using BeyondImmersion.BannouService.Matchmaking.Helpers;

namespace BeyondImmersion.BannouService.Matchmaking.Tests;

/// <summary>
/// Unit tests for MatchmakingAlgorithm helper methods.
/// Tests the pure algorithmic functions extracted from MatchmakingService.
/// </summary>
public class MatchmakingAlgorithmTests
{
    private readonly MatchmakingAlgorithm _sut;

    public MatchmakingAlgorithmTests()
    {
        _sut = new MatchmakingAlgorithm();
    }

    #region TryMatchTickets Tests

    [Fact]
    public void TryMatchTickets_WithInsufficientTickets_ReturnsNull()
    {
        // Arrange
        var tickets = new List<TicketModel>
        {
            CreateTicket()
        };
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryMatchTickets_WithExactMinCount_ReturnsMatched()
    {
        // Arrange
        var tickets = new List<TicketModel>
        {
            CreateTicket(),
            CreateTicket()
        };
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TryMatchTickets_SortsByCreatedAtFIFO()
    {
        // Arrange
        var oldTicket = CreateTicket();
        oldTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var newTicket = CreateTicket();
        newTicket.CreatedAt = DateTimeOffset.UtcNow;

        var tickets = new List<TicketModel> { newTicket, oldTicket }; // Out of order
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.NotNull(result);
        // First ticket in match should be the older one (FIFO)
        Assert.Equal(oldTicket.TicketId, result[0].TicketId);
    }

    [Fact]
    public void TryMatchTickets_RespectsMaxCount()
    {
        // Arrange
        var tickets = Enumerable.Range(0, 10).Select(_ => CreateTicket()).ToList();
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count <= 4); // Should not exceed maxCount
    }

    [Fact]
    public void TryMatchTickets_RespectsCountMultiple()
    {
        // Arrange
        var tickets = Enumerable.Range(0, 5).Select(_ => CreateTicket()).ToList();
        var queue = CreateQueue(minCount: 2, maxCount: 6);
        queue.CountMultiple = 2; // Match count must be multiple of 2

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.Count % 2); // Should be multiple of 2
    }

    [Fact]
    public void TryMatchTickets_WithSkillRange_ExcludesFarTickets()
    {
        // Arrange
        var anchorTicket = CreateTicket(skillRating: 1000);
        anchorTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var closeTicket = CreateTicket(skillRating: 1050);
        closeTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);

        var farTicket = CreateTicket(skillRating: 2000);
        farTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3);

        var tickets = new List<TicketModel> { anchorTicket, closeTicket, farTicket };
        var queue = CreateQueue(minCount: 2, maxCount: 4);
        queue.UseSkillRating = true;

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: 100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.TicketId == anchorTicket.TicketId);
        Assert.Contains(result, t => t.TicketId == closeTicket.TicketId);
        Assert.DoesNotContain(result, t => t.TicketId == farTicket.TicketId);
    }

    [Fact]
    public void TryMatchTickets_WithSkillRange_NoSkillOnQueue_IgnoresSkill()
    {
        // Arrange
        var anchorTicket = CreateTicket(skillRating: 1000);
        anchorTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var farTicket = CreateTicket(skillRating: 5000);
        farTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);

        var tickets = new List<TicketModel> { anchorTicket, farTicket };
        var queue = CreateQueue(minCount: 2, maxCount: 4);
        queue.UseSkillRating = false; // Skill rating disabled

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: 100);

        // Assert - Should match regardless of skill difference
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TryMatchTickets_WithNullSkillRange_IgnoresSkill()
    {
        // Arrange
        var tickets = new List<TicketModel>
        {
            CreateTicket(skillRating: 100),
            CreateTicket(skillRating: 5000)
        };
        var queue = CreateQueue(minCount: 2, maxCount: 4);
        queue.UseSkillRating = true;

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert - Should match without skill restriction
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TryMatchTickets_ValidatesCountMultiple_ReturnsNullIfNotMet()
    {
        // Arrange
        var tickets = new List<TicketModel>
        {
            CreateTicket(),
            CreateTicket(),
            CreateTicket()
        };
        var queue = CreateQueue(minCount: 2, maxCount: 6);
        queue.CountMultiple = 2; // Requires even numbers

        // Act - 3 tickets available, minimum is 2, but count multiple is 2
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert - Should return 2 (the first count that satisfies minCount AND countMultiple)
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TryMatchTickets_EmptyTicketList_ReturnsNull()
    {
        // Arrange
        var tickets = new List<TicketModel>();
        var queue = CreateQueue(minCount: 1, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region MatchesQuery Tests

    [Fact]
    public void MatchesQuery_EmptyQuery_ReturnsTrue()
    {
        // Arrange
        var ticket = CreateTicket();

        // Act
        var result = _sut.MatchesQuery(ticket, "");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_StringProperty_MatchingValue_ReturnsTrue()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.StringProperties["region"] = "na";

        // Act
        var result = _sut.MatchesQuery(ticket, "region:na");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_StringProperty_NonMatchingValue_ReturnsFalse()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.StringProperties["region"] = "eu";

        // Act
        var result = _sut.MatchesQuery(ticket, "region:na");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesQuery_StringProperty_CaseInsensitive()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.StringProperties["region"] = "NA";

        // Act
        var result = _sut.MatchesQuery(ticket, "region:na");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_NumericProperty_GreaterThan_Matches()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.NumericProperties["level"] = 50;

        // Act
        var result = _sut.MatchesQuery(ticket, "level:>30");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_NumericProperty_GreaterThan_DoesNotMatch()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.NumericProperties["level"] = 20;

        // Act
        var result = _sut.MatchesQuery(ticket, "level:>30");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesQuery_NumericProperty_LessThan_Matches()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.NumericProperties["ping"] = 50;

        // Act
        var result = _sut.MatchesQuery(ticket, "ping:<100");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_NumericProperty_LessThan_DoesNotMatch()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.NumericProperties["ping"] = 150;

        // Act
        var result = _sut.MatchesQuery(ticket, "ping:<100");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesQuery_NumericProperty_ExactMatch()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.NumericProperties["rank"] = 5;

        // Act
        var result = _sut.MatchesQuery(ticket, "rank:5");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_MultipleConditions_AllMatch()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.StringProperties["region"] = "na";
        ticket.NumericProperties["level"] = 50;

        // Act
        var result = _sut.MatchesQuery(ticket, "region:na level:>30");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_MultipleConditions_OneDoesNotMatch()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.StringProperties["region"] = "eu";
        ticket.NumericProperties["level"] = 50;

        // Act
        var result = _sut.MatchesQuery(ticket, "region:na level:>30");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesQuery_MultipleConditions_PlusDelimited()
    {
        // Arrange
        var ticket = CreateTicket();
        ticket.StringProperties["region"] = "na";
        ticket.NumericProperties["level"] = 50;

        // Act
        var result = _sut.MatchesQuery(ticket, "region:na+level:>30");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_UnknownProperty_ReturnsTrue()
    {
        // Arrange
        var ticket = CreateTicket();
        // No "unknown" property set

        // Act
        var result = _sut.MatchesQuery(ticket, "unknown:value");

        // Assert - Unknown properties are ignored (allow match)
        Assert.True(result);
    }

    [Fact]
    public void MatchesQuery_MalformedQuery_ReturnsTrue()
    {
        // Arrange
        var ticket = CreateTicket();

        // Act - Malformed query without colon
        var result = _sut.MatchesQuery(ticket, "malformed");

        // Assert - On parse error, allow the match
        Assert.True(result);
    }

    #endregion

    #region GetCurrentSkillRange Tests

    [Fact]
    public void GetCurrentSkillRange_NullQueue_ReturnsNull()
    {
        // Act
        var result = _sut.GetCurrentSkillRange(null, intervalsElapsed: 0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentSkillRange_NullSkillExpansion_ReturnsNull()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = null;

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentSkillRange_EmptySkillExpansion_ReturnsNull()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = new List<SkillExpansionStepModel>();

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentSkillRange_AtInterval0_ReturnsFirstStep()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = new List<SkillExpansionStepModel>
        {
            new SkillExpansionStepModel { Intervals = 0, Range = 100 },
            new SkillExpansionStepModel { Intervals = 3, Range = 200 },
            new SkillExpansionStepModel { Intervals = 6, Range = 500 }
        };

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 0);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void GetCurrentSkillRange_AtInterval3_ReturnsSecondStep()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = new List<SkillExpansionStepModel>
        {
            new SkillExpansionStepModel { Intervals = 0, Range = 100 },
            new SkillExpansionStepModel { Intervals = 3, Range = 200 },
            new SkillExpansionStepModel { Intervals = 6, Range = 500 }
        };

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 3);

        // Assert
        Assert.Equal(200, result);
    }

    [Fact]
    public void GetCurrentSkillRange_BetweenIntervals_ReturnsLowerStep()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = new List<SkillExpansionStepModel>
        {
            new SkillExpansionStepModel { Intervals = 0, Range = 100 },
            new SkillExpansionStepModel { Intervals = 3, Range = 200 },
            new SkillExpansionStepModel { Intervals = 6, Range = 500 }
        };

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 4);

        // Assert - At interval 4, should use step for interval 3 (highest step <= 4)
        Assert.Equal(200, result);
    }

    [Fact]
    public void GetCurrentSkillRange_BeyondAllIntervals_ReturnsLastStep()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = new List<SkillExpansionStepModel>
        {
            new SkillExpansionStepModel { Intervals = 0, Range = 100 },
            new SkillExpansionStepModel { Intervals = 3, Range = 200 },
            new SkillExpansionStepModel { Intervals = 6, Range = 500 }
        };

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 10);

        // Assert
        Assert.Equal(500, result);
    }

    [Fact]
    public void GetCurrentSkillRange_BeforeFirstStep_ReturnsNull()
    {
        // Arrange
        var queue = CreateQueue();
        queue.SkillExpansion = new List<SkillExpansionStepModel>
        {
            new SkillExpansionStepModel { Intervals = 2, Range = 100 }, // Starts at interval 2
            new SkillExpansionStepModel { Intervals = 5, Range = 200 }
        };

        // Act
        var result = _sut.GetCurrentSkillRange(queue, intervalsElapsed: 1);

        // Assert - No step covers interval 1
        Assert.Null(result);
    }

    #endregion

    #region Query Matching in TryMatchTickets

    [Fact]
    public void TryMatchTickets_WithQuery_MatchingTickets_ReturnsMatched()
    {
        // Arrange
        var anchorTicket = CreateTicket();
        anchorTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        anchorTicket.Query = "region:na";
        anchorTicket.StringProperties["region"] = "na";

        var matchingTicket = CreateTicket();
        matchingTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        matchingTicket.StringProperties["region"] = "na";

        var tickets = new List<TicketModel> { anchorTicket, matchingTicket };
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TryMatchTickets_WithQuery_NonMatchingTickets_ReturnsNull()
    {
        // Arrange
        var anchorTicket = CreateTicket();
        anchorTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        anchorTicket.Query = "region:na";
        anchorTicket.StringProperties["region"] = "na";

        var nonMatchingTicket = CreateTicket();
        nonMatchingTicket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        nonMatchingTicket.StringProperties["region"] = "eu"; // Different region

        var tickets = new List<TicketModel> { anchorTicket, nonMatchingTicket };
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert - Can't match because non-matching ticket doesn't satisfy anchor's query
        Assert.Null(result);
    }

    [Fact]
    public void TryMatchTickets_BothTicketsHaveQueries_MutualMatch()
    {
        // Arrange
        var ticket1 = CreateTicket();
        ticket1.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        ticket1.Query = "level:>10";
        ticket1.NumericProperties["level"] = 50;

        var ticket2 = CreateTicket();
        ticket2.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        ticket2.Query = "level:>20";
        ticket2.NumericProperties["level"] = 30;

        var tickets = new List<TicketModel> { ticket1, ticket2 };
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert - Both queries should be satisfied
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TryMatchTickets_BothTicketsHaveQueries_OneDoesNotMatch()
    {
        // Arrange
        var ticket1 = CreateTicket();
        ticket1.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        ticket1.Query = "level:>100"; // Requires level > 100
        ticket1.NumericProperties["level"] = 50;

        var ticket2 = CreateTicket();
        ticket2.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        ticket2.NumericProperties["level"] = 30; // Too low for ticket1's query

        var tickets = new List<TicketModel> { ticket1, ticket2 };
        var queue = CreateQueue(minCount: 2, maxCount: 4);

        // Act
        var result = _sut.TryMatchTickets(tickets, queue, skillRange: null);

        // Assert - ticket2 doesn't satisfy ticket1's query (level > 100)
        Assert.Null(result);
    }

    #endregion

    #region Helper Methods

    private static TicketModel CreateTicket(int? skillRating = null)
    {
        return new TicketModel
        {
            TicketId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            QueueId = "test-queue",
            Status = TicketStatus.Searching,
            SkillRating = skillRating,
            StringProperties = new Dictionary<string, string>(),
            NumericProperties = new Dictionary<string, double>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static QueueModel CreateQueue(int minCount = 2, int maxCount = 4)
    {
        return new QueueModel
        {
            QueueId = "test-queue",
            GameId = "test-game",
            SessionGameType = SessionGameType.Arcadia,
            DisplayName = "Test Queue",
            Enabled = true,
            MinCount = minCount,
            MaxCount = maxCount,
            CountMultiple = 1,
            UseSkillRating = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
