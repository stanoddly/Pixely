using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Content;

internal class SdlImage : Image
{
    private readonly Pointer<SDL_Surface> _surface;

    internal unsafe SdlImage(Pointer<SDL_Surface> surface)
    {
        _surface = surface;
    }

    internal Pointer<SDL_Surface> Surface => _surface;

    public override unsafe ReadOnlySpan<byte> Data
    {
        get
        {
            SDL_Surface* surface = _surface;
            int byteCount = surface->pitch * surface->h;
            return new ReadOnlySpan<byte>((void*)surface->pixels, byteCount);
        }
    }

    public override unsafe ShortSize Size
    {
        get
        {
            SDL_Surface* surface = _surface;
            return new ShortSize((ushort)surface->w, (ushort)surface->h);
        }
    }

    public override unsafe PixelFormat PixelFormat
    {
        get
        {
            SDL_Surface* surface = _surface;
            return (PixelFormat)surface->format;
        }
    }

    public override unsafe void Dispose()
    {
        SDL3.SDL_DestroySurface(_surface);
    }
}

internal class SdlImageLoader : IImageLoader
{
    private readonly ContentSource _contentSource;
    private readonly SdlImageDecoder _decoder = new();

    public SdlImageLoader(ContentSource contentSource)
    {
        _contentSource = contentSource;
    }

    public Image Load(ReadOnlySpan<char> path)
    {
        using Stream fileStream = _contentSource.GetFile(path).Open();
        byte[] fileData = new byte[fileStream.Length];
        fileStream.ReadExactly(fileData);

        return _decoder.Decode(fileData);
    }
}

internal class SdlImageDecoder : IImageDecoder
{
    private const int BytesPerPixel = 4;

    public unsafe Image Decode(ReadOnlySpan<byte> encodedImage)
    {
        fixed (byte* encodedImagePtr = encodedImage)
        {
            Pointer<SDL_IOStream> sdlStream = SDL3.SDL_IOFromConstMem((IntPtr)encodedImagePtr, (UIntPtr)encodedImage.Length);
            if (sdlStream.IsNull)
            {
                throw new InvalidOperationException($"SDL_IOFromConstMem failed: {SDL3.SDL_GetError()}");
            }

            Pointer<SDL_Surface> surface = SdlBoolInterop.IMG_Load_IO(sdlStream, true);
            if (surface.IsNull)
            {
                throw new InvalidDataException($"IMG_Load_IO failed: {SDL3.SDL_GetError()}");
            }

            SdlImage image = ConvertToAbgr8888(surface);
            try
            {
                CheckLayout(image.Surface);
            }
            catch
            {
                image.Dispose();
                throw;
            }

            return image;
        }
    }

    // SdlImage reports its size as ShortSize and its data as pitch * height bytes, and textures are uploaded as width * 4 bytes
    // per row, so a surface that does not fit these is rejected here rather than truncated or misread later.
    private static unsafe void CheckLayout(Pointer<SDL_Surface> surface)
    {
        SDL_Surface* sdlSurface = surface;
        if (sdlSurface->w <= 0 || sdlSurface->h <= 0 || sdlSurface->w > ushort.MaxValue || sdlSurface->h > ushort.MaxValue)
        {
            throw new InvalidDataException($"The image is {sdlSurface->w}x{sdlSurface->h} pixels; each side must be between 1 and {ushort.MaxValue}.");
        }

        if (sdlSurface->pitch != checked(sdlSurface->w * BytesPerPixel))
        {
            throw new InvalidDataException($"The decoded image has a pitch of {sdlSurface->pitch} bytes for {sdlSurface->w} pixels per row; rows must be tightly packed.");
        }
    }

    private static unsafe SdlImage ConvertToAbgr8888(Pointer<SDL_Surface> surface)
    {
        SDL_Surface* sdlSurface = surface;
        var pixelFormat = (PixelFormat)sdlSurface->format;

        // Already in the right format - take ownership
        if (pixelFormat == PixelFormat.Abgr8888)
        {
            return new SdlImage(surface);
        }

        // Convert to ABGR8888 - on little-endian systems this gives us
        // [R, G, B, A] byte order in memory, which matches RGBA8888 semantics
        Pointer<SDL_Surface> convertedSurface = SDL3.SDL_ConvertSurface(surface, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888);
        SDL3.SDL_DestroySurface(surface);

        if (convertedSurface.IsNull)
        {
            throw new InvalidOperationException($"SDL_ConvertSurface failed for format {pixelFormat}: {SDL3.SDL_GetError()}");
        }

        return new SdlImage(convertedSurface);
    }
}
