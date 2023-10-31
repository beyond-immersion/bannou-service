namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public class ServiceRequest<T> : ServiceRequest, IServiceRequest<T>
    where T : class, IServiceResponse, new()
{
    public T CreateResponse()
    {
        T? responseObj = null;
        try
        {
            responseObj = Activator.CreateInstance(typeof(T), true) as T;
            if (responseObj == null)
                return new();

            var requestHeaderProps = IServiceAttribute.GetPropertiesWithAttribute(GetType(), typeof(HeaderArrayAttribute));
            if (requestHeaderProps == null || requestHeaderProps.Count == 0)
                return responseObj;

            var responseHeaderProps = IServiceAttribute.GetPropertiesWithAttribute(typeof(T), typeof(HeaderArrayAttribute));
            if (responseHeaderProps == null || responseHeaderProps.Count == 0)
                return responseObj;

            foreach (var headerProp in responseHeaderProps.Select(t => t.Item1))
            {
                try
                {
                    var propertyName = headerProp.Name;
                    var requestProp = requestHeaderProps.First(t => string.Equals(propertyName, t.Item1.Name)).Item1;
                    headerProp.SetValue(responseObj, requestProp.GetValue(this));
                }
                catch (Exception exc)
                {
                    Program.Logger.Log(LogLevel.Error, exc, $"An exception was thrown copying property from [{GetType().Name}] to [{typeof(T).Name}].");
                }
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception was thrown using request model [{GetType().Name}] to create response model [{typeof(T).Name}].");
        }

        return responseObj ?? new();
    }
}

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public class ServiceRequest : IServiceRequest
{
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string>? RequestIDs { get; set; }
}
