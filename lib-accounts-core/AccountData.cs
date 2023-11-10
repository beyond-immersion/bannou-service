namespace BeyondImmersion.BannouService.Accounts;

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
    public DateTime? DeletedAt { get; set; }
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
