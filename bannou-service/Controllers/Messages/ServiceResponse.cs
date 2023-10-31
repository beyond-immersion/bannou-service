namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The base class for service responses.
/// </summary>
[JsonObject]
public class ServiceResponse : IServiceResponse
{
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string>? RequestIDs { get; set; }
}
