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
    public async Task<(HttpStatusCode, IAuthorizationService.LoginResult?)> Register(string username, string password, string? email)
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
                return (request.Response.StatusCode, null);
            }

            var newJWT = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var newToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(newJWT) || string.IsNullOrWhiteSpace(newToken))
                throw new NullReferenceException();

            if (!await StoreTokensInDatabase(username, newToken))
                return (HttpStatusCode.InternalServerError, null);

            return (HttpStatusCode.OK, new() { AccessToken = newJWT, RefreshToken = newToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client registration.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    /// <summary>
    /// Client login with username and password.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    public async Task<(HttpStatusCode, IAuthorizationService.LoginResult?)> LoginWithCredentials(string username, string password)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException();

            // retrieve stored account data
            var request = new GetAccountRequest() { IncludeClaims = true, Username = username };
            await request.ExecuteRequest("account", "get");

            if (request.Response == null)
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- null response to fetching user account.");
                return (HttpStatusCode.NotFound, null);
            }

            if (request.Response.StatusCode != HttpStatusCode.OK)
            {
                Program.Logger.Log(LogLevel.Warning, $"Login failed- non-OK response to fetching user account: {request.Response.StatusCode}.");
                return (request.Response.StatusCode, null);
            }

            if (request.Response.IdentityClaims == null)
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- user account identity claims are missing.");
                return (HttpStatusCode.Forbidden, null);
            }

            var secretSalt = request.Response.IdentityClaims.Where(t => t.StartsWith("SecretSalt:")).FirstOrDefault();
            var secretHash = request.Response.IdentityClaims.Where(t => t.StartsWith("SecretHash:")).FirstOrDefault();
            secretSalt = secretSalt?["SecretSalt:".Length..];
            secretHash = secretHash?["SecretHash:".Length..];

            if (string.IsNullOrWhiteSpace(secretSalt) || string.IsNullOrWhiteSpace(secretHash))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- couldn't find stored salt/hashed secret.");
                return (HttpStatusCode.Forbidden, null);
            }

            var hashedPassword = IAccountService.GenerateHashedSecret(password, secretSalt);
            if (!string.Equals(secretHash, hashedPassword))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- secret didn't match stored hash.");
                return (HttpStatusCode.Forbidden, null);
            }

            var newJWT = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var newToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(newJWT) || string.IsNullOrWhiteSpace(newToken))
                throw new NullReferenceException();

            if (!await StoreTokensInDatabase(username, newToken))
                return (HttpStatusCode.InternalServerError, null);

            return (HttpStatusCode.OK, new() { AccessToken = newJWT, RefreshToken = newToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    /// <summary>
    /// Client login using a refresh token.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    public async Task<(HttpStatusCode, IAuthorizationService.LoginResult?)> LoginWithToken(string token)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException();

            var username = await GetUsernameFromRefreshToken(token);
            if (string.IsNullOrWhiteSpace(username))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- client token didn't match stored token.");
                return (HttpStatusCode.Forbidden, null);
            }

            var request = new GetAccountRequest() { IncludeClaims = true, Username = username };
            await request.ExecuteRequest("account", "get");

            if (request.Response == null)
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- null response to fetching user account.");
                return (HttpStatusCode.NotFound, null);
            }

            if (request.Response.StatusCode != HttpStatusCode.OK)
            {
                Program.Logger.Log(LogLevel.Warning, $"Login failed- non-OK response to fetching user account: {request.Response.StatusCode}.");
                return (request.Response.StatusCode, null);
            }

            var newJWT = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var newToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(newJWT) || string.IsNullOrWhiteSpace(newToken))
                throw new NullReferenceException();

            if (!await StoreTokensInDatabase(username, newToken))
                return (HttpStatusCode.InternalServerError, null);

            return (HttpStatusCode.OK, new() { AccessToken = newJWT, RefreshToken = newToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    /// <summary>
    /// Stores the refresh token in Redis.
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    private async Task<bool> StoreTokensInDatabase(string username, string refreshToken)
    {
        if (_redisConnection == null)
            throw new NullReferenceException();

        try
        {
            var setTokenTransaction = _redisConnection.GetDatabase().CreateTransaction();
            _ = setTokenTransaction.StringSetAsync($"USER_TOKEN_{username}", refreshToken, expiry: TimeSpan.FromHours(12), when: When.Always, flags: CommandFlags.FireAndForget);
            _ = setTokenTransaction.StringSetAsync($"REFRESH_TOKEN_{refreshToken}", username, expiry: TimeSpan.FromHours(12), when: When.Always, flags: CommandFlags.FireAndForget);
            return await setTokenTransaction.ExecuteAsync();
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An error occured while setting refresh tokens to redis.");
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
            return await _redisConnection.GetDatabase().StringGetAsync($"REFRESH_TOKEN_{refreshToken}");
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
