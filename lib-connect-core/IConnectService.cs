using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Http;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for persistent client connection handling.
/// </summary>
public interface IConnectService : IDaprService
{
    Task<ServiceResponse> Connect(HttpContext requestContext);
}
