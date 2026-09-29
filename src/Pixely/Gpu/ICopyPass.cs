using Pixely.Content;

namespace Pixely.Gpu;

public interface ICopyPass: IDisposable
{
    bool IsEmpty { get; }
    GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(ReadOnlySpan<TVertexType> vertices) where TVertexType: unmanaged, IVertexType;

    GpuVertexBuffer<TVertexType> CreateVertexBuffer<TVertexType>(Shape<TVertexType> shape)
        where TVertexType : unmanaged, IVertexType;

    /// <summary>Writes <paramref name="vertices"/> at the start of the buffer. The rest of the buffer is undefined afterwards. A render pass that bound the buffer before the update keeps drawing the previous contents until it binds the buffer again.</summary>
    void UpdateVertexBuffer<TVertexType>(GpuVertexBuffer<TVertexType> vertexBuffer, ReadOnlySpan<TVertexType> vertices) where TVertexType: unmanaged, IVertexType;

    GpuIndexBuffer CreateIndexBuffer(ReadOnlySpan<ushort> indices);

    GpuIndexBuffer CreateIndexBuffer(ReadOnlySpan<uint> indices);

    /// <summary>Writes <paramref name="indices"/> at the start of the buffer. The rest of the buffer is undefined afterwards. A render pass that bound the buffer before the update keeps drawing the previous contents until it binds the buffer again.</summary>
    void UpdateIndexBuffer(GpuIndexBuffer indexBuffer, ReadOnlySpan<ushort> indices);

    /// <summary>Writes <paramref name="indices"/> at the start of the buffer. The rest of the buffer is undefined afterwards. A render pass that bound the buffer before the update keeps drawing the previous contents until it binds the buffer again.</summary>
    void UpdateIndexBuffer(GpuIndexBuffer indexBuffer, ReadOnlySpan<uint> indices);

    GpuStorageBuffer<T> CreateStorageBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged;

    /// <summary>Writes <paramref name="data"/> at the start of the buffer. The rest of the buffer is undefined afterwards. A render pass that bound the buffer before the update keeps drawing the previous contents until it binds the buffer again.</summary>
    void UpdateStorageBuffer<T>(GpuStorageBuffer<T> storageBuffer, ReadOnlySpan<T> data) where T : unmanaged;

    Texture CreateTexture(Image image);
    TextureArray CreateTextureArray(ReadOnlySpan<Image> images);
}
