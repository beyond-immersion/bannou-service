// =============================================================================
// Event Brain Handler Tests
// Tests for Event Brain ABML action handlers: emit_perception, query_options,
// query_actor_state, schedule_event, state_update.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;
using BeyondImmersion.BannouService.Actor.Handlers;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Tests.Integration;

/// <summary>
/// Tests for Event Brain action handlers that enable actor coordination.
/// </summary>
public sealed class EventBrainHandlerTests
{
    // =========================================================================
    // EMIT_PERCEPTION HANDLER TESTS
    // =========================================================================

    [Fact]
    public void EmitPerceptionHandler_CanHandle_ReturnsTrueForEmitPerception()
    {
        // Arrange
        var messageBus = new Mock<IMessageBus>();
        var logger = new Mock<ILogger<EmitPerceptionHandler>>();
        var handler = new EmitPerceptionHandler(messageBus.Object, logger.Object);

        var targetCharacterId = Guid.NewGuid();
        var action = new DomainAction("emit_perception", new Dictionary<string, object?>
        {
            ["target_character"] = targetCharacterId.ToString(),
            ["perception_type"] = "choreography_instruction",
            ["data"] = new Dictionary<string, object?> { ["test"] = "value" }
        });

        // Act
        var canHandle = handler.CanHandle(action);

        // Assert
        Assert.True(canHandle);
    }

    [Fact]
    public void EmitPerceptionHandler_CanHandle_ReturnsFalseForOtherActions()
    {
        // Arrange
        var messageBus = new Mock<IMessageBus>();
        var logger = new Mock<ILogger<EmitPerceptionHandler>>();
        var handler = new EmitPerceptionHandler(messageBus.Object, logger.Object);

        var action = new DomainAction("other_action", new Dictionary<string, object?>());

        // Act
        var canHandle = handler.CanHandle(action);

        // Assert
        Assert.False(canHandle);
    }

