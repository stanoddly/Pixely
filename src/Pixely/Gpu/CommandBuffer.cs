using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using Pixely.Content;
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
        // Once the submit reaches SDL's backend, SDL invalidates the command buffer even when it fails, so it cannot be retried
        // or cancelled. Only SDL's debug check for a pass still in progress fails earlier and leaves it valid, which SDL also
        // asserts on as a programming error.
        Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer = SdlGpuCommandBuffer;
        SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        unsafe
        {
            SdlError.ThrowOnFalse(SDL3.SDL_SubmitGPUCommandBuffer(sdlCommandBuffer), "SDL_SubmitGPUCommandBuffer");
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
        ThrowIfDisposed();
        texture.ThrowIfDisposed();
#if BROWSER
        throw new PlatformNotSupportedException("Downloading a texture is not supported in the browser.");
#else
        if (texture.SampleCount != SampleCount.Count1)
        {
            throw new ArgumentException($"A texture with {texture.SampleCount} cannot be downloaded. Resolve it into a texture with one sample and download that.", nameof(texture));
        }

        PixelFormat pixelFormat = texture.Format.ToPixelFormat();
        long layerSizeInBytes = texture.Format.CalculateSizeInBytes(texture.Size.Width, texture.Size.Height);
        if (layerSizeInBytes > int.MaxValue)
        {
            throw new NotSupportedException($"A {texture.Size.Width}x{texture.Size.Height} {texture.Format} layer is too large to download into one array.");
        }

        uint sizeInBytes = (uint)layerSizeInBytes;
        byte[] pixels = new byte[sizeInBytes];

        unsafe
        {
            SDL_GPUTransferBufferCreateInfo transferBufferCreateInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD,
                size = sizeInBytes
            };
            SDL_GPUTransferBuffer* transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(_gpuDevice.SdlGpuDevice, &transferBufferCreateInfo);
            SdlError.ThrowOnNull(transferBuffer);

            try
            {
                SDL_GPUTextureRegion source = new SDL_GPUTextureRegion { texture = texture.SdlGpuTexture, w = texture.Size.Width, h = texture.Size.Height, d = 1 };
                SDL_GPUTextureTransferInfo destination = new SDL_GPUTextureTransferInfo { transfer_buffer = transferBuffer };

                SDL_GPUCopyPass* copyPass = SDL3.SDL_BeginGPUCopyPass(SdlGpuCommandBuffer);
                SDL3.SDL_DownloadFromGPUTexture(copyPass, &source, &destination);
                SDL3.SDL_EndGPUCopyPass(copyPass);

                using (GpuFence fence = SubmitAndAcquireFence())
                {
                    _gpuDevice.WaitForFences([fence]);
                }

                byte* mapped = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
                SdlError.ThrowOnNull(mapped);
                new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
                SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
            }
            finally
            {
                SDL3.SDL_ReleaseGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
            }
        }

        return new RawImage(pixels, texture.Size, pixelFormat);
