using System.Runtime.CompilerServices;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

public abstract class GpuVertexBuffer: IDisposable, IGpuMemorySized
{
    internal GpuDevice GpuDevice { get; set; }

    internal Pointer<SDL_GPUBuffer> SdlVertexBuffer { get; set; }
    public int BufferSize { get; }
    public int Size { get; internal set; }
    public long SizeInBytes { get; }

    private protected GpuVertexBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUBuffer> sdlVertexBuffer, int size, long sizeInBytes)
    {
        GpuDevice = gpuDevice;
        SdlVertexBuffer = sdlVertexBuffer;
        BufferSize = size;
        Size = size;
        SizeInBytes = sizeInBytes;
    }

    public void Dispose()
    {
        GpuDevice.ReleaseVertexBuffer(this);
    }
}

public class GpuVertexBuffer<TVertexType>: GpuVertexBuffer where TVertexType : unmanaged, IVertexType
{
    internal GpuVertexBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUBuffer> sdlVertexBuffer, int size)
        : base(gpuDevice, sdlVertexBuffer, size, (long)Unsafe.SizeOf<TVertexType>() * size)
    {
    }
}
