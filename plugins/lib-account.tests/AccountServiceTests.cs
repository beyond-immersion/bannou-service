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
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<AuthMethodInfo>>> _mockAuthMethodsStore;
    private readonly Mock<IJsonQueryableStateStore<AccountModel>> _mockJsonQueryableStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILockResponse> _mockLockResponse;

    private const string ACCOUNT_STATE_STORE = "account-statestore";

    public AccountServiceTests()
    {
        _mockLogger = new Mock<ILogger<AccountService>>();
        _configuration = new AccountServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockAccountStore = new Mock<IStateStore<AccountModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockAuthMethodsStore = new Mock<IStateStore<List<AuthMethodInfo>>>();
        _mockJsonQueryableStore = new Mock<IJsonQueryableStateStore<AccountModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLockResponse = new Mock<ILockResponse>();

        // Default lock behavior: always succeeds
        _mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLockResponse.Object);

        // Setup default factory returns
        _mockStateStoreFactory
            .Setup(f => f.GetStore<AccountModel>(ACCOUNT_STATE_STORE))
            .Returns(_mockAccountStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(ACCOUNT_STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<AuthMethodInfo>>(ACCOUNT_STATE_STORE))
            .Returns(_mockAuthMethodsStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<AccountModel>(ACCOUNT_STATE_STORE))
            .Returns(_mockJsonQueryableStore.Object);
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
        Assert.Equal(18, endpoints.Count); // 16 endpoints defined in account-api.yaml with x-permissions
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

        Assert.Equal(18, guardedEndpoints.Count);
        Assert.Equal(13, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "admin")));
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
        Assert.Equal(18, registrationEvent.Endpoints.Count);
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
            _mockLockProvider.Object);
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
            _mockLockProvider.Object);

        // Mock empty JSON query result
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

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
            _mockLockProvider.Object);

        // Mock empty JSON query result
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

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
            _mockLockProvider.Object);

        // Mock empty JSON query result
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

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
            _mockLockProvider.Object);

        // Mock empty JSON query result
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

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
            _mockLockProvider.Object);

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


        // Mock account save
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
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
            _mockLockProvider.Object);

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


        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
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
            _mockLockProvider.Object);

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


        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
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
            _mockLockProvider.Object);

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);


        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
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
            _mockLockProvider.Object);

        // Mock email index lookup
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("email-index-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);


        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
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
            _mockLockProvider.Object);

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


        // Mock saves
        _mockAccountStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<AccountModel>(),
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

    #region BatchGetAccounts Tests

    [Fact]
    public async Task BatchGetAccountsAsync_ReturnsFoundAccountsWithAuthMethods()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();

        var account1 = new AccountModel
        {
            AccountId = accountId1,
            Email = "user1@test.local",
            DisplayName = "User One",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var account2 = new AccountModel
        {
            AccountId = accountId2,
            Email = "user2@test.local",
            DisplayName = "User Two",
            Roles = new List<string> { "user", "admin" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account1);
        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account2);

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        var request = new BatchGetAccountsRequest
        {
            AccountIds = new List<Guid> { accountId1, accountId2 }
        };

        // Act
        var (status, response) = await service.BatchGetAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Accounts.Count);
        Assert.Empty(response.NotFound);
    }

    [Fact]
    public async Task BatchGetAccountsAsync_ReportsNotFoundIds()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var existingId = Guid.NewGuid();
        var missingId = Guid.NewGuid();

        var existingAccount = new AccountModel
        {
            AccountId = existingId,
            Email = "exists@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{existingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);
        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{missingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        var request = new BatchGetAccountsRequest
        {
            AccountIds = new List<Guid> { existingId, missingId }
        };

        // Act
        var (status, response) = await service.BatchGetAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Accounts);
        Assert.Equal(existingId, response.Accounts.First().AccountId);
        Assert.Single(response.NotFound);
        Assert.Contains(missingId, response.NotFound);
    }

    [Fact]
    public async Task BatchGetAccountsAsync_TreatsSoftDeletedAsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var deletedId = Guid.NewGuid();

        var deletedAccount = new AccountModel
        {
            AccountId = deletedId,
            Email = "deleted@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow // Soft-deleted
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{deletedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedAccount);

        var request = new BatchGetAccountsRequest
        {
            AccountIds = new List<Guid> { deletedId }
        };

        // Act
        var (status, response) = await service.BatchGetAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Accounts);
        Assert.Single(response.NotFound);
        Assert.Contains(deletedId, response.NotFound);
    }

    [Fact]
    public async Task BatchGetAccountsAsync_MixOfFoundAndNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{id1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = id1,
                Email = "found@test.local",
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{id2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);
        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{id3}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = id3,
                Email = "deleted@test.local",
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        var request = new BatchGetAccountsRequest
        {
            AccountIds = new List<Guid> { id1, id2, id3 }
        };

        // Act
        var (status, response) = await service.BatchGetAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Accounts);
        Assert.Equal(2, response.NotFound.Count);
        Assert.Contains(id2, response.NotFound);
        Assert.Contains(id3, response.NotFound);
    }

    #endregion

    #region CountAccounts Tests

    [Fact]
    public async Task CountAccountsAsync_NoFilters_ReturnsCount()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        _mockJsonQueryableStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(42L);

        var request = new CountAccountsRequest();

        // Act
        var (status, response) = await service.CountAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(42L, response.Count);
    }

    [Fact]
    public async Task CountAccountsAsync_WithEmailFilter_PassesContainsCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        List<QueryCondition>? capturedConditions = null;
        _mockJsonQueryableStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, CancellationToken>(
                (conditions, ct) => capturedConditions = conditions?.ToList())
            .ReturnsAsync(5L);

        var request = new CountAccountsRequest { Email = "test@" };

        // Act
        var (status, response) = await service.CountAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedConditions);
        Assert.Contains(capturedConditions, c =>
            c.Path == "$.Email" &&
            c.Operator == QueryOperator.Contains &&
            c.Value.ToString() == "test@");
    }

    [Fact]
    public async Task CountAccountsAsync_WithRoleFilter_PassesInCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        List<QueryCondition>? capturedConditions = null;
        _mockJsonQueryableStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, CancellationToken>(
                (conditions, ct) => capturedConditions = conditions?.ToList())
            .ReturnsAsync(3L);

        var request = new CountAccountsRequest { Role = "admin" };

        // Act
        var (status, response) = await service.CountAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3L, response.Count);
        Assert.NotNull(capturedConditions);
        Assert.Contains(capturedConditions, c =>
            c.Path == "$.Roles" &&
            c.Operator == QueryOperator.In &&
            c.Value.ToString() == "admin");
    }

    [Fact]
    public async Task CountAccountsAsync_WithVerifiedFilter_PassesEqualsCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        List<QueryCondition>? capturedConditions = null;
        _mockJsonQueryableStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, CancellationToken>(
                (conditions, ct) => capturedConditions = conditions?.ToList())
            .ReturnsAsync(10L);

        var request = new CountAccountsRequest { Verified = true };

        // Act
        var (status, response) = await service.CountAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedConditions);
        Assert.Contains(capturedConditions, c =>
            c.Path == "$.IsVerified" &&
            c.Operator == QueryOperator.Equals &&
            c.Value is bool b && b == true);
    }

    [Fact]
    public async Task CountAccountsAsync_ReturnsZeroWhenNoMatches()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        _mockJsonQueryableStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        var request = new CountAccountsRequest { Email = "nonexistent@nowhere.com" };

        // Act
        var (status, response) = await service.CountAccountsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0L, response.Count);
    }

    #endregion

    #region BulkUpdateRoles Tests

    [Fact]
    public async Task BulkUpdateRolesAsync_AddRoles_SucceedsForMultipleAccounts()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        SetupAccountForBulkUpdate(id1, new List<string> { "user" });
        SetupAccountForBulkUpdate(id2, new List<string> { "user" });

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { id1, id2 },
            AddRoles = new List<string> { "moderator" }
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Succeeded.Count);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_RemoveRoles_SucceedsForMultipleAccounts()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        SetupAccountForBulkUpdate(id1, new List<string> { "user", "moderator" });
        SetupAccountForBulkUpdate(id2, new List<string> { "user", "moderator", "admin" });

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { id1, id2 },
            RemoveRoles = new List<string> { "moderator" }
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Succeeded.Count);
        Assert.Empty(response.Failed);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_NotFoundAccounts_ReportedInFailed()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var existingId = Guid.NewGuid();
        var missingId = Guid.NewGuid();

        SetupAccountForBulkUpdate(existingId, new List<string> { "user" });

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{missingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { existingId, missingId },
            AddRoles = new List<string> { "admin" }
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Succeeded);
        Assert.Contains(existingId, response.Succeeded);
        Assert.Single(response.Failed);
        Assert.Equal(missingId, response.Failed.First().AccountId);
        Assert.Equal("Account not found", response.Failed.First().Error);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_PublishesEventForChangedAccounts()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        SetupAccountForBulkUpdate(accountId, new List<string> { "user" });

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { accountId },
            AddRoles = new List<string> { "admin" }
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

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
    public async Task BulkUpdateRolesAsync_NoEventForUnchangedAccounts()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        // Account already has the role we're trying to add
        SetupAccountForBulkUpdate(accountId, new List<string> { "user", "admin" });

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { accountId },
            AddRoles = new List<string> { "admin" } // Already has admin
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Succeeded); // No-op is success
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_ReturnsBadRequest_WhenNoRolesSpecified()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { Guid.NewGuid() }
            // Neither addRoles nor removeRoles specified
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_ConcurrentModification_ReportedInFailed()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "conflict@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        // TrySaveAsync returns null to simulate ETag conflict
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { accountId },
            AddRoles = new List<string> { "admin" }
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Succeeded);
        Assert.Single(response.Failed);
        Assert.Equal(accountId, response.Failed.First().AccountId);
        Assert.Equal("Concurrent modification", response.Failed.First().Error);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_SoftDeletedAccount_ReportedAsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        var deletedAccount = new AccountModel
        {
            AccountId = accountId,
            Email = "deleted@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((deletedAccount, "etag-0"));

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { accountId },
            AddRoles = new List<string> { "admin" }
        };

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Succeeded);
        Assert.Single(response.Failed);
        Assert.Equal("Account not found", response.Failed.First().Error);
    }

    [Fact]
    public async Task BulkUpdateRolesAsync_AddAndRemoveRoles_SimultaneousOperation()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        SetupAccountForBulkUpdate(accountId, new List<string> { "user", "moderator" });

        var request = new BulkUpdateRolesRequest
        {
            AccountIds = new List<Guid> { accountId },
            AddRoles = new List<string> { "admin" },
            RemoveRoles = new List<string> { "moderator" }
        };

        // Capture saved account to verify roles
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, CancellationToken>(
                (key, account, etag, ct) => savedAccount = account)
            .ReturnsAsync("etag-1");

        // Act
        var (status, response) = await service.BulkUpdateRolesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Succeeded);
        Assert.NotNull(savedAccount);
        Assert.Contains("user", savedAccount.Roles);
        Assert.Contains("admin", savedAccount.Roles);
        Assert.DoesNotContain("moderator", savedAccount.Roles);
    }

    /// <summary>
    /// Helper method to set up an account for bulk update tests with ETag-based concurrency.
    /// </summary>
    private void SetupAccountForBulkUpdate(Guid accountId, List<string> roles)
    {
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = $"{accountId}@test.local",
            Roles = roles,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
    }

    #endregion
}
