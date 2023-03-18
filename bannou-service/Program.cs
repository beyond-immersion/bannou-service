using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Services.Messages;
using Dapr.Client;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace BeyondImmersion.BannouService
{
    public static class Program
    {
        /// <summary>
        /// Service configuration- pulled from Config.json, ENVs, and command switches.
        /// </summary>
        public static ServiceConfiguration Configuration { get; private set; }

        /// <summary>
        /// Service logger.
        /// </summary>
        public static ILogger Logger { get; private set; }

        /// <summary>
        /// Internal service GUID- largely used for administrative network commands.
        /// Randomly generated on service startup.
        /// </summary>
        public static string ServiceGUID { get; private set; }

        /// <summary>
        /// Shared dapr client interface, used by all enabled internal services.
        /// </summary>
        internal static DaprClient DaprClient { get; private set; }

        /// <summary>
        /// Token source for initiating a clean shutdown.
        /// </summary>
        internal static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

        /// <summary>
        /// Internal registry of all loaded dapr services.
        /// </summary>
        private static Dictionary<string, (DaprServiceAttribute, IDaprService)> ServiceRegistry { get; }
            = new Dictionary<string, (DaprServiceAttribute, IDaprService)>();

        private static async Task Main(string[] args)
        {
            Logger = ServiceLogging.CreateLogger();

            Logger.Log(LogLevel.Debug, null, "Service starting.");

            Configuration = ServiceConfiguration.BuildConfiguration(args, "BANNOU_");
            if (!ValidateConfiguration())
                return;

            ServiceGUID = Configuration.ForceServiceID ?? Guid.NewGuid().ToString().ToLower();

            WebApplication? webApp = WebApplication.CreateBuilder(args)?.Build();
            if (webApp == null)
            {
                Logger.Log(LogLevel.Error, null, "Building web application failed- exiting application.");
                return;
            }

            DaprClient = new DaprClientBuilder()
                .UseJsonSerializationOptions(ServiceConfiguration.DaprSerializerConfig)
                .Build();

            if (!await DaprClient.CheckHealthAsync(ShutdownCancellationTokenSource.Token))
            {
                Logger.Log(LogLevel.Error, null, "Dapr sidecar unhealthy/not found- exiting application.");
                return;
            }

            try
            {
                BuildDaprServiceRegistry();

                SetAdminEndpoints(webApp);
                var unused = AddDaprServiceEndpoints(webApp);

                Logger.Log(LogLevel.Debug, null, "Service startup complete- webhost starting.");
                {
                    // blocks until webhost dies / server shutdown command received
                    await Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token), ShutdownCancellationTokenSource.Token);
                }

                Logger.Log(LogLevel.Debug, null, "Webhost stopped- starting controlled service shutdown.");

                StopRegisteredDaprServices();
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e, "A critical error has occurred- starting service shutdown.");
            }
            finally
            {
                await webApp.DisposeAsync();
                DaprClient.Dispose();
            }

            Logger.Log(LogLevel.Debug, null, "Service shutdown complete.");
        }

        /// <summary>
        /// Will stop the webhost and initiate a service shutdown.
        /// </summary>
        public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();

        /// <summary>
        /// Verifies that the service configuration contains required values (from ENVs/switches/etc).
        /// </summary>
        private static bool ValidateConfiguration()
        {
            if (Configuration == null)
            {
                Logger.Log(LogLevel.Error, null, "Service configuration required, even if only with default values.");
                return false;
            }

            if (!Configuration.IsAnyServiceEnabled())
            {
                Logger.Log(LogLevel.Error, null, "Dapr services not configured to handle any roles / APIs.");
                return false;
            }

            foreach ((Type, DaprServiceAttribute) serviceClassData in GetDaprServiceTypes())
            {
                Type serviceType = serviceClassData.Item1;

                if (!Configuration.IsServiceEnabled(serviceType))
                    continue;

                if (!Configuration.HasRequiredConfiguration(serviceType))
                {
                    Logger.Log(LogLevel.Debug, null, $"Required configuration is missing to start an enabled dapr service.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Binds the HTTP endpoints for root administrative commands against this service against.
        /// </summary>
        private static void SetAdminEndpoints(WebApplication webApp) => webApp.MapGet($"/admin_{ServiceGUID}/shutdown", InitiateShutdown);

        /// <summary>
        /// Finds all dapr service classes in loaded assemblies, determines which need to be enabled
        /// based on the current configuration, and instantiates new instances of the services.
        /// 
        /// Places service instances (along with their service attribute) in the local registry.
        /// </summary>
        private static void BuildDaprServiceRegistry()
        {
            foreach ((Type, DaprServiceAttribute) serviceClassData in GetDaprServiceTypes())
            {
                Type serviceType = serviceClassData.Item1;
                DaprServiceAttribute serviceAttr = serviceClassData.Item2;

                if (!Configuration.IsServiceEnabled(serviceType))
                    continue;

                try
                {
                    var serviceInstance = (IDaprService?)Activator.CreateInstance(type: serviceType, nonPublic: true);
                    if (serviceInstance == null)
                        throw new NullReferenceException();

                    ServiceRegistry[serviceAttr.ServicePrefix] = (serviceAttr, serviceInstance);

                    Logger.Log(LogLevel.Debug, null, $"Instantiation of dapr service successful.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, e, $"Instantiation of dapr service failed.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                }
            }
        }

        /// <summary>
        /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
        /// </summary>
        private static (Type, DaprServiceAttribute)[] GetDaprServiceTypes()
        {
            List<(Type, DaprServiceAttribute)> serviceClasses = BaseServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
            if (!serviceClasses.Any())
            {
                Logger.Log(LogLevel.Error, null, $"No dapr services found to instantiate.");
                return Array.Empty<(Type, DaprServiceAttribute)>();
            }

            // prefixes need to be unique, so assign to a tmp hash/dictionary lookup
            var serviceLookup = new Dictionary<string, (Type, DaprServiceAttribute)>();
            foreach ((Type, DaprServiceAttribute) serviceClass in serviceClasses)
            {
                Type serviceType = serviceClass.Item1;
                DaprServiceAttribute serviceAttr = serviceClass.Item2;

                if (!typeof(IDaprService).IsAssignableFrom(serviceType))
                {
                    Logger.Log(LogLevel.Error, null, $"Dapr service attribute attached to a non-service class.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                    continue;
                }

                var servicePrefix = serviceAttr.ServicePrefix.ToUpper();
                if (!serviceLookup.ContainsKey(servicePrefix) || serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                    serviceLookup[servicePrefix] = serviceClass;
            }

            return serviceLookup.Values.ToArray();
        }

        /// <summary>
        /// Stops all dapr services instances in the registry, before clearing out the registry itself.
        /// </summary>
        private static void StopRegisteredDaprServices()
        {
            foreach ((DaprServiceAttribute, IDaprService) serviceData in ServiceRegistry.Values)
                serviceData.Item2.Shutdown();

            ServiceRegistry.Clear();
        }

        /// <summary>
        /// Binds HTTP endpoints for all registered dapr services.
        /// </summary>
        private static bool AddDaprServiceEndpoints(WebApplication webApp)
        {
            var endpointAdded = false;
            foreach ((DaprServiceAttribute, IDaprService) serviceClass in ServiceRegistry.Values)
            {
                DaprServiceAttribute serviceAttr = serviceClass.Item1;
                IDaprService serviceInstance = serviceClass.Item2;

                // iterate route methods for service, add to webapp
                foreach ((MethodInfo, ServiceRoute) routeMethod in BaseServiceAttribute.GetMethodsWithAttribute<ServiceRoute>(serviceClass.Item2.GetType()))
                {
                    MethodInfo routeMethodInfo = routeMethod.Item1;
                    ServiceRoute routeAttr = routeMethod.Item2;

                    try
                    {
                        Delegate methodDelegate = CreateServiceEndpointDelegate(routeMethodInfo, serviceInstance);

                        var routePrefix = serviceAttr.ServicePrefix ?? "";
                        if (string.IsNullOrWhiteSpace(routePrefix))
                        {
                            if (!routePrefix.StartsWith('/'))
                                routePrefix = $"/{routePrefix}";

                            if (routePrefix.EndsWith('/'))
                                routePrefix = routePrefix.Remove(routePrefix.Length - 1, 1);
                        }

                        var routeSuffix = routeAttr.RouteUrl ?? "";
                        if (!string.IsNullOrWhiteSpace(routeSuffix))
                        {
                            routeSuffix = routeSuffix.Replace(ServiceConstants.SERVICE_UUID_PLACEHOLDER, ServiceGUID);

                            if (routeSuffix.StartsWith('/'))
                                routeSuffix = routeSuffix.Remove(0, 1);

                            if (routeSuffix.EndsWith('/'))
                                routeSuffix = routeSuffix.Remove(routeSuffix.Length - 1, 1);
                        }

                        switch (routeAttr.HttpMethod)
                        {
                            case HttpMethodTypes.GET:
                                _ = webApp.MapGet($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            case HttpMethodTypes.POST:
                                _ = webApp.MapPost($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            case HttpMethodTypes.PUT:
                                _ = webApp.MapPut($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            case HttpMethodTypes.DELETE:
                                _ = webApp.MapDelete($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            default:
                                Logger.Log(LogLevel.Error, null, $"Unsupported dapr service endpoint type.",
                                    logParams: new JObject()
                                    {
                                        ["service_type"] = serviceInstance.GetType().Name,
                                        ["http_type"] = routeAttr.HttpMethod.ToString(),
                                        ["method"] = routeMethodInfo.Name
                                    });
                                continue;
                        }

                        Logger.Log(LogLevel.Debug, null, $"Dapr service endpoint added successfully.",
                            logParams: new JObject()
                            {
                                ["service_type"] = serviceInstance.GetType().Name,
                                ["http_type"] = routeAttr.HttpMethod.ToString(),
                                ["method"] = routeMethodInfo.Name
                            });
                    }
                    catch (Exception e)
                    {
                        Logger.Log(LogLevel.Error, e, $"Failed to add dapr service endpoint.",
                            logParams: new JObject()
                            {
                                ["service_type"] = serviceInstance.GetType().Name,
                                ["http_type"] = routeAttr.HttpMethod.ToString(),
                                ["method"] = routeMethodInfo.Name
                            });
                    }
                }
            }

            return endpointAdded;
        }

        /// <summary>
        /// Create delegate to HTTP service endpoint. Method can be static or instance.
        /// 
        /// Method must return void or async Task.
        /// Method must be parameterless, or accept a single parameter of type <see cref="HttpContext"/> or <see cref="ServiceMessageContext{T, S}"/>.
        /// </summary>
        private static Delegate CreateServiceEndpointDelegate(MethodInfo methodInfo, IDaprService service)
        {
            var methodParameters = methodInfo.GetParameters();
            if (methodParameters.Length > 1)
                throw new Exception($"Dapr service endpoints can only contain one parameter, or none.");

            if (methodParameters.Length == 1 && !new Type[] { typeof(HttpContext), typeof(ServiceMessageContext) }
                .Any(t => t.IsAssignableFrom(methodParameters.First().ParameterType)))
                throw new Exception($"Dapr service endpoints can only contain one parameter, of type HttpContext or derived from ServiceRequestContext.");

            if (!new[] { typeof(void), typeof(Task) }.Contains(methodInfo.ReturnType))
                throw new Exception($"Dapr service endpoints must have void or async/task return type.");

            Delegate? methodDelegate = null;
            if (!methodParameters.Any())
                methodDelegate = CreateParameterlessDelegateWrapper(methodInfo, service);
            else if (typeof(HttpContext).IsAssignableFrom(methodParameters.First().ParameterType))
                methodDelegate = CreateHTTPDelegateWrapper(methodInfo, service);
            else if (typeof(ServiceMessageContext).IsAssignableFrom(methodParameters.First().ParameterType))
                methodDelegate = CreateContextDelegateWrapper(methodInfo, service);

            if (methodDelegate == null)
                throw new InvalidOperationException("Could not create static service endpoint delegate.");

            return methodDelegate;
        }

        /// <summary>
        /// Create wrapper delegate for making requests into service endpoints.
        /// </summary>
        private static Delegate? CreateParameterlessDelegateWrapper(MethodInfo methodInfo, IDaprService service)
        {
            Delegate methodDelegate;
            if (methodInfo.ReturnType == typeof(Task))
            {
                if (methodInfo.IsStatic)
                    methodDelegate = Delegate.CreateDelegate(typeof(Func<Task>), target: service.GetType(), method: methodInfo.Name);
                else
                    methodDelegate = Delegate.CreateDelegate(typeof(Func<Task>), target: service, method: methodInfo.Name);
            }
            else
            {
                if (methodInfo.IsStatic)
                    methodDelegate = Delegate.CreateDelegate(typeof(Action), target: service.GetType(), method: methodInfo.Name);
                else
                    methodDelegate = Delegate.CreateDelegate(typeof(Action), target: service, method: methodInfo.Name);
            }

            return async (HttpContext context) =>
            {
                try
                {
                    _ = methodDelegate.DynamicInvoke();
                    await context.SendResponseAsync();
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.ServerError, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "parameterless"
                        });
                }
            };
        }

        /// <summary>
        /// Create wrapper delegate for making requests into service endpoints.
        /// </summary>
        private static Delegate? CreateHTTPDelegateWrapper(MethodInfo methodInfo, IDaprService service)
        {
            Delegate methodDelegate;
            if (methodInfo.ReturnType == typeof(Task))
            {
                if (methodInfo.IsStatic)
                    methodDelegate = Delegate.CreateDelegate(typeof(Func<HttpContext, Task>), target: service.GetType(), method: methodInfo.Name);
                else
                    methodDelegate = Delegate.CreateDelegate(typeof(Func<HttpContext, Task>), target: service, method: methodInfo.Name);
            }
            else
            {
                if (methodInfo.IsStatic)
                    methodDelegate = Delegate.CreateDelegate(typeof(Action<HttpContext>), target: service.GetType(), method: methodInfo.Name);
                else
                    methodDelegate = Delegate.CreateDelegate(typeof(Action<HttpContext>), target: service, method: methodInfo.Name);
            }

            return async (HttpContext context) =>
            {
                try
                {
                    _ = methodDelegate.DynamicInvoke(context);

                    // automatically send back response, if that wasn't handled by the method
                    if (!context.Response.HasStarted)
                        await context.SendResponseAsync();
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.ServerError, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "HTTP context"
                        });
                }
            };
        }

        /// <summary>
        /// Create wrapper delegate for making requests into service endpoints.
        /// Automatically handles parsing the request fields into an object model,
        /// as well as instantiating a usable response object for the method.
        /// </summary>
        private static Delegate CreateContextDelegateWrapper(MethodInfo methodInfo, IDaprService service)
        {
            Type contextType = methodInfo.GetParameters()[0].ParameterType;
            if (!contextType.IsGenericType)
                throw new ArgumentException($"Service contexts must be generic.");

            var includeRequest = false;
            switch (contextType.GenericTypeArguments.Length)
            {
                case 1:
                    if (!typeof(IServiceResponse).IsAssignableFrom(contextType.GenericTypeArguments[0]))
                        throw new ArgumentException($"The generic type argument for a response context must implement {nameof(IServiceResponse)}.");

                    if (contextType.GenericTypeArguments[0].IsAbstract || !contextType.GenericTypeArguments[0].IsClass)
                        throw new ArgumentException($"The generic type arguments for a response context must be instantiable classes.");

                    break;
                case >= 2:
                    if (!typeof(IServiceRequest).IsAssignableFrom(contextType.GenericTypeArguments[0]))
                        throw new ArgumentException($"The first generic type argument for a request/response context must implement {nameof(IServiceRequest)}.");

                    if (!typeof(IServiceResponse).IsAssignableFrom(contextType.GenericTypeArguments[1]))
                        throw new ArgumentException($"The second generic type argument for a request/response context must implement {nameof(IServiceResponse)}.");

                    if (contextType.GenericTypeArguments[0].IsAbstract || !contextType.GenericTypeArguments[0].IsClass
                        || contextType.GenericTypeArguments[1].IsAbstract || !contextType.GenericTypeArguments[1].IsClass)
                        throw new ArgumentException($"The generic type arguments for a request/response context must be instantiable classes.");

                    includeRequest = true;
                    break;
                default:
                    throw new ArgumentException($"Service contexts must contain 1 or more generic type arguments.");
            }

            Delegate methodDelegate;
            if (methodInfo.ReturnType == typeof(Task))
            {
                if (methodInfo.IsStatic)
                    methodDelegate = Delegate.CreateDelegate(Expression.GetFuncType(contextType, typeof(Task)), target: service.GetType(), method: methodInfo.Name);
                else
                    methodDelegate = Delegate.CreateDelegate(Expression.GetFuncType(contextType, typeof(Task)), target: service, method: methodInfo.Name);
            }
            else
            {
                if (methodInfo.IsStatic)
                    methodDelegate = Delegate.CreateDelegate(Expression.GetActionType(contextType), target: service.GetType(), method: methodInfo.Name);
                else
                    methodDelegate = Delegate.CreateDelegate(Expression.GetActionType(contextType), target: service, method: methodInfo.Name);
            }

            if (includeRequest)
                return CreateMessageDelegate(methodDelegate, service, contextType);
            else
                return CreateResponseDelegate(methodDelegate, service, contextType);

            throw new ArgumentException($"Service context structure is not supported- please read documentation on extending the context type.");
        }

        private static Delegate CreateResponseDelegate(Delegate methodDelegate, IDaprService service, Type contextType)
        {
            return async (HttpContext context) =>
            {
                IServiceResponse? responseObj = null;
                try
                {
                    responseObj = (IServiceResponse?)Activator.CreateInstance(type: contextType.GenericTypeArguments[0], nonPublic: true);
                    if (responseObj == null)
                        throw new Exception("Failed to instantiate the response data model.");
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.ServerError, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "response context",
                            ["failure_point"] = "instantiate response"
                        });
                    return;
                }

                try
                {
                    ///
                    /// See constructor: <see cref="ServiceResponseContext{S}"/>
                    ///

                    var requestContext = Activator.CreateInstance(type: contextType, context, responseObj);
                    if (requestContext == null)
                        throw new Exception("Failed to instantiate the message context container.");

                    _ = methodDelegate.DynamicInvoke(requestContext);
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.ServerError, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "response context",
                            ["failure_point"] = "invoke method"
                        });
                    return;
                }

                try
                {
                    if (!context.Response.HasStarted)
                        await context.SendResponseAsync(responseObj);
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "response context",
                            ["failure_point"] = "auto response"
                        });
                }
            };
        }

        private static Delegate CreateMessageDelegate(Delegate methodDelegate, IDaprService service, Type contextType)
        {
            return async (HttpContext context) =>
            {
                IServiceRequest? requestObj = null;
                try
                {
                    if (context.Request.ContentLength > 0)
                    {
                        if (!string.Equals(System.Net.Mime.MediaTypeNames.Application.Json, context.Request.ContentType, StringComparison.InvariantCultureIgnoreCase))
                            throw new Exception("The request content type is not application/json.");

                        if (!context.Request.HasJsonContentType())
                            throw new Exception("The request content is not valid JSON.");

                        requestObj = (IServiceRequest?)await context.Request.ReadFromJsonAsync(contextType.GenericTypeArguments[0], ShutdownCancellationTokenSource.Token);
                        if (requestObj == null)
                            throw new Exception("Required fields for the service request are missing.");
                    }
                    else
                    {
                        // handle headers - create JObject, then convert to model type once headers are added
                        // - the Json converter itself will determine if the requirements are met
                        throw new Exception("Header-only request models are not supported yet.");
                    }
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.BadRequest, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "request/response context",
                            ["failure_point"] = "parse request"
                        });
                    return;
                }

                IServiceResponse? responseObj = null;
                try
                {
                    responseObj = (IServiceResponse?)Activator.CreateInstance(type: contextType.GenericTypeArguments[1], nonPublic: true);
                    if (responseObj == null)
                        throw new Exception("Failed to instantiate the response data model.");
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.ServerError, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "request/response context",
                            ["failure_point"] = "instantiate response"
                        });
                    return;
                }

                try
                {
                    ///
                    /// See constructor: <see cref="ServiceMessageContext{T, S}"/>
                    ///

                    var requestContext = Activator.CreateInstance(type: contextType, context, requestObj, responseObj);
                    if (requestContext == null)
                        throw new Exception("Failed to instantiate the message context container.");

                    _ = methodDelegate.DynamicInvoke(requestContext);
                }
                catch (Exception e)
                {
                    await context.SendResponseAsync(ResponseCodes.ServerError, e.ToString());

                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "request/response context",
                            ["failure_point"] = "invoke method"
                        });
                    return;
                }

                try
                {
                    if (!context.Response.HasStarted)
                        await context.SendResponseAsync(responseObj);
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, e, $"Error processing incoming request to dapr service endpoint.",
                        logParams: new JObject()
                        {
                            ["service_type"] = service.GetType().Name,
                            ["endpoint_type"] = "request/response context",
                            ["failure_point"] = "auto response"
                        });
                }
            };
        }
    }
}
