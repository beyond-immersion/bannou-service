// =============================================================================
// Intent Channel Factory Tests
// Tests for runtime intent channel creation from archetypes.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Archetypes;

/// <summary>
/// Tests for <see cref="IntentChannelFactory"/> and <see cref="RuntimeChannelSet"/>.
/// </summary>
public sealed class IntentChannelFactoryTests
{
    private readonly IArchetypeRegistry _archetypes;
    private readonly IIntentChannelFactory _factory;

    public IntentChannelFactoryTests()
    {
        _archetypes = new ArchetypeRegistry();
        _factory = new IntentChannelFactory();
    }

    [Fact]
    public void CreateChannels_HumanoidArchetype_CreatesAllChannels()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);

        // Act
        var channelSet = _factory.CreateChannels(entityId, archetype);

        // Assert
        Assert.Equal(entityId, channelSet.EntityId);
        Assert.Equal("humanoid", channelSet.ArchetypeId);
        Assert.True(channelSet.Channels.Count > 0);

        // Verify some expected humanoid channels exist
        Assert.NotNull(channelSet.GetChannel("movement"));
        Assert.NotNull(channelSet.GetChannel("attention"));
    }

    [Fact]
    public void CreateChannels_VehicleArchetype_CreatesCorrectChannels()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("vehicle");
        Assert.NotNull(archetype);

        // Act
        var channelSet = _factory.CreateChannels(entityId, archetype);

        // Assert
        Assert.Equal("vehicle", channelSet.ArchetypeId);

        // Vehicles have throttle and steering instead of movement/locomotion
        Assert.NotNull(channelSet.GetChannel("throttle"));
        Assert.NotNull(channelSet.GetChannel("steering"));
        // Vehicles should not have expression
        Assert.Null(channelSet.GetChannel("expression"));
    }

    [Fact]
    public void RuntimeChannelSet_ApplyEmission_SetsChannelValue()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);
        var channelSet = _factory.CreateChannels(entityId, archetype);

        var emission = new IntentEmission("movement", "walk", 0.7f);

        // Act
        var result = channelSet.ApplyEmission(emission);

        // Assert
        Assert.True(result);
        var channel = channelSet.GetChannel("movement");
        Assert.NotNull(channel);
        Assert.NotNull(channel.CurrentValue);
        Assert.Equal("walk", channel.CurrentValue.Intent);
        Assert.Equal(0.7f, channel.CurrentValue.Urgency);
    }

    [Fact]
    public void RuntimeChannelSet_ApplyEmission_InvalidChannel_ReturnsFalse()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);
        var channelSet = _factory.CreateChannels(entityId, archetype);

        var emission = new IntentEmission("nonexistent_channel", "test", 0.5f);

        // Act
        var result = channelSet.ApplyEmission(emission);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RuntimeChannelSet_GetChannel_CaseInsensitive()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);
        var channelSet = _factory.CreateChannels(entityId, archetype);

        // Act & Assert
        var lowercase = channelSet.GetChannel("movement");
        var uppercase = channelSet.GetChannel("MOVEMENT");
        var mixedCase = channelSet.GetChannel("Movement");

        Assert.NotNull(lowercase);
        Assert.NotNull(uppercase);
        Assert.NotNull(mixedCase);
        Assert.Same(lowercase, uppercase);
        Assert.Same(lowercase, mixedCase);
    }

    [Fact]
    public void RuntimeChannelSet_ClearAll_ClearsAllChannelValues()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);
        var channelSet = _factory.CreateChannels(entityId, archetype);

        // Set some values
        channelSet.ApplyEmission(new IntentEmission("movement", "walk", 0.5f));
        channelSet.ApplyEmission(new IntentEmission("attention", "look_at", 0.6f));

        // Act
        channelSet.ClearAll();

        // Assert
        var movement = channelSet.GetChannel("movement");
        var attention = channelSet.GetChannel("attention");
        Assert.NotNull(movement);
        Assert.NotNull(attention);
        Assert.Null(movement.CurrentValue);
        Assert.Null(attention.CurrentValue);
    }

    [Fact]
    public void RuntimeChannel_SetValue_UpdatesTimestamp()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);
        var channelSet = _factory.CreateChannels(entityId, archetype);
        var channel = channelSet.GetChannel("movement");
        Assert.NotNull(channel);
        Assert.Null(channel.LastUpdated);

        // Act
        channelSet.ApplyEmission(new IntentEmission("movement", "run", 0.8f));

        // Assert
        Assert.NotNull(channel.LastUpdated);
        Assert.True(channel.LastUpdated <= DateTime.UtcNow);
    }

    [Fact]
    public void RuntimeChannel_Clear_ClearsValueAndTimestamp()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var archetype = _archetypes.GetArchetype("humanoid");
        Assert.NotNull(archetype);
        var channelSet = _factory.CreateChannels(entityId, archetype);
        var channel = channelSet.GetChannel("movement");
        Assert.NotNull(channel);

        channelSet.ApplyEmission(new IntentEmission("movement", "walk", 0.5f));
        Assert.NotNull(channel.CurrentValue);
        Assert.NotNull(channel.LastUpdated);

        // Act
        channel.Clear();

        // Assert
        Assert.Null(channel.CurrentValue);
        Assert.Null(channel.LastUpdated);
    }
}
