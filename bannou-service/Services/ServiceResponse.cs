using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.Services;

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
    /// <summary>
    /// Response status code.
    /// </summary>
    [JsonProperty("code", Required = Required.Always)]
    public int Code { get; set; } = 200;

    /// <summary>
    /// List of messages to return to the client.
    /// </summary>
    [JsonProperty("message", Required = Required.Default)]
    public string? Message { get; set; }

    public ServiceResponse() { }
    public ServiceResponse(int code, string? message)
    {
        Code = code;
        Message = message;
    }

    public ServiceResponse(ResponseCodes responseCode, string? message)
        => SetResponse(responseCode, message);

    /// <summary>
    /// Whether this response has data, or can be discarded.
    /// </summary>
    public virtual bool HasData()
    {
        if (Code != 200)
            return true;

        var thisObj = JObject.FromObject(this);
        if (thisObj.Count > 2)
        {
            Program.Logger.Log(LogLevel.Debug, null, $"Object model {GetType().Name} has data.");
            return true;
        }
        else
        {
            Program.Logger.Log(LogLevel.Debug, null, $"Object model {GetType().Name} does not have data.");
            return false;
        }
    }

    /// <summary>
    /// Set fixed service response, based on a given response code.
    /// </summary>
    public void SetResponse(ResponseCodes responseCode, string? message = null)
    {
        switch (responseCode)
        {
            case ResponseCodes.Ok:
                Code = 200;
                break;
            case ResponseCodes.Accepted:
                Code = 202;
                break;
            case ResponseCodes.BadRequest:
                Code = 400;
                Message = "Bad request";
                break;
            case ResponseCodes.Unauthorized:
                Code = 403;
                Message = "Unauthorized request";
                break;
            case ResponseCodes.NotFound:
                Code = 404;
                Message = "Resource not found";
                break;
            case ResponseCodes.ServerBusy:
                Code = 503;
                Message = "Server busy";
                break;
            case ResponseCodes.ServerError:
            default:
                Code = 500;
                Message = "Server error";
                break;
        }

        if (!string.IsNullOrWhiteSpace(message))
            Message = message;
    }
}
