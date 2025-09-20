using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Dapr-first implementation for Accounts service following schema-first architecture
/// Uses Dapr state management for persistence instead of Entity Framework
/// </summary>
[DaprService("accounts", typeof(IAccountsService), lifetime: ServiceLifetime.Scoped)]
public class AccountsService : IAccountsService
{
    private readonly ILogger<AccountsService> _logger;
    private readonly AccountsServiceConfiguration _configuration;
    private readonly DaprClient _daprClient;

    private const string ACCOUNTS_STATE_STORE = "statestore";
    private const string ACCOUNTS_KEY_PREFIX = "account-";
    private const string EMAIL_INDEX_KEY_PREFIX = "email-index-";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string ACCOUNT_CREATED_TOPIC = "account.created";
    private const string ACCOUNT_UPDATED_TOPIC = "account.updated";
    private const string ACCOUNT_DELETED_TOPIC = "account.deleted";

    public AccountsService(
        ILogger<AccountsService> logger,
        AccountsServiceConfiguration configuration,
        DaprClient daprClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
    }

    public Task<(StatusCodes, AccountListResponse?)> ListAccountsAsync(
        string? email = null,
        string? displayName = null,
        Provider? provider = null,
        bool? verified = null,
        int? page = 1,
        int? pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Apply default values for pagination parameters
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            _logger.LogDebug("Listing accounts with filters - Email: {Email}, DisplayName: {DisplayName}, Provider: {Provider}, Verified: {Verified}, Page: {Page}, PageSize: {PageSize}",
                email, displayName, provider, verified, page, pageSize);

            // TODO: Implement pagination and filtering with Dapr state store queries
            // For now, return empty list as placeholder
            var response = new AccountListResponse
            {
                Accounts = new List<AccountResponse>(),
                TotalCount = 0,
                Page = page ?? 1,
                PageSize = pageSize ?? 20
            };

            return Task.FromResult<(StatusCodes, AccountListResponse?)>(((StatusCodes, AccountListResponse?))(StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing accounts");
            return Task.FromResult<(StatusCodes, AccountListResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> CreateAccountAsync(
        CreateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogCritical("🚨 ACCOUNTS SERVICE: CreateAccountAsync called - This message should appear in logs!");
        try
        {
            _logger.LogInformation("DEBUG: CreateAccountAsync - Entry point reached");
            _logger.LogDebug("Creating account for email: {Email}", body.Email);

            _logger.LogInformation("DEBUG: CreateAccountAsync - About to create account entity");
            // Create account entity
            var accountId = Guid.NewGuid();
            _logger.LogInformation("DEBUG: CreateAccountAsync - AccountId generated: {AccountId}", accountId);

            var account = new AccountModel
            {
                AccountId = accountId.ToString(),
                Email = body.Email,
                DisplayName = body.DisplayName,
                PasswordHash = body.PasswordHash, // Store pre-hashed password from Auth service
                IsVerified = body.EmailVerified == true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _logger.LogInformation("DEBUG: CreateAccountAsync - AccountModel created successfully");

            _logger.LogInformation("DEBUG: CreateAccountAsync - About to save account to Dapr state store");
            // Store in Dapr state store (replaces Entity Framework)
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account);
            _logger.LogInformation("DEBUG: CreateAccountAsync - Account saved to state store");

            _logger.LogInformation("DEBUG: CreateAccountAsync - About to save email index");
            // Create email index for quick lookup
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{EMAIL_INDEX_KEY_PREFIX}{body.Email.ToLowerInvariant()}",
                accountId.ToString());
            _logger.LogInformation("DEBUG: CreateAccountAsync - Email index saved");

            _logger.LogDebug("Account created successfully: {AccountId}", accountId);

            // Publish account created event
            // TODO: Temporarily disabled to debug segfault
            // await PublishAccountCreatedEventAsync(account);

            // Return success response
            var response = new AccountResponse
            {
                AccountId = accountId,
                Email = account.Email,
                DisplayName = account.DisplayName,
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = new List<string>(),
                AuthMethods = new List<AuthMethodInfo>()
            };

            return (StatusCodes.Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating account");
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving account: {AccountId}", accountId);

            // Get from Dapr state store (replaces Entity Framework query)
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            var response = new AccountResponse
            {
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = new List<string>(),
                AuthMethods = new List<AuthMethodInfo>()
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account: {AccountId}", accountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating account: {AccountId}", accountId);

            // Get existing account
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for update: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            // Track changes for event publishing
            var changedFields = new List<string>();
            var previousValues = new Dictionary<string, object?>();
            var newValues = new Dictionary<string, object?>();

            // Update fields if provided
            if (body.DisplayName != null && body.DisplayName != account.DisplayName)
            {
                changedFields.Add("displayName");
                previousValues["displayName"] = account.DisplayName;
                newValues["displayName"] = body.DisplayName;
                account.DisplayName = body.DisplayName;
            }
            // TODO: Handle roles and metadata from body

            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account);

            _logger.LogInformation("Account updated successfully: {AccountId}", accountId);

            // Publish account updated event if there were changes
            if (changedFields.Count > 0)
            {
                await PublishAccountUpdatedEventAsync(accountId, changedFields, previousValues, newValues);
            }

            var response = new AccountResponse
            {
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = new List<string>(),
                AuthMethods = new List<AuthMethodInfo>()
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating account: {AccountId}", accountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> GetAccountByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving account by email: {Email}", email);

            // Get the account ID from email index
            var accountId = await _daprClient.GetStateAsync<string>(
                ACCOUNTS_STATE_STORE,
                $"{EMAIL_INDEX_KEY_PREFIX}{email.ToLowerInvariant()}",
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(accountId))
            {
                _logger.LogWarning("No account found for email: {Email}", email);
                return (StatusCodes.NotFound, null);
            }

            // Get the full account data
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account data not found for ID: {AccountId} (from email: {Email})", accountId, email);
                return (StatusCodes.NotFound, null);
            }

            // Convert to response model
            var response = new AccountResponse
            {
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                PasswordHash = account.PasswordHash, // Include password hash for auth service validation
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = new List<string>(), // TODO: Implement roles from account data
                AuthMethods = new List<AuthMethodInfo>() // TODO: Implement auth methods from account data
            };

            _logger.LogDebug("Account retrieved successfully for email: {Email}, AccountId: {AccountId}", email, accountId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account by email: {Email}", email);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public Task<(StatusCodes, AuthMethodsResponse?)> GetAuthMethodsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting auth methods for account: {AccountId}", accountId);

            // TODO: Implement auth methods retrieval
            var response = new AuthMethodsResponse
            {
                AuthMethods = new List<AuthMethodInfo>()
            };

            return Task.FromResult<(StatusCodes, AuthMethodsResponse?)>(((StatusCodes, AuthMethodsResponse?))(StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting auth methods: {AccountId}", accountId);
            return Task.FromResult<(StatusCodes, AuthMethodsResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    public Task<(StatusCodes, AuthMethodResponse?)> AddAuthMethodAsync(
        Guid accountId,
        AddAuthMethodRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Adding auth method for account: {AccountId}, provider: {Provider}", accountId, body.Provider);

            // TODO: Implement auth method addition
            var response = new AuthMethodResponse
            {
                MethodId = Guid.NewGuid(),
                Provider = AuthMethodResponseProvider.Google, // TODO: Parse body.Provider to enum
                ExternalId = body.ExternalId ?? string.Empty,
                LinkedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult<(StatusCodes, AuthMethodResponse?)>(((StatusCodes, AuthMethodResponse?))(StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding auth method: {AccountId}", accountId);
            return Task.FromResult<(StatusCodes, AuthMethodResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    public Task<(StatusCodes, AccountResponse?)> GetAccountByProviderAsync(
        Provider2 provider,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting account by provider: {Provider}, externalId: {ExternalId}", provider, externalId);

            // TODO: Implement provider-based account lookup
            _logger.LogWarning("GetAccountByProvider not fully implemented - requires provider indexing");
            return Task.FromResult<(StatusCodes, AccountResponse?)>((StatusCodes.InternalServerError, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account by provider: {Provider}", provider);
            return Task.FromResult<(StatusCodes, AccountResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> UpdateProfileAsync(
        Guid accountId,
        UpdateProfileRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating profile for account: {AccountId}", accountId);

            // Handle profile update similar to account update
            try
            {
                var account = await _daprClient.GetStateAsync<AccountModel>(
                    ACCOUNTS_STATE_STORE,
                    $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                    cancellationToken: cancellationToken);

                if (account == null)
                {
                    _logger.LogWarning("Account not found for profile update: {AccountId}", accountId);
                    return (StatusCodes.NotFound, null);
                }

                // Update profile fields
                if (body.DisplayName != null)
                    account.DisplayName = body.DisplayName;
                // TODO: Handle metadata from body

                account.UpdatedAt = DateTimeOffset.UtcNow;

                await _daprClient.SaveStateAsync(
                    ACCOUNTS_STATE_STORE,
                    $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                    account,
                    cancellationToken: cancellationToken);

                var response = new AccountResponse
                {
                    AccountId = Guid.Parse(account.AccountId),
                    Email = account.Email,
                    DisplayName = account.DisplayName,
                    EmailVerified = account.IsVerified,
                    CreatedAt = account.CreatedAt,
                    UpdatedAt = account.UpdatedAt,
                    Roles = new List<string>(),
                    AuthMethods = new List<AuthMethodInfo>()
                };

                return (StatusCodes.OK, response);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Inner exception updating profile: {AccountId}", accountId);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile: {AccountId}", accountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // Add missing methods from interface
    public async Task<(StatusCodes, object?)> DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting account: {AccountId}", accountId);

            // Get existing account for event publishing
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for deletion: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            // Soft delete by setting DeletedAt timestamp
            account.DeletedAt = DateTimeOffset.UtcNow;

            // Save the soft-deleted account
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account,
                cancellationToken: cancellationToken);

            // Remove email index
            await _daprClient.DeleteStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{EMAIL_INDEX_KEY_PREFIX}{account.Email.ToLowerInvariant()}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Account deleted successfully: {AccountId}", accountId);

            // Publish account deleted event
            await PublishAccountDeletedEventAsync(account, "User requested deletion");

            return (StatusCodes.NoContent, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account: {AccountId}", accountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public Task<(StatusCodes, object?)> RemoveAuthMethodAsync(
        Guid accountId,
        Guid methodId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Removing auth method {MethodId} for account: {AccountId}", methodId, accountId);
            // TODO: Implement auth method removal
            _logger.LogWarning("RemoveAuthMethod not fully implemented");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing auth method: {AccountId}", accountId);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    public Task<(StatusCodes, object?)> UpdatePasswordHashAsync(
        Guid accountId,
        UpdatePasswordRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating password hash for account: {AccountId}", accountId);
            // TODO: Implement password hash update with secure hashing
            _logger.LogWarning("UpdatePasswordHash not fully implemented");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password hash: {AccountId}", accountId);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    public Task<(StatusCodes, object?)> UpdateVerificationStatusAsync(
        Guid accountId,
        UpdateVerificationRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating verification status for account: {AccountId}", accountId);
            // TODO: Implement verification status update
            _logger.LogWarning("UpdateVerificationStatus not fully implemented");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating verification status: {AccountId}", accountId);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Publish AccountCreatedEvent to RabbitMQ via Dapr
    /// </summary>
    private async Task PublishAccountCreatedEventAsync(AccountModel account)
    {
        try
        {
            var eventModel = new AccountCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                Roles = new List<string>() // TODO: Load actual roles from account
            };

            await _daprClient.PublishEventAsync(PUBSUB_NAME, ACCOUNT_CREATED_TOPIC, eventModel);
            _logger.LogDebug("Published AccountCreatedEvent for account: {AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish AccountCreatedEvent for account: {AccountId}", account.AccountId);
            // Don't throw - event publishing failure shouldn't break account creation
        }
    }

    /// <summary>
    /// Publish AccountUpdatedEvent to RabbitMQ via Dapr
    /// </summary>
    private async Task PublishAccountUpdatedEventAsync(
        Guid accountId,
        List<string> changedFields,
        Dictionary<string, object?> previousValues,
        Dictionary<string, object?> newValues)
    {
        try
        {
            var eventModel = new AccountUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                ChangedFields = changedFields,
                PreviousValues = previousValues,
                NewValues = newValues
            };

            await _daprClient.PublishEventAsync(PUBSUB_NAME, ACCOUNT_UPDATED_TOPIC, eventModel);
            _logger.LogDebug("Published AccountUpdatedEvent for account: {AccountId}", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish AccountUpdatedEvent for account: {AccountId}", accountId);
            // Don't throw - event publishing failure shouldn't break account update
        }
    }

    /// <summary>
    /// Publish AccountDeletedEvent to RabbitMQ via Dapr
    /// </summary>
    private async Task PublishAccountDeletedEventAsync(AccountModel account, string deletionReason)
    {
        try
        {
            var eventModel = new AccountDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DeletionReason = deletionReason
            };

            await _daprClient.PublishEventAsync(PUBSUB_NAME, ACCOUNT_DELETED_TOPIC, eventModel);
            _logger.LogDebug("Published AccountDeletedEvent for account: {AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish AccountDeletedEvent for account: {AccountId}", account.AccountId);
            // Don't throw - event publishing failure shouldn't break account deletion
        }
    }
}

/// <summary>
/// Account data model for Dapr state storage
/// Replaces Entity Framework entities
/// </summary>
public class AccountModel
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PasswordHash { get; set; } // BCrypt hashed password for authentication
    public bool IsVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