#endif
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

    /// <summary>
    /// Begins a render pass. <paramref name="resolveTextures"/> is either empty or has one entry per color target: the texture with one
    /// sample that a multisampled color target resolves into, or null for a target that does not resolve.
    /// </summary>
    public RenderPass CreateRenderPass(
        ReadOnlySpan<Texture> colorTargets,
        ReadOnlySpan<ColorTargetSettings> colorTargetSettings,
        Texture? depthBuffer,
        DepthBufferSettings depthBufferSettings,
        ReadOnlySpan<Texture?> resolveTextures = default)
    {
        ThrowIfDisposed();

        if (!resolveTextures.IsEmpty && resolveTextures.Length != colorTargets.Length)
        {
            throw new ArgumentException($"There are {resolveTextures.Length} resolve textures for {colorTargets.Length} color targets. Pass one per color target, or none.", nameof(resolveTextures));
        }

        SampleCount sampleCount = ValidateSampleCounts(colorTargets, depthBuffer);
        if (depthBuffer != null)
        {
            ValidateDepthBufferStoreOperations(depthBufferSettings);
        }

        Span<SDL_GPUColorTargetInfo> colorTargetInfos = stackalloc SDL_GPUColorTargetInfo[colorTargets.Length];
            
        for (int i = 0; i < colorTargets.Length; i++)
        {
            Texture colorTarget = colorTargets[i];
            ColorTargetSettings colorTargetSetting = colorTargetSettings[i];
            Texture? resolveTexture = resolveTextures.IsEmpty ? null : resolveTextures[i];
            ValidateResolve(i, colorTarget, colorTargetSetting.StoreOperation, resolveTexture);

            colorTargetInfos[i] = new SDL_GPUColorTargetInfo
            {
                texture = colorTarget.SdlGpuTexture,
                clear_color = colorTargetSetting.ClearColorValue,
                load_op = (SDL_GPULoadOp)colorTargetSetting.LoadOperation,
                store_op = (SDL_GPUStoreOp)colorTargetSetting.StoreOperation,
                resolve_texture = resolveTexture != null ? resolveTexture.SdlGpuTexture : Pointer<SDL_GPUTexture>.Null
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
            sampleCount,
            CalculateTargetSize(colorTargets, depthBuffer));
    }

    // Every backend needs the attachments of a pass to share one sample count, which the pass's pipelines must also have.
    internal static SampleCount ValidateSampleCounts(ReadOnlySpan<Texture> colorTargets, Texture? depthBuffer)
    {
        SampleCount sampleCount = colorTargets.IsEmpty ? depthBuffer?.SampleCount ?? SampleCount.Count1 : colorTargets[0].SampleCount;

        for (int i = 1; i < colorTargets.Length; i++)
        {
            if (colorTargets[i].SampleCount != sampleCount)
            {
                throw new InvalidOperationException(
                    $"All attachments of a render pass need the same sample count, but color target 0 has {sampleCount} and color target {i} has {colorTargets[i].SampleCount}.");
            }
        }

        if (depthBuffer != null && depthBuffer.SampleCount != sampleCount)
        {
            throw new InvalidOperationException(
                $"All attachments of a render pass need the same sample count, but the color targets have {sampleCount} and the depth buffer has {depthBuffer.SampleCount}.");
        }

        return sampleCount;
    }

    // SDL has no resolve for depth-stencil targets.
    internal static void ValidateDepthBufferStoreOperations(DepthBufferSettings settings)
    {
        if (settings.DepthBufferStoreOperation is StoreOperation.Resolve or StoreOperation.ResolveAndStore
            || settings.StencilStoreOperation is StoreOperation.Resolve or StoreOperation.ResolveAndStore)
        {
            throw new InvalidOperationException(
                $"A depth-stencil buffer cannot be resolved, but its store operations are {settings.DepthBufferStoreOperation} for depth and {settings.StencilStoreOperation} for stencil.");
        }
    }

    internal static void ValidateResolve(int index, Texture colorTarget, StoreOperation storeOperation, Texture? resolveTexture)
    {
        bool resolves = storeOperation is StoreOperation.Resolve or StoreOperation.ResolveAndStore;

        if (resolves && resolveTexture == null)
        {
            throw new InvalidOperationException($"Color target {index} has the store operation {storeOperation} but no resolve texture.");
        }

        if (resolveTexture == null)
        {
            return;
        }

        if (!resolves)
        {
            throw new InvalidOperationException(
                $"Color target {index} has a resolve texture but the store operation {storeOperation}. Use {nameof(StoreOperation.Resolve)} or {nameof(StoreOperation.ResolveAndStore)}.");
        }

        if (colorTarget.SampleCount == SampleCount.Count1)
        {
            throw new InvalidOperationException($"Color target {index} has one sample, so there is nothing to resolve. Only a multisampled color target can be resolved.");
        }

        if ((resolveTexture.Usage & TextureUsage.ColorTarget) == 0)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} has the usage '{resolveTexture.Usage}', but a resolve texture needs {nameof(TextureUsage.ColorTarget)}.");
        }

        if (resolveTexture.SampleCount != SampleCount.Count1)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} has {resolveTexture.SampleCount}, but a resolve texture needs one sample.");
        }

        if (resolveTexture.Format != colorTarget.Format)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} has the format {resolveTexture.Format}, but the color target has {colorTarget.Format}.");
        }

        if (resolveTexture.Size != colorTarget.Size)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} is {resolveTexture.Size.Width}x{resolveTexture.Size.Height}, but the color target is {colorTarget.Size.Width}x{colorTarget.Size.Height}.");
        }
    }

    // A pass can only safely address the area every attachment shares, so the scissor bounds
    // are the smallest attachment, depth included.
    private static ShortSize CalculateTargetSize(ReadOnlySpan<Texture> colorTargets, Texture? depthBuffer)
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

    private RenderPass CreateMultipleRenderTargetsPassInternal(
        ReadOnlySpan<SDL_GPUColorTargetInfo> colorTargetInfos,
        Pointer<SDL_GPUTexture> depthBufferPointer,
        DepthBufferSettings depthBufferSettings,
        DepthBufferFormat depthBufferFormat,
        SampleCount sampleCount,
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
            
            RenderPass renderPass = new RenderPass(this, gpuRenderPass, depthBufferFormat, sampleCount, targetSize);

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

    public ComputePass CreateComputePass(
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

    public ComputePass CreateComputePass()
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
}
