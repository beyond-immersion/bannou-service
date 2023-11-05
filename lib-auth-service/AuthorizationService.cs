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
    public async Task<(HttpStatusCode, string?)> Register(string username, string password, string? email)
    {
        try
        {
            var request = new CreateAccountRequest() { Username = username, Password = password, Email = email };
            await request.ExecutePostRequest("account", "create");

            if (request.Response == null)
                return (HttpStatusCode.InternalServerError, null);

            if (request.Response.StatusCode != HttpStatusCode.OK)
                return (request.Response.StatusCode, null);

            return (HttpStatusCode.OK, request.Response.SecurityToken);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client registration.");
            return (HttpStatusCode.InternalServerError, null);
        }
    }

    public async Task<(HttpStatusCode, IAuthorizationService.LoginResult?)> Login(string username, string password)
    {
        try
        {
            if (Configuration.Token_Public_Key == null || Configuration.Token_Private_Key == null)
                throw new NullReferenceException("Public and Private key tokens are required configuration for handling client auth.");

            // retrieve stored account data
            var request = new GetAccountRequest() { Username = username };
            await request.ExecutePostRequest("account", "get");

            if (request.Response == null)
                return (HttpStatusCode.NotFound, null);

            if (request.Response.StatusCode != HttpStatusCode.OK)
                return (request.Response.StatusCode, null);

            // if password matches the token, no need to do any hashing
            if (!string.Equals(request.Response.SecurityToken, password))
            {
                // didn't match token- hash and compare
                if (request.Response.IdentityClaims == null)
                    return (HttpStatusCode.Forbidden, null);

                var secretSalt = request.Response.IdentityClaims.Where(t => t.StartsWith("SecretSalt:")).Select(t => t.Remove(0, "SecretSalt:".Length)).FirstOrDefault();
                var secretHash = request.Response.IdentityClaims.Where(t => t.StartsWith("SecretHash:")).Select(t => t.Remove(0, "SecretHash:".Length)).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(secretSalt) || string.IsNullOrWhiteSpace(secretHash))
                    return (HttpStatusCode.Forbidden, null);

                var hashedPassword = IAccountService.GenerateHashedSecret(password, secretSalt);
                if (!string.Equals(secretHash, hashedPassword))
                    return (HttpStatusCode.Forbidden, null);
            }

            // create JWT
            var jwtBuilder = CreateJWTBuilder(Configuration.Token_Public_Key, Configuration.Token_Private_Key);
            jwtBuilder.AddHeader("email", request.Response.Email);
            jwtBuilder.AddHeader("username", request.Response.Username);

            jwtBuilder.Id(request.Response.ID);
            jwtBuilder.Issuer("AUTHORIZATION_SERVICE:" + Program.ServiceGUID);
            jwtBuilder.IssuedAt(DateTime.Now);
            jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
            jwtBuilder.MustVerifySignature();

            if (request.Response.RoleClaims != null)
                foreach (var roleClaim in request.Response.RoleClaims)
                    jwtBuilder.AddClaim("role", roleClaim);

            var newJWT = jwtBuilder.Encode();

            return (HttpStatusCode.OK, new() { AccessToken = newJWT });
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occured while processing client login.");
            return (HttpStatusCode.InternalServerError, null);
        }
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
