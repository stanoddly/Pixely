using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using Pixely.Content;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Records GPU work for one submission. It is a <c>ref struct</c> that holds its own state, so pass it by <c>ref</c>: a copy
/// records on the same native command buffer but keeps its own state, and submitting both submits the native buffer twice.
/// </summary>
public ref struct CommandBuffer : IDisposable
{
    private CommandBufferState _state;

    internal CommandBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer)
    {
        _state.GpuDevice = gpuDevice;
        _state.SdlGpuCommandBuffer = sdlCommandBuffer;
    }

    [UnscopedRef]
    internal ref CommandBufferState State => ref _state;

    internal readonly Pointer<SDL_GPUCommandBuffer> SdlGpuCommandBuffer => _state.SdlGpuCommandBuffer;

    public readonly ShaderUniformSlotSizes FragmentShaderUniformSlotSizes => _state.FragmentShaderUniformSlotSizes;
    public readonly ShaderUniformSlotSizes VertexShaderUniformSlotSizes => _state.VertexShaderUniformSlotSizes;

    public void Submit()
    {
        _state.ThrowIfDisposed();
        _state.ThrowIfPassOpen();
        unsafe
        {
            // TODO: error handling
            SDL3.SDL_SubmitGPUCommandBuffer(_state.SdlGpuCommandBuffer);
            _state.SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        }
    }

    /// <summary>
    /// Submits the recorded work, waits for the GPU to finish it and returns the pixels of the texture's first layer, tightly packed.
    /// Not in the browser: mapping a download buffer and waiting for its fence both suspend the wasm stack under a managed frame.
    /// </summary>
    [UnsupportedOSPlatform("browser")]
    public Image SubmitAndDownloadTexture(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _state.ThrowIfDisposed();
        _state.ThrowIfPassOpen();
        texture.ThrowIfDisposed();
#if BROWSER
        throw new PlatformNotSupportedException("Downloading a texture is not supported in the browser.");
#else

        PixelFormat pixelFormat = texture.Format.ToPixelFormat();
        long layerSizeInBytes = texture.Format.CalculateSizeInBytes(texture.Size.Width, texture.Size.Height);
        if (layerSizeInBytes > int.MaxValue)
        {
            throw new NotSupportedException($"A {texture.Size.Width}x{texture.Size.Height} {texture.Format} layer is too large to download into one array.");
        }

        uint sizeInBytes = (uint)layerSizeInBytes;
        byte[] pixels = new byte[sizeInBytes];
        GpuDevice gpuDevice = _state.GpuDevice;

        unsafe
        {
            SDL_GPUTransferBufferCreateInfo transferBufferCreateInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD,
                size = sizeInBytes
            };
            SDL_GPUTransferBuffer* transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(gpuDevice.SdlGpuDevice, &transferBufferCreateInfo);
            SdlError.ThrowOnNull(transferBuffer);

            try
            {
                SDL_GPUTextureRegion source = new SDL_GPUTextureRegion { texture = texture.SdlGpuTexture, w = texture.Size.Width, h = texture.Size.Height, d = 1 };
                SDL_GPUTextureTransferInfo destination = new SDL_GPUTextureTransferInfo { transfer_buffer = transferBuffer };

                SDL_GPUCopyPass* copyPass = SDL3.SDL_BeginGPUCopyPass(_state.SdlGpuCommandBuffer);
                SDL3.SDL_DownloadFromGPUTexture(copyPass, &source, &destination);
                SDL3.SDL_EndGPUCopyPass(copyPass);

                using (GpuFence fence = SubmitAndAcquireFence())
                {
                    gpuDevice.WaitForFences([fence]);
                }

                byte* mapped = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(gpuDevice.SdlGpuDevice, transferBuffer, false);
                SdlError.ThrowOnNull(mapped);
                new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
                SDL3.SDL_UnmapGPUTransferBuffer(gpuDevice.SdlGpuDevice, transferBuffer);
            }
            finally
            {
                SDL3.SDL_ReleaseGPUTransferBuffer(gpuDevice.SdlGpuDevice, transferBuffer);
            }
        }

        return new RawImage(pixels, texture.Size, pixelFormat);