    [Fact]
    public async Task EmitPerceptionHandler_ExecuteAsync_PublishesToCorrectTopic()
    {
        // Arrange
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var logger = new Mock<ILogger<EmitPerceptionHandler>>();
        var handler = new EmitPerceptionHandler(messageBus.Object, logger.Object);

        var targetCharacterId = Guid.Parse("00000000-0000-0000-0000-000000000123");
        var (context, scope) = CreateExecutionContext();
        var action = new DomainAction("emit_perception", new Dictionary<string, object?>
        {
            ["target_character"] = targetCharacterId.ToString(),
            ["perception_type"] = "choreography_instruction",
            ["data"] = new Dictionary<string, object?>
            {
                ["encounter_id"] = "enc-001",
                ["sequence_id"] = "seq-001"
            }
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        messageBus.Verify(m => m.TryPublishAsync(
            $"character.{targetCharacterId}.perceptions",
            It.Is<CharacterPerceptionEvent>(e =>
                e.CharacterId == targetCharacterId &&
                e.Perception != null &&
                e.Perception.PerceptionType == "choreography_instruction"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmitPerceptionHandler_ExecuteAsync_ThrowsOnMissingTargetCharacter()
    {
        // Arrange
        var messageBus = new Mock<IMessageBus>();
        var logger = new Mock<ILogger<EmitPerceptionHandler>>();
        var handler = new EmitPerceptionHandler(messageBus.Object, logger.Object);

        var (context, _) = CreateExecutionContext();
        var action = new DomainAction("emit_perception", new Dictionary<string, object?>
        {
            ["perception_type"] = "test",
            ["data"] = new Dictionary<string, object?>()
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    // =========================================================================
    // STATE_UPDATE HANDLER TESTS
    // =========================================================================

    [Fact]
    public void StateUpdateHandler_CanHandle_ReturnsTrueForStateUpdate()
    {
        // Arrange
        var logger = new Mock<ILogger<StateUpdateHandler>>();
        var handler = new StateUpdateHandler(logger.Object);

        var action = new DomainAction("state_update", new Dictionary<string, object?>
        {
            ["path"] = "memories.test",
            ["operation"] = "set",
            ["value"] = "test_value"
        });

        // Act
        var canHandle = handler.CanHandle(action);

        // Assert
        Assert.True(canHandle);
    }

    [Fact]
    public async Task StateUpdateHandler_ExecuteAsync_SetOperation_SetsValueInScope()
    {
        // Arrange
        var logger = new Mock<ILogger<StateUpdateHandler>>();
        var handler = new StateUpdateHandler(logger.Object);

        var (context, scope) = CreateExecutionContext();
        var action = new DomainAction("state_update", new Dictionary<string, object?>
        {
            ["path"] = "test_key",
            ["operation"] = "set",
            ["value"] = "test_value"
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var storedValue = scope.GetValue("test_key");
        Assert.Equal("test_value", storedValue);
    }

    [Fact]
    public async Task StateUpdateHandler_ExecuteAsync_AppendOperation_AddsToList()
    {
        // Arrange
        var logger = new Mock<ILogger<StateUpdateHandler>>();
        var handler = new StateUpdateHandler(logger.Object);

        var (context, scope) = CreateExecutionContext();
        scope.SetValue("my_list", new List<object?> { "item1" });

        var action = new DomainAction("state_update", new Dictionary<string, object?>
        {
            ["path"] = "my_list",
            ["operation"] = "append",
            ["value"] = "item2"
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var list = scope.GetValue("my_list") as List<object?>;
        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
        Assert.Contains("item1", list);
        Assert.Contains("item2", list);
    }

    [Fact]
    public async Task StateUpdateHandler_ExecuteAsync_NestedPath_SetsNestedValue()
    {
        // Arrange
        var logger = new Mock<ILogger<StateUpdateHandler>>();
        var handler = new StateUpdateHandler(logger.Object);

        var (context, scope) = CreateExecutionContext();

        var action = new DomainAction("state_update", new Dictionary<string, object?>
        {
            ["path"] = "memories.active_encounters.current",
            ["operation"] = "set",
            ["value"] = "encounter-001"
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var memories = scope.GetValue("memories") as Dictionary<string, object?>;
        Assert.NotNull(memories);
        var activeEncounters = memories["active_encounters"] as Dictionary<string, object?>;
        Assert.NotNull(activeEncounters);
        Assert.Equal("encounter-001", activeEncounters["current"]);
    }

    [Fact]
    public async Task StateUpdateHandler_ExecuteAsync_ThrowsOnMissingPath()
    {
        // Arrange
        var logger = new Mock<ILogger<StateUpdateHandler>>();
        var handler = new StateUpdateHandler(logger.Object);

        var (context, _) = CreateExecutionContext();
        var action = new DomainAction("state_update", new Dictionary<string, object?>
        {
            ["operation"] = "set",
            ["value"] = "test"
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    // =========================================================================
    // SCHEDULE_EVENT HANDLER TESTS
    // =========================================================================

    [Fact]
    public void ScheduleEventHandler_CanHandle_ReturnsTrueForScheduleEvent()
    {
        // Arrange
        var scheduledEventManager = new Mock<IScheduledEventManager>();
        var logger = new Mock<ILogger<ScheduleEventHandler>>();
        var handler = new ScheduleEventHandler(scheduledEventManager.Object, logger.Object);

        var action = new DomainAction("schedule_event", new Dictionary<string, object?>
        {
            ["delay_ms"] = 5000,
            ["event_type"] = "timeout"
        });

        // Act
        var canHandle = handler.CanHandle(action);

        // Assert
        Assert.True(canHandle);
    }

    [Fact]
    public async Task ScheduleEventHandler_ExecuteAsync_SchedulesEvent()
    {
        // Arrange
        ScheduledEvent? capturedEvent = null;
        var scheduledEventManager = new Mock<IScheduledEventManager>();
        scheduledEventManager.Setup(m => m.Schedule(It.IsAny<ScheduledEvent>()))
            .Callback<ScheduledEvent>(e => capturedEvent = e);

        var logger = new Mock<ILogger<ScheduleEventHandler>>();
        var handler = new ScheduleEventHandler(scheduledEventManager.Object, logger.Object);

        var characterId = Guid.Parse("00000000-0000-0000-0000-000000000456");
        var (context, scope) = CreateExecutionContext();
        scope.SetValue("actor_id", "actor-456");
        scope.SetValue("character_id", characterId.ToString()); // NPC Brain actors have character_id in scope

        var action = new DomainAction("schedule_event", new Dictionary<string, object?>
        {
            ["delay_ms"] = 3000,
            ["event_type"] = "choreography.timeout",
            ["data"] = new Dictionary<string, object?>
            {
                ["encounter_id"] = "enc-001"
            }
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        Assert.NotNull(capturedEvent);
        Assert.Equal(characterId, capturedEvent.TargetCharacterId);
        Assert.Equal("actor-456", capturedEvent.SourceActorId);
        Assert.Equal("choreography.timeout", capturedEvent.EventType);
        Assert.True((capturedEvent.FireAt - capturedEvent.ScheduledAt).TotalMilliseconds >= 3000);
    }

    [Fact]
    public async Task ScheduleEventHandler_ExecuteAsync_ThrowsOnMissingDelayMs()
    {
        // Arrange
        var scheduledEventManager = new Mock<IScheduledEventManager>();
        var logger = new Mock<ILogger<ScheduleEventHandler>>();
        var handler = new ScheduleEventHandler(scheduledEventManager.Object, logger.Object);

        var (context, _) = CreateExecutionContext();
        var action = new DomainAction("schedule_event", new Dictionary<string, object?>
        {
            ["event_type"] = "test"
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    // =========================================================================
    // QUERY_ACTOR_STATE HANDLER TESTS
    // =========================================================================

    [Fact]
    public void QueryActorStateHandler_CanHandle_ReturnsTrueForQueryActorState()
    {
        // Arrange
        var actorRegistry = new Mock<IActorRegistry>();
        var logger = new Mock<ILogger<QueryActorStateHandler>>();
        var handler = new QueryActorStateHandler(actorRegistry.Object, logger.Object);

        var action = new DomainAction("query_actor_state", new Dictionary<string, object?>
        {
            ["actor_id"] = "actor-123"
        });

        // Act
        var canHandle = handler.CanHandle(action);

        // Assert
        Assert.True(canHandle);
    }

    [Fact]
    public async Task QueryActorStateHandler_ExecuteAsync_StoresNullWhenActorNotFound()
    {
        // Arrange
        var actorRegistry = new Mock<IActorRegistry>();
        actorRegistry.Setup(r => r.TryGet(It.IsAny<string>(), out It.Ref<IActorRunner?>.IsAny))
            .Returns(false);

        var logger = new Mock<ILogger<QueryActorStateHandler>>();
        var handler = new QueryActorStateHandler(actorRegistry.Object, logger.Object);

        var (context, scope) = CreateExecutionContext();
        var action = new DomainAction("query_actor_state", new Dictionary<string, object?>
        {
            ["actor_id"] = "nonexistent-actor",
            ["result_variable"] = "actor_data"
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var storedValue = scope.GetValue("actor_data");
        Assert.Null(storedValue);
    }

    [Fact]
    public async Task QueryActorStateHandler_ExecuteAsync_ThrowsOnMissingActorId()
    {
        // Arrange
        var actorRegistry = new Mock<IActorRegistry>();
        var logger = new Mock<ILogger<QueryActorStateHandler>>();
        var handler = new QueryActorStateHandler(actorRegistry.Object, logger.Object);

        var (context, _) = CreateExecutionContext();
        var action = new DomainAction("query_actor_state", new Dictionary<string, object?>
        {
            ["result_variable"] = "actor_data"
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    // =========================================================================
    // QUERY_OPTIONS HANDLER TESTS
    // =========================================================================

    [Fact]
    public void QueryOptionsHandler_CanHandle_ReturnsTrueForQueryOptions()
    {
        // Arrange
        var actorClient = new Mock<IActorClient>();
        var logger = new Mock<ILogger<QueryOptionsHandler>>();
        var handler = new QueryOptionsHandler(actorClient.Object, logger.Object);

        var action = new DomainAction("query_options", new Dictionary<string, object?>
        {
            ["actor_id"] = "actor-123",
            ["query_type"] = "combat"
        });

        // Act
        var canHandle = handler.CanHandle(action);

        // Assert
        Assert.True(canHandle);
    }

    [Fact]
    public async Task QueryOptionsHandler_ExecuteAsync_StoresResultInVariable()
    {
        // Arrange
        var expectedResponse = new QueryOptionsResponse
        {
            ActorId = "actor-123",
            QueryType = OptionsQueryType.Combat,
            Options = new List<ActorOption>
            {
                new ActorOption { ActionId = "sword_slash", Preference = 0.8f, Available = true }
            },
            ComputedAt = DateTimeOffset.UtcNow,
            AgeMs = 100
        };

        var actorClient = new Mock<IActorClient>();
        actorClient.Setup(c => c.QueryOptionsAsync(
                It.IsAny<QueryOptionsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var logger = new Mock<ILogger<QueryOptionsHandler>>();
        var handler = new QueryOptionsHandler(actorClient.Object, logger.Object);

        var (context, scope) = CreateExecutionContext();
        var action = new DomainAction("query_options", new Dictionary<string, object?>
        {
            ["actor_id"] = "actor-123",
            ["query_type"] = "combat",
            ["result_variable"] = "participant_options"
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var storedValue = scope.GetValue("participant_options");
        Assert.Same(expectedResponse, storedValue);
    }

    [Fact]
    public async Task QueryOptionsHandler_ExecuteAsync_ThrowsOnMissingActorId()
    {
        // Arrange
        var actorClient = new Mock<IActorClient>();
        var logger = new Mock<ILogger<QueryOptionsHandler>>();
        var handler = new QueryOptionsHandler(actorClient.Object, logger.Object);

        var (context, _) = CreateExecutionContext();
        var action = new DomainAction("query_options", new Dictionary<string, object?>
        {
            ["query_type"] = "combat"
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static (AbmlExecutionContext context, VariableScope scope) CreateExecutionContext()
    {
        var scope = new VariableScope();

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate(It.IsAny<string>(), It.IsAny<IVariableScope>()))
            .Returns<string, IVariableScope>((expr, s) =>
            {
                var path = expr.TrimStart('$').Trim('{', '}');
                return s.GetValue(path);
            });

        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test-doc", Type = "behavior" },
            Flows = new Dictionary<string, Flow>()
        };

        var handlers = ActionHandlerRegistry.CreateWithBuiltins();

        var context = new AbmlExecutionContext
        {
            Document = document,
            RootScope = scope,
            Evaluator = evaluatorMock.Object,
            Handlers = handlers
        };

        return (context, scope);
    }
}
