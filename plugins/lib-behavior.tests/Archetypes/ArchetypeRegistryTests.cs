// =============================================================================
// Archetype Registry Tests
// Unit tests for entity archetype system.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.Bannou.BehaviorCompiler.Intent;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Archetypes;

/// <summary>
/// Unit tests for ArchetypeRegistry.
/// </summary>
public class ArchetypeRegistryTests
{
    #region Standard Archetype Tests

    [Fact]
    public void Registry_HasStandardArchetypes()
    {
        // Arrange & Act
        var registry = new ArchetypeRegistry();

        // Assert
        Assert.True(registry.HasArchetype("humanoid"));
        Assert.True(registry.HasArchetype("vehicle"));
        Assert.True(registry.HasArchetype("creature"));
        Assert.True(registry.HasArchetype("object"));
        Assert.True(registry.HasArchetype("environmental"));
    }

    [Fact]
    public void Registry_GetArchetype_ReturnsCaseInsensitive()
    {
        // Arrange
        var registry = new ArchetypeRegistry();

        // Act
        var lower = registry.GetArchetype("humanoid");
        var upper = registry.GetArchetype("HUMANOID");
        var mixed = registry.GetArchetype("Humanoid");

        // Assert
        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.NotNull(mixed);
        Assert.Equal(lower?.Id, upper?.Id);
        Assert.Equal(lower?.Id, mixed?.Id);
    }

    [Fact]
    public void Registry_GetArchetype_NonExistent_ReturnsNull()
    {
        // Arrange
        var registry = new ArchetypeRegistry();

        // Act
        var result = registry.GetArchetype("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Registry_GetDefaultArchetype_ReturnsHumanoid()
    {
        // Arrange
        var registry = new ArchetypeRegistry();

        // Act
        var defaultArch = registry.GetDefaultArchetype();

        // Assert
        Assert.Equal("humanoid", defaultArch.Id);
    }

    #endregion

    #region Humanoid Archetype Tests

    [Fact]
    public void HumanoidArchetype_HasExpectedChannels()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Assert - should have 7 channels per planning doc
        Assert.True(humanoid.HasChannel("combat"));
        Assert.True(humanoid.HasChannel("movement"));
        Assert.True(humanoid.HasChannel("interaction"));
        Assert.True(humanoid.HasChannel("expression"));
        Assert.True(humanoid.HasChannel("attention"));
        Assert.True(humanoid.HasChannel("speech"));
        Assert.True(humanoid.HasChannel("stance"));
    }

    [Fact]
    public void HumanoidArchetype_HasExpectedModelTypes()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Assert
        Assert.True(humanoid.SupportsModelType(BehaviorModelType.Combat));
        Assert.True(humanoid.SupportsModelType(BehaviorModelType.Movement));
        Assert.True(humanoid.SupportsModelType(BehaviorModelType.Interaction));
        Assert.True(humanoid.SupportsModelType(BehaviorModelType.Idle));
    }

    [Fact]
    public void HumanoidArchetype_CombatMapsToActionPhysical()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Act
        var combat = humanoid.GetChannel("combat");

