using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeyondImmersion.BannouService.Accounts.Data;

/// <summary>
/// Entity model for user accounts in the database.
/// </summary>
[Table("accounts")]
public class AccountEntity
{
    /// <summary>
    /// Unique account identifier.
    /// </summary>
    [Key]
    [Column("account_id")]
    public Guid AccountId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Primary email address (unique).
    /// </summary>
    [Required]
    [MaxLength(255)]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Pre-hashed password from Auth service (nullable for OAuth-only accounts).
    /// </summary>
    [MaxLength(255)]
    [Column("password_hash")]
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Display name for the user.
    /// </summary>
    [MaxLength(100)]
    [Column("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether the email address has been verified.
    /// </summary>
    [Column("email_verified")]
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// Account creation timestamp.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft delete timestamp (null if not deleted).
    /// </summary>
    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// JSON metadata for additional account information.
    /// </summary>
    [Column("metadata", TypeName = "json")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Navigation property for authentication methods.
    /// </summary>
    public virtual ICollection<AuthMethodEntity> AuthMethods { get; set; } = [];

    /// <summary>
    /// Navigation property for account roles.
    /// </summary>
    public virtual ICollection<AccountRoleEntity> AccountRoles { get; set; } = [];
}

/// <summary>
/// Entity model for authentication methods linked to accounts.
/// </summary>
[Table("auth_methods")]
public class AuthMethodEntity
{
    /// <summary>
    /// Unique identifier for this auth method.
    /// </summary>
    [Key]
    [Column("auth_method_id")]
    public Guid AuthMethodId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Account this auth method belongs to.
    /// </summary>
    [ForeignKey(nameof(Account))]
    [Column("account_id")]
    public Guid AccountId { get; set; }

    /// <summary>
    /// Authentication provider (email, google, discord, etc.).
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// External provider user ID.
    /// </summary>
    [Required]
    [MaxLength(255)]
    [Column("provider_user_id")]
    public string ProviderUserId { get; set; } = string.Empty;

    /// <summary>
    /// When this auth method was first linked.
    /// </summary>
    [Column("linked_at")]
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property to the parent account.
    /// </summary>
    public virtual AccountEntity Account { get; set; } = null!;
}

/// <summary>
/// Entity model for account roles (many-to-many relationship).
/// </summary>
[Table("account_roles")]
public class AccountRoleEntity
{
    /// <summary>
    /// Account ID.
    /// </summary>
    [ForeignKey(nameof(Account))]
    [Column("account_id")]
    public Guid AccountId { get; set; }

    /// <summary>
    /// Role name.
    /// </summary>
    [MaxLength(50)]
    [Column("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// When this role was assigned.
    /// </summary>
    [Column("assigned_at")]
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property to the parent account.
    /// </summary>
    public virtual AccountEntity Account { get; set; } = null!;
}