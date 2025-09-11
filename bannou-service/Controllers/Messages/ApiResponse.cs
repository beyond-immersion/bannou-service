using System.Net;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The base API controller response model.
/// </summary>
[JsonObject]
public class ApiResponse : ApiMessage
{
    /// <summary>
    /// The HTTP status code for the response.
    /// </summary>
    [JsonIgnore]
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>
    /// Optional message associated with the response.
    /// </summary>
    [JsonIgnore]
    public string? Message { get; set; }
}