        // Assert
        Assert.NotNull(combat);
        Assert.Equal(PhysicalChannel.Action, combat.PhysicalChannel);
    }

    [Fact]
    public void HumanoidArchetype_MovementMapsToLocomotionPhysical()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Act
        var movement = humanoid.GetChannel("movement");

        // Assert
        Assert.NotNull(movement);
        Assert.Equal(PhysicalChannel.Locomotion, movement.PhysicalChannel);
    }

    #endregion

    #region Vehicle Archetype Tests

    [Fact]
    public void VehicleArchetype_HasExpectedChannels()
    {
        // Arrange
        var vehicle = ArchetypeDefinition.CreateVehicle();

        // Assert
        Assert.True(vehicle.HasChannel("throttle"));
        Assert.True(vehicle.HasChannel("steering"));
        Assert.True(vehicle.HasChannel("signals"));
        Assert.True(vehicle.HasChannel("systems"));

        // Should NOT have humanoid channels
        Assert.False(vehicle.HasChannel("combat"));
        Assert.False(vehicle.HasChannel("expression"));
    }

    [Fact]
    public void VehicleArchetype_ThrottleAndSteeringMapToLocomotion()
    {
        // Arrange
        var vehicle = ArchetypeDefinition.CreateVehicle();

        // Act
        var throttle = vehicle.GetChannel("throttle");
        var steering = vehicle.GetChannel("steering");

        // Assert
        Assert.NotNull(throttle);
        Assert.NotNull(steering);
        Assert.Equal(PhysicalChannel.Locomotion, throttle.PhysicalChannel);
        Assert.Equal(PhysicalChannel.Locomotion, steering.PhysicalChannel);
    }

    #endregion

    #region Creature Archetype Tests

    [Fact]
    public void CreatureArchetype_HasExpectedChannels()
    {
        // Arrange
        var creature = ArchetypeDefinition.CreateCreature();

        // Assert
        Assert.True(creature.HasChannel("locomotion"));
        Assert.True(creature.HasChannel("action"));
        Assert.True(creature.HasChannel("social"));
        Assert.True(creature.HasChannel("alert"));
    }

    #endregion

    #region Object Archetype Tests

    [Fact]
    public void ObjectArchetype_HasExpectedChannels()
    {
        // Arrange
        var obj = ArchetypeDefinition.CreateObject();

        // Assert
        Assert.True(obj.HasChannel("state"));
        Assert.True(obj.HasChannel("timing"));
        Assert.True(obj.HasChannel("feedback"));
    }

    [Fact]
    public void ObjectArchetype_OnlySupportsInteraction()
    {
        // Arrange
        var obj = ArchetypeDefinition.CreateObject();

        // Assert
        Assert.True(obj.SupportsModelType(BehaviorModelType.Interaction));
        Assert.False(obj.SupportsModelType(BehaviorModelType.Combat));
        Assert.False(obj.SupportsModelType(BehaviorModelType.Movement));
        Assert.False(obj.SupportsModelType(BehaviorModelType.Idle));
    }

    #endregion

    #region Environmental Archetype Tests

    [Fact]
    public void EnvironmentalArchetype_HasExpectedChannels()
    {
        // Arrange
        var env = ArchetypeDefinition.CreateEnvironmental();

        // Assert
        Assert.True(env.HasChannel("intensity"));
        Assert.True(env.HasChannel("type"));
        Assert.True(env.HasChannel("direction"));
        Assert.True(env.HasChannel("mood"));
    }

    #endregion

    #region Custom Archetype Registration Tests

    [Fact]
    public void Registry_RegisterCustomArchetype_Success()
    {
        // Arrange
        var registry = new ArchetypeRegistry();
        var custom = new ArchetypeDefinition
        {
            Id = "custom-test",
            Description = "Test custom archetype",
            ModelTypes = new[] { BehaviorModelType.Idle },
            Channels = new[]
            {
                new LogicalChannelDefinition("custom-channel", PhysicalChannel.Action, 0.5f, MergeStrategy.Priority, "Test channel"),
            }
        }.Initialize();

        // Act
        registry.RegisterArchetype(custom);

        // Assert
        Assert.True(registry.HasArchetype("custom-test"));
        var retrieved = registry.GetArchetype("custom-test");
        Assert.NotNull(retrieved);
        Assert.True(retrieved.HasChannel("custom-channel"));
    }

    [Fact]
    public void Registry_RegisterDuplicateArchetype_Throws()
    {
        // Arrange
        var registry = new ArchetypeRegistry();
        var duplicate = new ArchetypeDefinition
        {
            Id = "humanoid", // Already exists
            Description = "Duplicate",
            ModelTypes = Array.Empty<BehaviorModelType>(),
            Channels = Array.Empty<LogicalChannelDefinition>()
        }.Initialize();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.RegisterArchetype(duplicate));
    }

    #endregion

    #region Physical Channel Mapping Tests

    [Fact]
    public void Archetype_GetChannelsForPhysical_ReturnsMultiple()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Act - Action physical channel should have combat and interaction
        var actionChannels = humanoid.GetChannelsForPhysical(PhysicalChannel.Action);

        // Assert
        Assert.True(actionChannels.Count >= 2);
        Assert.Contains(actionChannels, c => c.Name == "combat");
        Assert.Contains(actionChannels, c => c.Name == "interaction");
    }

    [Fact]
    public void Archetype_GetUsedPhysicalChannels_ReturnsCorrectSet()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Act
        var used = humanoid.GetUsedPhysicalChannels();

        // Assert
        Assert.Contains(PhysicalChannel.Action, used);
        Assert.Contains(PhysicalChannel.Locomotion, used);
        Assert.Contains(PhysicalChannel.Attention, used);
        Assert.Contains(PhysicalChannel.Stance, used);
        Assert.Contains(PhysicalChannel.Vocalization, used);
        Assert.Contains(PhysicalChannel.Expression, used);
    }

    #endregion

    #region Merge Strategy Tests

    [Fact]
    public void Channel_MergeStrategy_CombatIsPriority()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Act
        var combat = humanoid.GetChannel("combat");

        // Assert
        Assert.NotNull(combat);
        Assert.Equal(MergeStrategy.Priority, combat.MergeStrategy);
    }

    [Fact]
    public void Channel_MergeStrategy_MovementIsBlend()
    {
        // Arrange
        var humanoid = ArchetypeDefinition.CreateHumanoid();

        // Act
        var movement = humanoid.GetChannel("movement");

        // Assert
        Assert.NotNull(movement);
        Assert.Equal(MergeStrategy.Blend, movement.MergeStrategy);
    }

    #endregion
}
