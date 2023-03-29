using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers;

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
    /// <summary>
    /// Message ID, for logging/tracing through the system.
    /// </summary>
    [JsonProperty("request_id", Required = Required.Default)]
    public virtual string RequestID { get; } = Guid.NewGuid().ToString().ToLower();
}
