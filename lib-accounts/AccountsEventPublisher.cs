using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Events;
using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Type-safe event publisher for Accounts service events.
/// Provides strongly-typed methods for publishing account lifecycle events.
/// </summary>
/// <remarks>
/// This class could be auto-generated from schemas/accounts-events.yaml.
/// Topic naming convention: {entity}.{action} (e.g., "account.created").
/// </remarks>
public class AccountsEventPublisher : EventPublisherBase
{
    private const string ACCOUNT_CREATED_TOPIC = "account.created";
    private const string ACCOUNT_UPDATED_TOPIC = "account.updated";
    private const string ACCOUNT_DELETED_TOPIC = "account.deleted";

    /// <summary>
    /// Creates an AccountsEventPublisher with the specified dependencies.
    /// </summary>
    /// <param name="daprClient">Dapr client for publishing events.</param>
    /// <param name="logger">Logger for event operations.</param>
    public AccountsEventPublisher(DaprClient daprClient, ILogger<AccountsEventPublisher> logger)
        : base(daprClient, logger)
    {
    }

    /// <summary>
    /// Publishes an account created event.
    /// </summary>
    /// <param name="accountId">ID of the created account.</param>
    /// <param name="email">Email address of the created account.</param>
    /// <param name="displayName">Display name of the created account.</param>
    /// <param name="roles">Roles assigned to the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if event was published successfully.</returns>
    public async Task<bool> PublishAccountCreatedAsync(
        Guid accountId,
        string email,
        string? displayName = null,
        IEnumerable<string>? roles = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AccountCreatedEvent
        {
            EventId = NewEventId(),
            Timestamp = CurrentTimestamp(),
            AccountId = accountId,
            Email = email,
            DisplayName = displayName ?? string.Empty,
            Roles = roles?.ToList() ?? []
        };

        Logger.LogInformation("Publishing AccountCreated event for account {AccountId}", accountId);
        return await PublishEventAsync(ACCOUNT_CREATED_TOPIC, eventData, cancellationToken);
    }

    /// <summary>
    /// Publishes an account updated event with current account state.
    /// </summary>
    /// <param name="account">The updated account response containing current state.</param>
    /// <param name="changedFields">Fields that were changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if event was published successfully.</returns>
    public async Task<bool> PublishAccountUpdatedAsync(
        AccountResponse account,
        IEnumerable<string> changedFields,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AccountUpdatedEvent
        {
            EventId = NewEventId(),
            Timestamp = CurrentTimestamp(),
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName ?? string.Empty,
            EmailVerified = account.EmailVerified,
            Roles = account.Roles?.ToList() ?? [],
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt ?? DateTimeOffset.UtcNow,
            Metadata = account.Metadata ?? new Dictionary<string, object>(),
            ChangedFields = changedFields.ToList()
        };

        Logger.LogInformation("Publishing AccountUpdated event for account {AccountId}", account.AccountId);
        return await PublishEventAsync(ACCOUNT_UPDATED_TOPIC, eventData, cancellationToken);
    }

    /// <summary>
    /// Publishes an account deleted event with account state at deletion time.
    /// </summary>
    /// <param name="account">The account being deleted.</param>
    /// <param name="deletedReason">Optional reason for account deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if event was published successfully.</returns>
    public async Task<bool> PublishAccountDeletedAsync(
        AccountResponse account,
        string? deletedReason = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = new AccountDeletedEvent
        {
            EventId = NewEventId(),
            Timestamp = CurrentTimestamp(),
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName ?? string.Empty,
            EmailVerified = account.EmailVerified,
            Roles = account.Roles?.ToList() ?? [],
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt ?? DateTimeOffset.UtcNow,
            Metadata = account.Metadata ?? new Dictionary<string, object>(),
            DeletedReason = deletedReason
        };

        Logger.LogInformation("Publishing AccountDeleted event for account {AccountId}", account.AccountId);
        return await PublishEventAsync(ACCOUNT_DELETED_TOPIC, eventData, cancellationToken);
    }
}
