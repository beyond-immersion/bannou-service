// =============================================================================
// Store Memory Handler Unit Tests
// Tests for memory storage (Cognition Stage 4).
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
/// Unit tests for StoreMemoryHandler.
/// </summary>
public class StoreMemoryHandlerTests : CognitionHandlerTestBase
{
    private readonly Mock<IMemoryStore> _mockMemoryStore;
    private readonly StoreMemoryHandler _handler;

    public StoreMemoryHandlerTests()
    {
        _mockMemoryStore = new Mock<IMemoryStore>();
        _handler = new StoreMemoryHandler(_mockMemoryStore.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<StoreMemoryHandler>();
        Assert.NotNull(_handler);
    }

    #endregion

    #region CanHandle Tests

    [Fact]
    public void CanHandle_StoreMemoryAction_ReturnsTrue()
    {
        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>());

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
        var perception = CreatePerception();
        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "perception", perception }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyEntityId_ThrowsInvalidOperationException()
    {
        var perception = CreatePerception();
        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", "" },
            { "perception", perception }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_MissingPerception_ThrowsInvalidOperationException()
    {
        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", "test-entity" }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_NullPerception_ThrowsInvalidOperationException()
    {
        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", "test-entity" },
            { "perception", null }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    #endregion

    #region ExecuteAsync Tests - Basic Storage

    [Fact]
    public async Task ExecuteAsync_ValidParams_StoresExperience()
    {
        var entityId = "test-entity";
        var perception = CreatePerception("threat", "Enemy spotted", 0.9f);
        var significance = 0.85f;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception },
            { "significance", significance }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        _mockMemoryStore.Verify(s => s.StoreExperienceAsync(
            entityId,
            It.Is<Perception>(p => p.Category == "threat"),
            significance,
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultSignificance_UsesHalf()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        float capturedSignificance = 0;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, sig, _, _) => capturedSignificance = sig)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception }
            // No significance specified
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(0.5f, capturedSignificance);
    }

    #endregion

    #region ExecuteAsync Tests - Context Memories

    [Fact]
    public async Task ExecuteAsync_WithContextMemories_PassesToStore()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        var contextMemories = new List<Memory>
        {
            CreateMemory("mem-1", entityId),
            CreateMemory("mem-2", entityId)
        };
        IReadOnlyList<Memory>? capturedContext = null;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, _, ctx, _) => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception },
            { "context", contextMemories }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Equal(2, capturedContext.Count);
    }

    [Fact]
    public async Task ExecuteAsync_NoContextMemories_PassesEmptyList()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        IReadOnlyList<Memory>? capturedContext = null;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, _, ctx, _) => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Empty(capturedContext);
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_PassesEmptyList()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        IReadOnlyList<Memory>? capturedContext = null;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, _, ctx, _) => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception },
            { "context", null }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Empty(capturedContext);
    }

    #endregion

    #region ExecuteAsync Tests - Input Conversion

    [Fact]
    public async Task ExecuteAsync_DictionaryPerception_ConvertsCorrectly()
    {
        var entityId = "test-entity";
        var dictPerception = new Dictionary<string, object?>
        {
            { "category", "threat" },
            { "content", "Enemy nearby" },
            { "urgency", 0.8f }
        };
        Perception? capturedPerception = null;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, p, _, _, _) => capturedPerception = p)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", dictPerception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedPerception);
        Assert.Equal("threat", capturedPerception.Category);
        Assert.Equal("Enemy nearby", capturedPerception.Content);
    }

    [Fact]
    public async Task ExecuteAsync_MixedContextTypes_FiltersMemoriesOnly()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        var mixedContext = new List<object>
        {
            CreateMemory("mem-1", entityId),
            "not a memory",
            CreateMemory("mem-2", entityId),
            123
        };
        IReadOnlyList<Memory>? capturedContext = null;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, _, ctx, _) => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception },
            { "context", mixedContext }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Equal(2, capturedContext.Count);  // Only Memory objects
    }

    #endregion

    #region ExecuteAsync Tests - Significance Conversion

    [Fact]
    public async Task ExecuteAsync_IntSignificance_ConvertsToFloat()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        float capturedSignificance = 0;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, sig, _, _) => capturedSignificance = sig)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception },
            { "significance", 1 }  // int, not float
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(1.0f, capturedSignificance);
    }

    [Fact]
    public async Task ExecuteAsync_DoubleSignificance_ConvertsToFloat()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        float capturedSignificance = 0;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, sig, _, _) => capturedSignificance = sig)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception },
            { "significance", 0.75 }  // double, not float
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(0.75f, capturedSignificance, 2);
    }

    #endregion

    #region ExecuteAsync Tests - Cancellation

    [Fact]
    public async Task ExecuteAsync_CancellationToken_PassedToStore()
    {
        var entityId = "test-entity";
        var perception = CreatePerception();
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        _mockMemoryStore.Setup(s => s.StoreExperienceAsync(
            It.IsAny<string>(),
            It.IsAny<Perception>(),
            It.IsAny<float>(),
            It.IsAny<IReadOnlyList<Memory>>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Perception, float, IReadOnlyList<Memory>, CancellationToken>(
                (_, _, _, _, ct) => capturedToken = ct)
            .Returns(Task.CompletedTask);

        var action = CreateDomainAction("store_memory", new Dictionary<string, object?>
        {
            { "entity_id", entityId },
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, cts.Token);

        Assert.Equal(cts.Token, capturedToken);
    }

    #endregion
}
