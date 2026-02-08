using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Account;

/// <summary>
/// State-backed implementation for Account service following schema-first architecture.
/// Uses IStateStoreFactory for persistence.
/// </summary>
[BannouService("account", typeof(IAccountService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class AccountService : IAccountService
{
    private readonly ILogger<AccountService> _logger;
    private readonly AccountServiceConfiguration _configuration;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;

    private const string ACCOUNT_KEY_PREFIX = "account-";
    private const string EMAIL_INDEX_KEY_PREFIX = "email-index-";
    private const string PROVIDER_INDEX_KEY_PREFIX = "provider-index-"; // provider:externalId -> accountId
    private const string AUTH_METHODS_KEY_PREFIX = "auth-methods-"; // accountId -> List<AuthMethodInfo>
    private const string ACCOUNT_CREATED_TOPIC = "account.created";
    private const string ACCOUNT_UPDATED_TOPIC = "account.updated";
    private const string ACCOUNT_DELETED_TOPIC = "account.deleted";

    public AccountService(
        ILogger<AccountService> logger,
        AccountServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _lockProvider = lockProvider;
    }

    /// <inheritdoc/>
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
            if (pageSize <= 0) pageSize = _configuration.DefaultPageSize;
            if (pageSize > _configuration.MaxPageSize) pageSize = _configuration.MaxPageSize;

            _logger.LogInformation("Listing accounts - Page: {Page}, PageSize: {PageSize}, ProviderFilter: {ProviderFilter}",
                page, pageSize, providerFilter.HasValue);

            var offset = (page - 1) * pageSize;

            // Build query conditions for MySQL JSON queries on account records
            var conditions = BuildAccountQueryConditions(emailFilter, displayNameFilter, verifiedFilter);

            // Provider filter requires in-memory filtering because auth methods are stored
            // in separate keys (auth-methods-{id}), not in the account record itself
            if (providerFilter.HasValue)
            {
                return await ListAccountsWithProviderFilterAsync(
                    conditions, providerFilter.Value, page, pageSize, cancellationToken);
            }

            // No provider filter: fully server-side via MySQL JSON queries
            var jsonStore = _stateStoreFactory.GetJsonQueryableStore<AccountModel>(StateStoreDefinitions.Account);

            var sortSpec = new JsonSortSpec
            {
                Path = "$.CreatedAtUnix",
                Descending = true
            };

            var result = await jsonStore.JsonQueryPagedAsync(
                conditions,
                offset,
                pageSize,
                sortSpec,
                cancellationToken);

            // Map results to response models with auth methods
            var accounts = new List<AccountResponse>();
            foreach (var item in result.Items)
            {
                var authMethods = await GetAuthMethodsForAccountAsync(
                    item.Value.AccountId.ToString(), cancellationToken);

                accounts.Add(new AccountResponse
                {
                    AccountId = item.Value.AccountId,
                    Email = item.Value.Email,
                    DisplayName = item.Value.DisplayName,
                    EmailVerified = item.Value.IsVerified,
                    CreatedAt = item.Value.CreatedAt,
                    UpdatedAt = item.Value.UpdatedAt,
                    Roles = item.Value.Roles,
                    AuthMethods = authMethods
                });
            }

            var response = new AccountListResponse
            {
                Accounts = accounts,
                TotalCount = (int)result.TotalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = (page * pageSize) < result.TotalCount,
                HasPreviousPage = page > 1
            };

            _logger.LogInformation("Returning {Count} accounts (Total: {Total})", accounts.Count, result.TotalCount);
            return (StatusCodes.OK, response);
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
    /// Builds query conditions for filtering account records in the MySQL state store.
    /// Uses AccountId exists as a type discriminator to match only account records
    /// (the store also contains email-index, provider-index, and auth-methods entries).
    /// </summary>
    private static List<QueryCondition> BuildAccountQueryConditions(
        string? emailFilter,
        string? displayNameFilter,
        bool? verifiedFilter)
    {
        var conditions = new List<QueryCondition>
        {
            // Type discriminator: only account records have AccountId in their JSON
            new QueryCondition { Path = "$.AccountId", Operator = QueryOperator.Exists, Value = true },
            // Exclude soft-deleted accounts
            new QueryCondition { Path = "$.DeletedAtUnix", Operator = QueryOperator.NotExists, Value = true }
        };

        if (!string.IsNullOrWhiteSpace(emailFilter))
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Email",
                Operator = QueryOperator.Contains,
                Value = emailFilter
            });
        }

        if (!string.IsNullOrWhiteSpace(displayNameFilter))
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.DisplayName",
                Operator = QueryOperator.Contains,
                Value = displayNameFilter
            });
        }

        if (verifiedFilter.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.IsVerified",
                Operator = QueryOperator.Equals,
                Value = verifiedFilter.Value
            });
        }

        return conditions;
    }

    /// <summary>
    /// Lists accounts with provider filter applied in-memory.
    /// Auth methods are stored in separate state keys so provider filtering
    /// cannot be done in a single JSON query. This is a rare admin operation.
    /// </summary>
    private async Task<(StatusCodes, AccountListResponse?)> ListAccountsWithProviderFilterAsync(
        List<QueryCondition> conditions,
        AuthProvider providerFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var jsonStore = _stateStoreFactory.GetJsonQueryableStore<AccountModel>(StateStoreDefinitions.Account);

        // Use paged query with configurable scan limit (admin-only endpoint)
        var scanLimit = _configuration.ProviderFilterMaxScanSize;
        var scanResult = await jsonStore.JsonQueryPagedAsync(
            conditions,
            offset: 0,
            limit: scanLimit,
            cancellationToken: cancellationToken);

        if (scanResult.TotalCount > scanLimit)
        {
            _logger.LogWarning("Provider filter scan capped at {ScanLimit}, total matching accounts: {TotalCount}",
                scanLimit, scanResult.TotalCount);
        }

        var filteredAccounts = new List<AccountResponse>();
        var batchSize = _configuration.ListBatchSize;
        var allResults = scanResult.Items.ToList();

        for (var i = 0; i < allResults.Count; i += batchSize)
        {
            var batch = allResults.Skip(i).Take(batchSize).ToList();

            // Load auth methods in parallel within each batch
            var batchTasks = batch.Select(async item =>
            {
                var authMethods = await GetAuthMethodsForAccountAsync(
                    item.Value.AccountId.ToString(), cancellationToken);
                return (item.Value, authMethods);
            });

            var batchResults = await Task.WhenAll(batchTasks);

            foreach (var (account, authMethods) in batchResults)
            {
                // Check if any auth method matches the provider filter
                if (authMethods.Any(m => m.Provider == providerFilter))
                {
                    filteredAccounts.Add(new AccountResponse
                    {
                        AccountId = account.AccountId,
                        Email = account.Email,
                        DisplayName = account.DisplayName,
                        EmailVerified = account.IsVerified,
                        CreatedAt = account.CreatedAt,
                        UpdatedAt = account.UpdatedAt,
                        Roles = account.Roles,
                        AuthMethods = authMethods
                    });
                }
            }
        }

        // Sort by creation time descending (newest first) and paginate in-memory
        filteredAccounts.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));

        var offset = (page - 1) * pageSize;
        var pagedAccounts = filteredAccounts.Skip(offset).Take(pageSize).ToList();

        var response = new AccountListResponse
        {
            Accounts = pagedAccounts,
            TotalCount = filteredAccounts.Count,
            Page = page,
            PageSize = pageSize,
            HasNextPage = (page * pageSize) < filteredAccounts.Count,
            HasPreviousPage = page > 1
        };

        _logger.LogInformation("Returning {Count} accounts (Total: {Total}, provider-filtered)",
            pagedAccounts.Count, filteredAccounts.Count);
        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AccountResponse?)> CreateAccountAsync(
        CreateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        ILockResponse? emailLock = null;
        try
        {
            _logger.LogInformation("Creating account for email: {Email}", body.Email ?? "(no email - OAuth/Steam)");

            // Check if email already exists (only if email provided)
            // Uses distributed lock to prevent TOCTOU race on concurrent registrations
            var emailIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
            if (!string.IsNullOrEmpty(body.Email))
            {
                var normalizedEmail = body.Email.ToLowerInvariant();
                var lockOwner = $"create-account-{Guid.NewGuid():N}";
                emailLock = await _lockProvider.LockAsync(
                    StateStoreDefinitions.AccountLock,
                    $"account-email:{normalizedEmail}",
                    lockOwner,
                    _configuration.CreateLockExpirySeconds,
                    cancellationToken);

                if (!emailLock.Success)
                {
                    _logger.LogWarning("Failed to acquire email lock for {Email}", body.Email);
                    await emailLock.DisposeAsync();
                    return (StatusCodes.Conflict, null);
                }

                var existingAccountId = await emailIndexStore.GetAsync(
                    $"{EMAIL_INDEX_KEY_PREFIX}{normalizedEmail}",
                    cancellationToken);

                if (!string.IsNullOrEmpty(existingAccountId))
                {
                    _logger.LogWarning("Account with email {Email} already exists (AccountId: {AccountId})", body.Email, existingAccountId);
                    await emailLock.DisposeAsync();
                    return (StatusCodes.Conflict, null);
                }
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
                AccountId = accountId,
                Email = body.Email,
                DisplayName = body.DisplayName,
                PasswordHash = body.PasswordHash, // Store pre-hashed password from Auth service
                IsVerified = body.EmailVerified == true,
                Roles = roles, // Store roles in account model
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Store in state store
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            await accountStore.SaveAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", account);

            // Create email index for quick lookup (only if email provided)
            if (!string.IsNullOrEmpty(body.Email))
            {
                await emailIndexStore.SaveAsync(
                    $"{EMAIL_INDEX_KEY_PREFIX}{body.Email.ToLowerInvariant()}",
                    accountId.ToString());
            }

            // Release email uniqueness lock now that the index is written
            if (emailLock != null)
            {
                await emailLock.DisposeAsync();
                emailLock = null; // Prevent double-dispose in catch
            }

            _logger.LogInformation("Account created: {AccountId} for email: {Email} with roles: {Roles}",
                accountId, body.Email ?? "(no email - OAuth/Steam)", string.Join(", ", roles));

            // Publish account created event
            await PublishAccountCreatedEventAsync(account, cancellationToken);

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
            // Release email lock on failure to prevent lock leaks
            if (emailLock != null)
            {
                await emailLock.DisposeAsync();
            }

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
    private bool ShouldAssignAdminRole(string? email)
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

    /// <inheritdoc/>
    public async Task<(StatusCodes, AccountResponse?)> GetAccountAsync(
        GetAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Retrieving account: {AccountId}", accountId);

            // Get from lib-state store (replaces Entity Framework query)
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);

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
                AccountId = account.AccountId,
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

    /// <inheritdoc/>
    public async Task<(StatusCodes, AccountResponse?)> UpdateAccountAsync(
        UpdateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating account: {AccountId}", accountId);

            // Get existing account with ETag for optimistic concurrency
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var accountKey = $"{ACCOUNT_KEY_PREFIX}{accountId}";
            var (account, etag) = await accountStore.GetWithETagAsync(accountKey, cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
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

                // Apply anonymous role auto-management if configured (per IMPLEMENTATION TENETS)
                if (_configuration.AutoManageAnonymousRole)
                {
                    if (newRoles.Any(r => r != "anonymous"))
                    {
                        newRoles.Remove("anonymous");
                    }

                    if (newRoles.Count == 0)
                    {
                        newRoles.Add("anonymous");
                        _logger.LogDebug("Auto-added 'anonymous' role to account {AccountId} to prevent zero roles", body.AccountId);
                    }
                }

                if (!new HashSet<string>(account.Roles).SetEquals(newRoles))
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

            // Save updated account with optimistic concurrency check
            var newEtag = await accountStore.TrySaveAsync(accountKey, account, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for account {AccountId}", accountId);
                return (StatusCodes.Conflict, null);
            }

            _logger.LogInformation("Account updated: {AccountId}", accountId);

            // Publish account updated event if there were changes
            if (changedFields.Count > 0)
            {
                await PublishAccountUpdatedEventAsync(account, changedFields, cancellationToken);
            }

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);

            var response = new AccountResponse
            {
                AccountId = account.AccountId,
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

    /// <inheritdoc/>
    public async Task<(StatusCodes, AccountResponse?)> GetAccountByEmailAsync(
        GetAccountByEmailRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var email = body.Email;
            _logger.LogInformation("Retrieving account by email: {Email}", email);

            // Get the account ID from email index
            var emailIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
            var accountId = await emailIndexStore.GetAsync(
                $"{EMAIL_INDEX_KEY_PREFIX}{email.ToLowerInvariant()}",
                cancellationToken);

            if (string.IsNullOrEmpty(accountId))
            {
                _logger.LogWarning("No account found for email: {Email}", email);
                return (StatusCodes.NotFound, null);
            }

            // Get the full account data
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);

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
                AccountId = account.AccountId,
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

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthMethodsResponse?)> GetAuthMethodsAsync(
        GetAuthMethodsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Getting auth methods for account: {AccountId}", accountId);

            // Verify account exists
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);

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

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthMethodResponse?)> AddAuthMethodAsync(
        AddAuthMethodRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Adding auth method for account: {AccountId}, provider: {Provider}", accountId, body.Provider);

            // Verify account exists
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get existing auth methods with ETag for optimistic concurrency
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(StateStoreDefinitions.Account);
            var (authMethods, authMethodsEtag) = await authMethodsStore.GetWithETagAsync(authMethodsKey, cancellationToken);
            authMethods ??= new List<AuthMethodInfo>();

            // Validate ExternalId - required for OAuth linking and provider index
            if (string.IsNullOrEmpty(body.ExternalId))
            {
                _logger.LogWarning("OAuth link attempt with empty ExternalId for account {AccountId}, provider {Provider}",
                    accountId, body.Provider);
                return (StatusCodes.BadRequest, null);
            }

            // Check if this provider is already linked on this account
            var mappedProvider = MapOAuthProviderToAuthProvider(body.Provider);
            var existingMethod = authMethods.FirstOrDefault(m =>
                m.Provider == mappedProvider && m.ExternalId == body.ExternalId);

            if (existingMethod != null)
            {
                return (StatusCodes.Conflict, null);
            }

            // Check if another account already owns this provider:externalId combination
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{body.Provider}:{body.ExternalId}";
            var providerIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
            var existingOwner = await providerIndexStore.GetAsync(providerIndexKey, cancellationToken);
            if (!string.IsNullOrEmpty(existingOwner) && existingOwner != accountId.ToString())
            {
                // Check if the owning account is still active (not soft-deleted)
                var ownerAccount = await accountStore.GetAsync(
                    $"{ACCOUNT_KEY_PREFIX}{existingOwner}", cancellationToken);
                if (ownerAccount != null && !ownerAccount.DeletedAt.HasValue)
                {
                    _logger.LogWarning("Provider {Provider}:{ExternalId} already linked to active account {ExistingOwner}",
                        body.Provider, body.ExternalId, existingOwner);
                    return (StatusCodes.Conflict, null);
                }
                // Owner deleted — orphaned index, safe to overwrite
                _logger.LogInformation("Overwriting orphaned provider index {Provider}:{ExternalId} (former owner {ExistingOwner} is deleted)",
                    body.Provider, body.ExternalId, existingOwner);
            }

            // Create new auth method
            var methodId = Guid.NewGuid();
            var linkedAt = DateTimeOffset.UtcNow;
            var newMethod = new AuthMethodInfo
            {
                MethodId = methodId,
                Provider = MapOAuthProviderToAuthProvider(body.Provider),
                ExternalId = body.ExternalId,
                LinkedAt = linkedAt
            };

            authMethods.Add(newMethod);

            // Save updated auth methods with optimistic concurrency
            var savedEtag = await authMethodsStore.TrySaveAsync(authMethodsKey, authMethods, authMethodsEtag ?? string.Empty, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogWarning("Concurrent modification of auth methods for account {AccountId}", accountId);
                return (StatusCodes.Conflict, null);
            }

            // Create/update provider index for lookup
            await providerIndexStore.SaveAsync(providerIndexKey, accountId.ToString());

            _logger.LogInformation("Auth method added for account: {AccountId}, methodId: {MethodId}, provider: {Provider}",
                accountId, methodId, body.Provider);

            // Publish account updated event
            await PublishAccountUpdatedEventAsync(account, new[] { "authMethods" }, cancellationToken);

            var response = new AuthMethodResponse
            {
                MethodId = methodId,
                Provider = body.Provider,
                ExternalId = body.ExternalId, // Already validated non-empty above
                LinkedAt = linkedAt
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
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown OAuth provider has no AuthProvider mapping")
        };
    }

    /// <inheritdoc/>
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
            var providerIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
            var accountId = await providerIndexStore.GetAsync(providerIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(accountId))
            {
                _logger.LogWarning("No account found for provider: {Provider}, externalId: {ExternalId}", provider, externalId);
                return (StatusCodes.NotFound, null);
            }

            // Get the full account data
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);

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
                AccountId = account.AccountId,
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
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(StateStoreDefinitions.Account);
            var authMethods = await authMethodsStore.GetAsync(authMethodsKey, cancellationToken);

            return authMethods ?? new List<AuthMethodInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get auth methods for account: {AccountId}", accountId);
            await PublishErrorEventAsync("GetAuthMethodsForAccount", ex.GetType().Name, ex.Message, dependency: "state", details: new { AccountId = accountId });
            throw; // Don't mask state store failures - empty list should mean "no auth methods", not "error"
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AccountResponse?)> UpdateProfileAsync(
        UpdateProfileRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating profile for account: {AccountId}", accountId);

            // Get existing account with ETag for optimistic concurrency
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var accountKey = $"{ACCOUNT_KEY_PREFIX}{accountId}";
            var (account, etag) = await accountStore.GetWithETagAsync(accountKey, cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                _logger.LogWarning("Account not found for profile update: {AccountId}", accountId);
                return (StatusCodes.NotFound, null);
            }

            // Track changed fields for event publication
            var changedFields = new List<string>();

            // Update profile fields
            if (body.DisplayName != null && body.DisplayName != account.DisplayName)
            {
                account.DisplayName = body.DisplayName;
                changedFields.Add("display_name");
            }

            // Handle metadata update if provided
            if (body.Metadata != null)
            {
                var newMetadata = ConvertToMetadataDictionary(body.Metadata);
                if (newMetadata != null)
                {
                    // If existing metadata is null or differs from new metadata, update
                    var hasChanged = account.Metadata == null || !MetadataEquals(account.Metadata, newMetadata);
                    if (hasChanged)
                    {
                        account.Metadata = newMetadata;
                        changedFields.Add("metadata");
                    }
                }
            }

            // If nothing changed, return early without saving or publishing
            if (changedFields.Count == 0)
            {
                var authMethodsNoChange = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);
                return (StatusCodes.OK, new AccountResponse
                {
                    AccountId = account.AccountId,
                    Email = account.Email,
                    DisplayName = account.DisplayName,
                    EmailVerified = account.IsVerified,
                    CreatedAt = account.CreatedAt,
                    UpdatedAt = account.UpdatedAt,
                    Roles = account.Roles,
                    AuthMethods = authMethodsNoChange
                });
            }

            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save with optimistic concurrency check
            var newEtag = await accountStore.TrySaveAsync(accountKey, account, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for account profile {AccountId}", accountId);
                return (StatusCodes.Conflict, null);
            }

            // Publish account updated event (T5: Event-Driven Architecture)
            await PublishAccountUpdatedEventAsync(account, changedFields, cancellationToken);

            // Get auth methods for the account
            var authMethods = await GetAuthMethodsForAccountAsync(accountId.ToString(), cancellationToken);

            var response = new AccountResponse
            {
                AccountId = account.AccountId,
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

    /// <inheritdoc/>
    public async Task<StatusCodes> DeleteAccountAsync(
        DeleteAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Deleting account: {AccountId}", accountId);

            // Get existing account with ETag for optimistic concurrency
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var accountKey = $"{ACCOUNT_KEY_PREFIX}{accountId}";
            var (account, etag) = await accountStore.GetWithETagAsync(accountKey, cancellationToken);

            if (account == null)
            {
                _logger.LogWarning("Account not found for deletion: {AccountId}", accountId);
                return StatusCodes.NotFound;
            }

            // Soft delete by setting DeletedAt timestamp
            account.DeletedAt = DateTimeOffset.UtcNow;

            // Save the soft-deleted account with optimistic concurrency check
            var newEtag = await accountStore.TrySaveAsync(accountKey, account, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for account deletion {AccountId}", accountId);
                return StatusCodes.Conflict;
            }

            // Remove email index (only if account has email)
            if (!string.IsNullOrEmpty(account.Email))
            {
                var emailIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
                await emailIndexStore.DeleteAsync($"{EMAIL_INDEX_KEY_PREFIX}{account.Email.ToLowerInvariant()}", cancellationToken);
            }

            // Remove provider index entries to prevent orphaned lookups
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(StateStoreDefinitions.Account);
            var authMethods = await authMethodsStore.GetAsync(authMethodsKey, cancellationToken);
            if (authMethods != null)
            {
                var providerIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
                foreach (var method in authMethods)
                {
                    var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{method.Provider}:{method.ExternalId}";
                    await providerIndexStore.DeleteAsync(providerIndexKey, cancellationToken);
                }
                await authMethodsStore.DeleteAsync(authMethodsKey, cancellationToken);
            }

            _logger.LogInformation("Account deleted: {AccountId}", accountId);

            // Publish account deleted event
            await PublishAccountDeletedEventAsync(account, "User requested deletion", cancellationToken);

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

    /// <inheritdoc/>
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
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                return StatusCodes.NotFound;
            }

            // Get existing auth methods with ETag for optimistic concurrency
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethodsStore = _stateStoreFactory.GetStore<List<AuthMethodInfo>>(StateStoreDefinitions.Account);
            var (authMethods, authEtag) = await authMethodsStore.GetWithETagAsync(authMethodsKey, cancellationToken);
            authMethods ??= new List<AuthMethodInfo>();

            // Find the auth method to remove
            var methodToRemove = authMethods.FirstOrDefault(m => m.MethodId == methodId);
            if (methodToRemove == null)
            {
                return StatusCodes.NotFound;
            }

            // Safety check: prevent orphaning the account (no way to authenticate)
            // Account can authenticate if it has: (1) a password, OR (2) at least one OAuth method
            var hasPassword = !string.IsNullOrEmpty(account.PasswordHash);
            var remainingAuthMethods = authMethods.Count - 1;

            if (!hasPassword && remainingAuthMethods == 0)
            {
                _logger.LogWarning(
                    "Rejecting auth method removal for account {AccountId}: would orphan account (no password, last OAuth method)",
                    accountId);
                return StatusCodes.BadRequest;
            }

            // Remove the auth method
            authMethods.Remove(methodToRemove);

            // Save updated auth methods with optimistic concurrency check
            var newAuthEtag = await authMethodsStore.TrySaveAsync(authMethodsKey, authMethods, authEtag ?? string.Empty, cancellationToken);
            if (newAuthEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for auth methods on account {AccountId}", accountId);
                return StatusCodes.Conflict;
            }

            // Remove provider index
            var providerIndexKey = $"{PROVIDER_INDEX_KEY_PREFIX}{methodToRemove.Provider}:{methodToRemove.ExternalId}";
            var providerIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Account);
            await providerIndexStore.DeleteAsync(providerIndexKey, cancellationToken);

            _logger.LogInformation("Auth method removed for account: {AccountId}, methodId: {MethodId}",
                accountId, methodId);

            await PublishAccountUpdatedEventAsync(account, new[] { "authMethods" }, cancellationToken);

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

    /// <inheritdoc/>
    public async Task<StatusCodes> UpdatePasswordHashAsync(
        UpdatePasswordRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating password hash for account: {AccountId}", accountId);

            // Get existing account with ETag for optimistic concurrency
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var accountKey = $"{ACCOUNT_KEY_PREFIX}{accountId}";
            var (account, etag) = await accountStore.GetWithETagAsync(accountKey, cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                _logger.LogWarning("Account not found for password update: {AccountId}", accountId);
                return StatusCodes.NotFound;
            }

            // Update password hash (should already be hashed by Auth service)
            account.PasswordHash = body.PasswordHash;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account with optimistic concurrency check
            var newEtag = await accountStore.TrySaveAsync(accountKey, account, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for password update on account {AccountId}", accountId);
                return StatusCodes.Conflict;
            }

            _logger.LogInformation("Password hash updated for account: {AccountId}", accountId);
            await PublishAccountUpdatedEventAsync(account, new[] { "passwordHash" }, cancellationToken);

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

    /// <inheritdoc/>
    public async Task<StatusCodes> UpdateVerificationStatusAsync(
        UpdateVerificationRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accountId = body.AccountId;
            _logger.LogInformation("Updating verification status for account: {AccountId}, Verified: {Verified}",
                accountId, body.EmailVerified);

            // Get existing account with ETag for optimistic concurrency
            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var accountKey = $"{ACCOUNT_KEY_PREFIX}{accountId}";
            var (account, etag) = await accountStore.GetWithETagAsync(accountKey, cancellationToken);

            if (account == null || account.DeletedAt.HasValue)
            {
                _logger.LogWarning("Account not found for verification update: {AccountId}", accountId);
                return StatusCodes.NotFound;
            }

            // Update verification status
            account.IsVerified = body.EmailVerified;
            account.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated account with optimistic concurrency check
            var newEtag = await accountStore.TrySaveAsync(accountKey, account, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for verification update on account {AccountId}", accountId);
                return StatusCodes.Conflict;
            }

            _logger.LogInformation("Verification status updated for account: {AccountId} -> {Verified}",
                accountId, body.EmailVerified);

            await PublishAccountUpdatedEventAsync(account, new[] { "isVerified" }, cancellationToken);
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
    private async Task PublishAccountCreatedEventAsync(AccountModel account, CancellationToken cancellationToken = default)
    {
        // Fetch auth methods to include in the event (events contain same data as Get*Response)
        var authMethods = await GetAuthMethodsForAccountAsync(account.AccountId.ToString(), cancellationToken);

        var eventModel = new AccountCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            Roles = account.Roles ?? [],
            AuthMethods = authMethods,
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
    private async Task PublishAccountUpdatedEventAsync(AccountModel account, IEnumerable<string> changedFields, CancellationToken cancellationToken = default)
    {
        // Fetch auth methods to include in the event (events contain same data as Get*Response)
        var authMethods = await GetAuthMethodsForAccountAsync(account.AccountId.ToString(), cancellationToken);

        var eventModel = new AccountUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            Roles = account.Roles ?? [],
            AuthMethods = authMethods,
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
    private async Task PublishAccountDeletedEventAsync(AccountModel account, string? deletedReason, CancellationToken cancellationToken = default)
    {
        // Fetch auth methods to include in the event (events contain same data as Get*Response)
        var authMethods = await GetAuthMethodsForAccountAsync(account.AccountId.ToString(), cancellationToken);

        var eventModel = new AccountDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.IsVerified,
            Roles = account.Roles ?? [],
            AuthMethods = authMethods,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.TryPublishAsync(ACCOUNT_DELETED_TOPIC, eventModel);
        _logger.LogDebug("Published AccountDeletedEvent for account {AccountId}", account.AccountId);
    }


    /// <inheritdoc/>
    public async Task<(StatusCodes, BatchGetAccountsResponse?)> BatchGetAccountsAsync(
        BatchGetAccountsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Batch getting {Count} accounts", body.AccountIds.Count);

            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var accounts = new List<AccountResponse>();
            var notFound = new List<Guid>();
            var failed = new List<BulkOperationFailure>();

            // Fetch all accounts in parallel with per-item error handling
            var accountIds = body.AccountIds.ToList();
            var fetchTasks = accountIds.Select(async accountId =>
            {
                try
                {
                    var account = await accountStore.GetAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", cancellationToken);
                    return (accountId, account, error: (string?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching account {AccountId}", accountId);
                    return (accountId, account: (AccountModel?)null, error: ex.Message);
                }
            });

            var results = await Task.WhenAll(fetchTasks);

            // Process results: separate found, not-found, and failed
            var foundAccounts = new List<(Guid Id, AccountModel Model)>();
            foreach (var (accountId, account, error) in results)
            {
                if (error != null)
                {
                    failed.Add(new BulkOperationFailure
                    {
                        AccountId = accountId,
                        Error = error
                    });
                }
                else if (account == null || account.DeletedAt.HasValue)
                {
                    notFound.Add(accountId);
                }
                else
                {
                    foundAccounts.Add((accountId, account));
                }
            }

            // Load auth methods in parallel for all found accounts with per-item error handling
            var authMethodTasks = foundAccounts.Select(async item =>
            {
                try
                {
                    var authMethods = await GetAuthMethodsForAccountAsync(item.Id.ToString(), cancellationToken);
                    return (item.Id, item.Model, authMethods, error: (string?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching auth methods for account {AccountId}", item.Id);
                    return (item.Id, item.Model, authMethods: (List<AuthMethodInfo>?)null, error: ex.Message);
                }
            });

            var authResults = await Task.WhenAll(authMethodTasks);

            foreach (var (accountId, account, authMethods, error) in authResults)
            {
                if (error != null)
                {
                    failed.Add(new BulkOperationFailure
                    {
                        AccountId = accountId,
                        Error = $"Auth methods fetch failed: {error}"
                    });
                }
                else
                {
                    accounts.Add(new AccountResponse
                    {
                        AccountId = account.AccountId,
                        Email = account.Email,
                        DisplayName = account.DisplayName,
                        EmailVerified = account.IsVerified,
                        CreatedAt = account.CreatedAt,
                        UpdatedAt = account.UpdatedAt,
                        Roles = account.Roles,
                        AuthMethods = authMethods ?? new List<AuthMethodInfo>()
                    });
                }
            }

            var response = new BatchGetAccountsResponse
            {
                Accounts = accounts,
                NotFound = notFound,
                Failed = failed
            };

            _logger.LogInformation("Batch get completed: {Found} found, {NotFound} not found, {Failed} failed",
                accounts.Count, notFound.Count, failed.Count);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch get accounts");
            await PublishErrorEventAsync(
                "BatchGetAccounts",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { Count = body.AccountIds.Count });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CountAccountsResponse?)> CountAccountsAsync(
        CountAccountsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Counting accounts with filters - Email: {Email}, DisplayName: {DisplayName}, Verified: {Verified}, Role: {Role}",
                body.Email != null, body.DisplayName != null, body.Verified, body.Role);

            var conditions = BuildAccountQueryConditions(body.Email, body.DisplayName, body.Verified);

            // Role filter uses JSON array containment (JSON_CONTAINS on $.Roles)
            if (!string.IsNullOrWhiteSpace(body.Role))
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.Roles",
                    Operator = QueryOperator.In,
                    Value = body.Role
                });
            }

            var jsonStore = _stateStoreFactory.GetJsonQueryableStore<AccountModel>(StateStoreDefinitions.Account);
            var count = await jsonStore.JsonCountAsync(conditions, cancellationToken);

            var response = new CountAccountsResponse
            {
                Count = count
            };

            _logger.LogInformation("Account count: {Count}", count);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting accounts");
            await PublishErrorEventAsync(
                "CountAccounts",
                "dependency_failure",
                ex.Message,
                dependency: "state");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BulkUpdateRolesResponse?)> BulkUpdateRolesAsync(
        BulkUpdateRolesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate that at least one operation is specified
            var hasAddRoles = body.AddRoles != null && body.AddRoles.Count > 0;
            var hasRemoveRoles = body.RemoveRoles != null && body.RemoveRoles.Count > 0;

            if (!hasAddRoles && !hasRemoveRoles)
            {
                _logger.LogWarning("Bulk update roles called with neither addRoles nor removeRoles");
                return (StatusCodes.BadRequest, null);
            }

            _logger.LogInformation("Bulk updating roles for {Count} accounts - AddRoles: {AddRoles}, RemoveRoles: {RemoveRoles}",
                body.AccountIds.Count,
                body.AddRoles != null ? string.Join(",", body.AddRoles) : "none",
                body.RemoveRoles != null ? string.Join(",", body.RemoveRoles) : "none");

            var accountStore = _stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
            var succeeded = new List<Guid>();
            var failed = new List<BulkOperationFailure>();

            // Process each account sequentially (ETag-based concurrency requires read-modify-write)
            foreach (var accountId in body.AccountIds)
            {
                try
                {
                    var accountKey = $"{ACCOUNT_KEY_PREFIX}{accountId}";
                    var (account, etag) = await accountStore.GetWithETagAsync(accountKey, cancellationToken);

                    if (account == null || account.DeletedAt.HasValue)
                    {
                        failed.Add(new BulkOperationFailure
                        {
                            AccountId = accountId,
                            Error = "Account not found"
                        });
                        continue;
                    }

                    // Compute new roles
                    var currentRoles = new HashSet<string>(account.Roles);
                    var originalRoles = new HashSet<string>(currentRoles);

                    if (hasAddRoles)
                    {
                        foreach (var role in body.AddRoles!)
                        {
                            currentRoles.Add(role);
                        }
                    }

                    if (hasRemoveRoles)
                    {
                        foreach (var role in body.RemoveRoles!)
                        {
                            currentRoles.Remove(role);
                        }
                    }

                    // Apply anonymous role auto-management if configured
                    if (_configuration.AutoManageAnonymousRole)
                    {
                        // If adding a non-anonymous role, remove "anonymous" if present
                        if (hasAddRoles && body.AddRoles!.Any(r => r != "anonymous"))
                        {
                            currentRoles.Remove("anonymous");
                        }

                        // If resulting roles would be empty, add "anonymous"
                        if (currentRoles.Count == 0)
                        {
                            currentRoles.Add("anonymous");
                            _logger.LogDebug("Auto-added 'anonymous' role to account {AccountId} to prevent zero roles", accountId);
                        }
                    }

                    // Check if roles actually changed
                    if (currentRoles.SetEquals(originalRoles))
                    {
                        // No-op is success
                        succeeded.Add(accountId);
                        continue;
                    }

                    // Update and save
                    account.Roles = currentRoles.ToList();
                    account.UpdatedAt = DateTimeOffset.UtcNow;

                    var newEtag = await accountStore.TrySaveAsync(accountKey, account, etag ?? string.Empty, cancellationToken);
                    if (newEtag == null)
                    {
                        failed.Add(new BulkOperationFailure
                        {
                            AccountId = accountId,
                            Error = "Concurrent modification"
                        });
                        continue;
                    }

                    succeeded.Add(accountId);

                    // Publish event for changed accounts
                    await PublishAccountUpdatedEventAsync(account, new[] { "roles" }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating roles for account {AccountId}", accountId);
                    failed.Add(new BulkOperationFailure
                    {
                        AccountId = accountId,
                        Error = ex.Message
                    });
                }
            }

            var response = new BulkUpdateRolesResponse
            {
                Succeeded = succeeded,
                Failed = failed
            };

            _logger.LogInformation("Bulk role update completed: {Succeeded} succeeded, {Failed} failed",
                succeeded.Count, failed.Count);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk update roles");
            await PublishErrorEventAsync(
                "BulkUpdateRoles",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { Count = body.AccountIds.Count });
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    /// <param name="appId">The application ID used to scope permission registrations.</param>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Account service permissions... (starting)");
        try
        {
            await AccountPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
            _logger.LogInformation("Account service permissions registered via event (complete)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Account service permissions");
            await PublishErrorEventAsync("RegisterServicePermissions", ex.GetType().Name, ex.Message, dependency: "permission");
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
            serviceName: "account",
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
    /// <summary>Unique identifier for the account.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Email address used for login and notifications. Null for OAuth/Steam accounts without email.</summary>
    public string? Email { get; set; }

    /// <summary>User-visible display name, optional.</summary>
    public string? DisplayName { get; set; }

    /// <summary>BCrypt hashed password for email/password authentication.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Whether the email address has been verified.</summary>
    public bool IsVerified { get; set; }

    /// <summary>User roles for permission checks (e.g., "admin", "user").</summary>
    public List<string> Roles { get; set; } = new List<string>();

    /// <summary>Custom metadata key-value pairs for the account.</summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Unix epoch timestamp for account creation.
    /// Stored as long to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long CreatedAtUnix { get; set; }

    /// <summary>
    /// Unix epoch timestamp for last account update.
    /// Stored as long to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long UpdatedAtUnix { get; set; }

    /// <summary>
    /// Unix epoch timestamp for soft-deletion, null if not deleted.
    /// Stored as long to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long? DeletedAtUnix { get; set; }

    /// <summary>Computed property for code convenience - not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset CreatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
        set => CreatedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>Computed property for code convenience - not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset UpdatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(UpdatedAtUnix);
        set => UpdatedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>Computed property for code convenience - not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset? DeletedAt
    {
        get => DeletedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(DeletedAtUnix.Value) : null;
        set => DeletedAtUnix = value?.ToUnixTimeSeconds();
    }
}
