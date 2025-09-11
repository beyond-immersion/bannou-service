using BCrypt.Net;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Service for secure password hashing and verification using BCrypt.
/// </summary>
public class PasswordHashingService
{
    private readonly AuthServiceConfiguration _configuration;

    public PasswordHashingService(AuthServiceConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Hashes a password using BCrypt.
    /// </summary>
    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, _configuration.BcryptWorkFactor);
    }

    /// <summary>
    /// Verifies a password against its hash.
    /// </summary>
    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            // Hash is invalid or corrupted
            return false;
        }
    }

    /// <summary>
    /// Checks if a password needs to be rehashed (e.g., due to work factor changes).
    /// </summary>
    public bool NeedsRehash(string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hash, _configuration.BcryptWorkFactor);
        }
        catch
        {
            // Hash is invalid, assume it needs rehashing
            return true;
        }
    }
}