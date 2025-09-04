using System.Net;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The base API controller response model.
/// </summary>
[JsonObject]
public class ApiResponse : ApiMessage
{
    [JsonIgnore]
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    [JsonIgnore]
    public string? Message { get; set; }
}
