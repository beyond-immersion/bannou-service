using BeyondImmersion.BannouService.Accounts.Data;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Controllers.Generated;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service implementation for account management operations.
/// Provides CRUD operations and authentication method management for user accounts.
/// </summary>
[DaprService("accounts", typeof(IAccountsService), lifetime: ServiceLifetime.Scoped)]
public class AccountsService : DaprService<AccountsServiceConfiguration>, IAccountsService
{
    private readonly AccountsDbContext _dbContext;
    private readonly ILogger<AccountsService> _logger;

    public AccountsService(
        AccountsDbContext dbContext,
        AccountsServiceConfiguration configuration,
        ILogger<AccountsService> logger)
        : base(configuration, logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ActionResult<AccountListResponse>> ListAccountsAsync(
        string? email = null,
        string? displayName = null,
        string? provider = null,
        bool? verified = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate pagination parameters
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, Configuration.MaxPageSize);

            var query = _dbContext.Accounts.AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(email))
            {
                query = query.Where(a => a.Email.Contains(email));
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                query = query.Where(a => a.DisplayName != null && a.DisplayName.Contains(displayName));
            }

            if (verified.HasValue)
            {
                query = query.Where(a => a.EmailVerified == verified.Value);
            }

            if (!string.IsNullOrWhiteSpace(provider))
            {
                query = query.Where(a => a.AuthMethods.Any(am => am.Provider == provider));
            }

            // Get total count for pagination
            var totalCount = await query.CountAsync(cancellationToken);

            // Apply pagination
            var accounts = await query
                .Include(a => a.AuthMethods)
                .Include(a => a.AccountRoles)
                .OrderBy(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => MapToAccountResponse(a))
                .ToListAsync(cancellationToken);

            var response = new AccountListResponse
            {
                Accounts = accounts,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing accounts with filters: email={Email}, displayName={DisplayName}, provider={Provider}, verified={Verified}",
                email, displayName, provider, verified);
            return StatusCode(500, "An error occurred while retrieving accounts");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<AccountResponse>> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if account already exists
            var existingAccount = await _dbContext.Accounts
                .FirstOrDefaultAsync(a => a.Email == request.Email, cancellationToken);

            if (existingAccount != null)
            {
                return Conflict($"Account with email '{request.Email}' already exists");
            }

            // Create new account entity
            var account = new AccountEntity
            {
                Email = request.Email,
                PasswordHash = request.PasswordHash,
                DisplayName = request.DisplayName,
                EmailVerified = request.EmailVerified ?? false,
                Metadata = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null
            };

            _dbContext.Accounts.Add(account);

