using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// Request service context base.
    /// See: <seealso cref="ServiceRequestContext{T, S}"/>.
    /// 
    /// Utilized exclusively for easier searching of derived types.
    /// </summary>
    public abstract class RequestContextBase { }

    /// <summary>
    /// A wrapper around the HTTPContext of service requests.
    /// 
    /// Helps automate parsing requests to the appropriate data models.
    /// </summary>
    public sealed class ServiceRequestContext<T, S> : ServiceRequestBase
        where T : IServiceRequest
        where S : IServiceResponse
    {
        /// <summary>
        /// HttpContext of client request to endpoint.
        /// </summary>
        public HttpContext HttpContext { get; }

        /// <summary>
        /// Data model for service request params.
        /// </summary>
        public T Request { get; }

        /// <summary>
        /// Instantiated service response object-
        /// add additional results and send back.
        /// </summary>
        public S Response { get; }

        public ServiceRequestContext() { }
        public ServiceRequestContext(HttpContext httpContext, T request, S response)
        {
            HttpContext = httpContext;
            Request = request;
            Response = response;
        }
    }
}
