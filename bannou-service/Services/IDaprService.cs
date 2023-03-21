using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Interface to implement for all internal dapr service,
    /// which provides the logic for any given set of APIs.
    /// 
    /// For example, the Inventory service is in charge of
    /// any API calls that desire to create/modify inventory
    /// data in the game.
    /// </summary>
    public interface IDaprService
    {
        public string GetServiceName()
            => GetServiceName(GetType());

        public static string GetServiceName(Type serviceType)
        {
            string serviceName = serviceType.Name;

            if (serviceName.EndsWith("Service", comparisonType: StringComparison.InvariantCultureIgnoreCase))
                serviceName = serviceName.Remove(serviceName.Length - "Service".Length, "Service".Length);

            if (serviceName.EndsWith("Controller", comparisonType: StringComparison.CurrentCultureIgnoreCase))
                serviceName = serviceName.Remove(serviceName.Length - "Controller".Length, "Controller".Length);

            if (serviceName.EndsWith("Dapr", comparisonType: StringComparison.CurrentCultureIgnoreCase))
                serviceName = serviceName.Remove(serviceName.Length - "Dapr".Length, "Dapr".Length);

            return serviceName;
        }

        /// <summary>
        /// Returns whether the configuration indicates the service should be enabled.
        /// </summary>
        public bool IsEnabled()
            => Program.Configuration.IsServiceEnabled(GetType());

        /// <summary>
        /// Returns whether the configuration is provided for a service to run properly.
        /// </summary>
        public bool HasRequiredConfiguration()
            => Program.Configuration.HasRequiredConfiguration(GetType());
    }
}
