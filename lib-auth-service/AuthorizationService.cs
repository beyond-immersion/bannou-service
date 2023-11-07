using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Authorization;

/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
[DaprService("authorization", typeof(IAuthorizationService))]
public class AuthorizationService : DaprService<AuthorizationServiceConfiguration>, IAuthorizationService
{
    private Dictionary<string, string> LocalRefreshTokens = new();

    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        // override sensitive configuration Dapr secret store
        await TryLoadFromDaprSecrets();

        if (string.IsNullOrWhiteSpace(Configuration.Token_Public_Key))
            throw new NullReferenceException("Shared public key for encoding/decoding authorizaton tokens not set.");

        if (string.IsNullOrWhiteSpace(Configuration.Token_Private_Key))
            throw new NullReferenceException("Shared private key for encoding/decoding authorizaton tokens not set.");
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
                throw new NullReferenceException("Null response to account create endpoint.");

            if (request.Response.StatusCode != HttpStatusCode.OK)
            {
                Program.Logger.Log(LogLevel.Warning, $"Registration failed- non-OK response to fetching user account: {request.Response.StatusCode}.");
                return (request.Response.StatusCode, null);
            }

            var newJWT = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var newRefreshToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(newJWT) || string.IsNullOrWhiteSpace(newRefreshToken))
                throw new NullReferenceException("JWT or token were not created successfully.");

            // store refresh token in Redis
            LocalRefreshTokens[username] = newRefreshToken;

            return (HttpStatusCode.OK, new() { AccessToken = newJWT, RefreshToken = newRefreshToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client registration.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    public async Task<(HttpStatusCode, IAuthorizationService.LoginResult?)> LoginWithCredentials(string username, string password)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException("Public and Private key tokens are required configuration for handling client auth.");

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

            // didn't match token- hash and compare
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
            var newRefreshToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(newJWT) || string.IsNullOrWhiteSpace(newRefreshToken))
                throw new NullReferenceException("JWT or token were not created successfully.");

            // store refresh token in Redis
            LocalRefreshTokens[username] = newRefreshToken;

            return (HttpStatusCode.OK, new() { AccessToken = newJWT, RefreshToken = newRefreshToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    public async Task<(HttpStatusCode, IAuthorizationService.LoginResult?)> LoginWithToken(string username, string token)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException("Public and Private key tokens are required configuration for handling client auth.");

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

            // if password matches the refresh token, no need to do any hashing
            if (!LocalRefreshTokens.TryGetValue(username, out var storedToken) || !string.Equals(storedToken, token))
            {
                Program.Logger.Log(LogLevel.Warning, "Login failed- client token didn't match stored token.");
                return (HttpStatusCode.Forbidden, null);
            }

            var newJWT = CreateJWT(request.Response.ID, request.Response.Username, request.Response.Email, request.Response.RoleClaims);
            var newToken = CreateRefreshToken(request.Response.SecurityToken);

            if (string.IsNullOrWhiteSpace(newJWT) || string.IsNullOrWhiteSpace(newToken))
                throw new NullReferenceException("JWT or refresh token were not created successfully.");

            // store refresh token in Redis
            LocalRefreshTokens[username] = newToken;

            return (HttpStatusCode.OK, new() { AccessToken = newJWT, RefreshToken = newToken });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    private static string? CreateRefreshToken(string? securityToken)
    {
        if (string.IsNullOrWhiteSpace(securityToken))
            return null;

        return $"{securityToken}_{Guid.NewGuid()}";
    }

    private string? CreateJWT(int? ID, string? username, string? email, HashSet<string>? roleClaims)
    {
        if (string.IsNullOrWhiteSpace(Configuration.Token_Public_Key) || string.IsNullOrWhiteSpace(Configuration.Token_Private_Key))
            return null;

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
