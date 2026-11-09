using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

public enum IndexElementSize
{
    UInt16,
    UInt32
}

public sealed class GpuIndexBuffer : IDisposable, IGpuMemorySized
{
    internal GpuDevice GpuDevice { get; }
    internal Pointer<SDL_GPUBuffer> SdlBuffer { get; set; }

    public int BufferSize { get; }
    public int Size { get; internal set; }
    public IndexElementSize ElementSize { get; }
    public long SizeInBytes { get; }

    internal GpuIndexBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUBuffer> sdlBuffer, int size, IndexElementSize elementSize)
    {
        GpuDevice = gpuDevice;
        SdlBuffer = sdlBuffer;
        BufferSize = size;
        Size = size;
        ElementSize = elementSize;
        SizeInBytes = (long)GetElementSizeInBytes(elementSize) * size;
    }

    public void Dispose()
    {
        GpuDevice.ReleaseIndexBuffer(this);
    }

    internal static int GetElementSizeInBytes(IndexElementSize elementSize)
    {
        return elementSize switch
        {
            IndexElementSize.UInt16 => sizeof(ushort),
            IndexElementSize.UInt32 => sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(nameof(elementSize), elementSize, null)
        };
    }
}
