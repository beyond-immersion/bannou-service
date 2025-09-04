using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The response model for service API calls to `/authorization/validate`.
/// </summary>
[JsonObject]
public class ValidateTokenResponse : ApiResponse
{
}
