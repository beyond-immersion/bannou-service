using System.Reflection;
using System.Text;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public class ServiceRequest<T> : ServiceRequest
    where T : ServiceResponse, new()
{
    public T CreateResponse()
    {
        var requestType = GetType();
        var responseType = typeof(T);
        T? responseObj = null;

        try
        {
            responseObj = Activator.CreateInstance(responseType, true) as T;
            if (responseObj == null)
            {
                Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to create instance of response type [{responseType.Name}].");
                return new();
            }

            var requestProps = requestType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => t.GetCustomAttribute<HeaderArrayAttribute>(true) != null);

            if (requestProps == null || !requestProps.Any())
            {
                Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to fetch header properties on request type [{requestType.Name}].");
                return responseObj;
            }

            var responseProps = responseType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => t.GetCustomAttribute<HeaderArrayAttribute>(true) != null);

            if (responseProps == null || !responseProps.Any())
            {
                Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to fetch header properties on response type [{responseType.Name}].");
                return responseObj;
            }

            foreach (var responseProp in responseProps)
            {
                try
                {
                    var requestProp = requestProps.First(requestProp => string.Equals(responseProp.Name, requestProp.Name));
                    if (requestProp != null && responseProp.PropertyType.IsAssignableFrom(requestProp.PropertyType))
                        responseProp.SetValue(responseObj, requestProp.GetValue(this));
                    else
                        Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to set header property value " +
                            $"from request type [{requestType.Name}] to response type [{responseType.Name}].");
                }
                catch (Exception exc)
                {
                    Program.Logger.Log(LogLevel.Error, exc, $"An exception was thrown copying property from [{requestType.Name}] to [{responseType.Name}].");
                }
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception was thrown using request model [{requestType.Name}] to create response model [{responseType.Name}].");
        }

        return responseObj ?? new();
    }

    public new virtual async Task<T?> ExecuteRequestToAPI(string? service, string method)
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
}
