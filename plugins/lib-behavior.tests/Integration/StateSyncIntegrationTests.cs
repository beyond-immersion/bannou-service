// =============================================================================
// State Sync Integration Tests
// Tests end-to-end flow from cinematic completion through StateSync to
// EntityStateRegistry, verifying the behavior system can read synced state.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using BeyondImmersion.BannouService.Behavior.Runtime;
using BeyondImmersion.BannouService.Behavior.Tests.Runtime;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for the StateSync system covering the full flow from
/// cinematic completion to state registry update to behavior system consumption.
/// </summary>
public sealed class StateSyncIntegrationTests : IDisposable
{
    private readonly EntityStateRegistry _stateRegistry;
    private readonly StateSync _stateSync;
    private readonly ControlGateManager _controlGates;
    private readonly BehaviorCompiler _compiler;

    public StateSyncIntegrationTests()
    {
        _stateRegistry = new EntityStateRegistry();
        _stateSync = new StateSync(_stateRegistry);
        _controlGates = new ControlGateManager();
        _compiler = new BehaviorCompiler();
    }

    public void Dispose()
    {
        _stateRegistry.Clear();
        _controlGates.Clear();
    }

    // =========================================================================
    // STATESYNC → ENTITYSTATEREGISTRY FLOW
    // =========================================================================

    [Fact]
    public async Task StateSync_AfterCinematic_WritesStateToRegistry()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var finalState = new EntityState
        {
            Position = new Vector3(100, 0, 200),
            Rotation = new Vector3(0, 45, 0),
            Health = 0.75f,
            Stance = "combat",
            Emotion = "focused"
        };
        var handoff = ControlHandoff.InstantWithState(finalState);

        // Act - Sync state as if cinematic completed
        await _stateSync.SyncStateAsync(entityId, finalState, handoff, CancellationToken.None);

