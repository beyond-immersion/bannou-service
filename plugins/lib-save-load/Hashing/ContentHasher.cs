using System.Security.Cryptography;

namespace BeyondImmersion.BannouService.SaveLoad.Hashing;

/// <summary>
/// Helper class for computing SHA-256 content hashes for save data integrity verification.
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// Computes the SHA-256 hash of the data.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The hash as a lowercase hex string.</returns>
    public static string ComputeHash(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return ComputeHash(Array.Empty<byte>());
        }

        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that the data matches the expected hash.
    /// </summary>
    /// <param name="data">The data to verify.</param>
    /// <param name="expectedHash">The expected SHA-256 hash (lowercase hex).</param>
    /// <returns>True if the hash matches, false otherwise.</returns>
    public static bool VerifyHash(byte[] data, string expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actualHash = ComputeHash(data);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
