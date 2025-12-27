using BeyondImmersion.BannouService.Configuration;
using System.Net.Mime;
using System.Text;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic API controller request payload model.
/// </summary>
public class ApiRequest : ApiMessage
{
    private static HttpClient _httpClient;
    /// <summary>
    /// HTTP client for making service requests.
    /// </summary>
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

    /// <summary>
    /// The response from the API request.
    /// </summary>
    [JsonIgnore]
    public ApiResponse? Response { get; protected set; }

    /// <summary>
    /// Executes an API request with a typed response.
    /// </summary>
    /// <typeparam name="T">The type of response expected.</typeparam>
    /// <param name="service">The target service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="additionalHeaders">Optional additional headers.</param>
    /// <param name="httpMethod">The HTTP method to use.</param>
    /// <returns>True if the request was successful, false otherwise.</returns>
    public virtual async Task<bool> ExecuteRequest<T>(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        where T : ApiResponse, new()
    {
        if (typeof(ApiRequest<T>).IsAssignableFrom(GetType()))
        {
            if (this is not ApiRequest<T> derivedRequest)
                return false;

            // calling execute on the derived type will also parse and set
            // the more specific Response type that's expected, on success
            return await derivedRequest.ExecuteRequest(service, method, additionalHeaders, httpMethod);
        }

        return await ExecuteRequest_INTERNAL<T>(service, method, additionalHeaders, httpMethod);
    }

    /// <summary>
    /// Executes an API request.
    /// </summary>
    /// <param name="service">The target service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="additionalHeaders">Optional additional headers.</param>
    /// <param name="httpMethod">The HTTP method to use.</param>
    /// <returns>True if the request was successful, false otherwise.</returns>
    public virtual async Task<bool> ExecuteRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        => await ExecuteRequest_INTERNAL(service, method, additionalHeaders, httpMethod);

    /// <summary>
    /// Internal implementation for executing API requests.
    /// </summary>
    /// <param name="service">The target service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="additionalHeaders">Optional additional headers.</param>
    /// <param name="httpMethod">The HTTP method to use.</param>
    /// <returns>True if the request was successful, false otherwise.</returns>
    protected async Task<bool> ExecuteRequest_INTERNAL(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? AppConstants.DEFAULT_APP_NAME;

            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"{service}/{method}";
            else
                requestUrl = $"{method}";

            if (Program.MeshInvocationClient == null)
            {
                throw new InvalidOperationException("MeshInvocationClient not initialized");
            }

            HttpRequestMessage requestMsg = Program.MeshInvocationClient.CreateInvokeMethodRequest(httpMethod.ToObject(), coordinatorService, requestUrl);
            requestMsg.Content = new StringContent(BannouJson.Serialize(this), System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Application.Json);
            requestMsg.AddPropertyHeaders(this);

            if (additionalHeaders != null)
                foreach (var headerKVP in additionalHeaders)
                    requestMsg.Headers.Add(headerKVP.Key, headerKVP.Value);

            await Program.MeshInvocationClient.InvokeMethodWithResponseAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
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

    /// <summary>
    /// Internal implementation for executing typed API requests.
    /// </summary>
    /// <typeparam name="T">The type of response expected.</typeparam>
    /// <param name="service">The target service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="additionalHeaders">Optional additional headers.</param>
    /// <param name="httpMethod">The HTTP method to use.</param>
    /// <returns>True if the request was successful, false otherwise.</returns>
    protected async Task<bool> ExecuteRequest_INTERNAL<T>(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
        where T : ApiResponse, new()
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? AppConstants.DEFAULT_APP_NAME;

            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"http://127.0.0.1:3500/v1.0/invoke/{coordinatorService}/method/{service}/{method}";
            else
                requestUrl = $"http://127.0.0.1:3500/v1.0/invoke/{coordinatorService}/method/{method}";

            StringContent? requestContent = null;
            if (httpMethod == HttpMethodTypes.POST)
            {
                var requestData = BannouJson.Serialize(this, GetType());
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
                if (typeof(T) == typeof(ApiResponse))
                {
                    Response = new T()
                    {
                        StatusCode = System.Net.HttpStatusCode.OK,
                        Message = "OK"
                    };
                    return true;
                }

                var responseContent = await responseMsg.Content.ReadAsStringAsync();
                var responseData = BannouJson.DeserializeRequired<T>(responseContent);

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
