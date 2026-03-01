using BeyondImmersion.BannouService.Orchestrator;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Provides information about services running on the control plane (main bannou instance).
/// Used by the orchestrator to report control plane service health alongside deployed services.
/// </summary>
public interface IControlPlaneServiceProvider
{
    /// <summary>
    /// Gets the app-id of the control plane instance.
    /// </summary>
    string ControlPlaneAppId { get; }

    /// <summary>
    /// Gets health status entries for all services enabled on the control plane.
    /// Each entry represents a service running locally on the control plane instance.
    /// </summary>
    /// <returns>Collection of health status entries for control plane services</returns>
    IReadOnlyList<ServiceHealthEntry> GetControlPlaneServiceHealth();

    /// <summary>
    /// Gets the names of all services enabled on the control plane.
    /// </summary>
    /// <returns>Collection of service names</returns>
    IReadOnlyList<string> GetEnabledServiceNames();
}
