using BeyondImmersion.BannouService.Configuration;
using Dapper;
using MySql.Data.MySqlClient;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service component responsible for account handling.
/// </summary>
[DaprService("account", typeof(IAccountService))]
public class AccountService : IAccountService
{
    private AccountServiceConfiguration? _configuration;
    public AccountServiceConfiguration Configuration
    {
        get => _configuration ??= IServiceConfiguration.BuildConfiguration<AccountServiceConfiguration>();
        internal set => _configuration = value;
    }

    private MySqlConnection? _dbConnection;

    public async Task OnStart()
    {
        _dbConnection = new MySqlConnection(Configuration.Connection_String);
        await _dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
        while (_dbConnection.State != System.Data.ConnectionState.Open)
        {
            Program.Logger.Log(LogLevel.Debug, "Waiting for MySQL connection to become ready...");
            await Task.Delay(100);
        }

        await InitializeDatabase();
    }

    public async Task OnShutdown()
    {
        if (_dbConnection != null)
            await _dbConnection.CloseAsync();
    }

    public async Task<AccountData?> GetAccount(string email)
    {
        await Task.CompletedTask;
        return null;
    }

    public static string GenerateHashedSecret(string secretString, string secretSalt)
        => IAccountService.GenerateHashedSecret(secretString, secretSalt);

    /// <summary>
    /// Create all account database tables, if needed.
    /// Will populate the reference tables as well.
    /// </summary>
    public async Task<bool> InitializeDatabase()
    {
        if (!await CreateUsersTable())
            return false;

        if (!await CreateClaimTypesTable())
            return false;

        if (!await CreateLoginProvidersTable())
            return false;

        if (!await CreateUserLoginsTable())
            return false;

        if (!await CreateUserClaimsTable())
            return false;

        return true;
    }

    /// <summary>
    /// Creates the primary table for user data.
    /// 
    /// Clients can potentially have null a username
    /// and password salt, if their account is purely
    /// generated off of an OAuth token, and no
    /// further data has been obtained yet.
    /// </summary>
    public async Task<bool> CreateUsersTable()
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `Users` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `Username` VARCHAR(255) UNIQUE NULL,
                `PasswordSalt` VARCHAR(255) NULL,
                `Email` VARCHAR(255) UNIQUE NULL,
                `EmailVerified` BOOLEAN NOT NULL DEFAULT FALSE,
                `TwoFactorEnabled` BOOLEAN NOT NULL DEFAULT FALSE,
                `LockoutEnd` TIMESTAMP NULL,
                `ProfilePictureUrl` VARCHAR(255) NULL,
                `SecurityStamp` VARCHAR(255) NOT NULL,
                `LastLoginAt` TIMESTAMP NULL,
                `CreatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                `UpdatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                `DeletedAt` TIMESTAMP NULL
            ) ENGINE = InnoDB;
        ");

        try
        {
            await _dbConnection.ExecuteAsync(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `Users` table.");
            return false;
        }
    }

    /// <summary>
    /// Creates the reference table for login / OAuth providers.
    /// This is a lot like an enumeration- just a lookup from id -> name.
    /// 
    /// Initially supporting Steam and Google, but can trivially add new
    /// providers if/as we need them.
    /// </summary>
    public async Task<bool> CreateLoginProvidersTable()
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `LoginProviders` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `Name` VARCHAR(255) UNIQUE NOT NULL
            ) ENGINE = InnoDB;
            INSERT IGNORE INTO `LoginProviders` (`Name`) VALUES ('Google'), ('Steam'), ('Password');
        ");

        try
        {
            await _dbConnection.ExecuteAsync(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `LoginProviders` table.");
            return false;
        }
    }

    /// <summary>
    /// Create the user logins table. Used for the traditional password login, with
    /// ProviderKey holding the password hash, and also maps users to third-party
    /// OAuth provider identities / integrations.
    /// </summary>
    public async Task<bool> CreateUserLoginsTable()
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `UserLogins` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `UserId` INT UNSIGNED NOT NULL,
                `LoginProviderId` INT UNSIGNED NOT NULL,
                `ProviderKey` VARCHAR(512) NOT NULL,
                FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
                FOREIGN KEY (`LoginProviderId`) REFERENCES `LoginProviders`(`Id`) ON DELETE CASCADE,
                UNIQUE(`UserId`, `LoginProviderId`)
            ) ENGINE = InnoDB;
        ");

        try
        {
            await _dbConnection.ExecuteAsync(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the user `UserLogins` table.");
            return false;
        }
    }

    /// <summary>
    /// Creates the reference table for claim types. Like LoginProviders,
    /// essentially an enum.
    /// 
    /// Claim types are much like categories, and a user will have a number of them,
    /// of many different types.
    /// </summary>
    public async Task<bool> CreateClaimTypesTable()
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `ClaimTypes` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `Name` VARCHAR(255) UNIQUE NOT NULL
            ) ENGINE = InnoDB;
            INSERT IGNORE INTO `ClaimTypes` (`Name`) VALUES ('Application'), ('Role'), ('Scope'), ('Identity'), ('Profile');
        ");

        try
        {
            await _dbConnection.ExecuteAsync(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `ClaimTypes` table.");
            return false;
        }
    }

    /// <summary>
    /// Table for storing all of the claims available to users in the network.
    /// The Value is a string typically formatted key:value, by convention, but
    /// can also be complex (JSON) types.
    /// </summary>
    public async Task<bool> CreateUserClaimsTable()
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `UserClaims` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `UserId` INT UNSIGNED NOT NULL,
                `TypeId` INT UNSIGNED NOT NULL,
                `Value` VARCHAR(512) NOT NULL,
                INDEX (`UserId`),
                FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
                FOREIGN KEY (`TypeId`) REFERENCES `ClaimTypes`(`Id`) ON DELETE CASCADE,
                UNIQUE (`UserId`, `TypeId`, `Value`)
            ) ENGINE = InnoDB;
        ");

        try
        {
            await _dbConnection.ExecuteAsync(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `UserClaims` table.");
            return false;
        }
    }
}
