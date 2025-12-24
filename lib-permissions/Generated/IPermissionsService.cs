using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Service interface for Permissions API
/// </summary>
public partial interface IPermissionsService : IDaprService
{
        /// <summary>
        /// GetCapabilities operation
        /// </summary>
        Task<(StatusCodes, CapabilityResponse?)> GetCapabilitiesAsync(CapabilityRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ValidateApiAccess operation
        /// </summary>
        Task<(StatusCodes, ValidationResponse?)> ValidateApiAccessAsync(ValidationRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RegisterServicePermissions operation
        /// </summary>
        Task<(StatusCodes, RegistrationResponse?)> RegisterServicePermissionsAsync(ServicePermissionMatrix body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateSessionState operation
        /// </summary>
        Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionStateAsync(SessionStateUpdate body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateSessionRole operation
        /// </summary>
        Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionRoleAsync(SessionRoleUpdate body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ClearSessionState operation
        /// </summary>
        Task<(StatusCodes, SessionUpdateResponse?)> ClearSessionStateAsync(ClearSessionStateRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetSessionInfo operation
        /// </summary>
        Task<(StatusCodes, SessionInfo?)> GetSessionInfoAsync(SessionInfoRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetRegisteredServices operation
        /// </summary>
        Task<(StatusCodes, RegisteredServicesResponse?)> GetRegisteredServicesAsync(ListServicesRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
