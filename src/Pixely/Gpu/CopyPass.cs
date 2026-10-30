using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// Records uploads into one submission's copy pass, which <see cref="GpuMemorySystem"/> keeps open until it submits. The
/// command buffer's device backs it; the validation and the creation of the buffers and textures happen here.
/// </summary>
internal abstract class CopyPass
{
    private readonly GpuDevice _gpuDevice;

    internal CopyPass(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public bool IsEmpty { get; private set; } = true;

    internal void End() => EndCore();

    private void UploadToBuffer<T>(ReadOnlySpan<T> data, Pointer<SDL_GPUBuffer> buffer, bool cycle) where T : unmanaged
    {
        UploadToBufferCore(MemoryMarshal.AsBytes(data), buffer, cycle);
        IsEmpty = false;
    }

    // Uploads into a buffer created for it, releasing the buffer if the upload fails, since nothing else holds it yet.
    private void UploadToNewBuffer<T>(ReadOnlySpan<T> data, Pointer<SDL_GPUBuffer> buffer) where T : unmanaged
    {
        try
        {
            UploadToBuffer(data, buffer, false);
        }
        catch
        {
            _gpuDevice.ReleaseBufferCore(buffer);
            throw;
        }
    }

    private void UploadToTexture(ReadOnlySpan<byte> data, Pointer<SDL_GPUTexture> texture, uint layer, uint width, uint height)
    {
        UploadToTextureCore(data, texture, layer, width, height);
        IsEmpty = false;
    }

    public GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(ReadOnlySpan<TVertexType> vertices) where TVertexType: unmanaged, IVertexType
    {
        if (vertices.Length == 0)
        {
            throw new ArgumentException("Cannot create an empty vertex buffer", nameof(vertices));
        }

        uint sizeBytes = (uint)(Unsafe.SizeOf<TVertexType>() * vertices.Length);
        Pointer<SDL_GPUBuffer> rawVertexBuffer = _gpuDevice.CreateBufferCore(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, sizeBytes);
        UploadToNewBuffer(vertices, rawVertexBuffer);

        GpuVertexBuffer<TVertexType> vertexBuffer = new GpuVertexBuffer<TVertexType>(_gpuDevice, rawVertexBuffer, vertices.Length);
        _gpuDevice.RegisterVertexBuffer(vertexBuffer);
        return vertexBuffer;
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

        UploadToBuffer(vertices, vertexBuffer.SdlVertexBuffer, true);
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
        Pointer<SDL_GPUBuffer> rawBuffer = _gpuDevice.CreateBufferCore(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, sizeBytes);
        UploadToNewBuffer(indices, rawBuffer);

        GpuIndexBuffer indexBuffer = new GpuIndexBuffer(_gpuDevice, rawBuffer, indices.Length, elementSize);
        _gpuDevice.RegisterIndexBuffer(indexBuffer);
        return indexBuffer;
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

        UploadToBuffer(indices, indexBuffer.SdlBuffer, true);
        indexBuffer.Size = indices.Length;
    }

    public GpuStorageBuffer<T> CreateStorageBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        uint sizeBytes = GpuStorageBuffer<T>.ValidateAllocationSize(data.Length, nameof(data));
        Pointer<SDL_GPUBuffer> rawBuffer = _gpuDevice.CreateBufferCore(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ, sizeBytes);
        UploadToNewBuffer(data, rawBuffer);

        GpuStorageBuffer<T> storageBuffer = new GpuStorageBuffer<T>(_gpuDevice, rawBuffer, data.Length, StorageBufferUsage.GraphicsStorageRead);
        _gpuDevice.RegisterStorageBuffer(storageBuffer);
        return storageBuffer;
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

        UploadToBuffer(data, storageBuffer.SdlBuffer, true);
        storageBuffer.Size = data.Length;
    }

    // Images hold color, which is sRGB-encoded, so sampling returns linear light. Data such as normal maps passes R8G8B8A8Unorm.
    public Texture CreateTexture(Image image)
    {
        return CreateTexture(image, TextureFormat.R8G8B8A8UnormSrgb);
    }

