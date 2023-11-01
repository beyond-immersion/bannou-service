using BeyondImmersion.BannouService.Configuration;
using System.Reflection;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service handler responsible for testing service APIs.
/// </summary>
[DaprService("testing", priority: false)]
public class TestingService : IDaprService
{
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

    private IDictionary<string, IDictionary<string, Func<TestingService, Task<bool>>>> ServiceTests { get; set; }

    public string? LastTestID { get; private set; }
    public string? LastTestService { get; private set; }
    public ServiceRequest? LastTestRequest { get; private set; }

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
    public void SetLastPostRequest(ServiceRequest? request)
        => LastTestRequest = request;

    public TestingService()
    {
        ServiceTests = new Dictionary<string, IDictionary<string, Func<TestingService, Task<bool>>>>();
        foreach ((Type, MethodInfo, ServiceTestAttribute) methodInfo in IServiceAttribute.GetMethodsWithAttribute<ServiceTestAttribute>())
        {
            var testName = methodInfo.Item3?.TestName?.ToLower();
            if (string.IsNullOrWhiteSpace(testName))
                testName = methodInfo.Item2.Name.ToLower();

            if (string.IsNullOrWhiteSpace(testName))
                continue;

            try
            {
                var serviceName = methodInfo.Item3?.ServiceType?.GetServiceName()?.ToLower();
                if (string.IsNullOrWhiteSpace(serviceName))
                    serviceName = "NONE";

                var methodDel = (Func<TestingService, Task<bool>>?)Delegate.CreateDelegate(type: typeof(Func<TestingService, Task<bool>>), method: methodInfo.Item2, throwOnBindFailure: true);
                if (methodDel == null)
                    continue;

                if (!ServiceTests.ContainsKey(serviceName))
                {
                    var newDict = new Dictionary<string, Func<TestingService, Task<bool>>>
                    {
                        { testName, methodDel }
                    };
                    ServiceTests.Add(serviceName, newDict);
                }
                else
                {
                    _ = ServiceTests[serviceName].TryAdd(testName, methodDel);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// API to run a given test.
    /// ServiceName can be null if unknown.
    /// </summary>
    public async Task<bool> Run(string testName, string? serviceName = null, bool defaultIfNotFound = false)
    {
        testName = testName.ToLower();
        Func<TestingService, Task<bool>>? testDelegate = null;

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            if (ServiceTests.TryGetValue(serviceName.ToLower(), out var testMethods))
                testMethods.TryGetValue(testName, out testDelegate);
        }
        else
        {
            if (ServiceTests.TryGetValue("NONE", out var testMethods))
                testMethods.TryGetValue(testName, out testDelegate);
        }

        if (testDelegate == null)
        {
            // go exploring
            foreach (var testMethods in ServiceTests)
            {
                if (testMethods.Value.TryGetValue(testName, out testDelegate))
                {
                    serviceName = testMethods.Key;
                    break;
                }
            }
        }

        if (testDelegate == null)
        {
            Program.Logger?.Log(LogLevel.Error, $"Test '{testName}' against service '{serviceName}' failed: Not found!");
            return defaultIfNotFound;
        }

        Program.Logger?.Log(LogLevel.Information, $"Running test '{testName}' against service '{serviceName}'.");
        if (!await testDelegate.Invoke(this))
        {
            Program.Logger?.Log(LogLevel.Error, $"Test '{testName}' against service '{serviceName}' failed!");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run a specific collection of tests from delegate functions.
    /// </summary>
    /// <param name="testLabel">The test group label to use for logging.</param>
    /// <param name="testDelegates">The list of delegate functions to run as integration tests.</param>
    /// <returns></returns>
    public async Task<bool> RunDelegates(string testLabel, IEnumerable<Func<TestingService, Task<bool>>>? testDelegates)
    {
        Program.Logger.Log(LogLevel.Debug, $"Running '{testLabel}' tests!");

        if (Program.DaprClient == null)
        {
            Program.Logger.Log(LogLevel.Error, "Dapr client is not loaded.");
            return false;
        }

        if (testDelegates == null || !testDelegates.Any())
            return true;

        foreach (var testMethod in testDelegates)
        {
            try
            {
                if (!await testMethod.Invoke(this))
                {
                    Program.Logger.Log(LogLevel.Error, $"Test failure for [{testMethod.Method.Name}] in '{testLabel}' tests.");
                    return false;
                }
            }
            catch (Dapr.Client.InvocationException exc)
            {
                var logMsg = $"[{testMethod.Method.Name}] in '{testLabel}' tests failed attempting to invoke '{exc.MethodName}' on app '{exc.AppId}'.";
                if (exc.Response != null)
                    logMsg += $"\nCode: {exc.Response.StatusCode}, Reason: {exc.Response.ReasonPhrase}";

                Program.Logger.Log(LogLevel.Error, exc, logMsg);
                return false;
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred running [{testMethod.Method.Name}] in '{testLabel}' tests.");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// API to run all tests against a given service.
    /// </summary>
    public async Task<bool> RunAllForService(string serviceName, bool defaultIfNotFound = false, bool stopOnFailure = false)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return false;

        serviceName = serviceName.ToLower();

        var testsFound = false;
        var results = true;
        if (ServiceTests.TryGetValue(serviceName, out var serviceTests))
        {
            foreach (var testMethods in serviceTests)
            {
                var testName = testMethods.Key;
                testsFound = true;

                Program.Logger?.Log(LogLevel.Debug, $"Running test '{testName}' against service '{serviceName}'.");

                if (!await testMethods.Value.Invoke(this))
                {
                    Program.Logger?.Log(LogLevel.Error, $"Test '{testName}' against service '{serviceName}' failed!");

                    results = false;
                    if (stopOnFailure)
                        break;
                }
            }
        }

        if (!testsFound)
        {
            Program.Logger?.Log(LogLevel.Error, $"Tests against service '{serviceName}' failed: None found!");
            return defaultIfNotFound;
        }

        return results;
    }

    /// <summary>
    /// API to run all tests against all enabled services.
    /// </summary>
    public async Task<bool> RunAllEnabled(bool defaultIfNotFound = false, bool stopOnFailure = true)
    {
        var testsFound = false;
        var results = true;

        foreach (var serviceData in IDaprService.EnabledServices)
        {
            var serviceName = serviceData.Item3?.Name?.ToLower();
            if (string.IsNullOrWhiteSpace(serviceName))
                continue;

            if (!ServiceTests.TryGetValue(serviceName, out var serviceTests))
                continue;

            foreach (var serviceTest in serviceTests)
            {
                var testName = serviceTest.Key;
                testsFound = true;

                Program.Logger?.Log(LogLevel.Information, $"Running test '{testName}' against service '{serviceName}'.");

                if (!await serviceTest.Value.Invoke(this))
                {
                    Program.Logger?.Log(LogLevel.Error, $"Test '{testName}' against service '{serviceName}' failed!");

                    results = false;
                    if (stopOnFailure)
                        break;
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

        foreach (var serviceTestData in ServiceTests)
        {
            var serviceName = serviceTestData.Key;

            foreach (var testMethods in serviceTestData.Value)
            {
                var testName = testMethods.Key;
                testsFound = true;

                Program.Logger?.Log(LogLevel.Information, $"Running test '{testName}' against service '{serviceName}'.");

                if (!await testMethods.Value.Invoke(this))
                {
                    Program.Logger?.Log(LogLevel.Error, $"Test '{testName}' against service '{serviceName}' failed!");

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
