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

        // Error handling tests
        new ServiceTest(TestGetNonExistentAsset, "GetNonExistentAsset", "Asset", "Test 404 for non-existent asset"),
        new ServiceTest(TestGetNonExistentBundle, "GetNonExistentBundle", "Asset", "Test 404 for non-existent bundle"),

        // Complete lifecycle test
        new ServiceTest(TestCompleteAssetLifecycle, "CompleteAssetLifecycle", "Asset", "Test complete asset lifecycle: upload → search → bundle → download"),
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
}
