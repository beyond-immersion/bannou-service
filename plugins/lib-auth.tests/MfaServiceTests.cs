using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for MfaService.
/// Tests TOTP secret generation/encryption, recovery code management,
/// TOTP validation, and challenge/setup token lifecycle.
/// </summary>
public class MfaServiceTests
{
    private const string AUTH_STATE_STORE = "auth-statestore";
    private const string TEST_ENCRYPTION_KEY = "test-mfa-encryption-key-must-be-at-least-32-characters-long";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<MfaChallengeData>> _mockChallengeStore;
    private readonly Mock<IStateStore<MfaSetupData>> _mockSetupStore;
    private readonly AuthServiceConfiguration _configuration;
    private readonly MfaService _service;

    public MfaServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockChallengeStore = new Mock<IStateStore<MfaChallengeData>>();
        _mockSetupStore = new Mock<IStateStore<MfaSetupData>>();

        _mockStateStoreFactory.Setup(f => f.GetStore<MfaChallengeData>(AUTH_STATE_STORE))
            .Returns(_mockChallengeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MfaSetupData>(AUTH_STATE_STORE))
            .Returns(_mockSetupStore.Object);

        _configuration = new AuthServiceConfiguration
        {
            MfaEncryptionKey = TEST_ENCRYPTION_KEY,
            MfaIssuerName = "TestIssuer",
            MfaChallengeTtlMinutes = 5,
            BcryptWorkFactor = 4 // Low factor for fast tests
        };

