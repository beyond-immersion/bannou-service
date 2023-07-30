using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(AuthorizationService), envPrefix: "AUTHORIZATION_", primary: true)]
public class AuthorizationServiceConfiguration : ServiceConfiguration
{
    [JsonPropertyName("token_shared_secret")]
    [JsonProperty("token_shared_secret")]
    public string TokenSharedSecret { get; set; }
}
