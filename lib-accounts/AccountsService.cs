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

    private const string ACCOUNTS_STATE_STORE = "accounts-statestore"; // MySQL-backed state store
    private const string ACCOUNTS_KEY_PREFIX = "account-";
    private const string EMAIL_INDEX_KEY_PREFIX = "email-index-";
    private const string PROVIDER_INDEX_KEY_PREFIX = "provider-index-"; // provider:externalId -> accountId
    private const string AUTH_METHODS_KEY_PREFIX = "auth-methods-"; // accountId -> List<AuthMethodInfo>
    private const string ACCOUNTS_LIST_KEY = "accounts-list"; // Sorted list of all account IDs for pagination
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

    public async Task<(StatusCodes, AccountListResponse?)> ListAccountsAsync(
        ListAccountsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract parameters from request body
            var emailFilter = body.Email;
            var displayNameFilter = body.DisplayName;
            var providerFilter = body.Provider;
            var verifiedFilter = body.Verified;
            var page = body.Page;
            var pageSize = body.PageSize;

            // Apply default values for pagination parameters
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 100) pageSize = 100; // Cap at 100

            var hasFilters = !string.IsNullOrWhiteSpace(emailFilter) ||
                            !string.IsNullOrWhiteSpace(displayNameFilter) ||
                            providerFilter.HasValue ||
                            verifiedFilter.HasValue;

            _logger.LogInformation("Listing accounts - Page: {Page}, PageSize: {PageSize}, HasFilters: {HasFilters}",
                page, pageSize, hasFilters);

            // Get the list of all account IDs (sorted by creation order)
            var accountIds = await _daprClient.GetStateAsync<List<string>>(
                ACCOUNTS_STATE_STORE,
                ACCOUNTS_LIST_KEY,
                cancellationToken: cancellationToken) ?? new List<string>();

            var totalCount = accountIds.Count;

            // Optimized path: No filters - load only the page we need
            if (!hasFilters)
            {
                var skip = (page - 1) * pageSize;

                // Get the subset of account IDs for this page (newest first)
                var pageAccountIds = accountIds
                    .AsEnumerable()
                    .Reverse() // Newest first (accounts added to end of list)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToList();

                var pagedAccounts = new List<AccountResponse>();
                foreach (var accountId in pageAccountIds)
                {
                    var account = await LoadAccountResponseAsync(accountId, cancellationToken);
                    if (account != null)
                    {
                        pagedAccounts.Add(account);
                    }
                }

                var response = new AccountListResponse
                {
                    Accounts = pagedAccounts,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                };

                _logger.LogInformation("Returning {Count} accounts (Total: {Total}, optimized path)", pagedAccounts.Count, totalCount);
                return (StatusCodes.OK, response);
            }

            // Filtered path: Need to load accounts to apply filters
            // For efficiency, we scan accounts and apply filters in batches
            var filteredAccounts = new List<AccountResponse>();
            var batchSize = 100; // Process 100 accounts at a time to reduce memory

            // Scan in reverse order (newest first) for consistent ordering
            var reversedIds = accountIds.AsEnumerable().Reverse().ToList();

            for (var i = 0; i < reversedIds.Count; i += batchSize)
            {
                var batchIds = reversedIds.Skip(i).Take(batchSize);

                foreach (var accountId in batchIds)
                {
                    var account = await LoadAccountResponseAsync(accountId, cancellationToken);
                    if (account == null) continue;

                    // Apply filters
                    if (!string.IsNullOrWhiteSpace(emailFilter) &&
                        !account.Email.Contains(emailFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrWhiteSpace(displayNameFilter) &&
                        (string.IsNullOrEmpty(account.DisplayName) ||
                        !account.DisplayName.Contains(displayNameFilter, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (providerFilter.HasValue &&
                        account.AuthMethods?.Any(m => m.Provider.ToString() == providerFilter.Value.ToString()) != true)
                        continue;

                    if (verifiedFilter.HasValue && account.EmailVerified != verifiedFilter.Value)
                        continue;

                    filteredAccounts.Add(account);
                }
            }

            var filteredTotalCount = filteredAccounts.Count;
            var skip2 = (page - 1) * pageSize;
            var pagedFilteredAccounts = filteredAccounts.Skip(skip2).Take(pageSize).ToList();

            var filteredResponse = new AccountListResponse
            {
                Accounts = pagedFilteredAccounts,
                TotalCount = filteredTotalCount,
                Page = page,
                PageSize = pageSize
            };

            _logger.LogInformation("Returning {Count} accounts (Total: {Total}, filtered path)", pagedFilteredAccounts.Count, filteredTotalCount);
            return (StatusCodes.OK, filteredResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing accounts");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Load a full AccountResponse for an account ID including auth methods.
    /// Returns null if account doesn't exist or is deleted.
    /// </summary>
    private async Task<AccountResponse?> LoadAccountResponseAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _daprClient.GetStateAsync<AccountModel>(
            ACCOUNTS_STATE_STORE,
            $"{ACCOUNTS_KEY_PREFIX}{accountId}",
            cancellationToken: cancellationToken);

        if (account == null || account.DeletedAt.HasValue)
            return null;

        var authMethods = await _daprClient.GetStateAsync<List<AuthMethodInfo>>(
            ACCOUNTS_STATE_STORE,
            $"{AUTH_METHODS_KEY_PREFIX}{accountId}",
            cancellationToken: cancellationToken) ?? new List<AuthMethodInfo>();

        return new AccountResponse
        {
            AccountId = Guid.Parse(account.AccountId),
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            Roles = account.Roles,
            AuthMethods = authMethods
        };
    }

    public async Task<(StatusCodes, AccountResponse?)> CreateAccountAsync(
        CreateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating account for email: {Email}", body.Email);

            // Check if email already exists
            var existingAccountId = await _daprClient.GetStateAsync<string>(
                ACCOUNTS_STATE_STORE,
                $"{EMAIL_INDEX_KEY_PREFIX}{body.Email.ToLowerInvariant()}",
                cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(existingAccountId))
            {
                _logger.LogWarning("Account with email {Email} already exists (AccountId: {AccountId})", body.Email, existingAccountId);
                return (StatusCodes.Conflict, null);
            }

            // Create account entity
            var accountId = Guid.NewGuid();

            // Determine roles - start with roles from request body, default to "user" role
            var roles = body.Roles?.ToList() ?? new List<string>();

            // All registered accounts get the "user" role by default if no roles specified
            // This ensures they have basic authenticated access to APIs
            if (roles.Count == 0)
            {
                roles.Add("user");
                _logger.LogDebug("Assigning default 'user' role to new account: {Email}", body.Email);
            }

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

            // Add to accounts list for pagination
            await AddAccountToIndexAsync(accountId.ToString(), cancellationToken);

            _logger.LogInformation("Account created: {AccountId} for email: {Email} with roles: {Roles}",
                accountId, body.Email, string.Join(", ", roles));

            // Publish account created event
            await PublishAccountCreatedEventAsync(account);

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
        GetAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
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

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);

            var response = new AccountResponse
            {
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = account.Roles, // Return stored roles
                AuthMethods = authMethods
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> UpdateAccountAsync(
        UpdateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
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

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);

            var response = new AccountResponse
            {
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = account.Roles, // Return stored roles
                AuthMethods = authMethods
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating account: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> GetAccountByEmailAsync(
        GetAccountByEmailRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var email = body.Email;
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

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId, cancellationToken);

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
                AuthMethods = authMethods
            };

            _logger.LogInformation("Account retrieved for email: {Email}, AccountId: {AccountId}", email, accountId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account by email: {Email}", body.Email);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AuthMethodsResponse?)> GetAuthMethodsAsync(
        GetAuthMethodsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Getting auth methods for account: {AccountId}", accountId);

            // Verify account exists
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);

            var response = new AuthMethodsResponse
            {
                AuthMethods = authMethods
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting auth methods: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, AuthMethodResponse?)> AddAuthMethodAsync(
        AddAuthMethodRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Adding auth method for account: {AccountId}, provider: {Provider}", accountId, body.Provider);

            // Verify account exists
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get existing auth methods
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethods = await _daprClient.GetStateAsync<List<AuthMethodInfo>>(
                ACCOUNTS_STATE_STORE,
                authMethodsKey,
                cancellationToken: cancellationToken) ?? new List<AuthMethodInfo>();

            // Check if this provider is already linked
            var existingMethod = authMethods.FirstOrDefault(m =>
                m.Provider.ToString() == body.Provider.ToString() && m.ExternalId == body.ExternalId);

            if (existingMethod != null)
            {
                return (StatusCodes.Conflict, null);
            }

            // Create new auth method
            var methodId = Guid.NewGuid();
            var newMethod = new AuthMethodInfo
            {
                MethodId = methodId,
                Provider = MapProviderToAuthMethodProvider(body.Provider),
                ExternalId = body.ExternalId ?? string.Empty,
                LinkedAt = DateTimeOffset.UtcNow
            };

            authMethods.Add(newMethod);

            // Save updated auth methods
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                authMethodsKey,
                authMethods,
                cancellationToken: cancellationToken);

            // Create provider index for lookup
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{body.Provider}:{body.ExternalId}";
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                providerIndexKey,
                accountId.ToString(),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Auth method added for account: {AccountId}, methodId: {MethodId}, provider: {Provider}",
                accountId, methodId, body.Provider);

            var response = new AuthMethodResponse
            {
                MethodId = methodId,
                Provider = MapProviderToAuthMethodResponseProvider(body.Provider),
                ExternalId = body.ExternalId ?? string.Empty,
                LinkedAt = DateTimeOffset.UtcNow
            };

            return (StatusCodes.Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding auth method: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Maps AddAuthMethodRequestProvider to AuthMethodInfoProvider
    /// </summary>
    private static AuthMethodInfoProvider MapProviderToAuthMethodProvider(AddAuthMethodRequestProvider provider)
    {
        return provider switch
        {
            AddAuthMethodRequestProvider.Discord => AuthMethodInfoProvider.Discord,
            AddAuthMethodRequestProvider.Google => AuthMethodInfoProvider.Google,
            AddAuthMethodRequestProvider.Steam => AuthMethodInfoProvider.Steam,
            AddAuthMethodRequestProvider.Twitch => AuthMethodInfoProvider.Twitch,
            _ => AuthMethodInfoProvider.Google // Default fallback
        };
    }

    /// <summary>
    /// Maps AddAuthMethodRequestProvider to AuthMethodResponseProvider
    /// </summary>
    private static AuthMethodResponseProvider MapProviderToAuthMethodResponseProvider(AddAuthMethodRequestProvider provider)
    {
        return provider switch
        {
            AddAuthMethodRequestProvider.Discord => AuthMethodResponseProvider.Discord,
            AddAuthMethodRequestProvider.Google => AuthMethodResponseProvider.Google,
            AddAuthMethodRequestProvider.Steam => AuthMethodResponseProvider.Steam,
            AddAuthMethodRequestProvider.Twitch => AuthMethodResponseProvider.Twitch,
            _ => AuthMethodResponseProvider.Google // Default fallback
        };
    }

    public async Task<(StatusCodes, AccountResponse?)> GetAccountByProviderAsync(
        GetAccountByProviderRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var provider = body.Provider;
            var externalId = body.ExternalId;
            _logger.LogInformation("Getting account by provider: {Provider}, externalId: {ExternalId}", provider, externalId);

            // Build the provider index key
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{provider}:{externalId}";

            // Get the account ID from provider index
            var accountId = await _daprClient.GetStateAsync<string>(
                ACCOUNTS_STATE_STORE,
                providerIndexKey,
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(accountId))
            {
                _logger.LogWarning("No account found for provider: {Provider}, externalId: {ExternalId}", provider, externalId);
                return (StatusCodes.NotFound, null);
            }

            // Get the full account data
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account data not found for ID: {AccountId} (from provider: {Provider})", accountId, provider);
                return (StatusCodes.NotFound, null);
            }

            // Check if account is soft-deleted
            if (account.DeletedAt.HasValue)
            {
                _logger.LogWarning("Account is deleted for provider: {Provider}, externalId: {ExternalId}", provider, externalId);
                return (StatusCodes.NotFound, null);
            }

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId, cancellationToken);

            var response = new AccountResponse
            {
                AccountId = Guid.Parse(account.AccountId),
                Email = account.Email,
                DisplayName = account.DisplayName,
                EmailVerified = account.IsVerified,
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt,
                Roles = account.Roles,
                AuthMethods = authMethods
            };

            _logger.LogInformation("Account retrieved for provider: {Provider}, externalId: {ExternalId}", provider, externalId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account by provider: {Provider}", body.Provider);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Helper method to get auth methods for an account
    /// </summary>
    private async Task<List<AuthMethodInfo>> GetAuthMethodsForAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        try
        {
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethods = await _daprClient.GetStateAsync<List<AuthMethodInfo>>(
                ACCOUNTS_STATE_STORE,
                authMethodsKey,
                cancellationToken: cancellationToken);

            return authMethods ?? new List<AuthMethodInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get auth methods for account: {AccountId}", accountId);
            return new List<AuthMethodInfo>();
        }
    }

    public async Task<(StatusCodes, AccountResponse?)> UpdateProfileAsync(
        UpdateProfileRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
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

                // Get auth methods for the account
                var authMethods = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);

                var response = new AccountResponse
                {
                    AccountId = Guid.Parse(account.AccountId),
                    Email = account.Email,
                    DisplayName = account.DisplayName,
                    EmailVerified = account.IsVerified,
                    CreatedAt = account.CreatedAt,
                    UpdatedAt = account.UpdatedAt,
                    Roles = account.Roles, // Return stored roles
                    AuthMethods = authMethods
                };

                return (StatusCodes.OK, response);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Inner exception updating profile: {AccountId}", body.AccountId);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // Add missing methods from interface
    public async Task<(StatusCodes, object?)> DeleteAccountAsync(
        DeleteAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
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

            // Remove from accounts list index
            await RemoveAccountFromIndexAsync(accountId.ToString(), cancellationToken);

            _logger.LogInformation("Account deleted: {AccountId}", accountId);

            // Publish account deleted event
            await PublishAccountDeletedEventAsync(accountId, account, "User requested deletion");

            return (StatusCodes.NoContent, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, object?)> RemoveAuthMethodAsync(
        RemoveAuthMethodRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            var methodId = body.MethodId;
            _logger.LogInformation("Removing auth method {MethodId} for account: {AccountId}", methodId, accountId);

            // Verify account exists
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get existing auth methods
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethods = await _daprClient.GetStateAsync<List<AuthMethodInfo>>(
                ACCOUNTS_STATE_STORE,
                authMethodsKey,
                cancellationToken: cancellationToken) ?? new List<AuthMethodInfo>();

            // Find the auth method to remove
            var methodToRemove = authMethods.FirstOrDefault(m => m.MethodId == methodId);
            if (methodToRemove == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Remove the auth method
            authMethods.Remove(methodToRemove);

            // Save updated auth methods
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                authMethodsKey,
                authMethods,
                cancellationToken: cancellationToken);

            // Remove provider index
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{methodToRemove.Provider}:{methodToRemove.ExternalId}";
            await _daprClient.DeleteStateAsync(
                ACCOUNTS_STATE_STORE,
                providerIndexKey,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Auth method removed for account: {AccountId}, methodId: {MethodId}",
                accountId, methodId);

            return (StatusCodes.NoContent, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing auth method: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, object?)> UpdatePasswordHashAsync(
        UpdatePasswordRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating password hash for account: {AccountId}", accountId);

            // Get existing account
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for password update: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            // Update password hash (should already be hashed by Auth service)
            account.PasswordHash = body.PasswordHash;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Password hash updated for account: {AccountId}", accountId);
            return (StatusCodes.OK, new { Message = "Password updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password hash: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, object?)> UpdateVerificationStatusAsync(
        UpdateVerificationRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating verification status for account: {AccountId}, Verified: {Verified}",
                accountId, body.EmailVerified);

            // Get existing account
            var account = await _daprClient.GetStateAsync<AccountModel>(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                cancellationToken: cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for verification update: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            // Update verification status
            account.IsVerified = body.EmailVerified;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await _daprClient.SaveStateAsync(
                ACCOUNTS_STATE_STORE,
                $"{ACCOUNTS_KEY_PREFIX}{accountId}",
                account,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Verification status updated for account: {AccountId} -> {Verified}",
                accountId, body.EmailVerified);
            return (StatusCodes.OK, new { Message = "Verification status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating verification status: {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
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

            _logger.LogDebug("Publishing AccountDeletedEvent for account {AccountId} to topic {Topic}",
                accountId, ACCOUNT_DELETED_TOPIC);

            await _daprClient.PublishEventAsync(PUBSUB_NAME, ACCOUNT_DELETED_TOPIC, eventModel);
            _logger.LogDebug("Published AccountDeletedEvent for account {AccountId}", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish AccountDeletedEvent for account {AccountId}", accountId);
            // Don't throw - event publishing failure shouldn't break account deletion
        }
    }

    #region Account Index Management

    /// <summary>
    /// Adds an account ID to the accounts list index for pagination support.
    /// </summary>
    private async Task AddAccountToIndexAsync(string accountId, CancellationToken cancellationToken)
    {
        try
        {
            var accountIds = await _daprClient.GetStateAsync<List<string>>(
                ACCOUNTS_STATE_STORE,
                ACCOUNTS_LIST_KEY,
                cancellationToken: cancellationToken) ?? new List<string>();

            if (!accountIds.Contains(accountId))
            {
                accountIds.Add(accountId);
                await _daprClient.SaveStateAsync(
                    ACCOUNTS_STATE_STORE,
                    ACCOUNTS_LIST_KEY,
                    accountIds,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Added account {AccountId} to accounts index (total: {Count})", accountId, accountIds.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add account {AccountId} to index", accountId);
            throw; // Re-throw as this is critical for account creation
        }
    }

    /// <summary>
    /// Removes an account ID from the accounts list index.
    /// </summary>
    private async Task RemoveAccountFromIndexAsync(string accountId, CancellationToken cancellationToken)
    {
        try
        {
            var accountIds = await _daprClient.GetStateAsync<List<string>>(
                ACCOUNTS_STATE_STORE,
                ACCOUNTS_LIST_KEY,
                cancellationToken: cancellationToken) ?? new List<string>();

            if (accountIds.Remove(accountId))
            {
                await _daprClient.SaveStateAsync(
                    ACCOUNTS_STATE_STORE,
                    ACCOUNTS_LIST_KEY,
                    accountIds,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Removed account {AccountId} from accounts index (remaining: {Count})", accountId, accountIds.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove account {AccountId} from index", accountId);
            // Don't throw - index cleanup failure shouldn't break account deletion
        }
    }

    #endregion

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

    // Store as Unix epoch timestamps (long) to avoid Dapr/System.Text.Json DateTimeOffset serialization bugs
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
    public long? DeletedAtUnix { get; set; }

    // Expose as DateTimeOffset for code convenience (not serialized)
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset CreatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
        set => CreatedAtUnix = value.ToUnixTimeSeconds();
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset UpdatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(UpdatedAtUnix);
        set => UpdatedAtUnix = value.ToUnixTimeSeconds();
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset? DeletedAt
    {
        get => DeletedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(DeletedAtUnix.Value) : null;
        set => DeletedAtUnix = value?.ToUnixTimeSeconds();
    }
}
