namespace Pixely.Gpu;

public interface IRenderPass: IDisposable
{
    void BindGraphicsPipeline(GraphicsPipeline graphicsPipeline);

    void BindVertexBuffer<TVertexType>(uint slot, GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType;

    void BindVertexBuffer<TVertexType>(GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType;

    void BindIndexBuffer(GpuIndexBuffer buffer);

    void BindFragmentSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0);
    void BindFragmentSampler(Texture texture, Sampler sampler);
    void BindFragmentSamplerArray(TextureArray textureArray, Sampler sampler, uint slot = 0);

    void BindVertexStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0);
    void BindVertexStorageBuffer(GpuStorageBuffer buffer, uint slot = 0);
    void BindFragmentStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0);
    void BindFragmentStorageBuffer(GpuStorageBuffer buffer, uint slot = 0);

    void SetStencilReference(byte reference);

    /// <summary>
    /// Restricts subsequent draws to <paramref name="scissor"/>, in render target pixels.
    /// The rectangle is clipped to the render target, so a larger one simply restricts nothing.
    /// </summary>
    void SetScissor(Rectangle scissor);

    /// <summary>
    /// Restores the scissor to cover the whole render target.
    /// </summary>
    void ClearScissor();

    void DrawPrimitive();
    void DrawPrimitiveInstanced(uint instanceCount);
    void DrawPrimitiveInstanced(uint instanceCount, uint firstInstance);
    void DrawIndexedPrimitive();
    void DrawIndexedPrimitive(uint indexCount, uint firstIndex = 0, int vertexOffset = 0);
    void DrawIndexedPrimitiveInstanced(uint instanceCount);
    void DrawIndexedPrimitiveInstanced(uint instanceCount, uint firstInstance);
    void DrawIndexedPrimitiveInstanced(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance);
}
