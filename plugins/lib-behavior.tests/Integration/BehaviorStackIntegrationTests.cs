// =============================================================================
// Behavior Stack Integration Tests
// Tests end-to-end behavior stacking with layer evaluation, merging,
// and registry management.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Stack;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for the behavior stack system covering layer stacking,
/// evaluation, merging, and registry management.
/// </summary>
public sealed class BehaviorStackIntegrationTests
{
    private readonly IArchetypeRegistry _archetypeRegistry;
    private readonly IArchetypeDefinition _humanoidArchetype;
    private readonly IIntentStackMerger _merger;

    public BehaviorStackIntegrationTests()
    {
        _archetypeRegistry = new ArchetypeRegistry();
        _humanoidArchetype = _archetypeRegistry.GetArchetype("humanoid")
            ?? throw new InvalidOperationException("Humanoid archetype not found");
        _merger = new IntentStackMerger();
    }

    // =========================================================================
    // FULL STACK EVALUATION TESTS
    // =========================================================================

    [Fact]
    public async Task EvaluateAsync_FullStackWithAllCategories_MergesCorrectly()
    {
        // Arrange - Build a realistic NPC behavior stack
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        // Base behavior: idle stance
        var baseBehavior = new DelegateBehaviorLayer(
            "humanoid-base",
            "Humanoid Base",
            BehaviorCategory.Base,
            0,
            (ctx, ct) => ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[]
            {
                new IntentEmission("expression", "idle", 0.5f)
            }));

