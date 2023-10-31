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
}

/// <summary>
/// The interface all message payload models to service endpoints should implement.
/// </summary>
public interface IServiceRequest
{
}
