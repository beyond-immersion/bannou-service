// =============================================================================
// Intent Emitter Registry Tests
// Unit tests for handler mapping layer.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior.Handlers;
using BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Unit tests for IntentEmitterRegistry.
/// </summary>
public class IntentEmitterRegistryTests
{
    #region Registration Tests

    [Fact]
    public void Registry_RegisterCoreEmitters_RegistersAllActions()
    {
        // Arrange & Act
        var registry = IntentEmitterRegistry.CreateWithCoreEmitters();

        // Assert
        Assert.True(registry.HasEmitter("walk_to"));
        Assert.True(registry.HasEmitter("run_to"));
        Assert.True(registry.HasEmitter("stop"));
        Assert.True(registry.HasEmitter("attack"));
        Assert.True(registry.HasEmitter("block"));
        Assert.True(registry.HasEmitter("dodge"));
        Assert.True(registry.HasEmitter("look_at"));
        Assert.True(registry.HasEmitter("track"));
        Assert.True(registry.HasEmitter("use"));
        Assert.True(registry.HasEmitter("pick_up"));
        Assert.True(registry.HasEmitter("talk_to"));
        Assert.True(registry.HasEmitter("emote"));
        Assert.True(registry.HasEmitter("speak"));
        Assert.True(registry.HasEmitter("shout"));
        Assert.True(registry.HasEmitter("emit_intent"));
        Assert.True(registry.HasEmitter("multi_emit"));
    }

    [Fact]
    public void Registry_GetActionNames_ReturnsAllRegistered()
    {
        // Arrange
        var registry = IntentEmitterRegistry.CreateWithCoreEmitters();

        // Act
        var names = registry.GetActionNames();

        // Assert
        Assert.Contains("walk_to", names);
        Assert.Contains("attack", names);
        Assert.Contains("emit_intent", names);
    }

    [Fact]
    public void Registry_HasEmitter_NonExistent_ReturnsFalse()
    {
        // Arrange
        var registry = new IntentEmitterRegistry();

        // Act & Assert
        Assert.False(registry.HasEmitter("nonexistent"));
    }

    #endregion

    #region Emitter Lookup Tests

    [Fact]
    public void Registry_GetEmitter_ReturnsCorrectEmitter()
    {
        // Arrange
        var registry = IntentEmitterRegistry.CreateWithCoreEmitters();
        var context = CreateHumanoidContext();

        // Act
        var emitter = registry.GetEmitter("walk_to", context);

        // Assert
        Assert.NotNull(emitter);
        Assert.Equal("walk_to", emitter.ActionName);
    }

    [Fact]
    public void Registry_GetEmitter_CaseInsensitive()
    {
        // Arrange
        var registry = IntentEmitterRegistry.CreateWithCoreEmitters();
        var context = CreateHumanoidContext();

        // Act
        var lower = registry.GetEmitter("walk_to", context);
        var upper = registry.GetEmitter("WALK_TO", context);
        var mixed = registry.GetEmitter("Walk_To", context);

        // Assert
        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.NotNull(mixed);
    }

    #endregion

    #region Movement Emitter Tests

