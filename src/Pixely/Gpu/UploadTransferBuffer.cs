using System.Numerics;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// The transfer buffer that buffer uploads share across submissions. It lives as long as its <see cref="GpuMemorySystem"/>,
/// so SDL can cycle it once per submission instead of Pixely creating a transfer buffer per update.
/// </summary>
internal sealed class UploadTransferBuffer : IDisposable
{
    // The most the buffer grows to. A larger upload gets a transfer buffer of its own.
    internal const uint MaxCapacity = 1 << 20;

    private const uint MinCapacity = 64 << 10;

    private readonly GpuDevice _gpuDevice;

    internal UploadTransferBuffer(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    internal Pointer<SDL_GPUTransferBuffer> SdlTransferBuffer { get; private set; }

    internal uint Capacity { get; private set; }

    internal void Grow(uint size)
    {
        uint capacity = Math.Min(MaxCapacity, Math.Max(Math.Max(MinCapacity, Capacity * 2), BitOperations.RoundUpToPowerOf2(size)));

        // Uploads already recorded from the old buffer still read it, which SDL allows: a released buffer is freed only once
        // the GPU is done with it. The new buffer is created first, so a failed create leaves the old one in place.
        Pointer<SDL_GPUTransferBuffer> transferBuffer = _gpuDevice.CreateUploadTransferBuffer(capacity);
        _gpuDevice.ReleaseTransferBuffer(SdlTransferBuffer);
        SdlTransferBuffer = transferBuffer;
        Capacity = capacity;
    }

    public void Dispose()
    {
        _gpuDevice.ReleaseTransferBuffer(SdlTransferBuffer);
        SdlTransferBuffer = Pointer<SDL_GPUTransferBuffer>.Null;
        Capacity = 0;
    }
}
