using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The response model for service API calls to `/template/create`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateGetResponse : ServiceResponse<TemplateGetRequest>
{
    [JsonProperty("template")]
    public TemplateModel Template { get; set; }

    public TemplateGetResponse() { }
    public TemplateGetResponse(TemplateModel template)
        => Template = template;
}
