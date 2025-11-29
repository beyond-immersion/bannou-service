namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Marker interface for Dapr service clients.
/// Provides explicit identification of client types without relying on naming conventions.
/// </summary>
public interface IDaprClient
{
    /// <summary>
    /// The name of the service this client communicates with.
    /// Should match the service name in the corresponding DaprServiceAttribute.
    /// </summary>
    string ServiceName { get; }
}
