using BeyondImmersion.BannouService.SaveLoad.Generated;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Handles save data export and import operations including
/// archive creation, download, and conflict resolution.
/// </summary>
/// <remarks>
/// Extracted from SaveLoadService for improved testability
/// and separation of export/import concerns.
/// </remarks>
public interface ISaveExportImportManager
{
    /// <summary>
    /// Exports save slots to a downloadable archive.
    /// Creates a ZIP archive containing save data and manifest,
    /// uploads to asset service, and returns download URL.
    /// </summary>
    /// <param name="request">Export request with owner and game filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and export response with download URL.</returns>
    Task<(StatusCodes, ExportSavesResponse?)> ExportSavesAsync(
        ExportSavesRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports saves from an uploaded export archive.
    /// Downloads archive from asset service, extracts and creates
    /// slots with conflict resolution handling.
    /// </summary>
    /// <param name="request">Import request with archive asset and target owner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and import response with statistics.</returns>
    Task<(StatusCodes, ImportSavesResponse?)> ImportSavesAsync(
        ImportSavesRequest request,
        CancellationToken cancellationToken);
}
