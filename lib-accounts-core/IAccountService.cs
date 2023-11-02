using BeyondImmersion.BannouService.Services;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service responsible for account handling.
/// </summary>
public interface IAccountService : IDaprService
{
    public sealed class AccountData
    {
        public int ID { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? Region { get; set; }
        public bool EmailVerified { get; set; }
        public string SecurityToken { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public DateTime? LockoutEnd { get; set; }
        public DateTime LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? RemovedAt { get; set; }
        public HashSet<string>? RoleClaims { get; set; }
        public HashSet<string>? AppClaims { get; set; }
        public HashSet<string>? ScopeClaims { get; set; }
        public HashSet<string>? IdentityClaims { get; set; }
        public HashSet<string>? ProfileClaims { get; set; }

        public AccountData(int id, string securityToken, DateTime createdAt)
        {
            ID = id;
            SecurityToken = securityToken;
            LastLoginAt = createdAt;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }
    }

    Task<AccountData?> GetAccount(bool includeClaims = false, string? guid = null, string? username = null, string? email = null,
        string? steamID = null, string? googleID = null, string? identityClaim = null);

    Task<AccountData?> CreateAccount(string? email, bool emailVerified, bool twoFactorEnabled, string? region, string? username, string? password,
        string? steamID, string? steamToken, string? googleID, string? googleToken,
        HashSet<string>? roleClaims, HashSet<string>? appClaims, HashSet<string>? scopeClaims, HashSet<string>? identityClaims, HashSet<string>? profileClaims);

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
