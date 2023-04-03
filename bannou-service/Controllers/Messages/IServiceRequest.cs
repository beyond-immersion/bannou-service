namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The interface all message payload models to service endpoints should implement.
/// </summary>
public interface IServiceRequest
{
    /// <summary>
    /// Optional ID for any requests through the system.
    /// 
    /// Never required, and only used for logging / debugging.
    /// </summary>
    string RequestID { get; }
}
