/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
public interface IAuthorizationService : IDaprService
{
    Task<string?> GetJWT(string email, string password);
}
