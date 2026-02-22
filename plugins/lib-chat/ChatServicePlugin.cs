using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Plugin wrapper for Chat service enabling plugin-based discovery and lifecycle management.
/// Registers built-in room types (text, sentiment, emoji) during startup.
/// </summary>
public class ChatServicePlugin : StandardServicePlugin<IChatService>
{
    public override string PluginName => "chat";
    public override string DisplayName => "Chat Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        services.AddHostedService<IdleRoomCleanupWorker>();
        services.AddHostedService<TypingExpiryWorker>();

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Running phase - registers built-in room types (text, sentiment, emoji).
    /// These are global types available to all game services.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        await RegisterBuiltInRoomTypesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Registers the three built-in room types as global (no game service scope).
    /// Idempotent: existing types return Conflict which is silently ignored.
    /// </summary>
    private async Task RegisterBuiltInRoomTypesAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException(
            "ServiceProvider not available during OnRunningAsync");

        using var scope = serviceProvider.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var builtInTypes = new[]
        {
            new RegisterRoomTypeRequest
            {
                Code = "text",
                DisplayName = "Text Chat",
                Description = "Standard text-based chat room",
                MessageFormat = MessageFormat.Text,
                PersistenceMode = PersistenceMode.Persistent,
            },
            new RegisterRoomTypeRequest
            {
                Code = "sentiment",
                DisplayName = "Sentiment Chat",
                Description = "Sentiment-based room for emotional expression tracking",
                MessageFormat = MessageFormat.Sentiment,
                PersistenceMode = PersistenceMode.Persistent,
            },
            new RegisterRoomTypeRequest
            {
                Code = "emoji",
                DisplayName = "Emoji Chat",
                Description = "Emoji-only room with code-based content validation",
                MessageFormat = MessageFormat.Emoji,
                PersistenceMode = PersistenceMode.Ephemeral,
            },
        };

        foreach (var roomType in builtInTypes)
        {
            var (status, _) = await chatService.RegisterRoomTypeAsync(roomType, cancellationToken);
            if (status == StatusCodes.OK)
            {
                Logger?.LogInformation("Registered built-in room type: {Code}", roomType.Code);
            }
            else if (status == StatusCodes.Conflict)
            {
                Logger?.LogDebug("Built-in room type already registered: {Code}", roomType.Code);
            }
            else
            {
                Logger?.LogError("Failed to register built-in room type {Code}: {Status}", roomType.Code, status);
                await messageBus.TryPublishErrorAsync(
                    "chat", "RegisterBuiltInRoomTypes", "StartupFailure",
                    $"Failed to register built-in room type '{roomType.Code}': status {status}",
                    severity: ServiceErrorEventSeverity.Error);
            }
        }
    }
}
