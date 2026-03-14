using BeyondImmersion.BannouService.CharacterEncounter;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.CharacterEncounter.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.CharacterEncounter.Tests;

/// <summary>
/// Tests for EncountersProviderFactory — validates that the factory
/// populates EncountersProvider with sentiment and hasMet data from the cache.
/// </summary>
public class EncountersProviderFactoryTests
{
    private readonly Mock<IEncounterDataCache> _mockCache;
    private readonly Mock<ITelemetryProvider> _mockTelemetry;
    private readonly CharacterEncounterServiceConfiguration _config;

    public EncountersProviderFactoryTests()
    {
        _mockCache = new Mock<IEncounterDataCache>();
        _mockTelemetry = new Mock<ITelemetryProvider>();
        _config = new CharacterEncounterServiceConfiguration
        {
            GrudgeSentimentThreshold = -0.5,
            AllySentimentThreshold = 0.5
        };
    }

    private EncountersProviderFactory CreateFactory()
    {
        return new EncountersProviderFactory(
            _mockCache.Object,
            _mockTelemetry.Object,
            _config);
    }

    [Fact]
    public async Task CreateAsync_NullCharacterId_ReturnsEmptyProvider()
    {
        var factory = CreateFactory();

        var provider = await factory.CreateAsync(null, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        // Empty provider returns 0 for count
        Assert.Equal(0, provider.GetValue(new[] { "count" }.AsSpan()));
    }

    [Fact]
    public async Task CreateAsync_WithEncounters_LoadsSentimentForParticipants()
    {
        var factory = CreateFactory();
        var characterId = Guid.NewGuid();
        var otherCharId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var encounters = new EncounterListResponse
        {
            Encounters = new List<EncounterResponse>
            {
                new()
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        EncounterTypeCode = "COMBAT",
                        Outcome = EncounterOutcome.Negative,
                        ParticipantIds = new List<Guid> { characterId, otherCharId },
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    Perspectives = new List<EncounterPerspectiveModel>()
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };

        var sentimentResponse = new SentimentResponse
        {
            CharacterId = characterId,
            TargetCharacterId = otherCharId,
            Sentiment = -0.7f,
            EncounterCount = 3
        };

        var hasMetResponse = new HasMetResponse
        {
            HasMet = true,
            EncounterCount = 3
        };

        _mockCache.Setup(c => c.GetEncountersOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounters);
        _mockCache.Setup(c => c.GetSentimentOrLoadAsync(characterId, otherCharId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sentimentResponse);
        _mockCache.Setup(c => c.HasMetOrLoadAsync(characterId, otherCharId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasMetResponse);

        // Act
        var provider = await factory.CreateAsync(characterId, realmId, null, TestContext.Current.CancellationToken);

        // Assert — provider should have loaded sentiment and hasMet data
        var sentiment = provider.GetValue(new[] { "sentiment", otherCharId.ToString() }.AsSpan());
        Assert.NotNull(sentiment);
        Assert.Equal(-0.7f, (float)sentiment);

        var hasMet = provider.GetValue(new[] { "has_met", otherCharId.ToString() }.AsSpan());
        Assert.NotNull(hasMet);
        Assert.True((bool)hasMet);

        var encounterCount = provider.GetValue(new[] { "encounter_count", otherCharId.ToString() }.AsSpan());
        Assert.Equal(3, encounterCount);

        // Verify cache was called for each participant
        _mockCache.Verify(c => c.GetSentimentOrLoadAsync(characterId, otherCharId, It.IsAny<CancellationToken>()), Times.Once);
        _mockCache.Verify(c => c.HasMetOrLoadAsync(characterId, otherCharId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DoesNotLoadSentimentForSelf()
    {
        var factory = CreateFactory();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var encounters = new EncounterListResponse
        {
            Encounters = new List<EncounterResponse>
            {
                new()
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        EncounterTypeCode = "SOCIAL",
                        Outcome = EncounterOutcome.Positive,
                        ParticipantIds = new List<Guid> { characterId, characterId },
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    Perspectives = new List<EncounterPerspectiveModel>()
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };

        _mockCache.Setup(c => c.GetEncountersOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounters);

        // Act
        await factory.CreateAsync(characterId, realmId, null, TestContext.Current.CancellationToken);

        // Assert — should NOT call sentiment/hasMet for self
        _mockCache.Verify(c => c.GetSentimentOrLoadAsync(characterId, characterId, It.IsAny<CancellationToken>()), Times.Never);
        _mockCache.Verify(c => c.HasMetOrLoadAsync(characterId, characterId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NoEncounters_ReturnsProviderWithZeroCount()
    {
        var factory = CreateFactory();
        var characterId = Guid.NewGuid();

        _mockCache.Setup(c => c.GetEncountersOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EncounterListResponse?)null);

        // Act
        var provider = await factory.CreateAsync(characterId, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, provider.GetValue(new[] { "count" }.AsSpan()));

        // No participants to load sentiment for
        _mockCache.Verify(c => c.GetSentimentOrLoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_GrudgeThresholdApplied_ProviderReturnsGrudges()
    {
        var factory = CreateFactory();
        var characterId = Guid.NewGuid();
        var enemyId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var encounters = new EncounterListResponse
        {
            Encounters = new List<EncounterResponse>
            {
                new()
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RealmId = realmId,
                        EncounterTypeCode = "COMBAT",
                        Outcome = EncounterOutcome.Negative,
                        ParticipantIds = new List<Guid> { characterId, enemyId },
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    Perspectives = new List<EncounterPerspectiveModel>()
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };

        _mockCache.Setup(c => c.GetEncountersOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounters);
        _mockCache.Setup(c => c.GetSentimentOrLoadAsync(characterId, enemyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SentimentResponse
            {
                CharacterId = characterId,
                TargetCharacterId = enemyId,
                Sentiment = -0.8f,
                EncounterCount = 5
            });
        _mockCache.Setup(c => c.HasMetOrLoadAsync(characterId, enemyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HasMetResponse { HasMet = true, EncounterCount = 5 });

        // Act
        var provider = await factory.CreateAsync(characterId, realmId, null, TestContext.Current.CancellationToken);

        // Assert — grudges should contain the enemy (-0.8 is below -0.5 threshold)
        var grudges = provider.GetValue(new[] { "grudges" }.AsSpan());
        Assert.NotNull(grudges);
        var grudgeList = grudges as List<Dictionary<string, object?>>;
        Assert.NotNull(grudgeList);
        Assert.Single(grudgeList);
        Assert.Equal(enemyId.ToString(), grudgeList[0]["character_id"]);
    }
}
