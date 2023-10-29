using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Authorization;

[ServiceConfiguration(typeof(AuthorizationService))]
public class AuthorizationServiceConfiguration : BaseServiceConfiguration
{
    public string? Token_Public_Key { get; set; }
    public string? Token_Private_Key { get; set; }
}
