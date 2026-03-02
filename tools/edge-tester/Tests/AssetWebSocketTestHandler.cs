using System.Text;
using System.Net.Http.Headers;
using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Asset;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for asset service API endpoints.
/// Tests the asset service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
///
/// Note: Asset upload requires HTTP PUT to MinIO, so this test handler focuses on
/// the API calls (request upload, complete upload, get asset, bundles, metabundles)
/// via WebSocket, using HTTP only for the actual file upload to MinIO.
/// </summary>
public class AssetWebSocketTestHandler : BaseWebSocketTestHandler
{
    private static readonly HttpClient _uploadClient = new();

    public override ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestGetBundleViaWebSocket, "Asset - Get Bundle (WebSocket)", "WebSocket",
                "Test bundle retrieval via typed proxy"),
            new ServiceTest(TestSearchAssetsViaWebSocket, "Asset - Search Assets (WebSocket)", "WebSocket",
                "Test asset search via typed proxy"),
            new ServiceTest(TestAssetUploadFlowViaWebSocket, "Asset - Upload Flow (WebSocket)", "WebSocket",
                "Test asset upload flow via typed proxy (API calls) and HTTP (file upload)"),
            new ServiceTest(TestCreateMetabundleViaWebSocket, "Asset - Create Metabundle (WebSocket)", "WebSocket",
                "Test metabundle creation with standalone assets via typed proxy"),
        };
    }

    /// <summary>
    /// Test searching for assets via WebSocket typed proxy.
    /// </summary>
    private void TestSearchAssetsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Search Test (WebSocket) ===");
        Console.WriteLine("Testing /assets/search via typed proxy...");

        RunWebSocketTest("Asset search test", async adminClient =>
        {
            // Search for assets with test tag using typed proxy
            var response = await adminClient.Asset.SearchAssetsAsync(new AssetSearchRequest
            {
                Tags = new List<string> { "test" },
                AssetType = AssetType.Behavior,
                Realm = "test-realm",
                Limit = 10,
                Offset = 0
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Search failed: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Assets array present: {result.Assets != null}");
            Console.WriteLine($"   Total: {result.Total}");

            return result.Assets != null;
        });
    }

    /// <summary>
    /// Test retrieving a bundle via WebSocket typed proxy.
    /// </summary>
    private void TestGetBundleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Get Bundle Test (WebSocket) ===");
        Console.WriteLine("Testing /bundles/get via typed proxy...");

        RunWebSocketTest("Asset get bundle test", async adminClient =>
        {
            // First upload and create a bundle for testing
            var uploadedAssetId = await UploadTestAssetAsync(adminClient, "ws-bundle-test");
            if (uploadedAssetId == null)
            {
                Console.WriteLine("   Failed to upload test asset");
                return false;
            }

            // Create a bundle using typed proxy
            var bundleId = $"ws-bundle-{DateTime.Now.Ticks}";
            var createResponse = await adminClient.Asset.CreateBundleAsync(new CreateBundleRequest
            {
                BundleId = bundleId,
                Version = "1.0.0",
                Owner = "edge-tester",
                AssetIds = new List<string> { uploadedAssetId },
                Compression = CompressionType.None
            }, timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create bundle: {FormatError(createResponse.Error)}");
                return false;
            }

            Console.WriteLine($"   Bundle created: {bundleId}, status: {createResponse.Result.Status}");

            // Now retrieve it using typed proxy
            var getResponse = await adminClient.Asset.GetBundleAsync(new GetBundleRequest
            {
                BundleId = bundleId,
                Format = BundleFormat.Bannou
            }, timeout: TimeSpan.FromSeconds(5));

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get bundle: {FormatError(getResponse.Error)}");
                return false;
            }

            var result = getResponse.Result;
            Console.WriteLine($"   Retrieved bundle: {result.BundleId}");
            Console.WriteLine($"   Has download URL: {result.DownloadUrl != null}");

            return result.BundleId == bundleId && result.DownloadUrl != null;
        });
    }

    /// <summary>
    /// Test the complete asset upload flow via WebSocket typed proxy for API calls.
    /// Uses HTTP only for the actual file upload to MinIO pre-signed URL.
    /// </summary>
    private void TestAssetUploadFlowViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Upload Flow Test (WebSocket) ===");
        Console.WriteLine("Testing upload flow via typed proxy (API) + HTTP (file upload)...");

        RunWebSocketTest("Asset upload flow test", async adminClient =>
        {
            // Step 1: Request upload URL via typed proxy
            Console.WriteLine("   Step 1: Requesting upload URL via typed proxy...");
            var testContent = $"{{\"test\": \"ws-upload-{DateTime.Now.Ticks}\"}}";
            var testBytes = Encoding.UTF8.GetBytes(testContent);

            var requestUploadResponse = await adminClient.Asset.RequestUploadAsync(new UploadRequest
            {
                Filename = $"ws-test-{DateTime.Now.Ticks}.json",
                Size = testBytes.Length,
                ContentType = "application/json",
                Owner = "edge-tester",
                Metadata = new AssetMetadataInput
                {
                    AssetType = AssetType.Behavior,
                    Realm = "test-realm",
                    Tags = new List<string> { "test", "websocket", "edge-test" }
                }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!requestUploadResponse.IsSuccess || requestUploadResponse.Result == null)
            {
                Console.WriteLine($"   Failed to request upload: {FormatError(requestUploadResponse.Error)}");
                return false;
            }

            var uploadUrl = requestUploadResponse.Result.UploadUrl;
            var uploadId = requestUploadResponse.Result.UploadId;

            if (uploadUrl == null)
            {
                Console.WriteLine("   Failed to get upload URL");
                return false;
            }
            Console.WriteLine($"   Got upload URL, uploadId: {uploadId}");

            // Step 2: Upload file to MinIO via HTTP
            Console.WriteLine("   Step 2: Uploading file to MinIO via HTTP...");
            using var content = new ByteArrayContent(testBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var minioResponse = await _uploadClient.PutAsync(uploadUrl, content);
            if (!minioResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"   MinIO upload failed: {minioResponse.StatusCode}");
                return false;
            }
            Console.WriteLine("   File uploaded to MinIO");

            // Step 3: Complete upload via typed proxy
            Console.WriteLine("   Step 3: Completing upload via typed proxy...");
            var completeResponse = await adminClient.Asset.CompleteUploadAsync(new CompleteUploadRequest
            {
                UploadId = uploadId
            }, timeout: TimeSpan.FromSeconds(10));

            if (!completeResponse.IsSuccess || completeResponse.Result == null)
            {
                Console.WriteLine($"   Failed to complete upload: {FormatError(completeResponse.Error)}");
                return false;
            }

            var assetId = completeResponse.Result.AssetId;
            var processingStatus = completeResponse.Result.ProcessingStatus;

            Console.WriteLine($"   Upload complete: assetId={assetId}, status={processingStatus}");

            return !string.IsNullOrEmpty(assetId);
        });
    }

    /// <summary>
    /// Test metabundle creation with standalone assets via WebSocket typed proxy.
    /// This tests the new standalone asset support for packaging behaviors with 3D assets.
    /// </summary>
    private void TestCreateMetabundleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Create Metabundle Test (WebSocket) ===");
        Console.WriteLine("Testing metabundle creation with standalone assets via typed proxy...");

        RunWebSocketTest("Asset create metabundle test", async adminClient =>
        {
            // Step 1: Upload standalone assets
            Console.WriteLine("   Step 1: Uploading standalone assets...");
            var standaloneAsset1 = await UploadTestAssetAsync(adminClient, "ws-metabundle-standalone-1");
            var standaloneAsset2 = await UploadTestAssetAsync(adminClient, "ws-metabundle-standalone-2");

            if (standaloneAsset1 == null || standaloneAsset2 == null)
            {
                Console.WriteLine("   Failed to upload standalone assets");
                return false;
            }
            Console.WriteLine($"   Uploaded: {standaloneAsset1}, {standaloneAsset2}");

            // Step 2: Create metabundle from standalone assets using typed proxy
            Console.WriteLine("   Step 2: Creating metabundle from standalone assets...");
            var metabundleId = $"ws-metabundle-{DateTime.Now.Ticks}";

            var createResponse = await adminClient.Asset.CreateMetabundleAsync(new CreateMetabundleRequest
            {
                MetabundleId = metabundleId,
                StandaloneAssetIds = new List<string> { standaloneAsset1, standaloneAsset2 },
                Owner = "edge-tester",
                Version = "1.0.0",
                Realm = "test-realm"
            }, timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create metabundle: {FormatError(createResponse.Error)}");
                return false;
            }

            var result = createResponse.Result;
            Console.WriteLine($"   Metabundle created: status={result.Status}, assetCount={result.AssetCount}, standaloneCount={result.StandaloneAssetCount}");

            // For metabundle with standalone assets, we expect Ready or Queued status
            return result.Status == BundleStatus.Ready || result.Status == BundleStatus.Queued;
        });
    }

    /// <summary>
    /// Helper method to upload a test asset via WebSocket typed proxy.
    /// Returns the asset ID (as string) if successful.
    /// </summary>
    private async Task<string?> UploadTestAssetAsync(BannouClient adminClient, string testName)
    {
        try
        {
            var testContent = $"{{\"test\": \"{testName}\", \"timestamp\": \"{DateTime.UtcNow:O}\"}}";
            var testBytes = Encoding.UTF8.GetBytes(testContent);

            // Request upload URL using typed proxy
            var requestResponse = await adminClient.Asset.RequestUploadAsync(new UploadRequest
            {
                Filename = $"{testName}-{DateTime.Now.Ticks}.json",
                Size = testBytes.Length,
                ContentType = "application/json",
                Owner = "edge-tester",
                Metadata = new AssetMetadataInput
                {
                    AssetType = AssetType.Behavior,
                    Realm = "test-realm",
                    Tags = new List<string> { "test", "edge-test", testName }
                }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!requestResponse.IsSuccess || requestResponse.Result == null)
                return null;

            var uploadUrl = requestResponse.Result.UploadUrl;
            var uploadId = requestResponse.Result.UploadId;

            if (uploadUrl == null)
                return null;

            // Upload to MinIO
            using var content = new ByteArrayContent(testBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var minioResponse = await _uploadClient.PutAsync(uploadUrl, content);
            if (!minioResponse.IsSuccessStatusCode)
                return null;

            // Complete upload using typed proxy
            var completeResponse = await adminClient.Asset.CompleteUploadAsync(new CompleteUploadRequest
            {
                UploadId = uploadId
            }, timeout: TimeSpan.FromSeconds(10));

            if (!completeResponse.IsSuccess || completeResponse.Result == null)
                return null;

            return completeResponse.Result.AssetId;
        }
        catch
        {
            return null;
        }
    }
}
