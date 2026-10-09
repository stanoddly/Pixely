using System.Runtime.CompilerServices;
using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Records uploads into one submission's native copy pass, which <see cref="GpuMemorySystem"/> keeps open until it submits.
/// Buffer uploads share the <see cref="UploadTransferBuffer"/> that outlives the pass, which SDL cycles once per submission, so
/// a steady stream of buffer updates does not create a transfer buffer per update.
/// </summary>
internal sealed class CopyPass
{
    // WebGPU needs offsets that are multiples of 4; 16 also keeps every vertex and storage element aligned.
    private const uint Alignment = 16;

    private readonly GpuDevice _gpuDevice;
    private readonly Pointer<SDL_GPUCopyPass> _sdlCopyPass;
    private readonly UploadTransferBuffer _transferBuffer;

    private uint _transferBufferOffset;

    internal CopyPass(GpuDevice gpuDevice, Pointer<SDL_GPUCopyPass> sdlCopyPass, UploadTransferBuffer transferBuffer)
    {
        _gpuDevice = gpuDevice;
        _sdlCopyPass = sdlCopyPass;
        _transferBuffer = transferBuffer;
    }

    public bool IsEmpty { get; private set; } = true;

    internal void End()
    {
        unsafe
        {
            SDL3.SDL_EndGPUCopyPass(_sdlCopyPass);
        }
    }

    // An update passes cycle = true: without it, the upload overwrites a buffer that an earlier submission may still read, such
    // as the previous frame or another window in this frame. SDL then writes into a copy of the buffer when a command buffer in
    // flight or still recording uses it, and overwrites in place otherwise. A new buffer passes false: nothing reads it yet.
    // Cycling has these costs:
    // - The copy starts undefined, so an update replaces the whole contents, not a prefix.
    // - A pass that bound the buffer before the update keeps the previous copy until it binds the buffer again.
    // - Each copy has the buffer's full size: one per submission in flight, plus one per extra update in the same submission.
    //   SDL keeps the copies until the buffer is released, and the GPU memory tracking does not count them.
    // - In the browser, uploads are already ordered against earlier submissions, so the copies there prevent no race.
    private unsafe void UploadToBuffer<T>(ReadOnlySpan<T> data, SDL_GPUBuffer* buffer, bool cycle) where T : unmanaged
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<T>() * data.Length);
        SDL_GPUBufferRegion destination = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = sizeBytes };
        uint offset = AlignUp(_transferBufferOffset);
        bool fits = offset + (ulong)sizeBytes <= _transferBuffer.Capacity;

        // A full buffer at its largest keeps its size: growing it no further, the upload gets a transfer buffer of its own.
        if (sizeBytes > UploadTransferBuffer.MaxCapacity || (!fits && _transferBuffer.Capacity == UploadTransferBuffer.MaxCapacity))
        {
            Pointer<SDL_GPUTransferBuffer> temporary = CreateFilledTransferBuffer(data);
            SDL_GPUTransferBufferLocation temporarySource = new SDL_GPUTransferBufferLocation { transfer_buffer = temporary, offset = 0 };
            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &temporarySource, &destination, cycle);
            _gpuDevice.ReleaseTransferBuffer(temporary);
        }
        else
        {
            if (!fits)
            {
                _transferBuffer.Grow(sizeBytes);
                offset = 0;
            }

            // Only a write at offset 0 cycles: the first of a submission, so that SDL hands out a copy no submission in flight
            // still reads, or the first into a grown buffer, which nothing uses yet, so SDL does not copy it. Later writes must
            // not cycle: the submission's own uploads already mark the buffer as in use, so cycling would create a copy per upload.
            Write(data, _transferBuffer.SdlTransferBuffer, offset, offset == 0);
            _transferBufferOffset = offset + sizeBytes;

            SDL_GPUTransferBufferLocation source = new SDL_GPUTransferBufferLocation { transfer_buffer = _transferBuffer.SdlTransferBuffer, offset = offset };
            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &source, &destination, cycle);
        }

        IsEmpty = false;
    }

    // Uploads into a buffer created for it, releasing the buffer if the upload fails, since nothing else holds it yet.
    private unsafe void UploadToNewBuffer<T>(ReadOnlySpan<T> data, SDL_GPUBuffer* buffer) where T : unmanaged
    {
        try
        {
            UploadToBuffer(data, buffer, false);
        }
        catch
        {
            SDL3.SDL_ReleaseGPUBuffer(_gpuDevice.SdlGpuDevice, buffer);
            throw;
        }
    }

    // Textures are uploaded at load time and can be large, so each gets a transfer buffer of its own rather than growing the
    // shared one for good. It also leaves D3D12's 512-byte offset alignment for texture copies to the offset 0 of a new buffer.
    private unsafe void UploadToTexture(ReadOnlySpan<byte> data, SDL_GPUTexture* texture, uint layer, uint width, uint height)
    {
        Pointer<SDL_GPUTransferBuffer> temporary = CreateFilledTransferBuffer(data);

        SDL_GPUTextureTransferInfo source = new SDL_GPUTextureTransferInfo { transfer_buffer = temporary, offset = 0 };
        SDL_GPUTextureRegion destination = new SDL_GPUTextureRegion { texture = texture, layer = layer, w = width, h = height, d = 1 };
        SdlBoolInterop.SDL_UploadToGPUTexture(_sdlCopyPass, &source, &destination, false);

        _gpuDevice.ReleaseTransferBuffer(temporary);
        IsEmpty = false;
    }

    private Pointer<SDL_GPUTransferBuffer> CreateFilledTransferBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        Pointer<SDL_GPUTransferBuffer> transferBuffer = _gpuDevice.CreateUploadTransferBuffer((uint)(Unsafe.SizeOf<T>() * data.Length));
        try
        {
            Write(data, transferBuffer, 0, false);
        }
        catch
        {
            _gpuDevice.ReleaseTransferBuffer(transferBuffer);
            throw;
        }

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
            UploadToNewBuffer(vertices, rawVertexBuffer);

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
            UploadToBuffer(vertices, vertexBuffer.SdlVertexBuffer, true);
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
            UploadToNewBuffer(indices, rawBuffer);

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
            UploadToBuffer(indices, indexBuffer.SdlBuffer, true);
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
            UploadToNewBuffer(data, rawBuffer);

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
            UploadToBuffer(data, storageBuffer.SdlBuffer, true);
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

            try
            {
                UploadToTexture(imageData, sdlGpuTexture, 0, width, height);
            }
            catch
            {
                SDL3.SDL_ReleaseGPUTexture(_gpuDevice.SdlGpuDevice, sdlGpuTexture);
                throw;
            }

            Texture texture = new UserTexture(_gpuDevice, sdlGpuTexture, (width, height), TextureFormat.R8G8B8A8Unorm, TextureUsage.Sampler);
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

            try
            {
                for (int layer = 0; layer < images.Length; layer++)
                {
                    UploadToTexture(images[layer].Data, sdlGpuTexture, (uint)layer, width, height);
                }
            }
            catch
            {
                SDL3.SDL_ReleaseGPUTexture(_gpuDevice.SdlGpuDevice, sdlGpuTexture);
                throw;
            }

            TextureArray textureArray = new TextureArray(_gpuDevice, sdlGpuTexture, size, (ushort)layerCount, TextureFormat.R8G8B8A8Unorm, TextureUsage.Sampler);
            _gpuDevice.RegisterTexture(textureArray);
            return textureArray;
        }
    }
}
