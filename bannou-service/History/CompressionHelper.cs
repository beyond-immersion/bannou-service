using System.IO;
using System.IO.Compression;
using System.Text;

namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Shared compression utilities for history and archive services.
/// Used by character-personality, character-history, character-encounter,
/// and realm-history for decompressing archived entity data.
/// </summary>
public static class CompressionHelper
{
    /// <summary>
    /// Decompresses gzipped JSON data to a UTF-8 string.
    /// </summary>
    /// <param name="compressedData">The gzip-compressed byte array.</param>
    /// <returns>The decompressed JSON string.</returns>
    public static string DecompressJsonData(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
