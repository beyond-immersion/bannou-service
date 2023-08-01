using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(ConnectService), envPrefix: "CONNECT_", primary: true)]
public class ConnectServiceConfiguration : ServiceConfiguration
{
    public string Token_Public_Key { get; set; }
}
