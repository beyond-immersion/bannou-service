namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(TestingService), envPrefix: "TESTING_", primary: true)]
public class TestingServiceConfiguration : ServiceConfiguration
{
    public bool? Service_Enabled { get; set; } = true;
}
