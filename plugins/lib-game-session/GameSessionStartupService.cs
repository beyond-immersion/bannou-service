using BeyondImmersion.BannouService.Subscription;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Background service that initializes game session subscription caches at startup.
/// Fetches all accounts subscribed to our game services so we can quickly filter
/// session.connected events and know which accounts should receive game shortcuts.
/// </summary>
public class GameSessionStartupService : BackgroundService
{
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly ILogger<GameSessionStartupService> _logger;

    /// <summary>
    /// Game service stub names that this service handles.
    /// </summary>
    private static readonly string[] SupportedGameServices = { "arcadia", "generic" };

    /// <summary>
    /// Creates a new GameSessionStartupService instance.
    /// </summary>
    /// <param name="subscriptionClient">Subscription client for fetching account subscriptions.</param>
    /// <param name="logger">Logger for this service.</param>
    public GameSessionStartupService(
        ISubscriptionClient subscriptionClient,
        ILogger<GameSessionStartupService> logger)
    {
        _subscriptionClient = subscriptionClient;
        _logger = logger;
    }

    /// <summary>
    /// Executes the startup initialization to load subscribed accounts.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a short time for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        _logger.LogInformation("GameSessionStartupService initializing subscription caches...");

        try
        {
            await InitializeSubscriptionCachesAsync(stoppingToken);
            _logger.LogInformation("GameSessionStartupService initialization complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GameSessionStartupService initialization cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GameSessionStartupService initialization failed - will rely on on-demand loading");
        }
    }

    /// <summary>
    /// Fetches all accounts subscribed to our game services and caches them.
    /// </summary>
    private async Task InitializeSubscriptionCachesAsync(CancellationToken cancellationToken)
    {
        foreach (var stubName in SupportedGameServices)
        {
            try
            {
                _logger.LogDebug("Fetching accounts subscribed to {StubName}...", stubName);

                var response = await _subscriptionClient.QueryCurrentSubscriptionsAsync(
                    new QueryCurrentSubscriptionsRequest { StubName = stubName },
                    cancellationToken);

                if (response?.AccountIds != null && response.AccountIds.Count > 0)
                {
                    // Cache each subscribed account
                    foreach (var accountId in response.AccountIds)
                    {
                        GameSessionService.AddAccountSubscription(accountId, stubName);
                    }

                    _logger.LogInformation("Loaded {Count} subscribed accounts for {StubName}",
                        response.AccountIds.Count, stubName);
                }
                else
                {
                    _logger.LogDebug("No accounts subscribed to {StubName}", stubName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch subscriptions for {StubName}", stubName);
            }
        }
    }
}
