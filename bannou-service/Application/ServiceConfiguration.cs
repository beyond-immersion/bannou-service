using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService
{
    public class ServiceConfiguration
    {
        /// <summary>
        /// Enable to have this service handle asset management APIs.
        /// </summary>
        public bool Asset_Endpoints_Enabled { get; set; } = false;

        /// <summary>
        /// Enable to have this service handle login queue APIs.
        /// </summary>
        public bool Login_Endpoints_Enabled { get; set; } = false;

        /// <summary>
        /// Enable to have this service handle login authorization APIs.
        /// </summary>
        public bool Authorization_Endpoints_Enabled { get; set; } = false;

        /// <summary>
        /// Enable to have this service handle player profile APIs.
        /// </summary>
        public bool Profile_Endpoints_Enabled { get; set; } = false;

        /// <summary>
        /// Enable to have this service handle inventory APIs.
        /// </summary>
        public bool Inventory_Endpoints_Enabled { get; set; } = false;

        /// <summary>
        /// Enable to have this service handle leaderboard APIs.
        /// </summary>
        public bool Leaderboard_Endpoints_Enabled { get; set; } = false;


        public static T? BuildConfiguration<T>(string[]? args, string? envPrefix)
            where T : ServiceConfiguration
        {
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            configurationBuilder
                .AddJsonFile("Config.json", true)
                .AddEnvironmentVariables(envPrefix)
                .AddCommandLine(args ?? Array.Empty<string>(), CreateSwitchMappings<T>());

            return configurationBuilder.Build().Get<T>();
        }

        public static string CreateSwitchFromName(string propertyName)
        {
            propertyName = propertyName.ToLower();
            propertyName = propertyName.Replace('_', '-');
            propertyName = "--" + propertyName;
            return propertyName;
        }

        public static IDictionary<string, string> CreateSwitchMappings<T>()
            where T : ServiceConfiguration
        {
            Dictionary<string, string> keyMappings = new Dictionary<string, string>();
            foreach (var propertyInfo in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                keyMappings[CreateSwitchFromName(propertyInfo.Name)] = propertyInfo.Name;

            return keyMappings;
        }
    }
}
