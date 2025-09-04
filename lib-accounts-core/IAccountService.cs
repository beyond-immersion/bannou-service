using BeyondImmersion.BannouService.Services;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service responsible for account handling.
/// </summary>
public interface IAccountService : IDaprService
{
    /// <summary>
    /// Retrieve a user account by any identifier.
    /// </summary>
    /// <returns>
    ///     <see cref="StatusCodes.OK"/>
    ///     <see cref="StatusCodes.BadRequest"/>
    ///     <see cref="StatusCodes.NotFound"/>
    ///     <see cref="StatusCodes.InternalServerError"/>
    /// </returns>
    Task<ServiceResponse<AccountData?>> GetAccount(bool includeClaims = false, int? id = null, string? username = null, string? email = null,
        string? steamID = null, string? googleID = null, string? identityClaim = null);

    /// <summary>
    /// Create a new user account.
    /// </summary>
    /// <returns>
    ///     <see cref="StatusCodes.OK"/>,
    ///     <see cref="StatusCodes.BadRequest"/>,
    ///     <see cref="StatusCodes.Conflict"/>,
    ///     <see cref="StatusCodes.InternalServerError"/>
    /// </returns>
    Task<ServiceResponse<AccountData?>> CreateAccount(string? email, bool emailVerified, bool twoFactorEnabled, string? region,
        string? username, string? password, string? steamID, string? steamToken, string? googleID, string? googleToken,
        HashSet<string>? roleClaims, HashSet<string>? appClaims, HashSet<string>? scopeClaims, HashSet<string>? identityClaims, HashSet<string>? profileClaims);

    /// <summary>
    /// Update an existing user account.
    /// </summary>
    /// <returns>
    ///     <see cref="StatusCodes.OK"/>,
    ///     <see cref="StatusCodes.BadRequest"/>,
    ///     <see cref="StatusCodes.NotFound"/>,
    ///     <see cref="StatusCodes.Conflict"/>,
    ///     <see cref="StatusCodes.InternalServerError"/>
    /// </returns>
    Task<ServiceResponse<AccountData?>> UpdateAccount(int id, string? email, bool? emailVerified, bool? twoFactorEnabled, string? region,
        string? username, string? password, string? steamID, string? steamToken, string? googleID, string? googleToken,
        Dictionary<string, string?>? roleClaims, Dictionary<string, string?>? appClaims, Dictionary<string, string?>? scopeClaims,
        Dictionary<string, string?>? identityClaims, Dictionary<string, string?>? profileClaims);

    /// <summary>
    /// Delete a user account.
    /// 
    /// Soft-delete, so record still exists- check DeleteAt
    /// for deletion status. Deleted user accounts will be
    /// permanently cleaned up on demand or at intervals.
    /// </summary>
    /// <returns>
    ///     <see cref="StatusCodes.OK"/>,
    ///     <see cref="StatusCodes.BadRequest"/>,
    ///     <see cref="StatusCodes.NotFound"/>,
    ///     <see cref="StatusCodes.Conflict"/>,
    ///     <see cref="StatusCodes.InternalServerError"/>
    /// </returns>
    Task<ServiceResponse<DateTime?>> DeleteAccount(int id);

    public static string GenerateHashedSecret(string secretString, string secretSalt)
    {
        var hashedBytes = SHA512.HashData(Encoding.UTF8.GetBytes(secretString + secretSalt));
        var builder = new StringBuilder();
        for (var i = 0; i < hashedBytes.Length; i++)
            builder.Append(hashedBytes[i].ToString("x2"));

        var hashedSecret = builder.ToString();
        return hashedSecret;
    }
}
