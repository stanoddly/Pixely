using System.Runtime.InteropServices;
using Pixely.Gpu;
using Pixely.Shaders;
using Pixely.Utilities;
using SDL;

namespace Pixely.Testing;

/// <summary>
/// A GPU device without a GPU, for a game's tests. It runs Pixely's validation and records what reaches it: every submitted
/// command buffer with its render and compute passes, draws and dispatches, and the contents uploaded to buffers and textures.
/// Nothing is rendered: a texture holds what was uploaded to it, or zeros.
/// </summary>
public sealed class StubGpuDevice : GpuDevice
{
    private readonly StubGpuDeviceOptions _options;
    private readonly Lock _lock = new();
    private readonly List<StubCommandBuffer> _submittedCommandBuffers = new();
    private readonly Dictionary<nint, byte[]> _bufferContents = new();
    private readonly Dictionary<(nint Texture, uint Layer), byte[]> _textureContents = new();
    private long _lastHandle;

    private StubGpuDevice(StubGpuDeviceOptions options) : base("stub", null)
    {
        _options = options;
    }

    public static StubGpuDevice Create(StubGpuDeviceOptions? options = null)
    {
        return new StubGpuDevice(options ?? StubGpuDeviceOptions.Default);
    }

    /// <summary>
    /// The command buffers submitted so far, oldest first, including the ones <see cref="GpuMemorySystem"/> submits its uploads
    /// with and the empty ones an offscreen window waits on.
    /// </summary>
    public IReadOnlyList<StubCommandBuffer> SubmittedCommandBuffers
    {
        get
        {
            lock (_lock)
            {
                return _submittedCommandBuffers.ToArray();
            }
        }
    }

    public void ClearSubmittedCommandBuffers()
    {
        lock (_lock)
        {
            _submittedCommandBuffers.Clear();
        }
    }

    /// <summary>
    /// A swapchain texture for a render context of a renderer under test, such as a <c>BasicRenderContext</c>, without a window.
    /// The format defaults to the one an offscreen window renders into.
    /// </summary>
    public SwapchainTexture CreateSwapchainTexture(ShortSize size, TextureFormat format = TextureFormat.B8G8R8A8UnormSrgb)
    {
        return new SwapchainTexture(NextHandle<SDL_GPUTexture>(), size, format);
    }

