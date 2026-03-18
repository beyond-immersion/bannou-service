using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Account;

// =============================================================================
// AccountService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by AccountService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (AccountService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IAccountService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (AccountService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for AccountService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class AccountService
{
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
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.ListAccountsWithProviderFilterAsync");

        // Use paged query with configurable scan limit (admin-only endpoint)
        var scanLimit = _configuration.ProviderFilterMaxScanSize;
        var scanResult = await _queryableStore.JsonQueryPagedAsync(
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
                        MfaEnabled = account.MfaEnabled,
                        MfaSecret = account.MfaSecret,
                        MfaRecoveryCodes = account.MfaRecoveryCodes,
                        AuthMethods = authMethods
                    });
                }
            }
        }

        // Sort by creation time descending (newest first) and paginate in-memory
        filteredAccounts.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));

        var paginationResult = PaginationHelper.Paginate(filteredAccounts, page, pageSize);

        var response = new AccountListResponse
        {
            Accounts = paginationResult.Items.ToList(),
            TotalCount = paginationResult.TotalCount,
            Page = paginationResult.Page,
            PageSize = paginationResult.PageSize,
            HasNextPage = paginationResult.HasNextPage,
            HasPreviousPage = paginationResult.HasPreviousPage
        };

        _logger.LogInformation("Returning {Count} accounts (Total: {Total}, provider-filtered)",
            paginationResult.Items.Count, paginationResult.TotalCount);
        return (StatusCodes.OK, response);
    }


    /// <summary>
    /// Core account creation logic shared by both locked (email) and unlocked (OAuth/Steam) paths.
    /// </summary>
    private async Task<(StatusCodes, AccountResponse?)> CreateAccountCoreAsync(
        CreateAccountRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.CreateAccountCoreAsync");

        // Create account entity
        var accountId = Guid.NewGuid();

        // Determine roles - start with roles from request body, default to "user" role
        var roles = body.Roles?.ToList() ?? new List<string>();

        // All registered accounts get the "user" role by default if no roles specified
        // This ensures they have basic authenticated access to APIs
        if (roles.Count == 0)
        {
            roles.Add("user");
            _logger.LogDebug("Assigning default 'user' role to new account");
        }

        // Apply ENV-based admin role assignment
        if (ShouldAssignAdminRole(body.Email))
        {
            if (!roles.Contains("admin"))
            {
                roles.Add("admin");
                _logger.LogInformation("Auto-assigning admin role to new account based on configuration");
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
        await _accountStore.SaveAsync($"{ACCOUNT_KEY_PREFIX}{accountId}", account);

        // Create email index for quick lookup (only if email provided)
        if (!string.IsNullOrEmpty(body.Email))
        {
            await _indexStore.SaveAsync(
                $"{EMAIL_INDEX_KEY_PREFIX}{body.Email.ToLowerInvariant()}",
                accountId.ToString());
        }

        _logger.LogInformation("Account created: {AccountId} with roles: {Roles}",
            accountId, string.Join(", ", roles));

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
            MfaEnabled = account.MfaEnabled,
            MfaSecret = account.MfaSecret,
            MfaRecoveryCodes = account.MfaRecoveryCodes,
            AuthMethods = new List<AuthMethodInfo>()
        };

        return (StatusCodes.OK, response);
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
                _logger.LogDebug("Email matches AdminEmails configuration");
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
                _logger.LogDebug("Email matches AdminEmailDomain configuration");
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// Helper method to get auth methods for an account.
    /// </summary>
    private async Task<List<AuthMethodInfo>> GetAuthMethodsForAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.GetAuthMethodsForAccountAsync");

        try
        {
            var authMethodsKey = $"{AUTH_METHODS_KEY_PREFIX}{accountId}";
            var authMethods = await _authMethodStore.GetAsync(authMethodsKey, cancellationToken);

            return authMethods ?? new List<AuthMethodInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get auth methods for account: {AccountId}", accountId);
            await PublishErrorEventAsync("GetAuthMethodsForAccount", ex.GetType().Name, ex.Message, dependency: "state", details: new { AccountId = accountId });
            throw; // Don't mask state store failures - empty list should mean "no auth methods", not "error"
        }
    }

    /// <summary>
    /// Publish AccountCreatedEvent to RabbitMQ via IMessageBus.
    /// TryPublishAsync handles buffering, retry, and error logging internally.
    /// </summary>
    private async Task PublishAccountCreatedEventAsync(AccountModel account, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.PublishAccountCreatedEventAsync");

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

        await _messageBus.PublishAccountCreatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published AccountCreatedEvent for account: {AccountId}", account.AccountId);
    }

    /// <summary>
    /// Publish AccountUpdatedEvent to RabbitMQ via IMessageBus.
    /// Event contains the current state of the account plus which fields changed.
    /// TryPublishAsync handles buffering, retry, and error logging internally.
    /// </summary>
    private async Task PublishAccountUpdatedEventAsync(AccountModel account, IEnumerable<string> changedFields, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.PublishAccountUpdatedEventAsync");

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

        await _messageBus.PublishAccountUpdatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published AccountUpdatedEvent for account: {AccountId}", account.AccountId);
    }

    /// <summary>
    /// Publish AccountDeletedEvent to RabbitMQ via IMessageBus.
    /// Event contains the final state of the account before deletion.
    /// TryPublishAsync handles buffering, retry, and error logging internally.
    /// </summary>
    private async Task PublishAccountDeletedEventAsync(AccountModel account, string? deletedReason, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.PublishAccountDeletedEventAsync");

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

        await _messageBus.PublishAccountDeletedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published AccountDeletedEvent for account {AccountId}", account.AccountId);
    }


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
                var value = ConvertJsonElement(property.Value);
                if (value != null)
                    result[property.Name] = value;
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
    private static object? ConvertJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            // GetString() returns string? but cannot return null when ValueKind is String;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            System.Text.Json.JsonValueKind.String => element.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,  // Null values excluded from metadata dictionary by caller
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
        using var activity = _telemetryProvider.StartActivity("bannou.account", "AccountService.PublishErrorEventAsync");

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
