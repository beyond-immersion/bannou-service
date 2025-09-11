using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Interface for account management service operations.
/// Implements business logic for the generated AccountsController.
/// </summary>
public interface IAccountsService
{
    /// <summary>
    /// Lists accounts with optional filtering and pagination.
    /// </summary>
    Task<ActionResult<AccountListResponse>> ListAccountsAsync(
        string? email = null,
        string? displayName = null,
        string? provider = null,
        bool? verified = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user account.
    /// </summary>
    Task<ActionResult<AccountResponse>> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an account by ID.
    /// </summary>
    Task<ActionResult<AccountResponse>> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing account.
    /// </summary>
    Task<ActionResult<AccountResponse>> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an account.
    /// </summary>
    Task<IActionResult> DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an account by email address.
    /// </summary>
    Task<ActionResult<AccountResponse>> GetAccountByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets authentication methods for an account.
    /// </summary>
    Task<ActionResult<AuthMethodsResponse>> GetAuthMethodsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an authentication method to an account.
    /// </summary>
    Task<ActionResult<AuthMethodResponse>> AddAuthMethodAsync(
        Guid accountId,
        AddAuthMethodRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an authentication method from an account.
    /// </summary>
    Task<IActionResult> RemoveAuthMethodAsync(
        Guid accountId,
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user profile information.
    /// </summary>
    Task<ActionResult<ProfileResponse>> UpdateProfileAsync(
        Guid accountId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user profile information.
    /// </summary>
    Task<ActionResult<ProfileResponse>> GetProfileAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}