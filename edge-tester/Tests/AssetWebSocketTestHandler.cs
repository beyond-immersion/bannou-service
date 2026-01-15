using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for asset service API endpoints.
/// Tests the asset service APIs through the Connect service WebSocket binary protocol.
///
/// Note: Asset upload requires HTTP PUT to MinIO, so this test handler focuses on
/// the API calls (request upload, complete upload, get asset, bundles, metabundles)
/// via WebSocket, using HTTP only for the actual file upload to MinIO.
/// </summary>
public class AssetWebSocketTestHandler : IServiceTestHandler
{
    private static readonly HttpClient _uploadClient = new();

    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestGetBundleViaWebSocket, "Asset - Get Bundle (WebSocket)", "WebSocket",
                "Test bundle retrieval via WebSocket binary protocol"),
            new ServiceTest(TestSearchAssetsViaWebSocket, "Asset - Search Assets (WebSocket)", "WebSocket",
                "Test asset search via WebSocket binary protocol"),
            new ServiceTest(TestAssetUploadFlowViaWebSocket, "Asset - Upload Flow (WebSocket)", "WebSocket",
                "Test asset upload flow via WebSocket (API calls) and HTTP (file upload)"),
            new ServiceTest(TestCreateMetabundleViaWebSocket, "Asset - Create Metabundle (WebSocket)", "WebSocket",
                "Test metabundle creation with standalone assets via WebSocket"),
        };
    }

    /// <summary>
    /// Test searching for assets via WebSocket.
    /// </summary>
    private void TestSearchAssetsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Search Test (WebSocket) ===");
        Console.WriteLine("Testing /assets/search via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                // Search for assets with test tag
                var response = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/assets/search",
                    new
                    {
                        tags = new[] { "test" },
                        limit = 10,
                        offset = 0
                    },
                    timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                var json = JsonNode.Parse(response.GetRawText())?.AsObject();
                var hasAssetsArray = json?["assets"] != null && json["assets"] is JsonArray;
                var total = json?["total"]?.GetValue<int>() ?? 0;

                Console.WriteLine($"   Assets array present: {hasAssetsArray}");
                Console.WriteLine($"   Total: {total}");

                return hasAssetsArray;
            }).Result;

            if (result)
                Console.WriteLine("Asset search test PASSED");
            else
                Console.WriteLine("Asset search test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset search test FAILED with exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Test retrieving a bundle via WebSocket (expects 404 for non-existent bundle).
    /// </summary>
    private void TestGetBundleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Get Bundle Test (WebSocket) ===");
        Console.WriteLine("Testing /bundles/get via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                // First upload and create a bundle for testing
                var uploadedAssetId = await UploadTestAssetAsync(adminClient, "ws-bundle-test");
                if (uploadedAssetId == null)
                {
                    Console.WriteLine("   Failed to upload test asset");
                    return false;
                }

                // Create a bundle
                var bundleId = $"ws-bundle-{DateTime.Now.Ticks}";
                var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/bundles/create",
                    new
                    {
                        bundleId = bundleId,
                        version = "1.0.0",
                        owner = "edge-tester",
                        assetIds = new[] { uploadedAssetId },
                        compression = "none"
                    },
                    timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                var status = createJson?["status"]?.GetValue<string>();
                Console.WriteLine($"   Bundle created: {bundleId}, status: {status}");

                // Now retrieve it
                var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/bundles/get",
                    new
                    {
                        bundleId = bundleId,
                        format = "bannou"
                    },
                    timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                var retrievedBundleId = getJson?["bundleId"]?.GetValue<string>();
                var downloadUrl = getJson?["downloadUrl"]?.GetValue<string>();

                Console.WriteLine($"   Retrieved bundle: {retrievedBundleId}");
                Console.WriteLine($"   Has download URL: {!string.IsNullOrEmpty(downloadUrl)}");

                return retrievedBundleId == bundleId && !string.IsNullOrEmpty(downloadUrl);
            }).Result;

            if (result)
                Console.WriteLine("Asset get bundle test PASSED");
            else
                Console.WriteLine("Asset get bundle test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset get bundle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// Test the complete asset upload flow via WebSocket for API calls.
    /// Uses HTTP only for the actual file upload to MinIO pre-signed URL.
    /// </summary>
    private void TestAssetUploadFlowViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Upload Flow Test (WebSocket) ===");
        Console.WriteLine("Testing upload flow via WebSocket (API) + HTTP (file upload)...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                // Step 1: Request upload URL via WebSocket
                Console.WriteLine("   Step 1: Requesting upload URL via WebSocket...");
                var testContent = $"{{\"test\": \"ws-upload-{DateTime.Now.Ticks}\"}}";
                var testBytes = Encoding.UTF8.GetBytes(testContent);

                var requestUploadResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/assets/upload/request",
                    new
                    {
                        filename = $"ws-test-{DateTime.Now.Ticks}.json",
                        size = testBytes.Length,
                        contentType = "application/json",
                        owner = "edge-tester",
                        metadata = new
                        {
                            assetType = "behavior",
                            realm = "arcadia",
                            tags = new[] { "test", "websocket", "edge-test" }
                        }
                    },
                    timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                var requestJson = JsonNode.Parse(requestUploadResponse.GetRawText())?.AsObject();
                var uploadUrl = requestJson?["uploadUrl"]?.GetValue<string>();
                var uploadIdStr = requestJson?["uploadId"]?.GetValue<string>();

                if (string.IsNullOrEmpty(uploadUrl) || string.IsNullOrEmpty(uploadIdStr))
                {
                    Console.WriteLine("   Failed to get upload URL");
                    return false;
                }
                Console.WriteLine($"   Got upload URL, uploadId: {uploadIdStr}");

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

                // Step 3: Complete upload via WebSocket
                Console.WriteLine("   Step 3: Completing upload via WebSocket...");
                var completeResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/assets/upload/complete",
                    new { uploadId = uploadIdStr },
                    timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                var completeJson = JsonNode.Parse(completeResponse.GetRawText())?.AsObject();
                var assetId = completeJson?["assetId"]?.GetValue<string>();
                var processingStatus = completeJson?["processingStatus"]?.GetValue<string>();

                Console.WriteLine($"   Upload complete: assetId={assetId}, status={processingStatus}");

                return !string.IsNullOrEmpty(assetId);
            }).Result;

            if (result)
                Console.WriteLine("Asset upload flow test PASSED");
            else
                Console.WriteLine("Asset upload flow test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset upload flow test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// Test metabundle creation with standalone assets via WebSocket.
    /// This tests the new standalone asset support for packaging behaviors with 3D assets.
    /// </summary>
    private void TestCreateMetabundleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Asset Create Metabundle Test (WebSocket) ===");
        Console.WriteLine("Testing metabundle creation with standalone assets via WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

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

                // Step 2: Create metabundle from standalone assets
                Console.WriteLine("   Step 2: Creating metabundle from standalone assets...");
                var metabundleId = $"ws-metabundle-{DateTime.Now.Ticks}";

                var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                    "POST",
                    "/bundles/create",
                    new
                    {
                        metabundleId = metabundleId,
                        standaloneAssetIds = new[] { standaloneAsset1, standaloneAsset2 },
                        owner = "edge-tester",
                        version = "1.0.0",
                        realm = "arcadia"
                    },
                    timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

                var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                var status = createJson?["status"]?.GetValue<string>();
                var assetCount = createJson?["assetCount"]?.GetValue<int>() ?? 0;
                var standaloneCount = createJson?["standaloneAssetCount"]?.GetValue<int>();

                Console.WriteLine($"   Metabundle created: status={status}, assetCount={assetCount}, standaloneCount={standaloneCount}");

                // For metabundle with standalone assets, we expect Ready or Queued status
                return status == "ready" || status == "queued" || status == "Ready" || status == "Queued";
            }).Result;

            if (result)
                Console.WriteLine("Asset create metabundle test PASSED");
            else
                Console.WriteLine("Asset create metabundle test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset create metabundle test FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// Helper method to upload a test asset via WebSocket API calls.
    /// Returns the asset ID if successful.
    /// </summary>
    private async Task<string?> UploadTestAssetAsync(BannouClient adminClient, string testName)
    {
        try
        {
            var testContent = $"{{\"test\": \"{testName}\", \"timestamp\": \"{DateTime.UtcNow:O}\"}}";
            var testBytes = Encoding.UTF8.GetBytes(testContent);

            // Request upload URL
            var requestResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/assets/upload/request",
                new
                {
                    filename = $"{testName}-{DateTime.Now.Ticks}.json",
                    size = testBytes.Length,
                    contentType = "application/json",
                    owner = "edge-tester",
                    metadata = new
                    {
                        assetType = "behavior",
                        realm = "arcadia",
                        tags = new[] { "test", "edge-test", testName }
                    }
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var requestJson = JsonNode.Parse(requestResponse.GetRawText())?.AsObject();
            var uploadUrl = requestJson?["uploadUrl"]?.GetValue<string>();
            var uploadIdStr = requestJson?["uploadId"]?.GetValue<string>();

            if (string.IsNullOrEmpty(uploadUrl) || string.IsNullOrEmpty(uploadIdStr))
                return null;

            // Upload to MinIO
            using var content = new ByteArrayContent(testBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var minioResponse = await _uploadClient.PutAsync(uploadUrl, content);
            if (!minioResponse.IsSuccessStatusCode)
                return null;

            // Complete upload
            var completeResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/assets/upload/complete",
                new { uploadId = uploadIdStr },
                timeout: TimeSpan.FromSeconds(10))).GetResultOrThrow();

            var completeJson = JsonNode.Parse(completeResponse.GetRawText())?.AsObject();
            return completeJson?["assetId"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
