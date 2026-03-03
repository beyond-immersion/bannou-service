using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Unit tests for ChatServicePlugin: plugin name, display name, and inheritance chain.
/// OnRunningAsync (built-in room type registration) requires full DI and is
/// covered by HTTP integration tests rather than unit tests.
/// </summary>
public class ChatServicePluginTests
{
    /// <summary>
    /// Verifies the plugin name matches the expected service identifier.
    /// </summary>
    [Fact]
    public void ChatServicePlugin_PluginName_ShouldBeChat()
    {
        var plugin = new ChatServicePlugin();

        Assert.Equal("chat", plugin.PluginName);
    }

    /// <summary>
    /// Verifies the display name for logging and diagnostics.
    /// </summary>
    [Fact]
    public void ChatServicePlugin_DisplayName_ShouldBeChatService()
    {
        var plugin = new ChatServicePlugin();

        Assert.Equal("Chat Service", plugin.DisplayName);
    }

    /// <summary>
    /// Verifies the plugin inherits from the correct StandardServicePlugin base.
    /// </summary>
    [Fact]
    public void ChatServicePlugin_ShouldInheritFromStandardServicePlugin()
    {
        var plugin = new ChatServicePlugin();

        Assert.IsAssignableFrom<StandardServicePlugin<IChatService>>(plugin);
    }

    /// <summary>
    /// Verifies ConfigureServices registers the expected background workers.
    /// Uses a real ServiceCollection to verify registrations.
    /// </summary>
    [Fact]
    public void ChatServicePlugin_ConfigureServices_RegistersBackgroundWorkers()
    {
        var plugin = new ChatServicePlugin();
        var services = new ServiceCollection();

        plugin.ConfigureServices(services);

        // Verify all 4 background workers are registered as hosted services
        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
            .ToList();

        Assert.True(hostedServiceDescriptors.Count >= 4,
            $"Expected at least 4 hosted services, got {hostedServiceDescriptors.Count}");

        var implementationTypes = hostedServiceDescriptors
            .Select(d => d.ImplementationType?.Name ?? d.ServiceType.Name)
            .ToList();

        Assert.Contains("IdleRoomCleanupWorker", implementationTypes);
        Assert.Contains("TypingExpiryWorker", implementationTypes);
        Assert.Contains("BanExpiryWorker", implementationTypes);
        Assert.Contains("MessageRetentionWorker", implementationTypes);
    }
}
