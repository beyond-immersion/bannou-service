namespace BeyondImmersion.BannouService.Misc;

/// <summary>
/// Static helper for CRC encoding strings.
/// Used in routing by service names.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Crc32Table;

    static Crc32()
    {
        const uint polynomial = 0xedb88320u;
        Crc32Table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (uint j = 8; j > 0; j--)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            Crc32Table[i] = crc;
        }
    }

    /// <summary>
    /// Computes the CRC32 checksum for the given input string.
    /// </summary>
    /// <param name="input">The input string to compute CRC32 for.</param>
    /// <returns>The 32-bit CRC checksum.</returns>
    /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
    public static uint ComputeCRC32(string input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        uint crcValue = 0xffffffff;

        foreach (var b in bytes)
        {
            byte tableIndex = (byte)(((crcValue) & 0xff) ^ b);
            crcValue = Crc32Table[tableIndex] ^ (crcValue >> 8);
        }

        return ~crcValue;
    }
}
