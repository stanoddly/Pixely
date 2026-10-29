using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Pixely.Shaders;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// The GPU device: it creates the resources and command buffers and tracks the memory of the resources. SDL GPU backs it in
/// a game, and the stub device of Pixely.Testing backs it in a game's tests. The constructor and the backend's members are
/// internal, so no other assembly derives from it.
/// </summary>
public abstract class GpuDevice : IDisposable
{
    private MemoryTrackedSet<Texture> _textures = new();
    private MemoryTrackedSet<GpuVertexBuffer> _vertexBuffers = new();
    private MemoryTrackedSet<GpuIndexBuffer> _indexBuffers = new();
    private MemoryTrackedSet<GpuStorageBuffer> _storageBuffers = new();
    private LockedSet<Sampler> _samplers = new();
    private LockedSet<GraphicsPipeline> _graphicsPipelines = new();
    private LockedSet<ComputePipeline> _computePipelines = new();
    private LockedSet<GraphicsShaderProgram> _graphicsShaderPrograms = new();

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

    internal GpuDevice(string driver, string? nativeDriverName)
    {
        Driver = driver;
        NativeDriverName = nativeDriverName;
    }

    public ShaderFormats GetSupportedShaderFormats() => GetSupportedShaderFormatsCore();

    public bool IsTextureFormatSupported(TextureFormat format, TextureType type, TextureUsage usage) => IsTextureFormatSupportedCore(format, type, usage);

    public CommandBuffer AcquireCommandBuffer() => AcquireCommandBufferCore();

    public Sampler CreateSampler(SamplerConfig config)
    {
        Sampler sampler = new Sampler(this, CreateSamplerCore(config));
        _samplers.Add(sampler);
        return sampler;
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

        return sampleCount == SampleCount.Count1 || IsSampleCountSupportedCore(format, sampleCount);
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
        if (!IsTextureFormatSupported(format, TextureType.TwoD, usage))
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

        Pointer<SDL_GPUTexture> handle = CreateTextureCore(TextureType.TwoD, size, 1, format, usage, sampleCount);

        Texture texture = new UserTexture(this, handle, size, format, usage, sampleCount);
        _textures.Add(texture);

        return texture;
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
        Pointer<SDL_GPUTexture> handle = texture.SdlGpuTexture;
        if (handle.IsNull)
        {
            return;
        }

        ReleaseTextureCore(handle);
        texture.SdlGpuTexture = Pointer<SDL_GPUTexture>.Null;
    }

    public void ReleaseGraphicsPipeline(GraphicsPipeline pipeline)
    {
        _graphicsPipelines.Remove(pipeline);
        Pointer<SDL_GPUGraphicsPipeline> handle = pipeline.Pointer;
        if (handle.IsNull)
        {
            return;
        }

        ReleaseGraphicsPipelineCore(handle);
        pipeline.Pointer = default;
    }

    public void ReleaseComputePipeline(ComputePipeline computePipeline)
    {
        _computePipelines.Remove(computePipeline);
        Pointer<SDL_GPUComputePipeline> handle = computePipeline.Pointer;
        if (handle.IsNull)
        {
            return;
        }

        ReleaseComputePipelineCore(handle);
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
        Pointer<SDL_GPUShader> handle = shader.Pointer;
        if (handle.IsNull)
        {
            return;
        }

        ReleaseShaderCore(handle);
        shader.Pointer = Pointer<SDL_GPUShader>.Null;
    }

    public void ReleaseVertexBuffer(GpuVertexBuffer vertexBuffer)
    {
        _vertexBuffers.Remove(vertexBuffer);
        if (!vertexBuffer.SdlVertexBuffer.IsNull)
        {
            ReleaseBufferCore(vertexBuffer.SdlVertexBuffer);
            vertexBuffer.SdlVertexBuffer = default;
        }
    }

    public void ReleaseIndexBuffer(GpuIndexBuffer indexBuffer)
    {
        _indexBuffers.Remove(indexBuffer);
        if (!indexBuffer.SdlBuffer.IsNull)
        {
            ReleaseBufferCore(indexBuffer.SdlBuffer);
            indexBuffer.SdlBuffer = default;
        }
    }

