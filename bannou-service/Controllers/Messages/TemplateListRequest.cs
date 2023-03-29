using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/template/list`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class TemplateListRequest : ServiceRequest<TemplateListResponse>
{
}
