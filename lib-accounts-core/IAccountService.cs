using BeyondImmersion.BannouService.Services;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service responsible for account handling.
/// </summary>
public interface IAccountService : IDaprService
{
    Task<AccountData?> GetAccount(string email);

    public static string GenerateHashedSecret(string secretString, string secretSalt)
    {
        var hashedBytes = SHA512.HashData(Encoding.UTF8.GetBytes(secretString + secretSalt));
        var builder = new StringBuilder();
        for (var i = 0; i < hashedBytes.Length; i++)
        {
            builder.Append(hashedBytes[i].ToString("x2"));
        }

        var hashedSecret = builder.ToString();
        return hashedSecret;
    }
}
