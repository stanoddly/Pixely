namespace Pixely.Gpu;

public static class TextureFormatExtensions
{
    /// <summary>
    /// Calculates the size in bytes for a texture with the given dimensions and format.
    /// Handles both uncompressed and block-compressed formats correctly.
    /// </summary>
    public static long CalculateSizeInBytes(this TextureFormat format, int width, int height, int layerCount = 1)
    {
        var info = GetFormatInfo(format);

        if (info.IsCompressed)
        {
            int blocksX = (width + info.BlockWidth - 1) / info.BlockWidth;
            int blocksY = (height + info.BlockHeight - 1) / info.BlockHeight;
            return (long)blocksX * blocksY * info.BytesPerBlock * layerCount;
        }

        return (long)width * height * info.BytesPerBlock * layerCount;
    }

    /// <summary>
    /// The CPU-side pixel layout of a texture's bytes, for formats SDL surfaces can hold. Throws for every other format.
    /// </summary>
    public static PixelFormat ToPixelFormat(this TextureFormat format) => format switch
    {
        // SDL names packed formats from the most significant byte, so on little-endian machines the memory order is reversed.
        TextureFormat.R8G8B8A8Unorm or TextureFormat.R8G8B8A8UnormSrgb => PixelFormat.Abgr8888,
        TextureFormat.B8G8R8A8Unorm or TextureFormat.B8G8R8A8UnormSrgb => PixelFormat.Argb8888,
        _ => throw new NotSupportedException($"Texture format '{format}' has no CPU pixel format.")
    };