        // Assert - Registry contains the synced state
        Assert.True(_stateRegistry.HasState(entityId));
        var registryState = _stateRegistry.GetState(entityId);
        Assert.NotNull(registryState);
        Assert.Equal(new Vector3(100, 0, 200), registryState.Position);
        Assert.Equal(new Vector3(0, 45, 0), registryState.Rotation);
        Assert.Equal(0.75f, registryState.Health);
        Assert.Equal("combat", registryState.Stance);
        Assert.Equal("focused", registryState.Emotion);
    }

    [Fact]
    public async Task StateSync_MultipleEntities_TracksAllIndependently()
    {
        // Arrange
        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();
        var entity3 = Guid.NewGuid();

        var state1 = new EntityState { Position = new Vector3(10, 0, 10), Health = 1.0f };
        var state2 = new EntityState { Position = new Vector3(20, 0, 20), Health = 0.5f };
        var state3 = new EntityState { Position = new Vector3(30, 0, 30), Health = 0.25f };

        var handoff = ControlHandoff.Instant();

        // Act - Sync all three entities
        await _stateSync.SyncStateAsync(entity1, state1, handoff, CancellationToken.None);
        await _stateSync.SyncStateAsync(entity2, state2, handoff, CancellationToken.None);
        await _stateSync.SyncStateAsync(entity3, state3, handoff, CancellationToken.None);

        // Assert - All three tracked independently
        Assert.Equal(3, _stateRegistry.Count);

        var retrieved1 = _stateRegistry.GetState(entity1);
        var retrieved2 = _stateRegistry.GetState(entity2);
        var retrieved3 = _stateRegistry.GetState(entity3);

        Assert.Equal(new Vector3(10, 0, 10), retrieved1?.Position);
        Assert.Equal(new Vector3(20, 0, 20), retrieved2?.Position);
        Assert.Equal(new Vector3(30, 0, 30), retrieved3?.Position);

        Assert.Equal(1.0f, retrieved1?.Health);
        Assert.Equal(0.5f, retrieved2?.Health);
        Assert.Equal(0.25f, retrieved3?.Health);
    }

    [Fact]
    public async Task StateSync_RaisesRegistryEvent_WithCorrectSource()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var state = new EntityState { Position = new Vector3(50, 0, 50) };
        var handoff = ControlHandoff.Instant();

        EntityStateUpdatedEventArgs? receivedArgs = null;
        _stateRegistry.StateUpdated += (_, args) => receivedArgs = args;

        // Act
        await _stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert - Event raised with "cinematic" source
        Assert.NotNull(receivedArgs);
        Assert.Equal(entityId, receivedArgs.EntityId);
        Assert.Equal("cinematic", receivedArgs.Source);
        Assert.Same(state, receivedArgs.NewState);
        Assert.Null(receivedArgs.PreviousState); // First update
    }

    [Fact]
    public async Task StateSync_SequentialUpdates_TracksPreviousState()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var firstState = new EntityState { Position = new Vector3(0, 0, 0), Health = 1.0f };
        var secondState = new EntityState { Position = new Vector3(100, 0, 100), Health = 0.5f };
        var handoff = ControlHandoff.Instant();

        EntityState? capturedPreviousState = null;
        _stateRegistry.StateUpdated += (_, args) => capturedPreviousState = args.PreviousState;

        // Act - First sync
        await _stateSync.SyncStateAsync(entityId, firstState, handoff, CancellationToken.None);
        // Second sync
        await _stateSync.SyncStateAsync(entityId, secondState, handoff, CancellationToken.None);

        // Assert - Second event captured first state as previous
        Assert.NotNull(capturedPreviousState);
        Assert.Equal(new Vector3(0, 0, 0), capturedPreviousState.Position);
        Assert.Equal(1.0f, capturedPreviousState.Health);

        // Current state is second state
        var current = _stateRegistry.GetState(entityId);
        Assert.Equal(new Vector3(100, 0, 100), current?.Position);
        Assert.Equal(0.5f, current?.Health);
    }

    // =========================================================================
    // CINEMATICCONTROLLER → STATESYNC INTEGRATION
    // =========================================================================

    [Fact]
    public async Task CinematicRunner_Complete_SyncsEntityStateToRegistry()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var interpreter = CreateMockInterpreter();
        var controller = new CinematicRunner(
            interpreter,
            _controlGates,
            _stateSync);

        var finalState = new EntityState
        {
            Position = new Vector3(999, 0, 999),
            Stance = "victorious",
            Emotion = "triumphant"
        };

        // Start cinematic and take control
        await controller.StartAsync(
            "test-cinematic",
            new[] { entityId },
            null,
            ControlHandoff.InstantWithState(finalState));

        // Set final state for entity
        controller.SetEntityFinalState(entityId, finalState);

        // Act - Complete the cinematic
        await controller.CompleteAsync(ControlHandoff.Instant());

        // Assert - State was synced to registry
        Assert.True(_stateRegistry.HasState(entityId));
        var syncedState = _stateRegistry.GetState(entityId);
        Assert.NotNull(syncedState);
        Assert.Equal(new Vector3(999, 0, 999), syncedState.Position);
        Assert.Equal("victorious", syncedState.Stance);
        Assert.Equal("triumphant", syncedState.Emotion);
    }

    [Fact]
    public async Task CinematicRunner_MultipleEntities_SyncsAllToRegistry()
    {
        // Arrange
        var entities = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var interpreter = CreateMockInterpreter();
        var controller = new CinematicRunner(
            interpreter,
            _controlGates,
            _stateSync);

        // Start cinematic
        await controller.StartAsync(
            "group-cinematic",
            entities,
            null,
            ControlHandoff.Instant());

        // Set different final states for each entity
        controller.SetEntityFinalState(entities[0], new EntityState
        {
            Position = new Vector3(10, 0, 10),
            Stance = "standing"
        });
        controller.SetEntityFinalState(entities[1], new EntityState
        {
            Position = new Vector3(20, 0, 20),
            Stance = "kneeling"
        });
        controller.SetEntityFinalState(entities[2], new EntityState
        {
            Position = new Vector3(30, 0, 30),
            Stance = "prone"
        });

        // Act - Complete
        await controller.CompleteAsync(ControlHandoff.Instant());

        // Assert - All three synced independently
        Assert.Equal(3, _stateRegistry.Count);

        Assert.Equal(new Vector3(10, 0, 10), _stateRegistry.GetState(entities[0])?.Position);
        Assert.Equal(new Vector3(20, 0, 20), _stateRegistry.GetState(entities[1])?.Position);
        Assert.Equal(new Vector3(30, 0, 30), _stateRegistry.GetState(entities[2])?.Position);

        Assert.Equal("standing", _stateRegistry.GetState(entities[0])?.Stance);
        Assert.Equal("kneeling", _stateRegistry.GetState(entities[1])?.Stance);
        Assert.Equal("prone", _stateRegistry.GetState(entities[2])?.Stance);
    }

    [Fact]
    public async Task CinematicRunner_Abort_DoesNotSyncState()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var interpreter = CreateMockInterpreter();
        var controller = new CinematicRunner(
            interpreter,
            _controlGates,
            _stateSync);

        await controller.StartAsync(
            "abort-cinematic",
            new[] { entityId },
            null,
            ControlHandoff.Instant());

        controller.SetEntityFinalState(entityId, new EntityState
        {
            Position = new Vector3(999, 999, 999)
        });

        // Act - Abort instead of complete
        await controller.AbortAsync();

        // Assert - State was NOT synced (abort doesn't sync)
        Assert.False(_stateRegistry.HasState(entityId));
    }

    [Fact]
    public async Task CinematicRunner_Complete_WithSyncStateFalse_DoesNotSyncState()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var interpreter = CreateMockInterpreter();
        var controller = new CinematicRunner(
            interpreter,
            _controlGates,
            _stateSync);

        await controller.StartAsync(
            "nosync-cinematic",
            new[] { entityId },
            null,
            new ControlHandoff(HandoffStyle.Instant, null, SyncState: false));

        controller.SetEntityFinalState(entityId, new EntityState
        {
            Position = new Vector3(111, 0, 111)
        });

        // Act - Complete with SyncState=false handoff
        await controller.CompleteAsync(new ControlHandoff(HandoffStyle.Instant, null, SyncState: false));

        // Assert - State was NOT synced because SyncState=false
        Assert.False(_stateRegistry.HasState(entityId));
    }

    // =========================================================================
    // FULL END-TO-END FLOW
    // =========================================================================

    [Fact]
    public async Task FullFlow_CinematicToStateSyncToBehaviorRead()
    {
        // Arrange - Full end-to-end scenario
        var entityId = Guid.NewGuid();
        var interpreter = CreateMockInterpreter();
        var controller = new CinematicRunner(
            interpreter,
            _controlGates,
            _stateSync);

        // Entity starts with no tracked state
        Assert.False(_stateRegistry.HasState(entityId));

        // Start cinematic - entity under cinematic control
        await controller.StartAsync(
            "full-flow-cinematic",
            new[] { entityId },
            null,
            ControlHandoff.Instant());

        // During cinematic, entity state changes
        var cinematicFinalState = new EntityState
        {
            Position = new Vector3(500, 10, 300),
            Rotation = new Vector3(0, 180, 0),
            Health = 0.6f,
            Stance = "defensive",
            Emotion = "wary",
            CurrentTarget = Guid.NewGuid(),
            AdditionalState = new Dictionary<string, object>
            {
                ["stamina"] = 0.4f,
                ["alertLevel"] = "high"
            }
        };
        controller.SetEntityFinalState(entityId, cinematicFinalState);

        // Act - Complete cinematic with state sync
        await controller.CompleteAsync(ControlHandoff.InstantWithState(cinematicFinalState));

        // Assert - Behavior system can now read the post-cinematic state
        var behaviorReadState = _stateRegistry.GetState(entityId);
        Assert.NotNull(behaviorReadState);

        // Position/rotation are correct
        Assert.Equal(new Vector3(500, 10, 300), behaviorReadState.Position);
        Assert.Equal(new Vector3(0, 180, 0), behaviorReadState.Rotation);

        // Health and stance preserved
        Assert.Equal(0.6f, behaviorReadState.Health);
        Assert.Equal("defensive", behaviorReadState.Stance);
        Assert.Equal("wary", behaviorReadState.Emotion);

        // Target preserved
        Assert.Equal(cinematicFinalState.CurrentTarget, behaviorReadState.CurrentTarget);

        // Additional state preserved
        Assert.NotNull(behaviorReadState.AdditionalState);
        Assert.Equal(0.4f, Convert.ToSingle(behaviorReadState.AdditionalState["stamina"]));
        Assert.Equal("high", behaviorReadState.AdditionalState["alertLevel"]);
    }

    [Fact]
    public async Task FullFlow_MultipleCinematics_OverwritesPreviousState()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var interpreter = CreateMockInterpreter();
        var controller1 = new CinematicRunner(interpreter, _controlGates, _stateSync);
        var controller2 = new CinematicRunner(CreateMockInterpreter(), _controlGates, _stateSync);

        // First cinematic
        await controller1.StartAsync("cinematic-1", new[] { entityId }, null, ControlHandoff.Instant());
        controller1.SetEntityFinalState(entityId, new EntityState
        {
            Position = new Vector3(100, 0, 100),
            Stance = "first"
        });
        await controller1.CompleteAsync();

        // Verify first state
        Assert.Equal(new Vector3(100, 0, 100), _stateRegistry.GetState(entityId)?.Position);
        Assert.Equal("first", _stateRegistry.GetState(entityId)?.Stance);

        // Second cinematic overwrites
        await controller2.StartAsync("cinematic-2", new[] { entityId }, null, ControlHandoff.Instant());
        controller2.SetEntityFinalState(entityId, new EntityState
        {
            Position = new Vector3(999, 0, 999),
            Stance = "second"
        });
        await controller2.CompleteAsync();

        // Assert - Second cinematic's state is now current
        Assert.Equal(new Vector3(999, 0, 999), _stateRegistry.GetState(entityId)?.Position);
        Assert.Equal("second", _stateRegistry.GetState(entityId)?.Stance);

        // Still only one entity tracked
        Assert.Equal(1, _stateRegistry.Count);
    }

    [Fact]
    public async Task FullFlow_WithBlendHandoff_SyncsImmediately()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var interpreter = CreateMockInterpreter();
        var controller = new CinematicRunner(interpreter, _controlGates, _stateSync);

        var finalState = new EntityState
        {
            Position = new Vector3(50, 0, 50),
            Stance = "relaxed"
        };

        await controller.StartAsync("blend-cinematic", new[] { entityId }, null, ControlHandoff.Instant());
        controller.SetEntityFinalState(entityId, finalState);

        // Act - Complete with blend handoff (1 second blend, with state for sync)
        await controller.CompleteAsync(ControlHandoff.Blend(TimeSpan.FromSeconds(1), finalState));

        // Assert - State is synced immediately (blend is client-side visual only)
        Assert.True(_stateRegistry.HasState(entityId));
        var syncedState = _stateRegistry.GetState(entityId);
        Assert.Equal(new Vector3(50, 0, 50), syncedState?.Position);
        Assert.Equal("relaxed", syncedState?.Stance);
    }

    // =========================================================================
    // REGISTRY QUERY TESTS
    // =========================================================================

    [Fact]
    public async Task Registry_GetStateOrEmpty_ReturnsEmptyForUnknownEntity()
    {
        // Arrange - Sync a known entity
        var knownEntity = Guid.NewGuid();
        var unknownEntity = Guid.NewGuid();

        await _stateSync.SyncStateAsync(
            knownEntity,
            new EntityState { Health = 0.5f },
            ControlHandoff.Instant(),
            CancellationToken.None);

        // Act
        var knownState = _stateRegistry.GetStateOrEmpty(knownEntity);
        var unknownState = _stateRegistry.GetStateOrEmpty(unknownEntity);

        // Assert - Known has data, unknown is empty (not null)
        Assert.NotNull(knownState);
        Assert.Equal(0.5f, knownState.Health);

        Assert.NotNull(unknownState);
        Assert.Null(unknownState.Health);
        Assert.Null(unknownState.Position);
    }

    [Fact]
    public async Task Registry_GetTrackedEntityIds_ReturnsAllSyncedEntities()
    {
        // Arrange
        var entities = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var handoff = ControlHandoff.Instant();

        foreach (var entityId in entities)
        {
            await _stateSync.SyncStateAsync(
                entityId,
                new EntityState { Health = 1.0f },
                handoff,
                CancellationToken.None);
        }

        // Act
        var trackedIds = _stateRegistry.GetTrackedEntityIds();

        // Assert
        Assert.Equal(3, trackedIds.Count);
        foreach (var entityId in entities)
        {
            Assert.Contains(entityId, trackedIds);
        }
    }

    [Fact]
    public async Task Registry_Clear_RemovesAllSyncedState()
    {
        // Arrange
        var entities = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var handoff = ControlHandoff.Instant();

        foreach (var entityId in entities)
        {
            await _stateSync.SyncStateAsync(
                entityId,
                new EntityState { Health = 1.0f },
                handoff,
                CancellationToken.None);
        }

        Assert.Equal(2, _stateRegistry.Count);

        // Act
        _stateRegistry.Clear();

        // Assert
        Assert.Equal(0, _stateRegistry.Count);
        foreach (var entityId in entities)
        {
            Assert.False(_stateRegistry.HasState(entityId));
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private CinematicInterpreter CreateMockInterpreter()
    {
        // Load and compile a minimal cinematic from fixtures
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var result = _compiler.CompileYaml(yaml);
        if (!result.Success || result.Bytecode is null)
        {
            throw new InvalidOperationException("Failed to compile test fixture");
        }
        var model = BehaviorModel.Deserialize(result.Bytecode);
        return new CinematicInterpreter(model);
    }
}
