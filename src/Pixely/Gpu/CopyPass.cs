using System.Runtime.CompilerServices;
using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class CopyPass: ICopyPass
{
    private Pointer<SDL_GPUCopyPass> _sdlCopyPass;
    private readonly GpuDevice _gpuDevice;
    private List<Pointer<SDL_GPUTransferBuffer>>? _transferBuffers;

    internal CopyPass(GpuDevice gpuDevice, Pointer<SDL_GPUCopyPass> sdlCopyPass)
    {
        _sdlCopyPass = sdlCopyPass;
        _gpuDevice = gpuDevice;
    }

    public bool IsEmpty => _transferBuffers == null;

    private unsafe SDL_GPUTransferBuffer* CreateAndTrackTransferBuffer(uint sizeBytes)
    {
        SDL_GPUTransferBufferCreateInfo sdlGpuTransferBufferCreateInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = sizeBytes
        };
        SDL_GPUTransferBuffer* transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(_gpuDevice.SdlGpuDevice, &sdlGpuTransferBufferCreateInfo);
        SdlError.ThrowOnNull(transferBuffer);

        _transferBuffers ??= new();
        _transferBuffers.Add(transferBuffer);
        return transferBuffer;
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
            
            SDL_GPUTransferBuffer* transferBuffer = CreateAndTrackTransferBuffer(sizeBytes);

            TVertexType* transferBufferPointer = (TVertexType*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
            Span<TVertexType> transferBufferSpan = new Span<TVertexType>(transferBufferPointer, vertices.Length);
            
            vertices.CopyTo(transferBufferSpan);
            
            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
            
            SDL_GPUTransferBufferLocation sdlGpuTransferBufferLocation = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
            SDL_GPUBufferRegion sdlGpuBufferRegion = new SDL_GPUBufferRegion
                { buffer = rawVertexBuffer, offset = 0, size = sizeBytes };
            
            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &sdlGpuTransferBufferLocation, &sdlGpuBufferRegion, false);
            
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
            SDL_GPUTransferBuffer* transferBuffer = CreateAndTrackTransferBuffer(sizeBytes);

            TVertexType* transferBufferPointer = (TVertexType*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
            Span<TVertexType> transferBufferSpan = new Span<TVertexType>(transferBufferPointer, vertices.Length);
            
            vertices.CopyTo(transferBufferSpan);
            
            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
            
            SDL_GPUTransferBufferLocation sdlGpuTransferBufferLocation = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
            SDL_GPUBufferRegion sdlGpuBufferRegion = new SDL_GPUBufferRegion
                { buffer = vertexBuffer.SdlVertexBuffer, offset = 0, size = sizeBytes };
            
            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &sdlGpuTransferBufferLocation, &sdlGpuBufferRegion, false);
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

            SDL_GPUTransferBuffer* transferBuffer = CreateAndTrackTransferBuffer(sizeBytes);

            TIndexType* transferBufferPointer = (TIndexType*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
            Span<TIndexType> transferBufferSpan = new Span<TIndexType>(transferBufferPointer, indices.Length);

            indices.CopyTo(transferBufferSpan);

            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);

            SDL_GPUTransferBufferLocation sdlGpuTransferBufferLocation = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
            SDL_GPUBufferRegion sdlGpuBufferRegion = new SDL_GPUBufferRegion
                { buffer = rawBuffer, offset = 0, size = sizeBytes };

            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &sdlGpuTransferBufferLocation, &sdlGpuBufferRegion, false);

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
            SDL_GPUTransferBuffer* transferBuffer = CreateAndTrackTransferBuffer(sizeBytes);

            TIndexType* transferBufferPointer = (TIndexType*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
            Span<TIndexType> transferBufferSpan = new Span<TIndexType>(transferBufferPointer, indices.Length);

            indices.CopyTo(transferBufferSpan);

            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);

            SDL_GPUTransferBufferLocation sdlGpuTransferBufferLocation = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
            SDL_GPUBufferRegion sdlGpuBufferRegion = new SDL_GPUBufferRegion
                { buffer = indexBuffer.SdlBuffer, offset = 0, size = sizeBytes };

            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &sdlGpuTransferBufferLocation, &sdlGpuBufferRegion, false);
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

            SDL_GPUTransferBuffer* transferBuffer = CreateAndTrackTransferBuffer(sizeBytes);

            T* transferBufferPointer = (T*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
            Span<T> transferBufferSpan = new Span<T>(transferBufferPointer, data.Length);

            data.CopyTo(transferBufferSpan);

            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);

            SDL_GPUTransferBufferLocation sdlGpuTransferBufferLocation = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
            SDL_GPUBufferRegion sdlGpuBufferRegion = new SDL_GPUBufferRegion
                { buffer = rawBuffer, offset = 0, size = sizeBytes };

            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &sdlGpuTransferBufferLocation, &sdlGpuBufferRegion, false);

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
            SDL_GPUTransferBuffer* transferBuffer = CreateAndTrackTransferBuffer(sizeBytes);

            T* transferBufferPointer = (T*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer, false);
            Span<T> transferBufferSpan = new Span<T>(transferBufferPointer, data.Length);

            data.CopyTo(transferBufferSpan);

            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);

            SDL_GPUTransferBufferLocation sdlGpuTransferBufferLocation = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
            SDL_GPUBufferRegion sdlGpuBufferRegion = new SDL_GPUBufferRegion
                { buffer = storageBuffer.SdlBuffer, offset = 0, size = sizeBytes };

            SdlBoolInterop.SDL_UploadToGPUBuffer(_sdlCopyPass, &sdlGpuTransferBufferLocation, &sdlGpuBufferRegion, false);
        }

        storageBuffer.Size = data.Length;
    }

    public Texture CreateTexture(Image image)
    {
        SdlError.Clear();

        // TODO: check parameters
        ReadOnlySpan<byte> imageData = image.Data;
        (ushort width, ushort height) = image.Size;
        uint sizeInBytes = (uint)imageData.Length;

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

            SDL_GPUTransferBuffer* textureTransferBuffer = CreateAndTrackTransferBuffer((uint)(width * height * 4));

            ushort* textureTransfer = (ushort*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, textureTransferBuffer, false);
            SdlError.ThrowOnNull(textureTransfer);
            fixed (byte* textureDataPointer = imageData)
            {
                Buffer.MemoryCopy(textureDataPointer, textureTransfer, sizeInBytes, sizeInBytes);
            }

            SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, textureTransferBuffer);

            SDL_GPUTextureTransferInfo sdlGpuTextureTransferInfo = new SDL_GPUTextureTransferInfo
            {
                transfer_buffer = textureTransferBuffer,
                offset = 0
            };

            SDL_GPUTextureRegion sdlGpuTextureRegion = new SDL_GPUTextureRegion
            {
                texture = sdlGpuTexture,
                w = (uint)width,
                h = (uint)height,
                d = 1
            };

            SdlBoolInterop.SDL_UploadToGPUTexture(
                _sdlCopyPass,
                &sdlGpuTextureTransferInfo,
                &sdlGpuTextureRegion,
                false);

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
        uint bytesPerPixel = 4; // R8G8B8A8
        uint bytesPerLayer = (uint)(width * height * bytesPerPixel);

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
                ReadOnlySpan<byte> imageData = images[layer].Data;
                uint sizeInBytes = (uint)imageData.Length;

                SDL_GPUTransferBuffer* textureTransferBuffer = CreateAndTrackTransferBuffer(bytesPerLayer);

                byte* textureTransfer = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, textureTransferBuffer, false);
                SdlError.ThrowOnNull(textureTransfer);
                fixed (byte* textureDataPointer = imageData)
                {
                    Buffer.MemoryCopy(textureDataPointer, textureTransfer, sizeInBytes, sizeInBytes);
                }
                SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.SdlGpuDevice, textureTransferBuffer);

                SDL_GPUTextureTransferInfo sdlGpuTextureTransferInfo = new SDL_GPUTextureTransferInfo
                {
                    transfer_buffer = textureTransferBuffer,
                    offset = 0
                };

                SDL_GPUTextureRegion sdlGpuTextureRegion = new SDL_GPUTextureRegion
                {
                    texture = sdlGpuTexture,
                    layer = (uint)layer,
                    w = width,
                    h = height,
                    d = 1
                };

                SdlBoolInterop.SDL_UploadToGPUTexture(
                    _sdlCopyPass,
                    &sdlGpuTextureTransferInfo,
                    &sdlGpuTextureRegion,
                    false);
            }

            TextureArray textureArray = new TextureArray(_gpuDevice, sdlGpuTexture, size, (ushort)layerCount, TextureFormat.R8G8B8A8Unorm);
            _gpuDevice.RegisterTexture(textureArray);
            return textureArray;
        }
    }

    public void Dispose()
    {
        unsafe
        {
            SDL3.SDL_EndGPUCopyPass(_sdlCopyPass);
            _sdlCopyPass = null;

            if (_transferBuffers == null)
            {
                return;
            }

            foreach (Pointer<SDL_GPUTransferBuffer> transferBuffer in _transferBuffers)
            {
                SDL3.SDL_ReleaseGPUTransferBuffer(_gpuDevice.SdlGpuDevice, transferBuffer);
            }
            _transferBuffers.Clear();
        }
    }
}
