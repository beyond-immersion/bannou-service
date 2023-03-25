namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Implemented for all service API controller.
/// </summary>
public interface IDaprController<T>
    where T : class, IDaprService
{
    public string GetName()
    => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public bool IsEnabled()
        => IServiceConfiguration.IsServiceEnabled(typeof(T));

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredConfiguration(typeof(T));
}
