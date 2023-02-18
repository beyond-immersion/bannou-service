using BeyondImmersion.BannouService.Application;
using System.Reflection;

namespace BeyondImmersion.BannouService.Attributes
{
    /// <summary>
    /// Attribute for auto-loading service configuration.
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ServiceConfigurationAttribute : BaseServiceAttribute { }

    /// <summary>
    /// Base type for RunServiceIfEnabledAttribute<T>. Required for reflection nonsense.
    /// </summary>
    public abstract class RunServiceIfEnabledAttribute : BaseServiceAttribute { }

    /// <summary>
    /// Base type for RequiredForServiceAttribute<T>. Required for reflection nonsense.
    /// </summary>
    public abstract class RequiredForServiceAttribute : BaseServiceAttribute { }

    /// <summary>
    /// Attach to boolean property to automatically enable service if set to `true`.
    /// </summary>
    /// <typeparam name="T">The dapr service type that this property enables.</typeparam>
    [AttributeUsage(validOn: AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class RunServiceIfEnabledAttribute<T> : RunServiceIfEnabledAttribute
        where T : Services.IDaprService
    {
        public RunServiceIfEnabledAttribute() { }
    }

    /// <summary>
    /// Attach to property to indicate a non-null/empty value is required for service to function.
    /// </summary>
    /// <typeparam name="T">The dapr service type that this property is required for.</typeparam>
    [AttributeUsage(validOn: AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class RequiredForServiceAttribute<T> : RequiredForServiceAttribute
        where T : Services.IDaprService
    {
        public RequiredForServiceAttribute() { }
    }
}
