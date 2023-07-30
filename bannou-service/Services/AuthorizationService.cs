using BeyondImmersion.BannouService.Services.Configuration;
using JWT.Builder;
using System.Security.Cryptography;
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

    async Task<bool> IDaprService.OnBuild(IApplicationBuilder appBuilder)
    {
        try
        {
            Configuration = IServiceConfiguration.BuildConfiguration<AuthorizationServiceConfiguration>();

            try
            {
                var secretEntry = await Program.DaprClient.GetSecretAsync("app-secrets", "auth", cancellationToken: Program.ShutdownCancellationTokenSource.Token);
                if (secretEntry != null && secretEntry.TryGetValue("token_shared_secret", out var tokenSharedSecret))
                    Configuration.TokenSharedSecret = tokenSharedSecret;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(Configuration.TokenSharedSecret))
                throw new NullReferenceException("Shared secret for encoding/decoding authorizaton tokens not set.");

            var id = Guid.NewGuid().ToString();
            var email = "user_1@celestialmail.com";
            var displayName = "Test Account";
            var secretString = "user_1_password";
            var secretSalt = Guid.NewGuid().ToString();
            var hashedSecret = GenerateHashedSecret(secretString, secretSalt);

            var accountEntry = new AccountModel(id, email, hashedSecret, secretSalt, displayName);
            await Program.DaprClient.SaveStateAsync("accounts", email.ToLower(), accountEntry, cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }
        catch(Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An error occurred on build with service handler [{nameof(AuthorizationService)}].");
            return false;
        }

        return true;
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

        var builder = new JwtBuilder();
        builder.AddHeader("email", accountEntry.Email);
        builder.AddHeader("display-name", accountEntry.DisplayName);

        builder.Id(accountEntry.ID);
        builder.Issuer("AUTHORIZATION_SERVICE:" + Program.ServiceGUID);
        builder.IssuedAt(DateTime.Now);
        builder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        builder.MustVerifySignature();
        builder.AddClaim("role", accountEntry.Role);
        builder.WithSecret(Configuration.TokenSharedSecret);

        return builder.Encode();
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
