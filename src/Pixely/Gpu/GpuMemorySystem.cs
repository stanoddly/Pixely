using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class GpuMemorySystem: ICopyPass
{
    private readonly GpuDevice _gpuDevice;
    private readonly UploadRing _uploadRing;
    private readonly CopyPass _copyPass;

    // Uploads are recorded from the update phase until the render phase submits them, so the command buffer outlives any
    // CommandBuffer value: a ref struct cannot be held in a field.
    private Pointer<SDL_GPUCommandBuffer> _sdlCommandBuffer;

    public GpuMemorySystem(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
        _uploadRing = new UploadRing(gpuDevice);
        _copyPass = new CopyPass(gpuDevice, _uploadRing);
    }

    public bool IsEmpty => _sdlCommandBuffer.IsNull || _copyPass.IsEmpty;

    private CopyPass GetOrCreateCopyPass()
    {
        if (_sdlCommandBuffer.IsNull)
        {
            _sdlCommandBuffer = _gpuDevice.AcquireSdlCommandBuffer();

            unsafe
            {
                Pointer<SDL_GPUCopyPass> sdlCopyPass = SDL3.SDL_BeginGPUCopyPass(_sdlCommandBuffer);
                SdlError.ThrowOnNull(sdlCopyPass);
                _copyPass.Begin(sdlCopyPass);
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
            _copyPass.End();
            unsafe
            {
                SDL3.SDL_CancelGPUCommandBuffer(_sdlCommandBuffer);
            }
            _sdlCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
            _uploadRing.CancelSubmission();
        }

        _uploadRing.Dispose();
    }

    public void Submit()
    {
        if (_sdlCommandBuffer.IsNull)
        {
            return;
        }

        _copyPass.End();
        Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer = _sdlCommandBuffer;
        _sdlCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;

        unsafe
        {
            if (!_uploadRing.NeedsFence)
            {
                bool submitted = SDL3.SDL_SubmitGPUCommandBuffer(sdlCommandBuffer);
                _uploadRing.EndSubmission(Pointer<SDL_GPUFence>.Null);
                SdlError.ThrowOnFalse(submitted, "SDL_SubmitGPUCommandBuffer");
                return;
            }

            Pointer<SDL_GPUFence> fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(sdlCommandBuffer);
            _uploadRing.EndSubmission(fence);

            if (fence.IsNull)
            {
                throw new PixelyException($"SDL_SubmitGPUCommandBufferAndAcquireFence failed: {SDL3.SDL_GetError()}");
            }
        }
    }
}
