using System.Runtime.CompilerServices;
using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Records uploads into the copy pass that <see cref="GpuMemorySystem"/> keeps open until it submits. One instance lives as
/// long as its <see cref="GpuMemorySystem"/> and is pointed at each new native copy pass, and the transfer buffers come
/// from the <see cref="UploadRing"/>.
/// </summary>
internal sealed class CopyPass
{
    private readonly GpuDevice _gpuDevice;
    private readonly UploadRing _uploadRing;
    private Pointer<SDL_GPUCopyPass> _sdlCopyPass;

    internal CopyPass(GpuDevice gpuDevice, UploadRing uploadRing)
    {
        _gpuDevice = gpuDevice;
        _uploadRing = uploadRing;
    }

    public bool IsEmpty { get; private set; } = true;

    internal void Begin(Pointer<SDL_GPUCopyPass> sdlCopyPass)
    {
        _sdlCopyPass = sdlCopyPass;
        IsEmpty = true;
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

    private UploadAllocation Write<T>(ReadOnlySpan<T> data, UploadAllocation allocation) where T : unmanaged
    {
        unsafe
        {
            byte* mapped = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, allocation.TransferBuffer, false);
            SdlError.ThrowOnNull(mapped);
            data.CopyTo(new Span<T>(mapped + allocation.Offset, data.Length));
            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, allocation.TransferBuffer);
        }

        return allocation;
    }

    private unsafe void UploadToBuffer<T>(ReadOnlySpan<T> data, SDL_GPUBuffer* buffer) where T : unmanaged
    {
        uint sizeBytes = (uint)(Unsafe.SizeOf<T>() * data.Length);
        UploadAllocation allocation = Write(data, _uploadRing.Reserve(sizeBytes));

        SDL_GPUTransferBufferLocation source = new SDL_GPUTransferBufferLocation { transfer_buffer = allocation.TransferBuffer, offset = allocation.Offset };
        SDL_GPUBufferRegion destination = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = sizeBytes };
        SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &source, &destination, false);

        _uploadRing.Complete(allocation);
        IsEmpty = false;
    }

    // Textures are uploaded at load time and can be large, so each gets a transfer buffer of its own rather than growing a
    // slot of the ring for good. It also leaves D3D12's 512-byte offset alignment for texture copies to the offset 0 of a new buffer.
    private unsafe void UploadToTexture(ReadOnlySpan<byte> data, SDL_GPUTexture* texture, uint layer, uint width, uint height)
    {
        UploadAllocation allocation = Write(data, _uploadRing.ReserveTemporary((uint)data.Length));

        SDL_GPUTextureTransferInfo source = new SDL_GPUTextureTransferInfo { transfer_buffer = allocation.TransferBuffer, offset = allocation.Offset };
        SDL_GPUTextureRegion destination = new SDL_GPUTextureRegion { texture = texture, layer = layer, w = width, h = height, d = 1 };
        SdlBoolInterop.SDL_UploadToGPUTexture(_sdlCopyPass, &source, &destination, false);

        _uploadRing.Complete(allocation);
        IsEmpty = false;
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