#endif
    }

    public GpuFence SubmitAndAcquireFence()
    {
        _state.ThrowIfDisposed();
        _state.ThrowIfPassOpen();
        unsafe
        {
            SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(_state.SdlGpuCommandBuffer);
            _state.SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;

            if (fence == null)
            {
                throw new PixelyException($"SDL_SubmitGPUCommandBufferAndAcquireFence failed: {SDL3.SDL_GetError()}");
            }

            return new GpuFence(_state.GpuDevice, fence);
        }
    }
    
    public void PushFragmentUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        _state.ThrowIfDisposed();

        AssignSlot(ref _state.FragmentShaderUniformSlotSizes, slot, Unsafe.SizeOf<TType>());

        unsafe
        {
            IntPtr data = new IntPtr(Unsafe.AsPointer(ref variable));
            uint size = (uint)Unsafe.SizeOf<TType>();
            SDL3.SDL_PushGPUFragmentUniformData(_state.SdlGpuCommandBuffer, slot, data, size);
        }
    }
    
    public void PushVertexUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        _state.ThrowIfDisposed();

        AssignSlot(ref _state.VertexShaderUniformSlotSizes, slot, Unsafe.SizeOf<TType>());

        unsafe
        {
            IntPtr data = new IntPtr(Unsafe.AsPointer(ref variable));
            uint size = (uint)Unsafe.SizeOf<TType>();
            SDL3.SDL_PushGPUVertexUniformData(_state.SdlGpuCommandBuffer, slot, data, size);
        }
    }

    [UnscopedRef]
    public RenderPass CreateRenderPass(scoped ReadOnlySpan<Texture> colorTargets, scoped ReadOnlySpan<ColorTargetSettings> colorTargetSettings, Texture? depthBuffer, DepthBufferSettings depthBufferSettings)
    {
        return RenderPass.Begin(ref _state, colorTargets, colorTargetSettings, depthBuffer, depthBufferSettings);
    }

    public void PushComputeUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        _state.ThrowIfDisposed();
        unsafe
        {
            IntPtr data = new IntPtr(Unsafe.AsPointer(ref variable));
            uint size = (uint)Unsafe.SizeOf<TType>();
            SDL3.SDL_PushGPUComputeUniformData(_state.SdlGpuCommandBuffer, slot, data, size);
        }
    }

    [UnscopedRef]
    public ComputePass CreateComputePass(
        scoped ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        scoped ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers)
    {
        return ComputePass.Begin(ref _state, readWriteStorageTextures, readWriteStorageBuffers);
    }

    [UnscopedRef]
    public ComputePass CreateComputePass()
    {
        return CreateComputePass(
            ReadOnlySpan<StorageTextureReadWriteBinding>.Empty,
            ReadOnlySpan<StorageBufferReadWriteBinding>.Empty);
    }

    private static void AssignSlot(ref ShaderUniformSlotSizes slotSizes, uint slot, int size)
    {
        if (slot > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot must be between 0 and 3.");
        }

        byte byteSize = (byte)size;

        if (slot == 0)
        {
            slotSizes.Slot0 = byteSize;
        }
        
        if (slot == 1)
        {
            slotSizes.Slot1 = byteSize;
        }
        
        if (slot == 2)
        {
            slotSizes.Slot2 = byteSize;
        }
        
        if (slot == 3)
        {
            slotSizes.Slot3 = byteSize;
        }
    }

    public void BlitTextures(Texture source, Texture destination)
    {
        _state.ThrowIfDisposed();
        _state.ThrowIfPassOpen();

        unsafe
        {
            SDL_GPUBlitRegion sourceRegion = new SDL_GPUBlitRegion
            {
                texture = source.SdlGpuTexture,
                x = 0,
                y = 0,
                w = source.Size.Width,
                h = source.Size.Height
            };

            SDL_GPUBlitRegion destinationRegion = new SDL_GPUBlitRegion
            {
                texture = destination.SdlGpuTexture,
                x = 0,
                y = 0,
                w = destination.Size.Width,
                h = destination.Size.Height
            };

            SDL_GPUBlitInfo blitInfo = new SDL_GPUBlitInfo
            {
                source = sourceRegion,
                destination = destinationRegion,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                flip_mode = SDL_FlipMode.SDL_FLIP_NONE,
                filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            };

            SDL3.SDL_BlitGPUTexture(_state.SdlGpuCommandBuffer, &blitInfo);
        }
    }

    public void Cancel()
    {
        if (!_state.SdlGpuCommandBuffer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_CancelGPUCommandBuffer(_state.SdlGpuCommandBuffer);
            }
            _state.SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
            _state.ClosePass();
        }
    }

    public void Dispose()
    {
        Cancel();
    }
}
