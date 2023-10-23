using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Controllers.Messages;
using System.Reflection;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Service handler responsible for testing service APIs.
/// </summary>
[DaprService("testing", priority: false)]
public class TestingService : IDaprService
{
    /// <summary>
    /// Service attribute used on static async methods returning boolean,
    /// to indicate the method performs an integration test against a service.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ServiceTestAttribute : BaseServiceAttribute
    {
        public string TestID { get; private set; }
        public Type? ServiceType { get; private set; }

        public ServiceTestAttribute(string testID, Type? serviceType = null)
        {
            if (serviceType != null && !typeof(IDaprService).IsAssignableFrom(serviceType))
                throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}.");

            TestID = testID;
            ServiceType = serviceType;
        }
    }

    private TestingServiceConfiguration? _configuration;
    public TestingServiceConfiguration Configuration
    {
        get
        {
            _configuration ??= IServiceConfiguration.BuildConfiguration<TestingServiceConfiguration>();
            return _configuration;
        }

        internal set => _configuration = value;
    }

    private IDictionary<string, IDictionary<string, Func<TestingService, Task<bool>>>> AsyncServiceTests { get; set; }

    public string? LastTestID { get; private set; }
    public string? LastTestService { get; private set; }
    public IServiceRequest? LastTestRequest { get; private set; }

    /// <summary>
    /// Resets all testing variables.
    /// </summary>
    public void ResetTestVars()
    {
        LastTestID = null;
        LastTestService = null;
        LastTestRequest = null;
    }

    /// <summary>
    /// Set last testID to come in to a test API.
    /// </summary>
    /// <param name="id"></param>
    public void SetLastTestID(string? id)
        => LastTestID = id;

    /// <summary>
    /// Set last test service to come in to a test API.
    /// </summary>
    /// <param name="service"></param>
    public void SetLastTestService(string? service)
        => LastTestService = service;

    /// <summary>
    /// Set last POST request object to test API.
    /// </summary>
    /// <param name="request"></param>
    public void SetLastPostRequest(IServiceRequest? request)
        => LastTestRequest = request;

    public TestingService()
    {
        AsyncServiceTests = new Dictionary<string, IDictionary<string, Func<TestingService, Task<bool>>>>();
        foreach ((Type, MethodInfo, ServiceTestAttribute) methodInfo in IServiceAttribute.GetMethodsWithAttribute<ServiceTestAttribute>())
        {
            var testID = methodInfo.Item3?.TestID?.ToLower();
            if (string.IsNullOrWhiteSpace(testID))
                continue;

            try
            {
                var serviceName = methodInfo.Item3?.ServiceType?.GetServiceName()?.ToLower() ?? string.Empty;
                var methodDel = (Func<TestingService, Task<bool>>?)Delegate.CreateDelegate(type: typeof(Func<TestingService, Task<bool>>), method: methodInfo.Item2, throwOnBindFailure: true);
                if (methodDel == null)
                    continue;

                if (!AsyncServiceTests.ContainsKey(serviceName))
                {
                    var newDict = new Dictionary<string, Func<TestingService, Task<bool>>>
                    {
                        { testID, methodDel }
                    };
                    AsyncServiceTests.Add(serviceName, newDict);
                }
                else
                {
                    _ = AsyncServiceTests[serviceName].TryAdd(testID, methodDel);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// API to run a given test.
    /// ServiceName can be null if unknown.
    /// </summary>
    public async Task<bool> Run(string id, string? service = null, bool defaultIfNotFound = false)
    {
        id = id.ToLower();

        Func<TestingService, Task<bool>>? testDel = null;
        if (!string.IsNullOrWhiteSpace(service))
        {
            if (AsyncServiceTests.TryGetValue(service.ToLower(), out IDictionary<string, Func<TestingService, Task<bool>>>? idLookup))
                _ = idLookup.TryGetValue(id, out testDel);
        }
        else
        {
            foreach (KeyValuePair<string, IDictionary<string, Func<TestingService, Task<bool>>>> serviceLookup in AsyncServiceTests)
            {
                if (serviceLookup.Value.ContainsKey(id))
                {
                    service = serviceLookup.Key;
                    testDel = serviceLookup.Value[id];
                    break;
                }
            }
        }

        if (testDel == null)
        {
            Program.Logger?.Log(LogLevel.Error, $"Running test '{id}' against service '{service}' failed- test not found.");
            return defaultIfNotFound;
        }

        Program.Logger?.Log(LogLevel.Debug, $"Running test '{id}' against service '{service}'.");
        return await testDel.Invoke(this);
    }

    /// <summary>
    /// API to run all tests against a given service.
    /// </summary>
    public async Task<bool> RunAllForService(string service, bool defaultIfNotFound = false, bool stopOnFailure = false)
    {
        var testsFound = false;
        var results = true;
        if (AsyncServiceTests.TryGetValue(service, out var asyncServiceTests))
        {
            foreach (KeyValuePair<string, Func<TestingService, Task<bool>>> testLookup in asyncServiceTests)
            {
                testsFound = true;
                Program.Logger?.Log(LogLevel.Debug, $"Running test '{testLookup.Key}' against service '{service}'.");

                if (!await testLookup.Value.Invoke(this))
                {
                    results = false;
                    if (stopOnFailure)
                        break;
                }
            }
        }

        if (!testsFound)
        {
            Program.Logger?.Log(LogLevel.Warning, $"No tests found to run against service '{service}'.");
            return defaultIfNotFound;
        }

        return results;
    }

    /// <summary>
    /// API to run all tests against all enabled services.
    /// </summary>
    public async Task<bool> RunAllEnabled(bool defaultIfNotFound = false, bool stopOnFailure = false)
    {
        var testsFound = false;
        var results = true;
        foreach (KeyValuePair<string, IDictionary<string, Func<TestingService, Task<bool>>>> serviceLookup in AsyncServiceTests)
        {
            var serviceName = serviceLookup.Key;

            if (!IDaprService.IsDisabled(serviceName))
            {
                foreach (KeyValuePair<string, Func<TestingService, Task<bool>>> testLookup in serviceLookup.Value)
                {
                    var testName = testLookup.Key;
                    testsFound = true;

                    Program.Logger?.Log(LogLevel.Debug, $"Running test '{testName}' against service '{serviceName}'.");

                    if (!await testLookup.Value.Invoke(this))
                    {
                        results = false;
                        if (stopOnFailure)
                            break;
                    }
                }
            }
        }

        if (!testsFound)
        {
            Program.Logger?.Log(LogLevel.Warning, $"No tests found to run against enabled services.");
            return defaultIfNotFound;
        }

        return results;
    }

    /// <summary>
    /// API to run all tests.
    /// </summary>
    public async Task<bool> RunAll(bool defaultIfNotFound = false, bool stopOnFailure = false)
    {
        var testsFound = false;
        var results = true;
        foreach (KeyValuePair<string, IDictionary<string, Func<TestingService, Task<bool>>>> serviceLookup in AsyncServiceTests)
        {
            var serviceName = serviceLookup.Key;

            foreach (KeyValuePair<string, Func<TestingService, Task<bool>>> testLookup in serviceLookup.Value)
            {
                var testName = testLookup.Key;
                testsFound = true;

                Program.Logger?.Log(LogLevel.Debug, $"Running test '{testName}' against service '{serviceName}'.");

                if (!await testLookup.Value.Invoke(this))
                {
                    results = false;
                    if (stopOnFailure)
                        break;
                }
            }
        }

        if (!testsFound)
        {
            Program.Logger?.Log(LogLevel.Warning, $"No tests found to run.");
            return defaultIfNotFound;
        }

        return results;
    }
}
