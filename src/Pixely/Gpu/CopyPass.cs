using System.Numerics;
using System.Runtime.CompilerServices;
using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Records uploads into the copy pass that <see cref="GpuMemorySystem"/> keeps open until it submits. One instance lives as
/// long as its <see cref="GpuMemorySystem"/> and is pointed at each new native copy pass. Buffer uploads share one transfer
/// buffer, which SDL cycles once per submission, so a steady stream of buffer updates does not create a transfer buffer per
/// update.
/// </summary>
internal sealed class CopyPass : IDisposable
{
    // The most the shared transfer buffer grows to. A larger upload gets a transfer buffer of its own.
    private const uint MaxTransferBufferCapacity = 1 << 20;

    private const uint MinTransferBufferCapacity = 64 << 10;

    // WebGPU needs offsets that are multiples of 4; 16 also keeps every vertex and storage element aligned.
    private const uint Alignment = 16;

    private readonly GpuDevice _gpuDevice;
    private Pointer<SDL_GPUCopyPass> _sdlCopyPass;

    private Pointer<SDL_GPUTransferBuffer> _transferBuffer;
    private uint _transferBufferCapacity;
    private uint _transferBufferOffset;

    // The first map of a submission cycles, so that SDL hands out a copy no submission in flight still reads. Later maps must
    // not: the submission's own uploads already mark the buffer as in use, so cycling would create a copy per upload.
    private bool _cycleOnNextMap;

