using BeyondImmersion.BannouService.Faction;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Faction.Tests;

public class FactionServiceConstructorTests
{
    [Fact]
    public void FactionService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<FactionService>();
}
