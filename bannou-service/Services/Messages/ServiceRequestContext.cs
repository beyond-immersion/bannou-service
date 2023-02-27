using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Google.Rpc;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// Request service context base.
    /// See: <seealso cref="ServiceRequestContext{S}"/> and
    /// <seealso cref="ServiceMessageContext{T, S}"/>.
    /// </summary>
    public abstract class ServiceMessageContext
    {
        /// <summary>
        /// HttpContext of client request to endpoint.
        /// </summary>
        public HttpContext HttpContext { get; }

        private ServiceMessageContext() { }
        public ServiceMessageContext(HttpContext httpContext) => HttpContext = httpContext;

        /// <summary>
        /// Helper method for generating and sending a JSON response to the client.
        /// </summary>
        public void SendResponse<T>(T? data = null)
            where T : class, IServiceResponse
            => HttpContext.SendResponse(data);

        /// <summary>
        /// Async helper method for generating and sending a JSON response to the client.
        /// </summary>
        public async Task SendResponseAsync<T>(T? data = null, CancellationToken cancellationToken = default)
            where T : class, IServiceResponse
            => await HttpContext.SendResponseAsync(data, cancellationToken);

        /// <summary>
        /// Async helper method for generating and sending a JSON response to the client.
        /// </summary>
        public async Task SetAndSendResponseAsync<T>(ResponseCodes responseCode, string? message = null, T? data = null, CancellationToken cancellationToken = default)
            where T : class, IServiceResponse
            => await HttpContext.SendResponseAsync(data, cancellationToken);
    }

    /// <summary>
    /// A wrapper around the HTTPContext of service requests.
    /// 
    /// Automates generating a response obj from the appropriate data model.
    /// </summary>
    public class ServiceResponseContext<S> : ServiceMessageContext
        where S : IServiceResponse
    {
        /// <summary>
        /// Instantiated service response object-
        /// add additional results and send back.
        /// </summary>
        public S Response { get; }

        public ServiceResponseContext(HttpContext httpContext, S response)
            : base(httpContext)
        {
            Response = response;
        }

        /// <summary>
        /// Helper method for generating and sending a JSON response to the client.
        /// </summary>
        public void SendResponse()
            => HttpContext.SendResponse(Response);

        /// <summary>
        /// Set fixed service response, based on a given response code.
        /// </summary>
        public void SetAndSendResponse(ResponseCodes responseCode, string? message = null)
        {
            Response.SetResponse(responseCode, message);
            SendResponse();
        }

        /// <summary>
        /// Async helper method for generating and sending a JSON response to the client.
        /// </summary>
        public async Task SendResponseAsync(CancellationToken cancellationToken = default)
            => await HttpContext.SendResponseAsync(Response, cancellationToken);

        /// <summary>
        /// Set fixed service response, based on a given response code.
        /// </summary>
        public async Task SetAndSendResponseAsync(ResponseCodes responseCode, string? message = null, CancellationToken cancellationToken = default)
        {
            Response.SetResponse(responseCode, message);
            await SendResponseAsync(cancellationToken);
        }
    }

    /// <summary>
    /// A wrapper around the HTTPContext of service requests.
    /// 
    /// Automates generating request and response objects from data models.
    /// </summary>
    public class ServiceMessageContext<T, S> : ServiceResponseContext<S>
        where T : IServiceRequest
        where S : IServiceResponse
    {
        /// <summary>
        /// Data model for service request params.
        /// </summary>
        public T Request { get; }

        public ServiceMessageContext(HttpContext httpContext, T request, S response)
            : base(httpContext, response)
        {
            Request = request;
        }
    }
}
