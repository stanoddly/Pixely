using System.Runtime.CompilerServices;
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

    public GpuMemorySystem(GpuDevice gpuDevice) : this(gpuDevice, false)
    {
    }

    internal GpuMemorySystem(GpuDevice gpuDevice, bool waitsForUploads)
    {
        _gpuDevice = gpuDevice;
        _uploadTransferBuffer = new UploadTransferBuffer(gpuDevice);
        WaitsForUploads = waitsForUploads;
    }

    // Whether Submit() returns only once the uploads have finished on the GPU, see PixelyConfig.UploadWait.
    internal bool WaitsForUploads { get; }

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

    /// <summary>Creates a storage buffer of <paramref name="length"/> elements with undefined contents, without an upload. A compute shader writes it, or <see cref="UpdateStorageBuffer{T}"/> fills it.</summary>
    public GpuStorageBuffer<T> CreateStorageBuffer<T>(int length, StorageBufferUsage usage) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        long sizeBytes = (long)Unsafe.SizeOf<T>() * length;
        if (sizeBytes > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, $"A storage buffer of {length} elements of {Unsafe.SizeOf<T>()} bytes exceeds {uint.MaxValue} bytes.");
        }

        const StorageBufferUsage allUsages = StorageBufferUsage.GraphicsStorageRead | StorageBufferUsage.ComputeStorageRead | StorageBufferUsage.ComputeStorageWrite;
        if (usage == 0 || (usage & ~allUsages) != 0)
        {
            throw new ArgumentException($"A storage buffer needs one or more of {allUsages}, but the usage is '{usage}'.", nameof(usage));
        }

        unsafe
        {
            SDL_GPUBufferCreateInfo sdlGpuBufferCreateInfo = new SDL_GPUBufferCreateInfo()
            {
                usage = (SDL_GPUBufferUsageFlags)usage,
                size = (uint)sizeBytes
            };

            Pointer<SDL_GPUBuffer> rawBuffer = SDL3.SDL_CreateGPUBuffer(_gpuDevice.SdlGpuDevice, &sdlGpuBufferCreateInfo);
            SdlError.ThrowOnNull(rawBuffer);

            GpuStorageBuffer<T> storageBuffer = new GpuStorageBuffer<T>(_gpuDevice, rawBuffer, length, usage);
            _gpuDevice.RegisterStorageBuffer(storageBuffer);
            return storageBuffer;
        }
    }

    public void UpdateStorageBuffer<T>(GpuStorageBuffer<T> storageBuffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        GetOrCreateCopyPass().UpdateStorageBuffer(storageBuffer, data);
    }

    /// <summary>Creates a sampled <see cref="TextureFormat.R8G8B8A8UnormSrgb"/> texture from 4-byte R, G, B, A pixels, so sampling returns linear light.</summary>
    public Texture CreateTexture(Image image)
    {
        return GetOrCreateCopyPass().CreateTexture(image);
    }

    /// <summary>Creates a sampled texture with <paramref name="format"/>, <see cref="TextureFormat.R8G8B8A8Unorm"/> for data or <see cref="TextureFormat.R8G8B8A8UnormSrgb"/> for color, from 4-byte R, G, B, A pixels.</summary>
    public Texture CreateTexture(Image image, TextureFormat format)
    {
        return GetOrCreateCopyPass().CreateTexture(image, format);
    }

    /// <summary>Creates a sampled <see cref="TextureFormat.R8G8B8A8UnormSrgb"/> texture array, one layer per image.</summary>
    public TextureArray CreateTextureArray(ReadOnlySpan<Image> images)
    {
        return GetOrCreateCopyPass().CreateTextureArray(images);
    }

    /// <summary>Creates a sampled texture array with <paramref name="format"/>, <see cref="TextureFormat.R8G8B8A8Unorm"/> for data or <see cref="TextureFormat.R8G8B8A8UnormSrgb"/> for color, one layer per image.</summary>
    public TextureArray CreateTextureArray(ReadOnlySpan<Image> images, TextureFormat format)
    {
        return GetOrCreateCopyPass().CreateTextureArray(images, format);
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

    // The V3DV driver of the Raspberry Pi 5 bins a draw, sorting its triangles into screen tiles, before an upload submitted ahead
    // of it has finished, although SDL ends the copy with the barrier Vulkan requires. A buffer updated every frame then leaves
    // holes in the tiles its triangles have moved into. Automatic waits there only: elsewhere a wait just stalls the CPU.
    internal static bool ShouldWaitForUploads(UploadWait uploadWait, string? nativeDriverName)
    {
        return uploadWait switch
        {
            UploadWait.Automatic => nativeDriverName != null && nativeDriverName.StartsWith("V3DV", StringComparison.Ordinal),
            UploadWait.On => true,
            UploadWait.Off => false,
            _ => throw new ArgumentOutOfRangeException(nameof(uploadWait), uploadWait, "Unknown upload wait")
        };
    }

    /// <summary>
    /// Submits the uploads recorded since the last submit. Uploads run only once this is called: a command buffer submitted
    /// earlier that reads a buffer updated since the last submit reads undefined contents. Render coordinators call this
    /// every frame, after the renderers and before the frame's own command buffer, except in the browser on a frame that gets
    /// no swapchain texture: there the uploads wait for the next drawn frame. Call it yourself before submitting any
    /// other command buffer that reads updated buffers. An app without window rendering must always call it itself: until
    /// then every update of a buffer cycles it into a new copy, because the unsubmitted uploads keep the earlier copies in use.
    /// With <see cref="PixelyConfig.UploadWait"/> on, which is the default on the Raspberry Pi's V3DV driver, it returns only
    /// once the uploads have finished on the GPU.
    /// </summary>
    public void Submit()
    {
        if (_sdlCommandBuffer.IsNull)
        {
            return;
        }

        _copyPass?.End();
        _copyPass = null;
        Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer = _sdlCommandBuffer;
        _sdlCommandBuffer = Pointer<SDL_GPUCommandBuffer>.Null;

#if !BROWSER
        // The fence signals once the uploads have finished, so the frame submitted after this cannot start before them.
        if (WaitsForUploads)
        {
            Pointer<SDL_GPUFence> sdlFence;
            unsafe
            {
                sdlFence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(sdlCommandBuffer);
            }

            SdlError.ThrowOnNull(sdlFence, "SDL_SubmitGPUCommandBufferAndAcquireFence");
            using (GpuFence fence = new(_gpuDevice, sdlFence))
            {
                _gpuDevice.WaitForFences([fence]);
            }

            return;
        }
#endif

        unsafe
        {
            SdlError.ThrowOnFalse(SDL3.SDL_SubmitGPUCommandBuffer(sdlCommandBuffer), "SDL_SubmitGPUCommandBuffer");
        }
    }
}
