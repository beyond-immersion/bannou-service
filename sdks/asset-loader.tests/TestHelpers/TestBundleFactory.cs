using BeyondImmersion.Bannou.AssetLoader.Registry;
using BeyondImmersion.Bannou.Bundle.Format;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.TestHelpers;

/// <summary>
/// Factory for creating test bundles and LoadedBundle instances.
/// </summary>
internal static class TestBundleFactory
{
    /// <summary>
    /// Creates a minimal bundle in memory with the specified assets.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assets">Asset definitions (assetId, content).</param>
    /// <returns>A memory stream containing the bundle data.</returns>
    public static MemoryStream CreateBundleStream(string bundleId, params (string assetId, byte[] content)[] assets)
    {
        var outputStream = new MemoryStream();

        using (var writer = new BannouBundleWriter(outputStream))
        {
            foreach (var (assetId, content) in assets)
            {
                writer.AddAsset(
                    assetId: assetId,
                    filename: $"{assetId}.bin",
                    contentType: "application/octet-stream",
                    data: content);
            }

            writer.Finalize(
                bundleId: bundleId,
                name: bundleId,
                version: "1.0.0",
                createdBy: "TestBundleFactory");
        }

        // Reset stream position for reading
        outputStream.Position = 0;
        return outputStream;
    }

    /// <summary>
    /// Creates a LoadedBundle ready for use in tests.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assets">Asset definitions (assetId, content).</param>
    /// <returns>A LoadedBundle instance.</returns>
    public static LoadedBundle CreateLoadedBundle(string bundleId, params (string assetId, byte[] content)[] assets)
    {
        var bundleStream = CreateBundleStream(bundleId, assets);
        var reader = new BannouBundleReader(bundleStream);
        reader.ReadHeader();

        return new LoadedBundle
        {
            BundleId = bundleId,
            Manifest = reader.Manifest,
            AssetIds = reader.Manifest.Assets.Select(a => a.AssetId).ToList(),
            Reader = reader
        };
    }

    /// <summary>
    /// Creates a simple test asset with text content.
    /// </summary>
    public static (string assetId, byte[] content) TextAsset(string assetId, string text = "test content")
        => (assetId, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Creates a simple test asset with binary content.
    /// </summary>
    public static (string assetId, byte[] content) BinaryAsset(string assetId, int size = 1024)
    {
        var content = new byte[size];
        new Random(assetId.GetHashCode()).NextBytes(content);
        return (assetId, content);
    }
}
