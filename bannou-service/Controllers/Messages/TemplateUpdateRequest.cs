using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/template/update`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateUpdateRequest : ServiceRequest
{
    /// <summary>
    /// Template data to be updated.
    /// </summary>
    [JsonProperty("templates", Required = Required.Always)]
    public TemplateModel[] Templates { get; }
}
