using System.Numerics;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public abstract class Texture: IDisposable, IGpuMemorySized
{
    internal Pointer<SDL_GPUTexture> SdlGpuTexture { get; set; }
    internal bool IsDisposed => SdlGpuTexture.IsNull;
    public TextureFormat Format { get; }
    public ShortSize Size { get; }
    public long SizeInBytes { get; }

    internal Texture(Pointer<SDL_GPUTexture> sdlGpuTexture, ShortSize size, TextureFormat format, long sizeInBytes)
    {
        SdlGpuTexture = sdlGpuTexture;
        Size = size;
        Format = format;
        SizeInBytes = sizeInBytes;
    }

    public Vector4 CalculateTextureRegionUVs(ShortRectangle sourceRectangle, SpriteFlip flip = SpriteFlip.None)
    {
        float left = sourceRectangle.X;
        float top = sourceRectangle.Y;
        float right = sourceRectangle.X + sourceRectangle.Width;
        float bottom = sourceRectangle.Y + sourceRectangle.Height;

        (ushort width, ushort height) = Size;

        float u0 = left / width;
        float v0 = top / height;
        float u1 = right / width;
        float v1 = bottom / height;

        if ((flip & SpriteFlip.Horizontal) != 0)
        {
            (u0, u1) = (u1, u0);
        }

        if ((flip & SpriteFlip.Vertical) != 0)
        {
            (v0, v1) = (v1, v0);
        }

        return new Vector4(u0, v0, u1, v1);
    }

    internal void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(Texture));
        }
    }

    public abstract void Dispose();
}

public class UserTexture: Texture
{
    private readonly GpuDevice _gpuDevice;

    internal UserTexture(GpuDevice gpuDevice, Pointer<SDL_GPUTexture> sdlGpuTexture, ShortSize size, TextureFormat format)
        : base(sdlGpuTexture, size, format, format.CalculateSizeInBytes(size.Width, size.Height))
    {
        _gpuDevice = gpuDevice;
    }

    public override void Dispose()
    {
        _gpuDevice.ReleaseTexture(this);
    }
}

// Aliases the backing texture's native handle without taking ownership of it.
internal sealed class BorrowedTexture : Texture
{
    internal BorrowedTexture(Texture backingTexture) : base(backingTexture.SdlGpuTexture, backingTexture.Size, backingTexture.Format, backingTexture.SizeInBytes)
    {
    }

    // A borrowed handle never owns or invalidates the shared native texture.
    public override void Dispose() { }

    internal void Invalidate() => SdlGpuTexture = Pointer<SDL_GPUTexture>.Null;
}

public class SwapchainTexture : Texture
{
    internal SwapchainTexture(Pointer<SDL_GPUTexture> sdlGpuTexture, ShortSize size, TextureFormat format)
        : base(sdlGpuTexture, size, format, format.CalculateSizeInBytes(size.Width, size.Height))
    {
    }

    public override void Dispose()
    {
    }
}
