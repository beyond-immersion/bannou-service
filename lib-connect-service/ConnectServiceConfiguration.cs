using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Connect;

[ServiceConfiguration(typeof(ConnectService))]
public class ConnectServiceConfiguration : BaseServiceConfiguration
{
    public string? Token_Public_Key { get; set; }
}
