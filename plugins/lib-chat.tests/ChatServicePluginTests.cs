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


}
