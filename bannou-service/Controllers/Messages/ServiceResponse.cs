using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Controllers.Messages;

[Serializable]
public class ServiceResponse<T> : ServiceResponse
where T : class, IServiceRequest
{
    public Type GetRequestType()
        => typeof(T);
}

/// <summary>
/// The base class for service responses.
/// </summary>
[Serializable]
public class ServiceResponse : IServiceResponse
{
    [JsonIgnore]
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string> RequestIDs { get; set; }
}
