using BeyondImmersion.BannouService.Services.Template.Data;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Services.Template.Messages;

/// <summary>
/// The response model for service API calls to `/template/list`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateListResponse : ServiceResponse<TemplateListRequest>
{
    [JsonProperty("templates")]
    public List<TemplateModel> Templates { get; } = new List<TemplateModel>();
}
