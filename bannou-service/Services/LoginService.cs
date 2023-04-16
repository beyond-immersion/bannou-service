namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for login queue handling.
/// 
/// Can have the login service make an additional endpoint for `/login_{service_guid}` which would be unique to this service instance.
/// This would allow communication with a specific instance, in order to count down a queue position. This means the bulk of the work
/// for the queue could be done internally, with minimal traffic to the datastore only to relay metrics used to bring instances up and
/// down as demand dictates.
/// 
/// This would mean that a bad actor could spam a specific login server instance, but if that doesn't actually increase the internal
/// network traffic on each request, I'm not sure it matters.
/// </summary>
[DaprService("login")]
public sealed class LoginService : IDaprService
{
    /// <summary>
    /// 
    /// </summary>
    public async Task Login() => await Task.CompletedTask;
}
