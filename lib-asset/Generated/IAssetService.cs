using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Service interface for Asset API
/// </summary>
[Obsolete]
public partial interface IAssetService : IDaprService
{
    /// <summary>
    /// RequestUpload operation
    /// </summary>
    Task<(StatusCodes, UploadResponse?)> RequestUploadAsync(UploadRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CompleteUpload operation
    /// </summary>
    Task<(StatusCodes, AssetMetadata?)> CompleteUploadAsync(CompleteUploadRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetAsset operation
    /// </summary>
    Task<(StatusCodes, AssetWithDownloadUrl?)> GetAssetAsync(GetAssetRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListAssetVersions operation
    /// </summary>
    Task<(StatusCodes, AssetVersionList?)> ListAssetVersionsAsync(ListVersionsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SearchAssets operation
    /// </summary>
    Task<(StatusCodes, AssetSearchResult?)> SearchAssetsAsync(AssetSearchRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateBundle operation
    /// </summary>
    Task<(StatusCodes, CreateBundleResponse?)> CreateBundleAsync(CreateBundleRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetBundle operation
    /// </summary>
    Task<(StatusCodes, BundleWithDownloadUrl?)> GetBundleAsync(GetBundleRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RequestBundleUpload operation
    /// </summary>
    Task<(StatusCodes, UploadResponse?)> RequestBundleUploadAsync(BundleUploadRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
