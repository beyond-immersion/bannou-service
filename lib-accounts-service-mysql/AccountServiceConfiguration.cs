[ServiceConfiguration(typeof(IAccountService))]
public class AccountServiceConfiguration : AppConfiguration
{
    public string Db { get; set; }
    public string Db_Username { get; set; }
    public string Db_Password { get; set; }
}
