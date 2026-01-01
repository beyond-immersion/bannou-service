using System;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Protocol;

/// <summary>
/// Shared GUID generation utilities for client-server security.
/// Provides client-salted GUIDs to prevent cross-session exploitation.
/// This is the shared version - all services should use this for consistency.
/// Server salt should come from service configuration (Tenet 21).
/// </summary>
/// <remarks>
/// <para>
/// <b>WARNING - MULTI-INSTANCE SAFETY (T9):</b> The <see cref="GenerateServerSalt"/> method
/// generates cryptographically random values. In a multi-instance deployment, each instance
/// would generate different salts, causing client GUIDs to differ across instances.
/// </para>
/// <para>
/// <b>CORRECT USAGE:</b> Server salt MUST be configured externally (e.g., via environment
/// variables or service configuration) and shared across all instances. Never call
/// <see cref="GenerateServerSalt"/> at runtime in production - use it only for initial
/// salt generation during deployment setup.
/// </para>
/// <para>
/// See TENETS.md T9: Multi-Instance Safety for distributed state requirements.
/// </para>
/// </remarks>
public static class GuidGenerator
{
    /// <summary>
    /// Generates a client-salted GUID for a service endpoint.
    /// Each client gets unique GUIDs for identical services, preventing security exploits.
    /// </summary>
    /// <param name="sessionId">Unique session identifier</param>
    /// <param name="serviceName">Name of the target service</param>
    /// <param name="serverSalt">Server-generated salt for additional security</param>
    /// <param name="additionalEntropy">Optional additional entropy for specialized use cases</param>
    /// <returns>Deterministic but unique GUID for this session-service combination</returns>
    public static Guid GenerateServiceGuid(
        string sessionId,
        string serviceName,
        string serverSalt,
        string? additionalEntropy = null)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));
        if (string.IsNullOrEmpty(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        if (string.IsNullOrEmpty(serverSalt))
            throw new ArgumentException("Server salt cannot be null or empty", nameof(serverSalt));

        // Combine inputs with separators to prevent collision attacks
        var input = $"service:{serviceName}|session:{sessionId}|salt:{serverSalt}";
        if (!string.IsNullOrEmpty(additionalEntropy))
        {
            input += $"|entropy:{additionalEntropy}";
        }

        // Use SHA256 for cryptographic security
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // Use first 16 bytes of hash as GUID bytes
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Set version bits to indicate this is a deterministic UUID (version 5 style)
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // Version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // Variant bits

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Generates a client-salted GUID for a session shortcut.
    /// Uses UUID version 7 bits to distinguish from service GUIDs (version 5)
    /// and client-to-client GUIDs (version 6).
    /// </summary>
    /// <param name="sessionId">Unique session identifier</param>
    /// <param name="shortcutName">Machine-readable name for this shortcut</param>
    /// <param name="sourceService">Service that created this shortcut</param>
    /// <param name="serverSalt">Server-generated salt for additional security</param>
    /// <returns>Deterministic but unique GUID for this session-shortcut combination</returns>
    public static Guid GenerateSessionShortcutGuid(
        string sessionId,
        string shortcutName,
        string sourceService,
        string serverSalt)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));
        if (string.IsNullOrEmpty(shortcutName))
            throw new ArgumentException("Shortcut name cannot be null or empty", nameof(shortcutName));
        if (string.IsNullOrEmpty(sourceService))
            throw new ArgumentException("Source service cannot be null or empty", nameof(sourceService));
        if (string.IsNullOrEmpty(serverSalt))
            throw new ArgumentException("Server salt cannot be null or empty", nameof(serverSalt));

        // Combine inputs with separators to prevent collision attacks
        var input = $"shortcut:{shortcutName}|session:{sessionId}|source:{sourceService}|salt:{serverSalt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Set version bits for session shortcut GUID (custom version 7)
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x70); // Version 7
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // Variant bits

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Validates that a GUID is a session shortcut GUID (version 7).
    /// </summary>
    public static bool IsSessionShortcutGuid(Guid guid)
    {
        var bytes = guid.ToByteArray();
        var version = (bytes[6] >> 4) & 0x0F;
        return version == 7;
    }

    /// <summary>
    /// Validates that a GUID is a service GUID (version 5).
    /// </summary>
    public static bool IsServiceGuid(Guid guid)
    {
        var bytes = guid.ToByteArray();
        var version = (bytes[6] >> 4) & 0x0F;
        return version == 5;
    }

    /// <summary>
    /// Creates a server salt with cryptographic randomness.
    /// Should be generated once per server instance and reused.
    /// </summary>
    /// <returns>Cryptographically secure server salt</returns>
    public static string GenerateServerSalt()
    {
        var saltBytes = new byte[32]; // 256 bits of entropy
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);

        return Convert.ToBase64String(saltBytes);
    }
}
