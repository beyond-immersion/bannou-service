using System.Net.Mime;
using System.Text;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service request payload model.
/// </summary>
[JsonObject]
public class ServiceRequest : ServiceMessage
{
    private static HttpClient _httpClient;
    [JsonIgnore]
    protected static HttpClient HttpClient
    {
        get
        {
            if (_httpClient != null)
                return _httpClient;

            _httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromSeconds(1)
            };

            return _httpClient;
        }
    }

    [JsonIgnore]
    public ServiceResponse? Response { get; protected set; }

    public virtual async Task<bool> ExecuteRequest<T>(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        where T : ServiceResponse, new()
    {
        if (typeof(ServiceRequest<T>).IsAssignableFrom(GetType()))
        {
            if (this is not ServiceRequest<T> derivedRequest)
                return false;

            // calling execute on the derived type will also parse and set
            // the more specific Response type that's expected, on success
            return await derivedRequest.ExecuteRequest(service, method, additionalHeaders, httpMethod);
        }

        return await ExecuteRequest_INTERNAL<T>(service, method, additionalHeaders, httpMethod);
    }

    public virtual async Task<bool> ExecuteRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        => await ExecuteRequest_INTERNAL(service, method, additionalHeaders, httpMethod);

    protected async Task<bool> ExecuteRequest_INTERNAL(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? "bannou";

            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"{service}/{method}";
            else
                requestUrl = $"{method}";

            HttpRequestMessage requestMsg = Program.DaprClient.CreateInvokeMethodRequest(httpMethod.ToObject(), coordinatorService, requestUrl, this);
            requestMsg.AddPropertyHeaders(this);

            if (additionalHeaders != null)
                foreach (var headerKVP in additionalHeaders)
                    requestMsg.Headers.Add(headerKVP.Key, headerKVP.Value);

            await Program.DaprClient.InvokeMethodAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
            Response = new()
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Message = "OK"
            };

            return true;
        }
        catch (HttpRequestException exc)
        {
            var statusCode = exc.StatusCode ?? System.Net.HttpStatusCode.InternalServerError;
            Response = new()
            {
                StatusCode = statusCode,
                Message = exc.Message ?? statusCode.ToString()
            };
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A failure occurred executing API method [{method}] on service [{service}].");
            Response = new()
            {
                StatusCode = System.Net.HttpStatusCode.InternalServerError,
                Message = "Internal service error"
            };
        }

        return false;
    }

    protected async Task<bool> ExecuteRequest_INTERNAL<T>(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        where T : ServiceResponse, new()
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? "bannou";

            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"http://127.0.0.1:3500/v1.0/invoke/{coordinatorService}/method/{service}/{method}";
            else
                requestUrl = $"http://127.0.0.1:3500/v1.0/invoke/{coordinatorService}/method/{method}";

            StringContent? requestContent = null;
            if (httpMethod == HttpMethodTypes.POST)
            {
                var requestData = JsonConvert.SerializeObject(this);
                requestContent = new StringContent(requestData, Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            var newRequest = new HttpRequestMessage(httpMethod.ToObject(), requestUrl);
            if (requestContent != null)
                newRequest.Content = requestContent;

            newRequest.AddPropertyHeaders(this);

            if (additionalHeaders != null)
                foreach (var headerKVP in additionalHeaders)
                    newRequest.Headers.Add(headerKVP.Key, headerKVP.Value);

            var responseMsg = await HttpClient.SendAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);
            if (responseMsg.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(ServiceResponse))
                {
                    Response = new T()
                    {
                        StatusCode = System.Net.HttpStatusCode.OK,
                        Message = "OK"
                    };
                    return true;
                }

                var responseContent = await responseMsg.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<T>(responseContent) ?? throw new NullReferenceException();

                responseData.SetHeadersToProperties(responseMsg.Headers);

                responseData.StatusCode = responseMsg.StatusCode;
                responseData.Message = responseMsg.ReasonPhrase ?? responseMsg.StatusCode.ToString();
                Response = responseData;

                return true;
            }
            else
            {
                Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]. HTTP Status: {responseMsg.StatusCode}");
                Response = new T()
                {
                    StatusCode = responseMsg.StatusCode,
                    Message = responseMsg.ReasonPhrase ?? responseMsg.StatusCode.ToString()
                };
            }
        }
        catch (HttpRequestException exc)
        {
            var statusCode = exc.StatusCode ?? System.Net.HttpStatusCode.InternalServerError;
            Response = new T()
            {
                StatusCode = statusCode,
                Message = exc.Message ?? statusCode.ToString()
            };
        }
        catch (UriFormatException)
        {
            Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]: Bad URI format.");
            Response = new T()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                Message = "Bad request"
            };
        }
        catch (InvalidOperationException)
        {
            Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]: Invalid operation.");
            Response = new T()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                Message = "Bad request"
            };
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A failure occurred executing API method [{method}] on service [{service}].");
            Response = new T()
            {
                StatusCode = System.Net.HttpStatusCode.InternalServerError,
                Message = "Internal service error"
            };
        }

        return false;
    }
}
