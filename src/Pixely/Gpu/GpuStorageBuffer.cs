using System.Runtime.CompilerServices;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public abstract class GpuStorageBuffer : IDisposable, IGpuMemorySized
{
    internal GpuDevice GpuDevice { get; set; }
    internal Pointer<SDL_GPUBuffer> SdlBuffer { get; set; }

    public int BufferSize { get; }
    public int Size { get; internal set; }
    public long SizeInBytes { get; }
    public abstract int ElementSize { get; }
    public StorageBufferUsage Usage { get; }

    protected GpuStorageBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUBuffer> sdlBuffer, int size, long sizeInBytes, StorageBufferUsage usage)
    {
        GpuDevice = gpuDevice;
        SdlBuffer = sdlBuffer;
        BufferSize = size;
        Size = size;
        SizeInBytes = sizeInBytes;
        Usage = usage;
    }

    public abstract void Dispose();

    // SDL does not check a storage buffer's usage when it is bound, not even in debug mode.
    internal void ValidateUsage(string binding, uint slot, StorageBufferUsage requiredUsage)
    {
        if ((Usage & requiredUsage) == 0)
        {
            throw new InvalidOperationException($"{binding} slot {slot} is bound to a storage buffer with the usage '{Usage}', but it needs {requiredUsage}.");
        }
    }
}

public class GpuStorageBuffer<T> : GpuStorageBuffer where T : unmanaged
{
    public override int ElementSize => Unsafe.SizeOf<T>();

    internal GpuStorageBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUBuffer> sdlBuffer, int size, StorageBufferUsage usage)
        : base(gpuDevice, sdlBuffer, size, Unsafe.SizeOf<T>() * size, usage)
    {
    }

    // SDL rejects a buffer under 4 bytes in debug mode, and D3D12 views a storage buffer as 4-byte elements, so a smaller one
    // has none. The upper limit keeps SizeInBytes, which the constructor computes as an int, from overflowing.
    internal static uint ValidateSizeInBytes(int length, string paramName)
    {
        long sizeInBytes = (long)Unsafe.SizeOf<T>() * length;
        if (sizeInBytes < 4 || sizeInBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(paramName, length,
                $"A storage buffer needs 4 to {int.MaxValue} bytes, but {length} elements of {Unsafe.SizeOf<T>()} bytes are {sizeInBytes} bytes.");
        }

        return (uint)sizeInBytes;
    }

    public override void Dispose()
    {
        GpuDevice.ReleaseStorageBuffer((GpuStorageBuffer)this);
    }
}
