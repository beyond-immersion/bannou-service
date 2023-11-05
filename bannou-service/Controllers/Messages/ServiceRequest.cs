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

    public virtual async Task<bool> ExecuteRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        => await ExecuteRequest<ServiceResponse>(service: service, method: method, additionalHeaders: additionalHeaders, httpMethod: httpMethod, data: this);

    public virtual async Task<bool> ExecuteRequest<T>(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        where T : ServiceResponse, new()
    {
        if (typeof(ServiceRequest<T>).IsAssignableFrom(GetType()))
        {
            if (this is not ServiceRequest<T> derivedRequest)
                return false;

            // calling execute on the derived type will also parse and set
            // the more specific Response type that's expected, on success
            return await derivedRequest.ExecuteRequest(service: service, method: method, additionalHeaders: additionalHeaders, httpMethod: httpMethod);
        }

        return await ExecuteRequest<T>(service:service, method: method, additionalHeaders: additionalHeaders, httpMethod: httpMethod, data: this);
    }

    public virtual async Task<bool> ExecuteGetRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null)
    => await ExecuteRequest(service: service, method: method, additionalHeaders: additionalHeaders, httpMethod: HttpMethodTypes.GET, data: this);

    public virtual async Task<bool> ExecutePostRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null)
        => await ExecuteRequest(service: service, method: method, additionalHeaders: additionalHeaders, httpMethod: HttpMethodTypes.POST, data: this);

    public static async Task<bool> ExecuteRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST, ServiceRequest? data = null)
        => await ExecuteRequest<ServiceResponse>(service: service, method: method, httpMethod: httpMethod, additionalHeaders: additionalHeaders, data: data);

    public static async Task<bool> ExecuteRequest<T>(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST, ServiceRequest? data = null)
        where T : ServiceResponse, new()
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? "bannou";

            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"{service}/{method}";
            else
                requestUrl = $"{method}";

            HttpRequestMessage requestMsg = Program.DaprClient.CreateInvokeMethodRequest(httpMethod.ToObject(), coordinatorService, requestUrl, data);
            if (data != null)
                requestMsg.AddPropertyHeaders(data);

            if (additionalHeaders != null)
                foreach (var headerKVP in additionalHeaders)
                    requestMsg.Headers.Add(headerKVP.Key, headerKVP.Value);

            var responseMsg = await HttpClient.SendAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
            if (responseMsg.IsSuccessStatusCode)
            {
                if (data == null)
                    return true;

                var responseContent = await responseMsg.Content.ReadAsStringAsync();
                if (typeof(T) == typeof(ServiceResponse))
                {
                    data.Response = new T()
                    {
                        StatusCode = System.Net.HttpStatusCode.OK,
                        Message = "OK",
                        Content = responseContent
                    };

                    return true;
                }

                var responseData = JsonConvert.DeserializeObject<T>(responseContent) ?? throw new NullReferenceException();
                responseData.SetHeadersToProperties(responseMsg.Headers);

                responseData.StatusCode = responseMsg.StatusCode;
                responseData.Message = responseMsg.ReasonPhrase ?? responseMsg.StatusCode.ToString();
                responseData.Content = responseContent;
                data.Response = responseData;

                return true;
            }
            else
            {
                Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]. HTTP Status: {responseMsg.StatusCode}");
                if (data != null)
                {
                    data.Response = new T()
                    {
                        StatusCode = responseMsg.StatusCode,
                        Message = responseMsg.ReasonPhrase ?? responseMsg.StatusCode.ToString()
                    };
                }
            }
        }
        catch (HttpRequestException exc)
        {
            var statusCode = exc.StatusCode ?? System.Net.HttpStatusCode.InternalServerError;
            if (data != null)
            {
                data.Response = new T()
                {
                    StatusCode = statusCode,
                    Message = exc.Message ?? statusCode.ToString()
                };
            }
        }
        catch (UriFormatException)
        {
            Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]: Bad URI format.");
            if (data != null)
            {
                data.Response = new T()
                {
                    StatusCode = System.Net.HttpStatusCode.BadRequest,
                    Message = "Bad request"
                };
            }
        }
        catch (InvalidOperationException)
        {
            Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]: Invalid operation.");
            if (data != null)
            {
                data.Response = new T()
                {
                    StatusCode = System.Net.HttpStatusCode.BadRequest,
                    Message = "Bad request"
                };
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A failure occurred executing API method [{method}] on service [{service}].");
            if (data != null)
            {
                data.Response = new T()
                {
                    StatusCode = System.Net.HttpStatusCode.InternalServerError,
                    Message = "Internal service error"
                };
            }
        }

        return false;
    }
}
