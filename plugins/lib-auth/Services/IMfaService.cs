namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Service for TOTP multi-factor authentication operations.
/// Handles secret generation, encryption, TOTP validation,
/// recovery code management, and temporary challenge/setup token storage.
/// </summary>
public interface IMfaService
{
    /// <summary>
    /// Generates a new random TOTP secret (160-bit, base32-encoded).
    /// </summary>
    /// <returns>Base32-encoded TOTP secret string.</returns>
    string GenerateSecret();

    /// <summary>
    /// Builds an otpauth:// URI for QR code scanning by authenticator apps.
    /// </summary>
    /// <param name="secret">Base32-encoded TOTP secret.</param>
    /// <param name="accountIdentifier">Account email or username for display in the authenticator app.</param>
    /// <returns>otpauth:// URI string.</returns>
    string BuildTotpUri(string secret, string accountIdentifier);

    /// <summary>
    /// Validates a 6-digit TOTP code against a secret.
    /// Allows one time step of drift in each direction (90 second window).
    /// </summary>
    /// <param name="secret">Base32-encoded TOTP secret.</param>
    /// <param name="code">6-digit TOTP code from authenticator app.</param>
    /// <returns>True if the code is valid within the verification window.</returns>
    bool ValidateTotp(string secret, string code);

    /// <summary>
    /// Encrypts a TOTP secret using AES-256-GCM.
    /// Storage format: Base64(nonce[12] + tag[16] + ciphertext[N]).
    /// </summary>
    /// <param name="secret">Plain text TOTP secret to encrypt.</param>
    /// <returns>Base64-encoded encrypted secret.</returns>
    string EncryptSecret(string secret);

    /// <summary>
    /// Decrypts an AES-256-GCM encrypted TOTP secret.
    /// </summary>
    /// <param name="encryptedSecret">Base64-encoded encrypted secret from storage.</param>
    /// <returns>Plain text TOTP secret.</returns>
    string DecryptSecret(string encryptedSecret);

    /// <summary>
    /// Generates a set of single-use recovery codes in format xxxx-xxxx.
    /// </summary>
    /// <param name="count">Number of recovery codes to generate (default 10).</param>
    /// <returns>List of plain text recovery codes.</returns>
    List<string> GenerateRecoveryCodes(int count = 10);

    /// <summary>
    /// BCrypt-hashes a list of recovery codes for secure storage.
    /// </summary>
    /// <param name="codes">Plain text recovery codes.</param>
    /// <returns>List of BCrypt-hashed recovery codes.</returns>
    List<string> HashRecoveryCodes(List<string> codes);

    /// <summary>
    /// Verifies a recovery code against a list of BCrypt-hashed codes.
    /// </summary>
    /// <param name="code">Plain text recovery code to verify.</param>
    /// <param name="hashedCodes">List of BCrypt-hashed recovery codes.</param>
    /// <returns>Tuple of (valid, matchIndex). matchIndex is -1 if not found.</returns>
    (bool valid, int matchIndex) VerifyRecoveryCode(string code, List<string> hashedCodes);

    /// <summary>
    /// Creates an MFA challenge token for the login flow. Stored in Redis with configurable TTL.
    /// </summary>
    /// <param name="accountId">Account that passed password verification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Challenge token string.</returns>
    Task<string> CreateMfaChallengeAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Consumes (retrieves and deletes) an MFA challenge token. Single-use.
    /// </summary>
    /// <param name="token">Challenge token to consume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Account ID if token was valid and not expired, null otherwise.</returns>
    Task<Guid?> ConsumeMfaChallengeAsync(string token, CancellationToken ct);

    /// <summary>
    /// Creates an MFA setup token storing pending MFA configuration. Stored in Redis with configurable TTL.
    /// </summary>
    /// <param name="accountId">Account initiating MFA setup.</param>
    /// <param name="encryptedSecret">AES-256-GCM encrypted TOTP secret.</param>
    /// <param name="hashedRecoveryCodes">BCrypt-hashed recovery codes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Setup token string.</returns>
    Task<string> CreateMfaSetupAsync(Guid accountId, string encryptedSecret, List<string> hashedRecoveryCodes, CancellationToken ct);

    /// <summary>
    /// Consumes (retrieves and deletes) an MFA setup token. Single-use.
    /// </summary>
    /// <param name="token">Setup token to consume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Setup data if token was valid and not expired, null otherwise.</returns>
    Task<MfaSetupData?> ConsumeMfaSetupAsync(string token, CancellationToken ct);
}
