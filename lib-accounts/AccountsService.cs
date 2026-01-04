using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// State-backed implementation for Accounts service following schema-first architecture.
/// Uses IStateStoreFactory for persistence.
/// </summary>
[BannouService("accounts", typeof(IAccountsService), lifetime: ServiceLifetime.Scoped)]
public partial class AccountsService : IAccountsService
{
    private readonly ILogger<AccountsService> _logger;
    private readonly AccountsServiceConfiguration _configuration;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;

    private const string ACCOUNTS_STATE_STORE = "accounts-statestore"; // MySQL-backed state store
    private const string ACCOUNTS_KEY_PREFIX = "account-";
    private const string EMAIL_INDEX_KEY_PREFIX = "email-index-";
    private const string PROVIDER_INDEX_KEY_PREFIX = "provider-index-"; // provider:externalId -> accountId
    private const string AUTH_METHODS_KEY_PREFIX = "auth-methods-"; // accountId -> List<AuthMethodInfo>
    private const string ACCOUNTS_LIST_KEY = "accounts-list"; // Sorted list of all account IDs for pagination
    private const string ACCOUNT_CREATED_TOPIC = "account.created";
    private const string ACCOUNT_UPDATED_TOPIC = "account.updated";
    private const string ACCOUNT_DELETED_TOPIC = "account.deleted";

