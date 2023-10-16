using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

[ServiceConfiguration(typeof(IAuthorizationService))]
public class AuthorizationServiceConfiguration : AppConfiguration
{
    public string Token_Public_Key { get; set; }
    public string Token_Private_Key { get; set; }
}
