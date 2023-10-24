namespace BeyondImmersion.BannouService.Controllers.Messages;

[JsonObject]
public class ServiceResponse<T> : ServiceResponse
where T : class, IServiceRequest
{
    public Type GetRequestType()
        => typeof(T);
}

/// <summary>
/// The base class for service responses.
/// </summary>
[JsonObject]
public class ServiceResponse : IServiceResponse
{
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string> RequestIDs { get; set; }
}
