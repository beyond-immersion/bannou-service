using BeyondImmersion.BannouService.Divine;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Divine.Tests;

public class DivineServiceConstructorTests
{
    [Fact]
    public void DivineService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<DivineService>();
}
