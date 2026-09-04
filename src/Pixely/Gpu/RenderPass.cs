using System.Diagnostics;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class RenderPass<TValidator> : IRenderPass
    where TValidator : IRenderPassValidator<TValidator>
{
    private Pointer<SDL_GPURenderPass> _nativePointer;
    private uint _verticesCount = 0;
    private GpuIndexBuffer? _indexBuffer;
    private TValidator _validator;

    private ShaderBindingCounts _fragmentShaderBindingCounts;
    private ShaderBindingCounts _vertexShaderBindingCounts;

    public ShaderBindingCounts FragmentShaderBindingCounts => _fragmentShaderBindingCounts;
    public ShaderBindingCounts VertexShaderBindingCounts => _vertexShaderBindingCounts;
    public DepthBufferFormat DepthBufferFormat { get; }

    /// <summary>
    /// The area every attachment of this pass covers, which is the smallest of them.
    /// It bounds what <see cref="SetScissor"/> accepts and is what <see cref="ClearScissor"/> restores.
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
        _validator = TValidator.Create(commandBuffer);
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
            _verticesCount = (uint)buffer.BufferSize;
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
        _indexBuffer = buffer;

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

    public void SetScissor(Rectangle scissor)
    {
        ThrowIfDisposed();

        _validator.OnSetScissor(this, scissor);

        unsafe
        {
            SDL_Rect sdlScissor = new SDL_Rect
            {
                x = scissor.X,
                y = scissor.Y,
                w = scissor.Width,
                h = scissor.Height
            };

            SDL3.SDL_SetGPUScissor(_nativePointer, &sdlScissor);
        }
    }

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
        DrawPrimitiveInstanced(instanceCount, 0);
    }

    public void DrawPrimitiveInstanced(uint instanceCount, uint firstInstance)
    {
        ThrowIfDisposed();

        _validator.OnDrawPrimitive(this, firstInstance);

        unsafe
        {
            SDL3.SDL_DrawGPUPrimitives(_nativePointer, _verticesCount, instanceCount, 0, firstInstance);
        }
    }

    public void DrawIndexedPrimitive()
    {
        uint indexCount = (uint)(_indexBuffer?.Size ?? 0);
        DrawIndexedPrimitive(indexCount);
    }

    public void DrawIndexedPrimitive(uint indexCount, uint firstIndex = 0, int vertexOffset = 0)
    {
        DrawIndexedPrimitiveInstanced(indexCount, 1, firstIndex, vertexOffset, 0);
    }

    public void DrawIndexedPrimitiveInstanced(uint instanceCount)
    {
        DrawIndexedPrimitiveInstanced(instanceCount, 0);
    }

    public void DrawIndexedPrimitiveInstanced(uint instanceCount, uint firstInstance)
    {
        uint indexCount = (uint)(_indexBuffer?.Size ?? 0);
        DrawIndexedPrimitiveInstanced(indexCount, instanceCount, 0, 0, firstInstance);
    }

    public void DrawIndexedPrimitiveInstanced(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance)
    {
        ThrowIfDisposed();

        _validator.OnDrawIndexedPrimitive(this, indexCount, firstIndex, vertexOffset, firstInstance);

        unsafe
        {
            SDL3.SDL_DrawGPUIndexedPrimitives(
                _nativePointer,
                indexCount,
                instanceCount,
                firstIndex,
                vertexOffset,
                firstInstance);
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

/// <summary>
/// Non-generic render pass using the default RenderPassValidator with full validation checks.
/// </summary>
public class RenderPass : RenderPass<RenderPassValidator>
{
    internal RenderPass(
        CommandBuffer commandBuffer,
        Pointer<SDL_GPURenderPass> nativePointer,
        DepthBufferFormat depthBufferFormat,
        ShortSize targetSize)
        : base(commandBuffer, nativePointer, depthBufferFormat, targetSize)
    {
    }
}
