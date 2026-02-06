// =============================================================================
// Actor Local Memory Store Unit Tests
// Tests for memory storage operations using mocked state stores.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Services;
using CognitionMemory = BeyondImmersion.BannouService.Abml.Cognition.Memory;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Cognition;

/// <summary>
/// Unit tests for ActorLocalMemoryStore.
/// </summary>
/// <remarks>
/// <para>
/// <strong>MVP Implementation Note:</strong> This memory store uses keyword-based relevance
/// matching (not semantic embeddings). See <see cref="CognitionConstants"/> for scoring weights:
/// <list type="bullet">
/// <item>Category match: <see cref="CognitionConstants.MemoryCategoryMatchWeight"/></item>
/// <item>Content overlap: <see cref="CognitionConstants.MemoryContentOverlapWeight"/></item>
/// <item>Metadata overlap: <see cref="CognitionConstants.MemoryMetadataOverlapWeight"/></item>
/// <item>Recency bonus: <see cref="CognitionConstants.MemoryRecencyBonusWeight"/></item>
/// <item>Significance bonus: <see cref="CognitionConstants.MemorySignificanceBonusWeight"/></item>
/// </list>
/// </para>
/// <para>
/// <strong>Test-Implementation Coupling:</strong> Tests that verify relevance scoring behavior
/// are coupled to the weight values in <see cref="CognitionConstants"/>. If those constants change,
/// tests may need adjustment to verify the new expected behavior.
/// </para>
/// <para>
/// Memories must score at least <see cref="CognitionConstants.MemoryMinimumRelevanceThreshold"/>
/// to be considered relevant. This prevents weakly-related memories from being returned.
/// </para>
/// </remarks>
public class ActorLocalMemoryStoreTests
{
    private readonly Mock<IStateStoreFactory> _mockFactory;
    private readonly Mock<IStateStore<CognitionMemory>> _mockMemoryStore;
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore;
    private readonly Mock<ILogger<ActorLocalMemoryStore>> _mockLogger;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly ActorLocalMemoryStore _memoryStore;

