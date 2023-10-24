using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Controllers.Messages;

[Serializable]
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
[Serializable]
public class ServiceRequest : IServiceRequest
{
    [JsonIgnore]
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string> RequestIDs { get; set; }
}