            // Add default roles
            var roles = request.Roles?.Any() == true ? request.Roles : Configuration.DefaultRoles;
            foreach (var role in roles)
            {
                account.AccountRoles.Add(new AccountRoleEntity
                {
                    AccountId = account.AccountId,
                    Role = role
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created new account {AccountId} with email {Email}", account.AccountId, account.Email);

            // Load the complete account for response
            var createdAccount = await _dbContext.Accounts
                .Include(a => a.AuthMethods)
                .Include(a => a.AccountRoles)
                .FirstOrDefaultAsync(a => a.AccountId == account.AccountId, cancellationToken);

            return CreatedAtAction(nameof(GetAccountAsync), new { accountId = account.AccountId }, MapToAccountResponse(createdAccount!));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating account with email {Email}", request.Email);
            return StatusCode(500, "An error occurred while creating the account");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<AccountResponse>> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .Include(a => a.AuthMethods)
                .Include(a => a.AccountRoles)
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            return MapToAccountResponse(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account {AccountId}", accountId);
            return StatusCode(500, "An error occurred while retrieving the account");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<AccountResponse>> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .Include(a => a.AccountRoles)
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            // Update basic properties
            if (request.DisplayName != null)
            {
                account.DisplayName = request.DisplayName;
            }

            if (request.Metadata != null)
            {
                account.Metadata = JsonSerializer.Serialize(request.Metadata);
            }

            // Update roles if provided
            if (request.Roles != null)
            {
                // Remove existing roles
                _dbContext.AccountRoles.RemoveRange(account.AccountRoles);
                account.AccountRoles.Clear();

                // Add new roles
                foreach (var role in request.Roles)
                {
                    account.AccountRoles.Add(new AccountRoleEntity
                    {
                        AccountId = accountId,
                        Role = role
                    });
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated account {AccountId}", accountId);

            // Reload account with all related data
            var updatedAccount = await _dbContext.Accounts
                .Include(a => a.AuthMethods)
                .Include(a => a.AccountRoles)
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            return MapToAccountResponse(updatedAccount!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating account {AccountId}", accountId);
            return StatusCode(500, "An error occurred while updating the account");
        }
    }

    /// <inheritdoc />
    public async Task<IActionResult> DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            if (Configuration.EnableSoftDelete)
            {
                // Soft delete
                account.DeletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Soft deleted account {AccountId}", accountId);
            }
            else
            {
                // Hard delete
                _dbContext.Accounts.Remove(account);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Hard deleted account {AccountId}", accountId);
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account {AccountId}", accountId);
            return StatusCode(500, "An error occurred while deleting the account");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<AccountResponse>> GetAccountByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .Include(a => a.AuthMethods)
                .Include(a => a.AccountRoles)
                .FirstOrDefaultAsync(a => a.Email == email, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account with email '{email}' not found");
            }

            return MapToAccountResponse(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving account by email {Email}", email);
            return StatusCode(500, "An error occurred while retrieving the account");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<AuthMethodsResponse>> GetAuthMethodsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .Include(a => a.AuthMethods)
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            var authMethods = account.AuthMethods.Select(am => new AuthMethodResponse
            {
                Provider = am.Provider,
                ProviderUserId = am.ProviderUserId,
                LinkedAt = am.LinkedAt
            }).ToList();

            return new AuthMethodsResponse { AuthMethods = authMethods };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving auth methods for account {AccountId}", accountId);
            return StatusCode(500, "An error occurred while retrieving authentication methods");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<AuthMethodResponse>> AddAuthMethodAsync(
        Guid accountId,
        AddAuthMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            // Check if auth method already exists
            var existingAuthMethod = await _dbContext.AuthMethods
                .FirstOrDefaultAsync(am => am.Provider == request.Provider && am.ProviderUserId == request.ProviderUserId, cancellationToken);

            if (existingAuthMethod != null)
            {
                return Conflict($"Authentication method for provider '{request.Provider}' with user ID '{request.ProviderUserId}' already exists");
            }

            var authMethod = new AuthMethodEntity
            {
                AccountId = accountId,
                Provider = request.Provider,
                ProviderUserId = request.ProviderUserId
            };

            _dbContext.AuthMethods.Add(authMethod);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added auth method {Provider} to account {AccountId}", request.Provider, accountId);

            return CreatedAtAction(nameof(GetAuthMethodsAsync), new { accountId }, new AuthMethodResponse
            {
                Provider = authMethod.Provider,
                ProviderUserId = authMethod.ProviderUserId,
                LinkedAt = authMethod.LinkedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding auth method {Provider} to account {AccountId}", request.Provider, accountId);
            return StatusCode(500, "An error occurred while adding the authentication method");
        }
    }

    /// <inheritdoc />
    public async Task<IActionResult> RemoveAuthMethodAsync(
        Guid accountId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authMethod = await _dbContext.AuthMethods
                .FirstOrDefaultAsync(am => am.AccountId == accountId && am.Provider == provider, cancellationToken);

            if (authMethod == null)
            {
                return NotFound($"Authentication method '{provider}' not found for account {accountId}");
            }

            _dbContext.AuthMethods.Remove(authMethod);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Removed auth method {Provider} from account {AccountId}", provider, accountId);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing auth method {Provider} from account {AccountId}", provider, accountId);
            return StatusCode(500, "An error occurred while removing the authentication method");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<ProfileResponse>> UpdateProfileAsync(
        Guid accountId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            // Update profile fields
            if (request.DisplayName != null)
            {
                account.DisplayName = request.DisplayName;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated profile for account {AccountId}", accountId);

            return new ProfileResponse
            {
                AccountId = account.AccountId,
                DisplayName = account.DisplayName,
                Email = account.Email,
                EmailVerified = account.EmailVerified,
                UpdatedAt = account.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile for account {AccountId}", accountId);
            return StatusCode(500, "An error occurred while updating the profile");
        }
    }

    /// <inheritdoc />
    public async Task<ActionResult<ProfileResponse>> GetProfileAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts
                .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound($"Account {accountId} not found");
            }

            return new ProfileResponse
            {
                AccountId = account.AccountId,
                DisplayName = account.DisplayName,
                Email = account.Email,
                EmailVerified = account.EmailVerified,
                UpdatedAt = account.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving profile for account {AccountId}", accountId);
            return StatusCode(500, "An error occurred while retrieving the profile");
        }
    }

    /// <summary>
    /// Maps an AccountEntity to an AccountResponse.
    /// </summary>
    private static AccountResponse MapToAccountResponse(AccountEntity account)
    {
        return new AccountResponse
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.EmailVerified,
            Roles = account.AccountRoles.Select(ar => ar.Role).ToList(),
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            Metadata = !string.IsNullOrEmpty(account.Metadata) 
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(account.Metadata) 
                : null
        };
    }

    /// <summary>
    /// Helper method to create ActionResult with proper status code.
    /// </summary>
    private ActionResult<T> StatusCode<T>(int statusCode, string message)
    {
        return new ObjectResult(new { error = message }) { StatusCode = statusCode };
    }

    /// <summary>
    /// Helper method to create ActionResult with proper status code.
    /// </summary>
    private IActionResult StatusCode(int statusCode, string message)
    {
        return new ObjectResult(new { error = message }) { StatusCode = statusCode };
    }
}