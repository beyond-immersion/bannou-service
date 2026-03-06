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
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

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
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

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
            e.Method == ServiceEndpointMethod.Post);

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
        Assert.Equal(14, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "admin")));
        Assert.Equal(4, guardedEndpoints.Count(e => e.Permissions.Any(p => p.Role == "user")));
    }

    [Fact]
    public void AccountPermissionRegistration_BuildPermissionMatrix_ShouldBeValid()
    {
        PermissionMatrixValidator.ValidatePermissionMatrix(
            AccountPermissionRegistration.ServiceId,
            AccountPermissionRegistration.ServiceVersion,
            AccountPermissionRegistration.BuildPermissionMatrix());
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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);
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
                It.IsAny<StateOptions?>(),
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
                It.IsAny<StateOptions?>(),
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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

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
                It.IsAny<StateOptions?>(),
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
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
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
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
    }

    #endregion

    #region GetAccount Tests

    [Fact]
    public async Task GetAccountAsync_Success_ReturnsAccountWithAuthMethods()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            DisplayName = "Test User",
            IsVerified = true,
            Roles = new List<string> { "user" },
            MfaEnabled = true,
            MfaSecret = "secret",
            MfaRecoveryCodes = new List<string> { "code1", "code2" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var authMethods = new List<AuthMethodInfo>
        {
            new AuthMethodInfo
            {
                MethodId = Guid.NewGuid(),
                Provider = AuthProvider.Discord,
                ExternalId = "discord-123",
                LinkedAt = DateTimeOffset.UtcNow
            }
        };

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(authMethods);

        // Act
        var (status, response) = await service.GetAccountAsync(new GetAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(accountId, response.AccountId);
        Assert.Equal("user@test.local", response.Email);
        Assert.Equal("Test User", response.DisplayName);
        Assert.True(response.EmailVerified);
        Assert.True(response.MfaEnabled);
        Assert.Equal("secret", response.MfaSecret);
        Assert.NotNull(response.MfaRecoveryCodes);
        Assert.Equal(2, response.MfaRecoveryCodes.Count);
        Assert.Single(response.AuthMethods);
        Assert.Equal(AuthProvider.Discord, response.AuthMethods.First().Provider);
    }

    [Fact]
    public async Task GetAccountAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        // Act
        var (status, response) = await service.GetAccountAsync(new GetAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAccountAsync_SoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "deleted@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var (status, response) = await service.GetAccountAsync(new GetAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateAccount Tests

    [Fact]
    public async Task UpdateAccountAsync_Success_UpdatesDisplayName()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            DisplayName = "Old Name",
            Roles = new List<string> { "user" },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        var request = new UpdateAccountRequest
        {
            AccountId = accountId,
            DisplayName = "New Name"
        };

        // Act
        var (status, response) = await service.UpdateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("New Name", response.DisplayName);

        // Verify event published with displayName changed
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.Is<AccountUpdatedEvent>(e => e.ChangedFields.Contains("displayName")),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAccountAsync_UpdatesRoles_WithAutoAnonymousManagement()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (key, model, etag, ct) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Send roles with "anonymous" plus "admin" -- auto-manage should remove anonymous
        var request = new UpdateAccountRequest
        {
            AccountId = accountId,
            Roles = new List<string> { "anonymous", "admin" }
        };

        // Act
        var (status, response) = await service.UpdateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedAccount);
        Assert.Contains("admin", savedAccount.Roles);
        Assert.DoesNotContain("anonymous", savedAccount.Roles);
    }

    [Fact]
    public async Task UpdateAccountAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var (status, response) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            DisplayName = "New Name"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateAccountAsync_SoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
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
            .ReturnsAsync((account, "etag-0"));

        // Act
        var (status, response) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            DisplayName = "New Name"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateAccountAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            DisplayName = "Old Name",
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            DisplayName = "New Name"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateAccountAsync_NoChanges_DoesNotPublishEvent()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            DisplayName = "Same Name",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        // Still needs to save (UpdatedAt changes) but no event should be published
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act - update with same display name
        var (status, response) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            DisplayName = "Same Name"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAccountAsync_UpdatesMetadata_PublishesEvent()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = new Dictionary<string, object> { { "key1", "oldValue" } },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // New metadata as a dictionary (not JsonElement)
        var newMetadata = new Dictionary<string, object> { { "key1", "newValue" } };
        var request = new UpdateAccountRequest
        {
            AccountId = accountId,
            Metadata = newMetadata
        };

        // Act
        var (status, response) = await service.UpdateAccountAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.Is<AccountUpdatedEvent>(e => e.ChangedFields.Contains("metadata")),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteAccount Tests

    [Fact]
    public async Task DeleteAccountAsync_Success_SoftDeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (key, model, etag, ct) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo
                {
                    MethodId = Guid.NewGuid(),
                    Provider = AuthProvider.Discord,
                    ExternalId = "ext-123",
                    LinkedAt = DateTimeOffset.UtcNow
                }
            });

        // Act
        var status = await service.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify soft delete was applied
        Assert.NotNull(savedAccount);
        Assert.NotNull(savedAccount.DeletedAt);

        // Verify email index removed
        _mockStringStore.Verify(s => s.DeleteAsync(
            "email-index-user@test.local",
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify provider index removed
        _mockStringStore.Verify(s => s.DeleteAsync(
            "provider-index-Discord:ext-123",
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify auth methods deleted
        _mockAuthMethodsStore.Verify(s => s.DeleteAsync(
            $"auth-methods-{accountId}",
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify deleted event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.deleted",
            It.IsAny<AccountDeletedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var status = await service.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteAccountAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var status = await service.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task DeleteAccountAsync_NoEmail_SkipsEmailIndexDeletion()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = null, // No email (OAuth/Steam)
            Roles = new List<string> { "user" },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<AuthMethodInfo>?)null);

        // Act
        var status = await service.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify email index delete was NOT called
        _mockStringStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.StartsWith("email-index-")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccountAsync_NoAuthMethods_SkipsProviderCleanup()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<AuthMethodInfo>?)null);

        // Act
        var status = await service.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify no provider index deletions
        _mockStringStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.StartsWith("provider-index-")),
            It.IsAny<CancellationToken>()), Times.Never);

        // Verify auth methods store was NOT asked to delete (nothing to delete)
        _mockAuthMethodsStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetAccountByEmail Tests

    [Fact]
    public async Task GetAccountByEmailAsync_Success_ReturnsAccount()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-user@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountId.ToString());

        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            DisplayName = "Test User",
            PasswordHash = "hashed-pw",
            IsVerified = true,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act
        var (status, response) = await service.GetAccountByEmailAsync(
            new GetAccountByEmailRequest { Email = "User@Test.Local" }); // Test case insensitivity

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(accountId, response.AccountId);
        Assert.Equal("hashed-pw", response.PasswordHash); // Email lookup includes password hash
    }

    [Fact]
    public async Task GetAccountByEmailAsync_NoIndex_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.GetAccountByEmailAsync(
            new GetAccountByEmailRequest { Email = "unknown@test.local" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAccountByEmailAsync_IndexExistsButAccountMissing_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-orphan@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountId.ToString());

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        // Act
        var (status, response) = await service.GetAccountByEmailAsync(
            new GetAccountByEmailRequest { Email = "orphan@test.local" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAccountByEmailAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-deleted@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountId.ToString());

        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "deleted@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var (status, response) = await service.GetAccountByEmailAsync(
            new GetAccountByEmailRequest { Email = "deleted@test.local" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetAccountByProvider Tests

    [Fact]
    public async Task GetAccountByProviderAsync_Success_ReturnsAccount()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Discord:discord-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountId.ToString());

        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act
        var (status, response) = await service.GetAccountByProviderAsync(
            new GetAccountByProviderRequest { Provider = OAuthProvider.Discord, ExternalId = "discord-123" });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(accountId, response.AccountId);
    }

    [Fact]
    public async Task GetAccountByProviderAsync_NoIndex_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.GetAccountByProviderAsync(
            new GetAccountByProviderRequest { Provider = OAuthProvider.Google, ExternalId = "unknown" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAccountByProviderAsync_IndexExistsButAccountMissing_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Steam:steam-999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountId.ToString());

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        // Act
        var (status, response) = await service.GetAccountByProviderAsync(
            new GetAccountByProviderRequest { Provider = OAuthProvider.Steam, ExternalId = "steam-999" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAccountByProviderAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Twitch:twitch-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountId.ToString());

        var account = new AccountModel
        {
            AccountId = accountId,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var (status, response) = await service.GetAccountByProviderAsync(
            new GetAccountByProviderRequest { Provider = OAuthProvider.Twitch, ExternalId = "twitch-456" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetAuthMethods Tests

    [Fact]
    public async Task GetAuthMethodsAsync_Success_ReturnsAuthMethods()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var authMethods = new List<AuthMethodInfo>
        {
            new AuthMethodInfo
            {
                MethodId = Guid.NewGuid(),
                Provider = AuthProvider.Discord,
                ExternalId = "disc-1",
                LinkedAt = DateTimeOffset.UtcNow
            },
            new AuthMethodInfo
            {
                MethodId = Guid.NewGuid(),
                Provider = AuthProvider.Google,
                ExternalId = "goog-2",
                LinkedAt = DateTimeOffset.UtcNow
            }
        };

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(authMethods);

        // Act
        var (status, response) = await service.GetAuthMethodsAsync(
            new GetAuthMethodsRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.AuthMethods.Count);
    }

    [Fact]
    public async Task GetAuthMethodsAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        // Act
        var (status, response) = await service.GetAuthMethodsAsync(
            new GetAuthMethodsRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAuthMethodsAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeletedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var (status, response) = await service.GetAuthMethodsAsync(
            new GetAuthMethodsRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAuthMethodsAsync_NoMethodsStored_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<AuthMethodInfo>?)null);

        // Act
        var (status, response) = await service.GetAuthMethodsAsync(
            new GetAuthMethodsRequest { AccountId = accountId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.AuthMethods);
    }

    #endregion

    #region AddAuthMethod Tests

    [Fact]
    public async Task AddAuthMethodAsync_Success_CreatesMethodAndIndex()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>(), "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.TrySaveAsync(
                $"auth-methods-{accountId}",
                It.IsAny<List<AuthMethodInfo>>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // No existing provider index
        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Discord:disc-external-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Discord,
            ExternalId = "disc-external-1"
        };

        // Act
        var (status, response) = await service.AddAuthMethodAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(OAuthProvider.Discord, response.Provider);
        Assert.Equal("disc-external-1", response.ExternalId);
        Assert.NotEqual(Guid.Empty, response.MethodId);

        // Verify provider index created
        _mockStringStore.Verify(s => s.SaveAsync(
            "provider-index-Discord:disc-external-1",
            accountId.ToString(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAuthMethodAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Discord,
            ExternalId = "ext-1"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddAuthMethodAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Discord,
            ExternalId = "ext-1"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddAuthMethodAsync_EmptyExternalId_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>(), "etag-0"));

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Google,
            ExternalId = ""
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddAuthMethodAsync_DuplicateOnSameAccount_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Already has this provider+externalId
        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>
            {
                new AuthMethodInfo
                {
                    MethodId = Guid.NewGuid(),
                    Provider = AuthProvider.Discord,
                    ExternalId = "disc-123",
                    LinkedAt = DateTimeOffset.UtcNow
                }
            }, "etag-0"));

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Discord,
            ExternalId = "disc-123"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddAuthMethodAsync_OwnedByAnotherActiveAccount_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>(), "etag-0"));

        // Provider index points to another account
        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Google:goog-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherAccountId.ToString());

        // Other account is active (not deleted)
        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{otherAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = otherAccountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Google,
            ExternalId = "goog-456"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddAuthMethodAsync_OrphanedIndexFromDeletedAccount_OverwritesSuccessfully()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var deletedAccountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Email = "user@test.local",
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>(), "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.TrySaveAsync(
                $"auth-methods-{accountId}",
                It.IsAny<List<AuthMethodInfo>>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Provider index points to a deleted account
        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Steam:steam-orphan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedAccountId.ToString());

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{deletedAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = deletedAccountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow // Deleted
            });

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Steam,
            ExternalId = "steam-orphan"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(OAuthProvider.Steam, response.Provider);
    }

    [Fact]
    public async Task AddAuthMethodAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>(), "etag-0"));

        // No existing provider index
        _mockStringStore
            .Setup(s => s.GetAsync("provider-index-Discord:disc-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // TrySaveAsync returns null (conflict)
        _mockAuthMethodsStore
            .Setup(s => s.TrySaveAsync(
                $"auth-methods-{accountId}",
                It.IsAny<List<AuthMethodInfo>>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.AddAuthMethodAsync(new AddAuthMethodRequest
        {
            AccountId = accountId,
            Provider = OAuthProvider.Discord,
            ExternalId = "disc-new"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region RemoveAuthMethod Tests

    [Fact]
    public async Task RemoveAuthMethodAsync_Success_RemovesMethodAndIndex()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var methodId = Guid.NewGuid();

        var account = new AccountModel
        {
            AccountId = accountId,
            PasswordHash = "has-password", // Has password so removing last method is safe
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>
            {
                new AuthMethodInfo
                {
                    MethodId = methodId,
                    Provider = AuthProvider.Discord,
                    ExternalId = "disc-rm-1",
                    LinkedAt = DateTimeOffset.UtcNow
                }
            }, "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.TrySaveAsync(
                $"auth-methods-{accountId}",
                It.IsAny<List<AuthMethodInfo>>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = methodId
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify provider index removed
        _mockStringStore.Verify(s => s.DeleteAsync(
            "provider-index-Discord:disc-rm-1",
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAuthMethodAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountModel?)null);

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = Guid.NewGuid()
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task RemoveAuthMethodAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            });

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = Guid.NewGuid()
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task RemoveAuthMethodAsync_MethodIdNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>(), "etag-0"));

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = Guid.NewGuid() // Non-existent method
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task RemoveAuthMethodAsync_WouldOrphanAccount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var methodId = Guid.NewGuid();

        // Account has no password
        var account = new AccountModel
        {
            AccountId = accountId,
            PasswordHash = null, // No password
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Only has one auth method -- removing it would orphan account
        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>
            {
                new AuthMethodInfo
                {
                    MethodId = methodId,
                    Provider = AuthProvider.Discord,
                    ExternalId = "disc-last",
                    LinkedAt = DateTimeOffset.UtcNow
                }
            }, "etag-0"));

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = methodId
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RemoveAuthMethodAsync_HasPasswordAndLastMethod_Succeeds()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var methodId = Guid.NewGuid();

        // Account HAS a password, so removing last OAuth method is fine
        var account = new AccountModel
        {
            AccountId = accountId,
            PasswordHash = "has-password",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>
            {
                new AuthMethodInfo
                {
                    MethodId = methodId,
                    Provider = AuthProvider.Google,
                    ExternalId = "goog-last",
                    LinkedAt = DateTimeOffset.UtcNow
                }
            }, "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.TrySaveAsync(
                $"auth-methods-{accountId}",
                It.IsAny<List<AuthMethodInfo>>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = methodId
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task RemoveAuthMethodAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var methodId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountModel
            {
                AccountId = accountId,
                PasswordHash = "has-password",
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetWithETagAsync($"auth-methods-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuthMethodInfo>
            {
                new AuthMethodInfo
                {
                    MethodId = methodId,
                    Provider = AuthProvider.Discord,
                    ExternalId = "disc-confl",
                    LinkedAt = DateTimeOffset.UtcNow
                }
            }, "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.TrySaveAsync(
                $"auth-methods-{accountId}",
                It.IsAny<List<AuthMethodInfo>>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // Conflict

        // Act
        var status = await service.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
        {
            AccountId = accountId,
            MethodId = methodId
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region UpdateProfile Tests

    [Fact]
    public async Task UpdateProfileAsync_Success_UpdatesDisplayName()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            DisplayName = "Old Display",
            Roles = new List<string> { "user" },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Capture event publication
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, id, ct) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            DisplayName = "New Display"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("New Display", response.DisplayName);

        Assert.Equal("account.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<AccountUpdatedEvent>(capturedEvent);
        Assert.Contains("displayName", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task UpdateProfileAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var (status, response) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            DisplayName = "Anything"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateProfileAsync_SoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        // Act
        var (status, response) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            DisplayName = "Anything"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateProfileAsync_NoChanges_ReturnsOkWithoutSavingOrPublishing()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            DisplayName = "Same Name",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act
        var (status, response) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            DisplayName = "Same Name"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify no save and no event
        _mockAccountStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(),
            It.IsAny<AccountModel>(),
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()), Times.Never);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProfileAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            DisplayName = "Old",
            Roles = new List<string> { "user" },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            DisplayName = "New"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateMfa Tests

    [Fact]
    public async Task UpdateMfaAsync_Success_EnablesMfa()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Roles = new List<string> { "user" },
            MfaEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (key, model, etag, ct) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Capture event publication
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, id, ct) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var status = await service.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = accountId,
            MfaEnabled = true,
            MfaSecret = "totp-secret",
            MfaRecoveryCodes = new List<string> { "recovery-1", "recovery-2" }
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedAccount);
        Assert.True(savedAccount.MfaEnabled);
        Assert.Equal("totp-secret", savedAccount.MfaSecret);
        Assert.NotNull(savedAccount.MfaRecoveryCodes);
        Assert.Equal(2, savedAccount.MfaRecoveryCodes.Count);

        Assert.Equal("account.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<AccountUpdatedEvent>(capturedEvent);
        Assert.Equal(accountId, typedEvent.AccountId);
    }

    [Fact]
    public async Task UpdateMfaAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var status = await service.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = accountId,
            MfaEnabled = true
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateMfaAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        // Act
        var status = await service.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = accountId,
            MfaEnabled = false
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateMfaAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var status = await service.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = accountId,
            MfaEnabled = true
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region UpdatePasswordHash Tests (Extended)

    [Fact]
    public async Task UpdatePasswordHashAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var status = await service.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = accountId,
            PasswordHash = "new-hash"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdatePasswordHashAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        // Act
        var status = await service.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = accountId,
            PasswordHash = "new-hash"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdatePasswordHashAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                PasswordHash = "old-hash",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var status = await service.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = accountId,
            PasswordHash = "new-hash"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task UpdatePasswordHashAsync_Success_UpdatesHashAndTimestamp()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                PasswordHash = "old-hash",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (key, model, etag, ct) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act
        var status = await service.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = accountId,
            PasswordHash = "new-hash-value"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedAccount);
        Assert.Equal("new-hash-value", savedAccount.PasswordHash);
    }

    #endregion

    #region UpdateVerificationStatus Tests (Extended)

    [Fact]
    public async Task UpdateVerificationStatusAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var status = await service.UpdateVerificationStatusAsync(new UpdateVerificationRequest
        {
            AccountId = accountId,
            EmailVerified = true
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateVerificationStatusAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        // Act
        var status = await service.UpdateVerificationStatusAsync(new UpdateVerificationRequest
        {
            AccountId = accountId,
            EmailVerified = true
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateVerificationStatusAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                IsVerified = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var status = await service.UpdateVerificationStatusAsync(new UpdateVerificationRequest
        {
            AccountId = accountId,
            EmailVerified = true
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region UpdateEmail Tests

    [Fact]
    public async Task UpdateEmailAsync_Success_ChangesEmailAndResetsVerification()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "old@test.local",
            IsVerified = true,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        // New email not taken
        _mockStringStore
            .Setup(s => s.GetAsync("email-index-new@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (key, model, etag, ct) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Capture event publication
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, id, ct) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "new@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("new@test.local", response.Email);
        Assert.False(response.EmailVerified); // Verification reset

        Assert.NotNull(savedAccount);
        Assert.Equal("new@test.local", savedAccount.Email);
        Assert.False(savedAccount.IsVerified);

        // Verify new email index created
        _mockStringStore.Verify(s => s.SaveAsync(
            "email-index-new@test.local",
            accountId.ToString(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify old email index deleted
        _mockStringStore.Verify(s => s.DeleteAsync(
            "email-index-old@test.local",
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published with capture pattern
        Assert.Equal("account.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<AccountUpdatedEvent>(capturedEvent);
        Assert.Contains("email", typedEvent.ChangedFields);
        Assert.Contains("isVerified", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task UpdateEmailAsync_LockFailure_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        // Lock fails
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "new@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateEmailAsync_EmailAlreadyTaken_ReturnsConflict()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        // Email already taken by another account
        _mockStringStore
            .Setup(s => s.GetAsync("email-index-taken@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "taken@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateEmailAsync_AccountNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        // New email not taken
        _mockStringStore
            .Setup(s => s.GetAsync("email-index-new@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountModel?)null, (string?)null));

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "new@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateEmailAsync_AccountSoftDeleted_ReturnsNotFound()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-new@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "new@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateEmailAsync_SameEmail_ReturnsOkWithoutChanges()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-same@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Email = "same@test.local",
                IsVerified = true,
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "Same@Test.Local" // Same email, different case
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.EmailVerified); // Verification NOT reset

        // Verify no save and no event
        _mockAccountStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(),
            It.IsAny<AccountModel>(),
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()), Times.Never);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.IsAny<AccountUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateEmailAsync_ConcurrentModification_RollsBackNewIndex()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-rollback@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Email = "old@test.local",
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        // Concurrent modification
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "rollback@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify rollback: the new email index was created then deleted
        _mockStringStore.Verify(s => s.SaveAsync(
            "email-index-rollback@test.local",
            accountId.ToString(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockStringStore.Verify(s => s.DeleteAsync(
            "email-index-rollback@test.local",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEmailAsync_FromNullEmail_SkipsOldIndexDeletion()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync("email-index-first@test.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new AccountModel
            {
                AccountId = accountId,
                Email = null, // No previous email (OAuth account)
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, "etag-0"));

        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Act
        var (status, response) = await service.UpdateEmailAsync(new UpdateEmailRequest
        {
            AccountId = accountId,
            NewEmail = "first@test.local"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify old email index delete was NOT called (there was no old email)
        _mockStringStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.StartsWith("email-index-") && k != "email-index-first@test.local"),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region BuildAccountQueryConditions Tests (via ListAccounts)

    /// <summary>
    /// Verifies that an email filter adds a Contains condition on $.Email.
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_WithEmailFilter_AddsEmailCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (conditions, _, _, _, _) => capturedConditions = conditions)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

        // Act
        await service.ListAccountsAsync(new ListAccountsRequest { Email = "test@example.com" });

        // Assert - should have base conditions (AccountId exists, DeletedAtUnix not exists) + email
        Assert.NotNull(capturedConditions);
        var emailCondition = capturedConditions.FirstOrDefault(c => c.Path == "$.Email");
        Assert.NotNull(emailCondition);
        Assert.Equal(QueryOperator.Contains, emailCondition.Operator);
        Assert.Equal("test@example.com", emailCondition.Value);
    }

    /// <summary>
    /// Verifies that a display name filter adds a Contains condition on $.DisplayName.
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_WithDisplayNameFilter_AddsDisplayNameCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (conditions, _, _, _, _) => capturedConditions = conditions)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

        // Act
        await service.ListAccountsAsync(new ListAccountsRequest { DisplayName = "Alice" });

        // Assert
        Assert.NotNull(capturedConditions);
        var nameCondition = capturedConditions.FirstOrDefault(c => c.Path == "$.DisplayName");
        Assert.NotNull(nameCondition);
        Assert.Equal(QueryOperator.Contains, nameCondition.Operator);
        Assert.Equal("Alice", nameCondition.Value);
    }

    /// <summary>
    /// Verifies that a verified filter adds an Equals condition on $.IsVerified.
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_WithVerifiedFilter_AddsVerifiedCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (conditions, _, _, _, _) => capturedConditions = conditions)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

        // Act
        await service.ListAccountsAsync(new ListAccountsRequest { Verified = true });

        // Assert
        Assert.NotNull(capturedConditions);
        var verifiedCondition = capturedConditions.FirstOrDefault(c => c.Path == "$.IsVerified");
        Assert.NotNull(verifiedCondition);
        Assert.Equal(QueryOperator.Equals, verifiedCondition.Operator);
        Assert.Equal(true, verifiedCondition.Value);
    }

    /// <summary>
    /// Verifies that multiple simultaneous filters all generate conditions.
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_WithMultipleFilters_AddsAllConditions()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (conditions, _, _, _, _) => capturedConditions = conditions)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

        // Act
        await service.ListAccountsAsync(new ListAccountsRequest
        {
            Email = "test@example.com",
            DisplayName = "Alice",
            Verified = false
        });

        // Assert - 2 base conditions + 3 filters = 5 total
        Assert.NotNull(capturedConditions);
        Assert.Equal(5, capturedConditions.Count);
        Assert.NotNull(capturedConditions.FirstOrDefault(c => c.Path == "$.Email"));
        Assert.NotNull(capturedConditions.FirstOrDefault(c => c.Path == "$.DisplayName"));
        Assert.NotNull(capturedConditions.FirstOrDefault(c => c.Path == "$.IsVerified"));
    }

    /// <summary>
    /// Verifies that empty/whitespace-only email filter is treated as absent (no condition added).
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_WithEmptyEmailFilter_DoesNotAddEmailCondition()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (conditions, _, _, _, _) => capturedConditions = conditions)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

        // Act
        await service.ListAccountsAsync(new ListAccountsRequest { Email = "   " });

        // Assert - only base conditions (AccountId exists + DeletedAtUnix not exists)
        Assert.NotNull(capturedConditions);
        Assert.Equal(2, capturedConditions.Count);
        Assert.Null(capturedConditions.FirstOrDefault(c => c.Path == "$.Email"));
    }

    /// <summary>
    /// Verifies that no filters produces only the base conditions (type discriminator + soft-delete check).
    /// </summary>
    [Fact]
    public async Task ListAccountsAsync_NoFilters_ProducesOnlyBaseConditions()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (conditions, _, _, _, _) => capturedConditions = conditions)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 20));

        // Act
        await service.ListAccountsAsync(new ListAccountsRequest());

        // Assert - AccountId exists + DeletedAtUnix not exists
        Assert.NotNull(capturedConditions);
        Assert.Equal(2, capturedConditions.Count);
        Assert.NotNull(capturedConditions.FirstOrDefault(c =>
            c.Path == "$.AccountId" && c.Operator == QueryOperator.Exists));
        Assert.NotNull(capturedConditions.FirstOrDefault(c =>
            c.Path == "$.DeletedAtUnix" && c.Operator == QueryOperator.NotExists));
    }

    #endregion

    #region MetadataEquals Tests (via UpdateAccount)

    /// <summary>
    /// Verifies that updating with identical metadata does NOT publish a metadata change event.
    /// </summary>
    [Fact]
    public async Task UpdateAccountAsync_SameMetadata_DoesNotPublishMetadataChange()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var existingMetadata = new Dictionary<string, object>
        {
            { "theme", "dark" },
            { "level", 5L }
        };
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = existingMetadata,
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Same metadata values (dict equality should be true)
        var sameMetadata = new Dictionary<string, object>
        {
            { "theme", "dark" },
            { "level", 5L }
        };

        // Act
        var (status, _) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            Metadata = sameMetadata
        });

        // Assert - no metadata change means no "metadata" in changed fields
        Assert.Equal(StatusCodes.OK, status);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "account.updated",
            It.Is<AccountUpdatedEvent>(e => e.ChangedFields.Contains("metadata")),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that metadata with same keys but different values IS detected as a change.
    /// </summary>
    [Fact]
    public async Task UpdateAccountAsync_DifferentMetadataValues_PublishesMetadataChange()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = new Dictionary<string, object> { { "theme", "dark" } },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Capture event publication
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, id, ct) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Same key, different value
        var differentMetadata = new Dictionary<string, object> { { "theme", "light" } };

        // Act
        var (status, _) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            Metadata = differentMetadata
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("account.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<AccountUpdatedEvent>(capturedEvent);
        Assert.Contains("metadata", typedEvent.ChangedFields);
    }

    /// <summary>
    /// Verifies that adding a new metadata key (different count) IS detected as a change.
    /// </summary>
    [Fact]
    public async Task UpdateAccountAsync_MetadataWithExtraKey_PublishesMetadataChange()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = new Dictionary<string, object> { { "theme", "dark" } },
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Capture event publication
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, id, ct) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Extra key → different count → MetadataEquals returns false
        var extraMetadata = new Dictionary<string, object>
        {
            { "theme", "dark" },
            { "language", "en" }
        };

        // Act
        var (status, _) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            Metadata = extraMetadata
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("account.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<AccountUpdatedEvent>(capturedEvent);
        Assert.Contains("metadata", typedEvent.ChangedFields);
    }

    /// <summary>
    /// Verifies that setting metadata on an account with null metadata IS detected as a change.
    /// </summary>
    [Fact]
    public async Task UpdateAccountAsync_NullExistingMetadata_NewMetadata_PublishesMetadataChange()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = null,
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
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Capture event publication
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, id, ct) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var (status, _) = await service.UpdateAccountAsync(new UpdateAccountRequest
        {
            AccountId = accountId,
            Metadata = new Dictionary<string, object> { { "theme", "dark" } }
        });

        // Assert - null existing → empty dict, vs non-empty new → change detected
        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("account.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<AccountUpdatedEvent>(capturedEvent);
        Assert.Contains("metadata", typedEvent.ChangedFields);
    }

    #endregion

    #region Gap 1: MaxPageSize Clamping Tests

    [Fact]
    public async Task ListAccountsAsync_WithPageSizeExceedingMax_ShouldClampToMaxPageSize()
    {
        // Arrange
        var configuration = new AccountServiceConfiguration
        {
            MaxPageSize = 50,
            DefaultPageSize = 20
        };
        var service = new AccountService(
            _mockLogger.Object,
            configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

        // Capture the limit passed to the query store
        int? capturedLimit = null;
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (_, _, limit, _, _) => capturedLimit = limit)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 50));

        // Act - Request page size of 200, which exceeds MaxPageSize of 50
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            PageSize = 200
        });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(50, response.PageSize);
        Assert.Equal(50, capturedLimit);
    }

    [Fact]
    public async Task ListAccountsAsync_WithPageSizeEqualToMax_ShouldNotClamp()
    {
        // Arrange
        var configuration = new AccountServiceConfiguration
        {
            MaxPageSize = 100,
            DefaultPageSize = 20
        };
        var service = new AccountService(
            _mockLogger.Object,
            configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

        int? capturedLimit = null;
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (_, _, limit, _, _) => capturedLimit = limit)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 100));

        // Act - Request page size exactly at MaxPageSize
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            PageSize = 100
        });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(100, response.PageSize);
        Assert.Equal(100, capturedLimit);
    }

    [Fact]
    public async Task ListAccountsAsync_WithPageSizeBelowMax_ShouldUseRequestedSize()
    {
        // Arrange
        var configuration = new AccountServiceConfiguration
        {
            MaxPageSize = 100,
            DefaultPageSize = 20
        };
        var service = new AccountService(
            _mockLogger.Object,
            configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

        int? capturedLimit = null;
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>(
                (_, _, limit, _, _) => capturedLimit = limit)
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>(), 0, 0, 15));

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            PageSize = 15
        });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(15, response.PageSize);
        Assert.Equal(15, capturedLimit);
    }

    #endregion

    #region Gap 2: ListAccountsWithProviderFilterAsync Tests

    [Fact]
    public async Task ListAccountsAsync_WithProviderFilter_ShouldFilterByProvider()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        var account1Id = Guid.NewGuid();
        var account2Id = Guid.NewGuid();
        var account3Id = Guid.NewGuid();

        var account1 = new AccountModel
        {
            AccountId = account1Id,
            Email = "user1@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };
        var account2 = new AccountModel
        {
            AccountId = account2Id,
            Email = "user2@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var account3 = new AccountModel
        {
            AccountId = account3Id,
            Email = "user3@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        // Mock the queryable store to return all 3 accounts
        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>
                {
                    new JsonQueryResult<AccountModel>($"account-{account1Id}", account1),
                    new JsonQueryResult<AccountModel>($"account-{account2Id}", account2),
                    new JsonQueryResult<AccountModel>($"account-{account3Id}", account3),
                },
                3, 0, 10000));

        // Mock auth methods: only account1 and account3 have Discord
        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                $"auth-methods-{account1Id}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo { Provider = AuthProvider.Discord, ExternalId = "disc1" }
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                $"auth-methods-{account2Id}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo { Provider = AuthProvider.Google, ExternalId = "goog1" }
            });

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                $"auth-methods-{account3Id}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo { Provider = AuthProvider.Discord, ExternalId = "disc2" }
            });

        // Act - Filter by Discord provider
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            Provider = AuthProvider.Discord
        });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Accounts.Count);
        Assert.All(response.Accounts, a =>
            Assert.Contains(a.AuthMethods, m => m.Provider == AuthProvider.Discord));
    }

    [Fact]
    public async Task ListAccountsAsync_WithProviderFilter_ShouldSortByCreatedAtDescending()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        var olderAccountId = Guid.NewGuid();
        var newerAccountId = Guid.NewGuid();

        var olderAccount = new AccountModel
        {
            AccountId = olderAccountId,
            Email = "older@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        var newerAccount = new AccountModel
        {
            AccountId = newerAccountId,
            Email = "newer@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>
                {
                    new JsonQueryResult<AccountModel>($"account-{olderAccountId}", olderAccount),
                    new JsonQueryResult<AccountModel>($"account-{newerAccountId}", newerAccount),
                },
                2, 0, 10000));

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("auth-methods-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo { Provider = AuthProvider.Steam, ExternalId = "steam1" }
            });

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            Provider = AuthProvider.Steam
        });

        // Assert - newer account should come first (descending order)
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Accounts.Count);
        var accountList = response.Accounts.ToList();
        Assert.Equal(newerAccountId, accountList[0].AccountId);
        Assert.Equal(olderAccountId, accountList[1].AccountId);
    }

    [Fact]
    public async Task ListAccountsAsync_WithProviderFilter_ShouldPaginateCorrectly()
    {
        // Arrange
        var configuration = new AccountServiceConfiguration
        {
            MaxPageSize = 100,
            DefaultPageSize = 20,
            ProviderFilterMaxScanSize = 10000,
            ListBatchSize = 100
        };
        var service = new AccountService(
            _mockLogger.Object,
            configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object);

        // Create 3 accounts, all with Google auth
        var accounts = new List<(Guid Id, AccountModel Model)>();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            accounts.Add((id, new AccountModel
            {
                AccountId = id,
                Email = $"user{i}@test.local",
                Roles = new List<string> { "user" },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-(3 - i)),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-(3 - i))
            }));
        }

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                accounts.Select(a =>
                    new JsonQueryResult<AccountModel>($"account-{a.Id}", a.Model)).ToList(),
                3, 0, 10000));

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("auth-methods-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo { Provider = AuthProvider.Google, ExternalId = "g1" }
            });

        // Act - Request page 2 with size 1
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            Provider = AuthProvider.Google,
            Page = 2,
            PageSize = 1
        });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalCount);
        Assert.Single(response.Accounts);
        Assert.Equal(2, response.Page);
        Assert.Equal(1, response.PageSize);
        Assert.True(response.HasNextPage);
        Assert.True(response.HasPreviousPage);
    }

    [Fact]
    public async Task ListAccountsAsync_WithProviderFilter_NoMatches_ShouldReturnEmpty()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();

        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<AccountModel>(
                new List<JsonQueryResult<AccountModel>>
                {
                    new JsonQueryResult<AccountModel>($"account-{accountId}", account)
                },
                1, 0, 10000));

        // Account only has Google, but we filter by Discord
        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("auth-methods-")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>
            {
                new AuthMethodInfo { Provider = AuthProvider.Google, ExternalId = "g1" }
            });

        // Act
        var (statusCode, response) = await service.ListAccountsAsync(new ListAccountsRequest
        {
            Provider = AuthProvider.Discord
        });

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Accounts);
        Assert.False(response.HasNextPage);
    }

    #endregion

    #region Gap 3: PublishErrorEventAsync via GetAuthMethodsForAccount Error Path

    [Fact]
    public async Task GetAccountAsync_WhenAuthMethodsStoreThrows_ShouldPublishErrorEvent()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();

        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetAsync(
                $"account-{accountId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Simulate state store failure for auth methods
        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(
                $"auth-methods-{accountId}",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection failed"));

        // Capture error event publication
        string? capturedServiceName = null;
        string? capturedOperation = null;
        string? capturedErrorType = null;
        string? capturedMessage = null;
        string? capturedDependency = null;

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string?, string?, ServiceErrorEventSeverity, object?, string?, Guid?, CancellationToken>(
                (svc, op, errType, msg, dep, endpoint, sev, details, stack, corr, ct) =>
                {
                    capturedServiceName = svc;
                    capturedOperation = op;
                    capturedErrorType = errType;
                    capturedMessage = msg;
                    capturedDependency = dep;
                })
            .ReturnsAsync(true);

        // Act & Assert - The exception should propagate (service re-throws after publishing error event)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccountAsync(new GetAccountRequest { AccountId = accountId }));

        // Assert on captured error event
        Assert.Equal("account", capturedServiceName);
        Assert.Equal("GetAuthMethodsForAccount", capturedOperation);
        Assert.Equal("InvalidOperationException", capturedErrorType);
        Assert.Equal("Redis connection failed", capturedMessage);
        Assert.Equal("state", capturedDependency);
    }

    #endregion

    #region Gap 4: ConvertJsonElement Recursive Paths via UpdateProfileAsync

    [Fact]
    public async Task UpdateProfileAsync_WithNestedObjectMetadata_ShouldConvertCorrectly()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        // Capture saved account to verify metadata conversion
        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Create a nested JSON structure via JsonElement to exercise ConvertJsonElement recursion
        var jsonString = """{"settings":{"theme":"dark","fontSize":14},"tags":["vip","beta"],"active":true,"score":42.5}""";
        var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonString);
        var jsonElement = jsonDoc.RootElement.Clone();
        jsonDoc.Dispose();

        // Act
        var (status, response) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            Metadata = jsonElement
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedAccount);
        Assert.NotNull(savedAccount.Metadata);

        // Verify nested object was converted
        Assert.True(savedAccount.Metadata.ContainsKey("settings"));
        var settings = savedAccount.Metadata["settings"];
        Assert.IsType<Dictionary<string, object?>>(settings);
        var settingsDict = (Dictionary<string, object?>)settings;
        Assert.Equal("dark", settingsDict["theme"]);
        Assert.Equal(14L, settingsDict["fontSize"]); // Int64 via TryGetInt64

        // Verify array was converted
        Assert.True(savedAccount.Metadata.ContainsKey("tags"));
        var tags = savedAccount.Metadata["tags"];
        Assert.IsType<List<object?>>(tags);
        var tagList = (List<object?>)tags;
        Assert.Equal(2, tagList.Count);
        Assert.Equal("vip", tagList[0]);
        Assert.Equal("beta", tagList[1]);

        // Verify boolean was converted
        Assert.True(savedAccount.Metadata.ContainsKey("active"));
        Assert.Equal(true, savedAccount.Metadata["active"]);

        // Verify double was converted (42.5 cannot be int64, falls through to GetDouble)
        Assert.True(savedAccount.Metadata.ContainsKey("score"));
        Assert.Equal(42.5, savedAccount.Metadata["score"]);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithArrayContainingNestedObjects_ShouldConvertRecursively()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // Create array with nested objects to exercise recursive array+object paths
        var jsonString = """{"items":[{"name":"sword","damage":50},{"name":"shield","defense":30}]}""";
        var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonString);
        var jsonElement = jsonDoc.RootElement.Clone();
        jsonDoc.Dispose();

        // Act
        var (status, _) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            Metadata = jsonElement
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedAccount);
        Assert.NotNull(savedAccount.Metadata);

        Assert.True(savedAccount.Metadata.ContainsKey("items"));
        var items = savedAccount.Metadata["items"];
        Assert.IsType<List<object?>>(items);
        var itemList = (List<object?>)items;
        Assert.Equal(2, itemList.Count);

        // Verify first nested object in array
        var sword = Assert.IsType<Dictionary<string, object?>>(itemList[0]);
        Assert.Equal("sword", sword["name"]);
        Assert.Equal(50L, sword["damage"]);

        // Verify second nested object in array
        var shield = Assert.IsType<Dictionary<string, object?>>(itemList[1]);
        Assert.Equal("shield", shield["name"]);
        Assert.Equal(30L, shield["defense"]);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithNullMetadataValues_ShouldExcludeFromDictionary()
    {
        // Arrange
        var service = CreateServiceWithConfiguration();
        var accountId = Guid.NewGuid();
        var account = new AccountModel
        {
            AccountId = accountId,
            Email = "user@test.local",
            Roles = new List<string> { "user" },
            Metadata = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAccountStore
            .Setup(s => s.GetWithETagAsync($"account-{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, "etag-0"));

        AccountModel? savedAccount = null;
        _mockAccountStore
            .Setup(s => s.TrySaveAsync(
                $"account-{accountId}",
                It.IsAny<AccountModel>(),
                "etag-0",
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, AccountModel, string, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedAccount = model)
            .ReturnsAsync("etag-1");

        _mockAuthMethodsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthMethodInfo>());

        // JSON with null value - ConvertJsonElement returns null, which is excluded by caller
        var jsonString = """{"present":"value","absent":null}""";
        var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonString);
        var jsonElement = jsonDoc.RootElement.Clone();
        jsonDoc.Dispose();

        // Act
        var (status, _) = await service.UpdateProfileAsync(new UpdateProfileRequest
        {
            AccountId = accountId,
            Metadata = jsonElement
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedAccount);
        Assert.NotNull(savedAccount.Metadata);
        Assert.True(savedAccount.Metadata.ContainsKey("present"));
        Assert.Equal("value", savedAccount.Metadata["present"]);
        // Null values are excluded from the metadata dictionary by ConvertToMetadataDictionary
        Assert.False(savedAccount.Metadata.ContainsKey("absent"));
    }

    #endregion

    #region Gap 5: AccountServicePlugin Tests

    [Fact]
    public void AccountServicePlugin_PluginName_ShouldBeAccount()
    {
        // Act
        var plugin = new AccountServicePlugin();

        // Assert
        Assert.Equal("account", plugin.PluginName);
    }

    [Fact]
    public void AccountServicePlugin_DisplayName_ShouldBeAccountService()
    {
        // Act
        var plugin = new AccountServicePlugin();

        // Assert
        Assert.Equal("Account Service", plugin.DisplayName);
    }

    [Fact]
    public void AccountServicePlugin_ShouldInheritFromStandardServicePlugin()
    {
        // Act
        var plugin = new AccountServicePlugin();

        // Assert - Verify inheritance chain
        Assert.IsAssignableFrom<BeyondImmersion.BannouService.Plugins.StandardServicePlugin<IAccountService>>(plugin);
    }

    #endregion
}
