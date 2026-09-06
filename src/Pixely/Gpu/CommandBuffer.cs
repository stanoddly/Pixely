using System.Runtime.CompilerServices;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class CommandBuffer: IDisposable
{
    private readonly GpuDevice _gpuDevice;
    private Pointer<SDL_GPUCommandBuffer> _sdlGpuCommandBuffer;
    private ShaderUniformSlotSizes _fragmentShaderUniformSlotSizes;
    private ShaderUniformSlotSizes _vertexShaderUniformSlotSizes;

    internal Pointer<SDL_GPUCommandBuffer> SdlGpuCommandBuffer
    {
        get => _sdlGpuCommandBuffer;
        private set => _sdlGpuCommandBuffer = value;
    }

    public ShaderUniformSlotSizes FragmentShaderUniformSlotSizes => _fragmentShaderUniformSlotSizes;
    public ShaderUniformSlotSizes VertexShaderUniformSlotSizes => _vertexShaderUniformSlotSizes;

    internal CommandBuffer(GpuDevice gpuDevice, Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer)
    {
        _gpuDevice = gpuDevice;
        SdlGpuCommandBuffer = sdlCommandBuffer;
    }

    public void Submit()
    {
        ThrowIfDisposed();
        unsafe
        {
            // TODO: error handling
            SDL3.SDL_SubmitGPUCommandBuffer(SdlGpuCommandBuffer);
            SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        }
    }

    public GpuFence SubmitAndAcquireFence()
    {
        ThrowIfDisposed();
        unsafe
        {
            SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(SdlGpuCommandBuffer);
            SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;

            if (fence == null)
            {
                throw new PixelyException($"SDL_SubmitGPUCommandBufferAndAcquireFence failed: {SDL3.SDL_GetError()}");
            }

            return new GpuFence(_gpuDevice, fence);
        }
    }
    
    public void PushFragmentUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        ThrowIfDisposed();
        
        AssignSlot(ref _fragmentShaderUniformSlotSizes, slot, Unsafe.SizeOf<TType>());
        
        unsafe
        {
            IntPtr data = new IntPtr(Unsafe.AsPointer(ref variable));
            uint size = (uint)Unsafe.SizeOf<TType>();
            SDL3.SDL_PushGPUFragmentUniformData(SdlGpuCommandBuffer, slot, data, size);
        }
    }
    
    public void PushVertexUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        ThrowIfDisposed();

        AssignSlot(ref _vertexShaderUniformSlotSizes, slot, Unsafe.SizeOf<TType>());
        
        unsafe
        {
            IntPtr data = new IntPtr(Unsafe.AsPointer(ref variable));
            uint size = (uint)Unsafe.SizeOf<TType>();
            SDL3.SDL_PushGPUVertexUniformData(SdlGpuCommandBuffer, slot, data, size);
        }
    }

    public IRenderPass CreateRenderPass(List<Texture> colorTargets, List<ColorTargetSettings> colorTargetSettings, Texture? depthBuffer, DepthBufferSettings depthBufferSettings)
    {
        ThrowIfDisposed();
        
        Span<SDL_GPUColorTargetInfo> colorTargetInfos = stackalloc SDL_GPUColorTargetInfo[colorTargets.Count];
            
        for (int i = 0; i < colorTargets.Count; i++)
        {
            Texture colorTarget = colorTargets[i];
            ColorTargetSettings colorTargetSetting = colorTargetSettings[i];

            colorTargetInfos[i] = new SDL_GPUColorTargetInfo
            {
                texture = colorTarget.SdlGpuTexture,
                clear_color = colorTargetSetting.ClearColorValue,
                load_op = (SDL_GPULoadOp)colorTargetSetting.LoadOperation,
                store_op = (SDL_GPUStoreOp)colorTargetSetting.StoreOperation
            };
        }
        
        Pointer<SDL_GPUTexture> depthBufferPointer = Pointer<SDL_GPUTexture>.Null;

        if (depthBuffer != null)
        {
            depthBufferPointer = depthBuffer.SdlGpuTexture;
        }
        
        DepthBufferFormat depthBufferFormat = depthBuffer != null
            ? (DepthBufferFormat)depthBuffer.Format
            : DepthBufferFormat.None;

        return CreateMultipleRenderTargetsPassInternal(
            colorTargetInfos,
            depthBufferPointer,
            depthBufferSettings,
            depthBufferFormat,
            CalculateTargetSize(colorTargets, depthBuffer));
    }

    // A pass can only safely address the area every attachment shares, so the scissor bounds
    // are the smallest attachment, depth included.
    private static ShortSize CalculateTargetSize(List<Texture> colorTargets, Texture? depthBuffer)
    {
        ushort width = ushort.MaxValue;
        ushort height = ushort.MaxValue;

        foreach (Texture colorTarget in colorTargets)
        {
            width = Math.Min(width, colorTarget.Size.Width);
            height = Math.Min(height, colorTarget.Size.Height);
        }

        if (depthBuffer != null)
        {
            width = Math.Min(width, depthBuffer.Size.Width);
            height = Math.Min(height, depthBuffer.Size.Height);
        }

        return new ShortSize(width, height);
    }

    private IRenderPass CreateMultipleRenderTargetsPassInternal(
        ReadOnlySpan<SDL_GPUColorTargetInfo> colorTargetInfos,
        Pointer<SDL_GPUTexture> depthBufferPointer,
        DepthBufferSettings depthBufferSettings,
        DepthBufferFormat depthBufferFormat,
        ShortSize targetSize)
    {
        ThrowIfDisposed();
        
        unsafe
        {
            SDL_GPURenderPass* gpuRenderPass;
            fixed (SDL_GPUColorTargetInfo* colorTargetInfosPtr = colorTargetInfos)
            {
                if (depthBufferPointer.IsNull)
                {
                    gpuRenderPass = SDL3.SDL_BeginGPURenderPass(
                        SdlGpuCommandBuffer,
                        colorTargetInfosPtr,
                        (uint)colorTargetInfos.Length,
                        null);
                }
                else
                {
                    SDL_GPUDepthStencilTargetInfo depthStencilTargetInfo = new SDL_GPUDepthStencilTargetInfo
                    {
                        texture = depthBufferPointer,
                        clear_depth = depthBufferSettings.ClearDepthValue,
                        load_op = (SDL_GPULoadOp)depthBufferSettings.DepthBufferLoadOperation,
                        store_op = (SDL_GPUStoreOp)depthBufferSettings.DepthBufferStoreOperation,
                        stencil_load_op = (SDL_GPULoadOp)depthBufferSettings.StencilLoadOperation,
                        stencil_store_op = (SDL_GPUStoreOp)depthBufferSettings.StencilStoreOperation,
                        clear_stencil = depthBufferSettings.ClearStencilValue
                    };
                    
                    gpuRenderPass = SDL3.SDL_BeginGPURenderPass(
                        SdlGpuCommandBuffer,
                        colorTargetInfosPtr,
                        (uint)colorTargetInfos.Length,
                        &depthStencilTargetInfo);
                }
            }
            
            RenderPass renderPass = new RenderPass(this, gpuRenderPass, depthBufferFormat, targetSize);

            return renderPass;
        }
    }

    public void PushComputeUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        ThrowIfDisposed();
        unsafe
        {
            IntPtr data = new IntPtr(Unsafe.AsPointer(ref variable));
            uint size = (uint)Unsafe.SizeOf<TType>();
            SDL3.SDL_PushGPUComputeUniformData(SdlGpuCommandBuffer, slot, data, size);
        }
    }

    public IComputePass CreateComputePass(
        ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers)
    {
        ThrowIfDisposed();
        unsafe
        {
            SDL_GPUStorageTextureReadWriteBinding* textureBindings = stackalloc SDL_GPUStorageTextureReadWriteBinding[readWriteStorageTextures.Length];
            for (int i = 0; i < readWriteStorageTextures.Length; i++)
            {
                textureBindings[i] = new SDL_GPUStorageTextureReadWriteBinding
                {
                    texture = readWriteStorageTextures[i].Texture.SdlGpuTexture,
                    mip_level = readWriteStorageTextures[i].MipLevel,
                    layer = readWriteStorageTextures[i].Layer,
                    cycle = readWriteStorageTextures[i].Cycle
                };
            }

            SDL_GPUStorageBufferReadWriteBinding* bufferBindings = stackalloc SDL_GPUStorageBufferReadWriteBinding[readWriteStorageBuffers.Length];
            for (int i = 0; i < readWriteStorageBuffers.Length; i++)
            {
                bufferBindings[i] = new SDL_GPUStorageBufferReadWriteBinding
                {
                    buffer = readWriteStorageBuffers[i].Buffer.SdlBuffer,
                    cycle = readWriteStorageBuffers[i].Cycle
                };
            }

            SDL_GPUComputePass* computePass = SDL3.SDL_BeginGPUComputePass(
                SdlGpuCommandBuffer,
                textureBindings,
                (uint)readWriteStorageTextures.Length,
                bufferBindings,
                (uint)readWriteStorageBuffers.Length);

            StorageBufferElementSizes rwElementSizes = BuildStorageBufferElementSizes(readWriteStorageBuffers);
            return new ComputePass(computePass, (uint)readWriteStorageTextures.Length, (uint)readWriteStorageBuffers.Length, rwElementSizes);
        }
    }

    public IComputePass CreateComputePass()
    {
        return CreateComputePass(
            ReadOnlySpan<StorageTextureReadWriteBinding>.Empty,
            ReadOnlySpan<StorageBufferReadWriteBinding>.Empty);
    }

    private static StorageBufferElementSizes BuildStorageBufferElementSizes(ReadOnlySpan<StorageBufferReadWriteBinding> buffers)
    {
        StorageBufferElementSizes sizes = default;
        for (int i = 0; i < buffers.Length && i < 4; i++)
        {
            ushort elementSize = (ushort)buffers[i].Buffer.ElementSize;
            sizes = i switch
            {
                0 => sizes with { Slot0 = elementSize },
                1 => sizes with { Slot1 = elementSize },
                2 => sizes with { Slot2 = elementSize },
                3 => sizes with { Slot3 = elementSize },
                _ => sizes
            };
        }
        return sizes;
    }

    private void AssignSlot(ref ShaderUniformSlotSizes slotSizes, uint slot, int size)
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
        ThrowIfDisposed();
        
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

            SDL3.SDL_BlitGPUTexture(SdlGpuCommandBuffer, &blitInfo);
        }
    }

    public void Cancel()
    {
        if (!SdlGpuCommandBuffer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_CancelGPUCommandBuffer(SdlGpuCommandBuffer);
            }
            SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        }
    }

    public void Dispose()
    {
        Cancel();
    }

    private void ThrowIfDisposed()
    {
        if (SdlGpuCommandBuffer.IsNull)
        {
            throw new ObjectDisposedException(nameof(CommandBuffer));
        }
    }

    public ICopyPass CreateCopyPass()
    {
        unsafe
        {
            SDL_GPUCopyPass* copyPass = SDL3.SDL_BeginGPUCopyPass(SdlGpuCommandBuffer);
            return new CopyPass(_gpuDevice, copyPass);
        }
    }
}
