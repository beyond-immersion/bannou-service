namespace BeyondImmersion.Bannou.VoxelCore.Serialization;

/// <summary>
/// RLE (Run-Length Encoding) compression for voxel data.
/// Format: [count, value] pairs where count is 1-255.
/// Runs longer than 255 are split into multiple pairs.
/// Paired with LZ4 for the complete .bvox compression pipeline.
/// </summary>
public static class VoxelCompression
{
    /// <summary>
    /// RLE-encodes a byte array. Produces [count, value] pairs.
    /// </summary>
    /// <param name="data">Input byte array (typically 4096 bytes for one chunk dimension).</param>
    /// <returns>RLE-encoded byte array.</returns>
    public static byte[] RleEncode(byte[] data)
    {
        if (data.Length == 0)
            return Array.Empty<byte>();

        var output = new List<byte>();
        var position = 0;

        while (position < data.Length)
        {
            var value = data[position];
            var runLength = 1;

            while (position + runLength < data.Length && data[position + runLength] == value && runLength < 255)
            {
                runLength++;
            }

            output.Add((byte)runLength);
            output.Add(value);
            position += runLength;
        }

        return output.ToArray();
    }

    /// <summary>
    /// Decodes an RLE-encoded byte array back to the original data.
    /// </summary>
    /// <param name="encoded">RLE-encoded byte array ([count, value] pairs).</param>
    /// <param name="expectedLength">Expected output length for validation (0 to skip validation).</param>
    /// <returns>Decoded byte array.</returns>
    /// <exception cref="FormatException">Thrown if the encoded data is malformed.</exception>
    public static byte[] RleDecode(byte[] encoded, int expectedLength = 0)
    {
        var output = new List<byte>(expectedLength > 0 ? expectedLength : encoded.Length);

        for (var i = 0; i < encoded.Length; i += 2)
        {
            if (i + 1 >= encoded.Length)
                throw new FormatException("RLE data is truncated: missing value byte");

            var count = encoded[i];
            var value = encoded[i + 1];

            for (var j = 0; j < count; j++)
                output.Add(value);
        }

        if (expectedLength > 0 && output.Count != expectedLength)
            throw new FormatException($"RLE decoded {output.Count} bytes, expected {expectedLength}");

        return output.ToArray();
    }
}
