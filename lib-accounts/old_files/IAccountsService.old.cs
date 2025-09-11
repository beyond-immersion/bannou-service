using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Accounts;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Interface for account management service operations.
/// Implements business logic for the generated AccountsController.
/// </summary>
public interface IAccountsService
{
    /// <summary>
    /// List accounts with filtering
    /// </summary>
    Task<ActionResult<AccountListResponse>> ListAccountsAsync(
        string? email,
        string? displayName,
        Provider? provider,
        bool? verified,
        int? page = 1,
        int? pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create new account
    /// </summary>
    Task<ActionResult<AccountResponse>> CreateAccountAsync(
        CreateAccountRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get account by ID
    /// </summary>
    Task<ActionResult<AccountResponse>> GetAccountAsync(
        System.Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing account
    /// </summary>
    Task<ActionResult<AccountResponse>> UpdateAccountAsync(
        System.Guid accountId,
        UpdateAccountRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete account
    /// </summary>
    Task<IActionResult> DeleteAccountAsync(
        System.Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get account by email address
    /// </summary>
    Task<ActionResult<AccountResponse>> GetAccountByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get authentication methods for account
    /// </summary>
    Task<ActionResult<AuthMethodsResponse>> GetAuthMethodsAsync(
        System.Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add authentication method to account
    /// </summary>
    Task<ActionResult<AuthMethodResponse>> AddAuthMethodAsync(
        System.Guid accountId,
        AddAuthMethodRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove authentication method from account
    /// </summary>
    Task<IActionResult> RemoveAuthMethodAsync(
        System.Guid accountId,
        System.Guid methodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get account by provider and external ID
    /// </summary>
    Task<ActionResult<AccountResponse>> GetAccountByProviderAsync(
        Provider2 provider,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update profile information
    /// </summary>
    Task<ActionResult<AccountResponse>> UpdateProfileAsync(
        System.Guid accountId,
        UpdateProfileRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update password hash
    /// </summary>
    Task<IActionResult> UpdatePasswordHashAsync(
        System.Guid accountId,
        UpdatePasswordRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update verification status
    /// </summary>
    Task<IActionResult> UpdateVerificationStatusAsync(
        System.Guid accountId,
        UpdateVerificationRequest body,
        CancellationToken cancellationToken = default);
}