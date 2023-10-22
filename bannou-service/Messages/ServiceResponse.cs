using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Messages;

[JsonObject(MemberSerialization = MemberSerialization.OptIn, ItemNullValueHandling = NullValueHandling.Ignore)]
public class ServiceResponse<T> : ServiceResponse
where T : class, IServiceRequest
{
    public Type GetRequestType()
        => typeof(T);
}

/// <summary>
/// The base class for service responses.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn, ItemNullValueHandling = NullValueHandling.Ignore)]
public class ServiceResponse : IServiceResponse
{
    [FromHeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string> RequestIDs { get; set; }
}