    public ActorLocalMemoryStoreTests()
    {
        _mockFactory = new Mock<IStateStoreFactory>();
        _mockMemoryStore = new Mock<IStateStore<CognitionMemory>>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();
        _mockLogger = new Mock<ILogger<ActorLocalMemoryStore>>();
        _configuration = new BehaviorServiceConfiguration();

        _mockFactory.Setup(f => f.GetStore<CognitionMemory>(StateStoreDefinitions.AgentMemories))
            .Returns(_mockMemoryStore.Object);
        _mockFactory.Setup(f => f.GetStore<List<string>>(StateStoreDefinitions.AgentMemories))
            .Returns(_mockIndexStore.Object);

        _memoryStore = new ActorLocalMemoryStore(_mockFactory.Object, _configuration, _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<ActorLocalMemoryStore>();
        Assert.NotNull(_memoryStore);
    }

    #endregion

    #region FindRelevantAsync Tests

    [Fact]
    public async Task FindRelevantAsync_EmptyPerceptions_ReturnsEmptyList()
    {
        var result = await _memoryStore.FindRelevantAsync(
            "entity-1", [], 10, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindRelevantAsync_ZeroLimit_ReturnsEmptyList()
    {
        var perceptions = new List<Perception> { CreatePerception() };

        var result = await _memoryStore.FindRelevantAsync(
            "entity-1", perceptions, 0, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindRelevantAsync_NoMemories_ReturnsEmptyList()
    {
        SetupEmptyIndex("entity-1");

        var perceptions = new List<Perception> { CreatePerception() };
        var result = await _memoryStore.FindRelevantAsync(
            "entity-1", perceptions, 10, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindRelevantAsync_MatchingCategory_ReturnsMemory()
    {
        var entityId = "entity-1";
        var memoryId = "mem-1";
        var memory = CreateMemory(memoryId, entityId, "threat", "Enemy nearby");

        SetupMemoryIndex(entityId, [memoryId]);
        SetupMemoryRetrieval(entityId, memoryId, memory);

        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Enemy spotted", 0.9f)
        };

        var result = await _memoryStore.FindRelevantAsync(
            entityId, perceptions, 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(memoryId, result[0].Id);
        Assert.True(result[0].QueryRelevance > 0);
    }

    [Fact]
    public async Task FindRelevantAsync_MatchingContent_ReturnsMemory()
    {
        var entityId = "entity-1";
        var memoryId = "mem-1";
        var memory = CreateMemory(memoryId, entityId, "routine", "Found treasure chest");

        SetupMemoryIndex(entityId, [memoryId]);
        SetupMemoryRetrieval(entityId, memoryId, memory);

        var perceptions = new List<Perception>
        {
            CreatePerception("novelty", "There's a treasure over there", 0.5f)
        };

        var result = await _memoryStore.FindRelevantAsync(
            entityId, perceptions, 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(memoryId, result[0].Id);
    }

    /// <summary>
    /// Verifies that memories with no relevance to perceptions are filtered out.
    /// Memory must score below <see cref="CognitionConstants.MemoryMinimumRelevanceThreshold"/>.
    /// </summary>
    [Fact]
    public async Task FindRelevantAsync_NoMatch_ReturnsEmptyList()
    {
        var entityId = "entity-1";
        var memoryId = "mem-1";

        // Create a memory that has zero relevance to the perception:
        // - Different category (social vs threat)
        // - No content keyword overlap ("Met a friend" vs "Danger ahead")
        // - Empty metadata (no key overlap)
        // - Old (>1 hour, no recency bonus)
        // - Zero significance (no significance bonus)
        // Total score: 0, which is below MemoryMinimumRelevanceThreshold (0.1)
        var memory = new CognitionMemory
        {
            Id = memoryId,
            EntityId = entityId,
            Category = "social",
            Content = "Met a friend",
            Significance = 0f,
            Timestamp = DateTimeOffset.UtcNow.AddHours(-24),
            Metadata = new Dictionary<string, object>()
        };

        SetupMemoryIndex(entityId, [memoryId]);
        SetupMemoryRetrieval(entityId, memoryId, memory);

        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Danger ahead", 0.9f)
        };

        var result = await _memoryStore.FindRelevantAsync(
            entityId, perceptions, 10, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindRelevantAsync_RespectsLimit()
    {
        var entityId = "entity-1";
        var memories = new List<CognitionMemory>
        {
            CreateMemory("mem-1", entityId, "threat", "First threat"),
            CreateMemory("mem-2", entityId, "threat", "Second threat"),
            CreateMemory("mem-3", entityId, "threat", "Third threat")
        };

        SetupMemoryIndex(entityId, ["mem-1", "mem-2", "mem-3"]);
        foreach (var mem in memories)
        {
            SetupMemoryRetrieval(entityId, mem.Id, mem);
        }

        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Threat detected", 0.9f)
        };

        var result = await _memoryStore.FindRelevantAsync(
            entityId, perceptions, 2, CancellationToken.None);

        Assert.True(result.Count <= 2);
    }

    [Fact]
    public async Task FindRelevantAsync_SortsByRelevanceScore()
    {
        var entityId = "entity-1";
        var memories = new List<CognitionMemory>
        {
            CreateMemory("mem-1", entityId, "routine", "Routine task", 0.2f),
            CreateMemory("mem-2", entityId, "threat", "Dangerous enemy", 0.9f)
        };

        SetupMemoryIndex(entityId, ["mem-1", "mem-2"]);
        foreach (var mem in memories)
        {
            SetupMemoryRetrieval(entityId, mem.Id, mem);
        }

        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Enemy attack", 0.9f)
        };

        var result = await _memoryStore.FindRelevantAsync(
            entityId, perceptions, 10, CancellationToken.None);

        // Higher significance memory should come first due to relevance scoring
        if (result.Count >= 2)
        {
            Assert.True(result[0].QueryRelevance >= result[1].QueryRelevance);
        }
    }

    #endregion

    #region StoreExperienceAsync Tests

    [Fact]
    public async Task StoreExperienceAsync_SavesMemory()
    {
        var entityId = "entity-1";
        var perception = CreatePerception("threat", "Enemy spotted", 0.9f);
        CognitionMemory? savedMemory = null;

        _mockMemoryStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<CognitionMemory>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, CognitionMemory, StateOptions?, CancellationToken>((_, mem, _, _) => savedMemory = mem)
            .ReturnsAsync("etag-new");

        _mockIndexStore.Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), "etag-1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        await _memoryStore.StoreExperienceAsync(
            entityId, perception, 0.85f, [], CancellationToken.None);

        Assert.NotNull(savedMemory);
        Assert.Equal(entityId, savedMemory.EntityId);
        Assert.Equal(perception.Content, savedMemory.Content);
        Assert.Equal(perception.Category, savedMemory.Category);
        Assert.Equal(0.85f, savedMemory.Significance);
    }

    [Fact]
    public async Task StoreExperienceAsync_UpdatesMemoryIndex()
    {
        var entityId = "entity-1";
        var perception = CreatePerception();
        List<string>? savedIndex = null;

        _mockMemoryStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<CognitionMemory>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-new");

        _mockIndexStore.Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { "existing-mem" }, "etag-1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, List<string>, string, CancellationToken>((_, idx, _, _) => savedIndex = idx)
            .ReturnsAsync("etag-2");

        await _memoryStore.StoreExperienceAsync(
            entityId, perception, 0.8f, [], CancellationToken.None);

        Assert.NotNull(savedIndex);
        Assert.Equal(2, savedIndex.Count);
        Assert.Contains("existing-mem", savedIndex);
    }

    [Fact]
    public async Task StoreExperienceAsync_IncludesRelatedMemoryIds()
    {
        var entityId = "entity-1";
        var perception = CreatePerception();
        var contextMemories = new List<CognitionMemory>
        {
            CreateMemory("ctx-1", entityId),
            CreateMemory("ctx-2", entityId)
        };
        CognitionMemory? savedMemory = null;

        _mockMemoryStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<CognitionMemory>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, CognitionMemory, StateOptions?, CancellationToken>((_, mem, _, _) => savedMemory = mem)
            .ReturnsAsync("etag-new");

        _mockIndexStore.Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<string>?)null, (string?)null));
        _mockIndexStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-new");

        await _memoryStore.StoreExperienceAsync(
            entityId, perception, 0.8f, contextMemories, CancellationToken.None);

        Assert.NotNull(savedMemory);
        Assert.Equal(2, savedMemory.RelatedMemoryIds.Count);
        Assert.Contains("ctx-1", savedMemory.RelatedMemoryIds);
        Assert.Contains("ctx-2", savedMemory.RelatedMemoryIds);
    }

    #endregion

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_ZeroLimit_ReturnsEmptyList()
    {
        var result = await _memoryStore.GetAllAsync("entity-1", 0, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_NoMemories_ReturnsEmptyList()
    {
        SetupEmptyIndex("entity-1");

        var result = await _memoryStore.GetAllAsync("entity-1", 10, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllMemories()
    {
        var entityId = "entity-1";
        var memories = new List<CognitionMemory>
        {
            CreateMemory("mem-1", entityId),
            CreateMemory("mem-2", entityId)
        };

        SetupMemoryIndex(entityId, ["mem-1", "mem-2"]);

        // Setup bulk retrieval to return all memories at once
        var allMemoriesDict = memories.ToDictionary(
            m => $"memory:{entityId}:{m.Id}",
            m => m);
        _mockMemoryStore.Setup(s => s.GetBulkAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(allMemoriesDict);

        var result = await _memoryStore.GetAllAsync(entityId, 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAllAsync_RespectsLimit()
    {
        var entityId = "entity-1";
        SetupMemoryIndex(entityId, ["mem-1", "mem-2", "mem-3"]);

        var memories = new Dictionary<string, CognitionMemory>
        {
            { $"memory:{entityId}:mem-2", CreateMemory("mem-2", entityId) },
            { $"memory:{entityId}:mem-3", CreateMemory("mem-3", entityId) }
        };

        _mockMemoryStore.Setup(s => s.GetBulkAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        var result = await _memoryStore.GetAllAsync(entityId, 2, CancellationToken.None);

        // Should only fetch last 2 (most recent)
        Assert.True(result.Count <= 2);
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_DeletesMemoryAndUpdatesIndex()
    {
        var entityId = "entity-1";
        var memoryId = "mem-1";
        var deletedKey = "";
        List<string>? savedIndex = null;

        _mockMemoryStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => deletedKey = key)
            .ReturnsAsync(true);

        _mockIndexStore.Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { "mem-1", "mem-2" }, "etag-1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, List<string>, string, CancellationToken>((_, idx, _, _) => savedIndex = idx)
            .ReturnsAsync("etag-2");

        await _memoryStore.RemoveAsync(entityId, memoryId, CancellationToken.None);

        Assert.Contains(memoryId, deletedKey);
        Assert.NotNull(savedIndex);
        Assert.Single(savedIndex);
        Assert.DoesNotContain(memoryId, savedIndex);
    }

    [Fact]
    public async Task RemoveAsync_NonExistentMemory_DoesNotFail()
    {
        var entityId = "entity-1";
        var memoryId = "non-existent";

        _mockMemoryStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockIndexStore.Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { "other-mem" }, "etag-1"));

        // Should not throw
        await _memoryStore.RemoveAsync(entityId, memoryId, CancellationToken.None);
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ClearAsync_DeletesAllMemoriesAndIndex()
    {
        var entityId = "entity-1";
        var deletedKeys = new List<string>();
        var indexDeleted = false;

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "mem-1", "mem-2" });

        _mockMemoryStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => deletedKeys.Add(key))
            .ReturnsAsync(true);

        _mockIndexStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => indexDeleted = true)
            .ReturnsAsync(true);

        await _memoryStore.ClearAsync(entityId, CancellationToken.None);

        Assert.Equal(2, deletedKeys.Count);
        Assert.True(indexDeleted);
    }

    [Fact]
    public async Task ClearAsync_EmptyMemoryList_OnlyDeletesIndex()
    {
        var entityId = "entity-1";
        var indexDeleted = false;

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockIndexStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => indexDeleted = true)
            .ReturnsAsync(true);

        await _memoryStore.ClearAsync(entityId, CancellationToken.None);

        Assert.True(indexDeleted);
        _mockMemoryStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Helper Methods

    private void SetupEmptyIndex(string entityId)
    {
        _mockIndexStore.Setup(s => s.GetAsync(
            $"memory-index:{entityId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);
    }

    private void SetupMemoryIndex(string entityId, List<string> memoryIds)
    {
        _mockIndexStore.Setup(s => s.GetAsync(
            $"memory-index:{entityId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(memoryIds);
    }

    private void SetupMemoryRetrieval(string entityId, string memoryId, CognitionMemory memory)
    {
        var key = $"memory:{entityId}:{memoryId}";
        var dict = new Dictionary<string, CognitionMemory> { { key, memory } };

        _mockMemoryStore.Setup(s => s.GetBulkAsync(
            It.Is<IEnumerable<string>>(keys => keys.Contains(key)),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken _) =>
            {
                var result = new Dictionary<string, CognitionMemory>();
                foreach (var k in keys)
                {
                    if (dict.TryGetValue(k, out var mem))
                    {
                        result[k] = mem;
                    }
                }
                return result;
            });
    }

    private static Perception CreatePerception(
        string category = "routine",
        string content = "Test perception",
        float urgency = 0.5f)
    {
        return new Perception
        {
            Id = Guid.NewGuid().ToString(),
            Category = category,
            Content = content,
            Urgency = urgency,
            Source = "test-source",
            Timestamp = DateTimeOffset.UtcNow,
            Data = new Dictionary<string, object> { { "test", true } }
        };
    }

    private static CognitionMemory CreateMemory(
        string id,
        string entityId,
        string category = "routine",
        string content = "Test memory",
        float significance = 0.5f)
    {
        return new CognitionMemory
        {
            Id = id,
            EntityId = entityId,
            Category = category,
            Content = content,
            Significance = significance,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object> { { "test", true } }
        };
    }

    #endregion
}
