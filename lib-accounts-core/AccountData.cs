using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts;

[JsonObject]
public sealed class AccountData
{
    /// <summary>
    /// The unique account ID (GUID).
    /// </summary>
    [JsonProperty("id")]
    public string? ID { get; }

    /// <summary>
    /// The account email address.
    /// </summary>
    [JsonProperty("email")]
    public string? Email { get; }

    /// <summary>
    /// The hash of the user's secret.
    /// </summary>
    [JsonProperty("hashed_secret")]
    public string? HashedSecret { get; }

    /// <summary>
    /// The salt added to the user's secret before hashing.
    /// </summary>
    [JsonProperty("secret_salt")]
    public string? SecretSalt { get; }

    /// <summary>
    /// The account username.
    /// </summary>
    [JsonProperty("display_name")]
    public string? DisplayName { get; }

    /// <summary>
    /// The user's role claim.
    /// </summary>
    [JsonProperty("role")]
    public string? Role { get; }

    private AccountData() { }
    public AccountData(string id, string email, string hashedSecret, string secretSalt, string displayName, string role = "user")
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentNullException(nameof(id));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentNullException(nameof(email));

        if (string.IsNullOrWhiteSpace(hashedSecret))
            throw new ArgumentNullException(nameof(hashedSecret));

        if (string.IsNullOrWhiteSpace(secretSalt))
            throw new ArgumentNullException(nameof(secretSalt));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentNullException(nameof(role));

        ID = id;
        Email = email;
        HashedSecret = hashedSecret;
        SecretSalt = secretSalt;
        Role = role;

        if (!string.IsNullOrWhiteSpace(displayName))
            DisplayName = displayName;
    }
}