    /// <summary>The <see cref="GpuVertexBuffer.Size"/> vertices at the start of the buffer, as submitted uploads left them.</summary>
    public T[] ReadVertexBuffer<T>(GpuVertexBuffer<T> vertexBuffer) where T : unmanaged, IVertexType
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        return ReadBuffer<T>(vertexBuffer.SdlVertexBuffer, vertexBuffer.Size, nameof(GpuVertexBuffer));
    }

    /// <summary>The <see cref="GpuIndexBuffer.Size"/> indices at the start of the buffer, as submitted uploads left them, 16-bit ones widened.</summary>
    public uint[] ReadIndexBuffer(GpuIndexBuffer indexBuffer)
    {
        ArgumentNullException.ThrowIfNull(indexBuffer);
        if (indexBuffer.ElementSize == IndexElementSize.UInt32)
        {
            return ReadBuffer<uint>(indexBuffer.SdlBuffer, indexBuffer.Size, nameof(GpuIndexBuffer));
        }

        ushort[] indices = ReadBuffer<ushort>(indexBuffer.SdlBuffer, indexBuffer.Size, nameof(GpuIndexBuffer));
        return Array.ConvertAll(indices, static index => (uint)index);
    }

    /// <summary>The <see cref="GpuStorageBuffer.Size"/> elements at the start of the buffer, as submitted uploads left them.</summary>
    public T[] ReadStorageBuffer<T>(GpuStorageBuffer<T> storageBuffer) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(storageBuffer);
        return ReadBuffer<T>(storageBuffer.SdlBuffer, storageBuffer.Size, nameof(GpuStorageBuffer));
    }

    /// <summary>
    /// The bytes of one layer of the texture as a submitted upload left them, tightly packed, or zeros for a layer nothing
    /// was uploaded to, such as a color target: the stub renders nothing.
    /// </summary>
    public byte[] ReadTexture(Texture texture, uint layer = 0)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        uint layerCount = texture is TextureArray textureArray ? textureArray.LayerCount : 1u;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layer, layerCount);

        lock (_lock)
        {
            if (_textureContents.TryGetValue((HandleOf(texture.SdlGpuTexture), layer), out byte[]? contents))
            {
                return contents.ToArray();
            }
        }

        return new byte[texture.Format.CalculateSizeInBytes(texture.Size.Width, texture.Size.Height)];
    }

    internal void OnSubmitted(StubCommandBuffer commandBuffer)
    {
        lock (_lock)
        {
            foreach (StubUpload upload in commandBuffer.Uploads)
            {
                Apply(upload);
            }

            _submittedCommandBuffers.Add(commandBuffer);
        }
    }

    private void Apply(StubUpload upload)
    {
        if (upload.IsTexture)
        {
            _textureContents[(upload.Handle, upload.Layer)] = upload.Data;
            return;
        }

        // A buffer released before the upload's submission has no contents left to write.
        if (_bufferContents.TryGetValue(upload.Handle, out byte[]? contents))
        {
            upload.Data.CopyTo(contents.AsSpan());
        }
    }

    private T[] ReadBuffer<T>(Pointer<SDL_GPUBuffer> buffer, int size, string bufferName) where T : unmanaged
    {
        if (buffer.IsNull)
        {
            throw new ObjectDisposedException(bufferName);
        }

        lock (_lock)
        {
            return MemoryMarshal.Cast<byte, T>(_bufferContents[HandleOf(buffer)])[..size].ToArray();
        }
    }

    internal Pointer<T> NextHandle<T>() where T : unmanaged
    {
        unsafe
        {
            return (T*)(nint)Interlocked.Increment(ref _lastHandle);
        }
    }

    internal static nint HandleOf<T>(Pointer<T> pointer) where T : unmanaged => (nint)pointer;

    internal override string? DeviceName => "Pixely stub GPU device";

    internal override string? DriverVersion => null;

    internal override ShaderFormats GetSupportedShaderFormatsCore() => _options.ShaderFormats;

    internal override bool IsTextureFormatSupportedCore(TextureFormat format, TextureType type, TextureUsage usage) =>
        _options.IsTextureFormatSupported(format, type, usage);

    internal override bool IsSampleCountSupportedCore(TextureFormat format, SampleCount sampleCount) =>
        _options.IsSampleCountSupported(format, sampleCount);

    internal override CommandBuffer AcquireCommandBufferCore() => new StubCommandBuffer(this);

    internal override Pointer<SDL_GPUSampler> CreateSamplerCore(SamplerConfig config) => NextHandle<SDL_GPUSampler>();

    internal override Pointer<SDL_GPUTexture> CreateTextureCore(TextureType type, ShortSize size, uint layerCount, TextureFormat format, TextureUsage usage, SampleCount sampleCount) =>
        NextHandle<SDL_GPUTexture>();

    internal override Pointer<SDL_GPUBuffer> CreateBufferCore(SDL_GPUBufferUsageFlags usage, uint sizeInBytes)
    {
        Pointer<SDL_GPUBuffer> buffer = NextHandle<SDL_GPUBuffer>();
        lock (_lock)
        {
            _bufferContents.Add(HandleOf(buffer), new byte[sizeInBytes]);
        }

        return buffer;
    }

    // Stub copy passes keep their uploads themselves, so nothing writes to a transfer buffer.
    internal override Pointer<SDL_GPUTransferBuffer> CreateUploadTransferBufferCore(uint sizeInBytes) => NextHandle<SDL_GPUTransferBuffer>();

    internal override Pointer<SDL_GPUShader> CreateShaderCore(in SDL_GPUShaderCreateInfo createInfo) => NextHandle<SDL_GPUShader>();

    internal override Pointer<SDL_GPUGraphicsPipeline> CreateGraphicsPipelineCore(in SDL_GPUGraphicsPipelineCreateInfo createInfo) =>
        NextHandle<SDL_GPUGraphicsPipeline>();

    internal override Pointer<SDL_GPUComputePipeline> CreateComputePipelineCore(in SDL_GPUComputePipelineCreateInfo createInfo) =>
        NextHandle<SDL_GPUComputePipeline>();

    internal override void ReleaseTextureCore(Pointer<SDL_GPUTexture> texture)
    {
        nint handle = HandleOf(texture);
        lock (_lock)
        {
            for (uint layer = 0; _textureContents.Remove((handle, layer)); layer++)
            {
            }
        }
    }

    internal override void ReleaseBufferCore(Pointer<SDL_GPUBuffer> buffer)
    {
        lock (_lock)
        {
            _bufferContents.Remove(HandleOf(buffer));
        }
    }

    internal override void ReleaseTransferBufferCore(Pointer<SDL_GPUTransferBuffer> transferBuffer)
    {
    }

    internal override void ReleaseSamplerCore(Pointer<SDL_GPUSampler> sampler)
    {
    }

    internal override void ReleaseShaderCore(Pointer<SDL_GPUShader> shader)
    {
    }

    internal override void ReleaseGraphicsPipelineCore(Pointer<SDL_GPUGraphicsPipeline> pipeline)
    {
    }

    internal override void ReleaseComputePipelineCore(Pointer<SDL_GPUComputePipeline> pipeline)
    {
    }

    // The stub finishes work as it is submitted.
    internal override bool QueryFenceCore(Pointer<SDL_GPUFence> fence) => true;

    internal override void ReleaseFenceCore(Pointer<SDL_GPUFence> fence)
    {
    }

    internal override void WaitForFencesCore(ReadOnlySpan<GpuFence> fences, bool waitAll)
    {
    }

    internal override void DestroyCore()
    {
    }
}