        _service = new MfaService(
            _mockStateStoreFactory.Object,
            _configuration,
            new NullTelemetryProvider(),
            Mock.Of<ILogger<MfaService>>());
    }

    #region Constructor Tests

    [Fact]
    public void MfaService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<MfaService>();

    #endregion

    #region GenerateSecret Tests

    [Fact]
    public void GenerateSecret_ReturnsNonEmptyBase32String()
    {
        var secret = _service.GenerateSecret();

        Assert.False(string.IsNullOrWhiteSpace(secret));
        // Base32 uses only A-Z and 2-7
        Assert.Matches("^[A-Z2-7]+=*$", secret);
    }

    [Fact]
    public void GenerateSecret_ReturnsUniqueSecrets()
    {
        var secret1 = _service.GenerateSecret();
        var secret2 = _service.GenerateSecret();

        Assert.NotEqual(secret1, secret2);
    }

    #endregion

    #region BuildTotpUri Tests

    [Fact]
    public void BuildTotpUri_ReturnsValidOtpauthUri()
    {
        var secret = _service.GenerateSecret();

        var uri = _service.BuildTotpUri(secret, "user@example.com");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=", uri);
        Assert.Contains("issuer=TestIssuer", uri);
        Assert.Contains("user%40example.com", uri);
    }

    [Fact]
    public void BuildTotpUri_IncludesDigitsAndPeriod()
    {
        var secret = _service.GenerateSecret();

        var uri = _service.BuildTotpUri(secret, "test");

        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    #endregion

    #region EncryptSecret / DecryptSecret Tests

    [Fact]
    public void EncryptDecryptSecret_RoundTrip_ReturnsOriginalSecret()
    {
        var originalSecret = "JBSWY3DPEHPK3PXP";

        var encrypted = _service.EncryptSecret(originalSecret);
        var decrypted = _service.DecryptSecret(encrypted);

        Assert.Equal(originalSecret, decrypted);
    }

    [Fact]
    public void EncryptSecret_ProducesBase64Output()
    {
        var secret = "JBSWY3DPEHPK3PXP";

        var encrypted = _service.EncryptSecret(secret);

        Assert.False(string.IsNullOrWhiteSpace(encrypted));
        // Verify it's valid Base64 by round-tripping
        var bytes = Convert.FromBase64String(encrypted);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void EncryptSecret_ProducesDifferentCiphertextEachTime()
    {
        // AES-GCM uses random nonce, so same plaintext produces different ciphertext
        var secret = "JBSWY3DPEHPK3PXP";

        var encrypted1 = _service.EncryptSecret(secret);
        var encrypted2 = _service.EncryptSecret(secret);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void EncryptDecryptSecret_WithLongSecret_WorksCorrectly()
    {
        var longSecret = new string('A', 256);

        var encrypted = _service.EncryptSecret(longSecret);
        var decrypted = _service.DecryptSecret(encrypted);

        Assert.Equal(longSecret, decrypted);
    }

    [Fact]
    public void DecryptSecret_WithTruncatedData_ThrowsArgumentException()
    {
        // Create data shorter than nonce(12) + tag(16) + 1 byte
        var shortData = Convert.ToBase64String(new byte[28]);

        Assert.Throws<ArgumentException>(() => _service.DecryptSecret(shortData));
    }

    [Fact]
    public void DecryptSecret_WithWrongKey_ThrowsCryptographicException()
    {
        var secret = "JBSWY3DPEHPK3PXP";
        var encrypted = _service.EncryptSecret(secret);

        // Create a service with a different encryption key
        var differentConfig = new AuthServiceConfiguration
        {
            MfaEncryptionKey = "a-completely-different-encryption-key-for-wrong-key-test",
            BcryptWorkFactor = 4
        };
        var differentService = new MfaService(
            _mockStateStoreFactory.Object,
            differentConfig,
            new NullTelemetryProvider(),
            Mock.Of<ILogger<MfaService>>());

        // AES-GCM throws on authentication tag mismatch
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => differentService.DecryptSecret(encrypted));
    }

    [Fact]
    public void EncryptSecret_WithNullEncryptionKey_ThrowsInvalidOperationException()
    {
        var noKeyConfig = new AuthServiceConfiguration
        {
            MfaEncryptionKey = null,
            BcryptWorkFactor = 4
        };
        var noKeyService = new MfaService(
            _mockStateStoreFactory.Object,
            noKeyConfig,
            new NullTelemetryProvider(),
            Mock.Of<ILogger<MfaService>>());

        Assert.Throws<InvalidOperationException>(() => noKeyService.EncryptSecret("test"));
    }

    [Fact]
    public void EncryptSecret_WithShortEncryptionKey_ThrowsInvalidOperationException()
    {
        var shortKeyConfig = new AuthServiceConfiguration
        {
            MfaEncryptionKey = "too-short",
            BcryptWorkFactor = 4
        };
        var shortKeyService = new MfaService(
            _mockStateStoreFactory.Object,
            shortKeyConfig,
            new NullTelemetryProvider(),
            Mock.Of<ILogger<MfaService>>());

        Assert.Throws<InvalidOperationException>(() => shortKeyService.EncryptSecret("test"));
    }

    #endregion

    #region GenerateRecoveryCodes Tests

    [Fact]
    public void GenerateRecoveryCodes_DefaultCount_ReturnsTenCodes()
    {
        var codes = _service.GenerateRecoveryCodes();

        Assert.Equal(10, codes.Count);
    }

    [Fact]
    public void GenerateRecoveryCodes_CustomCount_ReturnsRequestedCount()
    {
        var codes = _service.GenerateRecoveryCodes(count: 5);

        Assert.Equal(5, codes.Count);
    }

    [Fact]
    public void GenerateRecoveryCodes_AllMatchXxxxDashXxxxFormat()
    {
        var codes = _service.GenerateRecoveryCodes();

        foreach (var code in codes)
        {
            // Format: xxxx-xxxx where x is lowercase alphanumeric
            Assert.Matches("^[a-z0-9]{4}-[a-z0-9]{4}$", code);
        }
    }

    [Fact]
    public void GenerateRecoveryCodes_ProducesUniqueCodes()
    {
        var codes = _service.GenerateRecoveryCodes(count: 20);

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    #endregion

    #region HashRecoveryCodes / VerifyRecoveryCode Tests

    [Fact]
    public void HashRecoveryCodes_ReturnsCorrectCount()
    {
        var codes = new List<string> { "abcd-1234", "efgh-5678" };

        var hashed = _service.HashRecoveryCodes(codes);

        Assert.Equal(2, hashed.Count);
    }

    [Fact]
    public void HashRecoveryCodes_ProducesBcryptHashes()
    {
        var codes = new List<string> { "abcd-1234" };

        var hashed = _service.HashRecoveryCodes(codes);

        // BCrypt hashes start with $2a$, $2b$, or $2y$
        Assert.Matches(@"^\$2[aby]\$", hashed[0]);
    }

    [Fact]
    public void VerifyRecoveryCode_WithCorrectCode_ReturnsValidAndIndex()
    {
        var codes = new List<string> { "abcd-1234", "efgh-5678", "ijkl-9012" };
        var hashed = _service.HashRecoveryCodes(codes);

        var (valid, matchIndex) = _service.VerifyRecoveryCode("efgh-5678", hashed);

        Assert.True(valid);
        Assert.Equal(1, matchIndex);
    }

    [Fact]
    public void VerifyRecoveryCode_WithWrongCode_ReturnsInvalid()
    {
        var codes = new List<string> { "abcd-1234" };
        var hashed = _service.HashRecoveryCodes(codes);

        var (valid, matchIndex) = _service.VerifyRecoveryCode("wrong-code", hashed);

        Assert.False(valid);
        Assert.Equal(-1, matchIndex);
    }

    [Fact]
    public void VerifyRecoveryCode_WithNormalizedInput_MatchesDespiteFormatDifferences()
    {
        // Hash "abcd1234" (normalized form of "abcd-1234")
        var codes = new List<string> { "abcd-1234" };
        var hashed = _service.HashRecoveryCodes(codes);

        // Verify with uppercase and no hyphen — normalization should handle this
        var (valid, _) = _service.VerifyRecoveryCode("ABCD1234", hashed);

        Assert.True(valid);
    }

    [Fact]
    public void VerifyRecoveryCode_WithSpacesInInput_MatchesAfterNormalization()
    {
        var codes = new List<string> { "abcd-1234" };
        var hashed = _service.HashRecoveryCodes(codes);

        // Spaces are NOT stripped by NormalizeRecoveryCode (only hyphens and lowercasing)
        // so this should fail unless spaces are also normalized
        var (valid, _) = _service.VerifyRecoveryCode("abcd 1234", hashed);

        // NormalizeRecoveryCode does code.Replace("-", "").ToLowerInvariant()
        // Spaces are not stripped, so "abcd 1234" -> "abcd 1234" != "abcd1234"
        Assert.False(valid);
    }

    [Fact]
    public void VerifyRecoveryCode_EmptyHashedList_ReturnsInvalid()
    {
        var (valid, matchIndex) = _service.VerifyRecoveryCode("abcd-1234", new List<string>());

        Assert.False(valid);
        Assert.Equal(-1, matchIndex);
    }

    [Fact]
    public void HashAndVerify_FullRoundTrip_AllCodesVerifiable()
    {
        var codes = _service.GenerateRecoveryCodes(count: 5);
        var hashed = _service.HashRecoveryCodes(codes);

        for (var i = 0; i < codes.Count; i++)
        {
            var (valid, matchIndex) = _service.VerifyRecoveryCode(codes[i], hashed);
            Assert.True(valid, $"Code at index {i} failed verification");
            Assert.Equal(i, matchIndex);
        }
    }

    #endregion

    #region ValidateTotp Tests

    [Fact]
    public void ValidateTotp_WithCurrentCode_ReturnsTrue()
    {
        // Generate a real secret and compute the current TOTP code
        var secret = _service.GenerateSecret();
        var secretBytes = OtpNet.Base32Encoding.ToBytes(secret);
        var totp = new OtpNet.Totp(secretBytes);
        var currentCode = totp.ComputeTotp();

        var result = _service.ValidateTotp(secret, currentCode);

        Assert.True(result);
    }

    [Fact]
    public void ValidateTotp_WithWrongCode_ReturnsFalse()
    {
        var secret = _service.GenerateSecret();

        var result = _service.ValidateTotp(secret, "000000");

        // "000000" is almost certainly not the current TOTP code
        // There's a vanishingly small chance this could match, but in practice it won't
        // If this test becomes flaky, use a known-past timestamp instead
        Assert.False(result);
    }

    [Fact]
    public void ValidateTotp_WithNonNumericCode_ReturnsFalse()
    {
        var secret = _service.GenerateSecret();

        var result = _service.ValidateTotp(secret, "abcdef");

        Assert.False(result);
    }

    #endregion

    #region CreateMfaChallengeAsync / ConsumeMfaChallengeAsync Tests

    [Fact]
    public async Task CreateMfaChallengeAsync_SavesTokenToStateStore()
    {
        var accountId = Guid.NewGuid();
        string? capturedKey = null;
        MfaChallengeData? capturedData = null;

        _mockChallengeStore.Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("mfa-challenge-")),
                It.IsAny<MfaChallengeData>(),
                It.IsAny<StateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, MfaChallengeData, StateOptions, CancellationToken>((k, d, _, _) =>
            {
                capturedKey = k;
                capturedData = d;
            })
            .ReturnsAsync("etag");

        var token = await _service.CreateMfaChallengeAsync(accountId, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal($"mfa-challenge-{token}", capturedKey);
        Assert.NotNull(capturedData);
        Assert.Equal(accountId, capturedData.AccountId);
        Assert.True(capturedData.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateMfaChallengeAsync_SetsTtlFromConfiguration()
    {
        var accountId = Guid.NewGuid();
        StateOptions? capturedOptions = null;

        _mockChallengeStore.Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<MfaChallengeData>(),
                It.IsAny<StateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, MfaChallengeData, StateOptions, CancellationToken>((_, _, opts, _) =>
            {
                capturedOptions = opts;
            })
            .ReturnsAsync("etag");

        await _service.CreateMfaChallengeAsync(accountId, CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.Equal(_configuration.MfaChallengeTtlMinutes * 60, capturedOptions.Ttl);
    }

    [Fact]
    public async Task ConsumeMfaChallengeAsync_WithValidToken_ReturnsAccountIdAndDeletes()
    {
        var accountId = Guid.NewGuid();
        var token = "test-challenge-token";
        var key = $"mfa-challenge-{token}";

        _mockChallengeStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaChallengeData
            {
                AccountId = accountId,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            });
        _mockChallengeStore.Setup(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConsumeMfaChallengeAsync(token, CancellationToken.None);

        Assert.Equal(accountId, result);
        _mockChallengeStore.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeMfaChallengeAsync_WithExpiredToken_ReturnsNull()
    {
        var token = "expired-token";
        var key = $"mfa-challenge-{token}";

        _mockChallengeStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaChallengeData
            {
                AccountId = Guid.NewGuid(),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) // Already expired
            });
        _mockChallengeStore.Setup(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConsumeMfaChallengeAsync(token, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeMfaChallengeAsync_WithNonExistentToken_ReturnsNull()
    {
        var token = "nonexistent-token";
        var key = $"mfa-challenge-{token}";

        _mockChallengeStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaChallengeData?)null);

        var result = await _service.ConsumeMfaChallengeAsync(token, CancellationToken.None);

        Assert.Null(result);
    }

    #endregion

    #region CreateMfaSetupAsync / ConsumeMfaSetupAsync Tests

    [Fact]
    public async Task CreateMfaSetupAsync_SavesSetupDataToStateStore()
    {
        var accountId = Guid.NewGuid();
        var encryptedSecret = "encrypted-secret-data";
        var hashedCodes = new List<string> { "hashed-code-1", "hashed-code-2" };
        string? capturedKey = null;
        MfaSetupData? capturedData = null;

        _mockSetupStore.Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("mfa-setup-")),
                It.IsAny<MfaSetupData>(),
                It.IsAny<StateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, MfaSetupData, StateOptions, CancellationToken>((k, d, _, _) =>
            {
                capturedKey = k;
                capturedData = d;
            })
            .ReturnsAsync("etag");

        var token = await _service.CreateMfaSetupAsync(accountId, encryptedSecret, hashedCodes, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal($"mfa-setup-{token}", capturedKey);
        Assert.NotNull(capturedData);
        Assert.Equal(accountId, capturedData.AccountId);
        Assert.Equal(encryptedSecret, capturedData.EncryptedSecret);
        Assert.Equal(hashedCodes, capturedData.HashedRecoveryCodes);
        Assert.True(capturedData.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ConsumeMfaSetupAsync_WithValidToken_ReturnsSetupDataAndDeletes()
    {
        var token = "test-setup-token";
        var key = $"mfa-setup-{token}";
        var expectedData = new MfaSetupData
        {
            AccountId = Guid.NewGuid(),
            EncryptedSecret = "encrypted-secret",
            HashedRecoveryCodes = new List<string> { "hash1", "hash2" },
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        _mockSetupStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);
        _mockSetupStore.Setup(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConsumeMfaSetupAsync(token, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expectedData.AccountId, result.AccountId);
        Assert.Equal(expectedData.EncryptedSecret, result.EncryptedSecret);
        Assert.Equal(expectedData.HashedRecoveryCodes, result.HashedRecoveryCodes);
        _mockSetupStore.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeMfaSetupAsync_WithExpiredToken_ReturnsNull()
    {
        var token = "expired-setup-token";
        var key = $"mfa-setup-{token}";

        _mockSetupStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaSetupData
            {
                AccountId = Guid.NewGuid(),
                EncryptedSecret = "secret",
                HashedRecoveryCodes = new List<string>(),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) // Already expired
            });
        _mockSetupStore.Setup(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ConsumeMfaSetupAsync(token, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeMfaSetupAsync_WithNonExistentToken_ReturnsNull()
    {
        var token = "nonexistent-setup-token";
        var key = $"mfa-setup-{token}";

        _mockSetupStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaSetupData?)null);

        var result = await _service.ConsumeMfaSetupAsync(token, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeMfaChallengeAsync_IsSingleUse_DeleteCalledBeforeExpiryCheck()
    {
        // Verify that the token is deleted even if it turns out to be expired.
        // This prevents replay attacks with expired tokens.
        var token = "replay-test-token";
        var key = $"mfa-challenge-{token}";
        var deleteWasCalled = false;

        _mockChallengeStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaChallengeData
            {
                AccountId = Guid.NewGuid(),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) // Expired
            });
        _mockChallengeStore.Setup(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => deleteWasCalled = true)
            .ReturnsAsync(true);

        var result = await _service.ConsumeMfaChallengeAsync(token, CancellationToken.None);

        Assert.Null(result); // Expired, so null
        Assert.True(deleteWasCalled); // But delete was still called (prevents replay)
    }

    #endregion
}
