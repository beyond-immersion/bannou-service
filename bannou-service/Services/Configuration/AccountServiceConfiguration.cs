using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Services.Configuration;

[ServiceConfiguration(serviceType: typeof(AccountService), envPrefix: "ACCOUNT_", primary: true)]
public class AccountServiceConfiguration : ServiceConfiguration
{

}
