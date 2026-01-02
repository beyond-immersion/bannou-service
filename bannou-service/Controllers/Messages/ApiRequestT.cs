using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// Generic API controller request payload model with typed response support.
/// Used for header property transfer between requests and responses.
/// This class is retained for the CreateResponse functionality used in unit tests.
/// </summary>
public class ApiRequest<T> : ApiRequest
    where T : ApiResponse, new()
{
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
                Program.Logger.Log(LogLevel.Error, null, "A problem occurred attempting to create instance of response type {ResponseType}", responseType.Name);
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
                        Program.Logger.Log(LogLevel.Error, null, "A problem occurred attempting to set header property value from request type {RequestType} to response type {ResponseType}", requestType.Name, responseType.Name);
                }
                catch (Exception exc)
                {
                    Program.Logger.Log(LogLevel.Error, exc, "An exception was thrown copying property from {RequestType} to {ResponseType}", requestType.Name, responseType.Name);
                }
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception was thrown using request model {RequestType} to create response model {ResponseType}", requestType.Name, responseType.Name);
        }

        return responseObj ?? new();
    }
}
