using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Authorization;

[ServiceConfiguration(typeof(AuthorizationService))]
public class AuthorizationServiceConfiguration : BaseServiceConfiguration
{
    [ConfigRequired]
    public string? Redis_Connection_String { get; set; }

    [ConfigRequired]
    public string? Token_Public_Key { get; set; }

    [ConfigRequired]
    public string? Token_Private_Key { get; set; }
}
