using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Content;

internal static class ImageSurfaces
{
    internal static int GetPackedPitch(Image image)
    {
        (ushort width, ushort height) = image.Size;
        SDL_PixelFormat format = (SDL_PixelFormat)image.PixelFormat;
        int pitch = width * SDL_BYTESPERPIXEL(format);
        // An indexed image would need a palette, which Image does not carry, and a FourCC image has planes the pitch does not describe.
        if (pitch == 0 || SDL_ISPIXELFORMAT_INDEXED(format) || SDL_ISPIXELFORMAT_FOURCC(format) || image.Data.Length != (long)pitch * height)
        {
            throw new NotSupportedException($"Only tightly packed images in a non-planar, non-indexed pixel format are supported, not {image.PixelFormat} with {image.Data.Length} bytes for {width}x{height}.");
        }

        return pitch;
    }
}
