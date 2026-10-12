using System.Buffers.Binary;

namespace Pixely.Models.Gltf;

// Reads the size of a PNG or JPEG image from its header, so the reader can reject an image before anything decodes it. A small
// compressed image can declare a size that takes gigabytes to decode.
internal static class ImageHeader
{
    // Decoding takes 4 bytes per pixel, so the largest image takes 1 GiB.
    public const int MaxSide = 16384;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Check(ReadOnlySpan<byte> image, string mimeType, string name)
    {
        (int width, int height) = mimeType == "image/png" ? ReadPngSize(image, name) : ReadJpegSize(image, name);
        if (width < 1 || height < 1 || width > MaxSide || height > MaxSide)
        {
            throw new InvalidDataException($"{name}: the image is {width}x{height} pixels; each side must be between 1 and {MaxSide}.");
        }
    }

    // The signature is followed by the IHDR chunk: its length, its type, then the width and height as big-endian integers.
    private static (int Width, int Height) ReadPngSize(ReadOnlySpan<byte> image, string name)
    {
        if (image.Length < 24 || !image.StartsWith(PngSignature) || !image[12..16].SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException($"{name}: the data is not a PNG image, as its mimeType says.");
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(image[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(image[20..]);
        return ((int)Math.Min(width, int.MaxValue), (int)Math.Min(height, int.MaxValue));
    }

    // Walks the marker segments to the first frame header (SOF), which holds the height and then the width as big-endian integers.
    private static (int Width, int Height) ReadJpegSize(ReadOnlySpan<byte> image, string name)
    {
        if (image.Length < 4 || image[0] != 0xFF || image[1] != 0xD8)
        {
            throw new InvalidDataException($"{name}: the data is not a JPEG image, as its mimeType says.");
        }

        int offset = 2;
        while (offset + 4 <= image.Length)
        {
            if (image[offset] != 0xFF)
            {
                throw new InvalidDataException($"{name}: the JPEG image has no marker at byte {offset}.");
            }

            byte marker = image[offset + 1];

            // Fill bytes before a marker, and markers without a segment.
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(image[(offset + 2)..]);
            if (length < 2)
            {
                throw new InvalidDataException($"{name}: the JPEG segment at byte {offset} has a length of {length}.");
            }

            // SOF0 to SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7 || offset + 9 > image.Length)
                {
                    break;
                }

                return (BinaryPrimitives.ReadUInt16BigEndian(image[(offset + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(image[(offset + 5)..]));
            }

            offset += 2 + length;
        }

        throw new InvalidDataException($"{name}: the JPEG image has no frame header before its image data.");
    }
}
