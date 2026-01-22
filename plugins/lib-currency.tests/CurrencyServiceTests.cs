using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Currency.Tests;

/// <summary>
/// Unit tests for CurrencyService.
/// Tests constructor validation, configuration, and permission registration.
/// Full business logic testing is done via HTTP integration tests.
/// </summary>
public class CurrencyServiceTests
{
    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// Catches: multiple constructors, optional parameters, missing null checks,
    /// and wrong parameter names in ArgumentNullException.
    /// </summary>
    [Fact]
    public void CurrencyService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CurrencyService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void CurrencyServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new CurrencyServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void CurrencyServiceConfiguration_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new CurrencyServiceConfiguration();

        // Assert - verify default values are sensible
        Assert.False(config.DefaultAllowNegative);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        // Currency API has 31 endpoints defined
        Assert.True(endpoints.Count >= 25, $"Expected at least 25 endpoints, got {endpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldContainDefinitionEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert - verify core endpoints exist
        var createEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/currency/definition/create" &&
            e.Method == ServiceEndpointMethod.POST);

        Assert.NotNull(createEndpoint);
        Assert.NotNull(createEndpoint.Permissions);
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveWalletEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var walletEndpoints = endpoints.Where(e => e.Path.Contains("/wallet")).ToList();
        Assert.True(walletEndpoints.Count >= 5, $"Expected at least 5 wallet endpoints, got {walletEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveHoldEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var holdEndpoints = endpoints.Where(e => e.Path.Contains("/hold")).ToList();
        Assert.True(holdEndpoints.Count >= 4, $"Expected at least 4 hold endpoints, got {holdEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveBalanceEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var balanceEndpoints = endpoints.Where(e => e.Path.Contains("/balance")).ToList();
        Assert.True(balanceEndpoints.Count >= 2, $"Expected at least 2 balance endpoints, got {balanceEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_ShouldHaveTransactionEndpoints()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert
        var transactionEndpoints = endpoints.Where(e => e.Path.Contains("/transaction")).ToList();
        Assert.True(transactionEndpoints.Count >= 2, $"Expected at least 2 transaction endpoints, got {transactionEndpoints.Count}");
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_AllEndpointsHavePermissions()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert - every endpoint must have permissions defined
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Permissions);
            Assert.NotEmpty(endpoint.Permissions);
        }
    }

    [Fact]
    public void CurrencyPermissionRegistration_GetEndpoints_AllEndpointsArePOST()
    {
        // Act
        var endpoints = CurrencyPermissionRegistration.GetEndpoints();

        // Assert - POST-only API pattern
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(ServiceEndpointMethod.POST, endpoint.Method);
        }
    }

    #endregion

    #region Model Validation Tests

    [Fact]
    public void CurrencyScope_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(CurrencyScope), CurrencyScope.Global));
        Assert.True(Enum.IsDefined(typeof(CurrencyScope), CurrencyScope.Realm_specific));
        Assert.True(Enum.IsDefined(typeof(CurrencyScope), CurrencyScope.Multi_realm));
    }

    [Fact]
    public void WalletStatus_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(WalletStatus), WalletStatus.Active));
        Assert.True(Enum.IsDefined(typeof(WalletStatus), WalletStatus.Frozen));
        Assert.True(Enum.IsDefined(typeof(WalletStatus), WalletStatus.Closed));
    }

    [Fact]
    public void HoldStatus_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Active));
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Captured));
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Released));
        Assert.True(Enum.IsDefined(typeof(HoldStatus), HoldStatus.Expired));
    }

    [Fact]
    public void CurrencyPrecision_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(CurrencyPrecision), CurrencyPrecision.Integer));
        Assert.True(Enum.IsDefined(typeof(CurrencyPrecision), CurrencyPrecision.Decimal_2));
    }

    [Fact]
    public void WalletOwnerType_HasExpectedValues()
    {
        // Assert - verify enum values exist
        Assert.True(Enum.IsDefined(typeof(WalletOwnerType), WalletOwnerType.Account));
        Assert.True(Enum.IsDefined(typeof(WalletOwnerType), WalletOwnerType.Character));
    }

    #endregion

    #region Request Model Tests

    [Fact]
    public void CreateCurrencyDefinitionRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var request = new CreateCurrencyDefinitionRequest
        {
            Code = "gold",
            Name = "Gold Currency",
            Description = "Test currency",
            Scope = CurrencyScope.Global,
            Precision = CurrencyPrecision.Decimal_2
        };

        // Assert
        Assert.Equal("gold", request.Code);
        Assert.Equal("Gold Currency", request.Name);
        Assert.Equal(CurrencyScope.Global, request.Scope);
    }

    [Fact]
    public void CreateWalletRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var ownerId = Guid.NewGuid();
        var request = new CreateWalletRequest
        {
            OwnerId = ownerId,
            OwnerType = WalletOwnerType.Account
        };

        // Assert
        Assert.Equal(ownerId, request.OwnerId);
        Assert.Equal(WalletOwnerType.Account, request.OwnerType);
    }

    [Fact]
    public void CreditCurrencyRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var walletId = Guid.NewGuid();
        var currencyId = Guid.NewGuid();
        var request = new CreditCurrencyRequest
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyId,
            Amount = 100.50,
            TransactionType = TransactionType.Mint,
            ReferenceType = "test",
            IdempotencyKey = "test-key"
        };

        // Assert
        Assert.Equal(walletId, request.WalletId);
        Assert.Equal(currencyId, request.CurrencyDefinitionId);
        Assert.Equal(100.50, request.Amount);
        Assert.Equal(TransactionType.Mint, request.TransactionType);
    }

    [Fact]
    public void CreateHoldRequest_CanBeInstantiated()
    {
        // Arrange & Act
        var walletId = Guid.NewGuid();
        var currencyId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var request = new CreateHoldRequest
        {
            WalletId = walletId,
            CurrencyDefinitionId = currencyId,
            Amount = 50.00,
            ExpiresAt = expiresAt,
            ReferenceType = "test",
            IdempotencyKey = "hold-key"
        };

        // Assert
        Assert.Equal(walletId, request.WalletId);
        Assert.Equal(currencyId, request.CurrencyDefinitionId);
        Assert.Equal(50.00, request.Amount);
        Assert.Equal(expiresAt, request.ExpiresAt);
    }

    #endregion
}
