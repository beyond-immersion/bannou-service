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
        get
        {
            _configuration ??= IServiceConfiguration.BuildConfiguration<AccountServiceConfiguration>();
            return _configuration;
        }

        internal set => _configuration = value;
    }

    private MySqlConnection? dbConnection;

    public async Task OnStart()
    {
        await Task.CompletedTask;

        dbConnection = new MySqlConnection(Configuration.Connection_String);
        dbConnection.Open();
    }

    public async Task<AccountData?> GetAccount(string email)
    {
        await Task.CompletedTask;
        return null;
    }

    public static string GenerateHashedSecret(string secretString, string secretSalt)
        => IAccountService.GenerateHashedSecret(secretString, secretSalt);

    public static bool CreateUsersTable(MySqlConnection sqlConnection)
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `Users` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `Username` VARCHAR(255) UNIQUE NULL,
                `PasswordHash` VARCHAR(255) NULL,
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
            sqlConnection.Execute(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `Users` table.");
            return false;
        }
    }

    public static bool CreateLoginProvidersTable(MySqlConnection sqlConnection)
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `LoginProviders` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `Name` VARCHAR(255) UNIQUE NOT NULL
            ) ENGINE = InnoDB;
            INSERT IGNORE INTO `LoginProviders` (`Name`) VALUES ('Google'), ('Steam');
        ");

        try
        {
            sqlConnection.Execute(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `LoginProviders` table.");
            return false;
        }
    }

    public static bool CreateUserLoginsTable(MySqlConnection sqlConnection)
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
            sqlConnection.Execute(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the user `UserLogins` table.");
            return false;
        }
    }

    public static bool CreateClaimTypesTable(MySqlConnection sqlConnection)
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
            sqlConnection.Execute(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `ClaimTypes` table.");
            return false;
        }
    }

    public static bool CreateUserClaimsTable(MySqlConnection sqlConnection)
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(@"
            CREATE TABLE IF NOT EXISTS `UserClaims` (
                `Id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `UserId` INT UNSIGNED NOT NULL,
                `TypeId` INT UNSIGNED NOT NULL,
                `Value` VARCHAR(255) NOT NULL,
                INDEX (`UserId`),
                FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
                FOREIGN KEY (`TypeId`) REFERENCES `ClaimTypes`(`Id`) ON DELETE CASCADE,
                UNIQUE (`UserId`, `TypeId`, `Value`)
            ) ENGINE = InnoDB;
        ");

        try
        {
            sqlConnection.Execute(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occurred creating the `UserClaims` table.");
            return false;
        }
    }
}
