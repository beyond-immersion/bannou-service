using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Configuration class for lib-testing TestingService plugin.
/// </summary>
[ServiceConfiguration(typeof(TestingService))]
public class TestingServiceConfiguration : IServiceConfiguration
{
    public bool EnableDebugLogging { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public string TestEnvironment { get; set; } = "development";

    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <inheritdoc />
    public bool? Service_Disabled { get; set; }
}
