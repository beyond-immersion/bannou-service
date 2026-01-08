// =============================================================================
// ABML Emission Integration Tests
// Tests DomainActionHandler integration with mocked behavior interfaces.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Execution.Handlers;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Actor.Tests.Integration;

/// <summary>
/// Integration tests for ABML domain action handling with mocked behavior interfaces.
/// Tests the DomainActionHandler's integration with IIntentEmitterRegistry,
/// IArchetypeRegistry, and IControlGateRegistry.
/// </summary>
public sealed class AbmlEmissionIntegrationTests
{
    // =========================================================================
    // DOMAIN ACTION HANDLER BASIC TESTS
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_WithEmitter_EmitsIntentsToScope()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("locomotion", "walk", 0.5f);

        var emitterMock = CreateMockEmitter("walk_to", new[] { emission });
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var action = new DomainAction("walk_to", new Dictionary<string, object?>
        {
            ["target"] = "waypoint_1"
        });

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var emissions = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        Assert.NotNull(emissions);
        Assert.Single(emissions);
        Assert.Equal("locomotion", emissions[0].Channel);
        Assert.Equal("walk", emissions[0].Intent);
        Assert.Equal(0.5f, emissions[0].Urgency);
    }

    [Fact]
    public async Task ExecuteAsync_NoEmitterFound_LogsAndContinues()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emitterRegistry = new Mock<IIntentEmitterRegistry>();
        emitterRegistry.Setup(r => r.GetEmitter(It.IsAny<string>(), It.IsAny<IntentEmissionContext>()))
            .Returns((IIntentEmitter?)null);

        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var action = new DomainAction("unknown_action", new Dictionary<string, object?>());

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var emissions = scope.GetValue("_intent_emissions");
        Assert.Null(emissions); // No emissions stored
        Assert.Contains(context.Logs, log => log.Message.Contains("no emitter"));
    }

    [Fact]
    public async Task ExecuteAsync_WithCallback_UsesCallbackInsteadOfEmitter()
    {
        // Arrange
        var callbackCalled = false;
        var handler = new DomainActionHandler((name, parameters) =>
        {
            callbackCalled = true;
            Assert.Equal("custom_action", name);
            return ValueTask.FromResult(ActionResult.Continue);
        });

        var (context, _) = CreateExecutionContext(Guid.NewGuid());
        var action = new DomainAction("custom_action", new Dictionary<string, object?>());

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        Assert.True(callbackCalled);
    }

    // =========================================================================
    // CONTROL GATE FILTERING TESTS
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_ControlGateBlocks_EmissionsAreFiltered()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("combat", "attack", 0.9f);

        var emitterMock = CreateMockEmitter("attack", new[] { emission });
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();

        // Control gate that filters everything out
        var controlGateRegistry = CreateMockControlGateRegistry(
            entityId,
            passThrough: false,
            filteredEmissions: Array.Empty<IntentEmission>());

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var action = new DomainAction("attack", new Dictionary<string, object?>());

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var emissions = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        // Emissions list should not exist or be empty since all were filtered
        Assert.True(emissions == null || emissions.Count == 0);
    }

    [Fact]
    public async Task ExecuteAsync_ControlGatePartialFilter_SomeEmissionsPass()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emissions = new[]
        {
            new IntentEmission("locomotion", "walk", 0.5f),
            new IntentEmission("expression", "smile", 0.3f)
        };
        var filteredEmissions = new[]
        {
            new IntentEmission("locomotion", "walk", 0.5f)
        };

        var emitterMock = CreateMockEmitter("move_to", emissions);
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(
            entityId,
            passThrough: false,
            filteredEmissions: filteredEmissions);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var action = new DomainAction("move_to", new Dictionary<string, object?>());

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var storedEmissions = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        Assert.NotNull(storedEmissions);
        Assert.Single(storedEmissions);
        Assert.Equal("locomotion", storedEmissions[0].Channel);
    }

    [Fact]
    public async Task ExecuteAsync_NoControlGate_EmissionsPassThrough()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("action", "interact", 0.7f);

        var emitterMock = CreateMockEmitter("interact", new[] { emission });
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();

        // Control gate registry returns null (no gate for entity)
        var controlGateRegistry = new Mock<IControlGateRegistry>();
        controlGateRegistry.Setup(r => r.Get(It.IsAny<Guid>())).Returns((IControlGate?)null);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var action = new DomainAction("interact", new Dictionary<string, object?>());

        // Act
        var result = await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(ActionResult.Continue, result);
        var emissions = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        Assert.NotNull(emissions);
        Assert.Single(emissions);
    }

    // =========================================================================
    // ENTITY AND ARCHETYPE RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_ResolvesEntityIdFromAgentScope()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("locomotion", "walk", 0.5f);

        Guid? capturedEntityId = null;
        var emitterMock = new Mock<IIntentEmitter>();
        emitterMock.Setup(e => e.ActionName).Returns("walk_to");
        emitterMock.Setup(e => e.CanEmit(It.IsAny<string>(), It.IsAny<IntentEmissionContext>())).Returns(true);
        emitterMock.Setup(e => e.EmitAsync(
                It.IsAny<IReadOnlyDictionary<string, object>>(),
                It.IsAny<IntentEmissionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<string, object>, IntentEmissionContext, CancellationToken>(
                (_, ctx, _) => capturedEntityId = ctx.EntityId)
            .ReturnsAsync(new[] { emission });

        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContextWithAgent(entityId, "humanoid");
        var action = new DomainAction("walk_to", new Dictionary<string, object?>());

        // Act
        await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Equal(entityId, capturedEntityId);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesArchetypeFromScope()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("locomotion", "walk", 0.5f);

        IArchetypeDefinition? capturedArchetype = null;
        var emitterMock = new Mock<IIntentEmitter>();
        emitterMock.Setup(e => e.ActionName).Returns("walk_to");
        emitterMock.Setup(e => e.CanEmit(It.IsAny<string>(), It.IsAny<IntentEmissionContext>())).Returns(true);
        emitterMock.Setup(e => e.EmitAsync(
                It.IsAny<IReadOnlyDictionary<string, object>>(),
                It.IsAny<IntentEmissionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<string, object>, IntentEmissionContext, CancellationToken>(
                (_, ctx, _) => capturedArchetype = ctx.Archetype)
            .ReturnsAsync(new[] { emission });

        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);

        var archetypeMock = new Mock<IArchetypeDefinition>();
        archetypeMock.Setup(a => a.Id).Returns("humanoid");
        var archetypeRegistry = new Mock<IArchetypeRegistry>();
        archetypeRegistry.Setup(r => r.GetArchetype("humanoid")).Returns(archetypeMock.Object);
        archetypeRegistry.Setup(r => r.GetDefaultArchetype()).Returns(archetypeMock.Object);

        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, _) = CreateExecutionContextWithAgent(entityId, "humanoid");
        var action = new DomainAction("walk_to", new Dictionary<string, object?>());

        // Act
        await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedArchetype);
        Assert.Equal("humanoid", capturedArchetype.Id);
    }

    // =========================================================================
    // MULTIPLE EMISSIONS TESTS
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_MultipleActions_AccumulatesEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        var walkEmission = new IntentEmission("locomotion", "walk", 0.5f);
        var lookEmission = new IntentEmission("attention", "look_at", 0.7f);

        var walkEmitter = CreateMockEmitter("walk_to", new[] { walkEmission });
        var lookEmitter = CreateMockEmitter("look_at", new[] { lookEmission });

        var emitterRegistry = new Mock<IIntentEmitterRegistry>();
        emitterRegistry.Setup(r => r.GetEmitter("walk_to", It.IsAny<IntentEmissionContext>()))
            .Returns(walkEmitter.Object);
        emitterRegistry.Setup(r => r.GetEmitter("look_at", It.IsAny<IntentEmissionContext>()))
            .Returns(lookEmitter.Object);

        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var walkAction = new DomainAction("walk_to", new Dictionary<string, object?>());
        var lookAction = new DomainAction("look_at", new Dictionary<string, object?>());

        // Act
        await handler.ExecuteAsync(walkAction, context, CancellationToken.None);
        await handler.ExecuteAsync(lookAction, context, CancellationToken.None);

        // Assert
        var emissions = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        Assert.NotNull(emissions);
        Assert.Equal(2, emissions.Count);
        Assert.Contains(emissions, e => e.Channel == "locomotion");
        Assert.Contains(emissions, e => e.Channel == "attention");
    }

    [Fact]
    public async Task ExecuteAsync_EmitterReturnsMultipleEmissions_AllStored()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emissions = new[]
        {
            new IntentEmission("locomotion", "run", 0.9f),
            new IntentEmission("expression", "determined", 0.6f),
            new IntentEmission("vocalization", "breathing_heavy", 0.4f)
        };

        var emitterMock = CreateMockEmitter("sprint", emissions);
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        var action = new DomainAction("sprint", new Dictionary<string, object?>());

        // Act
        await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        var storedEmissions = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        Assert.NotNull(storedEmissions);
        Assert.Equal(3, storedEmissions.Count);
    }

    // =========================================================================
    // PARAMETER EVALUATION TESTS
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_EvaluatesParameterExpressions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("locomotion", "walk", 0.5f);

        IReadOnlyDictionary<string, object>? capturedParams = null;
        var emitterMock = new Mock<IIntentEmitter>();
        emitterMock.Setup(e => e.ActionName).Returns("walk_to");
        emitterMock.Setup(e => e.CanEmit(It.IsAny<string>(), It.IsAny<IntentEmissionContext>())).Returns(true);
        emitterMock.Setup(e => e.EmitAsync(
                It.IsAny<IReadOnlyDictionary<string, object>>(),
                It.IsAny<IntentEmissionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<string, object>, IntentEmissionContext, CancellationToken>(
                (p, _, _) => capturedParams = p)
            .ReturnsAsync(new[] { emission });

        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, scope) = CreateExecutionContext(entityId);
        scope.SetValue("target_speed", 0.8f);

        var action = new DomainAction("walk_to", new Dictionary<string, object?>
        {
            ["urgency"] = 0.5f,
            ["target"] = "waypoint_1"
        });

        // Act
        await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedParams);
        Assert.Equal(0.5f, capturedParams["urgency"]);
        Assert.Equal("waypoint_1", capturedParams["target"]);
    }

    // =========================================================================
    // LOGGING TESTS
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_WithEmissions_LogsEmissionSummary()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emission = new IntentEmission("locomotion", "walk", 0.5f);

        var emitterMock = CreateMockEmitter("walk_to", new[] { emission });
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, _) = CreateExecutionContext(entityId);
        var action = new DomainAction("walk_to", new Dictionary<string, object?>());

        // Act
        await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert
        Assert.Contains(context.Logs, log =>
            log.Level == "emit" &&
            log.Message.Contains("locomotion:walk"));
    }

    [Fact]
    public async Task ExecuteAsync_NoEmissions_DoesNotLogEmit()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        var emitterMock = CreateMockEmitter("silent_action", Array.Empty<IntentEmission>());
        var emitterRegistry = CreateMockEmitterRegistry(emitterMock);
        var archetypeRegistry = CreateMockArchetypeRegistry();
        var controlGateRegistry = CreateMockControlGateRegistry(entityId, passThrough: true);

        var handler = new DomainActionHandler(
            emitterRegistry.Object,
            archetypeRegistry.Object,
            controlGateRegistry.Object);

        var (context, _) = CreateExecutionContext(entityId);
        var action = new DomainAction("silent_action", new Dictionary<string, object?>());

        // Act
        await handler.ExecuteAsync(action, context, CancellationToken.None);

        // Assert - should not log "emit" level entry for empty emissions
        Assert.DoesNotContain(context.Logs, log => log.Level == "emit");
    }

    // =========================================================================
    // CAN HANDLE TESTS
    // =========================================================================

    [Fact]
    public void CanHandle_DomainAction_ReturnsTrue()
    {
        // Arrange
        var handler = new DomainActionHandler();
        var action = new DomainAction("test", new Dictionary<string, object?>());

        // Act
        var result = handler.CanHandle(action);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_OtherActionType_ReturnsFalse()
    {
        // Arrange
        var handler = new DomainActionHandler();
        var action = new SetAction("variable", "value");

        // Act
        var result = handler.CanHandle(action);

        // Assert
        Assert.False(result);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static Mock<IIntentEmitter> CreateMockEmitter(
        string actionName,
        IReadOnlyList<IntentEmission> emissions)
    {
        var mock = new Mock<IIntentEmitter>();
        mock.Setup(e => e.ActionName).Returns(actionName);
        mock.Setup(e => e.CanEmit(It.IsAny<string>(), It.IsAny<IntentEmissionContext>())).Returns(true);
        mock.Setup(e => e.EmitAsync(
                It.IsAny<IReadOnlyDictionary<string, object>>(),
                It.IsAny<IntentEmissionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emissions);
        return mock;
    }

    private static Mock<IIntentEmitterRegistry> CreateMockEmitterRegistry(Mock<IIntentEmitter> emitter)
    {
        var mock = new Mock<IIntentEmitterRegistry>();
        mock.Setup(r => r.GetEmitter(emitter.Object.ActionName, It.IsAny<IntentEmissionContext>()))
            .Returns(emitter.Object);
        return mock;
    }

    private static Mock<IArchetypeRegistry> CreateMockArchetypeRegistry()
    {
        var archetypeMock = new Mock<IArchetypeDefinition>();
        archetypeMock.Setup(a => a.Id).Returns("default");

        var mock = new Mock<IArchetypeRegistry>();
        mock.Setup(r => r.GetArchetype(It.IsAny<string>())).Returns(archetypeMock.Object);
        mock.Setup(r => r.GetDefaultArchetype()).Returns(archetypeMock.Object);
        return mock;
    }

    private static Mock<IControlGateRegistry> CreateMockControlGateRegistry(
        Guid entityId,
        bool passThrough,
        IReadOnlyList<IntentEmission>? filteredEmissions = null)
    {
        var gateMock = new Mock<IControlGate>();
        gateMock.Setup(g => g.EntityId).Returns(entityId);
        gateMock.Setup(g => g.CurrentSource).Returns(ControlSource.Behavior);
        gateMock.Setup(g => g.FilterEmissions(
                It.IsAny<IReadOnlyList<IntentEmission>>(),
                It.IsAny<ControlSource>()))
            .Returns<IReadOnlyList<IntentEmission>, ControlSource>((emissions, _) =>
                passThrough ? emissions : (filteredEmissions ?? Array.Empty<IntentEmission>()));

        var mock = new Mock<IControlGateRegistry>();
        mock.Setup(r => r.Get(entityId)).Returns(gateMock.Object);
        return mock;
    }

    private static (AbmlExecutionContext context, VariableScope scope) CreateExecutionContext(Guid entityId)
    {
        var scope = new VariableScope();
        scope.SetValue("entity", new Dictionary<string, object?>
        {
            ["id"] = entityId
        });

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate(It.IsAny<string>(), It.IsAny<IVariableScope>()))
            .Returns<string, IVariableScope>((expr, s) => s.GetValue(expr.TrimStart('$').Trim('{', '}')));

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

    private static (AbmlExecutionContext context, VariableScope scope) CreateExecutionContextWithAgent(
        Guid entityId, string archetypeId)
    {
        var scope = new VariableScope();
        scope.SetValue("agent", new Dictionary<string, object?>
        {
            ["id"] = entityId,
            ["archetype"] = archetypeId
        });

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate(It.IsAny<string>(), It.IsAny<IVariableScope>()))
            .Returns<string, IVariableScope>((expr, s) => s.GetValue(expr.TrimStart('$').Trim('{', '}')));

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
