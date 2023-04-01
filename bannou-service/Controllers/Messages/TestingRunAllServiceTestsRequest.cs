using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/template/update`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TestingRunAllServiceTestsRequest : ServiceRequest
{
    [JsonProperty(Required = Required.Always)]
    public string Service { get; set; }
}
