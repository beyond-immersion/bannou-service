using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/template/create`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateGetRequest : ServiceRequest<TemplateGetResponse>
{
    /// <summary>
    /// ID of template to retrieve.
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public string ID { get; }
}
