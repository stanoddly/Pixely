using System.Diagnostics;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class RenderPass : IDisposable
{
    private Pointer<SDL_GPURenderPass> _nativePointer;
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
    /// The area every attachment of this pass covers, which is the smallest of them.
    /// It is what <see cref="SetScissor"/> clips to and what <see cref="ClearScissor"/> restores.
    /// </summary>
    public ShortSize TargetSize { get; }

    internal RenderPass(
        CommandBuffer commandBuffer,
        Pointer<SDL_GPURenderPass> nativePointer,
        DepthBufferFormat depthBufferFormat,
        ShortSize targetSize)
    {
        _nativePointer = nativePointer;
        DepthBufferFormat = depthBufferFormat;
        TargetSize = targetSize;
        _validator = RenderPassValidator.Create(commandBuffer);
    }

    public void BindGraphicsPipeline(GraphicsPipeline graphicsPipeline)
    {
        ThrowIfDisposed();

        _validator.OnBindGraphicsPipeline(this, graphicsPipeline);

        unsafe
        {
            SDL3.SDL_BindGPUGraphicsPipeline(_nativePointer, graphicsPipeline.Pointer);
        }
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

        unsafe
        {
            SDL_GPUBufferBinding sdlGpuBufferBinding = new SDL_GPUBufferBinding { buffer = buffer.SdlVertexBuffer, offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(_nativePointer, slot, &sdlGpuBufferBinding, 1);
        }
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

        unsafe
        {
            SDL_GPUBufferBinding sdlGpuBufferBinding = new SDL_GPUBufferBinding { buffer = buffer.SdlBuffer, offset = 0 };
            SDL3.SDL_BindGPUIndexBuffer(_nativePointer, &sdlGpuBufferBinding, GetSdlIndexElementSize(buffer.ElementSize));
        }
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

        unsafe {
            SDL_GPUTextureSamplerBinding* sdlGpuBufferBindings =
                stackalloc SDL_GPUTextureSamplerBinding[textures.Length];

            for (int i = 0; i < textures.Length; i++)
            {
                sdlGpuBufferBindings[i] = new SDL_GPUTextureSamplerBinding
                    { texture = textures[i].SdlGpuTexture, sampler = sampler.Pointer };
            }

            SDL3.SDL_BindGPUVertexSamplers(_nativePointer, slot, sdlGpuBufferBindings, (uint)textures.Length);
        }
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

        unsafe {
            SDL_GPUTextureSamplerBinding* sdlGpuBufferBindings =
                stackalloc SDL_GPUTextureSamplerBinding[textures.Length];

            for (int i = 0; i < textures.Length; i++)
            {
                sdlGpuBufferBindings[i] = new SDL_GPUTextureSamplerBinding
                    { texture = textures[i].SdlGpuTexture, sampler = sampler.Pointer };
            }

            SDL3.SDL_BindGPUFragmentSamplers(_nativePointer, slot, sdlGpuBufferBindings, (uint)textures.Length);
        }
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

        unsafe
        {
            SDL_GPUTextureSamplerBinding sdlGpuBufferBinding = new SDL_GPUTextureSamplerBinding
            {
                texture = textureArray.SdlGpuTexture,
                sampler = sampler.Pointer
            };

            SDL3.SDL_BindGPUFragmentSamplers(_nativePointer, slot, &sdlGpuBufferBinding, 1);
        }
    }

    public void BindVertexStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ThrowIfDisposed();

        byte numStorageBuffers = (byte)Math.Max(_vertexShaderBindingCounts.NumStorageBuffers, slot + buffers.Length);
        _vertexShaderBindingCounts = _vertexShaderBindingCounts with { NumStorageBuffers = numStorageBuffers };

        _validator.OnBindVertexStorageBuffers(this, slot, buffers);

        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];

            for (int i = 0; i < buffers.Length; i++)
            {
                sdlBuffers[i] = buffers[i].SdlBuffer;
            }

            SDL3.SDL_BindGPUVertexStorageBuffers(_nativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
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

        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];

            for (int i = 0; i < buffers.Length; i++)
            {
                sdlBuffers[i] = buffers[i].SdlBuffer;
            }

            SDL3.SDL_BindGPUFragmentStorageBuffers(_nativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
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
        unsafe { SDL3.SDL_SetGPUStencilReference(_nativePointer, reference); }
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
        Rectangle clipped = scissor.Intersect(new Rectangle(0, 0, TargetSize.Width, TargetSize.Height));

        unsafe
        {
            SDL_Rect sdlScissor = new SDL_Rect
            {
                x = clipped.X,
                y = clipped.Y,
                w = clipped.Width,
                h = clipped.Height
            };

            SDL3.SDL_SetGPUScissor(_nativePointer, &sdlScissor);
        }
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

        unsafe
        {
            SDL3.SDL_DrawGPUPrimitives(_nativePointer, _verticesCount, instanceCount, 0, 0);
        }
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

        unsafe
        {
            SDL3.SDL_DrawGPUIndexedPrimitives(_nativePointer, indexCount, instanceCount, firstIndex, 0, 0);
        }
    }

    public bool IsDefault()
    {
        return _nativePointer.IsNull;
    }
    
    public void Dispose()
    {
        if (!_nativePointer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_EndGPURenderPass(_nativePointer);
            }
            _nativePointer = Pointer<SDL_GPURenderPass>.Null;
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (_nativePointer.IsNull)
        {
            throw new ObjectDisposedException(nameof(RenderPass));
        }
    }

    private static SDL_GPUIndexElementSize GetSdlIndexElementSize(IndexElementSize elementSize)
    {
        return elementSize switch
        {
            IndexElementSize.UInt16 => SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_16BIT,
            IndexElementSize.UInt32 => SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT,
            _ => throw new ArgumentOutOfRangeException(nameof(elementSize), elementSize, null)
        };
    }
}
