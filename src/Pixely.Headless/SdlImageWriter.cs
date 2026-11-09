using Pixely.Content;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Headless;

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
            Pointer<SDL_Surface> surface = SDL_CreateSurfaceFrom(width, height, format, (IntPtr)pixelsPointer, pitch);
            if (surface.IsNull)
            {
                throw new InvalidOperationException($"SDL_CreateSurfaceFrom failed: {SdlError.GetError()}");
            }

            try
            {
                using NullTerminatedUtf8 utf8Path = new NullTerminatedUtf8(path);
                if (!IMG_SavePNG(surface, utf8Path.Span))
                {
                    throw new IOException($"IMG_SavePNG failed for '{path}': {SdlError.GetError()}");
                }
            }
            finally
            {
                SDL_DestroySurface(surface);
            }
        }
    }
}
