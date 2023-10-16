using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Service responsible for account handling.
/// </summary>
public interface IAccountService : BeyondImmersion.BannouService.Services.IDaprService
{
    Task<AccountData?> GetAccount(string email);

    public static string GenerateHashedSecret(string secretString, string secretSalt)
    {
        var hashAlgo = SHA512.Create();
        var hashedBytes = hashAlgo.ComputeHash(Encoding.UTF8.GetBytes(secretString + secretSalt));
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < hashedBytes.Length; i++)
        {
            builder.Append(hashedBytes[i].ToString("x2"));
        }
        var hashedSecret = builder.ToString();
        return hashedSecret;
    }
}
