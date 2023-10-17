using BeyondImmersion.BannouService.Configuration;

[ServiceConfiguration(typeof(IAccountService))]
public class AccountServiceConfiguration :  BaseServiceConfiguration
{
    public string? Db { get; set; }
    public string? Db_Username { get; set; }
    public string? Db_Password { get; set; }
}