    public void RegisterStorageBuffer(GpuStorageBuffer storageBuffer) => _storageBuffers.Add(storageBuffer);

    public void ReleaseStorageBuffer(GpuStorageBuffer storageBuffer)
    {
        _storageBuffers.Remove(storageBuffer);
        if (!storageBuffer.SdlBuffer.IsNull)
        {
            ReleaseBufferCore(storageBuffer.SdlBuffer);
            storageBuffer.SdlBuffer = default;
        }
    }

    public void ReleaseSampler(Sampler sampler)
    {
        _samplers.Remove(sampler);
        ReleaseSamplerCore(sampler.Pointer);
        sampler.Pointer = default;
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(int length) where TVertexType: unmanaged, IVertexType
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<TVertexType>() * length);
        Pointer<SDL_GPUBuffer> handle = CreateBufferCore(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, sizeBytes);

        GpuVertexBuffer<TVertexType> vertexBuffer = new GpuVertexBuffer<TVertexType>(this, handle, length);
        RegisterVertexBuffer(vertexBuffer);
        return vertexBuffer;
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

        WaitForFencesCore(fences, waitAll);
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

        DestroyCore();
    }

    // The backend. Each creation returns a handle that is not null or throws, and only the device that created a handle reads
    // it: the SDL device takes it as the SDL object, the stub as an identifier. The SDL types are the vocabulary, not a promise
    // that SDL is behind the device.
    // The name of the GPU and the version of its driver, for diagnostics. Either may be unknown.
    internal abstract string? DeviceName { get; }
    internal abstract string? DriverVersion { get; }
    internal abstract ShaderFormats GetSupportedShaderFormatsCore();
    internal abstract bool IsTextureFormatSupportedCore(TextureFormat format, TextureType type, TextureUsage usage);
    // Called with a sample count above Count1 only.
    internal abstract bool IsSampleCountSupportedCore(TextureFormat format, SampleCount sampleCount);
    internal abstract CommandBuffer AcquireCommandBufferCore();
    internal abstract Pointer<SDL_GPUSampler> CreateSamplerCore(SamplerConfig config);
    internal abstract Pointer<SDL_GPUTexture> CreateTextureCore(TextureType type, ShortSize size, uint layerCount, TextureFormat format, TextureUsage usage, SampleCount sampleCount);
    internal abstract Pointer<SDL_GPUBuffer> CreateBufferCore(SDL_GPUBufferUsageFlags usage, uint sizeInBytes);
    internal abstract Pointer<SDL_GPUTransferBuffer> CreateUploadTransferBufferCore(uint sizeInBytes);
    internal abstract Pointer<SDL_GPUShader> CreateShaderCore(in SDL_GPUShaderCreateInfo createInfo);
    internal abstract Pointer<SDL_GPUGraphicsPipeline> CreateGraphicsPipelineCore(in SDL_GPUGraphicsPipelineCreateInfo createInfo);
    internal abstract Pointer<SDL_GPUComputePipeline> CreateComputePipelineCore(in SDL_GPUComputePipelineCreateInfo createInfo);
    internal abstract void ReleaseTextureCore(Pointer<SDL_GPUTexture> texture);
    internal abstract void ReleaseBufferCore(Pointer<SDL_GPUBuffer> buffer);
    // Called with a null transfer buffer too, which releases nothing.
    internal abstract void ReleaseTransferBufferCore(Pointer<SDL_GPUTransferBuffer> transferBuffer);
    internal abstract void ReleaseSamplerCore(Pointer<SDL_GPUSampler> sampler);
    internal abstract void ReleaseShaderCore(Pointer<SDL_GPUShader> shader);
    internal abstract void ReleaseGraphicsPipelineCore(Pointer<SDL_GPUGraphicsPipeline> pipeline);
    internal abstract void ReleaseComputePipelineCore(Pointer<SDL_GPUComputePipeline> pipeline);
    internal abstract bool QueryFenceCore(Pointer<SDL_GPUFence> fence);
    internal abstract void ReleaseFenceCore(Pointer<SDL_GPUFence> fence);
#if !BROWSER
    internal abstract void WaitForFencesCore(ReadOnlySpan<GpuFence> fences, bool waitAll);
#endif
    // Runs once Dispose has released every tracked resource.
    internal abstract void DestroyCore();
}
