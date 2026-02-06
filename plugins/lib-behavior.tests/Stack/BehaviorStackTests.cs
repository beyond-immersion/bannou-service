// =============================================================================
// Behavior Stack Tests
// Tests for the behavior stacking system.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior.Stack;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Stack;

/// <summary>
/// Tests for <see cref="BehaviorStack"/>.
/// </summary>
public sealed class BehaviorStackTests
{
    private readonly IArchetypeDefinition _humanoidArchetype;
    private readonly IIntentStackMerger _merger;

    public BehaviorStackTests()
    {
        var registry = new ArchetypeRegistry();
        _humanoidArchetype = registry.GetArchetype("humanoid")!;
        _merger = new IntentStackMerger();
    }

    // =========================================================================
    // LAYER MANAGEMENT TESTS
    // =========================================================================

    [Fact]
    public void AddLayer_AddsLayerToStack()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer = DelegateBehaviorLayer.CreateStatic(
            "test-layer",
            "Test Layer",
            BehaviorCategory.Base,
            0);

        // Act
        stack.AddLayer(layer);

        // Assert
        Assert.Single(stack.Layers);
        Assert.Equal("test-layer", stack.Layers[0].Id);
    }

    [Fact]
    public void AddLayer_DuplicateId_ThrowsArgumentException()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer1 = DelegateBehaviorLayer.CreateStatic("dup", "Layer 1", BehaviorCategory.Base, 0);
        var layer2 = DelegateBehaviorLayer.CreateStatic("dup", "Layer 2", BehaviorCategory.Base, 0);

        stack.AddLayer(layer1);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => stack.AddLayer(layer2));
    }

    [Fact]
    public void AddLayer_RaisesLayerAddedEvent()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);
        var layer = DelegateBehaviorLayer.CreateStatic("test", "Test", BehaviorCategory.Base, 0);

        BehaviorLayerEventArgs? receivedArgs = null;
        stack.LayerAdded += (_, args) => receivedArgs = args;

        // Act
        stack.AddLayer(layer);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Same(layer, receivedArgs.Layer);
    }

    [Fact]
    public void AddLayer_SortsByCategory()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var situational = DelegateBehaviorLayer.CreateStatic("situational", "Situational", BehaviorCategory.Situational, 0);
        var baseBehavior = DelegateBehaviorLayer.CreateStatic("base", "Base", BehaviorCategory.Base, 0);
        var professional = DelegateBehaviorLayer.CreateStatic("professional", "Professional", BehaviorCategory.Professional, 0);

        // Act - add in random order
        stack.AddLayer(situational);
        stack.AddLayer(baseBehavior);
        stack.AddLayer(professional);

        // Assert - should be sorted: Base, Professional, Situational
        Assert.Equal("base", stack.Layers[0].Id);
        Assert.Equal("professional", stack.Layers[1].Id);
        Assert.Equal("situational", stack.Layers[2].Id);
    }

    [Fact]
    public void AddLayer_SortsByPriorityWithinCategory()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var low = DelegateBehaviorLayer.CreateStatic("low", "Low", BehaviorCategory.Personal, 10);
        var high = DelegateBehaviorLayer.CreateStatic("high", "High", BehaviorCategory.Personal, 100);
        var medium = DelegateBehaviorLayer.CreateStatic("medium", "Medium", BehaviorCategory.Personal, 50);

        // Act
        stack.AddLayer(low);
        stack.AddLayer(high);
        stack.AddLayer(medium);

        // Assert - higher priority first within category
        Assert.Equal("high", stack.Layers[0].Id);
        Assert.Equal("medium", stack.Layers[1].Id);
        Assert.Equal("low", stack.Layers[2].Id);
    }

    [Fact]
    public void RemoveLayer_RemovesLayerFromStack()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer = DelegateBehaviorLayer.CreateStatic("test", "Test", BehaviorCategory.Base, 0);
        stack.AddLayer(layer);

        // Act
        var removed = stack.RemoveLayer("test");

        // Assert
        Assert.True(removed);
        Assert.Empty(stack.Layers);
    }

    [Fact]
    public void RemoveLayer_NonexistentId_ReturnsFalse()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        // Act
        var removed = stack.RemoveLayer("nonexistent");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void GetLayer_ReturnsCorrectLayer()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer = DelegateBehaviorLayer.CreateStatic("find-me", "Find Me", BehaviorCategory.Base, 0);
        stack.AddLayer(layer);

        // Act
        var found = stack.GetLayer("find-me");

        // Assert
        Assert.NotNull(found);
        Assert.Same(layer, found);
    }

    [Fact]
    public void GetLayersByCategory_ReturnsCorrectLayers()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("base1", "Base 1", BehaviorCategory.Base, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("personal1", "Personal 1", BehaviorCategory.Personal, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("base2", "Base 2", BehaviorCategory.Base, 0));

        // Act
        var baseLayers = stack.GetLayersByCategory(BehaviorCategory.Base);

        // Assert
        Assert.Equal(2, baseLayers.Count);
        Assert.All(baseLayers, l => Assert.Equal(BehaviorCategory.Base, l.Category));
    }

    // =========================================================================
    // ACTIVATION TESTS
    // =========================================================================

    [Fact]
    public void ActivateLayer_ActivatesInactiveLayer()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer = DelegateBehaviorLayer.CreateStatic("test", "Test", BehaviorCategory.Base, 0);
        layer.Deactivate();
        stack.AddLayer(layer);

        // Act
        var activated = stack.ActivateLayer("test");

        // Assert
        Assert.True(activated);
        Assert.True(layer.IsActive);
    }

    [Fact]
    public void DeactivateLayer_DeactivatesActiveLayer()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer = DelegateBehaviorLayer.CreateStatic("test", "Test", BehaviorCategory.Base, 0);
        stack.AddLayer(layer);

        // Act
        var deactivated = stack.DeactivateLayer("test");

        // Assert
        Assert.True(deactivated);
        Assert.False(layer.IsActive);
    }

    [Fact]
    public void ActiveLayers_ReturnsOnlyActiveLayers()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var active = DelegateBehaviorLayer.CreateStatic("active", "Active", BehaviorCategory.Base, 0);
        var inactive = DelegateBehaviorLayer.CreateStatic("inactive", "Inactive", BehaviorCategory.Base, 0);
        inactive.Deactivate();

        stack.AddLayer(active);
        stack.AddLayer(inactive);

        // Act
        var activeLayers = stack.ActiveLayers;

        // Assert
        Assert.Single(activeLayers);
        Assert.Equal("active", activeLayers[0].Id);
    }

    // =========================================================================
    // EVALUATION TESTS
    // =========================================================================

    [Fact]
    public async Task EvaluateAsync_NoActiveLayers_ReturnsEmptyOutput()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);
        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.Empty(output.MergedEmissions);
        Assert.Empty(output.AllContributions);
    }

    [Fact]
    public async Task EvaluateAsync_SingleLayer_ReturnsLayerEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var emission = new IntentEmission("movement", "walk", 0.5f);
        var layer = DelegateBehaviorLayer.CreateStatic("test", "Test", BehaviorCategory.Base, 0, emission);
        stack.AddLayer(layer);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.Single(output.MergedEmissions);
        Assert.True(output.HasEmission("movement"));
        Assert.Equal("walk", output.GetEmission("movement")!.Intent);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleLayers_MergesOutputs()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var baseLayer = DelegateBehaviorLayer.CreateStatic(
            "base", "Base", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "walk", 0.3f));

        var situationalLayer = DelegateBehaviorLayer.CreateStatic(
            "situational", "Situational", BehaviorCategory.Situational, 0,
            new IntentEmission("movement", "run", 0.8f));

        stack.AddLayer(baseLayer);
        stack.AddLayer(situationalLayer);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Situational has higher effective priority, should win
        Assert.Single(output.MergedEmissions);
        Assert.Equal("run", output.GetEmission("movement")!.Intent);
        Assert.Equal("situational", output.WinningLayers["movement"]);
    }

    [Fact]
    public async Task EvaluateAsync_InactiveLayers_AreSkipped()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var activeLayer = DelegateBehaviorLayer.CreateStatic(
            "active", "Active", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "walk", 0.5f));

        var inactiveLayer = DelegateBehaviorLayer.CreateStatic(
            "inactive", "Inactive", BehaviorCategory.Situational, 0,
            new IntentEmission("movement", "run", 0.8f));
        inactiveLayer.Deactivate();

        stack.AddLayer(activeLayer);
        stack.AddLayer(inactiveLayer);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - inactive layer should be skipped even though it has higher priority
        Assert.Equal("walk", output.GetEmission("movement")!.Intent);
        Assert.Single(output.AllContributions);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleChannels_MergesIndependently()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var layer1 = DelegateBehaviorLayer.CreateStatic(
            "layer1", "Layer 1", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "walk", 0.5f),
            new IntentEmission("attention", "look_around", 0.3f));

        var layer2 = DelegateBehaviorLayer.CreateStatic(
            "layer2", "Layer 2", BehaviorCategory.Personal, 0,
            new IntentEmission("expression", "smile", 0.6f));

        stack.AddLayer(layer1);
        stack.AddLayer(layer2);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - should have outputs on all three channels
        Assert.Equal(3, output.MergedEmissions.Count);
        Assert.True(output.HasEmission("movement"));
        Assert.True(output.HasEmission("attention"));
        Assert.True(output.HasEmission("expression"));
    }

    // =========================================================================
    // CLEAR TESTS
    // =========================================================================

    [Fact]
    public void Clear_RemovesAllLayers()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("1", "One", BehaviorCategory.Base, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("2", "Two", BehaviorCategory.Personal, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("3", "Three", BehaviorCategory.Situational, 0));

        // Act
        stack.Clear();

        // Assert
        Assert.Empty(stack.Layers);
    }

    [Fact]
    public void Clear_RaisesLayerRemovedEvents()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("1", "One", BehaviorCategory.Base, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("2", "Two", BehaviorCategory.Base, 0));

        var removedCount = 0;
        stack.LayerRemoved += (_, _) => removedCount++;

        // Act
        stack.Clear();

        // Assert
        Assert.Equal(2, removedCount);
    }
}