    private static FormatInfo GetFormatInfo(TextureFormat format) => format switch
    {
        // 1 byte per pixel
        TextureFormat.A8Unorm => new(1),
        TextureFormat.R8Unorm => new(1),
        TextureFormat.R8Snorm => new(1),
        TextureFormat.R8Uint => new(1),
        TextureFormat.R8Int => new(1),

        // 2 bytes per pixel
        TextureFormat.R8G8Unorm => new(2),
        TextureFormat.R8G8Snorm => new(2),
        TextureFormat.R8G8Uint => new(2),
        TextureFormat.R8G8Int => new(2),
        TextureFormat.R16Unorm => new(2),
        TextureFormat.R16Snorm => new(2),
        TextureFormat.R16Uint => new(2),
        TextureFormat.R16Int => new(2),
        TextureFormat.R16Float => new(2),
        TextureFormat.B5G6R5Unorm => new(2),
        TextureFormat.B5G5R5A1Unorm => new(2),
        TextureFormat.B4G4R4A4Unorm => new(2),
        TextureFormat.D16Unorm => new(2),

        // 3 bytes per pixel
        TextureFormat.D24Unorm => new(3),

        // 4 bytes per pixel
        TextureFormat.R8G8B8A8Unorm => new(4),
        TextureFormat.R8G8B8A8Snorm => new(4),
        TextureFormat.R8G8B8A8Uint => new(4),
        TextureFormat.R8G8B8A8Int => new(4),
        TextureFormat.R8G8B8A8UnormSrgb => new(4),
        TextureFormat.B8G8R8A8Unorm => new(4),
        TextureFormat.B8G8R8A8UnormSrgb => new(4),
        TextureFormat.R16G16Unorm => new(4),
        TextureFormat.R16G16Snorm => new(4),
        TextureFormat.R16G16Uint => new(4),
        TextureFormat.R16G16Int => new(4),
        TextureFormat.R16G16Float => new(4),
        TextureFormat.R32Uint => new(4),
        TextureFormat.R32Int => new(4),
        TextureFormat.R32Float => new(4),
        TextureFormat.R10G10B10A2Unorm => new(4),
        TextureFormat.R11G11B10Ufloat => new(4),
        TextureFormat.D32Float => new(4),
        TextureFormat.D24UnormS8Uint => new(4),

        // 8 bytes per pixel
        TextureFormat.R16G16B16A16Unorm => new(8),
        TextureFormat.R16G16B16A16Snorm => new(8),
        TextureFormat.R16G16B16A16Uint => new(8),
        TextureFormat.R16G16B16A16Int => new(8),
        TextureFormat.R16G16B16A16Float => new(8),
        TextureFormat.R32G32Uint => new(8),
        TextureFormat.R32G32Int => new(8),
        TextureFormat.R32G32Float => new(8),
        TextureFormat.D32FloatS8Uint => new(8),

        // 16 bytes per pixel
        TextureFormat.R32G32B32A32Uint => new(16),
        TextureFormat.R32G32B32A32Int => new(16),
        TextureFormat.R32G32B32A32Float => new(16),

        // BC compressed formats (4x4 blocks)
        TextureFormat.Bc1RgbaUnorm => new(8, 4, 4, true),
        TextureFormat.Bc1RgbaUnormSrgb => new(8, 4, 4, true),
        TextureFormat.Bc2RgbaUnorm => new(16, 4, 4, true),
        TextureFormat.Bc2RgbaUnormSrgb => new(16, 4, 4, true),
        TextureFormat.Bc3RgbaUnorm => new(16, 4, 4, true),
        TextureFormat.Bc3RgbaUnormSrgb => new(16, 4, 4, true),
        TextureFormat.Bc4RUnorm => new(8, 4, 4, true),
        TextureFormat.Bc5RgUnorm => new(16, 4, 4, true),
        TextureFormat.Bc6HRgbFloat => new(16, 4, 4, true),
        TextureFormat.Bc6HRgbUfloat => new(16, 4, 4, true),
        TextureFormat.Bc7RgbaUnorm => new(16, 4, 4, true),
        TextureFormat.Bc7RgbaUnormSrgb => new(16, 4, 4, true),

        // ASTC compressed formats (16 bytes per block, variable block sizes)
        TextureFormat.Astc4x4Unorm => new(16, 4, 4, true),
        TextureFormat.Astc4x4UnormSrgb => new(16, 4, 4, true),
        TextureFormat.Astc4x4Float => new(16, 4, 4, true),
        TextureFormat.Astc5x4Unorm => new(16, 5, 4, true),
        TextureFormat.Astc5x4UnormSrgb => new(16, 5, 4, true),
        TextureFormat.Astc5x4Float => new(16, 5, 4, true),
        TextureFormat.Astc5x5Unorm => new(16, 5, 5, true),
        TextureFormat.Astc5x5UnormSrgb => new(16, 5, 5, true),
        TextureFormat.Astc5x5Float => new(16, 5, 5, true),
        TextureFormat.Astc6x5Unorm => new(16, 6, 5, true),
        TextureFormat.Astc6x5UnormSrgb => new(16, 6, 5, true),
        TextureFormat.Astc6x5Float => new(16, 6, 5, true),
        TextureFormat.Astc6x6Unorm => new(16, 6, 6, true),
        TextureFormat.Astc6x6UnormSrgb => new(16, 6, 6, true),
        TextureFormat.Astc6x6Float => new(16, 6, 6, true),
        TextureFormat.Astc8x5Unorm => new(16, 8, 5, true),
        TextureFormat.Astc8x5UnormSrgb => new(16, 8, 5, true),
        TextureFormat.Astc8x5Float => new(16, 8, 5, true),
        TextureFormat.Astc8x6Unorm => new(16, 8, 6, true),
        TextureFormat.Astc8x6UnormSrgb => new(16, 8, 6, true),
        TextureFormat.Astc8x6Float => new(16, 8, 6, true),
        TextureFormat.Astc8x8Unorm => new(16, 8, 8, true),
        TextureFormat.Astc8x8UnormSrgb => new(16, 8, 8, true),
        TextureFormat.Astc8x8Float => new(16, 8, 8, true),
        TextureFormat.Astc10x5Unorm => new(16, 10, 5, true),
        TextureFormat.Astc10x5UnormSrgb => new(16, 10, 5, true),
        TextureFormat.Astc10x5Float => new(16, 10, 5, true),
        TextureFormat.Astc10x6Unorm => new(16, 10, 6, true),
        TextureFormat.Astc10x6UnormSrgb => new(16, 10, 6, true),
        TextureFormat.Astc10x6Float => new(16, 10, 6, true),
        TextureFormat.Astc10x8Unorm => new(16, 10, 8, true),
        TextureFormat.Astc10x8UnormSrgb => new(16, 10, 8, true),
        TextureFormat.Astc10x8Float => new(16, 10, 8, true),
        TextureFormat.Astc10x10Unorm => new(16, 10, 10, true),
        TextureFormat.Astc10x10UnormSrgb => new(16, 10, 10, true),
        TextureFormat.Astc10x10Float => new(16, 10, 10, true),
        TextureFormat.Astc12x10Unorm => new(16, 12, 10, true),
        TextureFormat.Astc12x10UnormSrgb => new(16, 12, 10, true),
        TextureFormat.Astc12x10Float => new(16, 12, 10, true),
        TextureFormat.Astc12x12Unorm => new(16, 12, 12, true),
        TextureFormat.Astc12x12UnormSrgb => new(16, 12, 12, true),
        TextureFormat.Astc12x12Float => new(16, 12, 12, true),

        _ => new(0)
    };

    private readonly record struct FormatInfo(int BytesPerBlock, int BlockWidth = 1, int BlockHeight = 1, bool IsCompressed = false);
}
