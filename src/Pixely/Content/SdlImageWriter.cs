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
        int pitch = pixels.Length / height;

        fixed (byte* pixelsPointer = pixels)
        {
            Pointer<SDL_Surface> surface = SDL3.SDL_CreateSurfaceFrom(width, height, (SDL_PixelFormat)image.PixelFormat, (IntPtr)pixelsPointer, pitch);
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
