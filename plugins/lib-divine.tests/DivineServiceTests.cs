using BeyondImmersion.BannouService.Divine;

namespace BeyondImmersion.BannouService.Divine.Tests;

public class DivineServiceTests
{
    [Fact]
    public void DivineServiceConfiguration_CanBeInstantiated()
    {
        var config = new DivineServiceConfiguration();
        Assert.NotNull(config);
    }
}
