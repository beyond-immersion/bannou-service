using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Configuration for the website service.
/// </summary>
[ServiceConfiguration]
public class WebsiteServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Redis connection string for caching (via Dapr state management).
    /// </summary>
    public string? Redis_State_Store { get; set; } = "statestore";

    /// <summary>
    /// Cache expiration time in seconds for page content.
    /// </summary>
    public int Cache_Expiration_Seconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Maximum number of contact form submissions per IP per hour.
    /// </summary>
    public int Contact_Rate_Limit { get; set; } = 5;

    /// <summary>
    /// Auth service endpoint for registration and login.
    /// </summary>
    public string Auth_Service_AppId { get; set; } = "auth";

    /// <summary>
    /// Connect service WebSocket endpoint URL to provide after login.
    /// </summary>
    public string Connect_Service_Url { get; set; } = "wss://connect.bannou.com/ws";

    /// <summary>
    /// Support email address for contact forms.
    /// </summary>
    public string Support_Email { get; set; } = "support@bannou.com";

    /// <summary>
    /// Enable debug mode for development.
    /// </summary>
    public bool Debug_Mode { get; set; } = false;

    /// <summary>
    /// CDN URL for static assets.
    /// </summary>
    public string? CDN_Url { get; set; }

    /// <summary>
    /// Google Analytics tracking ID.
    /// </summary>
    public string? Analytics_Tracking_Id { get; set; }
}