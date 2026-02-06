// =============================================================================
// Intent Emission Integration Tests
// Tests end-to-end intent emission flows with emitter registry, core emitters,
// and action-to-intent translation.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Handlers;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for the intent emission system covering emitter registry,
/// core emitters, and action-to-intent translation.
/// </summary>
public sealed class IntentEmissionIntegrationTests
{
    private readonly IntentEmitterRegistry _registry;

    public IntentEmissionIntegrationTests()
    {
        _registry = IntentEmitterRegistry.CreateWithCoreEmitters();
    }

    // =========================================================================
    // REGISTRY TESTS
    // =========================================================================

    [Fact]
    public void CreateWithCoreEmitters_RegistersAllCoreEmitters()
    {
        // Assert - Core emitters are registered
        var actions = _registry.GetActionNames();

        Assert.Contains("walk_to", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("run_to", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("stop", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("attack", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("look_at", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("emote", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("speak", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("emit_intent", actions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasEmitter_ExistingAction_ReturnsTrue()
    {
        // Assert
        Assert.True(_registry.HasEmitter("walk_to"));
        Assert.True(_registry.HasEmitter("WALK_TO")); // Case insensitive
    }

    [Fact]
    public void HasEmitter_NonExistentAction_ReturnsFalse()
    {
        // Assert
        Assert.False(_registry.HasEmitter("nonexistent_action"));
    }

    [Fact]
    public void GetEmitter_ValidAction_ReturnsEmitter()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };

        // Act
        var emitter = _registry.GetEmitter("walk_to", context);

        // Assert
        Assert.NotNull(emitter);
        Assert.Equal("walk_to", emitter.ActionName);
    }

    // =========================================================================
    // MOVEMENT EMITTER TESTS
    // =========================================================================

    [Fact]
    public async Task WalkTo_EmitsMovementIntent()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("walk_to", context);
        Assert.NotNull(emitter);

        var parameters = new Dictionary<string, object>
        {
            ["urgency"] = 0.5f
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("locomotion", emissions[0].Channel);
        Assert.Equal("walk", emissions[0].Intent);
        Assert.Equal(0.5f, emissions[0].Urgency);
    }

    [Fact]
    public async Task RunTo_EmitsHigherUrgencyIntent()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("run_to", context);
        Assert.NotNull(emitter);

        var parameters = new Dictionary<string, object>
        {
            ["urgency"] = 0.9f
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("run", emissions[0].Intent);
        Assert.Equal(0.9f, emissions[0].Urgency);
    }

    [Fact]
    public async Task Stop_EmitsStopIntent()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("stop", context);
        Assert.NotNull(emitter);

        var parameters = new Dictionary<string, object>();

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("stop", emissions[0].Intent);
        Assert.Equal(0.8f, emissions[0].Urgency); // Default urgency
    }

    // =========================================================================
    // PARAMETER HANDLING TESTS
    // =========================================================================

    [Fact]
    public async Task Emitter_UrgencyClamped_StaysInRange()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("walk_to", context);
        Assert.NotNull(emitter);

        // Test over 1.0
        var overParams = new Dictionary<string, object> { ["urgency"] = 2.0f };
        var overEmissions = await emitter.EmitAsync(overParams, context, CancellationToken.None);
        Assert.Equal(1.0f, overEmissions[0].Urgency);

        // Test under 0.0
        var underParams = new Dictionary<string, object> { ["urgency"] = -1.0f };
        var underEmissions = await emitter.EmitAsync(underParams, context, CancellationToken.None);
        Assert.Equal(0.0f, underEmissions[0].Urgency);
    }

    [Fact]
    public async Task Emitter_TargetEntity_IncludedInEmission()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("walk_to", context);
        Assert.NotNull(emitter);

        var targetId = Guid.NewGuid();
        var parameters = new Dictionary<string, object>
        {
            ["entity"] = targetId
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal(targetId, emissions[0].Target);
    }

    // =========================================================================
    // EXPRESSION EMITTER TESTS
    // =========================================================================

    [Fact]
    public async Task Emote_NoArchetype_ReturnsEmpty()
    {
        // Arrange - No archetype means no supported channels
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("emote", context);
        Assert.NotNull(emitter);

        var parameters = new Dictionary<string, object>
        {
            ["emotion"] = "happy",
            ["intensity"] = 0.8f
        };

        // Act - Without an archetype, emote returns empty (no expression channel)
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - No emissions without archetype support
        Assert.Empty(emissions);
    }

    // =========================================================================
    // GENERIC EMIT TESTS
    // =========================================================================

    [Fact]
    public async Task EmitIntent_DirectChannelEmission()
    {
        // Arrange
        var context = new IntentEmissionContext { EntityId = Guid.NewGuid() };
        var emitter = _registry.GetEmitter("emit_intent", context);
        Assert.NotNull(emitter);

        var parameters = new Dictionary<string, object>
        {
            ["channel"] = "custom_channel",
            ["intent"] = "custom_action",
            ["urgency"] = 0.6f
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("custom_channel", emissions[0].Channel);
        Assert.Equal("custom_action", emissions[0].Intent);
        Assert.Equal(0.6f, emissions[0].Urgency);
    }

    // =========================================================================
    // CUSTOM EMITTER REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public void Register_CustomEmitter_CanBeRetrieved()
    {
        // Arrange
        var registry = new IntentEmitterRegistry();
        var customEmitter = new TestCustomEmitter();

        // Act
        registry.Register(customEmitter);

        // Assert
        Assert.True(registry.HasEmitter("custom_test_action"));
        var emitter = registry.GetEmitter("custom_test_action", new IntentEmissionContext());
        Assert.NotNull(emitter);
        Assert.Same(customEmitter, emitter);
    }

    [Fact]
    public void Register_DocumentTypeFiltered_ReturnsCorrectEmitter()
    {
        // Arrange
        var registry = new IntentEmitterRegistry();
        var behaviorEmitter = new TestFilteredEmitter("behavior");
        var cutsceneEmitter = new TestFilteredEmitter("cutscene");

        registry.Register(behaviorEmitter);
        registry.Register(cutsceneEmitter);

        // Act
        var behaviorContext = new IntentEmissionContext { DocumentType = "behavior" };
        var cutsceneContext = new IntentEmissionContext { DocumentType = "cutscene" };

        var foundBehavior = registry.GetEmitter("filtered_action", behaviorContext);
        var foundCutscene = registry.GetEmitter("filtered_action", cutsceneContext);

        // Assert
        Assert.Same(behaviorEmitter, foundBehavior);
        Assert.Same(cutsceneEmitter, foundCutscene);
    }

    // =========================================================================
    // FULL EMISSION PIPELINE TESTS
    // =========================================================================

    [Fact]
    public async Task FullPipeline_LookupAndEmit_ProducesCorrectOutput()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var context = new IntentEmissionContext
        {
            EntityId = entityId,
            DocumentType = "behavior"
        };

        // Act - Simulate action lookup and emission
        var emitter = _registry.GetEmitter("attack", context);
        Assert.NotNull(emitter);

        var parameters = new Dictionary<string, object>
        {
            ["target"] = targetId,
            ["urgency"] = 0.9f
        };

        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - Full pipeline works
        // Without archetype, falls back to "action" channel, intent is "attack_basic"
        Assert.Single(emissions);
        Assert.Equal("action", emissions[0].Channel);
        Assert.Equal("attack_basic", emissions[0].Intent);
        Assert.Equal(0.9f, emissions[0].Urgency);
        Assert.Equal(targetId, emissions[0].Target);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>
    /// Test emitter for verifying custom registration.
    /// </summary>
    private sealed class TestCustomEmitter : IIntentEmitter
    {
        public string ActionName => "custom_test_action";
        public IReadOnlySet<string> SupportedDocumentTypes => new HashSet<string>();

        public bool CanEmit(string actionName, IntentEmissionContext context)
            => string.Equals(actionName, ActionName, StringComparison.OrdinalIgnoreCase);

        public ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
            IReadOnlyDictionary<string, object> parameters,
            IntentEmissionContext context,
            CancellationToken ct)
        {
            return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[]
            {
                new IntentEmission("test", "test_action", 0.5f)
            });
        }
    }

    /// <summary>
    /// Test emitter that filters by document type.
    /// </summary>
    private sealed class TestFilteredEmitter : IIntentEmitter
    {
        private readonly HashSet<string> _supportedTypes;

        public TestFilteredEmitter(string supportedType)
        {
            _supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { supportedType };
        }

        public string ActionName => "filtered_action";
        public IReadOnlySet<string> SupportedDocumentTypes => _supportedTypes;

        public bool CanEmit(string actionName, IntentEmissionContext context)
            => string.Equals(actionName, ActionName, StringComparison.OrdinalIgnoreCase)
                && _supportedTypes.Contains(context.DocumentType);

        public ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
            IReadOnlyDictionary<string, object> parameters,
            IntentEmissionContext context,
            CancellationToken ct)
        {
            return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[]
            {
                new IntentEmission("filtered", "action", 0.5f)
            });
        }
    }
}
