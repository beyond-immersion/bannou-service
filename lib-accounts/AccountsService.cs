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

            _logger.LogInformation("Listing accounts - Page: {Page}, PageSize: {PageSize}, Filters: Email={Email}, DisplayName={DisplayName}, Provider={Provider}, Verified={Verified}",
                page, pageSize, email ?? "(none)", displayName ?? "(none)", provider?.ToString() ?? "(none)", verified?.ToString() ?? "(none)");

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
        try
        {
            _logger.LogInformation("Creating account for email: {Email}", body.Email);

            // Create account entity
            var accountId = Guid.NewGuid();

            // Determine roles - start with roles from request body
            var roles = body.Roles?.ToList() ?? new List<string>();

            // Apply ENV-based admin role assignment
            if (ShouldAssignAdminRole(body.Email))
            {
                if (!roles.Contains("admin"))
                {
                    roles.Add("admin");
                    _logger.LogInformation("Auto-assigning admin role to {Email} based on configuration", body.Email);
                }
            }

            var account = new AccountModel
            {
                AccountId = accountId.ToString(),
                Email = body.Email,
                DisplayName = body.DisplayName,
                PasswordHash = body.PasswordHash, // Store pre-hashed password from Auth service
                IsVerified = body.EmailVerified == true,
                Roles = roles, // Store roles in account model
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Store in Dapr state store (replaces Entity Framework)
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account);

            // Create email index for quick lookup
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{EMAIL_INDEX_KEY_PREFIX}{body.Email.ToLowerInvariant()}",
                accountId.ToString());

            _logger.LogInformation("Account created: {AccountId} for email: {Email} with roles: {Roles}",
                accountId, body.Email, string.Join(", ", roles));

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
                Roles = account.Roles, // Return stored roles
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

    /// <summary>
    /// Determines if admin role should be auto-assigned based on email configuration.
    /// Checks AdminEmails (comma-separated list) and AdminEmailDomain settings.
    /// </summary>
    private bool ShouldAssignAdminRole(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var emailLower = email.ToLowerInvariant();

        // Check AdminEmails (comma-separated list of exact email addresses)
        if (!string.IsNullOrWhiteSpace(_configuration.AdminEmails))
        {
            var adminEmails = _configuration.AdminEmails
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.ToLowerInvariant());

            if (adminEmails.Contains(emailLower))
            {
                _logger.LogDebug("Email {Email} matches AdminEmails configuration", email);
                return true;
            }
        }

        // Check AdminEmailDomain (e.g., "@admin.test.local")
        if (!string.IsNullOrWhiteSpace(_configuration.AdminEmailDomain))
        {
            var domain = _configuration.AdminEmailDomain.ToLowerInvariant();
            // Ensure domain starts with @ for proper suffix matching
            if (!domain.StartsWith("@"))
                domain = "@" + domain;

            if (emailLower.EndsWith(domain))
            {
                _logger.LogDebug("Email {Email} matches AdminEmailDomain {Domain}", email, domain);
                return true;
            }
        }

        return false;
    }

    public async Task<(StatusCodes, AccountResponse?)> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Retrieving account: {AccountId}", accountId);

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

            // Check if account is soft-deleted
            if (account.DeletedAt.HasValue)
            {
                _logger.LogWarning("Account is deleted: {AccountId}", accountId);
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
                Roles = account.Roles, // Return stored roles
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
            _logger.LogInformation("Updating account: {AccountId}", accountId);

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

            // Handle roles update if provided
            if (body.Roles != null)
            {
                var newRoles = body.Roles.ToList();
                if (!account.Roles.SequenceEqual(newRoles))
                {
                    changedFields.Add("roles");
                    previousValues["roles"] = account.Roles;
                    newValues["roles"] = newRoles;
                    account.Roles = newRoles;
                }
            }
            // TODO: Handle metadata from body

            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account);

            _logger.LogInformation("Account updated: {AccountId}", accountId);

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
                Roles = account.Roles, // Return stored roles
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
            _logger.LogInformation("Retrieving account by email: {Email}", email);

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

            // Check if account is soft-deleted
            if (account.DeletedAt.HasValue)
            {
                _logger.LogWarning("Account is deleted for email: {Email}, AccountId: {AccountId}", email, accountId);
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
                Roles = account.Roles, // Return stored roles
                AuthMethods = new List<AuthMethodInfo>() // TODO: Implement auth methods from account data
            };

            _logger.LogInformation("Account retrieved for email: {Email}, AccountId: {AccountId}", email, accountId);
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
            _logger.LogInformation("Getting auth methods for account: {AccountId}", accountId);

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
            _logger.LogInformation("Adding auth method for account: {AccountId}, provider: {Provider}", accountId, body.Provider);

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
            _logger.LogInformation("Getting account by provider: {Provider}, externalId: {ExternalId}", provider, externalId);

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
            _logger.LogInformation("Updating profile for account: {AccountId}", accountId);

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
                    Roles = account.Roles, // Return stored roles
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
            _logger.LogInformation("Deleting account: {AccountId}", accountId);

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

            _logger.LogInformation("Account deleted: {AccountId}", accountId);

            // Publish account deleted event
            await PublishAccountDeletedEventAsync(accountId, account, "User requested deletion");

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
            _logger.LogInformation("Removing auth method {MethodId} for account: {AccountId}", methodId, accountId);
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
            _logger.LogInformation("Updating password hash for account: {AccountId}", accountId);
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
            _logger.LogInformation("Updating verification status for account: {AccountId}", accountId);
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
                Roles = account.Roles // Use stored roles
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
    private async Task PublishAccountDeletedEventAsync(Guid accountId, AccountModel account, string deletionReason)
    {
        try
        {
            var eventModel = new AccountDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Email = account.Email,
                DeletionReason = deletionReason
            };

            await _daprClient.PublishEventAsync(PUBSUB_NAME, ACCOUNT_DELETED_TOPIC, eventModel);
            _logger.LogDebug("Published AccountDeletedEvent for account: {AccountId}", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish AccountDeletedEvent for account: {AccountId}", accountId);
            // Don't throw - event publishing failure shouldn't break account deletion
        }
    }

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Overrides the default IDaprService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Accounts service permissions... (starting)");
        try
        {
            await AccountsPermissionRegistration.RegisterViaEventAsync(_daprClient, _logger);
            _logger.LogInformation("Accounts service permissions registered via event (complete)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Accounts service permissions");
            throw;
        }
    }

    #endregion
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
    public List<string> Roles { get; set; } = new List<string>(); // User roles (admin, user, etc.)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
