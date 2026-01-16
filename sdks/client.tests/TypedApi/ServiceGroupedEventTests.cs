// =============================================================================
// Service-Grouped Event Tests
// Tests for the service-grouped event subscription classes.
// =============================================================================

using System.Reflection;
using BeyondImmersion.Bannou.Client.Events;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.TypedApi;

/// <summary>
/// Tests for service-grouped event subscription classes.
/// Verifies BannouClientEvents structure and subscription method signatures.
/// </summary>
public class ServiceGroupedEventTests
{
    // =========================================================================
    // CONTAINER CLASS TESTS
    // =========================================================================

    [Fact]
    public void BannouClientEvents_Exists()
    {
        var type = typeof(BannouClientEvents);
        Assert.NotNull(type);
        Assert.True(type.IsSealed);
        Assert.True(type.IsClass);
    }

    [Fact]
    public void BannouClientEvents_HasGameSessionProperty()
    {
        var property = typeof(BannouClientEvents).GetProperty("GameSession");
        Assert.NotNull(property);
        Assert.Equal(typeof(GameSessionEventSubscriptions), property.PropertyType);
    }

    [Fact]
    public void BannouClientEvents_HasVoiceProperty()
    {
        var property = typeof(BannouClientEvents).GetProperty("Voice");
        Assert.NotNull(property);
        Assert.Equal(typeof(VoiceEventSubscriptions), property.PropertyType);
    }

    [Fact]
    public void BannouClientEvents_HasSystemProperty()
    {
        var property = typeof(BannouClientEvents).GetProperty("System");
        Assert.NotNull(property);
        Assert.Equal(typeof(SystemEventSubscriptions), property.PropertyType);
    }

    [Fact]
    public void BannouClientEvents_HasMultipleServiceProperties()
    {
        var properties = typeof(BannouClientEvents)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.Name.EndsWith("EventSubscriptions"))
            .ToList();

        // Should have Asset, GameSession, Matchmaking, System, Voice at minimum
        Assert.True(properties.Count >= 5, $"Expected at least 5 service properties, found {properties.Count}");
    }

    // =========================================================================
    // SUBSCRIPTION CLASS TESTS
    // =========================================================================

    [Fact]
    public void GameSessionEventSubscriptions_Exists()
    {
        var type = typeof(GameSessionEventSubscriptions);
        Assert.NotNull(type);
        Assert.True(type.IsSealed);
    }

    [Fact]
    public void VoiceEventSubscriptions_Exists()
    {
        var type = typeof(VoiceEventSubscriptions);
        Assert.NotNull(type);
        Assert.True(type.IsSealed);
    }

    // =========================================================================
    // SUBSCRIPTION METHOD TESTS
    // =========================================================================

    [Fact]
    public void GameSessionEventSubscriptions_HasOnChatMessageReceived()
    {
        var method = typeof(GameSessionEventSubscriptions).GetMethod("OnChatMessageReceived");
        Assert.NotNull(method);
        Assert.True(method.IsPublic);
        Assert.Equal(typeof(IEventSubscription), method.ReturnType);
    }

    [Fact]
    public void GameSessionEventSubscriptions_HasOnPlayerJoined()
    {
        var method = typeof(GameSessionEventSubscriptions).GetMethod("OnPlayerJoined");
        Assert.NotNull(method);
        Assert.True(method.IsPublic);
    }

    [Fact]
    public void VoiceEventSubscriptions_HasOnPeerJoined()
    {
        var method = typeof(VoiceEventSubscriptions).GetMethod("OnVoicePeerJoined");
        Assert.NotNull(method);
        Assert.True(method.IsPublic);
    }

    [Fact]
    public void SubscriptionMethods_TakeActionHandler()
    {
        var method = typeof(GameSessionEventSubscriptions).GetMethod("OnChatMessageReceived");
        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Single(parameters);

        var handlerType = parameters[0].ParameterType;
        Assert.True(handlerType.IsGenericType);
        Assert.Equal(typeof(Action<>), handlerType.GetGenericTypeDefinition());
        Assert.Equal(typeof(ChatMessageReceivedEvent), handlerType.GetGenericArguments()[0]);
    }

    [Fact]
    public void SubscriptionMethods_ReturnIEventSubscription()
    {
        var methods = typeof(GameSessionEventSubscriptions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("On") && m.DeclaringType == typeof(GameSessionEventSubscriptions));

        foreach (var method in methods)
        {
            Assert.Equal(typeof(IEventSubscription), method.ReturnType);
        }
    }

    // =========================================================================
    // SUBSCRIPTION COUNT TESTS
    // =========================================================================

    [Fact]
    public void GameSessionEventSubscriptions_HasMultipleMethods()
    {
        var methods = typeof(GameSessionEventSubscriptions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("On") && m.DeclaringType == typeof(GameSessionEventSubscriptions))
            .ToList();

        // GameSession has: SessionStateChanged, PlayerJoined, PlayerLeft, PlayerKicked,
        // ChatMessageReceived, GameStateUpdated, GameActionResult
        Assert.True(methods.Count >= 5, $"Expected at least 5 subscription methods, found {methods.Count}");
    }

    [Fact]
    public void VoiceEventSubscriptions_HasMultipleMethods()
    {
        var methods = typeof(VoiceEventSubscriptions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("On") && m.DeclaringType == typeof(VoiceEventSubscriptions))
            .ToList();

        // Voice has: RoomState, PeerJoined, PeerLeft, PeerUpdated, TierUpgrade, RoomClosed
        Assert.True(methods.Count >= 5, $"Expected at least 5 subscription methods, found {methods.Count}");
    }

    // =========================================================================
    // NAMESPACE TESTS
    // =========================================================================

    [Fact]
    public void ServiceGroupedClasses_AreInEventsNamespace()
    {
        Assert.Equal(
            "BeyondImmersion.Bannou.Client.Events",
            typeof(BannouClientEvents).Namespace);

        Assert.Equal(
            "BeyondImmersion.Bannou.Client.Events",
            typeof(GameSessionEventSubscriptions).Namespace);
    }

    // =========================================================================
    // CONSTRUCTOR TESTS
    // =========================================================================

    [Fact]
    public void SubscriptionClasses_HaveInternalConstructors()
    {
        var constructor = typeof(GameSessionEventSubscriptions).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotEmpty(constructor);
        Assert.True(constructor[0].IsAssembly, "Constructor should be internal");
    }
}
