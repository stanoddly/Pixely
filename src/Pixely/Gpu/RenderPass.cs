using Pixely.ShaderCommon;

namespace Pixely.Gpu;

/// <summary>
/// Records draws into a command buffer's render pass until it is disposed. The command buffer's device backs it; the
/// validation runs before every call reaches the backend.
/// </summary>
public abstract class RenderPass : IDisposable
{
    private bool _isEnded;
    // Both counts are taken at bind time: an update after the bind cycles the buffer into a new copy and changes its Size,
    // while this pass keeps drawing from the copy it bound.
    private uint _verticesCount = 0;
    private uint _indexCount;
    private RenderPassValidator _validator;

    private ShaderBindingCounts _fragmentShaderBindingCounts;
    private ShaderBindingCounts _vertexShaderBindingCounts;

    public ShaderBindingCounts FragmentShaderBindingCounts => _fragmentShaderBindingCounts;
    public ShaderBindingCounts VertexShaderBindingCounts => _vertexShaderBindingCounts;
    public DepthBufferFormat DepthBufferFormat { get; }

    /// <summary>
    /// The sample count every attachment of this pass has, which the pipelines it binds must also have.
    /// </summary>
    public SampleCount SampleCount { get; }

    /// <summary>
    /// The area every attachment of this pass covers, which is the smallest of them.
    /// It is what <see cref="SetScissor"/> clips to and what <see cref="ClearScissor"/> restores.
    /// </summary>
    public ShortSize TargetSize { get; }

    internal RenderPass(
        CommandBuffer commandBuffer,
        DepthBufferFormat depthBufferFormat,
        SampleCount sampleCount,
        ShortSize targetSize)
    {
        DepthBufferFormat = depthBufferFormat;
        SampleCount = sampleCount;
        TargetSize = targetSize;
        _validator = RenderPassValidator.Create(commandBuffer);
    }

    public void BindGraphicsPipeline(GraphicsPipeline graphicsPipeline)
    {
        ThrowIfDisposed();

        _validator.OnBindGraphicsPipeline(this, graphicsPipeline);
        BindGraphicsPipelineCore(graphicsPipeline);
    }

    public void BindVertexBuffer<TVertexType>(uint slot, GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType
    {
        ThrowIfDisposed();

        _validator.OnBindVertexBuffer(this, slot, buffer);

        // Only update vertex count from slot 0 (the per-vertex buffer)
        if (slot == 0)
        {
            _verticesCount = (uint)buffer.Size;
        }

        BindVertexBufferCore(slot, buffer);
    }

    public void BindVertexBuffer<TVertexType>(GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType
    {
        BindVertexBuffer(0, buffer);
    }

    public void BindIndexBuffer(GpuIndexBuffer buffer)
    {
        ThrowIfDisposed();

        _validator.OnBindIndexBuffer(this, buffer);
        _indexCount = (uint)buffer.Size;

        BindIndexBufferCore(buffer);
    }

    public void BindVertexSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0)
    {
        ThrowIfDisposed();

        foreach (Texture texture in textures)
        {
            texture.ThrowIfDisposed();
        }

        byte numSamplers = (byte)Math.Max(_vertexShaderBindingCounts.NumSamplers, slot + textures.Length);
        _vertexShaderBindingCounts = _vertexShaderBindingCounts with { NumSamplers = numSamplers };

        _validator.OnBindVertexSamplers(this, slot, textures.Length);

        BindVertexSamplersCore(slot, textures, sampler);
    }

    public void BindFragmentSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0)
    {
        ThrowIfDisposed();

        foreach (Texture texture in textures)
        {
            texture.ThrowIfDisposed();
        }

        byte numSamplers = (byte)Math.Max(_fragmentShaderBindingCounts.NumSamplers, slot + textures.Length);
        _fragmentShaderBindingCounts = _fragmentShaderBindingCounts with { NumSamplers = numSamplers };

        _validator.OnBindFragmentSamplers(this, slot, textures.Length);

        BindFragmentSamplersCore(slot, textures, sampler);
    }

    public void BindFragmentSampler(Texture texture, Sampler sampler)
    {
        ThrowIfDisposed();

        ReadOnlySpan<Texture> textures = [texture];
        BindFragmentSamplers(textures, sampler, 0);
    }

    public void BindFragmentSamplerArray(TextureArray textureArray, Sampler sampler, uint slot = 0)
    {
        ThrowIfDisposed();
        textureArray.ThrowIfDisposed();

        byte numSamplers = (byte)Math.Max(_fragmentShaderBindingCounts.NumSamplers, slot + 1);
        _fragmentShaderBindingCounts = _fragmentShaderBindingCounts with { NumSamplers = numSamplers };

        _validator.OnBindFragmentSamplers(this, slot, 1);

        ReadOnlySpan<Texture> textures = [textureArray];
        BindFragmentSamplersCore(slot, textures, sampler);
    }

    public void BindVertexStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ThrowIfDisposed();

