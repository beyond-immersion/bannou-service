using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(AuthorizationService), envPrefix: "AUTHORIZATION_", primary: true)]
public class AuthorizationServiceConfiguration : ServiceConfiguration
{
    [JsonPropertyName("token_secret_key")]
    [JsonProperty("token_secret_key")]
    public string TokenSecretKey { get; set; }
}
