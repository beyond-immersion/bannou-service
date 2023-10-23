using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Testing.Messages;

/// <summary>
/// The request model for service API calls to `/testing/run-test`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TestingRunTestRequest : ServiceRequest
{
    [JsonProperty]
    public string? ID { get; set; }

    [JsonProperty]
    public string? Service { get; set; }
}
