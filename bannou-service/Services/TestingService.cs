using Google.Api;
using System.Reflection;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service handler responsible for testing service APIs.
/// </summary>
[DaprService(name: "testing")]
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

    public TestingServiceConfiguration Configuration { get; private set; }
    private IDictionary<string, IDictionary<string, Func<Task<bool>>>> AsyncServiceTests { get; set; }

    public TestingService()
    {
        Configuration = IServiceConfiguration.BuildConfiguration<TestingServiceConfiguration>();

        AsyncServiceTests = new Dictionary<string, IDictionary<string, Func<Task<bool>>>>();
        foreach ((Type, MethodInfo, ServiceTestAttribute) methodInfo in IServiceAttribute.GetMethodsWithAttribute<ServiceTestAttribute>())
        {
            var testID = methodInfo.Item3?.TestID?.ToLower();
            if (string.IsNullOrWhiteSpace(testID))
                continue;

            var serviceName = methodInfo.Item3?.ServiceType?.GetServiceName()?.ToLower() ?? string.Empty;
            var methodDel = (Func<Task<bool>>?)Delegate.CreateDelegate(type: typeof(Func<Task<bool>>), method: methodInfo.Item2, throwOnBindFailure: true);
            if (methodDel == null)
                continue;

            if (!AsyncServiceTests.ContainsKey(serviceName))
            {
                var newDict = new Dictionary<string, Func<Task<bool>>>();
                newDict.Add(testID, methodDel);
                AsyncServiceTests.Add(serviceName, newDict);
            }
            else
                _ = AsyncServiceTests[serviceName].TryAdd(testID, methodDel);
        }
    }

    /// <summary>
    /// API to run a given test.
    /// ServiceName can be null if unknown.
    /// </summary>
    public async Task<bool> Run(string id, string? service = null, bool defaultIfNotFound = false)
    {
        id = id.ToLower();

        Func<Task<bool>>? testDel = null;
        if (!string.IsNullOrWhiteSpace(service))
        {
            if (AsyncServiceTests.TryGetValue(service.ToLower(), out var idLookup))
                idLookup.TryGetValue(id, out testDel);
        }
        else
        {
            foreach (var serviceLookup in AsyncServiceTests)
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
        return await testDel.Invoke();
    }

    /// <summary>
    /// API to run all tests against a given service.
    /// </summary>
    public async Task<bool> RunAllForService(string service, bool defaultIfNotFound = false, bool stopOnFailure = false)
    {
        bool testsFound = false;
        bool results = true;
        if (AsyncServiceTests.ContainsKey(service))
        {
            foreach (var testLookup in AsyncServiceTests[service])
            {
                testsFound = true;
                Program.Logger?.Log(LogLevel.Debug, $"Running test '{testLookup.Key}' against service '{service}'.");

                if (!await testLookup.Value.Invoke())
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
        bool testsFound = false;
        bool results = true;
        foreach (var serviceLookup in AsyncServiceTests)
        {
            string serviceName = serviceLookup.Key;

            if (IDaprService.IsEnabled(serviceName))
            {
                foreach (var testLookup in serviceLookup.Value)
                {
                    string testName = testLookup.Key;
                    testsFound = true;

                    Program.Logger?.Log(LogLevel.Debug, $"Running test '{testName}' against service '{serviceName}'.");

                    if (!await testLookup.Value.Invoke())
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
        bool testsFound = false;
        bool results = true;
        foreach (var serviceLookup in AsyncServiceTests)
        {
            string serviceName = serviceLookup.Key;

            foreach (var testLookup in serviceLookup.Value)
            {
                string testName = testLookup.Key;
                testsFound = true;

                Program.Logger?.Log(LogLevel.Debug, $"Running test '{testName}' against service '{serviceName}'.");

                if (!await testLookup.Value.Invoke())
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
