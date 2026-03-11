#nullable enable

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Marker interface for services that store account-owned data and must handle
/// account.deleted events to clean up that data per FOUNDATION TENETS (T28).
/// </summary>
/// <remarks>
/// <para>
/// Account deletion cleanup is the ONE entity where event-based cleanup is mandatory
/// (lib-resource cannot be used for accounts due to privacy constraints). Every service
/// storing data with account ownership must subscribe to account.deleted and clean up
/// all account-owned data.
/// </para>
/// <para>
/// Structural tests validate that implementing services have a HandleAccountDeletedAsync
/// method. Reference implementation: <c>plugins/lib-collection/CollectionServiceEvents.cs</c>
/// </para>
/// </remarks>
public interface IAccountDeletionCleanupRequired;
