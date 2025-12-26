namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Marker interface for Bannou service clients.
/// Provides explicit identification of client types without relying on naming conventions.
/// </summary>
public interface IBannouClient
{
    /// <summary>
    /// The name of the service this client communicates with.
    /// Should match the service name in the corresponding BannouServiceAttribute.
    /// </summary>
    string ServiceName { get; }
}