        byte numStorageBuffers = (byte)Math.Max(_vertexShaderBindingCounts.NumStorageBuffers, slot + buffers.Length);
        _vertexShaderBindingCounts = _vertexShaderBindingCounts with { NumStorageBuffers = numStorageBuffers };

        _validator.OnBindVertexStorageBuffers(this, slot, buffers);

        BindVertexStorageBuffersCore(slot, buffers);
    }

    public void BindVertexStorageBuffer(GpuStorageBuffer buffer, uint slot = 0)
    {
        ThrowIfDisposed();

        ReadOnlySpan<GpuStorageBuffer> buffers = [buffer];
        BindVertexStorageBuffers(buffers, slot);
    }

    public void BindFragmentStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ThrowIfDisposed();

        byte numStorageBuffers = (byte)Math.Max(_fragmentShaderBindingCounts.NumStorageBuffers, slot + buffers.Length);
        _fragmentShaderBindingCounts = _fragmentShaderBindingCounts with { NumStorageBuffers = numStorageBuffers };

        _validator.OnBindFragmentStorageBuffers(this, slot, buffers);

        BindFragmentStorageBuffersCore(slot, buffers);
    }

    public void BindFragmentStorageBuffer(GpuStorageBuffer buffer, uint slot = 0)
    {
        ThrowIfDisposed();

        ReadOnlySpan<GpuStorageBuffer> buffers = [buffer];
        BindFragmentStorageBuffers(buffers, slot);
    }

    public void SetStencilReference(byte reference)
    {
        ThrowIfDisposed();
        SetStencilReferenceCore(reference);
    }

    /// <summary>
    /// Restricts subsequent draws to <paramref name="scissor"/>, in render target pixels.
    /// The rectangle is clipped to the render target, so a larger one simply restricts nothing.
    /// </summary>
    public void SetScissor(Rectangle scissor)
    {
        ThrowIfDisposed();

        _validator.OnSetScissor(this, scissor);

        // A scissor restricts drawing to an area, so anything outside the pass is simply not part
        // of it. Intersecting rather than rejecting also keeps the rectangle within what the
        // backends accept, which an arbitrary caller rectangle is not.
        SetScissorCore(scissor.Intersect(new Rectangle(0, 0, TargetSize.Width, TargetSize.Height)));
    }

    /// <summary>
    /// Restores the scissor to cover the whole render target.
    /// </summary>
    public void ClearScissor()
    {
        SetScissor(new Rectangle(0, 0, TargetSize.Width, TargetSize.Height));
    }

    public void DrawPrimitive()
    {
        DrawPrimitiveInstanced(1);
    }

    public void DrawPrimitiveInstanced(uint instanceCount)
    {
        ThrowIfDisposed();

        _validator.OnDrawPrimitive(this);
        DrawPrimitivesCore(_verticesCount, instanceCount);
    }

    public void DrawIndexedPrimitive()
    {
        uint indexCount = _indexCount;
        DrawIndexedPrimitive(indexCount);
    }

    public void DrawIndexedPrimitive(uint indexCount, uint firstIndex = 0)
    {
        DrawIndexedPrimitiveInstanced(indexCount, 1, firstIndex);
    }

    public void DrawIndexedPrimitiveInstanced(uint instanceCount)
    {
        DrawIndexedPrimitiveInstanced(_indexCount, instanceCount, 0);
    }

    // Draws have no base vertex or first instance, so SV_VertexID and SV_InstanceID agree on every backend without
    // Vulkan's shaderDrawParameters.
    public void DrawIndexedPrimitiveInstanced(uint indexCount, uint instanceCount, uint firstIndex)
    {
        ThrowIfDisposed();

        _validator.OnDrawIndexedPrimitive(this, indexCount, firstIndex);
        DrawIndexedPrimitivesCore(indexCount, instanceCount, firstIndex);
    }

    public bool IsDefault()
    {
        return _isEnded;
    }

    public void Dispose()
    {
        if (!_isEnded)
        {
            _isEnded = true;
            EndCore();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isEnded)
        {
            throw new ObjectDisposedException(nameof(RenderPass));
        }
    }

    // The backend, called only on a pass that has not ended and with validated arguments.
    internal abstract void BindGraphicsPipelineCore(GraphicsPipeline graphicsPipeline);
    internal abstract void BindVertexBufferCore(uint slot, GpuVertexBuffer buffer);
    internal abstract void BindIndexBufferCore(GpuIndexBuffer buffer);
    internal abstract void BindVertexSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler);
    internal abstract void BindFragmentSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler);
    internal abstract void BindVertexStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers);
    internal abstract void BindFragmentStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers);
    internal abstract void SetStencilReferenceCore(byte reference);
    // The scissor is already clipped to TargetSize.
    internal abstract void SetScissorCore(Rectangle scissor);
    internal abstract void DrawPrimitivesCore(uint vertexCount, uint instanceCount);
    internal abstract void DrawIndexedPrimitivesCore(uint indexCount, uint instanceCount, uint firstIndex);
    internal abstract void EndCore();
}
