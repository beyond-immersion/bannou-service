using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Application
{
    [ServiceConfiguration]
    public class ServiceConfiguration
    {
        /// <summary>
        /// Shared serializer options, between all dapr services/consumers.
        /// </summary>
        public static readonly JsonSerializerOptions DaprSerializerConfig = new()
        {
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            IgnoreReadOnlyFields = false,
            IgnoreReadOnlyProperties = false,
            IncludeFields = false,
            MaxDepth = 32,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
            WriteIndented = false
        };

        /// <summary>
        /// Set to override GUID for administrative service endpoints.
        /// If not set, will generate a new GUID automatically on service startup.
        /// </summary>
        public string? ForceServiceID { get; set; }

        /// <summary>
        /// Emulate dapr / unit testing mode.
        /// </summary>
        public bool EmulateDapr { get; set; } = false;

        /// <summary>
        /// Enable to have this service handle asset management APIs.
        /// </summary>
        [RunServiceIfEnabled<AssetService>]
        public bool EnableAssetService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        /// <summary>
        /// Enable to have this service handle login queue APIs.
        /// </summary>
        [RunServiceIfEnabled<LoginService>]
        public bool EnableLoginService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        [RequiredForService<LoginService>]
        public string? LoginSecret { get; set; }

        /// <summary>
        /// Enable to have this service handle login authorization APIs.
        /// </summary>
        [RunServiceIfEnabled<AuthorizationService>]
        public bool EnableAuthorizationService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        /// <summary>
        /// Enable to have this service handle player profile APIs.
        /// </summary>
        [RunServiceIfEnabled<ProfileService>]
        public bool EnableProfileService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        /// <summary>
        /// Enable to have this service handle inventory APIs.
        /// </summary>
        [RunServiceIfEnabled<InventoryService>]
        public bool EnableInventoryService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        /// <summary>
        /// Enable to have this service handle template APIs.
        /// </summary>
        [RunServiceIfEnabled<TemplateService>]
        public bool EnableTemplateService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        /// <summary>
        /// Enable to have this service handle leaderboard APIs.
        /// </summary>
        [RunServiceIfEnabled<LeaderboardService>]
        public bool EnableLeaderboardService { get; set; }
            = ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;

        /// <summary>
        /// Returns whether the configuration indicates ANY services should be enabled.
        /// </summary>
        public bool IsAnyServiceEnabled()
        {
            return BaseServiceAttribute.GetPropertiesWithAttribute(GetType(), typeof(RunServiceIfEnabledAttribute))
                .Any(t => (bool?)t.Item1.GetValue(this) ?? false);
        }

        /// <summary>
        /// Returns whether the configuration indicates the service should be enabled.
        /// </summary>
        public bool IsServiceEnabled<T>(T _)
            => IsServiceEnabled(typeof(T));

        /// <summary>
        /// Returns whether the configuration indicates the service should be enabled.
        /// </summary>
        public bool IsServiceEnabled(Type serviceType)
        {
            return BaseServiceAttribute.GetPropertiesWithAttribute(GetType(), typeof(RunServiceIfEnabledAttribute))
                .Any(t =>
                {
                    return t.Item2.GetType().IsGenericType
                        && t.Item2.GetType().GenericTypeArguments.FirstOrDefault() == serviceType
                        && ((bool?)t.Item1.GetValue(this) ?? false);
                });
        }

        /// <summary>
        /// Returns whether the configuration is provided for a service to run properly.
        /// </summary>
        public bool HasRequiredConfiguration<T>()
            where T : IDaprService
            => HasRequiredConfiguration(typeof(T));

        /// <summary>
        /// Returns whether the configuration is provided for a service to run properly.
        /// </summary>
        public bool HasRequiredConfiguration(Type serviceType)
        {
            return BaseServiceAttribute.GetPropertiesWithAttribute(GetType(), typeof(RequiredForServiceAttribute))
                .All(t =>
                {
                    if (!t.Item2.GetType().IsGenericType)
                        return true;

                    if (t.Item2.GetType().GenericTypeArguments.FirstOrDefault() == serviceType)
                    {
                        var propValue = t.Item1.GetValue(this);
                        if (propValue == null)
                            return false;
                    }

                    return true;
                });
        }

        /// <summary>
        /// Builds the service configuration from available Config.json, ENVs, and command line switches.
        /// Uses the best available configuration type discovered in loaded assemblies, rather than
        /// specifying the type explicitly.
        /// </summary>
        public static ServiceConfiguration BuildConfiguration(string[]? args = null, string? envPrefix = null)
        {
            // use reflection to find configuration with attributes
            Type? bestConfigurationType = null;
            foreach ((Type, ServiceConfigurationAttribute) configurationType in BaseServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
            {
                if (bestConfigurationType == null || configurationType.Item1.Assembly != Assembly.GetExecutingAssembly())
                    bestConfigurationType = configurationType.Item1;
            }

            return BuildConfiguration(bestConfigurationType ?? typeof(ServiceConfiguration), args, envPrefix) ?? new ServiceConfiguration();
        }

        /// <summary>
        /// Builds the service configuration from available Config.json, ENVs, and command line switches.
        /// </summary>
        public static T? BuildConfiguration<T>(string[]? args = null, string? envPrefix = null)
            where T : ServiceConfiguration => BuildConfiguration(typeof(T), args, envPrefix) as T;

        /// <summary>
        /// Builds the service configuration from available Config.json, ENVs, and command line switches.
        /// </summary>
        public static ServiceConfiguration? BuildConfiguration(Type configurationType, string[]? args = null, string? envPrefix = null)
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("Config.json", true)
                .AddEnvironmentVariables(envPrefix)
                .AddCommandLine(args ?? Array.Empty<string>(), CreateSwitchMappings(configurationType));

            return configurationBuilder.Build().Get(configurationType) as ServiceConfiguration;
        }

        /// <summary>
        /// Create and return the full lookup of switch mappings for the configuration class.
        /// </summary>
        public static IDictionary<string, string> CreateSwitchMappings<T>()
            where T : ServiceConfiguration
            => CreateSwitchMappings(typeof(T));

        /// <summary>
        /// Create and return the full lookup of switch mappings for the configuration class.
        /// </summary>
        public static IDictionary<string, string> CreateSwitchMappings(Type configurationType)
        {
            Dictionary<string, string> keyMappings = new();
            foreach (PropertyInfo propertyInfo in configurationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                keyMappings[CreateSwitchFromName(propertyInfo.Name)] = propertyInfo.Name;

            return keyMappings;
        }

        /// <summary>
        /// Create a deterministic command switch (ie: --some-switch ) from the given property name.
        /// </summary>
        public static string CreateSwitchFromName(string propertyName)
        {
            propertyName = propertyName.ToLower();
            propertyName = propertyName.Replace('_', '-');
            propertyName = "--" + propertyName;
            return propertyName;
        }
    }
}
