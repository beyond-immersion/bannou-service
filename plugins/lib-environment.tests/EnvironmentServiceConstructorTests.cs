using BeyondImmersion.BannouService.Environment;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Environment.Tests;

/// <summary>
/// Constructor validation and configuration tests for EnvironmentService.
/// </summary>
public class EnvironmentServiceConstructorTests
{
    [Fact]
    public void EnvironmentService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<EnvironmentService>();

    [Fact]
    public void EnvironmentServiceConfiguration_CanBeInstantiated()
    {
        var config = new EnvironmentServiceConfiguration();
        Assert.NotNull(config);
    }

    [Fact]
    public void EnvironmentServiceConfiguration_HasExpectedDefaults()
    {
        var config = new EnvironmentServiceConfiguration();

        Assert.Equal(60, config.ConditionCacheTtlSeconds);
        Assert.Equal(5, config.ConditionSnapshotCacheTtlSeconds);
        Assert.Equal(600, config.WeatherCacheTtlSeconds);
        Assert.Equal(60, config.ClimateCacheTtlMinutes);
        Assert.Equal(ConditionRefreshMode.AllLocations, config.ConditionRefreshMode);
        Assert.Equal(30, config.ActiveLocationWindowMinutes);
        Assert.Equal(30, config.ConditionUpdateIntervalSeconds);
        Assert.Equal(15, config.ConditionUpdateStartupDelaySeconds);
        Assert.Equal(60, config.EventExpirationCheckIntervalSeconds);
        Assert.Equal(50, config.MaxClimateTemplatesPerGameService);
        Assert.Equal(10, config.MaxActiveEventsPerScope);
        Assert.Equal("temperate", config.DefaultBiomeCode);
        Assert.Equal(0.1, config.ResourceAvailabilityChangeThreshold);
        Assert.Equal(0.2, config.DroughtThreshold);
        Assert.Equal(0.8, config.AbundanceThreshold);
    }
}
