using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class GpuFence : IDisposable
{
    private readonly GpuDevice _gpuDevice;
    internal Pointer<SDL_GPUFence> Pointer { get; private set; }

    internal GpuFence(GpuDevice gpuDevice, Pointer<SDL_GPUFence> pointer)
    {
        _gpuDevice = gpuDevice;
        Pointer = pointer;
    }

    public bool IsSignaled()
    {
        ThrowIfDisposed();
        return _gpuDevice.QueryFenceCore(Pointer);
    }

    public void Dispose()
    {
        if (!Pointer.IsNull)
        {
            _gpuDevice.ReleaseFenceCore(Pointer);
            Pointer = Pointer<SDL_GPUFence>.Null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Pointer.IsNull)
        {
            throw new ObjectDisposedException(nameof(GpuFence));
        }
    }
}