    public AccountsService(
        ILogger<AccountsService> logger,
        AccountsServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IEventConsumer eventConsumer)
    {
        _logger = logger;
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;

        // Register event handlers via partial class (AccountsServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
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
            var accountIdStore = _stateStoreFactory.GetStore<List<string>>(ACCOUNTS_STATE_STORE);
            var accountIds = await accountIdStore.GetAsync(ACCOUNTS_LIST_KEY, cancellationToken) ?? new List<string>();

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
                var skippedCount = 0;
                foreach (var accountId in pageAccountIds)
                {
                    var account = await LoadAccountResponseAsync(accountId, cancellationToken);
                    if (account != null)
                    {
                        pagedAccounts.Add(account);
                    }
                    else
                    {
                        skippedCount++;
                        _logger.LogWarning("Account {AccountId} in index but failed to load - possible data inconsistency", accountId);
                    }
                }

                if (skippedCount > 0)
                {
                    _logger.LogWarning("Skipped {SkippedCount} accounts that failed to load on page {Page}", skippedCount, page);
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
                    if (account == null)
                    {
                        _logger.LogWarning("Account {AccountId} in index but failed to load - possible data inconsistency", accountId);
                        continue;
                    }

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
            await PublishErrorEventAsync(
                "ListAccounts",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.Page, body.PageSize });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Load a full AccountResponse for an account ID including auth methods.
    /// Returns null if account doesn't exist or is deleted.
    /// </summary>
    private async Task<AccountResponse?> LoadAccountResponseAsync(string accountId, CancellationToken cancellationToken)
    {
        var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
        var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

        if (account == null || account.DeletedAt.HasValue)
            return null;

        var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(ACCOUNTS_STATE_STORE);
        var authMethods = await authMethodsStore.GetAsync($"{AUTH_METHODS_KEY_PREFIX}{accountId}", cancellationToken) ?? new List<AuthMethodInfo>();

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
            var emailIndexStore = _stateStoreFactory.GetStore<string>(ACCOUNTS_STATE_STORE);
            var existingAccountId = await emailIndexStore.GetAsync(
                $"{EMAIL_INDEX_KEY_PREFIX}{body.Email.ToLowerInvariant()}",
                cancellationToken);

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

            // Store in state store
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            await accountStore.SaveAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", account);

            // Create email index for quick lookup
            await emailIndexStore.SaveAsync(
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

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating account");
            await PublishErrorEventAsync(
                "CreateAccount",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.Email });
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

            // Get from lib-state store (replaces Entity Framework query)
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

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
            await PublishErrorEventAsync(
                "GetAccount",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
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
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for update: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            // Track changes for event publishing
            var changedFields = new List<string>();

            // Update fields if provided
            if (body.DisplayName != null && body.DisplayName != account.DisplayName)
            {
                changedFields.Add("displayName");
                account.DisplayName = body.DisplayName;
            }

            // Handle roles update if provided
            if (body.Roles != null)
            {
                var newRoles = body.Roles.ToList();
                if (!account.Roles.SequenceEqual(newRoles))
                {
                    changedFields.Add("roles");
                    account.Roles = newRoles;
                }
            }

            // Handle metadata update if provided
            if (body.Metadata != null)
            {
                var newMetadata = ConvertToMetadataDictionary(body.Metadata);
                if (newMetadata != null)
                {
                    var currentMetadata = account.Metadata ?? new Dictionary<string, object>();
                    if (!MetadataEquals(currentMetadata, newMetadata))
                    {
                        changedFields.Add("metadata");
                        account.Metadata = newMetadata;
                    }
                }
            }

            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await accountStore.SaveAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", account);

            _logger.LogInformation("Account updated: {AccountId}", accountId);

            // Publish account updated event if there were changes
            if (changedFields.Count > 0)
            {
                await PublishAccountUpdatedEventAsync(account, changedFields);
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
            await PublishErrorEventAsync(
                "UpdateAccount",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
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
            var emailIndexStore = _stateStoreFactory.GetStore<string>(ACCOUNTS_STATE_STORE);
            var accountId = await emailIndexStore.GetAsync(
                $"{EMAIL_INDEX_KEY_PREFIX}{email.ToLowerInvariant()}",
                cancellationToken);

            if (string.IsNullOrEmpty(accountId))
            {
                _logger.LogWarning("No account found for email: {Email}", email);
                return (StatusCodes.NotFound, null);
            }

            // Get the full account data
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

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
            await PublishErrorEventAsync(
                "GetAccountByEmail",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.Email });
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
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

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
            await PublishErrorEventAsync(
                "GetAuthMethods",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
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
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get existing auth methods
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(ACCOUNTS_STATE_STORE);
            var authMethods = await authMethodsStore.GetAsync(authMethodsKey, cancellationToken) ?? new List<AuthMethodInfo>();

            // Validate ExternalId - required for OAuth linking and provider index
            if (string.IsNullOrEmpty(body.ExternalId))
            {
                _logger.LogWarning("OAuth link attempt with empty ExternalId for account {AccountId}, provider {Provider}",
                    accountId, body.Provider);
                return (StatusCodes.BadRequest, null);
            }

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
                Provider = MapOAuthProviderToAuthProvider(body.Provider),
                ExternalId = body.ExternalId,
                LinkedAt = DateTimeOffset.UtcNow
            };

            authMethods.Add(newMethod);

            // Save updated auth methods
            await authMethodsStore.SaveAsync(authMethodsKey, authMethods);

            // Create provider index for lookup
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{body.Provider}:{body.ExternalId}";
            var providerIndexStore = _stateStoreFactory.GetStore<string>(ACCOUNTS_STATE_STORE);
            await providerIndexStore.SaveAsync(providerIndexKey, accountId.ToString());

            _logger.LogInformation("Auth method added for account: {AccountId}, methodId: {MethodId}, provider: {Provider}",
                accountId, methodId, body.Provider);

            // Publish account updated event
            await PublishAccountUpdatedEventAsync(account, new[] { "authMethods" });

            var response = new AuthMethodResponse
            {
                MethodId = methodId,
                Provider = body.Provider,
                ExternalId = body.ExternalId, // Already validated non-empty above
                LinkedAt = DateTimeOffset.UtcNow
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding auth method: {AccountId}", body.AccountId);
            await PublishErrorEventAsync(
                "AddAuthMethod",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId, body.Provider });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Maps OAuthProvider (OAuth-only providers) to AuthProvider (all providers including email)
    /// </summary>
    private static AuthProvider MapOAuthProviderToAuthProvider(OAuthProvider provider)
    {
        return provider switch
        {
            OAuthProvider.Discord => AuthProvider.Discord,
            OAuthProvider.Google => AuthProvider.Google,
            OAuthProvider.Steam => AuthProvider.Steam,
            OAuthProvider.Twitch => AuthProvider.Twitch,
            _ => AuthProvider.Google // Default fallback
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
            var providerIndexStore = _stateStoreFactory.GetStore<string>(ACCOUNTS_STATE_STORE);
            var accountId = await providerIndexStore.GetAsync(providerIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(accountId))
            {
                _logger.LogWarning("No account found for provider: {Provider}, externalId: {ExternalId}", provider, externalId);
                return (StatusCodes.NotFound, null);
            }

            // Get the full account data
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

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
            await PublishErrorEventAsync(
                "GetAccountByProvider",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.Provider, body.ExternalId });
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
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(ACCOUNTS_STATE_STORE);
            var authMethods = await authMethodsStore.GetAsync(authMethodsKey, cancellationToken);

            return authMethods ?? new List<AuthMethodInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get auth methods for account: {AccountId}", accountId);
            _ = PublishErrorEventAsync("GetAuthMethodsForAccount", ex.GetType().Name, ex.Message, dependency: "state", details: new { AccountId = accountId });
            throw; // Don't mask state store failures - empty list should mean "no auth methods", not "error"
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
                var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
                var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

                if (account == null)
                {
                    _logger.LogWarning("Account not found for profile update: {AccountId}", accountId);
                    return (StatusCodes.NotFound, null);
                }

                // Update profile fields
                if (body.DisplayName != null)
                    account.DisplayName = body.DisplayName;

                // Handle metadata update if provided
                if (body.Metadata != null)
                {
                    var newMetadata = ConvertToMetadataDictionary(body.Metadata);
                    if (newMetadata != null)
                    {
                        account.Metadata = newMetadata;
                    }
                }

                account.UpdatedAt = DateTimeOffset.UtcNow;

                await accountStore.SaveAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", account);

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
            await PublishErrorEventAsync(
                "UpdateProfile",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
            return (StatusCodes.InternalServerError, null);
        }
    }

    // Add missing methods from interface
    public async Task<StatusCodes> DeleteAccountAsync(
        DeleteAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Deleting account: {AccountId}", accountId);

            // Get existing account for event publishing
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for deletion: {AccountId}", accountId);
                return StatusCodes.NotFound;
            }

            // Soft delete by setting DeletedAt timestamp
            account.DeletedAt = DateTimeOffset.UtcNow;

            // Save the soft-deleted account
            await accountStore.SaveAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", account);

            // Remove email index
            var emailIndexStore = _stateStoreFactory.GetStore<string>(ACCOUNTS_STATE_STORE);
            await emailIndexStore.DeleteAsync($"{EMAIL_INDEX_KEY_PREFIX}{account.Email.ToLowerInvariant()}", cancellationToken);

            // Remove from accounts list index
            await RemoveAccountFromIndexAsync(accountId.ToString(), cancellationToken);

            _logger.LogInformation("Account deleted: {AccountId}", accountId);

            // Publish account deleted event
            await PublishAccountDeletedEventAsync(account, "User requested deletion");

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account: {AccountId}", body.AccountId);
            await PublishErrorEventAsync(
                "DeleteAccount",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
            return StatusCodes.InternalServerError;
        }
    }

    public async Task<StatusCodes> RemoveAuthMethodAsync(
        RemoveAuthMethodRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            var methodId = body.MethodId;
            _logger.LogInformation("Removing auth method {MethodId} for account: {AccountId}", methodId, accountId);

            // Verify account exists
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return StatusCodes.NotFound;
            }

            // Get existing auth methods
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(ACCOUNTS_STATE_STORE);
            var authMethods = await authMethodsStore.GetAsync(authMethodsKey, cancellationToken) ?? new List<AuthMethodInfo>();

            // Find the auth method to remove
            var methodToRemove = authMethods.FirstOrDefault(m => m.MethodId == methodId);
            if (methodToRemove == null)
            {
                return StatusCodes.NotFound;
            }

            // Remove the auth method
            authMethods.Remove(methodToRemove);

            // Save updated auth methods
            await authMethodsStore.SaveAsync(authMethodsKey, authMethods, cancellationToken: cancellationToken);

            // Remove provider index
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{methodToRemove.Provider}:{methodToRemove.ExternalId}";
            var providerIndexStore = _stateStoreFactory.GetStore<string>(ACCOUNTS_STATE_STORE);
            await providerIndexStore.DeleteAsync(providerIndexKey, cancellationToken);

            _logger.LogInformation("Auth method removed for account: {AccountId}, methodId: {MethodId}",
                accountId, methodId);

            await PublishAccountUpdatedEventAsync(account, new[] { "authMethods" });

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing auth method: {AccountId}", body.AccountId);
            await PublishErrorEventAsync(
                "RemoveAuthMethod",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId, body.MethodId });
            return StatusCodes.InternalServerError;
        }
    }

