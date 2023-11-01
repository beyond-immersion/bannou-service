using System.Text;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service request payload model.
/// </summary>
[JsonObject]
public abstract class ServiceRequest : ServiceMessage
{
    protected const string COORDINATOR_APP = "bannou";
    protected static readonly HttpClient HttpClient = new();

    public virtual async Task<T?> ExecuteRequestToAPI<T>(string? service, string method)
        where T : ServiceResponse, new()
    {
        try
        {
            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"http://127.0.0.1:3500/v1.0/invoke/{COORDINATOR_APP}/method/{service}/{method}";
            else
                requestUrl = $"http://127.0.0.1:3500/v1.0/invoke/{COORDINATOR_APP}/method/{method}";

            var requestData = JsonConvert.SerializeObject(this);
            var requestContent = new StringContent(requestData, Encoding.UTF8, "application/json");

            var headerLookup = SetPropertiesToHeaders();
            foreach (var headerKVP in headerLookup)
                foreach (var headerValue in headerKVP.Item2)
                    requestContent.Headers.Add(headerKVP.Item1, headerValue);

            var responseMsg = await HttpClient.PostAsync(requestUrl, requestContent, Program.ShutdownCancellationTokenSource.Token);
            if (responseMsg.IsSuccessStatusCode)
            {
                var responseContent = await responseMsg.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<T>(responseContent);
                responseData?.SetHeadersToProperties(responseMsg.Headers);
                return responseData;
            }
            else
            {
                Program.Logger.Log(LogLevel.Error, $"Failed to invoke API method [{method}] on service [{service}]. HTTP Status: {responseMsg.StatusCode}");
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A failure occurred executing API method [{method}] on service [{service}].");
        }

        return null;
    }

    public virtual async Task<bool> ExecuteRequestToAPI(string? service, string method)
    {
        try
        {
            string? requestUrl = null;
            if (!string.IsNullOrWhiteSpace(service))
                requestUrl = $"{service}/{method}";
            else
                requestUrl = $"{method}";

            HttpRequestMessage requestMsg = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", requestUrl, this);
            requestMsg.AddPropertyHeaders(this);

            await Program.DaprClient.InvokeMethodAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A failure occurred executing API method [{method}] on service [{service}].");
            return false;
        }

        return true;
    }
}
