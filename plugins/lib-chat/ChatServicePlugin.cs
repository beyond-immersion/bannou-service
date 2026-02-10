using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Plugin wrapper for Chat service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ChatServicePlugin : StandardServicePlugin<IChatService>
{
    public override string PluginName => "chat";
    public override string DisplayName => "Chat Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register any chat-specific helper services here
        // Example: services.AddScoped<IChatMessageValidator, ChatMessageValidator>();

        Logger?.LogDebug("Service dependencies configured");
    }
}
