// =============================================================================
// Query Memory Handler Unit Tests
// Tests for memory querying (Cognition Stage 2).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Abml.Cognition.Handlers;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.TestUtilities;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Unit tests for QueryMemoryHandler.
/// </summary>
public class QueryMemoryHandlerTests : CognitionHandlerTestBase
{
    private readonly Mock<IMemoryStore> _mockMemoryStore;
    private readonly QueryMemoryHandler _handler;

    public QueryMemoryHandlerTests()
    {
        _mockMemoryStore = new Mock<IMemoryStore>();
        _handler = new QueryMemoryHandler(_mockMemoryStore.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<QueryMemoryHandler>();
        Assert.NotNull(_handler);
    }

    #endregion

    #region CanHandle Tests

    [Fact]
    public void CanHandle_QueryMemoryAction_ReturnsTrue()
    {
        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.True(result);
    }

    [Fact]
    public void CanHandle_OtherAction_ReturnsFalse()
    {
        var action = CreateDomainAction("other_action", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    [Fact]
    public void CanHandle_NonDomainAction_ReturnsFalse()
    {
        var action = new SetAction("var", "value");

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    #endregion

    #region ExecuteAsync Tests - Validation

    [Fact]
    public async Task ExecuteAsync_MissingEntityId_ThrowsInvalidOperationException()
    {
        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "perceptions", new List<Perception> { CreatePerception() } }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyEntityId_ThrowsInvalidOperationException()
    {
        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", "" },
            { "perceptions", new List<Perception> { CreatePerception() } }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    #endregion

    #region ExecuteAsync Tests - Basic Query

    [Fact]
    public async Task ExecuteAsync_ValidQuery_ReturnsMemories()
    {
        var entityId = "test-entity";
        var perceptions = new List<Perception> { CreatePerception() };
        var expectedMemories = new List<Memory>
        {
            CreateMemory("mem-1", entityId),
            CreateMemory("mem-2", entityId)
        };

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            entityId,
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMemories);

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", perceptions }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var memories = GetScopeValue<IReadOnlyList<Memory>>(context, "relevant_memories");
        Assert.NotNull(memories);
        Assert.Equal(2, memories.Count);
    }

    [Fact]
    public async Task ExecuteAsync_NoPerceptions_QueriesWithEmptyList()
    {
        var entityId = "test-entity";
        IReadOnlyList<Perception>? capturedPerceptions = null;

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            entityId,
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Perception>, int, CancellationToken>(
                (_, p, _, _) => capturedPerceptions = p)
            .ReturnsAsync(new List<Memory>());

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", null }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedPerceptions);
        Assert.Empty(capturedPerceptions);
    }

    #endregion

    #region ExecuteAsync Tests - Limit Parameter

    [Fact]
    public async Task ExecuteAsync_DefaultLimit_Uses10()
    {
        var entityId = "test-entity";
        int capturedLimit = 0;

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            entityId,
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Perception>, int, CancellationToken>(
                (_, _, l, _) => capturedLimit = l)
            .ReturnsAsync(new List<Memory>());

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", new List<Perception>() }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(10, capturedLimit);
    }

    [Fact]
    public async Task ExecuteAsync_CustomLimit_UsesProvidedLimit()
    {
        var entityId = "test-entity";
        int capturedLimit = 0;

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            entityId,
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Perception>, int, CancellationToken>(
                (_, _, l, _) => capturedLimit = l)
            .ReturnsAsync(new List<Memory>());

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", new List<Perception>() },
            { "limit", 25 }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(25, capturedLimit);
    }

    #endregion

    #region ExecuteAsync Tests - Result Variable

    [Fact]
    public async Task ExecuteAsync_DefaultResultVariable_StoresAsRelevantMemories()
    {
        var entityId = "test-entity";
        var memories = new List<Memory> { CreateMemory() };

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", new List<Perception>() }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<IReadOnlyList<Memory>>(context, "relevant_memories");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteAsync_CustomResultVariable_UsesCustomName()
    {
        var entityId = "test-entity";
        var memories = new List<Memory> { CreateMemory() };

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", new List<Perception>() },
            { "result_variable", "my_memories" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<IReadOnlyList<Memory>>(context, "my_memories");
        Assert.NotNull(result);
    }

    #endregion

    #region ExecuteAsync Tests - Input Conversion

    [Fact]
    public async Task ExecuteAsync_DictionaryPerceptions_ConvertsCorrectly()
    {
        var entityId = "test-entity";
        IReadOnlyList<Perception>? capturedPerceptions = null;

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Perception>, int, CancellationToken>(
                (_, p, _, _) => capturedPerceptions = p)
            .ReturnsAsync(new List<Memory>());

        var dictPerceptions = new List<object>
        {
            new Dictionary<string, object?>
            {
                { "category", "threat" },
                { "content", "Enemy spotted" }
            }
        };

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", dictPerceptions }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedPerceptions);
        Assert.Single(capturedPerceptions);
        Assert.Equal("threat", capturedPerceptions[0].Category);
    }

    [Fact]
    public async Task ExecuteAsync_MixedPerceptionTypes_HandlesGracefully()
    {
        var entityId = "test-entity";
        IReadOnlyList<Perception>? capturedPerceptions = null;

        _mockMemoryStore.Setup(s => s.FindRelevantAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Perception>>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Perception>, int, CancellationToken>(
                (_, p, _, _) => capturedPerceptions = p)
            .ReturnsAsync(new List<Memory>());

        var mixedPerceptions = new List<object>
        {
            CreatePerception("social", "Hello"),
            new Dictionary<string, object?>
            {
                { "category", "routine" },
                { "content", "Walking" }
            }
        };

        var action = CreateDomainAction("query_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perceptions", mixedPerceptions }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedPerceptions);
        Assert.Equal(2, capturedPerceptions.Count);
    }

    #endregion
}
