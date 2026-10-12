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
        int pitch = ImageSurfaces.GetPackedPitch(image);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
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
