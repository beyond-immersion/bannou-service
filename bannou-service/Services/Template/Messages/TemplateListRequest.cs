using BeyondImmersion.BannouService.Services.Template.Data;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Services.Template.Messages;

/// <summary>
/// The request model for service API calls to `/template/list`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateListRequest : ServiceRequest<TemplateListResponse>
{
}
