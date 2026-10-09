using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pixely.Shaders;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class GpuDevice : IDisposable
{
    private MemoryTrackedSet<Texture> _textures = new();
    private MemoryTrackedSet<GpuVertexBuffer> _vertexBuffers = new();
    private MemoryTrackedSet<GpuIndexBuffer> _indexBuffers = new();
    private MemoryTrackedSet<GpuStorageBuffer> _storageBuffers = new();
    private LockedSet<Sampler> _samplers = new();
    private LockedSet<GraphicsPipeline> _graphicsPipelines = new();
    private LockedSet<ComputePipeline> _computePipelines = new();
    private LockedSet<GraphicsShaderProgram> _graphicsShaderPrograms = new();

    internal Pointer<SDL_GPUDevice> SdlGpuDevice { get; private set; }

    public string Driver { get; }

    // The graphics driver's own name, such as radv or V3DV Mesa, where Driver is SDL's backend. SDL may not report one.
    internal string? NativeDriverName { get; }

    public GpuMemoryStats MemoryStats
    {
        get
        {
            (int Count, long TotalBytes) textures = _textures.CountAndTotalBytes;
            (int Count, long TotalBytes) vertexBuffers = _vertexBuffers.CountAndTotalBytes;
            (int Count, long TotalBytes) indexBuffers = _indexBuffers.CountAndTotalBytes;
            (int Count, long TotalBytes) storageBuffers = _storageBuffers.CountAndTotalBytes;
            return new GpuMemoryStats(
                textures.Count, textures.TotalBytes,
                vertexBuffers.Count, vertexBuffers.TotalBytes,
                indexBuffers.Count, indexBuffers.TotalBytes,
                storageBuffers.Count, storageBuffers.TotalBytes
            );
        }
    }

    internal GpuDevice(Pointer<SDL_GPUDevice> sdlGpuDevice)
    {
        SdlGpuDevice = sdlGpuDevice;

        unsafe
        {
            byte* driver = SDL3.Unsafe_SDL_GetGPUDeviceDriver(sdlGpuDevice);
            Driver = Marshal.PtrToStringUTF8((IntPtr)driver) ??
                     throw new PixelyInitializationException("SDL_GetGPUDeviceDriver returned null");
        }

        SDL_PropertiesID properties;
        unsafe
        {
            properties = SDL3.SDL_GetGPUDeviceProperties(sdlGpuDevice);
        }

        NativeDriverName = SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING, null);
    }

    public ShaderFormats GetSupportedShaderFormats()
    {
        unsafe
        {
            SDL_GPUShaderFormat formats = SDL3.SDL_GetGPUShaderFormats(SdlGpuDevice);

            ShaderFormats shaderFormats = new ShaderFormats((uint)formats);

            return shaderFormats;
        }
    }

    public bool IsTextureFormatSupported(TextureFormat format, TextureType type, TextureUsage usage)
    {
        unsafe
        {
            return SDL3.SDL_GPUTextureSupportsFormat(
                SdlGpuDevice,
                (SDL_GPUTextureFormat)format,
                (SDL_GPUTextureType)type,
                (SDL_GPUTextureUsageFlags)usage);
        }
    }

    public CommandBuffer AcquireCommandBuffer()
    {
        return new CommandBuffer(this, AcquireSdlCommandBuffer());
    }

    internal Pointer<SDL_GPUCommandBuffer> AcquireSdlCommandBuffer()
    {
        unsafe
        {
            Pointer<SDL_GPUCommandBuffer> sdlGpuCommandBuffer = SDL3.SDL_AcquireGPUCommandBuffer(SdlGpuDevice);

            if (sdlGpuCommandBuffer.IsNull)
            {
                throw new PixelyInitializationException($"SDL_AcquireGPUCommandBuffer failed: {SDL3.SDL_GetError()}");
            }

            return sdlGpuCommandBuffer;
        }
    }

    internal Pointer<SDL_GPUTransferBuffer> CreateUploadTransferBuffer(uint size)
    {
        unsafe
        {
            SDL_GPUTransferBufferCreateInfo createInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                size = size
            };
            Pointer<SDL_GPUTransferBuffer> transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(SdlGpuDevice, &createInfo);
            SdlError.ThrowOnNull(transferBuffer);
            return transferBuffer;
        }
    }

    internal void ReleaseTransferBuffer(Pointer<SDL_GPUTransferBuffer> transferBuffer)
    {
        if (transferBuffer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUTransferBuffer(SdlGpuDevice, transferBuffer);
        }
    }

    public Sampler CreateSampler(SamplerConfig config)
    {
        SDL_GPUSamplerCreateInfo sdlGpuSamplerCreateInfo = new SDL_GPUSamplerCreateInfo()
        {
            min_filter = (SDL_GPUFilter)config.MinFilter,
            mag_filter = (SDL_GPUFilter)config.MagFilter,
            mipmap_mode = (SDL_GPUSamplerMipmapMode)config.MipmapMode,
            address_mode_u = (SDL_GPUSamplerAddressMode)config.AddressModeU,
            address_mode_v = (SDL_GPUSamplerAddressMode)config.AddressModeV,
            address_mode_w = (SDL_GPUSamplerAddressMode)config.AddressModeW,
            mip_lod_bias = config.MipLodBias,
            min_lod = config.MinLod,
            max_lod = config.MaxLod,
            max_anisotropy = config.MaxAnisotropy,
            enable_anisotropy = config.EnableAnisotropy,
            compare_op = (SDL_GPUCompareOp)config.CompareOp,
            enable_compare = config.EnableCompare
        };

        unsafe
        {
            Pointer<SDL_GPUSampler> samplerPointer = SDL3.SDL_CreateGPUSampler(SdlGpuDevice, &sdlGpuSamplerCreateInfo);
            SdlError.ThrowOnNull(samplerPointer);

            var sampler = new Sampler(this, samplerPointer);
            _samplers.Add(sampler);
            return sampler;
        }
    }

    /// <summary>
    /// Whether textures of the format can have the sample count on this device. <see cref="SampleCount.Count1"/> is always supported.
    /// </summary>
    public bool IsSampleCountSupported(TextureFormat format, SampleCount sampleCount)
    {
        // SDL indexes its backends' tables with the value, so an undefined one must not reach it.
        if (sampleCount is < SampleCount.Count1 or > SampleCount.Count8)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, $"'{sampleCount}' is not a {nameof(SampleCount)}.");
        }

        // On Direct3D 12 the SDL that Pixely pins checks a depth format by its shader-view format, so it can report no multisampling for a depth
        // format the GPU supports it for, such as Depth24Stencil8. Texture creation and pipeline building then reject that count.
        // libsdl-org/SDL#16124, fixed by libsdl-org/SDL#16230 in a later SDL.
        unsafe
        {
            return sampleCount == SampleCount.Count1 || SDL3.SDL_GPUTextureSupportsSampleCount(SdlGpuDevice, (SDL_GPUTextureFormat)format, (SDL_GPUSampleCount)sampleCount);
        }
    }

    /// <summary>
    /// Creates a depth-stencil buffer. A multisampled one cannot be sampled, so <paramref name="sampler"/> must be false with a
    /// <paramref name="sampleCount"/> above <see cref="SampleCount.Count1"/>.
    /// </summary>
    public Texture CreateDepthBufferTexture(ShortSize size, DepthBufferFormat format, bool sampler = false, SampleCount sampleCount = SampleCount.Count1)
    {
        TextureUsage usage = sampler ? TextureUsage.DepthStencilTarget | TextureUsage.Sampler : TextureUsage.DepthStencilTarget;
        return CreateTexture(size, (TextureFormat)format, usage, sampleCount);
    }

    /// <summary>
    /// Creates a color target that can also be sampled. A multisampled one cannot be sampled, so with a <paramref name="sampleCount"/>
    /// above <see cref="SampleCount.Count1"/> it is only a color target, for a render pass to resolve into a texture with one sample.
    /// </summary>
    public Texture CreateColorTargetTexture(ShortSize size, TextureFormat format, SampleCount sampleCount = SampleCount.Count1)
    {
        TextureUsage usage = sampleCount == SampleCount.Count1 ? TextureUsage.ColorTarget | TextureUsage.Sampler : TextureUsage.ColorTarget;
        return CreateTexture(size, format, usage, sampleCount);
    }

    /// <summary>
    /// Creates a 2D texture. A multisampled one, with a <paramref name="sampleCount"/> above <see cref="SampleCount.Count1"/>,
    /// can only be a color or depth-stencil target.
    /// </summary>
    public Texture CreateTexture(ShortSize size, TextureFormat format, TextureUsage usage, SampleCount sampleCount = SampleCount.Count1)
    {
        unsafe
        {
            SDL_GPUTextureUsageFlags sdlUsage = (SDL_GPUTextureUsageFlags)usage;

            if (SDL3.SDL_GPUTextureSupportsFormat(SdlGpuDevice, (SDL_GPUTextureFormat)format, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, sdlUsage) == false)
            {
                throw new ArgumentException($"Texture format '{format}' is not supported for usage '{usage}' on this GPU.", nameof(format));
            }

            if (sampleCount != SampleCount.Count1)
            {
                if ((usage & ~(TextureUsage.ColorTarget | TextureUsage.DepthStencilTarget)) != 0)
                {
                    throw new ArgumentException($"A multisampled texture can only be a color or depth-stencil target, but the usage is '{usage}'.", nameof(usage));
                }

                if (!IsSampleCountSupported(format, sampleCount))
                {
                    throw new ArgumentException($"Sample count '{sampleCount}' is not supported for texture format '{format}' on this GPU.", nameof(sampleCount));
                }
            }

            SDL_GPUTextureCreateInfo info = new SDL_GPUTextureCreateInfo
            {
                usage = sdlUsage,
                format = (SDL_GPUTextureFormat)format,
                width = size.Width,
                height = size.Height,
                layer_count_or_depth = 1,
                num_levels = 1,
                sample_count = (SDL_GPUSampleCount)sampleCount
            };

            Pointer<SDL_GPUTexture> rawTexture = SDL3.SDL_CreateGPUTexture(SdlGpuDevice, &info);
            SdlError.ThrowOnNull(rawTexture);

            Texture texture = new UserTexture(this, rawTexture, size, format, usage, sampleCount);
            _textures.Add(texture);

            return texture;
        }
    }

    public void RegisterTexture(Texture texture) => _textures.Add(texture);

    public void RegisterVertexBuffer(GpuVertexBuffer vertexBuffer) => _vertexBuffers.Add(vertexBuffer);

    public void RegisterIndexBuffer(GpuIndexBuffer indexBuffer) => _indexBuffers.Add(indexBuffer);

    public void RegisterGraphicsPipeline(GraphicsPipeline graphicsPipeline) => _graphicsPipelines.Add(graphicsPipeline);

    public void RegisterComputePipeline(ComputePipeline computePipeline) => _computePipelines.Add(computePipeline);

    internal void RegisterGraphicsShaderProgram(GraphicsShaderProgram shaderProgram) =>
        _graphicsShaderPrograms.Add(shaderProgram);

    public void ReleaseTexture(Texture texture)
    {
        _textures.Remove(texture);
        Pointer<SDL_GPUTexture> pointer = texture.SdlGpuTexture;
        if (pointer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUTexture(SdlGpuDevice, texture.SdlGpuTexture);
        }

        texture.SdlGpuTexture = Pointer<SDL_GPUTexture>.Null;
    }
    
    public void ReleaseGraphicsPipeline(GraphicsPipeline pipeline)
    {
        _graphicsPipelines.Remove(pipeline);
        Pointer<SDL_GPUGraphicsPipeline> pointer = pipeline.Pointer;
        if (pointer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUGraphicsPipeline(SdlGpuDevice, pointer);
        }

        pipeline.Pointer = default;
    }

    public void ReleaseComputePipeline(ComputePipeline computePipeline)
    {
        _computePipelines.Remove(computePipeline);
        Pointer<SDL_GPUComputePipeline> pointer = computePipeline.Pointer;
        if (pointer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUComputePipeline(SdlGpuDevice, pointer);
        }

        computePipeline.Pointer = default;
    }

    internal void ReleaseGraphicsShaderProgram(GraphicsShaderProgram shaderProgram)
    {
        _graphicsShaderPrograms.Remove(shaderProgram);
        ReleaseShader(shaderProgram.VertexShader);
        ReleaseShader(shaderProgram.FragmentShader);
    }

    internal void ReleaseShader(GraphicsShader shader)
    {
        Pointer<SDL_GPUShader> pointer = shader.Pointer;
        if (pointer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUShader(SdlGpuDevice, pointer);
        }

        shader.Pointer = Pointer<SDL_GPUShader>.Null;
    }

    public void ReleaseVertexBuffer(GpuVertexBuffer vertexBuffer)
    {
        _vertexBuffers.Remove(vertexBuffer);
        if (!vertexBuffer.SdlVertexBuffer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_ReleaseGPUBuffer(SdlGpuDevice, vertexBuffer.SdlVertexBuffer);
            }

            vertexBuffer.SdlVertexBuffer = default;
        }
    }

    public void ReleaseIndexBuffer(GpuIndexBuffer indexBuffer)
    {
        _indexBuffers.Remove(indexBuffer);
        if (!indexBuffer.SdlBuffer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_ReleaseGPUBuffer(SdlGpuDevice, indexBuffer.SdlBuffer);
            }

            indexBuffer.SdlBuffer = default;
        }
    }

    public void RegisterStorageBuffer(GpuStorageBuffer storageBuffer) => _storageBuffers.Add(storageBuffer);

    public void ReleaseStorageBuffer(GpuStorageBuffer storageBuffer)
    {
        _storageBuffers.Remove(storageBuffer);
        if (!storageBuffer.SdlBuffer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_ReleaseGPUBuffer(SdlGpuDevice, storageBuffer.SdlBuffer);
            }

            storageBuffer.SdlBuffer = default;
        }
    }

    public void ReleaseSampler(Sampler sampler)
    {
        _samplers.Remove(sampler);

        unsafe
        {
            SDL3.SDL_ReleaseGPUSampler(SdlGpuDevice, sampler.Pointer);
        }

        sampler.Pointer = default;
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(int length) where TVertexType: unmanaged, IVertexType
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<TVertexType>() * length);
        unsafe
        {
            SDL_GPUBufferCreateInfo sdlGpuBufferCreateInfo = new SDL_GPUBufferCreateInfo()
            {
                usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
                size = sizeBytes
            };

            SDL_GPUBuffer* rawVertexBuffer = SDL3.SDL_CreateGPUBuffer(SdlGpuDevice, &sdlGpuBufferCreateInfo);

            GpuVertexBuffer<TVertexType> vertexBuffer = new GpuVertexBuffer<TVertexType>(this, rawVertexBuffer, length);
            RegisterVertexBuffer(vertexBuffer);
            return vertexBuffer;
        }
    }

    // SDL's WebGPU backend waits by suspending the wasm stack, which a managed frame cannot survive.
    [UnsupportedOSPlatform("browser")]
    public void WaitForFences(ReadOnlySpan<GpuFence> fences, bool waitAll = true)
    {
#if BROWSER
        throw new PlatformNotSupportedException("Waiting for GPU fences is not supported in the browser.");
#else
        if (fences.Length == 0)
        {
            return;
        }

        Span<Pointer<SDL_GPUFence>> fencePointers = stackalloc Pointer<SDL_GPUFence>[fences.Length];
        for (int i = 0; i < fences.Length; i++)
        {
            fencePointers[i] = fences[i].Pointer;
        }

        unsafe
        {
            fixed (Pointer<SDL_GPUFence>* fencePointersPtr = fencePointers)
            {
                if (!SdlBoolInterop.SDL_WaitForGPUFences(SdlGpuDevice, waitAll, (SDL_GPUFence**)fencePointersPtr, (uint)fences.Length))
                {
                    throw new PixelyException($"SDL_WaitForGPUFences failed: {SDL3.SDL_GetError()}");
                }
            }
        }
#endif
    }

    public void Dispose()
    {
        // ClearAndCopy atomically copies and clears under lock.
        // Release methods will find their collections already cleared, so the Remove is a no-op.
        foreach (GraphicsPipeline graphicsPipeline in _graphicsPipelines.ClearAndCopy())
        {
            ReleaseGraphicsPipeline(graphicsPipeline);
        }

        foreach (ComputePipeline computePipeline in _computePipelines.ClearAndCopy())
        {
            ReleaseComputePipeline(computePipeline);
        }

        foreach (GraphicsShaderProgram shaderProgram in _graphicsShaderPrograms.ClearAndCopy())
        {
            ReleaseGraphicsShaderProgram(shaderProgram);
        }

        foreach (GpuVertexBuffer vertexBuffer in _vertexBuffers.ClearAndCopy())
        {
            ReleaseVertexBuffer(vertexBuffer);
        }

        foreach (GpuIndexBuffer indexBuffer in _indexBuffers.ClearAndCopy())
        {
            ReleaseIndexBuffer(indexBuffer);
        }

        foreach (GpuStorageBuffer storageBuffer in _storageBuffers.ClearAndCopy())
        {
            ReleaseStorageBuffer(storageBuffer);
        }

        foreach (Texture texture in _textures.ClearAndCopy())
        {
            ReleaseTexture(texture);
        }

        foreach (Sampler sampler in _samplers.ClearAndCopy())
        {
            ReleaseSampler(sampler);
        }

#if !BROWSER
        // In the browser the device lives as long as the page, which frees it. A clean destroy has to wait for the last submissions,
        // and a submission completes only after the page's event loop turns, which a synchronous Dispose cannot wait for.
        unsafe
        {
            SDL3.SDL_DestroyGPUDevice(SdlGpuDevice);
            SdlGpuDevice = null;
        }
#endif
    }

}
