// =============================================================================
// Cinematic Controller Tests
// Tests for control gating integration with cinematic playback.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using BeyondImmersion.BannouService.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Runtime;

/// <summary>
/// Tests for <see cref="CinematicRunner"/> integration with control gating.
/// </summary>
public sealed class CinematicRunnerTests
{
    private readonly BehaviorCompiler _compiler = new();

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private BehaviorModel CompileYaml(string yaml)
    {
        var result = _compiler.CompileYaml(yaml);
        if (!result.Success || result.Bytecode is null)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Compilation failed: {errors}");
        }
        return BehaviorModel.Deserialize(result.Bytecode);
    }

    private CinematicRunner CreateController(BehaviorModel model)
    {
        var interpreter = new CinematicInterpreter(model);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        return new CinematicRunner(interpreter, controlGates, stateSync);
    }

    private CinematicRunner CreateController(
        BehaviorModel model,
        ControlGateManager controlGates,
        IStateSync stateSync)
    {
        var interpreter = new CinematicInterpreter(model);
        return new CinematicRunner(interpreter, controlGates, stateSync);
    }

    // =========================================================================
    // START TESTS
    // =========================================================================

    [Fact]
    public async Task StartAsync_TakesControlOfEntities()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();
        var entities = new[] { entity1, entity2 };

        // Act
        var started = await controller.StartAsync("test_cinematic", entities);

        // Assert
        Assert.True(started);
        Assert.Equal(CinematicRunnerState.Running, controller.State);
        Assert.Equal("test_cinematic", controller.CinematicId);

        // Verify control was taken
        var gate1 = controlGates.Get(entity1);
        var gate2 = controlGates.Get(entity2);
        Assert.NotNull(gate1);
        Assert.NotNull(gate2);
        Assert.Equal(ControlSource.Cinematic, gate1.CurrentSource);
        Assert.Equal(ControlSource.Cinematic, gate2.CurrentSource);
    }

    [Fact]
    public async Task StartAsync_WithAllowBehaviorChannels_SetsChannelsOnGate()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        var allowedChannels = new HashSet<string> { "expression", "attention" };

        // Act
        await controller.StartAsync("test_cinematic", new[] { entity }, allowedChannels);

        // Assert
        var gate = controlGates.Get(entity);
        Assert.NotNull(gate);
        Assert.Contains("expression", gate.BehaviorInputChannels);
        Assert.Contains("attention", gate.BehaviorInputChannels);
    }

    [Fact]
    public async Task StartAsync_RaisesCinematicStartedEvent()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        CinematicStartedEventArgs? receivedArgs = null;
        controller.CinematicStarted += (_, args) => receivedArgs = args;

        // Act
        await controller.StartAsync("test_cinematic", new[] { entity });

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal("test_cinematic", receivedArgs.CinematicId);
        Assert.Contains(entity, receivedArgs.Entities);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_ReturnsFalse()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic_1", new[] { entity });

        // Act
        var started = await controller.StartAsync("test_cinematic_2", new[] { entity });

        // Assert
        Assert.False(started);
        Assert.Equal("test_cinematic_1", controller.CinematicId);
    }

    [Fact]
    public async Task StartAsync_WithEmptyCinematicId_ThrowsArgumentException()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => controller.StartAsync("", new[] { entity }));
    }

    // =========================================================================
    // EVALUATE TESTS
    // =========================================================================

    [Fact]
    public async Task Evaluate_DelegatestoInterpreter()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        // Act
        var result = controller.Evaluate();

        // Assert - should pause at continuation point like the interpreter
        Assert.True(result.IsWaiting);
        Assert.Equal(CinematicRunnerState.WaitingForExtension, controller.State);
    }

    [Fact]
    public void Evaluate_BeforeStart_ReturnsCompleted()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        // Act
        var result = controller.Evaluate();

        // Assert
        Assert.True(result.IsCompleted);
        Assert.Contains("not started", result.Message);
    }

    // =========================================================================
    // COMPLETE TESTS
    // =========================================================================

    [Fact]
    public async Task CompleteAsync_ReturnsControlToEntities()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();
        var entities = new[] { entity1, entity2 };

        await controller.StartAsync("test_cinematic", entities);

        // Verify control was taken
        Assert.Equal(ControlSource.Cinematic, controlGates.Get(entity1)!.CurrentSource);
        Assert.Equal(ControlSource.Cinematic, controlGates.Get(entity2)!.CurrentSource);

        // Act
        await controller.CompleteAsync();

        // Assert
        Assert.Equal(CinematicRunnerState.Completed, controller.State);
        Assert.Equal(ControlSource.Behavior, controlGates.Get(entity1)!.CurrentSource);
        Assert.Equal(ControlSource.Behavior, controlGates.Get(entity2)!.CurrentSource);
    }

    [Fact]
    public async Task CompleteAsync_RaisesCinematicCompletedEvent()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        CinematicCompletedEventArgs? receivedArgs = null;
        controller.CinematicCompleted += (_, args) => receivedArgs = args;

        // Act
        await controller.CompleteAsync();

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal("test_cinematic", receivedArgs.CinematicId);
        Assert.False(receivedArgs.WasAborted);
    }

    [Fact]
    public async Task CompleteAsync_WithFinalState_SyncsState()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        var finalState = new EntityState
        {
            Position = new System.Numerics.Vector3(10, 0, 5),
            Health = 0.75f
        };
        controller.SetEntityFinalState(entity, finalState);

        StateSyncCompletedEventArgs? syncArgs = null;
        stateSync.StateSyncCompleted += (_, args) => syncArgs = args;

        // Act
        await controller.CompleteAsync(ControlHandoff.InstantWithState(finalState));

        // Assert
        Assert.NotNull(syncArgs);
        Assert.Equal(entity, syncArgs.EntityId);
        Assert.Equal(finalState.Position, syncArgs.SyncedState.Position);
    }

    [Fact]
    public async Task CompleteAsync_RaisesControlReturnedEventForEachEntity()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity1, entity2 });

        var controlReturnedCount = 0;
        controller.ControlReturned += (_, args) => controlReturnedCount++;

        // Act
        await controller.CompleteAsync();

        // Assert
        Assert.Equal(2, controlReturnedCount);
    }

    // =========================================================================
    // ABORT TESTS
    // =========================================================================

    [Fact]
    public async Task AbortAsync_ReturnsControlImmediately()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        // Act
        await controller.AbortAsync();

        // Assert
        Assert.Equal(CinematicRunnerState.Completed, controller.State);
        Assert.Equal(ControlSource.Behavior, controlGates.Get(entity)!.CurrentSource);
    }

    [Fact]
    public async Task AbortAsync_RaisesCompletedEventWithAbortFlag()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        CinematicCompletedEventArgs? receivedArgs = null;
        controller.CinematicCompleted += (_, args) => receivedArgs = args;

        // Act
        await controller.AbortAsync();

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.True(receivedArgs.WasAborted);
    }

    [Fact]
    public async Task AbortAsync_WhenNotStarted_DoesNothing()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        // Act - should not throw
        await controller.AbortAsync();

        // Assert
        Assert.Equal(CinematicRunnerState.Idle, controller.State);
    }

    // =========================================================================
    // RESET TESTS
    // =========================================================================

    [Fact]
    public async Task Reset_ClearsControllerState()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });
        await controller.CompleteAsync();

        // Act
        controller.Reset();

        // Assert
        Assert.Equal(CinematicRunnerState.Idle, controller.State);
        Assert.Empty(controller.CinematicId);
        Assert.Empty(controller.ControlledEntities);
    }

    [Fact]
    public async Task Reset_AllowsReuse()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        using var controller = CreateController(model);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic_1", new[] { entity });
        await controller.CompleteAsync();
        controller.Reset();

        // Act - start a new cinematic after reset
        var started = await controller.StartAsync("test_cinematic_2", new[] { Guid.NewGuid() });

        // Assert
        Assert.True(started);
        Assert.Equal("test_cinematic_2", controller.CinematicId);
    }

    // =========================================================================
    // DISPOSE TESTS
    // =========================================================================

    [Fact]
    public async Task Dispose_AbortsRunningCinematic()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        // Act
        controller.Dispose();

        // Assert - control should be returned
        Assert.Equal(ControlSource.Behavior, controlGates.Get(entity)!.CurrentSource);
    }

    [Fact]
    public async Task MethodsThrowAfterDispose()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controller = CreateController(model);

        controller.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => controller.StartAsync("test", new[] { Guid.NewGuid() }));

        Assert.Throws<ObjectDisposedException>(() => controller.Evaluate());
    }

    // =========================================================================
    // CONTROL GATE INTERACTION TESTS
    // =========================================================================

    [Fact]
    public async Task BehaviorEmissions_FilteredDuringCinematic()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        var gate = controlGates.Get(entity)!;

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("combat", "attack", 0.8f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Behavior);

        // Assert - all behavior emissions should be filtered during cinematic
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task BehaviorEmissions_AllowedOnSpecifiedChannels()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        var allowedChannels = new HashSet<string> { "expression" };
        await controller.StartAsync("test_cinematic", new[] { entity }, allowedChannels);

        var gate = controlGates.Get(entity)!;

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("expression", "smile", 0.4f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Behavior);

        // Assert - only expression should pass
        Assert.Single(filtered);
        Assert.Equal("expression", filtered[0].Channel);
    }

    [Fact]
    public async Task CinematicEmissions_AlwaysPass()
    {
        // Arrange
        var yaml = RuntimeTestFixtures.Load("cinematic_base");
        var model = CompileYaml(yaml);
        var controlGates = new ControlGateManager();
        var stateSync = new StateSync();
        using var controller = CreateController(model, controlGates, stateSync);

        var entity = Guid.NewGuid();
        await controller.StartAsync("test_cinematic", new[] { entity });

        var gate = controlGates.Get(entity)!;

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("combat", "attack", 0.8f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Cinematic);

        // Assert - cinematic emissions always pass
        Assert.Equal(2, filtered.Count);
    }
}
