using System.Buffers.Binary;

namespace Pixely.Models.Tests;

// The first bytes of PNG and JPEG files, enough for the reader's size check. Nothing in the reader tests decodes them.
internal static class ImageHeaders
{
    public static byte[] Png(uint width, uint height)
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), height);
        return header;
    }

    // Start of image, an APP0 segment, then a baseline frame header with three components.
    public static byte[] Jpeg(ushort width, ushort height)
    {
        byte[] header = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, .. new byte[14], 0xFF, 0xC0, 0, 17, 8, 0, 0, 0, 0, 3, .. new byte[9]];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(25), height);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(27), width);
        return header;
    }
}
