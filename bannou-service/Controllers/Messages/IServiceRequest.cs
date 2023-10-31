namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The interface that message request models to service endpoints can implement to
/// simplify the transfer of shared properties (such as headers) to the corresponding
/// response objects.
/// 
/// Use CreateResponse() to create a response that already has headers and other
/// "transfer properties" copied over.
/// </summary>
public interface IServiceRequest<T>
    where T : class, IServiceResponse, new()
{
    T CreateResponse()
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
                catch { }
            }
        }
        catch { }

        return responseObj ?? new();
    }
}

/// <summary>
/// The interface all message payload models to service endpoints should implement.
/// </summary>
public interface IServiceRequest
{
}
