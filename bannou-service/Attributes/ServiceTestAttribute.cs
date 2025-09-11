namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Service attribute used on static async methods returning boolean,
/// to indicate the method performs an integration test against a service.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class ServiceTestAttribute : BaseServiceAttribute
{
    /// <summary>
    /// Gets the name of the integration test.
    /// </summary>
    public string? TestName { get; private set; }

    /// <summary>
    /// Gets the service type this test is for.
    /// </summary>
    public Type? ServiceType { get; private set; }

    /// <summary>
    /// Initializes a new instance of the ServiceTestAttribute with the specified test configuration.
    /// </summary>
    /// <param name="testName">The name of the integration test.</param>
    /// <param name="serviceType">The service type this test is for (must implement IDaprService).</param>
    public ServiceTestAttribute(string? testName = null, Type? serviceType = null)
    {
        if (serviceType != null && !typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type '{serviceType}' does not implement {nameof(IDaprService)}.");

        TestName = testName;
        ServiceType = serviceType;
    }
}
