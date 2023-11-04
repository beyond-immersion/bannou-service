using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Dapper;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;
using System.Net;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service component responsible for account handling.
/// 
/// Utilizes MySQL for storing / indexing / searching
/// account data.
/// </summary>
[DaprService("account", typeof(IAccountService))]
public class AccountService : DaprService<AccountServiceConfiguration>, IAccountService
{
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

    async Task<(HttpStatusCode, IAccountService.AccountData?)> IAccountService.GetAccount(bool includeClaims, int? id, string? username, string? email,
        string? steamID, string? googleID, string? identityClaim)
    {
        try
        {
            if (_dbConnectionString == null)
                throw new SystemException("Database connection string not found.");

            var builder = new SqlBuilder();
            SqlBuilder.Template? template = null;
            object? parameters = null;

            if (id != null)
            {
                template = builder.AddTemplate(includeClaims ? SqlScripts.GetUser_ById_WithClaims : SqlScripts.GetUser_ById);
                parameters = new { UserId = id };
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
                return (HttpStatusCode.BadRequest, null);

            using var dbConnection = new MySqlConnection(_dbConnectionString);
            dynamic? transactionResult = null;

            await dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
            using (var transaction = await dbConnection.BeginTransactionAsync(Program.ShutdownCancellationTokenSource.Token))
            {
                try
                {
                    transactionResult = await dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
                    if (transactionResult == null)
                        throw new NullReferenceException(nameof(transactionResult));

                    transaction.Commit();
                }
                catch
                {
                    await transaction.RollbackAsync(Program.ShutdownCancellationTokenSource.Token);
                    throw;
                }
            }

            // should move to DTO later
            var resUserID = (int)transactionResult.Id;
            string resSecurityToken = transactionResult.SecurityToken;
            string? resUsername = transactionResult.Username;
            string? resEmail = transactionResult.Email;
            string? resRegion = transactionResult.Region;
            bool resEmailVerified = transactionResult.EmailVerified;
            bool resTwoFactorEnabled = transactionResult.TwoFactorEnabled;
            DateTime resCreatedAt = transactionResult.CreatedAt;
            DateTime resUpdatedAt = transactionResult.UpdatedAt ?? resCreatedAt;
            DateTime resLastLoginAt = transactionResult.LastLoginAt ?? resCreatedAt;
            DateTime? resRemovedAt = transactionResult.RemovedAt;
            DateTime? resLockoutEnd = transactionResult.LockoutEnd;

            // NOTE: should never be the same as the DTO (if added)
            // or the Controller API response model- stay decoupled
            var responseObj = new IAccountService.AccountData(resUserID, resSecurityToken, resCreatedAt)
            {
                Username = resUsername,
                Email = resEmail,
                EmailVerified = resEmailVerified,
                TwoFactorEnabled = resTwoFactorEnabled,
                Region = resRegion,
                LockoutEnd = resLockoutEnd,
                LastLoginAt = resLastLoginAt,
                UpdatedAt = resUpdatedAt,
                DeletedAt = resRemovedAt
            };

            if (includeClaims)
            {
                var claims = transactionResult.Role?.Split(',');
                if (claims != null)
                    responseObj.RoleClaims = new HashSet<string>(claims);

                claims = transactionResult.App?.Split(',');
                if (claims != null)
                    responseObj.AppClaims = new HashSet<string>(claims);

                claims = transactionResult.Scope?.Split(',');
                if (claims != null)
                    responseObj.ScopeClaims = new HashSet<string>(claims);

                claims = transactionResult.Identity?.Split(',');
                if (claims != null)
                    responseObj.IdentityClaims = new HashSet<string>(claims);

                claims = transactionResult.Profile?.Split(',');
                if (claims != null)
                    responseObj.ProfileClaims = new HashSet<string>(claims);
            }

            return (HttpStatusCode.OK, responseObj);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred while fetching the user account.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    async Task<(HttpStatusCode, IAccountService.AccountData?)> IAccountService.CreateAccount(string? email, bool emailVerified, bool twoFactorEnabled, string? region,
        string? username, string? password, string? steamID, string? steamToken, string? googleID, string? googleToken,
        HashSet<string>? roleClaims, HashSet<string>? appClaims, HashSet<string>? scopeClaims, HashSet<string>? identityClaims, HashSet<string>? profileClaims)
    {
        try
        {
            if (_dbConnectionString == null)
                throw new SystemException("Database connection string not found.");

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
                return (HttpStatusCode.BadRequest, null);

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
                Region = region,
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

            using var dbConnection = new MySqlConnection(_dbConnectionString);
            dynamic? transactionResult = null;

            await dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
            using (var transaction = await dbConnection.BeginTransactionAsync(Program.ShutdownCancellationTokenSource.Token))
            {
                try
                {
                    transactionResult = await dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
                    if (transactionResult == null)
                        throw new NullReferenceException(nameof(transactionResult));

                    transaction.Commit();
                }
                catch
                {
                    await transaction.RollbackAsync(Program.ShutdownCancellationTokenSource.Token);
                    throw;
                }
            }

            // should move to DTO later
            var resUserID = (int)transactionResult.Id;
            string resSecurityToken = transactionResult.SecurityToken;
            string? resUsername = transactionResult.Username;
            string? resEmail = transactionResult.Email;
            bool resEmailVerified = transactionResult.EmailVerified;
            bool resTwoFactorEnabled = transactionResult.TwoFactorEnabled;
            string? resRegion = transactionResult.Region;
            DateTime resCreatedAt = transactionResult.CreatedAt;
            DateTime resUpdatedAt = transactionResult.UpdatedAt ?? resCreatedAt;
            DateTime resLastLoginAt = transactionResult.LastLoginAt ?? resCreatedAt;
            DateTime? resRemovedAt = transactionResult.RemovedAt;     // null (just created)
            DateTime? resLockoutEnd = transactionResult.LockoutEnd;   // null (just created)

            // NOTE: should never be the same as the DTO (if added)
            // or the Controller API response model- stay decoupled
            var responseObj = new IAccountService.AccountData(resUserID, resSecurityToken, resCreatedAt)
            {
                Username = resUsername,
                Email = resEmail,
                EmailVerified = resEmailVerified,
                TwoFactorEnabled = resTwoFactorEnabled,
                Region = resRegion,
                LockoutEnd = resLockoutEnd,
                LastLoginAt = resLastLoginAt,
                UpdatedAt = resUpdatedAt,
                DeletedAt = resRemovedAt,
                RoleClaims = roleClaims?.ToHashSet(),
                AppClaims = appClaims?.ToHashSet(),
                ScopeClaims = scopeClaims?.ToHashSet(),
                IdentityClaims = identityClaims?.ToHashSet(),
                ProfileClaims = profileClaims?.ToHashSet()
            };

            return (HttpStatusCode.OK, responseObj);
        }
        catch (MySqlException exc) when (exc.Number == 1062)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A duplicate user account already exists with the provided keys.");
            return (HttpStatusCode.Conflict, null);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred while inserting and fetching the new user account.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    async Task<(HttpStatusCode, IAccountService.AccountData?)> IAccountService.UpdateAccount(int id, string? email, bool? emailVerified, bool? twoFactorEnabled, string? region,
    string? username, string? password, string? steamID, string? steamToken, string? googleID, string? googleToken,
    Dictionary<string, string?>? roleClaims, Dictionary<string, string?>? appClaims, Dictionary<string, string?>? scopeClaims, Dictionary<string, string?>? identityClaims, Dictionary<string, string?>? profileClaims)
    {
        try
        {
            if (id < 0)
                return (HttpStatusCode.BadRequest, null);

            if (_dbConnectionString == null)
                throw new SystemException("Database connection string not found.");

            SplitClaims(roleClaims, out var roleClaimsToRemove, out var roleClaimsToAdd);
            SplitClaims(appClaims, out var appClaimsToRemove, out var appClaimsToAdd);
            SplitClaims(scopeClaims, out var scopeClaimsToRemove, out var scopeClaimsToAdd);
            SplitClaims(identityClaims, out var identityClaimsToRemove, out var identityClaimsToAdd);
            SplitClaims(profileClaims, out var profileClaimsToRemove, out var profileClaimsToAdd);

            // handle steam OAUTH
            if (!string.IsNullOrWhiteSpace(steamID))
            {
                identityClaimsToAdd ??= new JArray();
                identityClaimsToAdd.Add($"SteamID:{steamID}");
            }

            string? steamData = null;
            if (!string.IsNullOrWhiteSpace(steamToken))
            {
                steamData = new JObject()
                {
                    ["Token"] = steamToken
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // handle Google OAUTH
            if (!string.IsNullOrWhiteSpace(googleID))
            {
                identityClaimsToAdd ??= new JArray();
                identityClaimsToAdd.Add($"GoogleID:{googleID}");
            }

            string? googleData = null;
            if (!string.IsNullOrWhiteSpace(googleToken))
            {
                googleData = new JObject()
                {
                    ["Token"] = googleToken
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            string? passwordSalt = null;
            string? hashedPassword = null;
            JObject? passwordData = null;

            // handle traditional username/password logins
            if (!string.IsNullOrWhiteSpace(password))
            {
                passwordSalt = Guid.NewGuid().ToString();
                hashedPassword = IAccountService.GenerateHashedSecret(password, passwordSalt);
                passwordData = new JObject() { ["Hash"] = hashedPassword, ["Salt"] = passwordSalt };

                identityClaimsToAdd ??= new JArray();
                identityClaimsToAdd.Add($"SecretHash:{hashedPassword}");
                identityClaimsToAdd.Add($"SecretSalt:{passwordSalt}");
            }

            var builder = new SqlBuilder();
            var template = builder.AddTemplate(SqlScripts.UpdateUser_WithClaims);
            var securityToken = Guid.NewGuid().ToString();

            var parameters = new
            {
                UserId = (uint)id,
                SecurityToken = securityToken,
                Email = email,
                EmailVerified = emailVerified,
                TwoFactorEnabled = twoFactorEnabled,
                Region = region,
                Username = username,
                PasswordData = passwordData?.ToString(Newtonsoft.Json.Formatting.None),
                GoogleUserId = googleID,
                GoogleData = googleData,
                SteamUserId = steamID,
                SteamData = steamData,
                AddRoleClaims = roleClaimsToAdd?.ToString(Newtonsoft.Json.Formatting.None),
                AddAppClaims = appClaimsToAdd?.ToString(Newtonsoft.Json.Formatting.None),
                AddScopeClaims = scopeClaimsToAdd?.ToString(Newtonsoft.Json.Formatting.None),
                AddIdentityClaims = identityClaimsToAdd?.ToString(Newtonsoft.Json.Formatting.None),
                AddProfileClaims = profileClaimsToAdd?.ToString(Newtonsoft.Json.Formatting.None),
                RemoveRoleClaims = roleClaimsToRemove?.ToString(Newtonsoft.Json.Formatting.None),
                RemoveAppClaims = appClaimsToRemove?.ToString(Newtonsoft.Json.Formatting.None),
                RemoveScopeClaims = scopeClaimsToRemove?.ToString(Newtonsoft.Json.Formatting.None),
                RemoveIdentityClaims = identityClaimsToRemove?.ToString(Newtonsoft.Json.Formatting.None),
                RemoveProfileClaims = profileClaimsToRemove?.ToString(Newtonsoft.Json.Formatting.None)
            };

            using var dbConnection = new MySqlConnection(_dbConnectionString);
            dynamic? transactionResult = null;

            await dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
            using (var transaction = await dbConnection.BeginTransactionAsync(Program.ShutdownCancellationTokenSource.Token))
            {
                try
                {
                    transactionResult = await dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
                    if (transactionResult == null)
                        throw new NullReferenceException(nameof(transactionResult));

                    transaction.Commit();
                }
                catch
                {
                    await transaction.RollbackAsync(Program.ShutdownCancellationTokenSource.Token);
                    throw;
                }
            }

            if (transactionResult == null)
                return (HttpStatusCode.NotFound, null);

            // should move to DTO later
            var resUserID = (int)transactionResult.Id;
            string resSecurityToken = transactionResult.SecurityToken;
            string? resUsername = transactionResult.Username;
            string? resEmail = transactionResult.Email;
            bool resEmailVerified = transactionResult.EmailVerified;
            bool resTwoFactorEnabled = transactionResult.TwoFactorEnabled;
            string? resRegion = transactionResult.Region;
            DateTime resCreatedAt = transactionResult.CreatedAt;
            DateTime resUpdatedAt = transactionResult.UpdatedAt ?? resCreatedAt;
            DateTime resLastLoginAt = transactionResult.LastLoginAt ?? resCreatedAt;
            DateTime? resRemovedAt = transactionResult.RemovedAt;
            DateTime? resLockoutEnd = transactionResult.LockoutEnd;

            // NOTE: should never be the same as the DTO (if added)
            // or the Controller API response model- stay decoupled
            var responseObj = new IAccountService.AccountData(resUserID, resSecurityToken, resCreatedAt)
            {
                Username = resUsername,
                Email = resEmail,
                EmailVerified = resEmailVerified,
                TwoFactorEnabled = resTwoFactorEnabled,
                Region = resRegion,
                LockoutEnd = resLockoutEnd,
                LastLoginAt = resLastLoginAt,
                UpdatedAt = resUpdatedAt,
                DeletedAt = resRemovedAt,
                RoleClaims = roleClaimsToAdd == null ? new HashSet<string>() : new HashSet<string>(roleClaimsToAdd.ToObject<string[]>() ?? Array.Empty<string>()),
                AppClaims = appClaimsToAdd == null ? new HashSet<string>() : new HashSet<string>(appClaimsToAdd.ToObject<string[]>() ?? Array.Empty<string>()),
                ScopeClaims = scopeClaimsToAdd == null ? new HashSet<string>() : new HashSet<string>(scopeClaimsToAdd.ToObject<string[]>() ?? Array.Empty<string>()),
                IdentityClaims = identityClaimsToAdd == null ? new HashSet<string>() : new HashSet<string>(identityClaimsToAdd.ToObject<string[]>() ?? Array.Empty<string>()),
                ProfileClaims = profileClaimsToAdd == null ? new HashSet<string>() : new HashSet<string>(profileClaimsToAdd.ToObject<string[]>() ?? Array.Empty<string>())
            };

            var claims = transactionResult.Role?.Split(',');
            if (claims != null)
                foreach (var claim in claims)
                    responseObj.RoleClaims.Add(claim);

            claims = transactionResult.App?.Split(',');
            if (claims != null)
                foreach (var claim in claims)
                    responseObj.AppClaims.Add(claim);

            claims = transactionResult.Scope?.Split(',');
            if (claims != null)
                foreach (var claim in claims)
                    responseObj.ScopeClaims.Add(claim);

            claims = transactionResult.Identity?.Split(',');
            if (claims != null)
                foreach (var claim in claims)
                    responseObj.IdentityClaims.Add(claim);

            claims = transactionResult.Profile?.Split(',');
            if (claims != null)
                foreach (var claim in claims)
                    responseObj.ProfileClaims.Add(claim);

            return (HttpStatusCode.OK, responseObj);

            static void SplitClaims(Dictionary<string, string?>? claims, out JArray? claimsToRemove, out JArray? claimsToAdd)
            {
                claimsToRemove = null;
                claimsToAdd = null;

                if (claims != null)
                {
                    claimsToRemove = new();
                    claimsToAdd = new();

                    foreach (var claim in claims)
                    {
                        if (!string.Equals(claim.Value, claim.Key))
                            if (!string.IsNullOrWhiteSpace(claim.Key))
                                claimsToRemove.Add(claim.Key);

                        if (!string.IsNullOrWhiteSpace(claim.Value))
                            claimsToAdd.Add(claim.Value);
                    }
                }

                if (claimsToRemove?.Count == 0)
                    claimsToRemove = null;

                if (claimsToAdd?.Count == 0)
                    claimsToAdd = null;
            }
        }
        catch (MySqlException exc) when (exc.Number == 1062)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"A duplicate user account already exists with one of the provided IDs.");
            return (HttpStatusCode.Conflict, null);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred while updating the user account.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    async Task<(HttpStatusCode, DateTime?)> IAccountService.DeleteAccount(int id)
    {
        try
        {
            if (id < 0)
                return (HttpStatusCode.BadRequest, null);

            if (_dbConnectionString == null)
                throw new SystemException("Database connection string not found.");

            var builder = new SqlBuilder();
            var template = builder.AddTemplate(SqlScripts.DeleteUser);
            var parameters = new
            {
                UserId = (uint)id
            };

            using var dbConnection = new MySqlConnection(_dbConnectionString);
            dynamic? transactionResult = null;

            await dbConnection.OpenAsync(Program.ShutdownCancellationTokenSource.Token);
            using (var transaction = await dbConnection.BeginTransactionAsync(Program.ShutdownCancellationTokenSource.Token))
            {
                try
                {
                    transactionResult = await dbConnection.QuerySingleOrDefaultAsync(template.RawSql, parameters, transaction);
                    transaction.Commit();
                }
                catch
                {
                    await transaction.RollbackAsync(Program.ShutdownCancellationTokenSource.Token);
                    throw;
                }
            }

            if (transactionResult == null)
                return (HttpStatusCode.NotFound, null);

            // should move to DTO later
            var resUserID = (int)transactionResult.Id;
            DateTime? resDeletedAt = transactionResult.DeletedAt;

            if (resDeletedAt < (DateTime.Now - TimeSpan.FromSeconds(5)))
            {
                Program.Logger.Log(LogLevel.Error, $"The user account was already deleted.");
                return (HttpStatusCode.Conflict, null);
            }

            return (HttpStatusCode.OK, resDeletedAt);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred while deleting the user account.");
            return (HttpStatusCode.InternalServerError, null);
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
            !await CreateTable(dbConnection, "ClaimTypes", SqlScripts.CreateTable_ClaimTypes_Initialize) ||
            !await CreateTable(dbConnection, "LoginProviders", SqlScripts.CreateTable_LoginProviders_Initialize) ||
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
