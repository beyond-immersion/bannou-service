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
        var eventName = ClientEventRegistry.GetEventName<SessionChatReceivedClientEvent>();

        Assert.Equal("game-session.chat-received", eventName);
    }

    [Fact]
    public void GetEventName_VoiceEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<VoicePeerJoinedClientEvent>();

        Assert.Equal("voice.peer-joined", eventName);
    }

    [Fact]
    public void GetEventName_PlayerJoinedClientEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<PlayerJoinedClientEvent>();

        Assert.Equal("game-session.player-joined", eventName);
    }

    [Fact]
    public void GetEventName_VoiceRoomStateClientEvent_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName<VoiceRoomStateClientEvent>();

        Assert.Equal("voice.room-state", eventName);
    }

    // =========================================================================
    // GetEventName(Type) TESTS
    // =========================================================================

    [Fact]
    public void GetEventName_ByType_ReturnsCorrectName()
    {
        var eventName = ClientEventRegistry.GetEventName(typeof(PlayerJoinedClientEvent));

        Assert.Equal("game-session.player-joined", eventName);
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
        var eventType = ClientEventRegistry.GetEventType("game-session.player-left");

        Assert.Equal(typeof(PlayerLeftClientEvent), eventType);
    }

    [Fact]
    public void GetEventType_VoiceEventName_ReturnsCorrectType()
    {
        var eventType = ClientEventRegistry.GetEventType("voice.room-closed");

        Assert.Equal(typeof(VoiceRoomClosedClientEvent), eventType);
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
        var isRegistered = ClientEventRegistry.IsRegistered<SessionChatReceivedClientEvent>();

        Assert.True(isRegistered);
    }

    [Fact]
    public void IsRegistered_String_RegisteredName_ReturnsTrue()
    {
        var isRegistered = ClientEventRegistry.IsRegistered("voice.peer-updated");

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

        Assert.Contains(typeof(SessionChatReceivedClientEvent), types);
        Assert.Contains(typeof(VoicePeerJoinedClientEvent), types);
        Assert.Contains(typeof(PlayerJoinedClientEvent), types);
    }

    [Fact]
    public void GetAllEventNames_ContainsExpectedNames()
    {
        var names = ClientEventRegistry.GetAllEventNames().ToList();

        Assert.Contains("game-session.chat-received", names);
        Assert.Contains("voice.peer-joined", names);
        Assert.Contains("system.error", names);
    }

    // =========================================================================
    // BIDIRECTIONAL CONSISTENCY TESTS
    // =========================================================================

    [Fact]
    public void RoundTrip_TypeToNameToType_ReturnsOriginalType()
    {
        var originalType = typeof(SessionChatReceivedClientEvent);

        var eventName = ClientEventRegistry.GetEventName(originalType);
        Assert.NotNull(eventName);

        var resolvedType = ClientEventRegistry.GetEventType(eventName);

        Assert.Equal(originalType, resolvedType);
    }

    [Fact]
    public void RoundTrip_NameToTypeToName_ReturnsOriginalName()
    {
        var originalName = "voice.tier-upgrade";

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

    // =========================================================================
    // INTERNAL EVENT EXCLUSION TESTS
    // Verifies that internal events (x-internal: true) are NOT in the registry
    // =========================================================================

    [Fact]
    public void InternalEvents_NotRegisteredByName_SessionCapabilities()
    {
        // SessionCapabilitiesEvent is internal - should not be in registry
        var eventType = ClientEventRegistry.GetEventType("permission.session-capabilities");
        Assert.Null(eventType);
    }

    [Fact]
    public void InternalEvents_NotRegisteredByName_ShortcutPublished()
    {
        // ShortcutPublishedEvent is internal - should not be in registry
        var eventType = ClientEventRegistry.GetEventType("session.shortcut-published");
        Assert.Null(eventType);
    }

    [Fact]
    public void InternalEvents_NotRegisteredByName_ShortcutRevoked()
    {
        // ShortcutRevokedEvent is internal - should not be in registry
        var eventType = ClientEventRegistry.GetEventType("session.shortcut-revoked");
        Assert.Null(eventType);
    }

    [Fact]
    public void InternalEventNames_NotInAllEventNames()
    {
        var names = ClientEventRegistry.GetAllEventNames().ToList();

        // Internal events should NOT be in the list
        Assert.DoesNotContain("permission.session-capabilities", names);
        Assert.DoesNotContain("session.shortcut-published", names);
        Assert.DoesNotContain("session.shortcut-revoked", names);
    }
}
