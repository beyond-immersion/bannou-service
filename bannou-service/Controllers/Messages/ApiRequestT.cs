using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic API controller request payload model.
/// </summary>
public class ApiRequest<T> : ApiRequest
    where T : ApiResponse, new()
{
    /// <summary>
    /// The typed response from the API request.
    /// </summary>
    public new T? Response { get; protected set; }

    /// <summary>
    /// Executes an API request with typed response handling.
    /// </summary>
    /// <param name="service">The target service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="additionalHeaders">Optional additional headers.</param>
    /// <param name="httpMethod">The HTTP method to use.</param>
    /// <returns>True if the request was successful, false otherwise.</returns>
    public new virtual async Task<bool> ExecuteRequest(string? service, string method, IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, HttpMethodTypes httpMethod = HttpMethodTypes.POST)
    {
        var result = await ExecuteRequest_INTERNAL<T>(service, method, additionalHeaders, httpMethod);

        if (base.Response != null)
        {
            try
            {
                this.Response = base.Response as T;
            }
            catch (Exception ex)
            {
                Program.Logger.Log(LogLevel.Warning, ex, "Failed to cast response to type {ResponseType}", typeof(T).Name);
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a response instance and copies header properties from the request.
    /// </summary>
    /// <returns>A new response instance of type T.</returns>
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
                return responseObj;

            var responseProps = responseType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => t.GetCustomAttribute<HeaderArrayAttribute>(true) != null);

            if (responseProps == null || !responseProps.Any())
                return responseObj;

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
}
