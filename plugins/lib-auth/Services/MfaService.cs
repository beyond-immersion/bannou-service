using System.Security.Cryptography;
using System.Text;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// TOTP multi-factor authentication service implementation.
/// Handles secret generation/encryption, TOTP validation, recovery code management,
/// and Redis-backed challenge/setup token lifecycle.
/// </summary>
public class MfaService : IMfaService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly AuthServiceConfiguration _configuration;
    private readonly ILogger<MfaService> _logger;

    private const string MFA_CHALLENGE_KEY_PREFIX = "mfa-challenge-";
    private const string MFA_SETUP_KEY_PREFIX = "mfa-setup-";
    private const int TOTP_SECRET_LENGTH = 20; // 160-bit per RFC 4226/6238
    private const int NONCE_SIZE = 12; // AES-GCM nonce size in bytes
    private const int TAG_SIZE = 16; // AES-GCM authentication tag size in bytes
    private const int RECOVERY_CODE_SEGMENT_LENGTH = 4;

    /// <summary>
    /// Initializes MFA service with state store and configuration dependencies.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for Redis-backed token storage.</param>
    /// <param name="configuration">Auth service configuration containing MFA settings.</param>
    /// <param name="logger">Logger instance.</param>
    public MfaService(
        IStateStoreFactory stateStoreFactory,
        AuthServiceConfiguration configuration,
        ILogger<MfaService> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string GenerateSecret()
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(TOTP_SECRET_LENGTH);
        return Base32Encoding.ToString(secretBytes);
    }

    /// <inheritdoc/>
    public string BuildTotpUri(string secret, string accountIdentifier)
    {
        var otpUri = new OtpUri(
            OtpType.Totp,
            secret,
            accountIdentifier,
            _configuration.MfaIssuerName,
            digits: 6,
            period: 30);
        return otpUri.ToString();
    }

    /// <inheritdoc/>
    public bool ValidateTotp(string secret, string code)
    {
        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes);
        // Allow 1 time step drift in each direction (90 second total window)
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    /// <inheritdoc/>
    public string EncryptSecret(string secret)
    {
        var key = DeriveEncryptionKey();
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];

        using var aesGcm = new AesGcm(key, TAG_SIZE);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Storage format: nonce[12] + tag[16] + ciphertext[N]
        var result = new byte[NONCE_SIZE + TAG_SIZE + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NONCE_SIZE);
        Buffer.BlockCopy(tag, 0, result, NONCE_SIZE, TAG_SIZE);
        Buffer.BlockCopy(ciphertext, 0, result, NONCE_SIZE + TAG_SIZE, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    /// <inheritdoc/>
    public string DecryptSecret(string encryptedSecret)
    {
        var key = DeriveEncryptionKey();
        var data = Convert.FromBase64String(encryptedSecret);

        if (data.Length < NONCE_SIZE + TAG_SIZE + 1)
        {
            throw new ArgumentException("Encrypted secret data is too short to contain valid AES-GCM ciphertext");
        }

        var nonce = new byte[NONCE_SIZE];
        var tag = new byte[TAG_SIZE];
        var ciphertextLength = data.Length - NONCE_SIZE - TAG_SIZE;
        var ciphertext = new byte[ciphertextLength];
        var plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(data, 0, nonce, 0, NONCE_SIZE);
        Buffer.BlockCopy(data, NONCE_SIZE, tag, 0, TAG_SIZE);
        Buffer.BlockCopy(data, NONCE_SIZE + TAG_SIZE, ciphertext, 0, ciphertextLength);

        using var aesGcm = new AesGcm(key, TAG_SIZE);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <inheritdoc/>
    public List<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            codes.Add(GenerateSingleRecoveryCode());
        }
        return codes;
    }

    /// <inheritdoc/>
    public List<string> HashRecoveryCodes(List<string> codes)
    {
        return codes.Select(code =>
            BCrypt.Net.BCrypt.HashPassword(NormalizeRecoveryCode(code), _configuration.BcryptWorkFactor))
            .ToList();
    }

    /// <inheritdoc/>
    public (bool valid, int matchIndex) VerifyRecoveryCode(string code, List<string> hashedCodes)
    {
        var normalizedCode = NormalizeRecoveryCode(code);
        for (var i = 0; i < hashedCodes.Count; i++)
        {
            if (BCrypt.Net.BCrypt.Verify(normalizedCode, hashedCodes[i]))
            {
                return (true, i);
            }
        }
        return (false, -1);
    }

    /// <inheritdoc/>
    public async Task<string> CreateMfaChallengeAsync(Guid accountId, CancellationToken ct)
    {
        var token = GenerateSecureToken();
        var challengeData = new MfaChallengeData
        {
            AccountId = accountId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_configuration.MfaChallengeTtlMinutes)
        };

        var store = _stateStoreFactory.GetStore<MfaChallengeData>(StateStoreDefinitions.Auth);
        var key = $"{MFA_CHALLENGE_KEY_PREFIX}{token}";
        await store.SaveAsync(key, challengeData, new StateOptions { Ttl = _configuration.MfaChallengeTtlMinutes * 60 }, ct);

        _logger.LogDebug("Created MFA challenge for account {AccountId}, TTL {TtlMinutes}m", accountId, _configuration.MfaChallengeTtlMinutes);
        return token;
    }

    /// <inheritdoc/>
    public async Task<Guid?> ConsumeMfaChallengeAsync(string token, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<MfaChallengeData>(StateStoreDefinitions.Auth);
        var key = $"{MFA_CHALLENGE_KEY_PREFIX}{token}";

        var challenge = await store.GetAsync(key, ct);
        if (challenge == null)
        {
            _logger.LogDebug("MFA challenge token not found or expired");
            return null;
        }

        // Delete immediately (single-use)
        await store.DeleteAsync(key, ct);

        // Double-check expiry (Redis TTL should handle this, but verify in case of clock skew)
        if (challenge.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("MFA challenge token expired for account {AccountId}", challenge.AccountId);
            return null;
        }

        _logger.LogDebug("Consumed MFA challenge for account {AccountId}", challenge.AccountId);
        return challenge.AccountId;
    }

    /// <inheritdoc/>
    public async Task<string> CreateMfaSetupAsync(Guid accountId, string encryptedSecret, List<string> hashedRecoveryCodes, CancellationToken ct)
    {
        var token = GenerateSecureToken();
        var setupData = new MfaSetupData
        {
            AccountId = accountId,
            EncryptedSecret = encryptedSecret,
            HashedRecoveryCodes = hashedRecoveryCodes,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_configuration.MfaChallengeTtlMinutes)
        };

        var store = _stateStoreFactory.GetStore<MfaSetupData>(StateStoreDefinitions.Auth);
        var key = $"{MFA_SETUP_KEY_PREFIX}{token}";
        await store.SaveAsync(key, setupData, new StateOptions { Ttl = _configuration.MfaChallengeTtlMinutes * 60 }, ct);

        _logger.LogDebug("Created MFA setup token for account {AccountId}, TTL {TtlMinutes}m", accountId, _configuration.MfaChallengeTtlMinutes);
        return token;
    }

    /// <inheritdoc/>
    public async Task<MfaSetupData?> ConsumeMfaSetupAsync(string token, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<MfaSetupData>(StateStoreDefinitions.Auth);
        var key = $"{MFA_SETUP_KEY_PREFIX}{token}";

        var setup = await store.GetAsync(key, ct);
        if (setup == null)
        {
            _logger.LogDebug("MFA setup token not found or expired");
            return null;
        }

        // Delete immediately (single-use)
        await store.DeleteAsync(key, ct);

        if (setup.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("MFA setup token expired for account {AccountId}", setup.AccountId);
            return null;
        }

        _logger.LogDebug("Consumed MFA setup token for account {AccountId}", setup.AccountId);
        return setup;
    }

    /// <summary>
    /// Derives a 32-byte AES-256 key from the configured MFA encryption key using SHA-256.
    /// Normalizes any key >= 32 chars to exactly 32 bytes.
    /// </summary>
    private byte[] DeriveEncryptionKey()
    {
        var encryptionKey = _configuration.MfaEncryptionKey
            ?? throw new InvalidOperationException(
                "MFA encryption key (AUTH_MFA_ENCRYPTION_KEY) is not configured. "
                + "This key is required for MFA operations. Set it to a string of at least 32 characters.");

        if (encryptionKey.Length < 32)
        {
            throw new InvalidOperationException(
                "MFA encryption key must be at least 32 characters. Current length: " + encryptionKey.Length);
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
    }

    /// <summary>
    /// Generates a single recovery code in format xxxx-xxxx using alphanumeric characters.
    /// </summary>
    private static string GenerateSingleRecoveryCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> randomBytes = stackalloc byte[RECOVERY_CODE_SEGMENT_LENGTH * 2];
        RandomNumberGenerator.Fill(randomBytes);

        var sb = new StringBuilder(RECOVERY_CODE_SEGMENT_LENGTH * 2 + 1);
        for (var i = 0; i < RECOVERY_CODE_SEGMENT_LENGTH * 2; i++)
        {
            if (i == RECOVERY_CODE_SEGMENT_LENGTH)
            {
                sb.Append('-');
            }
            sb.Append(chars[randomBytes[i] % chars.Length]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalizes recovery code input by removing hyphens and lowercasing for consistent comparison.
    /// </summary>
    private static string NormalizeRecoveryCode(string code)
    {
        return code.Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Generates a cryptographically secure URL-safe token (same pattern as TokenService).
    /// </summary>
    private static string GenerateSecureToken()
    {
        var tokenBytes = new byte[32]; // 256 bits
        RandomNumberGenerator.Fill(tokenBytes);
        return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
