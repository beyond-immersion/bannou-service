using Newtonsoft.Json;
using System.Text.Json.Serialization;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public sealed class AccountData
{
    /// <summary>
    /// The unique account ID (GUID).
    /// </summary>
    [JsonPropertyName("id")]
    [JsonProperty("id", Required = Required.Always)]
    public string? ID { get; }

    /// <summary>
    /// The account email address.
    /// </summary>
    [JsonPropertyName("email")]
    [JsonProperty("email", Required = Required.Always)]
    public string? Email { get; }

    /// <summary>
    /// The hash of the user's secret.
    /// </summary>
    [JsonPropertyName("hashed_secret")]
    [JsonProperty("hashed_secret", Required = Required.Always)]
    public string? HashedSecret { get; }

    /// <summary>
    /// The salt added to the user's secret before hashing.
    /// </summary>
    [JsonPropertyName("secret_salt")]
    [JsonProperty("secret_salt", Required = Required.Always)]
    public string? SecretSalt { get; }

    /// <summary>
    /// The account username.
    /// </summary>
    [JsonPropertyName("display_name")]
    [JsonProperty("display_name")]
    public string? DisplayName { get; }

    /// <summary>
    /// The user's role claim.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonProperty("role", Required = Required.Always)]
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
