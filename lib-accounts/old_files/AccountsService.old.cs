using BeyondImmersion.BannouService.Accounts.Data;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        ILogger<AccountsService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ActionResult<AccountListResponse>> ListAccountsAsync(
        string? email,
        string? displayName,
        Provider? provider,
        bool? verified,
        int? page = 1,
        int? pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.Accounts.AsQueryable();

            if (!string.IsNullOrEmpty(email))
                query = query.Where(a => a.Email.Contains(email));
            
            if (!string.IsNullOrEmpty(displayName))
                query = query.Where(a => a.DisplayName != null && a.DisplayName.Contains(displayName));

            var pageNumber = page ?? 1;
            var size = pageSize ?? 20;
            
            var total = await query.CountAsync(cancellationToken);
            var accounts = await query
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync(cancellationToken);

            var response = new AccountListResponse
            {
                Accounts = accounts.Select(MapAccountToResponse).ToList(),
                TotalCount = total,
                Page = pageNumber,
                PageSize = size
            };

            return new ActionResult<AccountListResponse>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing accounts");
            return new ObjectResult("Internal server error") { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<AccountResponse>> CreateAccountAsync(
        CreateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if account with email already exists
            if (await _dbContext.Accounts.AnyAsync(a => a.Email == body.Email, cancellationToken))
            {
                return new ObjectResult("Account with email already exists") { StatusCode = 409 };
            }

            var account = new AccountEntity
            {
                AccountId = Guid.NewGuid(),
                Email = body.Email,
                DisplayName = body.DisplayName,
                EmailVerified = body.EmailVerified,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Accounts.Add(account);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ActionResult<AccountResponse>(MapAccountToResponse(account));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating account");
            return new ObjectResult("Internal server error") { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<AccountResponse>> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts.FindAsync(new object[] { accountId }, cancellationToken);
            if (account == null)
            {
                return new ObjectResult("Account not found") { StatusCode = 404 };
            }

            return new ActionResult<AccountResponse>(MapAccountToResponse(account));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account {AccountId}", accountId);
            return new ObjectResult("Internal server error") { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<AccountResponse>> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts.FindAsync(new object[] { accountId }, cancellationToken);
            if (account == null)
            {
                return new ObjectResult("Account not found") { StatusCode = 404 };
            }

            if (!string.IsNullOrEmpty(body.DisplayName))
                account.DisplayName = body.DisplayName;
            
            account.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ActionResult<AccountResponse>(MapAccountToResponse(account));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating account {AccountId}", accountId);
            return new ObjectResult("Internal server error") { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts.FindAsync(new object[] { accountId }, cancellationToken);
            if (account == null)
            {
                return new ObjectResult("Account not found") { StatusCode = 404 };
            }

            _dbContext.Accounts.Remove(account);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new StatusCodeResult(204); // NoContent
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account {AccountId}", accountId);
            return new ObjectResult("Internal server error") { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<AccountResponse>> GetAccountByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var account = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
            if (account == null)
            {
                return new ObjectResult("Account not found") { StatusCode = 404 };
            }

            return new ActionResult<AccountResponse>(MapAccountToResponse(account));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account by email {Email}", email);
            return new ObjectResult("Internal server error") { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<AuthMethodsResponse>> GetAuthMethodsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement auth methods retrieval
        return new ActionResult<AuthMethodsResponse>(new AuthMethodsResponse
        {
            AuthMethods = new List<AuthMethodInfo>()
        });
    }

    public async Task<ActionResult<AuthMethodResponse>> AddAuthMethodAsync(
        Guid accountId,
        AddAuthMethodRequest body,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement auth method addition
        return new ObjectResult("Not implemented") { StatusCode = 501 };
    }

    public async Task<IActionResult> RemoveAuthMethodAsync(
        Guid accountId,
        Guid methodId,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement auth method removal
        return new ObjectResult("Not implemented") { StatusCode = 501 };
    }

    public async Task<ActionResult<AccountResponse>> GetAccountByProviderAsync(
        Provider2 provider,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement provider-based account lookup
        return new ObjectResult("Not implemented") { StatusCode = 501 };
    }

    public async Task<ActionResult<AccountResponse>> UpdateProfileAsync(
        Guid accountId,
        UpdateProfileRequest body,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement profile update
        return new ObjectResult("Not implemented") { StatusCode = 501 };
    }

    public async Task<IActionResult> UpdatePasswordHashAsync(
        Guid accountId,
        UpdatePasswordRequest body,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement password hash update
        return new ObjectResult("Not implemented") { StatusCode = 501 };
    }

    public async Task<IActionResult> UpdateVerificationStatusAsync(
        Guid accountId,
        UpdateVerificationRequest body,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement verification status update
        return new ObjectResult("Not implemented") { StatusCode = 501 };
    }

    private static AccountResponse MapAccountToResponse(AccountEntity account)
    {
        return new AccountResponse
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            EmailVerified = account.EmailVerified,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            Roles = new List<string>(),
            AuthMethods = new List<AuthMethodInfo>()
        };
    }
}