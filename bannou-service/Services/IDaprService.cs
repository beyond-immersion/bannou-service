namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Interface to implement for all internal dapr service,
/// which provides the logic for any given set of APIs.
/// 
/// For example, the Inventory service is in charge of
/// any API calls that desire to create/modify inventory
/// data in the game.
/// </summary>
public interface IDaprService
{
    public string GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public bool IsEnabled()
        => IServiceConfiguration.IsServiceEnabled(GetType());

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredConfiguration(GetType());
}
