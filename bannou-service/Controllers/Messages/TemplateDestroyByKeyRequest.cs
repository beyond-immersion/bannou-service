using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/template/destroy-by-key`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateDestroyByKeyRequest : ServiceRequest
{
    /// <summary>
    /// IDs of templates to destroy.
    /// </summary>
    [JsonProperty("ids", Required = Required.Always)]
    public string[] IDs { get; }

    private TemplateDestroyByKeyRequest() { }
    public TemplateDestroyByKeyRequest(string[] templateIDs)
        => templateIDs = templateIDs ?? throw new ArgumentNullException(nameof(templateIDs));
}
