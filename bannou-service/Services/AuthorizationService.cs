using BeyondImmersion.BannouService.Services.Configuration;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
[DaprService("authorization")]
public class AuthorizationService : IDaprService
{
    public AuthorizationServiceConfiguration Configuration { get; set; }
    protected Task SubscribeConfigurationTask { get; set; }

    async Task IDaprService.OnStart()
    {
        Configuration = IServiceConfiguration.BuildConfiguration<AuthorizationServiceConfiguration>();

        // override sensitive configuration Dapr secret store
        await TryLoadFromDaprSecrets();

        if (string.IsNullOrWhiteSpace(Configuration.Token_Public_Key))
            throw new NullReferenceException("Shared public key for encoding/decoding authorizaton tokens not set.");

        if (string.IsNullOrWhiteSpace(Configuration.Token_Private_Key))
            throw new NullReferenceException("Shared private key for encoding/decoding authorizaton tokens not set.");

        // if integration testing, then add default test record to account datastore
        if (Program.Configuration.Integration_Testing)
        {
            var id = Guid.NewGuid().ToString();
            var email = "user_1@celestialmail.com";
            var displayName = "Test Account";
            var secretString = "user_1_password";
            var secretSalt = Guid.NewGuid().ToString();
            var hashedSecret = GenerateHashedSecret(secretString, secretSalt);

            var accountEntry = new AccountModel(id, email, hashedSecret, secretSalt, displayName);
            await Program.DaprClient.SaveStateAsync("accounts", email.ToLower(), accountEntry, cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }
    }

    async Task IDaprService.OnShutdown()
    {
        await Task.CompletedTask;
    }

    public async Task<string?> GetJWT(string email, string password)
    {
        AccountModel accountEntry = await Program.DaprClient.GetStateAsync<AccountModel>("accounts", email.ToLower(), cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        if (accountEntry == null)
            return null;

        var hashedSecret = GenerateHashedSecret(password, accountEntry.SecretSalt);
        if (!string.Equals(accountEntry.HashedSecret, hashedSecret))
            return null;

        var jwtBuilder = CreateJWTBuilder(Configuration.Token_Public_Key, Configuration.Token_Private_Key);
        jwtBuilder.AddHeader("email", accountEntry.Email);
        jwtBuilder.AddHeader("display-name", accountEntry.DisplayName);

        jwtBuilder.Id(accountEntry.ID);
        jwtBuilder.Issuer("AUTHORIZATION_SERVICE:" + Program.ServiceGUID);
        jwtBuilder.IssuedAt(DateTime.Now);
        jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        jwtBuilder.MustVerifySignature();
        jwtBuilder.AddClaim("role", accountEntry.Role);

        var newJWT = jwtBuilder.Encode();
        return newJWT;
    }

    private JwtBuilder CreateJWTBuilder(string publicKey, string privateKey)
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
            var subscribeResponse = await Program.DaprClient.SubscribeConfiguration("app-secrets", new[] { "auth" }, cancellationToken: Program.ShutdownCancellationTokenSource.Token);
            SubscribeConfigurationTask = Task.Run(async () => 
            {
                while (true)
                {
                    await foreach (var configurationItems in subscribeResponse.Source.WithCancellation(Program.ShutdownCancellationTokenSource.Token))
                    {
                        if (configurationItems.TryGetValue("token_public_key", out var tokenPublicKey))
                            Configuration.Token_Public_Key = tokenPublicKey.Value;

                        if (configurationItems.TryGetValue("token_private_key", out var tokenPrivateKey))
                            Configuration.Token_Private_Key = tokenPrivateKey.Value;
                    }
                }
            }, Program.ShutdownCancellationTokenSource.Token);

            var secretEntry = await Program.DaprClient.GetSecretAsync("app-secrets", "auth", cancellationToken: Program.ShutdownCancellationTokenSource.Token);
            if (secretEntry != null)
            {
                if (secretEntry.TryGetValue("token_public_key", out var tokenPublicKey))
                    Configuration.Token_Public_Key = tokenPublicKey;

                if (secretEntry.TryGetValue("token_private_key", out var tokenPrivateKey))
                    Configuration.Token_Private_Key = tokenPrivateKey;
            }
        }
        catch { }
    }

    private string GenerateHashedSecret(string secretString, string secretSalt)
    {
        var hashAlgo = SHA512.Create();
        var hashedBytes = hashAlgo.ComputeHash(Encoding.UTF8.GetBytes(secretString + secretSalt));
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < hashedBytes.Length; i++)
        {
            builder.Append(hashedBytes[i].ToString("x2"));
        }
        var hashedSecret = builder.ToString();
        return hashedSecret;
    }
}
