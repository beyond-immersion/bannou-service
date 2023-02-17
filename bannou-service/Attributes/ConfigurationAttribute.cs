using System.Reflection;
using BeyondImmersion.BannouService.Application;

namespace BeyondImmersion.BannouService.Attributes
{
    /// <summary>
    /// Attribute for auto-loading service configuration.
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ServiceConfigurationAttribute : BaseServiceAttribute
    {

    }

    /// <summary>
    /// 
    /// </summary>
    public abstract class RunServiceIfEnabledAttribute : BaseServiceAttribute { }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T">The dapr service type that this property enables.</typeparam>
    [AttributeUsage(validOn: AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class RunServiceIfEnabledAttribute<T> : RunServiceIfEnabledAttribute
        where T : Services.IDaprService
    {
        public RunServiceIfEnabledAttribute() { }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T">The dapr service type that this property is required for.</typeparam>
    [AttributeUsage(validOn: AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class RequiredForServiceAttribute<T> : BaseServiceAttribute
        where T : Services.IDaprService
    {
        public RequiredForServiceAttribute() { }
    }
}