    public async Task<StatusCodes> UpdatePasswordHashAsync(
        UpdatePasswordRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating password hash for account: {AccountId}", accountId);

            // Get existing account
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for password update: {AccountId}", accountId);
                return StatusCodes.NotFound;
            }

            // Update password hash (should already be hashed by Auth service)
            account.PasswordHash = body.PasswordHash;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await accountStore.SaveAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", account, cancellationToken: cancellationToken);

            _logger.LogInformation("Password hash updated for account: {AccountId}", accountId);
            await PublishAccountUpdatedEventAsync(account, new[] { "passwordHash" });

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password hash: {AccountId}", body.AccountId);
            await PublishErrorEventAsync(
                "UpdatePasswordHash",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
            return StatusCodes.InternalServerError;
        }
    }

    public async Task<StatusCodes> UpdateVerificationStatusAsync(
        UpdateVerificationRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating verification status for account: {AccountId}, Verified: {Verified}",
                accountId, body.EmailVerified);

            // Get existing account
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(ACCOUNTS_STATE_STORE);
            var account = await accountStore.GetAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for verification update: {AccountId}", accountId);
                return StatusCodes.NotFound;
            }

            // Update verification status
            account.IsVerified = body.EmailVerified;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account
            await accountStore.SaveAsync($"{ACCOUNTS_KEY_PREFIX}{accountId}", account, cancellationToken: cancellationToken);

            _logger.LogInformation("Verification status updated for account: {AccountId} -> {Verified}",
                accountId, body.EmailVerified);

            await PublishAccountUpdatedEventAsync(account, new[] { "isVerified" });
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating verification status: {AccountId}", body.AccountId);
            await PublishErrorEventAsync(
                "UpdateVerificationStatus",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { body.AccountId });
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Publish AccountCreatedEvent to RabbitMQ via IMessageBus.
    /// TryPublishAsync handles buffering, retry, and error logging internally.
    /// </summary>
    private async Task PublishAccountCreatedEventAsync(AccountModel account)
    {
        var eventModel = new AccountCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = Guid.Parse(account.AccountId),
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            Roles = account.Roles ?? [],
            CreatedAt = account.CreatedAt
        };

        await _messageBus.TryPublishAsync(ACCOUNT_CREATED_TOPIC, eventModel);
        _logger.LogDebug("Published AccountCreatedEvent for account: {AccountId}", account.AccountId);
    }

    /// <summary>
    /// Publish AccountUpdatedEvent to RabbitMQ via IMessageBus.
    /// Event contains the current state of the account plus which fields changed.
    /// TryPublishAsync handles buffering, retry, and error logging internally.
    /// </summary>
    private async Task PublishAccountUpdatedEventAsync(AccountModel account, IEnumerable<string> changedFields)
    {
        var eventModel = new AccountUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = Guid.Parse(account.AccountId),
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            Roles = account.Roles ?? [],
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.TryPublishAsync(ACCOUNT_UPDATED_TOPIC, eventModel);
        _logger.LogDebug("Published AccountUpdatedEvent for account: {AccountId}", account.AccountId);
    }

    /// <summary>
    /// Publish AccountDeletedEvent to RabbitMQ via IMessageBus.
    /// Event contains the final state of the account before deletion.
    /// TryPublishAsync handles buffering, retry, and error logging internally.
    /// </summary>
    private async Task PublishAccountDeletedEventAsync(AccountModel account, string? deletedReason)
    {
        var eventModel = new AccountDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = Guid.Parse(account.AccountId),
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            Roles = account.Roles ?? [],
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.TryPublishAsync(ACCOUNT_DELETED_TOPIC, eventModel);
        _logger.LogDebug("Published AccountDeletedEvent for account {AccountId}", account.AccountId);
    }

    #region Account Index Management

    /// <summary>
    /// Adds an account ID to the accounts list index for pagination support.
    /// Uses optimistic concurrency with retry for concurrent updates.
    /// </summary>
    private async Task AddAccountToIndexAsync(string accountId, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var indexStore = _stateStoreFactory.GetStore<List<string>>(ACCOUNTS_STATE_STORE);

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var (accountIds, etag) = await indexStore.GetWithETagAsync(ACCOUNTS_LIST_KEY, cancellationToken);
                accountIds ??= new List<string>();

                if (!accountIds.Contains(accountId))
                {
                    accountIds.Add(accountId);

                    // Use optimistic concurrency - retry if etag mismatch
                    var saved = string.IsNullOrEmpty(etag)
                        ? await indexStore.SaveAsync(ACCOUNTS_LIST_KEY, accountIds, cancellationToken: cancellationToken) != null
                        : await indexStore.TrySaveAsync(ACCOUNTS_LIST_KEY, accountIds, etag, cancellationToken);

                    if (saved)
                    {
                        _logger.LogDebug("Added account {AccountId} to accounts index (total: {Count})", accountId, accountIds.Count);
                        return;
                    }

                    // ETag mismatch - retry
                    _logger.LogDebug("ETag mismatch adding {AccountId} to index, retrying (attempt {Attempt})", accountId, attempt + 1);
                    continue;
                }

                return; // Already in list
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Error adding account {AccountId} to index, retrying (attempt {Attempt})", accountId, attempt + 1);
                await Task.Delay(100 * (attempt + 1), cancellationToken);
            }
        }

        throw new InvalidOperationException($"Failed to add account {accountId} to index after {maxRetries} attempts");
    }

    /// <summary>
    /// Removes an account ID from the accounts list index.
    /// Uses optimistic concurrency with retry for concurrent updates.
    /// </summary>
    private async Task RemoveAccountFromIndexAsync(string accountId, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var indexStore = _stateStoreFactory.GetStore<List<string>>(ACCOUNTS_STATE_STORE);

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var (accountIds, etag) = await indexStore.GetWithETagAsync(ACCOUNTS_LIST_KEY, cancellationToken);
                accountIds ??= new List<string>();

                if (accountIds.Remove(accountId))
                {
                    // Use optimistic concurrency - retry if etag mismatch
                    var saved = string.IsNullOrEmpty(etag)
                        ? await indexStore.SaveAsync(ACCOUNTS_LIST_KEY, accountIds, cancellationToken: cancellationToken) != null
                        : await indexStore.TrySaveAsync(ACCOUNTS_LIST_KEY, accountIds, etag, cancellationToken);

                    if (saved)
                    {
                        _logger.LogDebug("Removed account {AccountId} from accounts index (remaining: {Count})", accountId, accountIds.Count);
                        return;
                    }

                    // ETag mismatch - retry
                    _logger.LogDebug("ETag mismatch removing {AccountId} from index, retrying (attempt {Attempt})", accountId, attempt + 1);
                    continue;
                }

                return; // Not in list
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Error removing account {AccountId} from index, retrying (attempt {Attempt})", accountId, attempt + 1);
                await Task.Delay(100 * (attempt + 1), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove account {AccountId} from index after {MaxRetries} attempts", accountId, maxRetries);
                await _messageBus.TryPublishErrorAsync(
                    "accounts",
                    "RemoveAccountFromIndex",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "state",
                    cancellationToken: cancellationToken);
                // Index cleanup failure must propagate - orphaned index entries are a data integrity issue
                throw;
            }
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Accounts service permissions... (starting)");
        try
        {
            await AccountsPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
            _logger.LogInformation("Accounts service permissions registered via event (complete)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Accounts service permissions");
            await PublishErrorEventAsync("RegisterServicePermissions", ex.GetType().Name, ex.Message, dependency: "permissions");
            throw;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Compares two metadata dictionaries for equality.
    /// </summary>
    private static bool MetadataEquals(IDictionary<string, object> a, IDictionary<string, object> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var bValue)) return false;
            if (!Equals(kvp.Value, bValue)) return false;
        }
        return true;
    }

    /// <summary>
    /// Converts an object (typically from JSON deserialization) to a metadata dictionary.
    /// Handles JsonElement and Dictionary types.
    /// </summary>
    private static Dictionary<string, object>? ConvertToMetadataDictionary(object? metadata)
    {
        if (metadata == null) return null;

        // Handle JsonElement from System.Text.Json deserialization
        if (metadata is System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            var result = new Dictionary<string, object>();
            foreach (var property in jsonElement.EnumerateObject())
            {
                result[property.Name] = ConvertJsonElement(property.Value);
            }
            return result;
        }

        // Handle Dictionary types directly
        if (metadata is IDictionary<string, object> dict)
        {
            return new Dictionary<string, object>(dict);
        }

        // Handle generic Dictionary with string keys
        if (metadata is System.Collections.IDictionary genericDict)
        {
            var result = new Dictionary<string, object>();
            foreach (System.Collections.DictionaryEntry entry in genericDict)
            {
                if (entry.Key is string key && entry.Value != null)
                {
                    result[key] = entry.Value;
                }
            }
            return result;
        }

        return null;
    }

    /// <summary>
    /// Converts a JsonElement to its appropriate .NET type.
    /// </summary>
    private static object ConvertJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            // GetString() returns string? but cannot return null when ValueKind is String;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            System.Text.Json.JsonValueKind.String => element.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => string.Empty, // Use empty string for null to avoid null reference issues
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString()
        };
    }

    #endregion

    #region Error Event Publishing

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    private async Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        await _messageBus.TryPublishErrorAsync(
            serviceName: "accounts",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
    }

    #endregion
}

/// <summary>
/// Account data model for lib-state storage.
/// Replaces Entity Framework entities.
/// </summary>
public class AccountModel
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PasswordHash { get; set; } // BCrypt hashed password for authentication
    public bool IsVerified { get; set; }
    public List<string> Roles { get; set; } = new List<string>(); // User roles (admin, user, etc.)
    public Dictionary<string, object>? Metadata { get; set; } // Custom metadata for the account

    // Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues
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
