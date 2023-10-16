using BeyondImmersion.BannouService.Services;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
[DaprService("authorization")]
public class AuthorizationService : IAuthorizationService
{
    public AuthorizationServiceConfiguration Configuration { get; set; }

    async Task IDaprService.OnStart()
    {
        Configuration = IServiceConfiguration.BuildConfiguration<AuthorizationServiceConfiguration>();

        // override sensitive configuration Dapr secret store
        await TryLoadFromDaprSecrets();

        if (string.IsNullOrWhiteSpace(Configuration.Token_Public_Key))
            throw new NullReferenceException("Shared public key for encoding/decoding authorizaton tokens not set.");

        if (string.IsNullOrWhiteSpace(Configuration.Token_Private_Key))
            throw new NullReferenceException("Shared private key for encoding/decoding authorizaton tokens not set.");
    }

    public async Task<string?> GetJWT(string email, string password)
    {
        var dataModel = new GetAccountRequest()
        {
            Email = email
        };

        HttpRequestMessage daprRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName("account"), $"account/get", dataModel);
        var daprResponse = await Program.DaprClient.InvokeMethodWithResponseAsync(daprRequest, Program.ShutdownCancellationTokenSource.Token);

        if (daprResponse == null || !daprResponse.IsSuccessStatusCode)
            return null;

        GetAccountResponse? responseData = await daprResponse.Content.ReadFromJsonAsync<GetAccountResponse>();
        if (responseData == null)
            return null;

        var hashedSecret = GenerateHashedSecret(password);
        if (!string.Equals(responseData.HashedSecret, hashedSecret))
            return null;

        var jwtBuilder = CreateJWTBuilder(Configuration.Token_Public_Key, Configuration.Token_Private_Key);
        jwtBuilder.AddHeader("email", responseData.Email);
        jwtBuilder.AddHeader("display-name", responseData.DisplayName);

        jwtBuilder.Id(responseData.ID);
        jwtBuilder.Issuer("AUTHORIZATION_SERVICE:" + Program.ServiceGUID);
        jwtBuilder.IssuedAt(DateTime.Now);
        jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        jwtBuilder.MustVerifySignature();
        jwtBuilder.AddClaim("role", responseData.Role);

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
