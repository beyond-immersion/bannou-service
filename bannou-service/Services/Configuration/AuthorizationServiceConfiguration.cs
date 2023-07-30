using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(AuthorizationService), envPrefix: "AUTHORIZATION_", primary: true)]
public class AuthorizationServiceConfiguration : ServiceConfiguration
{
    public string Token_Secret_Key { get; set; }
}
