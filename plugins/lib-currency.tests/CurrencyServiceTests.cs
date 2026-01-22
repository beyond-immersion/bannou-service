using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Currency.Tests;

public class CurrencyServiceTests
{
    #region Constructor Validation

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

    #endregion

    // TODO: Add service-specific tests based on schema operations
    // Schema file: ../schemas/currency-api.yaml
    //
    // Guidelines:
    // - Use the Capture Pattern for event/state verification (see TESTING_PATTERNS.md)
    // - Verify side effects (saves, events, indices), not just response structure
    // - Keep Arrange < 50% of test code; extract helpers if needed
}
