using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ServiceRequest<T> : ServiceRequest
where T : class, IServiceResponse, new()
{
    public Type GetResponseType()
        => typeof(T);

    public T CreateResponse()
        => new();
}

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ServiceRequest : IServiceRequest
{
    [JsonIgnore]
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string> RequestIDs { get; set; }
}
