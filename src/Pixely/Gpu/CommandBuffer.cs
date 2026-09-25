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

    // Handed out again for every pass begun on this command buffer.
    private RenderPass? _renderPass;
    private ComputePass? _computePass;

    // SDL allows one open pass per command buffer.
    private IDisposable? _openPass;

    // SDL forbids cancelling once a swapchain texture is acquired, so a failure after the acquire has to submit instead.
    private bool _hasSwapchainTexture;

    internal Pointer<SDL_GPUCommandBuffer> SdlGpuCommandBuffer
    {
        get => _sdlGpuCommandBuffer;
        private set => _sdlGpuCommandBuffer = value;
    }

    public ShaderUniformSlotSizes FragmentShaderUniformSlotSizes => _fragmentShaderUniformSlotSizes;
    public ShaderUniformSlotSizes VertexShaderUniformSlotSizes => _vertexShaderUniformSlotSizes;

    internal CommandBuffer(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    internal void Begin(Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer)
    {
        SdlGpuCommandBuffer = sdlCommandBuffer;
        _fragmentShaderUniformSlotSizes = default;
        _vertexShaderUniformSlotSizes = default;
        _openPass = null;
        _hasSwapchainTexture = false;
    }

    internal void OnSwapchainTextureAcquired()
    {
        _hasSwapchainTexture = true;
    }

    public void Submit()
    {
        if (SubmitEndingOpenPass())
        {
            throw new InvalidOperationException(OpenPassAtSubmitMessage);
        }
    }

    /// <summary>
    /// Submits without complaining about a pass left open: it is ended first. For disposal paths, where throwing would hide the
    /// exception that left the pass open. Returns whether a pass was open.
    /// </summary>
    internal bool SubmitEndingOpenPass()
    {
        ThrowIfDisposed();
        bool passWasOpen = EndOpenPass();
        unsafe
        {
            // TODO: error handling
            SDL3.SDL_SubmitGPUCommandBuffer(SdlGpuCommandBuffer);
            SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
        }
        _gpuDevice.ReturnCommandBuffer(this);
        return passWasOpen;
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
        ThrowIfPassOpen();
        texture.ThrowIfDisposed();
#if BROWSER
        throw new PlatformNotSupportedException("Downloading a texture is not supported in the browser.");
#else

        // Until the submit below, a failure cancels the command buffer, so that it is not left unsubmitted and out of the pool.
        bool submitted = false;
        try
        {
            return DownloadTexture(texture, ref submitted);
        }
        catch when (!submitted)
        {
            Cancel();
            throw;
        }
#endif
    }

#if !BROWSER
    private Image DownloadTexture(Texture texture, ref bool submitted)
    {
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

                submitted = true;
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
    }
#endif

    public GpuFence SubmitAndAcquireFence()
    {
        ThrowIfDisposed();
        bool passWasOpen = EndOpenPass();
        unsafe
        {
            SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(SdlGpuCommandBuffer);
            SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
            _gpuDevice.ReturnCommandBuffer(this);

            if (fence == null)
            {
                throw new PixelyException($"SDL_SubmitGPUCommandBufferAndAcquireFence failed: {SDL3.SDL_GetError()}");
            }

            if (passWasOpen)
            {
                SDL3.SDL_ReleaseGPUFence(_gpuDevice.SdlGpuDevice, fence);
                throw new InvalidOperationException(OpenPassAtSubmitMessage);
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
    /// Begins a render pass. The command buffer hands out the same <see cref="RenderPass"/> object for every pass it begins,
    /// so dispose one before beginning the next; beginning a second while one is open throws.
    /// </summary>
    public RenderPass CreateRenderPass(ReadOnlySpan<Texture> colorTargets, ReadOnlySpan<ColorTargetSettings> colorTargetSettings, Texture? depthBuffer, DepthBufferSettings depthBufferSettings)
    {
        ThrowIfDisposed();

        if (colorTargetSettings.Length != colorTargets.Length)
        {
            throw new ArgumentException($"{colorTargets.Length} color targets need {colorTargets.Length} settings, but {colorTargetSettings.Length} were given.", nameof(colorTargetSettings));
        }

        Span<SDL_GPUColorTargetInfo> colorTargetInfos = stackalloc SDL_GPUColorTargetInfo[colorTargets.Length];

        for (int i = 0; i < colorTargets.Length; i++)
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
        ShortSize targetSize)
    {
        ThrowIfDisposed();
        ThrowIfPassOpen();

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
            
            SdlError.ThrowOnNull(gpuRenderPass);

            RenderPass renderPass = _renderPass ??= new RenderPass(this);
            renderPass.Begin(gpuRenderPass, depthBufferFormat, targetSize);
            _openPass = renderPass;
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
        ThrowIfPassOpen();
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

            SdlError.ThrowOnNull(computePass);

            StorageBufferElementSizes rwElementSizes = BuildStorageBufferElementSizes(readWriteStorageBuffers);
            ComputePass pass = _computePass ??= new ComputePass(this);
            pass.Begin(computePass, (uint)readWriteStorageTextures.Length, (uint)readWriteStorageBuffers.Length, rwElementSizes);
            _openPass = pass;
            return pass;
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
            EndOpenPass();
            unsafe
            {
                // SDL refuses to cancel once a swapchain texture is acquired, and the command buffer then stays pending, so it is
                // not handed out again.
                SdlError.ThrowOnFalse(SDL3.SDL_CancelGPUCommandBuffer(SdlGpuCommandBuffer), "SDL_CancelGPUCommandBuffer");
            }
            SdlGpuCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;
            _gpuDevice.ReturnCommandBuffer(this);
        }
    }

    /// <summary>
    /// Gives up a command buffer whose recording failed: cancels it, or submits it when it holds a swapchain texture, which SDL
    /// does not let it cancel. Used in failure paths, so it throws nothing about a pass left open.
    /// </summary>
    internal void CancelOrSubmit()
    {
        if (_hasSwapchainTexture)
        {
            SubmitEndingOpenPass();
        }
        else
        {
            Cancel();
        }
    }

    private const string OpenPassAtSubmitMessage =
        "A pass was still open when the command buffer was submitted. It was ended first; dispose passes before submitting.";

    // A pass still open when its command buffer goes back to the pool would later end whatever pass the next holder begins,
    // so it is ended here, while it still belongs to this submission.
    private bool EndOpenPass()
    {
        if (_openPass == null)
        {
            return false;
        }

        _openPass.Dispose();
        return true;
    }

    internal void OnPassEnded(IDisposable pass)
    {
        if (ReferenceEquals(_openPass, pass))
        {
            _openPass = null;
        }
    }

    private void ThrowIfPassOpen()
    {
        if (_openPass != null)
        {
            throw new InvalidOperationException("A pass is still open on this command buffer. Dispose it before beginning another.");
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
