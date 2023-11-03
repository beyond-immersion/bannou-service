using System.Net.Mime;
using System.Text;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service request payload model.
/// </summary>
[JsonObject]
public abstract class ServiceRequest : ServiceMessage
{
    [JsonIgnore]
    protected static HttpClient HttpClient { get; } = new();

    [JsonIgnore]
    public ServiceResponse? Response { get; protected set; }

    public virtual async Task<bool> ExecuteRequestToAPI<T>(string? service, string method)
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

            var requestData = JsonConvert.SerializeObject(this);
            var requestContent = new StringContent(requestData, Encoding.UTF8, MediaTypeNames.Application.Json);

            var headerLookup = SetPropertiesToHeaders();
            foreach (var headerKVP in headerLookup)
                foreach (var headerValue in headerKVP.Item2)
                    if (!string.IsNullOrWhiteSpace(headerKVP.Item1) && !string.IsNullOrWhiteSpace(headerValue))
                        requestContent.Headers.Add(headerKVP.Item1, headerValue);

            var responseMsg = await HttpClient.PostAsync(requestUrl, requestContent, Program.ShutdownCancellationTokenSource.Token);
            if (responseMsg.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(ServiceResponse))
                {
                    Response = new()
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
                Response = new()
                {
                    StatusCode = responseMsg.StatusCode,
                    Message = responseMsg.ReasonPhrase ?? responseMsg.StatusCode.ToString()
                };
            }
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
        catch (UriFormatException)
        {
            Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]: Bad URI format.");
            Response = new()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                Message = "Bad request"
            };
        }
        catch (InvalidOperationException)
        {
            Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]: Invalid operation.");
            Response = new()
            {
                StatusCode = System.Net.HttpStatusCode.BadRequest,
                Message = "Bad request"
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

    public virtual async Task<bool> ExecuteRequestToAPI(string? service, string method)
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? "bannou";

            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"{service}/{method}";
            else
                requestUrl = $"{method}";

            HttpRequestMessage requestMsg = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, coordinatorService, requestUrl, this);
            requestMsg.AddPropertyHeaders(this);

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
}
