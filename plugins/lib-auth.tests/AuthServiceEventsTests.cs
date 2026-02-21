using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for AuthService event handlers (AuthServiceEvents.cs).
/// Tests HandleAccountDeletedAsync and HandleAccountUpdatedAsync behavior.
/// </summary>
public class AuthServiceEventsTests
{
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly Mock<IAccountClient> _mockAccountClient;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<AuthService.PasswordResetData>> _mockPasswordResetStore;
    private readonly Mock<IStateStore<SessionDataModel>> _mockSessionStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<IOAuthProviderService> _mockOAuthService;
    private readonly Mock<IEdgeRevocationService> _mockEdgeRevocationService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ICacheableStateStore<SessionDataModel>> _mockCacheableStore;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IMfaService> _mockMfaService;

    public AuthServiceEventsTests()
    {
        // Configure JWT settings in Program.Configuration (used by auth services)
        TestConfigurationHelper.ConfigureJwt();

        _mockLogger = new Mock<ILogger<AuthService>>();
        _configuration = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = true,
            DiscordClientId = "test-discord-client-id",
            DiscordClientSecret = "test-discord-client-secret",
            DiscordRedirectUri = "http://localhost:5012/auth/oauth/discord/callback",
            GoogleClientId = "test-google-client-id",
            GoogleClientSecret = "test-google-client-secret",
            GoogleRedirectUri = "http://localhost:5012/auth/oauth/google/callback",
            TwitchClientId = "test-twitch-client-id",
            TwitchClientSecret = "test-twitch-client-secret",
            TwitchRedirectUri = "http://localhost:5012/auth/oauth/twitch/callback",
            SteamApiKey = "test-steam-api-key",
            SteamAppId = "123456"
        };
        _appConfiguration = new AppConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            ServiceDomain = "localhost"
        };
        _mockAccountClient = new Mock<IAccountClient>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockPasswordResetStore = new Mock<IStateStore<AuthService.PasswordResetData>>();
        _mockSessionStore = new Mock<IStateStore<SessionDataModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockTokenService = new Mock<ITokenService>();
        _mockSessionService = new Mock<ISessionService>();
        _mockOAuthService = new Mock<IOAuthProviderService>();
        _mockEdgeRevocationService = new Mock<IEdgeRevocationService>();
        _mockEmailService = new Mock<IEmailService>();
        _mockCacheableStore = new Mock<ICacheableStateStore<SessionDataModel>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockMfaService = new Mock<IMfaService>();

        // Setup default behavior for edge revocation service (disabled by default)
        _mockEdgeRevocationService.Setup(e => e.IsEnabled).Returns(false);

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<AuthService.PasswordResetData>(It.IsAny<string>()))
            .Returns(_mockPasswordResetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SessionDataModel>(It.IsAny<string>()))
            .Returns(_mockSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(It.IsAny<string>()))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStoreAsync<SessionDataModel>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockCacheableStore.Object);

        // Setup default behavior for state stores
        _mockPasswordResetStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<AuthService.PasswordResetData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockSessionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SessionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockListStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup default behavior for message bus
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Helper method to create an AuthService with all required dependencies.
    /// </summary>
    private AuthService CreateAuthService()
    {
        return new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);
    }

    #region HandleAccountDeletedAsync Tests

    /// <summary>
    /// Verifies that HandleAccountDeletedAsync invalidates all sessions for the account
    /// and cleans up OAuth links via the OAuth provider service.
    /// </summary>
    [Fact]
    public async Task HandleAccountDeletedAsync_InvalidatesSessionsAndCleansUpOAuthLinks()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var evt = new AccountDeletedEvent
        {
            AccountId = accountId,
            Email = "deleted-user@example.com",
            DisplayName = "DeletedUser",
            EmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        _mockSessionService.Setup(s => s.InvalidateAllSessionsForAccountAsync(
            accountId,
            SessionInvalidatedEventReason.AccountDeleted,
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockOAuthService.Setup(o => o.CleanupOAuthLinksForAccountAsync(
            accountId,
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateAuthService();

        // Act
        await service.HandleAccountDeletedAsync(evt);

        // Assert - verify session invalidation was called with AccountDeleted reason
        _mockSessionService.Verify(s => s.InvalidateAllSessionsForAccountAsync(
            accountId,
            SessionInvalidatedEventReason.AccountDeleted,
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - verify OAuth cleanup was called
        _mockOAuthService.Verify(o => o.CleanupOAuthLinksForAccountAsync(
            accountId,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region HandleAccountUpdatedAsync Tests

    /// <summary>
    /// Verifies that HandleAccountUpdatedAsync propagates role changes when the changedFields
    /// list includes "roles". Should load session keys, update each session, and publish events.
    /// </summary>
    [Fact]
    public async Task HandleAccountUpdatedAsync_PropagatesRoleChanges_WhenRolesChanged()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey = "abc123def456";
        var sessionId = Guid.NewGuid();
        var newRoles = new List<string> { "user", "moderator" };

        var evt = new AccountUpdatedEvent
        {
            AccountId = accountId,
            Email = "updated-user@example.com",
            DisplayName = "UpdatedUser",
            EmailVerified = true,
            ChangedFields = new List<string> { "roles", "displayName" },
            Roles = newRoles,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-15)
        };

        // Mock the list store to return session keys for the account
        _mockListStore.Setup(s => s.GetAsync(
            $"account-sessions:{accountId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionKey });

        // Mock the session store to return session data
        var sessionData = new SessionDataModel
        {
            AccountId = accountId,
            SessionId = sessionId,
            Email = "updated-user@example.com",
            DisplayName = "UpdatedUser",
            Roles = new List<string> { "user" },
            Authorizations = new List<string> { "read", "write" },
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockSessionStore.Setup(s => s.GetAsync(
            $"session:{sessionKey}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionData);

        _mockSessionService.Setup(s => s.PublishSessionUpdatedEventAsync(
            accountId,
            sessionId,
            It.IsAny<List<string>>(),
            It.IsAny<List<string>>(),
            SessionUpdatedEventReason.RoleChanged,
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateAuthService();

        // Act
        await service.HandleAccountUpdatedAsync(evt);

        // Assert - verify session was loaded
        _mockSessionStore.Verify(s => s.GetAsync(
            $"session:{sessionKey}",
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - verify session was saved with updated roles
        _mockSessionStore.Verify(s => s.SaveAsync(
            $"session:{sessionKey}",
            It.Is<SessionDataModel>(sd => sd.Roles.Contains("moderator") && sd.Roles.Contains("user")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - verify session updated event was published
        _mockSessionService.Verify(s => s.PublishSessionUpdatedEventAsync(
            accountId,
            sessionId,
            It.Is<List<string>>(r => r.Contains("user") && r.Contains("moderator")),
            It.IsAny<List<string>>(),
            SessionUpdatedEventReason.RoleChanged,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that HandleAccountUpdatedAsync does NOT propagate role changes when the
    /// changedFields list does not include "roles". No session lookups or updates should occur.
    /// </summary>
    [Fact]
    public async Task HandleAccountUpdatedAsync_SkipsRolePropagation_WhenRolesNotChanged()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var evt = new AccountUpdatedEvent
        {
            AccountId = accountId,
            Email = "user@example.com",
            DisplayName = "SomeUser",
            EmailVerified = true,
            ChangedFields = new List<string> { "displayName", "email" },
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        var service = CreateAuthService();

        // Act
        await service.HandleAccountUpdatedAsync(evt);

        // Assert - verify no session lookups occurred (role propagation was skipped)
        _mockListStore.Verify(s => s.GetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        _mockSessionStore.Verify(s => s.GetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        _mockSessionStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<SessionDataModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        _mockSessionService.Verify(s => s.PublishSessionUpdatedEventAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<List<string>>(),
            It.IsAny<List<string>>(),
            It.IsAny<SessionUpdatedEventReason>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that HandleAccountUpdatedAsync uses an empty roles list when the Roles
    /// property on the event is null but changedFields contains "roles".
    /// This ensures roles are cleared (not left stale) when the event indicates roles changed
    /// but provides no role data.
    /// </summary>
    [Fact]
    public async Task HandleAccountUpdatedAsync_UsesEmptyRoles_WhenRolesNull()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey = "session-key-xyz";
        var sessionId = Guid.NewGuid();

        var evt = new AccountUpdatedEvent
        {
            AccountId = accountId,
            Email = "nullroles@example.com",
            DisplayName = "NullRolesUser",
            EmailVerified = false,
            ChangedFields = new List<string> { "roles" },
            Roles = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };

        // Mock the list store to return session keys for the account
        _mockListStore.Setup(s => s.GetAsync(
            $"account-sessions:{accountId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionKey });

        // Mock the session store to return existing session data with roles
        var sessionData = new SessionDataModel
        {
            AccountId = accountId,
            SessionId = sessionId,
            Email = "nullroles@example.com",
            Roles = new List<string> { "user", "admin" },
            Authorizations = new List<string> { "read" },
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2)
        };

        _mockSessionStore.Setup(s => s.GetAsync(
            $"session:{sessionKey}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionData);

        _mockSessionService.Setup(s => s.PublishSessionUpdatedEventAsync(
            accountId,
            sessionId,
            It.IsAny<List<string>>(),
            It.IsAny<List<string>>(),
            SessionUpdatedEventReason.RoleChanged,
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateAuthService();

        // Act
        await service.HandleAccountUpdatedAsync(evt);

        // Assert - verify session was saved with empty roles list (not the old roles)
        _mockSessionStore.Verify(s => s.SaveAsync(
            $"session:{sessionKey}",
            It.Is<SessionDataModel>(sd => sd.Roles.Count == 0),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - verify session updated event was published with empty roles
        _mockSessionService.Verify(s => s.PublishSessionUpdatedEventAsync(
            accountId,
            sessionId,
            It.Is<List<string>>(r => r.Count == 0),
            It.IsAny<List<string>>(),
            SessionUpdatedEventReason.RoleChanged,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
