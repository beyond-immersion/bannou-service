using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Subscriptions;

/// <summary>
/// Configuration class for Subscriptions service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(SubscriptionsService))]
public class SubscriptionsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// State store name for subscriptions
    /// Environment variable: SUBSCRIPTIONS_STATE_STORE_NAME
    /// </summary>
    public string StateStoreName { get; set; } = "subscriptions-statestore";

    /// <summary>
    /// Suffix for authorization keys in state store
    /// Environment variable: SUBSCRIPTIONS_AUTHORIZATION_SUFFIX
    /// </summary>
    public string AuthorizationSuffix { get; set; } = "authorized";

}
