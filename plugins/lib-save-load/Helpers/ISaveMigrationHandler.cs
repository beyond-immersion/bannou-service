using BeyondImmersion.BannouService;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Handles schema registration and save data migration operations.
/// </summary>
/// <remarks>
/// Extracted from SaveLoadService for improved testability
/// and separation of migration concerns.
/// </remarks>
public interface ISaveMigrationHandler
{
    /// <summary>
    /// Registers a new schema version with optional migration patch.
    /// </summary>
    /// <param name="request">Schema registration request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and schema response.</returns>
    Task<(StatusCodes, SchemaResponse?)> RegisterSchemaAsync(
        RegisterSchemaRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all schemas registered for a namespace.
    /// </summary>
    /// <param name="request">List schemas request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and list of schemas.</returns>
    Task<(StatusCodes, ListSchemasResponse?)> ListSchemasAsync(
        ListSchemasRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Migrates a save from its current schema version to a target version.
    /// </summary>
    /// <param name="request">Migration request with target version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and migration result.</returns>
    Task<(StatusCodes, MigrateSaveResponse?)> MigrateSaveAsync(
        MigrateSaveRequest request,
        CancellationToken cancellationToken);
}