    public Texture CreateTexture(Image image, TextureFormat format)
    {
        ValidateImageFormat(format);
        ValidateImage(image, nameof(image));

        ReadOnlySpan<byte> imageData = image.Data;
        (ushort width, ushort height) = image.Size;

        Pointer<SDL_GPUTexture> sdlGpuTexture = _gpuDevice.CreateTextureCore(TextureType.TwoD, image.Size, 1, format, TextureUsage.Sampler, SampleCount.Count1);

        try
        {
            UploadToTexture(imageData, sdlGpuTexture, 0, width, height);
        }
        catch
        {
            _gpuDevice.ReleaseTextureCore(sdlGpuTexture);
            throw;
        }

        Texture texture = new UserTexture(_gpuDevice, sdlGpuTexture, (width, height), format, TextureUsage.Sampler);
        _gpuDevice.RegisterTexture(texture);
        return texture;
    }

    public TextureArray CreateTextureArray(ReadOnlySpan<Image> images)
    {
        return CreateTextureArray(images, TextureFormat.R8G8B8A8UnormSrgb);
    }

    public TextureArray CreateTextureArray(ReadOnlySpan<Image> images, TextureFormat format)
    {
        ValidateImageFormat(format);

        if (images.Length == 0)
        {
            throw new ArgumentException("At least one image required", nameof(images));
        }

        ShortSize size = images[0].Size;
        for (int i = 0; i < images.Length; i++)
        {
            ValidateImage(images[i], $"{nameof(images)}[{i}]");

            if (images[i].Size != size)
            {
                throw new ArgumentException(
                    $"All images must have same size. Image[0]={size}, Image[{i}]={images[i].Size}",
                    nameof(images));
            }
        }

        (ushort width, ushort height) = size;
        uint layerCount = (uint)images.Length;

        Pointer<SDL_GPUTexture> sdlGpuTexture = _gpuDevice.CreateTextureCore(TextureType.TwoDArray, size, layerCount, format, TextureUsage.Sampler, SampleCount.Count1);

        try
        {
            for (int layer = 0; layer < images.Length; layer++)
            {
                UploadToTexture(images[layer].Data, sdlGpuTexture, (uint)layer, width, height);
            }
        }
        catch
        {
            _gpuDevice.ReleaseTextureCore(sdlGpuTexture);
            throw;
        }

        TextureArray textureArray = new TextureArray(_gpuDevice, sdlGpuTexture, size, (ushort)layerCount, format, TextureUsage.Sampler);
        _gpuDevice.RegisterTexture(textureArray);
        return textureArray;
    }

    // Both formats take the image's R, G, B, A bytes as they are; the sRGB one only changes how sampling reads them.
    private static void ValidateImageFormat(TextureFormat format)
    {
        if (format != TextureFormat.R8G8B8A8Unorm && format != TextureFormat.R8G8B8A8UnormSrgb)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, $"Only {TextureFormat.R8G8B8A8Unorm} and {TextureFormat.R8G8B8A8UnormSrgb} can be created from an image.");
        }
    }

    private static void ValidateImage(Image image, string paramName)
    {
        // Pixely labels R, G, B, A bytes as Abgr8888 (SdlImageDecoder) and as Rgba8888 (RawImage producers).
        if (image.PixelFormat != PixelFormat.Abgr8888 && image.PixelFormat != PixelFormat.Rgba8888)
        {
            throw new ArgumentException($"The image's pixel format is {image.PixelFormat}; only 4-byte R, G, B, A pixels can be uploaded.", paramName);
        }

        (ushort width, ushort height) = image.Size;

        if (width == 0 || height == 0)
        {
            throw new ArgumentException($"The image is {width}x{height} pixels; neither side can be 0.", paramName);
        }

        // Two ushort sides times 4 bytes can exceed int, so the length is a long.
        long expectedLength = (long)width * height * 4;
        if (image.Data.Length != expectedLength)
        {
            throw new ArgumentException($"The image has {image.Data.Length} bytes of data, but {width}x{height} pixels need {expectedLength}.", paramName);
        }
    }

    // The backend. An update passes cycle = true and a new buffer false, see SdlCopyPass. Data is the whole upload, which
    // starts at the beginning of the buffer or covers one layer of the texture.
    internal abstract void UploadToBufferCore(ReadOnlySpan<byte> data, Pointer<SDL_GPUBuffer> buffer, bool cycle);
    internal abstract void UploadToTextureCore(ReadOnlySpan<byte> data, Pointer<SDL_GPUTexture> texture, uint layer, uint width, uint height);
    internal abstract void EndCore();
}
