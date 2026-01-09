using BeyondImmersion.BannouService.ClientEvents;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for client event whitelist functionality.
/// </summary>
public class ClientEventTests
{
    #region ClientEventWhitelist Tests

    [Fact]
    public void IsValidEventName_WithNullInput_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithEmptyString_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithWhitespace_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("   ");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithValidConnectEvent_ShouldReturnTrue()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("connect.capability_manifest");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEventName_WithValidGameSessionEvent_ShouldReturnTrue()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("game_session.state_changed");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEventName_WithValidSystemEvent_ShouldReturnTrue()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("system.error");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEventName_WithUnknownEvent_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("unknown.event.type");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithCaseVariation_ShouldReturnTrue()
    {
        // The whitelist uses OrdinalIgnoreCase comparison
        // Act
        var result = ClientEventWhitelist.IsValidEventName("CONNECT.CAPABILITY_MANIFEST");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetAllValidEventNames_ShouldReturnNonEmptySet()
    {
        // Act
        var eventNames = ClientEventWhitelist.GetAllValidEventNames();

        // Assert
        Assert.NotEmpty(eventNames);
        Assert.Contains("connect.capability_manifest", eventNames);
        Assert.Contains("game_session.state_changed", eventNames);
        Assert.Contains("system.error", eventNames);
    }

    [Fact]
    public void Count_ShouldReturnPositiveNumber()
    {
        // Act
        var count = ClientEventWhitelist.Count;

        // Assert
        Assert.True(count > 0);
    }

    [Theory]
    [InlineData("connect.capability_manifest")]
    [InlineData("connect.disconnect_notification")]
    [InlineData("game_session.action_result")]
    [InlineData("game_session.chat_received")]
    [InlineData("game_session.player_joined")]
    [InlineData("game_session.player_kicked")]
    [InlineData("game_session.player_left")]
    [InlineData("game_session.state_changed")]
    [InlineData("game_session.state_updated")]
    [InlineData("system.error")]
    [InlineData("system.notification")]
    public void IsValidEventName_AllRegisteredEvents_ShouldReturnTrue(string eventName)
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName(eventName);

        // Assert
        Assert.True(result, $"Expected event '{eventName}' to be valid");
    }

    #endregion
}
