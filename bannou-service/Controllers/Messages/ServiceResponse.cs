using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.Controllers.Messages;

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
}
