using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Authorization;

/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
[DaprService("authorization", typeof(IAuthorizationService))]
public sealed class AuthorizationService : DaprService<AuthorizationServiceConfiguration>, IAuthorizationService
{
    private IConnectionMultiplexer? _redisConnection;

    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        if (Configuration.Redis_Connection_String == null)
            throw new NullReferenceException();

        await TryLoadFromDaprSecrets();

        _redisConnection = await ConnectionMultiplexer.ConnectAsync(Configuration.Redis_Connection_String);
        if (!_redisConnection.IsConnected)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Could not connect to Redis.");
    }

    /// <summary>
    /// Register a new user to the system.
    /// Returns the user's security token, which
    /// can be used in place of their password for
    /// logging in.
    /// </summary>
    public async Task<ServiceResponse<AccessData?>> Register(string username, string password, string? email)
    {
        try
        {
            var request = new CreateAccountRequest() { Username = username, Password = password, Email = email };
            await request.ExecuteRequest("account", "create");

            if (request.Response == null)
                throw new NullReferenceException();

            if (request.Response.StatusCode != HttpStatusCode.OK)
            {
                Program.Logger.Log(LogLevel.Warning, $"Registration failed- non-OK response to fetching user account: {request.Response.StatusCode}.");
                return new(StatusCodes.Forbidden, null);
            }

            if (string.IsNullOrWhiteSpace(request.Response.Username) || string.IsNullOrWhiteSpace(request.Response.SecurityToken))
                throw new NullReferenceException();

            var jwt = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var refreshToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(jwt) || string.IsNullOrWhiteSpace(refreshToken))
                throw new NullReferenceException();

            if (!await StoreTokensInDatabase(request.Response.Username, request.Response.SecurityToken, refreshToken))
                return new(StatusCodes.InternalServerError, null);

            return new(StatusCodes.OK, new() { AccessToken = jwt, RefreshToken = refreshToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client registration.");
            return new(StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Client login with username and password.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    public async Task<ServiceResponse<AccessData?>> LoginWithCredentials(string username, string password)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException();

            // retrieve stored account data
            var request = new GetAccountRequest() { IncludeClaims = true, Username = username };
            await request.ExecuteRequest("account", "get");

            if (request.Response == null)
                throw new NullReferenceException();

            if (request.Response.StatusCode != HttpStatusCode.OK)
            {
                Program.Logger.Log(LogLevel.Warning, $"Login failed- non-OK response to fetching user account: {request.Response.StatusCode}.");
                return new(StatusCodes.OK, null);
            }

            if (string.IsNullOrWhiteSpace(request.Response.Username) || string.IsNullOrWhiteSpace(request.Response.SecurityToken))
                throw new NullReferenceException();

            if (request.Response.IdentityClaims == null)
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- user account identity claims are missing.");
                return new(StatusCodes.Forbidden, null);
            }

            var secretSalt = request.Response.IdentityClaims.Where(t => t.StartsWith("SecretSalt:")).FirstOrDefault();
            var secretHash = request.Response.IdentityClaims.Where(t => t.StartsWith("SecretHash:")).FirstOrDefault();
            secretSalt = secretSalt?["SecretSalt:".Length..];
            secretHash = secretHash?["SecretHash:".Length..];

            if (string.IsNullOrWhiteSpace(secretSalt) || string.IsNullOrWhiteSpace(secretHash))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- couldn't find stored salt/hashed secret.");
                return new(StatusCodes.Forbidden, null);
            }

            var hashedPassword = IAccountService.GenerateHashedSecret(password, secretSalt);
            if (!string.Equals(secretHash, hashedPassword))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- secret didn't match stored hash.");
                return new(StatusCodes.Forbidden, null);
            }

            var jwt = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var refreshToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(jwt) || string.IsNullOrWhiteSpace(refreshToken))
                throw new NullReferenceException();

            if (!await StoreTokensInDatabase(request.Response.Username, request.Response.SecurityToken, refreshToken))
                return new(StatusCodes.InternalServerError, null);

            return new(StatusCodes.OK, new() { AccessToken = jwt, RefreshToken = refreshToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return new(StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Client login using a refresh token.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    public async Task<ServiceResponse<AccessData?>> LoginWithToken(string refreshToken)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException();

            var username = await GetUsernameFromRefreshToken(refreshToken);
            if (string.IsNullOrWhiteSpace(username))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- client token didn't match stored token.");
                return new(StatusCodes.Forbidden, null);
            }

            var request = new GetAccountRequest() { IncludeClaims = true, Username = username };
            await request.ExecuteRequest("account", "get");

            if (request.Response == null)
                throw new NullReferenceException();

            if (request.Response.StatusCode != HttpStatusCode.OK)
            {
                Program.Logger.Log(LogLevel.Warning, $"Login failed- non-OK response to fetching user account: {request.Response.StatusCode}.");
                return new(StatusCodes.Forbidden, null);
            }

            if (string.IsNullOrWhiteSpace(request.Response.Username) || string.IsNullOrWhiteSpace(request.Response.SecurityToken))
                throw new NullReferenceException();

            var jwt = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var newRefreshToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(jwt) || string.IsNullOrWhiteSpace(newRefreshToken))
                throw new NullReferenceException();

            if (!await StoreTokensInDatabase(request.Response.Username, request.Response.SecurityToken, newRefreshToken))
                return new(StatusCodes.InternalServerError, null);

            return new(StatusCodes.OK, new() { AccessToken = jwt, RefreshToken = newRefreshToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return new(StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Stores the refresh token in Redis.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    private async Task<bool> StoreTokensInDatabase(string username, string securityToken, string refreshToken)
    {
        if (_redisConnection == null)
            throw new NullReferenceException();

        try
        {
            var dbTransaction = _redisConnection.GetDatabase().CreateTransaction();
            _ = dbTransaction.StringSetAsync($"REFRESH_TOKEN_FROM_USERNAME__{username}", refreshToken, expiry: TimeSpan.FromHours(12), when: When.Always);
            _ = dbTransaction.StringSetAsync($"USERNAME_FROM_REFRESH_TOKEN__{refreshToken}", username, expiry: TimeSpan.FromHours(12), when: When.Always);
            _ = dbTransaction.StringSetAsync($"REFRESH_TOKEN_FROM_SECURITY_TOKEN__{securityToken}", refreshToken, expiry: TimeSpan.FromHours(12), when: When.Always);
            return await dbTransaction.ExecuteAsync();
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occured while setting refresh tokens to redis.");
            return false;
        }
    }

    private readonly string ClearBySecurityToken = @"
local securityToken = KEYS[1]
local refreshToken = redis.call('GET', 'REFRESH_TOKEN_FROM_SECURITY_TOKEN__' .. securityToken)
local username = nil

if refreshToken then
    username = redis.call('GET', 'USERNAME_FROM_REFRESH_TOKEN__' .. refreshToken)
end

if username then
    redis.call('DEL', 'REFRESH_TOKEN_FROM_USERNAME__' .. username)
end

if refreshToken then
    redis.call('DEL', 'USERNAME_FROM_REFRESH_TOKEN__' .. refreshToken)
end

if securityToken then
    redis.call('DEL', 'REFRESH_TOKEN_FROM_SECURITY_TOKEN__' .. securityToken)
end

return 1
";

    /// <summary>
    /// Stores the stored security and refresh tokens in Redis.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    private async Task<bool> ClearTokensFromDatabase(string? username, string? securityToken, string? refreshToken)
    {
        if (_redisConnection == null)
            throw new NullReferenceException();

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(securityToken) && string.IsNullOrWhiteSpace(refreshToken))
            return false;

        try
        {
            var db = _redisConnection.GetDatabase();

            // if all we have is the security token, clear all from just that-
            // easier to do in Lua, instead of requiring 3 separate requests
            if (!string.IsNullOrWhiteSpace(securityToken) && (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(refreshToken)))
                return (bool)await db.ScriptEvaluateAsync(ClearBySecurityToken, new RedisKey[] { securityToken });

            if (string.IsNullOrWhiteSpace(refreshToken))
                refreshToken = await db.StringGetAsync($"REFRESH_TOKEN_FROM_USERNAME__{username}");
            else if (string.IsNullOrWhiteSpace(username))
                username = await db.StringGetAsync($"USERNAME_FROM_REFRESH_TOKEN__{refreshToken}");

            var dbTransaction = db.CreateTransaction();
            if (!string.IsNullOrWhiteSpace(username))
                _ = dbTransaction.KeyDeleteAsync($"REFRESH_TOKEN_FROM_USERNAME__{username}");

            if (!string.IsNullOrWhiteSpace(refreshToken))
                _ = dbTransaction.KeyDeleteAsync($"USERNAME_FROM_REFRESH_TOKEN__{refreshToken}");

            if (!string.IsNullOrWhiteSpace(securityToken))
                _ = dbTransaction.KeyDeleteAsync($"REFRESH_TOKEN_FROM_SECURITY_TOKEN__{securityToken}");

            return await dbTransaction.ExecuteAsync();
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occured while clearing tokens from redis.");
            return false;
        }
    }

    /// <summary>
    /// Retrieves the username for the given refresh token from Redis.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    private async Task<string?> GetUsernameFromRefreshToken(string refreshToken)
    {
        if (_redisConnection == null)
            throw new NullReferenceException();

        try
        {
            return await _redisConnection.GetDatabase().StringGetAsync($"USERNAME_FROM_REFRESH_TOKEN__{refreshToken}");
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occured while verifying a refresh tokens in redis.");
            throw;
        }
    }

    /// <summary>
    /// Create the refresh token format from a given security token (retrieved from user account data).
    /// </summary>
    private static string? CreateRefreshToken(string? securityToken)
    {
        if (string.IsNullOrWhiteSpace(securityToken))
            return null;

        return $"{securityToken}_{Guid.NewGuid()}";
    }

    /// <summary>
    /// Creates a JWT from the given user account data.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    private string? CreateJWT(int? ID, string? username, string? email, HashSet<string>? roleClaims)
    {
        if (string.IsNullOrWhiteSpace(Configuration.Token_Public_Key) || string.IsNullOrWhiteSpace(Configuration.Token_Private_Key))
            throw new NullReferenceException();

        var jwtBuilder = CreateJWTBuilder(Configuration.Token_Public_Key, Configuration.Token_Private_Key);
        if (!string.IsNullOrWhiteSpace(email))
            jwtBuilder.AddHeader("email", email);

        if (!string.IsNullOrWhiteSpace(username))
            jwtBuilder.AddHeader("username", username);

        if (ID != null)
            jwtBuilder.Id(ID.Value);

        jwtBuilder.Issuer("AUTHORIZATION_SERVICE:" + Program.ServiceGUID);
        jwtBuilder.IssuedAt(DateTime.Now);
        jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        jwtBuilder.MustVerifySignature();

        if (roleClaims != null)
            foreach (var roleClaim in roleClaims)
                jwtBuilder.AddClaim("role", roleClaim);

        var newJWT = jwtBuilder.Encode();
        return newJWT;
    }

    /// <summary>
    /// Creates a JWT builder with the appropriate settings for the access
    /// token we want to make.
    /// </summary>
    private static JwtBuilder CreateJWTBuilder(string publicKey, string privateKey)
    {
        var jwtBuilder = new JwtBuilder();

        var publicKeyByes = Convert.FromBase64String(publicKey);
        var publicKeyDecoded = Encoding.UTF8.GetString(publicKeyByes);
        var privateKeyBytes = Convert.FromBase64String(privateKey);
        var privateKeyDecoded = Encoding.UTF8.GetString(privateKeyBytes);

        var publicRSA = RSA.Create();
        publicRSA.ImportFromPem(publicKeyDecoded);
        var privateRSA = RSA.Create();
        privateRSA.ImportFromPem(privateKeyDecoded);
        var jwtAlgorithm = new RS512Algorithm(publicRSA, privateRSA);

        var jwtSerializer = new JsonNetSerializer();
        var jwtDateTimeProvider = new UtcDateTimeProvider();
        var jwtUrlEncoder = new JwtBase64UrlEncoder();
        var jwtValidator = new JwtValidator(jwtSerializer, jwtDateTimeProvider);
        var jwtEncoder = new JwtEncoder(jwtAlgorithm, jwtSerializer, jwtUrlEncoder);
        var jwtDecoder = new JwtDecoder(jwtSerializer, jwtValidator, jwtUrlEncoder, jwtAlgorithm);
        jwtBuilder.WithJsonSerializer(jwtSerializer);
        jwtBuilder.WithDateTimeProvider(jwtDateTimeProvider);
        jwtBuilder.WithUrlEncoder(jwtUrlEncoder);
        jwtBuilder.WithAlgorithm(jwtAlgorithm);
        jwtBuilder.WithEncoder(jwtEncoder);
        jwtBuilder.WithDecoder(jwtDecoder);
        jwtBuilder.WithValidator(jwtValidator);

        return jwtBuilder;
    }

    /// <summary>
    /// If configured, will attempt to set overrides from Dapr Secrets into the service configuration.
    /// It's really better to just restart the service when configuration changes, unless there's a
    /// compelling reason not to.
    /// </summary>
    private async Task TryLoadFromDaprSecrets()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Program.Configuration.Dapr_Secret_Store))
            {
                var secretEntry = await Program.DaprClient.GetSecretAsync(Program.Configuration.Dapr_Secret_Store, "authorization", cancellationToken: Program.ShutdownCancellationTokenSource.Token);
                if (secretEntry != null)
                {
                    if (secretEntry.TryGetValue("AUTH_TOKEN_PUBLIC_KEY", out var tokenPublicKey))
                        Configuration.Token_Public_Key = tokenPublicKey;

                    if (secretEntry.TryGetValue("AUTH_TOKEN_PRIVATE_KEY", out var tokenPrivateKey))
                        Configuration.Token_Private_Key = tokenPrivateKey;
                }
            }
        }
        catch { }
    }
}
