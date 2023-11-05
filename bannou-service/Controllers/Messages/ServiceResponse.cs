using System.Net;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The base class for service responses.
/// </summary>
[JsonObject]
public class ServiceResponse : ServiceMessage
{
    [JsonIgnore]
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    [JsonIgnore]
    public string? Message { get; set; }

    [JsonIgnore]
    public string? Content { get; set; }
}
