using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class GpuMemorySystem: ICopyPass
{
    private readonly GpuDevice _gpuDevice;
    private readonly UploadTransferBuffer _uploadTransferBuffer;

    // Uploads are recorded from the update phase until the render phase submits them. The native command buffer is held
    // directly because the copy pass and the cancel on dispose are all this class's own.
    private Pointer<SDL_GPUCommandBuffer> _sdlCommandBuffer;
    private CopyPass? _copyPass;

    public GpuMemorySystem(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
        _uploadTransferBuffer = new UploadTransferBuffer(gpuDevice);
    }

    public bool IsEmpty => _copyPass == null || _copyPass.IsEmpty;

    private CopyPass GetOrCreateCopyPass()
    {
        if (_copyPass == null)
        {
            if (_sdlCommandBuffer.IsNull)
            {
                _sdlCommandBuffer = _gpuDevice.AcquireSdlCommandBuffer();
            }

            unsafe
            {
                Pointer<SDL_GPUCopyPass> sdlCopyPass = SDL3.SDL_BeginGPUCopyPass(_sdlCommandBuffer);
                // A failure keeps the command buffer, so the next upload begins the copy pass on it again.
                SdlError.ThrowOnNull(sdlCopyPass);
                _copyPass = new CopyPass(_gpuDevice, sdlCopyPass, _uploadTransferBuffer);
            }
        }

        return _copyPass;
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(ReadOnlySpan<TVertexType> vertices) where TVertexType : unmanaged, IVertexType
    {
        return GetOrCreateCopyPass().CreateVertexBuffer(vertices);
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(Shape<TVertexType> shape) where TVertexType : unmanaged, IVertexType
    {
        return GetOrCreateCopyPass().CreateVertexBuffer(shape);
    }

    public void UpdateVertexBuffer<TVertexType>(GpuVertexBuffer<TVertexType> vertexBuffer, ReadOnlySpan<TVertexType> vertices) where TVertexType : unmanaged, IVertexType
    {
        GetOrCreateCopyPass().UpdateVertexBuffer(vertexBuffer, vertices);
    }

    public GpuIndexBuffer CreateIndexBuffer(ReadOnlySpan<ushort> indices)
    {
        return GetOrCreateCopyPass().CreateIndexBuffer(indices);
    }

    public GpuIndexBuffer CreateIndexBuffer(ReadOnlySpan<uint> indices)
    {
        return GetOrCreateCopyPass().CreateIndexBuffer(indices);
    }

    public void UpdateIndexBuffer(GpuIndexBuffer indexBuffer, ReadOnlySpan<ushort> indices)
    {
        GetOrCreateCopyPass().UpdateIndexBuffer(indexBuffer, indices);
    }

    public void UpdateIndexBuffer(GpuIndexBuffer indexBuffer, ReadOnlySpan<uint> indices)
    {
        GetOrCreateCopyPass().UpdateIndexBuffer(indexBuffer, indices);
    }

    public GpuStorageBuffer<T> CreateStorageBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        return GetOrCreateCopyPass().CreateStorageBuffer(data);
    }

    public void UpdateStorageBuffer<T>(GpuStorageBuffer<T> storageBuffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        GetOrCreateCopyPass().UpdateStorageBuffer(storageBuffer, data);
    }

    public Texture CreateTexture(Image image)
    {
        return GetOrCreateCopyPass().CreateTexture(image);
    }

    public TextureArray CreateTextureArray(ReadOnlySpan<Image> images)
    {
        return GetOrCreateCopyPass().CreateTextureArray(images);
    }

    // Nothing consumes uploads once the app tears down and the device is destroyed next, so a pending command buffer is cancelled
    // rather than submitted; submitting would make the device destruction wait for work nobody reads.
    public void Dispose()
    {
        if (!_sdlCommandBuffer.IsNull)
        {
            _copyPass?.End();
            _copyPass = null;
            unsafe
            {
                SDL3.SDL_CancelGPUCommandBuffer(_sdlCommandBuffer);
            }
            _sdlCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        }

        _uploadTransferBuffer.Dispose();
    }

    public void Submit()
    {
        if (_sdlCommandBuffer.IsNull)
        {
            return;
        }

        unsafe
        {
            SdlError.ThrowOnFalse(SDL3.SDL_SubmitGPUCommandBuffer(EndCommandBuffer()), "SDL_SubmitGPUCommandBuffer");
        }
    }

    // For a frame in which no submission acquires a swapchain texture, such as one whose windows are all minimized. SDL's Vulkan
    // backend frees finished command buffers only on a submit that acquired a swapchain texture, on a fence wait, or when no
    // window is claimed. Until then those command buffers keep the buffers they upload into in use, so every later update of
    // such a buffer cycles it into a new full-size copy, and SDL keeps each copy until the buffer is released. Waiting on a
    // fence makes SDL free them all: the device has one queue, so the fence also covers every earlier submission. The wait
    // blocks until the GPU finishes that work, so it runs only when uploads are pending.
    internal void SubmitAndReleaseFinishedWork()
    {
        if (_sdlCommandBuffer.IsNull)
        {
            return;
        }

#if BROWSER
        // The WebGPU fork checks its fences on every submit, and waiting on a fence is not supported in the browser.
        Submit();
#else
        unsafe
        {
            Pointer<SDL_GPUFence> sdlFence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(EndCommandBuffer());
            SdlError.ThrowOnNull(sdlFence);
            using (GpuFence fence = new GpuFence(_gpuDevice, sdlFence))
            {
                _gpuDevice.WaitForFences([fence]);
            }
        }
#endif
    }

    private Pointer<SDL_GPUCommandBuffer> EndCommandBuffer()
    {
        _copyPass?.End();
        _copyPass = null;
        Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer = _sdlCommandBuffer;
        _sdlCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        return sdlCommandBuffer;
    }
}
