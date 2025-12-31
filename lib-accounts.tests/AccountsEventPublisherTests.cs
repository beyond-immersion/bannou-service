using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Unit tests for AccountsEventPublisher.
/// Tests event publishing for account lifecycle events.
/// </summary>
public class AccountsEventPublisherTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<AccountsEventPublisher>> _mockLogger;
    private readonly AccountsEventPublisher _publisher;

    public AccountsEventPublisherTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<AccountsEventPublisher>>();

        _publisher = new AccountsEventPublisher(
            _mockMessageBus.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        Assert.NotNull(_publisher);
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AccountsEventPublisher(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AccountsEventPublisher(
            _mockMessageBus.Object,
            null!));
    }

    #endregion

    #region PublishAccountCreatedAsync Tests

    [Fact]
    public async Task PublishAccountCreatedAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var email = "test@example.com";
        var displayName = "Test User";
        var roles = new List<string> { "user" };

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.created",
            It.IsAny<AccountCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _publisher.PublishAccountCreatedAsync(accountId, email, displayName, roles);

        // Assert
        Assert.True(result);
        _mockMessageBus.Verify(m => m.PublishAsync(
            "account.created",
            It.Is<AccountCreatedEvent>(e =>
                e.AccountId == accountId &&
                e.Email == email &&
                e.DisplayName == displayName &&
                e.Roles.SequenceEqual(roles)),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAccountCreatedAsync_WithNullDisplayName_ShouldUseEmptyString()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var email = "test@example.com";

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.created",
            It.IsAny<AccountCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _publisher.PublishAccountCreatedAsync(accountId, email, null, null);

        // Assert
        Assert.True(result);
        _mockMessageBus.Verify(m => m.PublishAsync(
            "account.created",
            It.Is<AccountCreatedEvent>(e =>
                e.DisplayName == string.Empty &&
                e.Roles.Count == 0),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAccountCreatedAsync_WhenPublishFails_ShouldReturnFalse()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var email = "test@example.com";

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.created",
            It.IsAny<AccountCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Publish failed"));

        // Act
        var result = await _publisher.PublishAccountCreatedAsync(accountId, email);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task PublishAccountCreatedAsync_ShouldGenerateUniqueEventId()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var email = "test@example.com";
        AccountCreatedEvent? capturedEvent = null;

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.created",
            It.IsAny<AccountCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, AccountCreatedEvent, PublishOptions?, CancellationToken>((t, e, o, c) =>
            {
                capturedEvent = e;
            })
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _publisher.PublishAccountCreatedAsync(accountId, email);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.NotEqual(Guid.Empty, capturedEvent.EventId);
    }

    #endregion

    #region PublishAccountUpdatedAsync Tests

    [Fact]
    public async Task PublishAccountUpdatedAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var account = CreateTestAccount();
        var changedFields = new List<string> { "DisplayName", "Email" };

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _publisher.PublishAccountUpdatedAsync(account, changedFields);

        // Assert
        Assert.True(result);
        _mockMessageBus.Verify(m => m.PublishAsync(
            "account.updated",
            It.Is<AccountUpdatedEvent>(e =>
                e.AccountId == account.AccountId &&
                e.Email == account.Email &&
                e.ChangedFields.SequenceEqual(changedFields)),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAccountUpdatedAsync_ShouldIncludeAccountState()
    {
        // Arrange
        var account = CreateTestAccount();
        var changedFields = new List<string> { "DisplayName" };
        AccountUpdatedEvent? capturedEvent = null;

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, AccountUpdatedEvent, PublishOptions?, CancellationToken>((t, e, o, c) =>
            {
                capturedEvent = e;
            })
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _publisher.PublishAccountUpdatedAsync(account, changedFields);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(account.AccountId, capturedEvent.AccountId);
        Assert.Equal(account.Email, capturedEvent.Email);
        Assert.Equal(account.DisplayName, capturedEvent.DisplayName);
        Assert.Equal(account.EmailVerified, capturedEvent.EmailVerified);
    }

    [Fact]
    public async Task PublishAccountUpdatedAsync_WhenPublishFails_ShouldReturnFalse()
    {
        // Arrange
        var account = CreateTestAccount();
        var changedFields = new List<string> { "DisplayName" };

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Publish failed"));

        // Act
        var result = await _publisher.PublishAccountUpdatedAsync(account, changedFields);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region PublishAccountDeletedAsync Tests

    [Fact]
    public async Task PublishAccountDeletedAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var account = CreateTestAccount();
        var deletedReason = "User requested deletion";

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.deleted",
            It.IsAny<AccountDeletedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _publisher.PublishAccountDeletedAsync(account, deletedReason);

        // Assert
        Assert.True(result);
        _mockMessageBus.Verify(m => m.PublishAsync(
            "account.deleted",
            It.Is<AccountDeletedEvent>(e =>
                e.AccountId == account.AccountId &&
                e.DeletedReason == deletedReason),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAccountDeletedAsync_WithNullReason_ShouldStillPublish()
    {
        // Arrange
        var account = CreateTestAccount();

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.deleted",
            It.IsAny<AccountDeletedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _publisher.PublishAccountDeletedAsync(account, null);

        // Assert
        Assert.True(result);
        _mockMessageBus.Verify(m => m.PublishAsync(
            "account.deleted",
            It.Is<AccountDeletedEvent>(e =>
                e.AccountId == account.AccountId &&
                e.DeletedReason == null),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAccountDeletedAsync_ShouldIncludeAccountStateAtDeletion()
    {
        // Arrange
        var account = CreateTestAccount();
        AccountDeletedEvent? capturedEvent = null;

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.deleted",
            It.IsAny<AccountDeletedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, AccountDeletedEvent, PublishOptions?, CancellationToken>((t, e, o, c) =>
            {
                capturedEvent = e;
            })
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _publisher.PublishAccountDeletedAsync(account);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(account.AccountId, capturedEvent.AccountId);
        Assert.Equal(account.Email, capturedEvent.Email);
        Assert.Equal(account.DisplayName, capturedEvent.DisplayName);
        Assert.Equal(account.EmailVerified, capturedEvent.EmailVerified);
        Assert.Equal(account.CreatedAt, capturedEvent.CreatedAt);
    }

    [Fact]
    public async Task PublishAccountDeletedAsync_WhenPublishFails_ShouldReturnFalse()
    {
        // Arrange
        var account = CreateTestAccount();

        _mockMessageBus.Setup(m => m.PublishAsync(
            "account.deleted",
            It.IsAny<AccountDeletedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Publish failed"));

        // Act
        var result = await _publisher.PublishAccountDeletedAsync(account);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Helper Methods

    private static AccountResponse CreateTestAccount()
    {
        return new AccountResponse
        {
            AccountId = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            EmailVerified = true,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object> { { "key", "value" } }
        };
    }

    #endregion
}
