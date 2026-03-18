using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Constructor validation for GenesisService.
/// Uses centralized ServiceConstructorValidator to catch DI wiring issues.
/// </summary>
public class GenesisServiceConstructorTests
{
    [Fact]
    public void GenesisService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<GenesisService>();
}
