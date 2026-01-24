using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Account.Tests;

/// <summary>
/// Unit tests for AccountService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class AccountServiceTests
{
    private readonly Mock<ILogger<AccountService>> _mockLogger;
    private readonly AccountServiceConfiguration _configuration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<AccountModel>> _mockAccountStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<AuthMethodInfo>>> _mockAuthMethodsStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string ACCOUNT_STATE_STORE = "account-statestore";

    public AccountServiceTests()
    {
        _mockLogger = new Mock<ILogger<AccountService>>();
        _configuration = new AccountServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockAccountStore = new Mock<IStateStore<AccountModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockAuthMethodsStore = new Mock<IStateStore<List<AuthMethodInfo>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup default factory returns
        _mockStateStoreFactory
            .Setup(f => f.GetStore<AccountModel>(ACCOUNT_STATE_STORE))
            .Returns(_mockAccountStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<string>>(ACCOUNT_STATE_STORE))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(ACCOUNT_STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<AuthMethodInfo>>(ACCOUNT_STATE_STORE))
            .Returns(_mockAuthMethodsStore.Object);
    }

    #region Permission Registration Tests

    [Fact]
    public void AccountPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = AccountPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        Assert.Equal(13, endpoints.Count); // 13 endpoints defined in account-api.yaml with x-permissions
    }

    [Fact]
    public void AccountPermissionRegistration_GetEndpoints_ShouldContainListAccountEndpoint()
    {
        // Act
        var endpoints = AccountPermissionRegistration.GetEndpoints();

        // Assert - POST-only pattern: /account/list replaces GET /account
        var listEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/account/list" &&
            e.Method == BeyondImmersion.BannouService.Events.ServiceEndpointMethod.POST);

        Assert.NotNull(listEndpoint);
        Assert.NotNull(listEndpoint.Permissions);
    }

    [Fact]
    public void AccountPermissionRegistration_GetEndpoints_ShouldRequireUserOrHigherRole()
    {
        // Act
        var endpoints = AccountPermissionRegistration.GetEndpoints();

        // Assert - All account endpoints should require user or admin (no anonymous/service)
        var guardedEndpoints = endpoints.Where(e =>
            e.Permissions.All(p => p.Role == "user" || p.Role == "admin")).ToList();

        Assert.Equal(13, guardedEndpoints.Count);
        Assert.Equal(10, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "admin")));
        Assert.Equal(3, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "user")));
    }

    [Fact]
    public void AccountPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var registrationEvent = AccountPermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("account", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.Endpoints);
        Assert.Equal(13, registrationEvent.Endpoints.Count);
    }

    [Fact]
    public void AccountPermissionRegistration_ServiceId_ShouldBeAccount()
    {
        // Assert
        Assert.Equal("account", AccountPermissionRegistration.ServiceId);
    }

    #endregion

    #region Admin Role Assignment Tests

    private AccountService CreateServiceWithConfiguration(
        string? adminEmails = null,
        string? adminEmailDomain = null)
    {
        // Create real configuration instance with test values
        var configuration = new AccountServiceConfiguration
        {
            AdminEmails = adminEmails,
            AdminEmailDomain = adminEmailDomain
        };

        return new AccountService(
            _mockLogger.Object,
            configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
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

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list (used by AddAccountToIndexAsync)
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedAccount = data)
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
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

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedAccount = data)
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
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
            AccountId = accountId,
            Email = "user@test.local",
            PasswordHash = "old",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync(
                $"account-{accountId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((accountModel, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var status = await service.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = accountId,
            PasswordHash = "new-hash"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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
            AccountId = accountId,
            Email = "user@test.local",
            PasswordHash = "hash",
            IsVerified = false,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync(
                $"account-{accountId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((accountModel, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var status = await service.UpdateVerificationStatusAsync(new UpdateVerificationRequest
        {
            AccountId = accountId,
            EmailVerified = true
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedAccount = data)
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
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

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedAccount = data)
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
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

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedAccount = data)
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
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

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Track what's saved
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("account-")),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedAccount = data)
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
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

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void AccountService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<AccountService>();

    #endregion

    #region ListAccounts Tests

    [Fact]
    public async Task ListAccountsAsync_WithDefaultParameters_ShouldReturnOK()
    {
        // Arrange
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        // Mock empty accounts list
        _mockListStore
            .Setup(s => s.GetAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

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
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        // Mock empty accounts list
        _mockListStore
            .Setup(s => s.GetAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

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
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        // Mock empty accounts list
        _mockListStore
            .Setup(s => s.GetAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

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
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        // Mock empty accounts list
        _mockListStore
            .Setup(s => s.GetAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

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
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "newuser@example.com",
            DisplayName = "New User",
            PasswordHash = "hashed_password"
        };

        // Mock email index lookup (returns null - email doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Mock account save
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Mock list save
        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(request.Email, response.Email);
        Assert.Equal(request.DisplayName, response.DisplayName);
        Assert.NotEqual(Guid.Empty, response.AccountId);
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldStoreEmailVerifiedStatus()
    {
        // Arrange
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "verified@example.com",
            DisplayName = "Verified User",
            PasswordHash = "hashed_password",
            EmailVerified = true
        };

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.EmailVerified);
    }

    [Fact]
    public async Task CreateAccountAsync_WithoutEmailVerified_ShouldDefaultToFalse()
    {
        // Arrange
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "unverified@example.com",
            DisplayName = "Unverified User",
            PasswordHash = "hashed_password"
            // EmailVerified not set
        };

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.EmailVerified);
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldGenerateUniqueAccountIds()
    {
        // Arrange
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

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

            Assert.Equal(StatusCodes.OK, statusCode);
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
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

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
        Assert.Equal(StatusCodes.OK, statusCode);
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
        var service = new AccountService(
            _mockLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object);

        var request = new CreateAccountRequest
        {
            Email = "authmethods@example.com",
            DisplayName = "Auth Methods Test",
            PasswordHash = "hashed_password"
        };

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock GetWithETagAsync for accounts-list
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "accounts-list",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockListStore
            .Setup(s => s.SaveAsync(
                "accounts-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.AuthMethods);
        Assert.Empty(response.AuthMethods);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void AccountServiceConfiguration_DefaultValues_ShouldNotBeNull()
    {
        // Arrange
        var config = new AccountServiceConfiguration();

        // Assert - properties should exist (may be empty strings)
        Assert.NotNull(config);
    }

    [Fact]
    public void AccountServiceConfiguration_AdminEmails_ShouldBeParsable()
    {
        // Arrange
        var config = new AccountServiceConfiguration
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
    public void AccountServiceConfiguration_AdminEmailDomain_ShouldBeTrimmed()
    {
        // Arrange
        var config = new AccountServiceConfiguration
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
