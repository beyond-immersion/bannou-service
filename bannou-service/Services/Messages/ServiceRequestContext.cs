using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// Request service context base.
    /// See: <seealso cref="ServiceRequestContext{T, S}"/>.
    /// 
    /// Utilized exclusively for easier searching of derived types.
    /// </summary>
    public abstract class RequestContextBase
    {
        /// <summary>
        /// HttpContext of client request to endpoint.
        /// </summary>
        public HttpContext HttpContext { get; }

        private RequestContextBase() { }
        public RequestContextBase(HttpContext httpContext) => HttpContext = httpContext;

        /// <summary>
        /// Async helper method for generating and sending a JSON response to the client.
        /// </summary>
        public async Task SendResponseAsync<T>(T data, CancellationToken cancellationToken = default)
            where T : class
            => await HttpContext.SendResponseAsync(data, cancellationToken);

        /// <summary>
        /// Helper method for generating and sending a JSON response to the client.
        /// </summary>
        public void SendResponse<T>(T data)
            where T : class
            => HttpContext.SendResponse(data);
    }

    /// <summary>
    /// A wrapper around the HTTPContext of service requests.
    /// 
    /// Helps automate parsing requests to the appropriate data models.
    /// </summary>
    public sealed class ServiceRequestContext<T, S> : RequestContextBase
        where T : IServiceRequest
        where S : IServiceResponse
    {
        /// <summary>
        /// Data model for service request params.
        /// Will be null if no request content.
        /// </summary>
        public T? Request { get; }

        /// <summary>
        /// Instantiated service response object-
        /// add additional results and send back.
        /// </summary>
        public S Response { get; }

        public ServiceRequestContext(HttpContext httpContext, T? request, S response)
            : base(httpContext)
        {
            Request = request;
            Response = response;
        }
    }
}
