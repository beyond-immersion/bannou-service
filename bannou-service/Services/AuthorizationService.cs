namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for login authorization handling.
/// </summary>
[DaprService("authorization")]
public class AuthorizationService : IDaprService
{
    /// <summary>
    /// Shared endpoint to try authorizing a client connection.
    /// Will hand back a specific instance endpoint to use, for
    /// follow-up requests / exchanges.
    /// </summary>
    public async Task Authorize()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Instance endpoint, for any follow-up exchanges beyond the
    /// initial handshake, for authorizing a client connection.
    /// </summary>
    public async Task AuthorizeDirect()
    {
        await Task.CompletedTask;
    }
}
