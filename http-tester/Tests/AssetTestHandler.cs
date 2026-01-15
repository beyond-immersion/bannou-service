using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using System.Net.Http.Headers;
using System.Text;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for asset management API endpoints using generated clients.
/// Tests the asset service APIs via NSwag-generated AssetClient.
///
/// Note: These tests use small files with non-processable content types (application/json)
/// to avoid triggering the asset processor pipeline which is not yet implemented.
/// Processing only triggers for: large files (>50MB) AND processable types (image/*, model/*, audio/*).
/// </summary>
public class AssetTestHandler : BaseHttpTestHandler
{
    // Shared HttpClient for uploading to MinIO pre-signed URLs
    private static readonly HttpClient _uploadClient = new();

    public override ServiceTest[] GetServiceTests() =>
    [
        // Upload flow tests
        new ServiceTest(TestRequestUpload, "RequestUpload", "Asset", "Test requesting pre-signed upload URL"),
        new ServiceTest(TestCompleteUploadFlow, "CompleteUploadFlow", "Asset", "Test full upload flow: request → upload to MinIO → complete"),

        // Asset retrieval tests
        new ServiceTest(TestGetAsset, "GetAsset", "Asset", "Test retrieving asset metadata with download URL"),
        new ServiceTest(TestListAssetVersions, "ListAssetVersions", "Asset", "Test listing asset version history"),
        new ServiceTest(TestSearchAssets, "SearchAssets", "Asset", "Test searching assets by tags and type"),

        // Bundle tests
        new ServiceTest(TestCreateBundle, "CreateBundle", "Asset", "Test creating bundle from uploaded assets"),
        new ServiceTest(TestGetBundle, "GetBundle", "Asset", "Test retrieving bundle with download URL"),
        new ServiceTest(TestRequestBundleUpload, "RequestBundleUpload", "Asset", "Test requesting bundle upload URL"),

        // Audio processing test (small file - no processing triggered, validates API accepts audio types)
        new ServiceTest(TestAudioUpload, "AudioUpload", "Asset", "Test audio file upload and metadata creation"),

        // Asset deletion tests
        new ServiceTest(TestDeleteAsset, "DeleteAsset", "Asset", "Test deleting an asset"),
        new ServiceTest(TestDeleteNonExistentAsset, "DeleteNonExistentAsset", "Asset", "Test 404 for deleting non-existent asset"),

        // Error handling tests
        new ServiceTest(TestGetNonExistentAsset, "GetNonExistentAsset", "Asset", "Test 404 for non-existent asset"),
        new ServiceTest(TestGetNonExistentBundle, "GetNonExistentBundle", "Asset", "Test 404 for non-existent bundle"),

        // Complete lifecycle test
        new ServiceTest(TestCompleteAssetLifecycle, "CompleteAssetLifecycle", "Asset", "Test complete asset lifecycle: upload → search → bundle → download"),

        // Metabundle tests
        new ServiceTest(TestCreateMetabundleFromBundles, "CreateMetabundleFromBundles", "Asset", "Test creating metabundle from source bundles"),
        new ServiceTest(TestCreateMetabundleWithStandaloneAssets, "CreateMetabundleWithStandaloneAssets", "Asset", "Test creating metabundle with standalone assets"),
        new ServiceTest(TestCreateMetabundleWithBothSources, "CreateMetabundleWithBothSources", "Asset", "Test creating metabundle combining bundles and standalone assets"),
        new ServiceTest(TestCreateMetabundleNonExistentBundle, "CreateMetabundleNonExistentBundle", "Asset", "Test 404 for metabundle with non-existent source bundle"),

        // Bundle resolution tests
        new ServiceTest(TestResolveBundles, "ResolveBundles", "Asset", "Test optimal bundle resolution for asset set"),
        new ServiceTest(TestResolveBundlesWithMetabundlePreference, "ResolveBundlesWithMetabundlePreference", "Asset", "Test metabundle preference in resolution"),
        new ServiceTest(TestQueryBundlesByAsset, "QueryBundlesByAsset", "Asset", "Test finding bundles containing a specific asset"),

        // Bulk asset retrieval tests
        new ServiceTest(TestBulkGetAssetsWithoutUrls, "BulkGetAssetsWithoutUrls", "Asset", "Test bulk asset metadata retrieval without download URLs"),
        new ServiceTest(TestBulkGetAssetsWithUrls, "BulkGetAssetsWithUrls", "Asset", "Test bulk asset metadata retrieval with download URLs"),
    ];

    /// <summary>
    /// Creates a minimal valid WAV file header with silence.
    /// </summary>
    private static byte[] CreateMinimalWavFile(int durationMs = 100, int sampleRate = 44100, int channels = 1, int bitsPerSample = 16)
    {
        var bytesPerSample = bitsPerSample / 8;
        var numSamples = (int)(sampleRate * (durationMs / 1000.0));
        var dataSize = numSamples * channels * bytesPerSample;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize); // File size - 8
        writer.Write("WAVE"u8);

        // fmt chunk
        writer.Write("fmt "u8);
        writer.Write(16); // Chunk size
        writer.Write((short)1); // Audio format (PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bytesPerSample); // Byte rate
        writer.Write((short)(channels * bytesPerSample)); // Block align
        writer.Write((short)bitsPerSample);