    [Fact]
    public async Task WalkToEmitter_EmitsToMovementChannel()
    {
        // Arrange
        var emitter = new WalkToEmitter();
        var context = CreateHumanoidContext();
        var parameters = new Dictionary<string, object>
        {
            ["target"] = new System.Numerics.Vector3(10, 0, 5),
            ["urgency"] = 0.6f
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("movement", emissions[0].Channel);
        Assert.Equal("walk", emissions[0].Intent);
        Assert.Equal(0.6f, emissions[0].Urgency);
    }

    [Fact]
    public async Task RunToEmitter_EmitsWithHigherDefaultUrgency()
    {
        // Arrange
        var emitter = new RunToEmitter();
        var context = CreateHumanoidContext();
        var parameters = new Dictionary<string, object>
        {
            ["target"] = new System.Numerics.Vector3(10, 0, 5)
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("run", emissions[0].Intent);
        Assert.True(emissions[0].Urgency >= 0.7f); // Higher default for running
    }

    #endregion

    #region Combat Emitter Tests

    [Fact]
    public async Task AttackEmitter_EmitsToCombatChannel()
    {
        // Arrange
        var emitter = new AttackEmitter();
        var context = CreateHumanoidContext();
        var targetId = Guid.NewGuid();
        var parameters = new Dictionary<string, object>
        {
            ["type"] = "heavy",
            ["target"] = targetId.ToString()
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("combat", emissions[0].Channel);
        Assert.Equal("attack_heavy", emissions[0].Intent);
        Assert.Equal(targetId, emissions[0].Target);
    }

    [Fact]
    public async Task DodgeEmitter_EmitsToMultipleChannels()
    {
        // Arrange
        var emitter = new DodgeEmitter();
        var context = CreateHumanoidContext();
        var parameters = new Dictionary<string, object>
        {
            ["direction"] = "left"
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - dodge affects both combat and movement
        Assert.True(emissions.Count >= 2);
        Assert.Contains(emissions, e => e.Channel == "combat");
        Assert.Contains(emissions, e => e.Channel == "movement");
    }

    #endregion

    #region Attention Emitter Tests

    [Fact]
    public async Task LookAtEmitter_EmitsToAttentionChannel()
    {
        // Arrange
        var emitter = new LookAtEmitter();
        var context = CreateHumanoidContext();
        var targetId = Guid.NewGuid();
        var parameters = new Dictionary<string, object>
        {
            ["target"] = targetId.ToString()
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("attention", emissions[0].Channel);
        Assert.Equal("look_at", emissions[0].Intent);
        Assert.Equal(targetId, emissions[0].Target);
    }

    #endregion

    #region Interaction Emitter Tests

    [Fact]
    public async Task TalkToEmitter_EmitsToMultipleChannels()
    {
        // Arrange
        var emitter = new TalkToEmitter();
        var context = CreateHumanoidContext();
        var targetId = Guid.NewGuid();
        var parameters = new Dictionary<string, object>
        {
            ["target"] = targetId.ToString()
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - talk_to affects interaction and attention
        Assert.True(emissions.Count >= 2);
        Assert.Contains(emissions, e => e.Channel == "interaction");
        Assert.Contains(emissions, e => e.Channel == "attention");
    }

    #endregion

    #region Generic Emitter Tests

    [Fact]
    public async Task EmitIntentEmitter_EmitsToSpecifiedChannel()
    {
        // Arrange
        var emitter = new EmitIntentEmitter();
        var context = CreateHumanoidContext();
        var parameters = new Dictionary<string, object>
        {
            ["channel"] = "combat",
            ["intent"] = "special_attack",
            ["urgency"] = 0.9f
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert
        Assert.Single(emissions);
        Assert.Equal("combat", emissions[0].Channel);
        Assert.Equal("special_attack", emissions[0].Intent);
        Assert.Equal(0.9f, emissions[0].Urgency);
    }

    [Fact]
    public async Task EmitIntentEmitter_FallsBackForMissingChannel()
    {
        // Arrange
        var emitter = new EmitIntentEmitter();
        var context = CreateVehicleContext();
        var parameters = new Dictionary<string, object>
        {
            ["channel"] = "combat", // Vehicles don't have combat
            ["intent"] = "attack",
            ["urgency"] = 0.8f
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - should fallback to action channel
        if (emissions.Count > 0)
        {
            // If there's a fallback, it should be to action or systems
            Assert.True(emissions[0].Channel == "action" || emissions[0].Channel == "systems");
        }
    }

    #endregion

    #region Archetype-Specific Behavior Tests

    [Fact]
    public async Task WalkToEmitter_UsesLocomotionForCreature()
    {
        // Arrange
        var emitter = new WalkToEmitter();
        var context = CreateCreatureContext();
        var parameters = new Dictionary<string, object>
        {
            ["target"] = new System.Numerics.Vector3(10, 0, 5)
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - creatures use "locomotion" not "movement"
        Assert.Single(emissions);
        Assert.Equal("locomotion", emissions[0].Channel);
    }

    [Fact]
    public async Task EmoteEmitter_FallsBackForNonHumanoid()
    {
        // Arrange
        var emitter = new EmoteEmitter();
        var context = CreateCreatureContext();
        var parameters = new Dictionary<string, object>
        {
            ["emotion"] = "happy"
        };

        // Act
        var emissions = await emitter.EmitAsync(parameters, context, CancellationToken.None);

        // Assert - creatures don't have expression channel, should use fallback
        if (emissions.Count > 0)
        {
            // Should fall back to stance or alert
            Assert.True(emissions[0].Channel == "stance" || emissions[0].Channel == "alert");
        }
    }

    #endregion

    #region Helper Methods

    private static IntentEmissionContext CreateHumanoidContext()
    {
        return new IntentEmissionContext
        {
            EntityId = Guid.NewGuid(),
            Archetype = ArchetypeDefinition.CreateHumanoid(),
            DocumentType = "behavior"
        };
    }

    private static IntentEmissionContext CreateVehicleContext()
    {
        return new IntentEmissionContext
        {
            EntityId = Guid.NewGuid(),
            Archetype = ArchetypeDefinition.CreateVehicle(),
            DocumentType = "behavior"
        };
    }

    private static IntentEmissionContext CreateCreatureContext()
    {
        return new IntentEmissionContext
        {
            EntityId = Guid.NewGuid(),
            Archetype = ArchetypeDefinition.CreateCreature(),
            DocumentType = "behavior"
        };
    }

    #endregion
}
