using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Affix.Tests;

public class AffixServiceConstructorTests
{
    [Fact]
    public void AffixService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<AffixService>();
}
