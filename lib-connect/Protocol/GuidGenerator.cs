using System;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Dependency-free GUID generation utilities for client-server security.
/// Provides client-salted GUIDs to prevent cross-session exploitation.
/// </summary>
public static class GuidGenerator
{
    private static string? _cachedServerSalt;
    private static readonly object _saltLock = new();

    /// <summary>
    /// Gets the shared server salt for GUID generation.
    /// Uses BANNOU_SERVER_SALT environment variable if set, otherwise generates once per process.
    /// All services must use the same salt for shortcuts to work correctly.
    /// </summary>
    public static string GetSharedServerSalt()
    {
        if (_cachedServerSalt != null)
            return _cachedServerSalt;

        lock (_saltLock)
        {
            if (_cachedServerSalt != null)
                return _cachedServerSalt;

            // Try environment variable first (allows shared salt across services)
            var envSalt = Environment.GetEnvironmentVariable("BANNOU_SERVER_SALT");
            if (!string.IsNullOrEmpty(envSalt))
            {
                _cachedServerSalt = envSalt;
                return _cachedServerSalt;
            }

            // Generate new salt (will be unique per process - fine for development)
            _cachedServerSalt = GenerateServerSalt();
            return _cachedServerSalt;
        }
    }

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
    /// Generates a client-to-client routing GUID for P2P communication.
    /// </summary>
    /// <param name="sourceSessionId">Source client session ID</param>
    /// <param name="targetSessionId">Target client session ID</param>
    /// <param name="serverSalt">Server salt for security</param>
    /// <returns>GUID for client-to-client message routing</returns>
    public static Guid GenerateClientGuid(
        string sourceSessionId,
        string targetSessionId,
        string serverSalt)
    {
        if (string.IsNullOrEmpty(sourceSessionId))
            throw new ArgumentException("Source session ID cannot be null or empty", nameof(sourceSessionId));
        if (string.IsNullOrEmpty(targetSessionId))
            throw new ArgumentException("Target session ID cannot be null or empty", nameof(targetSessionId));
        if (string.IsNullOrEmpty(serverSalt))
            throw new ArgumentException("Server salt cannot be null or empty", nameof(serverSalt));

        // Order the session IDs consistently to ensure same GUID regardless of direction
        var orderedSessions = string.Compare(sourceSessionId, targetSessionId) < 0
            ? $"{sourceSessionId}:{targetSessionId}"
            : $"{targetSessionId}:{sourceSessionId}";

        var input = $"client:{orderedSessions}|salt:{serverSalt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Set version bits for client-to-client GUID (custom version)
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x60); // Custom version 6
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
    /// Useful for detecting shortcut GUIDs vs service GUIDs (v5) or client GUIDs (v6).
    /// </summary>
    /// <param name="guid">GUID to validate</param>
    /// <returns>True if the GUID has version 7 bits set (session shortcut)</returns>
    public static bool IsSessionShortcutGuid(Guid guid)
    {
        var bytes = guid.ToByteArray();
        // Version is stored in byte 6 (high nibble) - matches where we set it during generation
        var version = (bytes[6] >> 4) & 0x0F;
        return version == 7;
    }

    /// <summary>
    /// Validates that a GUID is a service GUID (version 5).
    /// </summary>
    /// <param name="guid">GUID to validate</param>
    /// <returns>True if the GUID has version 5 bits set (service capability)</returns>
    public static bool IsServiceGuid(Guid guid)
    {
        var bytes = guid.ToByteArray();
        // Version is stored in byte 6 (high nibble) - matches where we set it during generation
        var version = (bytes[6] >> 4) & 0x0F;
        return version == 5;
    }

    /// <summary>
    /// Validates that a GUID is a client-to-client GUID (version 6).
    /// </summary>
    /// <param name="guid">GUID to validate</param>
    /// <returns>True if the GUID has version 6 bits set (client-to-client routing)</returns>
    public static bool IsClientGuid(Guid guid)
    {
        var bytes = guid.ToByteArray();
        // Version is stored in byte 6 (high nibble) - matches where we set it during generation
        var version = (bytes[6] >> 4) & 0x0F;
        return version == 6;
    }

    /// <summary>
    /// Generates a unique message ID for request/response correlation.
    /// Uses timestamp and entropy for uniqueness.
    /// </summary>
    /// <returns>Unique message ID</returns>
    public static ulong GenerateMessageId()
    {
        // Use high-resolution timestamp (microseconds) + random entropy
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entropy = Random.Shared.Next(0, 65536);

        // Combine timestamp (48 bits) with entropy (16 bits)
        // Cast entropy to uint first to avoid sign extension issues
        return ((ulong)timestamp << 16) | (uint)entropy;
    }

    /// <summary>
    /// Validates that a GUID was generated by this system.
    /// Useful for detecting tampered or externally-generated GUIDs.
    /// </summary>
    /// <param name="guid">GUID to validate</param>
    /// <param name="sessionId">Session ID that should have generated this GUID</param>
    /// <param name="serviceName">Service name that should match</param>
    /// <param name="serverSalt">Server salt used in generation</param>
    /// <returns>True if the GUID is valid for these parameters</returns>
    public static bool ValidateServiceGuid(
        Guid guid,
        string sessionId,
        string serviceName,
        string serverSalt)
    {
        try
        {
            var expectedGuid = GenerateServiceGuid(sessionId, serviceName, serverSalt);
            return guid == expectedGuid;
        }
        catch
        {
            return false;
        }
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
