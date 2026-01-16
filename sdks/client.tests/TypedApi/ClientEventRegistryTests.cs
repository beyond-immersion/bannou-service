// =============================================================================
// Client Event Registry Tests
// Tests for event type to eventName mapping and lookup.
// =============================================================================

using BeyondImmersion.Bannou.Client.Events;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.TypedApi;

/// <summary>
/// Tests for the ClientEventRegistry class.
/// Verifies type-to-eventName mapping and bidirectional lookup.
/// </summary>
public class ClientEventRegistryTests
{
    // =========================================================================
    // GetEventName<TEvent> TESTS
    // =========================================================================

    [Fact]
    public void GetEventName_GameSessionEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<ChatMessageReceivedEvent>();

        Assert.Equal("game_session.chat_received", eventName);
    }

    [Fact]
    public void GetEventName_VoiceEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<VoicePeerJoinedEvent>();

        Assert.Equal("voice.peer_joined", eventName);
    }

    [Fact]
    public void GetEventName_PlayerJoinedEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<PlayerJoinedEvent>();

        Assert.Equal("game_session.player_joined", eventName);
    }

    [Fact]
    public void GetEventName_VoiceRoomStateEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<VoiceRoomStateEvent>();

        Assert.Equal("voice.room_state", eventName);
    }

    // =========================================================================
    // GetEventName(Type) TESTS
    // =========================================================================

    [Fact]
    public void GetEventName_ByType_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName(typeof(PlayerJoinedEvent));

        Assert.Equal("game_session.player_joined", eventName);
    }

    [Fact]
    public void GetEventName_ByType_UnregisteredType_ReturnsNull()
    {
        var eventName = ClientEventRegistry.GetEventName(typeof(string));

        Assert.Null(eventName);
    }

    // =========================================================================
    // GetEventType TESTS
    // =========================================================================

    [Fact]
    public void GetEventType_ValidEventName_ReturnsCorrectType()
    {
        var eventType = ClientEventRegistry.GetEventType("game_session.player_left");

        Assert.Equal(typeof(PlayerLeftEvent), eventType);
    }

    [Fact]
    public void GetEventType_VoiceEventName_ReturnsCorrectType()
    {
        var eventType = ClientEventRegistry.GetEventType("voice.room_closed");

        Assert.Equal(typeof(VoiceRoomClosedEvent), eventType);
    }

    [Fact]
    public void GetEventType_UnknownEventName_ReturnsNull()
    {
        var eventType = ClientEventRegistry.GetEventType("unknown.event");

        Assert.Null(eventType);
    }

    [Fact]
    public void GetEventType_EmptyString_ReturnsNull()
    {
        var eventType = ClientEventRegistry.GetEventType("");

        Assert.Null(eventType);
    }

    // =========================================================================
    // IsRegistered TESTS
    // =========================================================================

    [Fact]
    public void IsRegistered_Generic_RegisteredType_ReturnsTrue()
    {
        var isRegistered = ClientEventRegistry.IsRegistered<ChatMessageReceivedEvent>();

        Assert.True(isRegistered);
    }

    [Fact]
    public void IsRegistered_String_RegisteredName_ReturnsTrue()
    {
        var isRegistered = ClientEventRegistry.IsRegistered("voice.peer_updated");

        Assert.True(isRegistered);
    }

    [Fact]
    public void IsRegistered_String_UnknownName_ReturnsFalse()
    {
        var isRegistered = ClientEventRegistry.IsRegistered("not.a.real.event");

        Assert.False(isRegistered);
    }

    // =========================================================================
    // GetAll TESTS
    // =========================================================================

    [Fact]
    public void GetAllEventTypes_ReturnsNonEmptyCollection()
    {
        var types = ClientEventRegistry.GetAllEventTypes();

        Assert.NotEmpty(types);
    }

    [Fact]
    public void GetAllEventNames_ReturnsNonEmptyCollection()
    {
        var names = ClientEventRegistry.GetAllEventNames();

        Assert.NotEmpty(names);
    }

    [Fact]
    public void GetAllEventTypes_ContainsExpectedTypes()
    {
        var types = ClientEventRegistry.GetAllEventTypes().ToList();

        Assert.Contains(typeof(ChatMessageReceivedEvent), types);
        Assert.Contains(typeof(VoicePeerJoinedEvent), types);
        Assert.Contains(typeof(PlayerJoinedEvent), types);
    }

    [Fact]
    public void GetAllEventNames_ContainsExpectedNames()
    {
        var names = ClientEventRegistry.GetAllEventNames().ToList();

        Assert.Contains("game_session.chat_received", names);
        Assert.Contains("voice.peer_joined", names);
        Assert.Contains("system.error", names);
    }

    // =========================================================================
    // BIDIRECTIONAL CONSISTENCY TESTS
    // =========================================================================

    [Fact]
    public void RoundTrip_TypeToNameToType_ReturnsOriginalType()
    {
        var originalType = typeof(ChatMessageReceivedEvent);

        var eventName = ClientEventRegistry.GetEventName(originalType);
        Assert.NotNull(eventName);

        var resolvedType = ClientEventRegistry.GetEventType(eventName);

        Assert.Equal(originalType, resolvedType);
    }

    [Fact]
    public void RoundTrip_NameToTypeToName_ReturnsOriginalName()
    {
        var originalName = "voice.tier_upgrade";

        var eventType = ClientEventRegistry.GetEventType(originalName);
        Assert.NotNull(eventType);

        var resolvedName = ClientEventRegistry.GetEventName(eventType);

        Assert.Equal(originalName, resolvedName);
    }

    [Fact]
    public void AllRegisteredTypes_HaveValidRoundTrip()
    {
        foreach (var eventType in ClientEventRegistry.GetAllEventTypes())
        {
            var eventName = ClientEventRegistry.GetEventName(eventType);
            Assert.NotNull(eventName);

            var resolvedType = ClientEventRegistry.GetEventType(eventName);
            Assert.Equal(eventType, resolvedType);
        }
    }

    [Fact]
    public void AllRegisteredNames_HaveValidRoundTrip()
    {
        foreach (var eventName in ClientEventRegistry.GetAllEventNames())
        {
            var eventType = ClientEventRegistry.GetEventType(eventName);
            Assert.NotNull(eventType);

            var resolvedName = ClientEventRegistry.GetEventName(eventType);
            Assert.Equal(eventName, resolvedName);
        }
    }
}
