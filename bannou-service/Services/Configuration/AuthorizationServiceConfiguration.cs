using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(AuthorizationService), envPrefix: "AUTHORIZATION_", primary: true)]
public class AuthorizationServiceConfiguration : ServiceConfiguration
{
    public bool Testing { get; set; }
    public string Token_Public_Key { get; set; }
    public string Token_Private_Key { get; set; }
}