        // Cultural behavior: formal gestures
        var culturalBehavior = new DelegateBehaviorLayer(
            "medieval-european",
            "Medieval European",
            BehaviorCategory.Cultural,
            0,
            (ctx, ct) => ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[]
            {
                new IntentEmission("expression", "bow", 0.6f)
            }));

        // Professional behavior: guard patrol
        var professionalBehavior = new DelegateBehaviorLayer(
            "guard-patrol",
            "Guard Patrol",
            BehaviorCategory.Professional,
            0,
            (ctx, ct) => ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[]
            {
                new IntentEmission("movement", "patrol", 0.7f),
                new IntentEmission("expression", "alert", 0.7f)
            }));

        // Personal trait: grumpy
        var personalBehavior = new DelegateBehaviorLayer(
            "grumpy",
            "Grumpy Personality",
            BehaviorCategory.Personal,
            0,
            (ctx, ct) => ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[]
            {
                new IntentEmission("expression", "annoyed", 0.8f)
            }));

        stack.AddLayer(baseBehavior);
        stack.AddLayer(culturalBehavior);
        stack.AddLayer(professionalBehavior);
        stack.AddLayer(personalBehavior);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Higher category layers override lower ones
        Assert.NotNull(output);
        Assert.True(output.HasEmission("movement"));
        Assert.True(output.HasEmission("expression"));

        // Personal category should win for expression channel
        Assert.Equal("grumpy", output.WinningLayers["expression"]);
    }

    [Fact]
    public async Task EvaluateAsync_SituationalOverridesAll_HighestPriority()
    {
        // Arrange - Normal stack with situational override
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        // Add base and professional layers
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "base", "Base", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "walk", 0.5f)));

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "professional", "Professional", BehaviorCategory.Professional, 0,
            new IntentEmission("movement", "patrol", 0.6f)));

        // Add situational combat layer - should override everything
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "combat-mode", "Combat Mode", BehaviorCategory.Situational, 100,
            new IntentEmission("movement", "engage", 0.9f)));

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Situational wins
        Assert.NotNull(output);
        Assert.Equal("combat-mode", output.WinningLayers["movement"]);
    }

    [Fact]
    public async Task EvaluateAsync_InactiveLayers_NotIncluded()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var activeLayer = DelegateBehaviorLayer.CreateStatic(
            "active", "Active", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "walk", 0.5f));

        var inactiveLayer = DelegateBehaviorLayer.CreateStatic(
            "inactive", "Inactive", BehaviorCategory.Base, 100,
            new IntentEmission("movement", "run", 0.9f));

        stack.AddLayer(activeLayer);
        stack.AddLayer(inactiveLayer);

        // Deactivate the higher-priority layer
        stack.DeactivateLayer("inactive");

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Only active layer contributes
        Assert.Single(output.AllContributions);
        Assert.Equal("active", output.WinningLayers["movement"]);
    }

    // =========================================================================
    // REGISTRY INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void BehaviorStackRegistry_ManagesMultipleEntities()
    {
        // Arrange
        var registry = new BehaviorStackRegistry(_merger);
        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();
        var entity3 = Guid.NewGuid();

        // Act - Create stacks for multiple entities
        var stack1 = registry.GetOrCreate(entity1, _humanoidArchetype);
        var stack2 = registry.GetOrCreate(entity2, _humanoidArchetype);
        var stack3 = registry.GetOrCreate(entity3, _humanoidArchetype);

        // Assert
        Assert.Equal(3, registry.Count);
        Assert.Contains(entity1, registry.GetEntityIds());
        Assert.Contains(entity2, registry.GetEntityIds());
        Assert.Contains(entity3, registry.GetEntityIds());
    }

    [Fact]
    public void BehaviorStackRegistry_ReusesExistingStack()
    {
        // Arrange
        var registry = new BehaviorStackRegistry(_merger);
        var entityId = Guid.NewGuid();

        // Act
        var stack1 = registry.GetOrCreate(entityId, _humanoidArchetype);
        var stack2 = registry.GetOrCreate(entityId, _humanoidArchetype);

        // Assert - Same instance
        Assert.Same(stack1, stack2);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void BehaviorStackRegistry_RemoveStack_CleansUp()
    {
        // Arrange
        var registry = new BehaviorStackRegistry(_merger);
        var entityId = Guid.NewGuid();

        registry.GetOrCreate(entityId, _humanoidArchetype);

        // Act
        var removed = registry.Remove(entityId);

        // Assert
        Assert.True(removed);
        Assert.Null(registry.Get(entityId));
        Assert.Equal(0, registry.Count);
    }

    // =========================================================================
    // MULTI-ENTITY EVALUATION TESTS
    // =========================================================================

    [Fact]
    public async Task MultipleEntities_IndependentStacks_EvaluateCorrectly()
    {
        // Arrange
        var registry = new BehaviorStackRegistry(_merger);

        var guard = Guid.NewGuid();
        var merchant = Guid.NewGuid();

        var guardStack = registry.GetOrCreate(guard, _humanoidArchetype);
        var merchantStack = registry.GetOrCreate(merchant, _humanoidArchetype);

        // Guard has patrol behavior
        guardStack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "guard-duty", "Guard Duty", BehaviorCategory.Professional, 0,
            new IntentEmission("movement", "patrol", 0.7f),
            new IntentEmission("combat", "ready", 0.7f)));

        // Merchant has greeting behavior
        merchantStack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "merchant-trade", "Merchant Trading", BehaviorCategory.Professional, 0,
            new IntentEmission("dialogue", "welcome", 0.7f),
            new IntentEmission("expression", "friendly", 0.7f)));

        var guardContext = new BehaviorEvaluationContext(guard, _humanoidArchetype);
        var merchantContext = new BehaviorEvaluationContext(merchant, _humanoidArchetype);

        // Act
        var guardOutput = await guardStack.EvaluateAsync(guardContext, CancellationToken.None);
        var merchantOutput = await merchantStack.EvaluateAsync(merchantContext, CancellationToken.None);

        // Assert - Each entity has distinct behavior
        Assert.True(guardOutput.HasEmission("combat"));
        Assert.False(guardOutput.HasEmission("dialogue"));

        Assert.True(merchantOutput.HasEmission("dialogue"));
        Assert.False(merchantOutput.HasEmission("combat"));
    }

    // =========================================================================
    // LAYER ACTIVATION/DEACTIVATION TESTS
    // =========================================================================

    [Fact]
    public async Task LayerActivation_ChangesOutput()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var normalBehavior = DelegateBehaviorLayer.CreateStatic(
            "normal", "Normal", BehaviorCategory.Base, 0,
            new IntentEmission("expression", "neutral", 0.5f));

        var angryBehavior = DelegateBehaviorLayer.CreateStatic(
            "angry", "Angry", BehaviorCategory.Situational, 0,
            new IntentEmission("expression", "angry", 0.9f));

        stack.AddLayer(normalBehavior);
        stack.AddLayer(angryBehavior);
        stack.DeactivateLayer("angry"); // Start with angry deactivated

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act - Before activation
        var beforeOutput = await stack.EvaluateAsync(context, CancellationToken.None);

        // Activate angry layer
        stack.ActivateLayer("angry");

        // After activation
        var afterOutput = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal("normal", beforeOutput.WinningLayers["expression"]);
        Assert.Equal("angry", afterOutput.WinningLayers["expression"]);
    }

    [Fact]
    public void LayerEvents_FiredCorrectly()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        var addedLayers = new List<string>();
        var removedLayers = new List<string>();
        var activatedLayers = new List<string>();
        var deactivatedLayers = new List<string>();

        stack.LayerAdded += (_, e) => addedLayers.Add(e.Layer.Id);
        stack.LayerRemoved += (_, e) => removedLayers.Add(e.Layer.Id);
        stack.LayerActivated += (_, e) => activatedLayers.Add(e.Layer.Id);
        stack.LayerDeactivated += (_, e) => deactivatedLayers.Add(e.Layer.Id);

        var layer = DelegateBehaviorLayer.CreateStatic(
            "test-layer", "Test", BehaviorCategory.Base, 0);

        // Act
        stack.AddLayer(layer);
        stack.DeactivateLayer("test-layer");
        stack.ActivateLayer("test-layer");
        stack.RemoveLayer("test-layer");

        // Assert
        Assert.Contains("test-layer", addedLayers);
        Assert.Contains("test-layer", removedLayers);
        Assert.Contains("test-layer", activatedLayers);
        Assert.Contains("test-layer", deactivatedLayers);
    }

    // =========================================================================
    // PRIORITY MERGING TESTS
    // =========================================================================

    [Fact]
    public async Task PriorityWithinCategory_HigherPriorityWins()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        // Two personal traits at different priorities
        var lowPriority = DelegateBehaviorLayer.CreateStatic(
            "casual", "Casual", BehaviorCategory.Personal, 10,
            new IntentEmission("expression", "relaxed", 0.5f));

        var highPriority = DelegateBehaviorLayer.CreateStatic(
            "anxious", "Anxious", BehaviorCategory.Personal, 100,
            new IntentEmission("expression", "tense", 0.9f));

        stack.AddLayer(lowPriority);
        stack.AddLayer(highPriority);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Higher priority wins
        Assert.Equal("anxious", output.WinningLayers["expression"]);
    }

    [Fact]
    public async Task CategoryOverridesPriority_HigherCategoryAlwaysWins()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        // High priority base behavior
        var basePriority1000 = DelegateBehaviorLayer.CreateStatic(
            "base-high", "Base High Priority", BehaviorCategory.Base, 1000,
            new IntentEmission("movement", "slow", 0.9f));

        // Low priority situational behavior
        var situationalPriority1 = DelegateBehaviorLayer.CreateStatic(
            "situational-low", "Situational Low Priority", BehaviorCategory.Situational, 1,
            new IntentEmission("movement", "fast", 0.3f));

        stack.AddLayer(basePriority1000);
        stack.AddLayer(situationalPriority1);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Category (Situational) trumps priority (1000)
        Assert.Equal("situational-low", output.WinningLayers["movement"]);
    }

    // =========================================================================
    // STACK CLEAR TESTS
    // =========================================================================

    [Fact]
    public async Task Clear_RemovesAllLayers()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("layer1", "L1", BehaviorCategory.Base, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("layer2", "L2", BehaviorCategory.Personal, 0));
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic("layer3", "L3", BehaviorCategory.Situational, 0));

        // Act
        stack.Clear();

        // Assert
        Assert.Empty(stack.Layers);
        Assert.Empty(stack.ActiveLayers);

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);
        var output = await stack.EvaluateAsync(context, CancellationToken.None);
        Assert.Empty(output.MergedEmissions);
    }

    // =========================================================================
    // CONTRIBUTION TRACING TESTS
    // =========================================================================

    [Fact]
    public async Task AllContributions_TrackedForDebugging()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "base", "Base", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "idle", 0.3f),
            new IntentEmission("expression", "neutral", 0.3f)));

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "override", "Override", BehaviorCategory.Situational, 0,
            new IntentEmission("movement", "run", 0.9f)));

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - All contributions tracked, not just winners
        Assert.Equal(3, output.AllContributions.Count);
        Assert.Contains(output.AllContributions, c => c.LayerId == "base" && c.Emission.Channel == "movement");
        Assert.Contains(output.AllContributions, c => c.LayerId == "base" && c.Emission.Channel == "expression");
        Assert.Contains(output.AllContributions, c => c.LayerId == "override" && c.Emission.Channel == "movement");
    }

    // =========================================================================
    // MULTI-CHANNEL OUTPUT TESTS
    // =========================================================================

    [Fact]
    public async Task MultipleChannels_EachMergedIndependently()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        // Base layer provides defaults for multiple channels
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "base", "Base", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "idle", 0.3f),
            new IntentEmission("expression", "neutral", 0.3f),
            new IntentEmission("dialogue", "silent", 0.3f)));

        // Professional layer overrides only movement
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "professional", "Professional", BehaviorCategory.Professional, 0,
            new IntentEmission("movement", "patrol", 0.6f)));

        // Situational layer overrides only expression
        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "situational", "Situational", BehaviorCategory.Situational, 0,
            new IntentEmission("expression", "alert", 0.9f)));

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert - Each channel won by different layer
        Assert.Equal("professional", output.WinningLayers["movement"]);
        Assert.Equal("situational", output.WinningLayers["expression"]);
        Assert.Equal("base", output.WinningLayers["dialogue"]);
    }

    [Fact]
    public async Task ActiveChannels_ReflectsAllOutput()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var stack = new BehaviorStack(entityId, _humanoidArchetype, _merger);

        stack.AddLayer(DelegateBehaviorLayer.CreateStatic(
            "multi-channel", "Multi Channel", BehaviorCategory.Base, 0,
            new IntentEmission("movement", "walk", 0.5f),
            new IntentEmission("expression", "happy", 0.5f),
            new IntentEmission("combat", "defensive", 0.5f)));

        var context = new BehaviorEvaluationContext(entityId, _humanoidArchetype);

        // Act
        var output = await stack.EvaluateAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(3, output.ActiveChannels.Count);
        Assert.Contains("movement", output.ActiveChannels);
        Assert.Contains("expression", output.ActiveChannels);
        Assert.Contains("combat", output.ActiveChannels);
    }
}
