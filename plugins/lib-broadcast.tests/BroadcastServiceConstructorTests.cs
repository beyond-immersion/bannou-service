using BeyondImmersion.BannouService.Broadcast;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Broadcast.Tests;

/// <summary>
/// Constructor validation for BroadcastService.
/// Structural tests handle cross-cutting constructor patterns;
/// this validates the specific service constructor is well-formed.
/// </summary>
public class BroadcastServiceConstructorTests
{
    [Fact]
    public void BroadcastService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<BroadcastService>();
}
