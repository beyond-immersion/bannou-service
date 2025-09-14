using System;
using System.Buffers.Binary;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Network byte order utilities for cross-platform binary protocol compatibility.
/// Ensures consistent endianness across all client and server implementations.
/// </summary>
public static class NetworkByteOrder
{
    /// <summary>
    /// Writes a 16-bit unsigned integer in network byte order (big-endian).
    /// </summary>
    public static void WriteUInt16(Span<byte> destination, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, value);
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer in network byte order (big-endian).
    /// </summary>
    public static void WriteUInt32(Span<byte> destination, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, value);
    }

    /// <summary>
    /// Writes a 64-bit unsigned integer in network byte order (big-endian).
    /// </summary>
    public static void WriteUInt64(Span<byte> destination, ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, value);
    }

    /// <summary>
    /// Reads a 16-bit unsigned integer from network byte order (big-endian).
    /// </summary>
    public static ushort ReadUInt16(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(source);
    }

    /// <summary>
    /// Reads a 32-bit unsigned integer from network byte order (big-endian).
    /// </summary>
    public static uint ReadUInt32(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    /// <summary>
    /// Reads a 64-bit unsigned integer from network byte order (big-endian).
    /// </summary>
    public static ulong ReadUInt64(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt64BigEndian(source);
    }

    /// <summary>
    /// Writes a GUID in consistent network byte order.
    /// Uses RFC 4122 standard byte ordering for cross-platform compatibility.
    /// </summary>
    public static void WriteGuid(Span<byte> destination, Guid guid)
    {
        if (destination.Length < 16)
            throw new ArgumentException("Destination must be at least 16 bytes", nameof(destination));

        // Get GUID bytes in RFC 4122 format (network standard)
        var guidBytes = guid.ToByteArray();

        // RFC 4122 specifies network byte order for time fields
        // Convert the first 8 bytes from little-endian to big-endian
        // Keep bytes 8-15 as-is (they're already in network order)

        // Time-low (4 bytes) - reverse
        destination[0] = guidBytes[3];
        destination[1] = guidBytes[2];
        destination[2] = guidBytes[1];
        destination[3] = guidBytes[0];

        // Time-mid (2 bytes) - reverse
        destination[4] = guidBytes[5];
        destination[5] = guidBytes[4];

        // Time-high-and-version (2 bytes) - reverse
        destination[6] = guidBytes[7];
        destination[7] = guidBytes[6];

        // Clock-seq-and-reserved + clock-seq-low + node (8 bytes) - keep as-is
        guidBytes.AsSpan(8, 8).CopyTo(destination.Slice(8, 8));
    }

    /// <summary>
    /// Reads a GUID from consistent network byte order.
    /// Uses RFC 4122 standard byte ordering for cross-platform compatibility.
    /// </summary>
    public static Guid ReadGuid(ReadOnlySpan<byte> source)
    {
        if (source.Length < 16)
            throw new ArgumentException("Source must be at least 16 bytes", nameof(source));

        var guidBytes = new byte[16];

        // Convert from network byte order back to system byte order
        // Reverse the first 8 bytes for time fields
        guidBytes[0] = source[3];
        guidBytes[1] = source[2];
        guidBytes[2] = source[1];
        guidBytes[3] = source[0];

        guidBytes[4] = source[5];
        guidBytes[5] = source[4];

        guidBytes[6] = source[7];
        guidBytes[7] = source[6];

        // Copy clock-seq and node (8 bytes) as-is
        source.Slice(8, 8).CopyTo(guidBytes.AsSpan(8, 8));

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Validates that the current system can handle network byte order operations.
    /// Returns true if BinaryPrimitives are available (should always be true in .NET Core/5+).
    /// </summary>
    public static bool IsNetworkByteOrderSupported()
    {
        try
        {
            // Test basic operations
            Span<byte> testBuffer = stackalloc byte[8];
            WriteUInt32(testBuffer, 0x12345678);
            var result = ReadUInt32(testBuffer);
            return result == 0x12345678;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets information about the current system's endianness for debugging.
    /// </summary>
    public static string GetSystemEndianness()
    {
        return BitConverter.IsLittleEndian ? "Little-Endian" : "Big-Endian";
    }

    /// <summary>
    /// Tests cross-platform compatibility of network byte order operations.
    /// Useful for unit testing and validation.
    /// </summary>
    public static bool TestNetworkByteOrderCompatibility()
    {
        try
        {
            Span<byte> buffer = stackalloc byte[32];

            // Test all data types
            WriteUInt16(buffer.Slice(0, 2), 0x1234);
            WriteUInt32(buffer.Slice(2, 4), 0x12345678);
            WriteUInt64(buffer.Slice(6, 8), 0x123456789ABCDEF0);

            var testGuid = new Guid("12345678-1234-5678-9ABC-DEF012345678");
            WriteGuid(buffer.Slice(14, 16), testGuid);

            // Read back and verify
            var readUInt16 = ReadUInt16(buffer.Slice(0, 2));
            var readUInt32 = ReadUInt32(buffer.Slice(2, 4));
            var readUInt64 = ReadUInt64(buffer.Slice(6, 8));
            var readGuid = ReadGuid(buffer.Slice(14, 16));

            return readUInt16 == 0x1234 &&
                   readUInt32 == 0x12345678 &&
                   readUInt64 == 0x123456789ABCDEF0 &&
                   readGuid == testGuid;
        }
        catch
        {
            return false;
        }
    }
}
