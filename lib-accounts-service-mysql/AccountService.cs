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

    private MySqlConnection? _dbConnection;

    async Task IDaprService.OnStart()
    {
        var dbHost = Configuration.Database_Host;
        var dbName = "accounts";
        var dbPort = Configuration.Database_Port;
        var dbUsername = Uri.EscapeDataString(Configuration.Database_User);
        var dbPassword = Uri.EscapeDataString(Configuration.Database_Password);

        var connectionString = $"Host='{dbHost}'; Port={dbPort}; UserID='{dbUsername}'; Password='{dbPassword}'; Database='{dbName}'";
        _dbConnection = new MySqlConnection(connectionString);

        await _dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
        while (_dbConnection.State != System.Data.ConnectionState.Open)
        {
            Program.Logger.Log(LogLevel.Debug, "Waiting for MySQL connection to become ready...");
            await Task.Delay(100);
        }

        await InitializeDatabase();
    }

    async Task IDaprService.OnShutdown()
    {
        if (_dbConnection != null)
            await _dbConnection.CloseAsync();
    }

    async Task<IAccountService.AccountData?> IAccountService.GetAccount(bool includeClaims, string? guid, string? username, string? email,
        string? steamID, string? googleID, string? identityClaim)
    {
        try
        {
            if (_dbConnection == null)
                throw new NullReferenceException();

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
                parameters = new { UserId = email };
            }
            else if (!string.IsNullOrWhiteSpace(username))
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ByUsername_WithClaims : SqlScripts.GetUser_ByUsername);
                parameters = new { UserId = username };
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
                throw new NullReferenceException();

            var transaction = await _dbConnection.BeginTransactionAsync();
            var newUser = await _dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
            transaction.Commit();

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

    async Task<IAccountService.AccountData?> IAccountService.CreateAccount(string? username, string? password, string? email, bool emailVerified, bool twoFactorEnabled,
        HashSet<string>? roleClaims, HashSet<string>? appClaims, HashSet<string>? scopeClaims, HashSet<string>? identityClaims, HashSet<string>? profileClaims)
    {
        try
        {
            if (_dbConnection == null)
                throw new NullReferenceException();

            var builder = new SqlBuilder();
            var template = builder.AddTemplate(SqlScripts.AddUser);

            var securityToken = Guid.NewGuid().ToString();
            string? passwordSalt = null;
            string? hashedPassword = null;
            JObject? passwordData = null;

            if (!string.IsNullOrWhiteSpace(password))
            {
                passwordSalt = Guid.NewGuid().ToString();
                hashedPassword = IAccountService.GenerateHashedSecret(password, passwordSalt);
                passwordData = new JObject() { ["Hash"] =  hashedPassword, ["Salt"] = passwordSalt };

                if (identityClaims != null)
                    identityClaims = new HashSet<string>(identityClaims);
                else
                    identityClaims = new HashSet<string>();

                identityClaims.Add($"SecretHash:{hashedPassword}");
                identityClaims.Add($"SecretSalt:{passwordSalt}");
            }

            var parameters = new
            {
                Username = username,
                SecurityToken = securityToken,
                Email = email,
                EmailVerified = emailVerified,
                TwoFactorEnabled = twoFactorEnabled,
                PasswordData = passwordData?.ToString(Newtonsoft.Json.Formatting.None)
            };

            var transaction = await _dbConnection.BeginTransactionAsync();
            var newUser = await _dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
            transaction.Commit();

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
    private async Task<bool> InitializeDatabase()
    {
        Program.Logger.Log(LogLevel.Information, "Creating initial user account tables in MySQL...");

        if (!await CreateTable("Users", SqlScripts.CreateTable_Users) ||
            !await CreateTable("ClaimTypes", SqlScripts.CreateTable_ClaimTypes) ||
            !await CreateTable("LoginProviders", SqlScripts.CreateTable_LoginProviders) ||
            !await CreateTable("UserLogins", SqlScripts.CreateTable_UserLogins) ||
            !await CreateTable("UserClaims", SqlScripts.CreateTable_UserClaims))
            return false;

        return true;
    }

    /// <summary>
    /// Creates a table using the given SQL script.
    /// </summary>
    private async Task<bool> CreateTable(string tableName, string sqlScript)
    {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate(sqlScript);

        try
        {
            await _dbConnection.ExecuteAsync(template.RawSql);
            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred creating the `{tableName}` table.");
            return false;
        }
    }
}
