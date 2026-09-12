using Pixely.Utilities;
using SDL;

namespace Pixely.Content;

internal class SdlImageWriter : IImageWriter
{
    public unsafe void SavePng(Image image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(path);

        (ushort width, ushort height) = image.Size;
        ReadOnlySpan<byte> pixels = image.Data;
        SDL_PixelFormat format = (SDL_PixelFormat)image.PixelFormat;
        int pitch = width * SDL3.SDL_BYTESPERPIXEL(format);
        // An indexed image would need a palette, which Image does not carry, and a FourCC image has planes the pitch does not describe.
        if (pitch == 0 || SDL3.SDL_ISPIXELFORMAT_INDEXED(format) || SDL3.SDL_ISPIXELFORMAT_FOURCC(format) || pixels.Length != (long)pitch * height)
        {
            throw new NotSupportedException($"Only tightly packed images in a non-planar, non-indexed pixel format can be saved, not {image.PixelFormat} with {pixels.Length} bytes for {width}x{height}.");
        }

        fixed (byte* pixelsPointer = pixels)
        {
            Pointer<SDL_Surface> surface = SDL3.SDL_CreateSurfaceFrom(width, height, format, (IntPtr)pixelsPointer, pitch);
            if (surface.IsNull)
            {
                throw new InvalidOperationException($"SDL_CreateSurfaceFrom failed: {SDL3.SDL_GetError()}");
            }

            try
            {
                if (!SDL3_image.IMG_SavePNG(surface, path))
                {
                    throw new IOException($"IMG_SavePNG failed for '{path}': {SDL3.SDL_GetError()}");
                }
            }
            finally
            {
                SDL3.SDL_DestroySurface(surface);
            }
        }
    }
}
