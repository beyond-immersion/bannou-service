namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public class ServiceRequest : IServiceRequest
{
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string>? RequestIDs { get; set; }
}
