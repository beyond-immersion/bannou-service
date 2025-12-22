using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Unit tests for AccountsService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class AccountsServiceTests
{
    private readonly Mock<ILogger<AccountsService>> _mockLogger;
    private readonly AccountsServiceConfiguration _configuration;
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public AccountsServiceTests()
    {
        _mockLogger = new Mock<ILogger<AccountsService>>();
        _configuration = new AccountsServiceConfiguration();
        _mockDaprClient = new Mock<DaprClient>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockEventConsumer = new Mock<IEventConsumer>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        Assert.NotNull(service);
    }


    #region Permission Registration Tests

    [Fact]
    public void AccountsPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = AccountsPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        Assert.Equal(13, endpoints.Count); // 13 endpoints defined in accounts-api.yaml with x-permissions
    }

    [Fact]
    public void AccountsPermissionRegistration_GetEndpoints_ShouldContainListAccountsEndpoint()
    {
        // Act
        var endpoints = AccountsPermissionRegistration.GetEndpoints();

        // Assert - POST-only pattern: /accounts/list replaces GET /accounts
        var listEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/accounts/list" &&
            e.Method == BeyondImmersion.BannouService.Events.ServiceEndpointMethod.POST);

        Assert.NotNull(listEndpoint);
        Assert.NotNull(listEndpoint.Permissions);
    }

    [Fact]
    public void AccountsPermissionRegistration_GetEndpoints_ShouldRequireUserOrHigherRole()
    {
        // Act
        var endpoints = AccountsPermissionRegistration.GetEndpoints();

        // Assert - All account endpoints should require user or admin (no anonymous/service)
        var guardedEndpoints = endpoints.Where(e =>
            e.Permissions.All(p => p.Role == "user" || p.Role == "admin")).ToList();

        Assert.Equal(13, guardedEndpoints.Count);
        Assert.Equal(10, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "admin")));
        Assert.Equal(3, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "user")));
    }

    [Fact]
    public void AccountsPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Act
        var registrationEvent = AccountsPermissionRegistration.CreateRegistrationEvent();

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("accounts", registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.EventId);
        Assert.NotNull(registrationEvent.Endpoints);
        Assert.Equal(13, registrationEvent.Endpoints.Count);
    }

    [Fact]
    public void AccountsPermissionRegistration_ServiceId_ShouldBeAccounts()
    {
        // Assert
        Assert.Equal("accounts", AccountsPermissionRegistration.ServiceId);
    }

    #endregion

    #region Admin Role Assignment Tests

    private AccountsService CreateServiceWithConfiguration(
        string? adminEmails = null,
        string? adminEmailDomain = null)
    {
        // Create real configuration instance with test values
        var configuration = new AccountsServiceConfiguration
        {
            AdminEmails = adminEmails ?? string.Empty,
            AdminEmailDomain = adminEmailDomain ?? string.Empty
        };

        return new AccountsService(
            _mockLogger.Object,
            configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);
    }

    [Fact]
    public async Task CreateAccountAsync_AssignsAdminRole_WhenEmailMatchesDomain()
    {
        // Arrange
        var service = CreateServiceWithConfiguration(adminEmailDomain: "@admin.test.local");

        var request = new CreateAccountRequest
        {
            Email = "test@admin.test.local",
            DisplayName = "Test Admin",
            PasswordHash = "hashed_password"
        };

        // Mock GetStateAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "accounts-statestore",
                "accounts-list",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, AccountModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAccount = data);

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Roles);
        Assert.Contains("admin", response.Roles);

        // Verify the saved account also has admin role
        Assert.NotNull(savedAccount);
        Assert.Contains("admin", savedAccount.Roles);
    }

    [Fact]
    public async Task CreateAccountAsync_AssignsAdminRole_WhenEmailMatchesList()
    {
        // Arrange
        var service = CreateServiceWithConfiguration(adminEmails: "admin@example.com, superadmin@example.com");

        var request = new CreateAccountRequest
        {
            Email = "admin@example.com",
            DisplayName = "Admin User",
            PasswordHash = "hashed_password"
        };

        // Mock GetStateAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "accounts-statestore",
                "accounts-list",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, AccountModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAccount = data);

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Roles);
        Assert.Contains("admin", response.Roles);

        // Verify the saved account also has admin role
        Assert.NotNull(savedAccount);
        Assert.Contains("admin", savedAccount.Roles);
    }

    [Fact]
    public async Task UpdatePasswordHashAsync_PublishesAccountUpdatedEvent()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var accountModel = new AccountModel
        {
            AccountId = accountId.ToString(),
            Email = "user@test.local",
            PasswordHash = "old",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<AccountModel>(
                "accounts-statestore",
                $"account-{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountModel);

        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var (status, _) = await service.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = accountId,
            PasswordHash = "new-hash"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateVerificationStatusAsync_PublishesAccountUpdatedEvent()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var accountModel = new AccountModel
        {
            AccountId = accountId.ToString(),
            Email = "user@test.local",
            PasswordHash = "hash",
            IsVerified = false,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<AccountModel>(
                "accounts-statestore",
                $"account-{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountModel);

        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var (status, _) = await service.UpdateVerificationStatusAsync(new UpdateVerificationRequest
        {
            AccountId = accountId,
            EmailVerified = true
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAccountAsync_DoesNotAssignAdminRole_WhenEmailDoesNotMatch()
    {
        // Arrange - Set admin domain that doesn't match the test email
        var service = CreateServiceWithConfiguration(adminEmailDomain: "@admin.test.local");

        var request = new CreateAccountRequest
        {
            Email = "user@example.com",
            DisplayName = "Regular User",
            PasswordHash = "hashed_password"
        };

        // Mock GetStateAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "accounts-statestore",
                "accounts-list",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, AccountModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAccount = data);

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);

        // Should NOT have admin role
        if (response.Roles != null)
        {
            Assert.DoesNotContain("admin", response.Roles);
        }

        // Verify the saved account also does NOT have admin role
        Assert.NotNull(savedAccount);
        Assert.DoesNotContain("admin", savedAccount.Roles);
    }

    [Fact]
    public async Task CreateAccountAsync_CaseInsensitive_AdminEmailDomain()
    {
        // Arrange - Test case insensitivity
        var service = CreateServiceWithConfiguration(adminEmailDomain: "@ADMIN.TEST.LOCAL");

        var request = new CreateAccountRequest
        {
            Email = "Test@Admin.Test.Local", // Mixed case
            DisplayName = "Case Test Admin",
            PasswordHash = "hashed_password"
        };

        // Mock GetStateAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "accounts-statestore",
                "accounts-list",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, AccountModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAccount = data);

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Roles);
        Assert.Contains("admin", response.Roles);
    }

    [Fact]
    public async Task CreateAccountAsync_PreservesExistingRoles_WhenAdminRoleAdded()
    {
        // Arrange
        var service = CreateServiceWithConfiguration(adminEmailDomain: "@admin.test.local");

        var request = new CreateAccountRequest
        {
            Email = "test@admin.test.local",
            DisplayName = "Admin with Existing Roles",
            PasswordHash = "hashed_password",
            Roles = new List<string> { "moderator", "editor" }
        };

        // Mock GetStateAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "accounts-statestore",
                "accounts-list",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, AccountModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAccount = data);

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Roles);

        // Should have all roles including admin
        Assert.Contains("admin", response.Roles);
        Assert.Contains("moderator", response.Roles);
        Assert.Contains("editor", response.Roles);

        // Verify saved account
        Assert.NotNull(savedAccount);
        Assert.Equal(3, savedAccount.Roles.Count);
    }

    [Fact]
    public async Task CreateAccountAsync_DoesNotDuplicateAdminRole_WhenAlreadyInRequest()
    {
        // Arrange
        var service = CreateServiceWithConfiguration(adminEmailDomain: "@admin.test.local");

        var request = new CreateAccountRequest
        {
            Email = "test@admin.test.local",
            DisplayName = "Pre-Admin User",
            PasswordHash = "hashed_password",
            Roles = new List<string> { "admin" } // Already has admin
        };

        // Mock GetStateAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "accounts-statestore",
                "accounts-list",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                "accounts-statestore",
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, AccountModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAccount = data);

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Roles);

        // Should have admin only once
        var adminCount = response.Roles.Count(r => r == "admin");
        Assert.Equal(1, adminCount);

        // Verify saved account
        Assert.NotNull(savedAccount);
        Assert.Equal(1, savedAccount.Roles.Count(r => r == "admin"));
    }

    #endregion

    #region Constructor Validation Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AccountsService(
            null!,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AccountsService(
            _mockLogger.Object,
            null!,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AccountsService(
            _mockLogger.Object,
            _configuration,
            null!,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object));
    }

    #endregion

    #region ListAccounts Tests

    [Fact]
    public async Task ListAccountsAsync_WithDefaultParameters_ShouldReturnOK()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Accounts);
    }

    [Fact]
    public async Task ListAccountsAsync_WithNegativePage_ShouldDefaultToPage1()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest { Page = -5 });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(1, response.Page);
    }

    [Fact]
    public async Task ListAccountsAsync_WithNegativePageSize_ShouldDefaultTo20()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest { PageSize = -10 });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(20, response.PageSize);
    }

    [Fact]
    public async Task ListAccountsAsync_WithZeroPage_ShouldDefaultToPage1()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest { Page = 0 });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(1, response.Page);
    }

    #endregion

    #region CreateAccount Tests

    [Fact]
    public async Task CreateAccountAsync_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "newuser@example.com",
            DisplayName = "New User",
            PasswordHash = "hashed_password"
        };

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.Equal(request.Email, response.Email);
        Assert.Equal(request.DisplayName, response.DisplayName);
        Assert.NotEqual(Guid.Empty, response.AccountId);
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldStoreEmailVerifiedStatus()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "verified@example.com",
            DisplayName = "Verified User",
            PasswordHash = "hashed_password",
            EmailVerified = true
        };

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.True(response.EmailVerified);
    }

    [Fact]
    public async Task CreateAccountAsync_WithoutEmailVerified_ShouldDefaultToFalse()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "unverified@example.com",
            DisplayName = "Unverified User",
            PasswordHash = "hashed_password"
            // EmailVerified not set
        };

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.False(response.EmailVerified);
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldGenerateUniqueAccountIds()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        var accountIds = new List<Guid>();

        for (int i = 0; i < 5; i++)
        {
            var request = new CreateAccountRequest
            {
                Email = $"user{i}@example.com",
                DisplayName = $"User {i}",
                PasswordHash = "hashed_password"
            };

            var (statusCode, response) = await service.CreateAccountAsync(request);

            Assert.Equal(StatusCodes.Created, statusCode);
            Assert.NotNull(response);
            accountIds.Add(response.AccountId);
        }

        // Assert - All IDs should be unique
        Assert.Equal(5, accountIds.Distinct().Count());
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldSetCreatedAtAndUpdatedAt()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        // Truncate to second precision to match Unix timestamp storage
        var beforeCreation = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var request = new CreateAccountRequest
        {
            Email = "timestamps@example.com",
            DisplayName = "Timestamp Test",
            PasswordHash = "hashed_password"
        };

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Truncate to second precision to match Unix timestamp storage
        var afterCreation = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.True(response.CreatedAt >= beforeCreation, $"CreatedAt ({response.CreatedAt}) should be >= beforeCreation ({beforeCreation})");
        Assert.True(response.CreatedAt <= afterCreation, $"CreatedAt ({response.CreatedAt}) should be <= afterCreation ({afterCreation})");
        Assert.True(response.UpdatedAt >= beforeCreation, $"UpdatedAt ({response.UpdatedAt}) should be >= beforeCreation ({beforeCreation})");
        Assert.True(response.UpdatedAt <= afterCreation, $"UpdatedAt ({response.UpdatedAt}) should be <= afterCreation ({afterCreation})");
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldInitializeEmptyAuthMethods()
    {
        // Arrange
        var service = new AccountsService(
            _mockLogger.Object,
            _configuration,
            _mockDaprClient.Object,
            _mockErrorEventEmitter.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "authmethods@example.com",
            DisplayName = "Auth Methods Test",
            PasswordHash = "hashed_password"
        };

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.AuthMethods);
        Assert.Empty(response.AuthMethods);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void AccountsServiceConfiguration_DefaultValues_ShouldNotBeNull()
    {
        // Arrange
        var config = new AccountsServiceConfiguration();

        // Assert - properties should exist (may be empty strings)
        Assert.NotNull(config);
    }

    [Fact]
    public void AccountsServiceConfiguration_AdminEmails_ShouldBeParsable()
    {
        // Arrange
        var config = new AccountsServiceConfiguration
        {
            AdminEmails = "admin1@example.com, admin2@example.com, admin3@example.com"
        };

        // Act - simulate the parsing logic used by the service
        var emails = config.AdminEmails.Split(',')
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();

        // Assert
        Assert.Equal(3, emails.Count);
        Assert.Contains("admin1@example.com", emails);
        Assert.Contains("admin2@example.com", emails);
        Assert.Contains("admin3@example.com", emails);
    }

    [Fact]
    public void AccountsServiceConfiguration_AdminEmailDomain_ShouldBeTrimmed()
    {
        // Arrange
        var config = new AccountsServiceConfiguration
        {
            AdminEmailDomain = "  @admin.example.com  "
        };

        // Act
        var trimmedDomain = config.AdminEmailDomain.Trim();

        // Assert
        Assert.Equal("@admin.example.com", trimmedDomain);
    }

    #endregion
}
