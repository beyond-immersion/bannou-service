using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Subscriptions;

/// <summary>
/// Configuration class for Subscriptions service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(SubscriptionsService), envPrefix: "BANNOU_")]
public class SubscriptionsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Dapr state store name for subscriptions
    /// Environment variable: SUBSCRIPTIONS_STATE_STORE_NAME or BANNOU_SUBSCRIPTIONS_STATE_STORE_NAME
    /// </summary>
    public string StateStoreName { get; set; } = "subscriptions-statestore";

    /// <summary>
    /// Suffix for authorization keys in state store
    /// Environment variable: SUBSCRIPTIONS_AUTHORIZATION_SUFFIX or BANNOU_SUBSCRIPTIONS_AUTHORIZATION_SUFFIX
    /// </summary>
    public string AuthorizationSuffix { get; set; } = "authorized";

}
