namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Interface for test handler classes that provide collections of related tests
/// </summary>
public interface IServiceTestHandler
{
    /// <summary>
    /// Get all service tests provided by this handler
    /// </summary>
    ServiceTest[] GetServiceTests();
}
