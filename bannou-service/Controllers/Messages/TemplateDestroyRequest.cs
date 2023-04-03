using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/template/destroy`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateDestroyRequest : ServiceRequest
{
    /// <summary>
    /// Templates to destroy.
    /// </summary>
    [JsonProperty("templates", Required = Required.Always)]
    public TemplateModel[] Templates { get; }
}
