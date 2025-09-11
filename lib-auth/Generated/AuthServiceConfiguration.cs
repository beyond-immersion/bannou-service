using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Auth
{
    /// <summary>
    /// Generated configuration for Auth service
    /// </summary>
    [ServiceConfiguration(typeof(AuthService), envPrefix: "AUTH_")]
    public class AuthServiceConfiguration : IServiceConfiguration
    {
        /// <summary>
        /// Force specific service ID (optional)
        /// </summary>
        public string? Force_Service_ID { get; set; }

        /// <summary>
        /// Disable this service (optional)
        /// </summary>
        public bool? Service_Disabled { get; set; }

        // TODO: Add service-specific configuration properties from schema
        // Example properties:
        // [Required]
        // public string ConnectionString { get; set; } = string.Empty;
        //
        // public int MaxRetries { get; set; } = 3;
        //
        // public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