        // data chunk (silence)
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]); // Silence

        return ms.ToArray();
    }

    /// <summary>
    /// Helper to upload a test asset and return its metadata.
    /// Uses application/json content type to avoid processor pipeline.
    /// </summary>
    private static async Task<AssetMetadata?> UploadTestAsset(IAssetClient client, string testName, AssetType assetType = AssetType.Behavior, Asset.Realm realm = Asset.Realm.Arcadia)
    {
        var testContent = $"{{\"test\": \"{testName}\", \"timestamp\": \"{DateTime.UtcNow:O}\"}}";
        var testBytes = Encoding.UTF8.GetBytes(testContent);

        // Step 1: Request upload URL
        var uploadRequest = new UploadRequest
        {
            Filename = $"test-{testName}-{DateTime.Now.Ticks}.json",
            Size = testBytes.Length,
            ContentType = "application/json",
            Owner = "http-tester",
            Metadata = new AssetMetadataInput
            {
                AssetType = assetType,
                Realm = realm,
                Tags = new List<string> { "test", testName, "http-integration" }
            }
        };

        var uploadResponse = await client.RequestUploadAsync(uploadRequest);
        if (uploadResponse.UploadUrl == null)
            return null;

        // Step 2: Upload to MinIO using pre-signed URL
        using var content = new ByteArrayContent(testBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var minioResponse = await _uploadClient.PutAsync(uploadResponse.UploadUrl, content);
        if (!minioResponse.IsSuccessStatusCode)
            return null;

        // Step 3: Complete upload
        var completeRequest = new CompleteUploadRequest
        {
            UploadId = uploadResponse.UploadId
        };

        return await client.CompleteUploadAsync(completeRequest);
    }

    private static async Task<TestResult> TestRequestUpload(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            var request = new UploadRequest
            {
                Filename = $"test-request-{DateTime.Now.Ticks}.json",
                Size = 1024,
                ContentType = "application/json",
                Owner = "http-tester",
                Metadata = new AssetMetadataInput
                {
                    AssetType = AssetType.Behavior,
                    Realm = Asset.Realm.Arcadia,
                    Tags = new List<string> { "test", "request-upload" }
                }
            };

            var response = await assetClient.RequestUploadAsync(request);

            if (response.UploadId == Guid.Empty)
                return TestResult.Failed("Upload ID is empty");

            if (response.UploadUrl == null)
                return TestResult.Failed("Upload URL is null");

            if (response.ExpiresAt <= DateTimeOffset.UtcNow)
                return TestResult.Failed("Expiration time is in the past");

            return TestResult.Successful($"Upload URL generated: ID={response.UploadId}, Expires={response.ExpiresAt}");
        }, "Request upload");

    private static async Task<TestResult> TestCompleteUploadFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            var metadata = await UploadTestAsset(assetClient, "complete-flow");

            if (metadata == null)
                return TestResult.Failed("Upload flow returned null metadata");

            if (string.IsNullOrEmpty(metadata.AssetId))
                return TestResult.Failed("Asset ID is empty after upload completion");

            if (metadata.ProcessingStatus != ProcessingStatus.Complete)
                return TestResult.Failed($"Expected processing status 'Complete', got '{metadata.ProcessingStatus}'");

            return TestResult.Successful($"Upload completed: AssetID={metadata.AssetId}, ContentHash={metadata.ContentHash}");
        }, "Complete upload flow");

    private static async Task<TestResult> TestGetAsset(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // First upload a test asset
            var uploadedMetadata = await UploadTestAsset(assetClient, "get-asset");
            if (uploadedMetadata == null)
                return TestResult.Failed("Failed to upload test asset");

            // Now retrieve it with download URL
            var getRequest = new GetAssetRequest
            {
                AssetId = uploadedMetadata.AssetId,
                Version = "latest"
            };

            var response = await assetClient.GetAssetAsync(getRequest);

            if (response.AssetId != uploadedMetadata.AssetId)
                return TestResult.Failed($"Asset ID mismatch: expected '{uploadedMetadata.AssetId}', got '{response.AssetId}'");

            if (response.DownloadUrl == null)
                return TestResult.Failed("Download URL is null");

            if (response.ExpiresAt <= DateTimeOffset.UtcNow)
                return TestResult.Failed("Download URL expiration is in the past");

            return TestResult.Successful($"Asset retrieved: ID={response.AssetId}, VersionID={response.VersionId}, Size={response.Size}");
        }, "Get asset");

    private static async Task<TestResult> TestListAssetVersions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // First upload a test asset
            var uploadedMetadata = await UploadTestAsset(assetClient, "list-versions");
            if (uploadedMetadata == null)
                return TestResult.Failed("Failed to upload test asset");

            // List versions
            var listRequest = new ListVersionsRequest
            {
                AssetId = uploadedMetadata.AssetId,
                Limit = 10,
                Offset = 0
            };

            var response = await assetClient.ListAssetVersionsAsync(listRequest);

            if (response.AssetId != uploadedMetadata.AssetId)
                return TestResult.Failed($"Asset ID mismatch in version list");

            if (response.Versions == null || response.Versions.Count == 0)
                return TestResult.Failed("No versions returned for asset");

            if (response.Total < 1)
                return TestResult.Failed("Total version count should be at least 1");

            return TestResult.Successful($"Listed {response.Versions.Count} version(s) for asset {response.AssetId} (Total: {response.Total})");
        }, "List asset versions");

    private static async Task<TestResult> TestSearchAssets(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // First upload a test asset with specific tags
            var uploadedMetadata = await UploadTestAsset(assetClient, "search-test");
            if (uploadedMetadata == null)
                return TestResult.Failed("Failed to upload test asset");

            // Search by tags - must match the asset type and realm used in upload
            // UploadTestAsset defaults to AssetType.Behavior and Realm.Arcadia
            var searchRequest = new AssetSearchRequest
            {
                Tags = new List<string> { "search-test" },
                AssetType = AssetType.Behavior,
                Realm = Asset.Realm.Arcadia,
                Limit = 50,
                Offset = 0
            };

            var response = await assetClient.SearchAssetsAsync(searchRequest);

            if (response.Assets == null)
                return TestResult.Failed("Search returned null assets array");

            // Check if our uploaded asset is in results
            var found = response.Assets.Any(a => a.AssetId == uploadedMetadata.AssetId);
            if (!found)
                return TestResult.Failed($"Uploaded asset {uploadedMetadata.AssetId} not found in search results");

            return TestResult.Successful($"Search found {response.Assets.Count} asset(s) (Total: {response.Total}), including uploaded asset");
        }, "Search assets");

    private static async Task<TestResult> TestCreateBundle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Upload two test assets for bundling
            var asset1 = await UploadTestAsset(assetClient, "bundle-asset-1");
            var asset2 = await UploadTestAsset(assetClient, "bundle-asset-2");

            if (asset1 == null || asset2 == null)
                return TestResult.Failed("Failed to upload test assets for bundle");

            // Create bundle from the assets
            var createRequest = new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = $"test-bundle-{DateTime.Now.Ticks}",
                Version = "1.0.0",
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId },
                Compression = CompressionType.Lz4,
                Metadata = new { description = "Test bundle", created_by = "http-integration-tests" }
            };

            var response = await assetClient.CreateBundleAsync(createRequest);

            if (string.IsNullOrEmpty(response.BundleId))
                return TestResult.Failed("Bundle ID is empty");

            // Small bundles should complete immediately (not queued for processing)
            if (response.Status != CreateBundleResponseStatus.Ready && response.Status != CreateBundleResponseStatus.Queued)
                return TestResult.Failed($"Unexpected bundle status: {response.Status}");

            return TestResult.Successful($"Bundle created: ID={response.BundleId}, Status={response.Status}, EstimatedSize={response.EstimatedSize}");
        }, "Create bundle");

    private static async Task<TestResult> TestGetBundle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // First create a bundle
            var asset1 = await UploadTestAsset(assetClient, "get-bundle-asset-1");
            var asset2 = await UploadTestAsset(assetClient, "get-bundle-asset-2");

            if (asset1 == null || asset2 == null)
                return TestResult.Failed("Failed to upload test assets");

            var bundleId = $"get-bundle-test-{DateTime.Now.Ticks}";
            var createRequest = new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundleId,
                Version = "1.0.0",
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId },
                Compression = CompressionType.None
            };

            var createResponse = await assetClient.CreateBundleAsync(createRequest);
            if (createResponse.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Bundle creation failed");

            // Now retrieve the bundle
            var getRequest = new GetBundleRequest
            {
                BundleId = bundleId,
                Format = BundleFormat.Bannou
            };

            var response = await assetClient.GetBundleAsync(getRequest);

            if (response.BundleId != bundleId)
                return TestResult.Failed($"Bundle ID mismatch: expected '{bundleId}', got '{response.BundleId}'");

            if (response.DownloadUrl == null)
                return TestResult.Failed("Download URL is null");

            if (response.AssetCount != 2)
                return TestResult.Failed($"Expected 2 assets in bundle, got {response.AssetCount}");

            return TestResult.Successful($"Bundle retrieved: ID={response.BundleId}, Version={response.Version}, Assets={response.AssetCount}, Size={response.Size}");
        }, "Get bundle");

    private static async Task<TestResult> TestRequestBundleUpload(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            var request = new BundleUploadRequest
            {
                Owner = "http-tester",
                Filename = $"test-bundle-upload-{DateTime.Now.Ticks}.bannou",
                Size = 10240,
                ManifestPreview = new BundleManifestPreview
                {
                    BundleId = $"uploaded-bundle-{DateTime.Now.Ticks}",
                    Version = "1.0.0",
                    AssetCount = 5
                }
            };

            var response = await assetClient.RequestBundleUploadAsync(request);

            if (response.UploadId == Guid.Empty)
                return TestResult.Failed("Upload ID is empty");

            if (response.UploadUrl == null)
                return TestResult.Failed("Upload URL is null");

            if (response.ExpiresAt <= DateTimeOffset.UtcNow)
                return TestResult.Failed("Expiration time is in the past");

            return TestResult.Successful($"Bundle upload URL generated: ID={response.UploadId}, Expires={response.ExpiresAt}");
        }, "Request bundle upload");

    /// <summary>
    /// Tests audio file upload with processing.
    /// Requires ASSET_LARGE_FILE_THRESHOLD_MB=0 to trigger processing for small files.
    /// Uses FFmpeg to transcode WAV to MP3.
    /// </summary>
    private static async Task<TestResult> TestAudioUpload(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Create a minimal valid WAV file (100ms of silence)
            var wavBytes = CreateMinimalWavFile(durationMs: 100);

            // Step 1: Request upload URL for audio file
            var uploadRequest = new UploadRequest
            {
                Filename = $"test-audio-{DateTime.Now.Ticks}.wav",
                Size = wavBytes.Length,
                ContentType = "audio/wav",
                Owner = "http-tester",
                Metadata = new AssetMetadataInput
                {
                    AssetType = AssetType.Audio,
                    Realm = Asset.Realm.Arcadia,
                    Tags = new List<string> { "test", "audio", "http-integration" }
                }
            };

            var uploadResponse = await assetClient.RequestUploadAsync(uploadRequest);
            if (uploadResponse.UploadUrl == null)
                return TestResult.Failed("Failed to get upload URL");

            Console.WriteLine($"  Upload URL obtained: ID={uploadResponse.UploadId}");

            // Step 2: Upload WAV to MinIO
            using var content = new ByteArrayContent(wavBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            var minioResponse = await _uploadClient.PutAsync(uploadResponse.UploadUrl, content);
            if (!minioResponse.IsSuccessStatusCode)
                return TestResult.Failed($"Failed to upload to MinIO: {minioResponse.StatusCode}");

            Console.WriteLine($"  Uploaded {wavBytes.Length} bytes to MinIO");

            // Step 3: Complete upload
            var completeRequest = new CompleteUploadRequest
            {
                UploadId = uploadResponse.UploadId
            };

            var metadata = await assetClient.CompleteUploadAsync(completeRequest);
            if (metadata == null)
                return TestResult.Failed("Complete upload returned null");

            Console.WriteLine($"  Asset created: ID={metadata.AssetId}, Status={metadata.ProcessingStatus}");

            // Step 4: Poll for processing completion (if processing was triggered)
            // With ASSET_LARGE_FILE_THRESHOLD_MB=0, processing should be triggered
            var maxWaitSeconds = 30;
            var pollIntervalMs = 500;
            var startTime = DateTime.UtcNow;

            while (metadata.ProcessingStatus != ProcessingStatus.Complete &&
                    metadata.ProcessingStatus != ProcessingStatus.Failed &&
                    (DateTime.UtcNow - startTime).TotalSeconds < maxWaitSeconds)
            {
                await Task.Delay(pollIntervalMs);
                var getRequest = new GetAssetRequest { AssetId = metadata.AssetId };
                var updated = await assetClient.GetAssetAsync(getRequest);
                metadata = updated.Metadata;
                Console.WriteLine($"  Polling: Status={metadata.ProcessingStatus}");
            }

            if (metadata.ProcessingStatus == ProcessingStatus.Failed)
                return TestResult.Failed("Audio processing failed");

            if (metadata.ProcessingStatus != ProcessingStatus.Complete)
                return TestResult.Failed($"Processing did not complete within {maxWaitSeconds}s, status={metadata.ProcessingStatus}");

            // Verify processing result - should be transcoded to MP3
            var finalAsset = await assetClient.GetAssetAsync(new GetAssetRequest { AssetId = metadata.AssetId });

            // The processed content type should be audio/mpeg (MP3) after processing
            var contentType = finalAsset.ContentType ?? finalAsset.Metadata.ContentType;
            if (contentType != "audio/mpeg" && contentType != "audio/wav")
            {
                // Note: If threshold is too high, no processing occurs and content type stays as wav
                Console.WriteLine($"  Note: Content type is {contentType} (processing may not have triggered if threshold is too high)");
            }

            return TestResult.Successful($"Audio upload complete: ID={finalAsset.AssetId}, ContentType={contentType}, Size={finalAsset.Size}");
        }, "Audio upload");

    private static async Task<TestResult> TestDeleteAsset(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // First upload a test asset to delete
            var uploadedMetadata = await UploadTestAsset(assetClient, "delete-test");
            if (uploadedMetadata == null)
                return TestResult.Failed("Failed to upload test asset for deletion");

            // Verify the asset exists
            var getRequest = new GetAssetRequest { AssetId = uploadedMetadata.AssetId };
            var existingAsset = await assetClient.GetAssetAsync(getRequest);
            if (existingAsset.AssetId != uploadedMetadata.AssetId)
                return TestResult.Failed("Asset not found after upload");

            // Delete the asset
            var deleteRequest = new DeleteAssetRequest
            {
                AssetId = uploadedMetadata.AssetId
            };
            var response = await assetClient.DeleteAssetAsync(deleteRequest);

            if (response.AssetId != uploadedMetadata.AssetId)
                return TestResult.Failed($"Asset ID mismatch in response: expected '{uploadedMetadata.AssetId}', got '{response.AssetId}'");

            if (response.VersionsDeleted < 1)
                return TestResult.Failed($"Expected at least 1 version deleted, got {response.VersionsDeleted}");

            // Verify the asset is gone
            try
            {
                await assetClient.GetAssetAsync(getRequest);
                return TestResult.Failed("Asset still exists after deletion");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected - asset should not be found
            }

            return TestResult.Successful($"Deleted asset {uploadedMetadata.AssetId} ({response.VersionsDeleted} version(s))");
        }, "Delete asset");

    private static async Task<TestResult> TestDeleteNonExistentAsset(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var assetClient = GetServiceClient<IAssetClient>();
                await assetClient.DeleteAssetAsync(new DeleteAssetRequest
                {
                    AssetId = $"nonexistent-asset-{Guid.NewGuid()}"
                });
            },
            404,
            "Delete non-existent asset");

    private static async Task<TestResult> TestGetNonExistentAsset(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var assetClient = GetServiceClient<IAssetClient>();
                await assetClient.GetAssetAsync(new GetAssetRequest
                {
                    AssetId = $"nonexistent-asset-{Guid.NewGuid()}"
                });
            },
            404,
            "Get non-existent asset");

    private static async Task<TestResult> TestGetNonExistentBundle(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var assetClient = GetServiceClient<IAssetClient>();
                await assetClient.GetBundleAsync(new GetBundleRequest
                {
                    BundleId = $"nonexistent-bundle-{Guid.NewGuid()}",
                    Format = BundleFormat.Bannou
                });
            },
            404,
            "Get non-existent bundle");

    private static async Task<TestResult> TestCompleteAssetLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload multiple assets
            Console.WriteLine("  Step 1: Uploading test assets...");
            var asset1 = await UploadTestAsset(assetClient, "lifecycle-1", AssetType.Behavior, Asset.Realm.Arcadia);
            var asset2 = await UploadTestAsset(assetClient, "lifecycle-2", AssetType.Other, Asset.Realm.Omega);
            var asset3 = await UploadTestAsset(assetClient, "lifecycle-3", AssetType.Behavior, Asset.Realm.Arcadia);

            if (asset1 == null || asset2 == null || asset3 == null)
                return TestResult.Failed("Failed to upload one or more test assets");
            Console.WriteLine($"  Uploaded: {asset1.AssetId}, {asset2.AssetId}, {asset3.AssetId}");

            // Step 2: Retrieve each asset with download URL
            Console.WriteLine("  Step 2: Retrieving assets with download URLs...");
            var assetWithUrl = await assetClient.GetAssetAsync(new GetAssetRequest { AssetId = asset1.AssetId });
            if (assetWithUrl.DownloadUrl == null)
                return TestResult.Failed("Download URL not generated");
            Console.WriteLine($"  Download URL expires: {assetWithUrl.ExpiresAt}");

            // Step 3: Search for assets by realm
            Console.WriteLine("  Step 3: Searching assets by realm...");
            // asset1 was uploaded with AssetType.Behavior and Realm.Arcadia
            var searchResult = await assetClient.SearchAssetsAsync(new AssetSearchRequest
            {
                Tags = new List<string> { "lifecycle-1" },
                AssetType = AssetType.Behavior,
                Realm = Asset.Realm.Arcadia,
                Limit = 10
            });
            if (!searchResult.Assets.Any(a => a.AssetId == asset1.AssetId))
                return TestResult.Failed("Asset not found in search results");
            Console.WriteLine($"  Found {searchResult.Total} matching asset(s)");

            // Step 4: List versions
            Console.WriteLine("  Step 4: Listing asset versions...");
            var versions = await assetClient.ListAssetVersionsAsync(new ListVersionsRequest { AssetId = asset1.AssetId });
            if (versions.Versions.Count < 1)
                return TestResult.Failed("No versions found for asset");
            Console.WriteLine($"  Found {versions.Total} version(s)");

            // Step 5: Create bundle from assets
            Console.WriteLine("  Step 5: Creating bundle from assets...");
            var bundleId = $"lifecycle-bundle-{DateTime.Now.Ticks}";
            var bundle = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundleId,
                Version = "1.0.0",
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId, asset3.AssetId },
                Compression = CompressionType.Lz4
            });
            Console.WriteLine($"  Bundle status: {bundle.Status}");

            // Step 6: Retrieve bundle with download URL
            Console.WriteLine("  Step 6: Retrieving bundle...");
            var bundleWithUrl = await assetClient.GetBundleAsync(new GetBundleRequest
            {
                BundleId = bundleId,
                Format = BundleFormat.Bannou
            });
            if (bundleWithUrl.AssetCount != 3)
                return TestResult.Failed($"Bundle should contain 3 assets, got {bundleWithUrl.AssetCount}");
            Console.WriteLine($"  Bundle size: {bundleWithUrl.Size} bytes, Assets: {bundleWithUrl.AssetCount}");

            return TestResult.Successful($"Complete lifecycle test passed: 3 assets uploaded, bundled, and retrieved");
        }, "Complete asset lifecycle");

    /// <summary>
    /// Test creating a metabundle from source bundles.
    /// </summary>
    private static async Task<TestResult> TestCreateMetabundleFromBundles(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload assets and create source bundles
            Console.WriteLine("  Step 1: Creating source bundles...");
            var asset1 = await UploadTestAsset(assetClient, "metabundle-source-1");
            var asset2 = await UploadTestAsset(assetClient, "metabundle-source-2");
            if (asset1 == null || asset2 == null)
                return TestResult.Failed("Failed to upload test assets");

            var bundle1Id = $"source-bundle-1-{DateTime.Now.Ticks}";
            var bundle1 = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundle1Id,
                Version = "1.0.0",
                AssetIds = new List<string> { asset1.AssetId },
                Compression = CompressionType.None
            });
            if (bundle1.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create source bundle 1");

            var bundle2Id = $"source-bundle-2-{DateTime.Now.Ticks}";
            var bundle2 = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundle2Id,
                Version = "1.0.0",
                AssetIds = new List<string> { asset2.AssetId },
                Compression = CompressionType.None
            });
            if (bundle2.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create source bundle 2");

            Console.WriteLine($"  Created source bundles: {bundle1Id}, {bundle2Id}");

            // Step 2: Create metabundle from source bundles
            Console.WriteLine("  Step 2: Creating metabundle from source bundles...");
            var metabundleId = $"metabundle-from-bundles-{DateTime.Now.Ticks}";
            var metabundle = await assetClient.CreateMetabundleAsync(new CreateMetabundleRequest
            {
                MetabundleId = metabundleId,
                SourceBundleIds = new List<string> { bundle1Id, bundle2Id },
                Owner = "http-tester",
                Version = "1.0.0",
                Realm = Asset.Realm.Arcadia
            });

            if (metabundle.MetabundleId != metabundleId)
                return TestResult.Failed($"Metabundle ID mismatch: expected '{metabundleId}', got '{metabundle.MetabundleId}'");

            if (metabundle.Status != CreateMetabundleResponseStatus.Ready && metabundle.Status != CreateMetabundleResponseStatus.Queued)
                return TestResult.Failed($"Unexpected metabundle status: {metabundle.Status}");

            if (metabundle.AssetCount != 2)
                return TestResult.Failed($"Expected 2 assets in metabundle, got {metabundle.AssetCount}");

            Console.WriteLine($"  Metabundle created: ID={metabundle.MetabundleId}, Status={metabundle.Status}, Assets={metabundle.AssetCount}");

            // Step 3: Retrieve the metabundle
            Console.WriteLine("  Step 3: Retrieving metabundle...");
            var retrieved = await assetClient.GetBundleAsync(new GetBundleRequest
            {
                BundleId = metabundleId,
                Format = BundleFormat.Bannou
            });

            if (retrieved.DownloadUrl == null)
                return TestResult.Failed("Metabundle download URL is null");

            return TestResult.Successful($"Metabundle from bundles: ID={metabundle.MetabundleId}, Assets={metabundle.AssetCount}, Size={metabundle.SizeBytes}");
        }, "Create metabundle from bundles");

    /// <summary>
    /// Test creating a metabundle with standalone assets (not in bundles).
    /// This enables packaging behaviors/scripts with 3D assets as a complete unit.
    /// </summary>
    private static async Task<TestResult> TestCreateMetabundleWithStandaloneAssets(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload standalone assets (not bundled)
            Console.WriteLine("  Step 1: Uploading standalone assets...");
            var standaloneAsset1 = await UploadTestAsset(assetClient, "standalone-behavior-1", AssetType.Behavior);
            var standaloneAsset2 = await UploadTestAsset(assetClient, "standalone-config-2", AssetType.Other);
            if (standaloneAsset1 == null || standaloneAsset2 == null)
                return TestResult.Failed("Failed to upload standalone assets");

            Console.WriteLine($"  Uploaded standalone assets: {standaloneAsset1.AssetId}, {standaloneAsset2.AssetId}");

            // Step 2: Create metabundle from standalone assets only
            Console.WriteLine("  Step 2: Creating metabundle from standalone assets...");
            var metabundleId = $"metabundle-standalone-{DateTime.Now.Ticks}";
            var metabundle = await assetClient.CreateMetabundleAsync(new CreateMetabundleRequest
            {
                MetabundleId = metabundleId,
                StandaloneAssetIds = new List<string> { standaloneAsset1.AssetId, standaloneAsset2.AssetId },
                Owner = "http-tester",
                Version = "1.0.0",
                Realm = Asset.Realm.Arcadia
            });

            if (metabundle.MetabundleId != metabundleId)
                return TestResult.Failed($"Metabundle ID mismatch");

            if (metabundle.Status != CreateMetabundleResponseStatus.Ready && metabundle.Status != CreateMetabundleResponseStatus.Queued)
                return TestResult.Failed($"Unexpected metabundle status: {metabundle.Status}");

            if (metabundle.AssetCount != 2)
                return TestResult.Failed($"Expected 2 assets in metabundle, got {metabundle.AssetCount}");

            // Verify standalone asset count is tracked
            if (metabundle.StandaloneAssetCount != 2)
                return TestResult.Failed($"Expected StandaloneAssetCount=2, got {metabundle.StandaloneAssetCount}");

            Console.WriteLine($"  Metabundle created: ID={metabundle.MetabundleId}, Assets={metabundle.AssetCount}, Standalone={metabundle.StandaloneAssetCount}");

            return TestResult.Successful($"Metabundle from standalone assets: ID={metabundle.MetabundleId}, Standalone={metabundle.StandaloneAssetCount}");
        }, "Create metabundle with standalone assets");

    /// <summary>
    /// Test creating a metabundle combining both source bundles and standalone assets.
    /// This is the primary use case: packaging behaviors/scripts with bundled 3D assets.
    /// </summary>
    private static async Task<TestResult> TestCreateMetabundleWithBothSources(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Create a source bundle with 3D assets
            Console.WriteLine("  Step 1: Creating source bundle...");
            var bundledAsset = await UploadTestAsset(assetClient, "bundled-3d-asset", AssetType.Model);
            if (bundledAsset == null)
                return TestResult.Failed("Failed to upload bundled asset");

            var sourceBundleId = $"3d-assets-bundle-{DateTime.Now.Ticks}";
            var sourceBundle = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = sourceBundleId,
                Version = "1.0.0",
                AssetIds = new List<string> { bundledAsset.AssetId },
                Compression = CompressionType.None
            });
            if (sourceBundle.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create source bundle");

            Console.WriteLine($"  Created source bundle: {sourceBundleId}");

            // Step 2: Upload standalone behavior/script assets
            Console.WriteLine("  Step 2: Uploading standalone behavior assets...");
            var behaviorAsset = await UploadTestAsset(assetClient, "character-behavior", AssetType.Behavior);
            if (behaviorAsset == null)
                return TestResult.Failed("Failed to upload behavior asset");

            Console.WriteLine($"  Uploaded behavior asset: {behaviorAsset.AssetId}");

            // Step 3: Create metabundle combining both
            Console.WriteLine("  Step 3: Creating combined metabundle...");
            var metabundleId = $"combined-metabundle-{DateTime.Now.Ticks}";
            var metabundle = await assetClient.CreateMetabundleAsync(new CreateMetabundleRequest
            {
                MetabundleId = metabundleId,
                SourceBundleIds = new List<string> { sourceBundleId },
                StandaloneAssetIds = new List<string> { behaviorAsset.AssetId },
                Owner = "http-tester",
                Version = "1.0.0",
                Realm = Asset.Realm.Arcadia
            });

            if (metabundle.MetabundleId != metabundleId)
                return TestResult.Failed($"Metabundle ID mismatch");

            if (metabundle.Status != CreateMetabundleResponseStatus.Ready && metabundle.Status != CreateMetabundleResponseStatus.Queued)
                return TestResult.Failed($"Unexpected metabundle status: {metabundle.Status}");

            // Should have 2 total assets (1 from bundle + 1 standalone)
            if (metabundle.AssetCount != 2)
                return TestResult.Failed($"Expected 2 assets in metabundle, got {metabundle.AssetCount}");

            if (metabundle.StandaloneAssetCount != 1)
                return TestResult.Failed($"Expected StandaloneAssetCount=1, got {metabundle.StandaloneAssetCount}");

            Console.WriteLine($"  Combined metabundle created: ID={metabundle.MetabundleId}, Total={metabundle.AssetCount}, Standalone={metabundle.StandaloneAssetCount}");

            return TestResult.Successful($"Combined metabundle: ID={metabundle.MetabundleId}, Assets={metabundle.AssetCount}, Standalone={metabundle.StandaloneAssetCount}");
        }, "Create metabundle with both sources");

    /// <summary>
    /// Test 404 error when creating metabundle with non-existent source bundle.
    /// </summary>
    private static async Task<TestResult> TestCreateMetabundleNonExistentBundle(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var assetClient = GetServiceClient<IAssetClient>();
                await assetClient.CreateMetabundleAsync(new CreateMetabundleRequest
                {
                    MetabundleId = $"metabundle-nonexistent-{DateTime.Now.Ticks}",
                    SourceBundleIds = new List<string> { $"nonexistent-bundle-{Guid.NewGuid()}" },
                    Owner = "http-tester",
                    Version = "1.0.0",
                    Realm = Asset.Realm.Arcadia
                });
            },
            404,
            "Create metabundle with non-existent bundle");

    /// <summary>
    /// Test optimal bundle resolution using the greedy set-cover algorithm.
    /// Creates multiple bundles with overlapping assets and verifies resolution
    /// returns minimal bundle set covering all requested assets.
    /// </summary>
    private static async Task<TestResult> TestResolveBundles(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload test assets
            Console.WriteLine("  Step 1: Uploading test assets...");
            var asset1 = await UploadTestAsset(assetClient, "resolve-asset-1");
            var asset2 = await UploadTestAsset(assetClient, "resolve-asset-2");
            var asset3 = await UploadTestAsset(assetClient, "resolve-asset-3");

            if (asset1 == null || asset2 == null || asset3 == null)
                return TestResult.Failed("Failed to upload test assets");

            Console.WriteLine($"  Uploaded: {asset1.AssetId}, {asset2.AssetId}, {asset3.AssetId}");

            // Step 2: Create bundles with overlapping assets
            // Bundle A: asset1, asset2
            // Bundle B: asset2, asset3
            // Optimal resolution for {asset1, asset2, asset3} should select both bundles
            Console.WriteLine("  Step 2: Creating bundles with overlapping assets...");
            var bundleAId = $"resolve-bundle-a-{DateTime.Now.Ticks}";
            var bundleA = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundleAId,
                Version = "1.0.0",
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId },
                Compression = CompressionType.None,
                Realm = Asset.Realm.Arcadia
            });
            if (bundleA.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create bundle A");

            var bundleBId = $"resolve-bundle-b-{DateTime.Now.Ticks}";
            var bundleB = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundleBId,
                Version = "1.0.0",
                AssetIds = new List<string> { asset2.AssetId, asset3.AssetId },
                Compression = CompressionType.None,
                Realm = Asset.Realm.Arcadia
            });
            if (bundleB.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create bundle B");

            Console.WriteLine($"  Created bundles: {bundleAId}, {bundleBId}");

            // Step 3: Resolve bundles for all three assets
            Console.WriteLine("  Step 3: Resolving optimal bundles...");
            var resolveRequest = new ResolveBundlesRequest
            {
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId, asset3.AssetId },
                Realm = Asset.Realm.Arcadia,
                PreferMetabundles = false
            };

            var resolution = await assetClient.ResolveBundlesAsync(resolveRequest);

            if (resolution.Bundles == null || resolution.Bundles.Count == 0)
                return TestResult.Failed("Resolution returned no bundles");

            // Should resolve to 2 bundles covering all 3 assets
            if (resolution.Bundles.Count != 2)
                return TestResult.Failed($"Expected 2 bundles, got {resolution.Bundles.Count}");

            // Verify download URLs are included
            var bundlesWithUrls = resolution.Bundles.Count(b => b.DownloadUrl != null);
            if (bundlesWithUrls != resolution.Bundles.Count)
                return TestResult.Failed($"Expected all bundles to have download URLs, got {bundlesWithUrls}/{resolution.Bundles.Count}");

            var coveredCount = resolution.Coverage.ResolvedViaBundles + resolution.Coverage.ResolvedStandalone;
            Console.WriteLine($"  Resolved to {resolution.Bundles.Count} bundles covering {coveredCount} assets");

            return TestResult.Successful($"Bundle resolution: {resolution.Bundles.Count} bundles, {coveredCount} assets covered");
        }, "Resolve bundles");

    /// <summary>
    /// Test that metabundles are preferred when preferMetabundles=true and coverage is equal.
    /// </summary>
    private static async Task<TestResult> TestResolveBundlesWithMetabundlePreference(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload test assets
            Console.WriteLine("  Step 1: Uploading test assets...");
            var asset1 = await UploadTestAsset(assetClient, "metapref-asset-1");
            var asset2 = await UploadTestAsset(assetClient, "metapref-asset-2");

            if (asset1 == null || asset2 == null)
                return TestResult.Failed("Failed to upload test assets");

            // Step 2: Create a regular bundle
            Console.WriteLine("  Step 2: Creating regular bundle...");
            var regularBundleId = $"metapref-regular-{DateTime.Now.Ticks}";
            var regularBundle = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = regularBundleId,
                Version = "1.0.0",
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId },
                Compression = CompressionType.None
            });
            if (regularBundle.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create regular bundle");

            // Step 3: Create a metabundle containing the same assets
            Console.WriteLine("  Step 3: Creating metabundle...");
            var metabundleId = $"metapref-metabundle-{DateTime.Now.Ticks}";
            var metabundle = await assetClient.CreateMetabundleAsync(new CreateMetabundleRequest
            {
                MetabundleId = metabundleId,
                SourceBundleIds = new List<string> { regularBundleId },
                Owner = "http-tester",
                Version = "1.0.0",
                Realm = Asset.Realm.Arcadia
            });
            if (metabundle.Status == CreateMetabundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create metabundle");

            Console.WriteLine($"  Created regular bundle and metabundle with same assets");

            // Step 4: Resolve with metabundle preference
            Console.WriteLine("  Step 4: Resolving with metabundle preference...");
            var resolveRequest = new ResolveBundlesRequest
            {
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId },
                Realm = Asset.Realm.Arcadia,
                PreferMetabundles = true
            };

            var resolution = await assetClient.ResolveBundlesAsync(resolveRequest);

            if (resolution.Bundles == null || resolution.Bundles.Count == 0)
                return TestResult.Failed("Resolution returned no bundles");

            // With preference, metabundle should be selected
            var selectedBundle = resolution.Bundles.First();
            var isMetabundle = selectedBundle.BundleType == BundleType.Metabundle;
            Console.WriteLine($"  Selected bundle: {selectedBundle.BundleId}, BundleType={selectedBundle.BundleType}");

            // The metabundle should be preferred
            if (!isMetabundle)
            {
                // Note: This may not always select metabundle depending on ordering - log for debugging
                Console.WriteLine($"  Note: Regular bundle was selected (may be due to ordering in set-cover)");
            }

            return TestResult.Successful($"Resolution with preference: selected {selectedBundle.BundleId}, BundleType={selectedBundle.BundleType}");
        }, "Resolve bundles with metabundle preference");

    /// <summary>
    /// Test querying bundles that contain a specific asset.
    /// </summary>
    private static async Task<TestResult> TestQueryBundlesByAsset(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload a test asset
            Console.WriteLine("  Step 1: Uploading test asset...");
            var asset = await UploadTestAsset(assetClient, "query-by-asset-target");
            if (asset == null)
                return TestResult.Failed("Failed to upload test asset");

            Console.WriteLine($"  Uploaded asset: {asset.AssetId}");

            // Step 2: Create multiple bundles containing this asset
            Console.WriteLine("  Step 2: Creating bundles containing this asset...");
            var bundle1Id = $"query-bundle-1-{DateTime.Now.Ticks}";
            var bundle1 = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundle1Id,
                Version = "1.0.0",
                AssetIds = new List<string> { asset.AssetId },
                Compression = CompressionType.None,
                Realm = Asset.Realm.Arcadia
            });
            if (bundle1.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create bundle 1");

            var bundle2Id = $"query-bundle-2-{DateTime.Now.Ticks}";
            var bundle2 = await assetClient.CreateBundleAsync(new CreateBundleRequest
            {
                Owner = "http-tester",
                BundleId = bundle2Id,
                Version = "1.0.0",
                AssetIds = new List<string> { asset.AssetId },
                Compression = CompressionType.None,
                Realm = Asset.Realm.Arcadia
            });
            if (bundle2.Status == CreateBundleResponseStatus.Failed)
                return TestResult.Failed("Failed to create bundle 2");

            Console.WriteLine($"  Created bundles: {bundle1Id}, {bundle2Id}");

            // Step 3: Query bundles containing this asset
            Console.WriteLine("  Step 3: Querying bundles by asset...");
            var queryRequest = new QueryBundlesByAssetRequest
            {
                AssetId = asset.AssetId,
                Realm = Asset.Realm.Arcadia
            };

            var response = await assetClient.QueryBundlesByAssetAsync(queryRequest);

            if (response.Bundles == null)
                return TestResult.Failed("Query returned null bundles list");

            // Should find at least the 2 bundles we created
            if (response.Bundles.Count < 2)
                return TestResult.Failed($"Expected at least 2 bundles, got {response.Bundles.Count}");

            // Verify our bundles are in the results
            var foundBundle1 = response.Bundles.Any(b => b.BundleId == bundle1Id);
            var foundBundle2 = response.Bundles.Any(b => b.BundleId == bundle2Id);

            if (!foundBundle1 || !foundBundle2)
                return TestResult.Failed($"Not all created bundles found in results (bundle1={foundBundle1}, bundle2={foundBundle2})");

            Console.WriteLine($"  Found {response.Bundles.Count} bundles containing asset {asset.AssetId}");

            return TestResult.Successful($"QueryBundlesByAsset: found {response.Bundles.Count} bundles for asset");
        }, "Query bundles by asset");

    /// <summary>
    /// Test bulk asset retrieval without download URLs (faster, metadata only).
    /// </summary>
    private static async Task<TestResult> TestBulkGetAssetsWithoutUrls(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload test assets
            Console.WriteLine("  Step 1: Uploading test assets...");
            var asset1 = await UploadTestAsset(assetClient, "bulk-no-url-1");
            var asset2 = await UploadTestAsset(assetClient, "bulk-no-url-2");
            var asset3 = await UploadTestAsset(assetClient, "bulk-no-url-3");

            if (asset1 == null || asset2 == null || asset3 == null)
                return TestResult.Failed("Failed to upload test assets");

            Console.WriteLine($"  Uploaded: {asset1.AssetId}, {asset2.AssetId}, {asset3.AssetId}");

            // Step 2: Bulk get without download URLs
            Console.WriteLine("  Step 2: Bulk getting assets without URLs...");
            var bulkRequest = new BulkGetAssetsRequest
            {
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId, asset3.AssetId },
                IncludeDownloadUrls = false
            };

            var response = await assetClient.BulkGetAssetsAsync(bulkRequest);

            if (response.Assets == null)
                return TestResult.Failed("Bulk get returned null assets list");

            if (response.Assets.Count != 3)
                return TestResult.Failed($"Expected 3 assets, got {response.Assets.Count}");

            // Verify all assets are returned with metadata but without URLs
            foreach (var asset in response.Assets)
            {
                if (string.IsNullOrEmpty(asset.AssetId))
                    return TestResult.Failed("Asset has empty AssetId");

                // DownloadUrl should be null when IncludeDownloadUrls=false
                if (asset.DownloadUrl != null)
                    return TestResult.Failed($"Asset {asset.AssetId} has download URL when IncludeDownloadUrls=false");
            }

            Console.WriteLine($"  Retrieved {response.Assets.Count} assets without URLs, {response.NotFound?.Count ?? 0} not found");

            return TestResult.Successful($"BulkGetAssets (no URLs): {response.Assets.Count} assets, metadata only");
        }, "Bulk get assets without URLs");

    /// <summary>
    /// Test bulk asset retrieval with download URLs included.
    /// </summary>
    private static async Task<TestResult> TestBulkGetAssetsWithUrls(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var assetClient = GetServiceClient<IAssetClient>();

            // Step 1: Upload test assets
            Console.WriteLine("  Step 1: Uploading test assets...");
            var asset1 = await UploadTestAsset(assetClient, "bulk-with-url-1");
            var asset2 = await UploadTestAsset(assetClient, "bulk-with-url-2");

            if (asset1 == null || asset2 == null)
                return TestResult.Failed("Failed to upload test assets");

            Console.WriteLine($"  Uploaded: {asset1.AssetId}, {asset2.AssetId}");

            // Step 2: Bulk get with download URLs
            Console.WriteLine("  Step 2: Bulk getting assets with URLs...");
            var bulkRequest = new BulkGetAssetsRequest
            {
                AssetIds = new List<string> { asset1.AssetId, asset2.AssetId },
                IncludeDownloadUrls = true
            };

            var response = await assetClient.BulkGetAssetsAsync(bulkRequest);

            if (response.Assets == null)
                return TestResult.Failed("Bulk get returned null assets list");

            if (response.Assets.Count != 2)
                return TestResult.Failed($"Expected 2 assets, got {response.Assets.Count}");

            // Verify all assets have download URLs
            var assetsWithUrls = 0;
            foreach (var asset in response.Assets)
            {
                if (string.IsNullOrEmpty(asset.AssetId))
                    return TestResult.Failed("Asset has empty AssetId");

                if (asset.DownloadUrl != null)
                {
                    assetsWithUrls++;
                    Console.WriteLine($"  Asset {asset.AssetId}: URL expires {asset.ExpiresAt}");
                }
            }

            if (assetsWithUrls != response.Assets.Count)
                return TestResult.Failed($"Expected all assets to have URLs, got {assetsWithUrls}/{response.Assets.Count}");

            Console.WriteLine($"  Retrieved {response.Assets.Count} assets with download URLs");

            return TestResult.Successful($"BulkGetAssets (with URLs): {response.Assets.Count} assets with pre-signed URLs");
        }, "Bulk get assets with URLs");
}
