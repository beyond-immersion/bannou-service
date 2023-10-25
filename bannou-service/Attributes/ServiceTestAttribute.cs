namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Service attribute used on static async methods returning boolean,
/// to indicate the method performs an integration test against a service.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class ServiceTestAttribute : BaseServiceAttribute
{
    public string? TestName { get; private set; }
    public Type? ServiceType { get; private set; }

    public ServiceTestAttribute(string? testName = null, Type? serviceType = null)
    {
        if (serviceType != null && !typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type '{serviceType}' does not implement {nameof(IDaprService)}.");

        TestName = testName;
        ServiceType = serviceType;
    }
}
