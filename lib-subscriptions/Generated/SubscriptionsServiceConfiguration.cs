using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

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
    /// Name of the Dapr state store for subscription data
    /// Environment variable: STATESTORENAME or BANNOU_STATESTORENAME
    /// </summary>
    public string StateStoreName { get; set; } = "subscriptions-statestore";

    /// <summary>
    /// Suffix appended to stub name for authorization strings
    /// Environment variable: AUTHORIZATIONSUFFIX or BANNOU_AUTHORIZATIONSUFFIX
    /// </summary>
    public string AuthorizationSuffix { get; set; } = "authorized";

}
