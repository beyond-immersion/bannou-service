using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.ServiceTester.Application;

[ServiceConfiguration(envPrefix: "TEST_")]
public sealed class TestConfiguration : AppConfiguration
{

}
