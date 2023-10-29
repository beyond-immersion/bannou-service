using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using Dapper;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service component responsible for account handling.
/// 
/// Utilizes MySQL for storing / indexing / searching
/// account data.
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

    private string? _dbConnectionString;

    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        var dbHost = Configuration.Database_Host;
        var dbName = "accounts";
        var dbPort = Configuration.Database_Port;
        var dbUsername = Uri.EscapeDataString(Configuration.Database_User);
        var dbPassword = Uri.EscapeDataString(Configuration.Database_Password);

        _dbConnectionString = $"Host={dbHost}; Port={dbPort}; UserID={dbUsername}; Password={dbPassword}; Database={dbName}; AllowUserVariables=True";
        Program.Logger.Log(LogLevel.Warning, $"Connecting to MySQL with connection string '{_dbConnectionString}'.");

        MySqlConnection? dbConnection = null;
        while (dbConnection == null)
        {
            try
            {
                var connectionAttempt = new MySqlConnection(_dbConnectionString);
                await connectionAttempt.OpenAsync(cancellationToken);
                dbConnection = connectionAttempt;
            }
            catch
            {
                await Task.Delay(200, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    return;
            }
        }

        Program.Logger.Log(LogLevel.Warning, $"Creating MySQL tables with connection string '{_dbConnectionString}'.");

        await InitializeDatabase(dbConnection);
        await dbConnection.CloseAsync();
    }

    async Task<IAccountService.AccountData?> IAccountService.GetAccount(bool includeClaims, string? guid, string? username, string? email,
        string? steamID, string? googleID, string? identityClaim)
    {
        try
        {
            if (_dbConnectionString == null)
                throw new SystemException("Database connection string not found.");

            Program.Logger.Log(LogLevel.Warning, $"Executing 'account/get' service function with connection string '{_dbConnectionString}'.");

            var builder = new SqlBuilder();
            SqlBuilder.Template? template = null;
            object? parameters = null;

            if (!string.IsNullOrWhiteSpace(guid))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ById_WithClaims : SqlScripts.GetUser_ById);
                parameters = new { UserId = guid };
            }
            else if (!string.IsNullOrWhiteSpace(email))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ByEmail_WithClaims : SqlScripts.GetUser_ByEmail);
                parameters = new { Email = email };
            }
            else if (!string.IsNullOrWhiteSpace(username))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ByUsername_WithClaims : SqlScripts.GetUser_ByUsername);
                parameters = new { Username = username };
            }
            else if (!string.IsNullOrWhiteSpace(steamID))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ByProviderId_WithClaims : SqlScripts.GetUser_ByProviderId);
                parameters = new { ProviderName = "Steam", UserId = steamID };
            }
            else if (!string.IsNullOrWhiteSpace(googleID))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ByProviderId_WithClaims : SqlScripts.GetUser_ByProviderId);
                parameters = new { ProviderName = "Google", UserId = googleID };
            }
            else if (!string.IsNullOrWhiteSpace(identityClaim))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ByIdentityClaim_WithClaims : SqlScripts.GetUser_ByIdentityClaim);
                parameters = new { UserId = identityClaim };
            }
            else
            { }

            if (template == null || parameters == null)
                throw new ArgumentException("Template or parameters could not be established by the given arguments.");

            var dbConnection = new MySqlConnection(_dbConnectionString);
            await dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
            var transaction = await dbConnection.BeginTransactionAsync();
            var newUser = await dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
            transaction.Commit();
            await dbConnection.CloseAsync();

            var newAccountData = new IAccountService.AccountData(newUser.Id, newUser.SecurityToken, newUser.CreatedAt)
            {
                Username = newUser.Username,
                Email = newUser.Email,
                EmailVerified = newUser.EmailVerified,
                TwoFactorEnabled = newUser.TwoFactorEnabled,
                LockoutEnd = newUser.LockoutEnd,
                LastLoginAt = newUser.LastLoginAt,
                UpdatedAt = newUser.UpdatedAt,
                RemovedAt = newUser.RemovedAt
            };

            if (includeClaims)
            {
                var claims = newUser.Role?.Split(',');
                if (claims != null)
                    newAccountData.RoleClaims = new HashSet<string>(claims);

                claims = newUser.App?.Split(',');
                if (claims != null)
                    newAccountData.AppClaims = new HashSet<string>(claims);

                claims = newUser.Scope?.Split(',');
                if (claims != null)
                    newAccountData.ScopeClaims = new HashSet<string>(claims);

                claims = newUser.Identity?.Split(',');
                if (claims != null)
                    newAccountData.IdentityClaims = new HashSet<string>(claims);

                claims = newUser.Profile?.Split(',');
                if (claims != null)
                    newAccountData.ProfileClaims = new HashSet<string>(claims);
            }

            return newAccountData;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred while fetching the user account.");
            return null;
        }
    }

    async Task<IAccountService.AccountData?> IAccountService.CreateAccount(string? email, bool emailVerified, bool twoFactorEnabled,
        string? username, string? password, string? steamID, string? steamToken, string? googleID, string? googleToken,
        HashSet<string>? roleClaims, HashSet<string>? appClaims, HashSet<string>? scopeClaims, HashSet<string>? identityClaims, HashSet<string>? profileClaims)
    {
        try
        {
            if (_dbConnectionString == null)
                throw new SystemException("Database connection string not found.");

            Program.Logger.Log(LogLevel.Warning, $"Executing 'account/create' service function with connection string '{_dbConnectionString}'.");

            if (identityClaims != null)
                identityClaims = new HashSet<string>(identityClaims);
            else
                identityClaims = new HashSet<string>();

            // handle steam OAUTH
            string? steamData = null;
            if (string.IsNullOrWhiteSpace(steamID) || string.IsNullOrWhiteSpace(steamToken))
            {
                steamID = null;
                steamToken = null;
            }
            else
            {
                identityClaims.Add($"SteamID:{steamID}");
                steamData = new JObject()
                {
                    ["Token"] = steamToken
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // handle Google OAUTH
            string? googleData = null;
            if (string.IsNullOrWhiteSpace(googleID) || string.IsNullOrWhiteSpace(googleToken))
            {
                googleID = null;
                googleToken = null;
            }
            else
            {
                identityClaims.Add($"GoogleID:{googleID}");
                googleData = new JObject()
                {
                    ["Token"] = googleToken
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // if a third-party identity claim is provided, the account can be accessed through that, but otherwise...
            if ((string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) && identityClaims.Count == 0)
                throw new ArgumentException("No valid identities provided to create account. " +
                    "Resulting account would have no manner of access than by GUID, and this is disallowed to prevent orphans.");

            string? passwordSalt = null;
            string? hashedPassword = null;
            JObject? passwordData = null;

            // handle traditional username/password logins
            if (!string.IsNullOrWhiteSpace(password))
            {
                passwordSalt = Guid.NewGuid().ToString();
                hashedPassword = IAccountService.GenerateHashedSecret(password, passwordSalt);
                passwordData = new JObject() { ["Hash"] = hashedPassword, ["Salt"] = passwordSalt };

                identityClaims.Add($"SecretHash:{hashedPassword}");
                identityClaims.Add($"SecretSalt:{passwordSalt}");
            }

            var builder = new SqlBuilder();
            var template = builder.AddTemplate(SqlScripts.AddUser_WithClaims);
            var securityToken = Guid.NewGuid().ToString();

            var parameters = new
            {
                SecurityToken = securityToken,
                Email = email,
                EmailVerified = emailVerified,
                TwoFactorEnabled = twoFactorEnabled,
                Username = username,
                PasswordData = passwordData?.ToString(Newtonsoft.Json.Formatting.None),
                GoogleUserId = googleID,
                GoogleData = googleData,
                SteamUserId = steamID,
                SteamData = steamData,
                RoleClaims = roleClaims != null ? JArray.FromObject(roleClaims).ToString(Newtonsoft.Json.Formatting.None) : null,
                AppClaims = appClaims != null ? JArray.FromObject(appClaims).ToString(Newtonsoft.Json.Formatting.None) : null,
                ScopeClaims = scopeClaims != null ? JArray.FromObject(scopeClaims).ToString(Newtonsoft.Json.Formatting.None) : null,
                IdentityClaims = identityClaims != null ? JArray.FromObject(identityClaims).ToString(Newtonsoft.Json.Formatting.None) : null,
                ProfileClaims = profileClaims != null ? JArray.FromObject(profileClaims).ToString(Newtonsoft.Json.Formatting.None) : null
            };

            var dbConnection = new MySqlConnection(_dbConnectionString);
            await dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
            var transaction = await dbConnection.BeginTransactionAsync(Program.ShutdownCancellationTokenSource.Token);
            var newUser = await dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
            transaction.Commit();
            await dbConnection.CloseAsync();

            var newAccountData = new IAccountService.AccountData(newUser.Id, newUser.SecurityToken, newUser.CreatedAt)
            {
                Username = newUser.Username,
                Email = newUser.Email,
                EmailVerified = newUser.EmailVerified,
                TwoFactorEnabled = newUser.TwoFactorEnabled,
                LockoutEnd = newUser.LockoutEnd,
                LastLoginAt = newUser.LastLoginAt,
                UpdatedAt = newUser.UpdatedAt,
                RemovedAt = newUser.RemovedAt,
                RoleClaims = roleClaims?.ToHashSet(),
                AppClaims = appClaims?.ToHashSet(),
                ScopeClaims = scopeClaims?.ToHashSet(),
                IdentityClaims = identityClaims?.ToHashSet(),
                ProfileClaims = profileClaims?.ToHashSet()
            };

            return newAccountData;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred while inserting and fetching the new user account.");
            return null;
        }
    }

    /// <summary>
    /// Create all account database tables, if needed.
    /// Will populate the reference tables as well.
    /// </summary>
    private static async Task<bool> InitializeDatabase(MySqlConnection dbConnection)
    {
        Program.Logger.Log(LogLevel.Information, "Creating initial user account tables in MySQL...");

        if (!await CreateTable(dbConnection, "Users", SqlScripts.CreateTable_Users) ||
            !await CreateTable(dbConnection, "ClaimTypes", SqlScripts.CreateTable_ClaimTypes) ||
            !await CreateTable(dbConnection, "LoginProviders", SqlScripts.CreateTable_LoginProviders) ||
            !await CreateTable(dbConnection, "UserLogins", SqlScripts.CreateTable_UserLogins) ||
            !await CreateTable(dbConnection, "UserClaims", SqlScripts.CreateTable_UserClaims))
            return false;

        return true;
    }

    /// <summary>
    /// Creates a table using the given SQL script.
    /// </summary>
    private static async Task<bool> CreateTable(MySqlConnection dbConnection, string tableName, string sqlScript)
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(sqlScript);

        try
        {
            await dbConnection.ExecuteAsync(template.RawSql);
            Program.Logger.Log(LogLevel.Warning, $"Table {tableName} was created successfully.");
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred creating the `{tableName}` table.");
            return false;
        }
    }
}
