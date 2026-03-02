using BeyondImmersion.BannouService.Connect.Protocol;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for GuidGenerator.
/// Tests verify determinism, bidirectionality, version bit patterns, and validation.
/// </summary>
public class GuidGeneratorTests
{
    private const string SERVER_SALT = "test-server-salt-abc123";

    #region GenerateClientGuid Tests

    /// <summary>
    /// CRITICAL PROTOCOL INVARIANT: GenerateClientGuid(A,B) must equal GenerateClientGuid(B,A).
    /// This ensures the same routing GUID is used regardless of which client initiates.
    /// </summary>
    [Fact]
    public void GenerateClientGuid_IsBidirectional()
    {
        // Arrange
        var sessionA = "session-alpha";
        var sessionB = "session-beta";

        // Act
        var guidAB = GuidGenerator.GenerateClientGuid(sessionA, sessionB, SERVER_SALT);
        var guidBA = GuidGenerator.GenerateClientGuid(sessionB, sessionA, SERVER_SALT);

        // Assert — same GUID regardless of direction
        Assert.Equal(guidAB, guidBA);
    }

    /// <summary>
    /// Verifies that client GUIDs have version 6 bits set (custom client-to-client version).
    /// </summary>
    [Fact]
    public void GenerateClientGuid_SetsVersion6Bits()
    {
        // Arrange & Act
        var guid = GuidGenerator.GenerateClientGuid("session-1", "session-2", SERVER_SALT);

        // Assert
        Assert.True(GuidGenerator.IsClientGuid(guid));
        Assert.False(GuidGenerator.IsServiceGuid(guid));
        Assert.False(GuidGenerator.IsSessionShortcutGuid(guid));
    }

    /// <summary>
    /// Verifies determinism: same inputs always produce the same GUID.
    /// </summary>
    [Fact]
    public void GenerateClientGuid_IsDeterministic()
    {
        // Act
        var guid1 = GuidGenerator.GenerateClientGuid("session-a", "session-b", SERVER_SALT);
        var guid2 = GuidGenerator.GenerateClientGuid("session-a", "session-b", SERVER_SALT);

        // Assert
        Assert.Equal(guid1, guid2);
    }

    /// <summary>
    /// Verifies that different session pairs produce different GUIDs.
    /// </summary>
    [Fact]
    public void GenerateClientGuid_DifferentInputs_ProduceDifferentGuids()
    {
        // Act
        var guid1 = GuidGenerator.GenerateClientGuid("session-a", "session-b", SERVER_SALT);
        var guid2 = GuidGenerator.GenerateClientGuid("session-a", "session-c", SERVER_SALT);

        // Assert
        Assert.NotEqual(guid1, guid2);
    }

    /// <summary>
    /// Verifies that null/empty inputs throw ArgumentException.
    /// </summary>
    [Theory]
    [InlineData(null, "target", "salt")]
    [InlineData("source", null, "salt")]
    [InlineData("source", "target", null)]
    [InlineData("", "target", "salt")]
    [InlineData("source", "", "salt")]
    [InlineData("source", "target", "")]
    public void GenerateClientGuid_InvalidInputs_Throws(string? source, string? target, string? salt)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            GuidGenerator.GenerateClientGuid(source!, target!, salt!));
    }

    #endregion

    #region ValidateServiceGuid Tests

    /// <summary>
    /// Verifies that a correctly generated GUID validates successfully.
    /// </summary>
    [Fact]
    public void ValidateServiceGuid_MatchingParameters_ReturnsTrue()
    {
        // Arrange
        var sessionId = "session-123";
        var serviceName = "account";
        var guid = GuidGenerator.GenerateServiceGuid(sessionId, serviceName, SERVER_SALT);

        // Act
        var isValid = GuidGenerator.ValidateServiceGuid(guid, sessionId, serviceName, SERVER_SALT);

        // Assert
        Assert.True(isValid);
    }

    /// <summary>
    /// Verifies that a GUID generated for a different session fails validation.
    /// </summary>
    [Fact]
    public void ValidateServiceGuid_DifferentSession_ReturnsFalse()
    {
        // Arrange
        var guid = GuidGenerator.GenerateServiceGuid("session-123", "account", SERVER_SALT);

        // Act
        var isValid = GuidGenerator.ValidateServiceGuid(guid, "session-different", "account", SERVER_SALT);

        // Assert
        Assert.False(isValid);
    }

    /// <summary>
    /// Verifies that a GUID generated for a different service fails validation.
    /// </summary>
    [Fact]
    public void ValidateServiceGuid_DifferentService_ReturnsFalse()
    {
        // Arrange
        var guid = GuidGenerator.GenerateServiceGuid("session-123", "account", SERVER_SALT);

        // Act
        var isValid = GuidGenerator.ValidateServiceGuid(guid, "session-123", "auth", SERVER_SALT);

        // Assert
        Assert.False(isValid);
    }

    /// <summary>
    /// Verifies that a random GUID fails validation.
    /// </summary>
    [Fact]
    public void ValidateServiceGuid_RandomGuid_ReturnsFalse()
    {
        // Act
        var isValid = GuidGenerator.ValidateServiceGuid(Guid.NewGuid(), "session-123", "account", SERVER_SALT);

        // Assert
        Assert.False(isValid);
    }

    #endregion
}
