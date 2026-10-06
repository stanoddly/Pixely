using Pixely.Utilities;
using SDL;

namespace Pixely.Content;

internal static class ImageSurfaces
{
    internal static int GetPackedPitch(Image image)
    {
        (ushort width, ushort height) = image.Size;
        SDL_PixelFormat format = (SDL_PixelFormat)image.PixelFormat;
        int pitch = width * SDL3.SDL_BYTESPERPIXEL(format);
        // An indexed image would need a palette, which Image does not carry, and a FourCC image has planes the pitch does not describe.
        if (pitch == 0 || SDL3.SDL_ISPIXELFORMAT_INDEXED(format) || SDL3.SDL_ISPIXELFORMAT_FOURCC(format) || image.Data.Length != (long)pitch * height)
        {
            throw new NotSupportedException($"Only tightly packed images in a non-planar, non-indexed pixel format are supported, not {image.PixelFormat} with {image.Data.Length} bytes for {width}x{height}.");
        }

        return pitch;
    }

    // SDL_CreateSurfaceFrom only borrows the pixels, which stay pinned for the call alone, so a surface SDL keeps after the call,
    // as an animated cursor on Windows does, must be a copy SDL owns.
    internal static unsafe Pointer<SDL_Surface> CreateCopy(Image image, SDL_PixelFormat format)
    {
        int pitch = GetPackedPitch(image);
        (ushort width, ushort height) = image.Size;

        fixed (byte* pixels = image.Data)
        {
            Pointer<SDL_Surface> borrowed = SDL3.SDL_CreateSurfaceFrom(width, height, (SDL_PixelFormat)image.PixelFormat, (IntPtr)pixels, pitch);
            if (borrowed.IsNull)
            {
                throw new PixelyException($"SDL_CreateSurfaceFrom failed: {SDL3.SDL_GetError()}");
            }

            try
            {
                Pointer<SDL_Surface> copy = SDL3.SDL_ConvertSurface(borrowed, format);
                if (copy.IsNull)
                {
                    throw new PixelyException($"SDL_ConvertSurface failed: {SDL3.SDL_GetError()}");
                }

                return copy;
            }
            finally
            {
                SDL3.SDL_DestroySurface(borrowed);
            }
        }
    }
}