    internal CopyPass(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public bool IsEmpty { get; private set; } = true;

    internal void Begin(Pointer<SDL_GPUCopyPass> sdlCopyPass)
    {
        _sdlCopyPass = sdlCopyPass;
        IsEmpty = true;
        _transferBufferOffset = 0;
        _cycleOnNextMap = true;
    }

    internal void End()
    {
        if (_sdlCopyPass.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_EndGPUCopyPass(_sdlCopyPass);
        }
        _sdlCopyPass = Pointer<SDL_GPUCopyPass>.Null;
    }

    public void Dispose()
    {
        ReleaseTransferBuffer(_transferBuffer);
        _transferBuffer = Pointer<SDL_GPUTransferBuffer>.Null;
        _transferBufferCapacity = 0;
    }

    private unsafe void UploadToBuffer<T>(ReadOnlySpan<T> data, SDL_GPUBuffer* buffer) where T : unmanaged
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<T>() * data.Length);
        SDL_GPUBufferRegion destination = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = sizeBytes };
        uint offset = AlignUp(_transferBufferOffset);
        bool fits = offset + (ulong)sizeBytes <= _transferBufferCapacity;

        // A full buffer at its largest keeps its size: growing it no further, the upload gets a transfer buffer of its own.
        if (sizeBytes > MaxTransferBufferCapacity || (!fits && _transferBufferCapacity == MaxTransferBufferCapacity))
        {
            Pointer<SDL_GPUTransferBuffer> temporary = CreateFilledTransferBuffer(data);
            SDL_GPUTransferBufferLocation temporarySource = new SDL_GPUTransferBufferLocation { transfer_buffer = temporary, offset = 0 };
            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &temporarySource, &destination, false);
            ReleaseTransferBuffer(temporary);
        }
        else
        {
            if (!fits)
            {
                GrowTransferBuffer(sizeBytes);
                offset = 0;
            }

            Write(data, _transferBuffer, offset, _cycleOnNextMap);
            _cycleOnNextMap = false;
            _transferBufferOffset = offset + sizeBytes;

            SDL_GPUTransferBufferLocation source = new SDL_GPUTransferBufferLocation { transfer_buffer = _transferBuffer, offset = offset };
            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &source, &destination, false);
        }

        IsEmpty = false;
    }

    // Textures are uploaded at load time and can be large, so each gets a transfer buffer of its own rather than growing the
    // shared one for good. It also leaves D3D12's 512-byte offset alignment for texture copies to the offset 0 of a new buffer.
    private unsafe void UploadToTexture(ReadOnlySpan<byte> data, SDL_GPUTexture* texture, uint layer, uint width, uint height)
    {
        Pointer<SDL_GPUTransferBuffer> temporary = CreateFilledTransferBuffer(data);

        SDL_GPUTextureTransferInfo source = new SDL_GPUTextureTransferInfo { transfer_buffer = temporary, offset = 0 };
        SDL_GPUTextureRegion destination = new SDL_GPUTextureRegion { texture = texture, layer = layer, w = width, h = height, d = 1 };
        SdlBoolInterop.SDL_UploadToGPUTexture(_sdlCopyPass, &source, &destination, false);

        ReleaseTransferBuffer(temporary);
        IsEmpty = false;
    }

    private void GrowTransferBuffer(uint size)
    {
        uint capacity = Math.Min(MaxTransferBufferCapacity, Math.Max(Math.Max(MinTransferBufferCapacity, _transferBufferCapacity * 2), BitOperations.RoundUpToPowerOf2(size)));

        // Uploads already recorded from the old buffer still read it, which SDL allows: a released buffer is freed only once
        // the GPU is done with it. The new buffer is created first, so a failed create leaves the old one in place.
        Pointer<SDL_GPUTransferBuffer> transferBuffer = CreateTransferBuffer(capacity);
        ReleaseTransferBuffer(_transferBuffer);
        _transferBuffer = transferBuffer;
        _transferBufferCapacity = capacity;
    }

    private Pointer<SDL_GPUTransferBuffer> CreateFilledTransferBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        Pointer<SDL_GPUTransferBuffer> transferBuffer = CreateTransferBuffer((uint)(Unsafe.SizeOf<T>() * data.Length));
        // Not handled: a failed map leaks this transfer buffer.
        Write(data, transferBuffer, 0, false);
        return transferBuffer;
    }

    private void Write<T>(ReadOnlySpan<T> data, Pointer<SDL_GPUTransferBuffer> transferBuffer, uint offset, bool cycle) where T : unmanaged
    {
        unsafe
        {
            byte* mapped = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, cycle);
            SdlError.ThrowOnNull(mapped);
            data.CopyTo(new Span<T>(mapped + offset, data.Length));
            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
        }
    }

    private Pointer<SDL_GPUTransferBuffer> CreateTransferBuffer(uint size)
    {
        unsafe
        {
            SDL_GPUTransferBufferCreateInfo createInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                size = size
            };
            Pointer<SDL_GPUTransferBuffer> transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(_gpuDevice.SdlGpuDevice, &createInfo);
            SdlError.ThrowOnNull(transferBuffer);
            return transferBuffer;
        }
    }

    private void ReleaseTransferBuffer(Pointer<SDL_GPUTransferBuffer> transferBuffer)
    {
        if (transferBuffer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
        }
    }

    private static uint AlignUp(uint offset)
    {
        return (offset + Alignment - 1) & ~(Alignment - 1);
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(ReadOnlySpan<TVertexType> vertices) where TVertexType: unmanaged, IVertexType
    {
        if (vertices.Length == 0)
        {
            throw new ArgumentException("Cannot create an empty vertex buffer", nameof(vertices));
        }

        uint sizeBytes = (uint)(Unsafe.SizeOf<TVertexType>() * vertices.Length);
        unsafe
        {
            SDL_GPUBufferCreateInfo sdlGpuBufferCreateInfo = new SDL_GPUBufferCreateInfo()
            {
                usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
                size = sizeBytes
            };

            SDL_GPUBuffer* rawVertexBuffer = SDL3.SDL_CreateGPUBuffer(_gpuDevice.SdlGpuDevice, &sdlGpuBufferCreateInfo);
            UploadToBuffer(vertices, rawVertexBuffer);

            GpuVertexBuffer<TVertexType> vertexBuffer = new GpuVertexBuffer<TVertexType>(_gpuDevice, rawVertexBuffer, vertices.Length);
            _gpuDevice.RegisterVertexBuffer(vertexBuffer);
            return vertexBuffer;
        }
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(Shape<TVertexType> shape)
        where TVertexType : unmanaged, IVertexType
    {
        return CreateVertexBuffer((ReadOnlySpan<TVertexType>)shape);
    }

    public void UpdateVertexBuffer<TVertexType>(GpuVertexBuffer<TVertexType> vertexBuffer, ReadOnlySpan<TVertexType> vertices) where TVertexType: unmanaged, IVertexType
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<TVertexType>() * vertices.Length);

        if (sizeBytes == 0)
        {
            throw new ArgumentException($"{nameof(vertices.Length)} is 0");
        }

        uint bufferSizeBytes = (uint)vertexBuffer.SizeInBytes;

        if (sizeBytes > bufferSizeBytes)
        {
            throw new ArgumentException($"{nameof(vertices)} cannot fit to {nameof(vertexBuffer)}");
        }

        unsafe
        {
            UploadToBuffer(vertices, vertexBuffer.SdlVertexBuffer);
        }

        vertexBuffer.Size = vertices.Length;
    }

    public GpuIndexBuffer CreateIndexBuffer(ReadOnlySpan<ushort> indices)
    {
        return CreateIndexBuffer(indices, IndexElementSize.UInt16);
    }

    public GpuIndexBuffer CreateIndexBuffer(ReadOnlySpan<uint> indices)
    {
        return CreateIndexBuffer(indices, IndexElementSize.UInt32);
    }

    private GpuIndexBuffer CreateIndexBuffer<TIndexType>(ReadOnlySpan<TIndexType> indices, IndexElementSize elementSize)
        where TIndexType : unmanaged
    {
        if (indices.Length == 0)
        {
            throw new ArgumentException("Cannot create an empty index buffer", nameof(indices));
        }

        uint sizeBytes = (uint)(Unsafe.SizeOf<TIndexType>() * indices.Length);
        unsafe
        {
            SDL_GPUBufferCreateInfo sdlGpuBufferCreateInfo = new SDL_GPUBufferCreateInfo()
            {
                usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX,
                size = sizeBytes
            };

            SDL_GPUBuffer* rawBuffer = SDL3.SDL_CreateGPUBuffer(_gpuDevice.SdlGpuDevice, &sdlGpuBufferCreateInfo);
            UploadToBuffer(indices, rawBuffer);

            GpuIndexBuffer indexBuffer = new GpuIndexBuffer(_gpuDevice, rawBuffer, indices.Length, elementSize);
            _gpuDevice.RegisterIndexBuffer(indexBuffer);
            return indexBuffer;
        }
    }

    public void UpdateIndexBuffer(GpuIndexBuffer indexBuffer, ReadOnlySpan<ushort> indices)
    {
        UpdateIndexBuffer(indexBuffer, indices, IndexElementSize.UInt16);
    }

    public void UpdateIndexBuffer(GpuIndexBuffer indexBuffer, ReadOnlySpan<uint> indices)
    {
        UpdateIndexBuffer(indexBuffer, indices, IndexElementSize.UInt32);
    }

    private void UpdateIndexBuffer<TIndexType>(GpuIndexBuffer indexBuffer, ReadOnlySpan<TIndexType> indices, IndexElementSize elementSize)
        where TIndexType : unmanaged
    {
        if (indexBuffer.ElementSize != elementSize)
        {
            throw new ArgumentException($"Cannot update a {indexBuffer.ElementSize} index buffer with {elementSize} indices.", nameof(indices));
        }

        uint sizeBytes = (uint)(Unsafe.SizeOf<TIndexType>() * indices.Length);

        if (sizeBytes == 0)
        {
            throw new ArgumentException("Cannot update index buffer with empty data", nameof(indices));
        }

        uint bufferSizeBytes = (uint)indexBuffer.SizeInBytes;

        if (sizeBytes > bufferSizeBytes)
        {
            throw new ArgumentException($"{nameof(indices)} cannot fit to {nameof(indexBuffer)}");
        }

        unsafe
        {
            UploadToBuffer(indices, indexBuffer.SdlBuffer);
        }

        indexBuffer.Size = indices.Length;
    }

    public GpuStorageBuffer<T> CreateStorageBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.Length == 0)
        {
            throw new ArgumentException("Cannot create an empty storage buffer", nameof(data));
        }

        uint sizeBytes = (uint)(Unsafe.SizeOf<T>() * data.Length);
        unsafe
        {
            SDL_GPUBufferCreateInfo sdlGpuBufferCreateInfo = new SDL_GPUBufferCreateInfo()
            {
                usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ,
                size = sizeBytes
            };

            SDL_GPUBuffer* rawBuffer = SDL3.SDL_CreateGPUBuffer(_gpuDevice.SdlGpuDevice, &sdlGpuBufferCreateInfo);
            UploadToBuffer(data, rawBuffer);

            GpuStorageBuffer<T> storageBuffer = new GpuStorageBuffer<T>(_gpuDevice, rawBuffer, data.Length);
            _gpuDevice.RegisterStorageBuffer(storageBuffer);
            return storageBuffer;
        }
    }

    public void UpdateStorageBuffer<T>(GpuStorageBuffer<T> storageBuffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<T>() * data.Length);

        if (sizeBytes == 0)
        {
            throw new ArgumentException($"{nameof(data.Length)} is 0");
        }

        uint bufferSizeBytes = (uint)storageBuffer.SizeInBytes;

        if (sizeBytes > bufferSizeBytes)
        {
            throw new ArgumentException($"{nameof(data)} cannot fit to {nameof(storageBuffer)}");
        }

        unsafe
        {
            UploadToBuffer(data, storageBuffer.SdlBuffer);
        }

        storageBuffer.Size = data.Length;
    }

    public Texture CreateTexture(Image image)
    {
        SdlError.Clear();

        // TODO: check parameters
        ReadOnlySpan<byte> imageData = image.Data;
        (ushort width, ushort height) = image.Size;

        unsafe
        {
            SDL_GPUTextureCreateInfo sdlGpuTextureCreateInfo = new SDL_GPUTextureCreateInfo
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
                width = (uint)width,
                height = (uint)height,
                layer_count_or_depth = 1,
                num_levels = 1,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER
            };
            Pointer<SDL_GPUTexture> sdlGpuTexture = SDL3.SDL_CreateGPUTexture(_gpuDevice.SdlGpuDevice, &sdlGpuTextureCreateInfo);
            SdlError.ThrowOnNull(sdlGpuTexture);

            UploadToTexture(imageData, sdlGpuTexture, 0, width, height);

            Texture texture = new UserTexture(_gpuDevice, sdlGpuTexture, (width, height), TextureFormat.R8G8B8A8Unorm);
            _gpuDevice.RegisterTexture(texture);
            return texture;
        }
    }

    public TextureArray CreateTextureArray(ReadOnlySpan<Image> images)
    {
        if (images.Length == 0)
        {
            throw new ArgumentException("At least one image required", nameof(images));
        }

        ShortSize size = images[0].Size;
        for (int i = 1; i < images.Length; i++)
        {
            if (images[i].Size != size)
            {
                throw new ArgumentException(
                    $"All images must have same size. Image[0]={size}, Image[{i}]={images[i].Size}",
                    nameof(images));
            }
        }

        SdlError.Clear();

        (ushort width, ushort height) = size;
        uint layerCount = (uint)images.Length;

        unsafe
        {
            SDL_GPUTextureCreateInfo sdlGpuTextureCreateInfo = new SDL_GPUTextureCreateInfo
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D_ARRAY,
                format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
                width = width,
                height = height,
                layer_count_or_depth = layerCount,
                num_levels = 1,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER
            };
            Pointer<SDL_GPUTexture> sdlGpuTexture = SDL3.SDL_CreateGPUTexture(_gpuDevice.SdlGpuDevice, &sdlGpuTextureCreateInfo);
            SdlError.ThrowOnNull(sdlGpuTexture);

            for (int layer = 0; layer < images.Length; layer++)
            {
                UploadToTexture(images[layer].Data, sdlGpuTexture, (uint)layer, width, height);
            }

            TextureArray textureArray = new TextureArray(_gpuDevice, sdlGpuTexture, size, (ushort)layerCount, TextureFormat.R8G8B8A8Unorm);
            _gpuDevice.RegisterTexture(textureArray);
            return textureArray;
        }
    }
}
