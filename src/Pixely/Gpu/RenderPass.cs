using System.Runtime.CompilerServices;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// A render pass open on a <see cref="CommandBuffer"/>. It holds no state of its own: the pass state lives on the command buffer,
/// which allows one open pass at a time, so copies of this handle all act on the same pass. Using a handle after its pass was
/// disposed throws <see cref="ObjectDisposedException"/>, and disposing it again does nothing.
/// </summary>
public readonly ref struct RenderPass : IDisposable
{
    private readonly ref CommandBufferState _commandBuffer;
    private readonly uint _passNumber;

    private RenderPass(ref CommandBufferState commandBuffer, uint passNumber)
    {
        _commandBuffer = ref commandBuffer;
        _passNumber = passNumber;
    }

    public ShaderBindingCounts FragmentShaderBindingCounts => OpenState().FragmentShaderBindingCounts;
    public ShaderBindingCounts VertexShaderBindingCounts => OpenState().VertexShaderBindingCounts;
    public DepthBufferFormat DepthBufferFormat => OpenState().DepthBufferFormat;

    /// <summary>
    /// The area every attachment of this pass covers, which is the smallest of them.
    /// It is what <see cref="SetScissor"/> clips to and what <see cref="ClearScissor"/> restores.
    /// </summary>
    public ShortSize TargetSize => OpenState().TargetSize;

    internal static RenderPass Begin(
        ref CommandBufferState commandBuffer,
        scoped ReadOnlySpan<Texture> colorTargets,
        scoped ReadOnlySpan<ColorTargetSettings> colorTargetSettings,
        Texture? depthBuffer,
        DepthBufferSettings depthBufferSettings)
    {
        commandBuffer.ThrowIfDisposed();
        commandBuffer.ThrowIfPassOpen();

        if (colorTargetSettings.Length != colorTargets.Length)
        {
            throw new ArgumentException($"{colorTargets.Length} color targets need {colorTargets.Length} settings, but {colorTargetSettings.Length} were given.", nameof(colorTargetSettings));
        }

        Span<SDL_GPUColorTargetInfo> colorTargetInfos = stackalloc SDL_GPUColorTargetInfo[colorTargets.Length];

        for (int i = 0; i < colorTargets.Length; i++)
        {
            ColorTargetSettings colorTargetSetting = colorTargetSettings[i];

            colorTargetInfos[i] = new SDL_GPUColorTargetInfo
            {
                texture = colorTargets[i].SdlGpuTexture,
                clear_color = colorTargetSetting.ClearColorValue,
                load_op = (SDL_GPULoadOp)colorTargetSetting.LoadOperation,
                store_op = (SDL_GPUStoreOp)colorTargetSetting.StoreOperation
            };
        }

        Pointer<SDL_GPURenderPass> nativePointer;

        unsafe
        {
            fixed (SDL_GPUColorTargetInfo* colorTargetInfosPtr = colorTargetInfos)
            {
                if (depthBuffer == null)
                {
                    nativePointer = SDL3.SDL_BeginGPURenderPass(commandBuffer.SdlGpuCommandBuffer, colorTargetInfosPtr, (uint)colorTargetInfos.Length, null);
                }
                else
                {
                    SDL_GPUDepthStencilTargetInfo depthStencilTargetInfo = new SDL_GPUDepthStencilTargetInfo
                    {
                        texture = depthBuffer.SdlGpuTexture,
                        clear_depth = depthBufferSettings.ClearDepthValue,
                        load_op = (SDL_GPULoadOp)depthBufferSettings.DepthBufferLoadOperation,
                        store_op = (SDL_GPUStoreOp)depthBufferSettings.DepthBufferStoreOperation,
                        stencil_load_op = (SDL_GPULoadOp)depthBufferSettings.StencilLoadOperation,
                        stencil_store_op = (SDL_GPUStoreOp)depthBufferSettings.StencilStoreOperation,
                        clear_stencil = depthBufferSettings.ClearStencilValue
                    };

                    nativePointer = SDL3.SDL_BeginGPURenderPass(commandBuffer.SdlGpuCommandBuffer, colorTargetInfosPtr, (uint)colorTargetInfos.Length, &depthStencilTargetInfo);
                }
            }
        }

        SdlError.ThrowOnNull(nativePointer);

        uint passNumber = commandBuffer.OpenNextPass(OpenPassKind.Render);
        commandBuffer.RenderPass = new RenderPassState
        {
            NativePointer = nativePointer,
            DepthBufferFormat = depthBuffer != null ? (DepthBufferFormat)depthBuffer.Format : DepthBufferFormat.None,
            TargetSize = CalculateTargetSize(colorTargets, depthBuffer)
        };

        return new RenderPass(ref commandBuffer, passNumber);
    }

    // A pass can only safely address the area every attachment shares, so the scissor bounds
    // are the smallest attachment, depth included.
    private static ShortSize CalculateTargetSize(ReadOnlySpan<Texture> colorTargets, Texture? depthBuffer)
    {
        ushort width = ushort.MaxValue;
        ushort height = ushort.MaxValue;

        foreach (Texture colorTarget in colorTargets)
        {
            width = Math.Min(width, colorTarget.Size.Width);
            height = Math.Min(height, colorTarget.Size.Height);
        }

        if (depthBuffer != null)
        {
            width = Math.Min(width, depthBuffer.Size.Width);
            height = Math.Min(height, depthBuffer.Size.Height);
        }

        return new ShortSize(width, height);
    }

    public void BindGraphicsPipeline(GraphicsPipeline graphicsPipeline)
    {
        ref RenderPassState state = ref OpenState();

        state.Validator.OnBindGraphicsPipeline(state.DepthBufferFormat, graphicsPipeline);

        unsafe
        {
            SDL3.SDL_BindGPUGraphicsPipeline(state.NativePointer, graphicsPipeline.Pointer);
        }
    }
    
    public void BindVertexBuffer<TVertexType>(uint slot, GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType
    {
        ref RenderPassState state = ref OpenState();

        state.Validator.OnBindVertexBuffer(slot, buffer);

        // Only update vertex count from slot 0 (the per-vertex buffer)
        if (slot == 0)
        {
            state.VerticesCount = (uint)buffer.BufferSize;
        }

        unsafe
        {
            SDL_GPUBufferBinding sdlGpuBufferBinding = new SDL_GPUBufferBinding { buffer = buffer.SdlVertexBuffer, offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(state.NativePointer, slot, &sdlGpuBufferBinding, 1);
        }
    }

    public void BindVertexBuffer<TVertexType>(GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType
    {
        BindVertexBuffer(0, buffer);
    }

    public void BindIndexBuffer(GpuIndexBuffer buffer)
    {
        ref RenderPassState state = ref OpenState();

        state.Validator.OnBindIndexBuffer(buffer);
        state.IndexBuffer = buffer;

        unsafe
        {
            SDL_GPUBufferBinding sdlGpuBufferBinding = new SDL_GPUBufferBinding { buffer = buffer.SdlBuffer, offset = 0 };
            SDL3.SDL_BindGPUIndexBuffer(state.NativePointer, &sdlGpuBufferBinding, GetSdlIndexElementSize(buffer.ElementSize));
        }
    }

    public void BindVertexSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0)
    {
        ref RenderPassState state = ref OpenState();

        foreach (Texture texture in textures)
        {
            texture.ThrowIfDisposed();
        }
        
        byte numSamplers = (byte)Math.Max(state.VertexShaderBindingCounts.NumSamplers, slot + textures.Length);
        state.VertexShaderBindingCounts = state.VertexShaderBindingCounts with { NumSamplers = numSamplers };

        unsafe {
            SDL_GPUTextureSamplerBinding* sdlGpuBufferBindings =
                stackalloc SDL_GPUTextureSamplerBinding[textures.Length];

            for (int i = 0; i < textures.Length; i++)
            {
                sdlGpuBufferBindings[i] = new SDL_GPUTextureSamplerBinding
                    { texture = textures[i].SdlGpuTexture, sampler = sampler.Pointer };
            }

            SDL3.SDL_BindGPUVertexSamplers(state.NativePointer, slot, sdlGpuBufferBindings, (uint)textures.Length);
        }
    }

    public void BindFragmentSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0)
    {
        ref RenderPassState state = ref OpenState();

        foreach (Texture texture in textures)
        {
            texture.ThrowIfDisposed();
        }
        
        byte numSamplers = (byte)Math.Max(state.FragmentShaderBindingCounts.NumSamplers, slot + textures.Length);
        state.FragmentShaderBindingCounts = state.FragmentShaderBindingCounts with { NumSamplers = numSamplers };

        unsafe {
            SDL_GPUTextureSamplerBinding* sdlGpuBufferBindings =
                stackalloc SDL_GPUTextureSamplerBinding[textures.Length];

            for (int i = 0; i < textures.Length; i++)
            {
                sdlGpuBufferBindings[i] = new SDL_GPUTextureSamplerBinding
                    { texture = textures[i].SdlGpuTexture, sampler = sampler.Pointer };
            }

            SDL3.SDL_BindGPUFragmentSamplers(state.NativePointer, slot, sdlGpuBufferBindings, (uint)textures.Length);
        }
    }

    public void BindFragmentSampler(Texture texture, Sampler sampler)
    {
        ReadOnlySpan<Texture> textures = [texture];
        BindFragmentSamplers(textures, sampler, 0);
    }

    public void BindFragmentSamplerArray(TextureArray textureArray, Sampler sampler, uint slot = 0)
    {
        ref RenderPassState state = ref OpenState();
        textureArray.ThrowIfDisposed();

        byte numSamplers = (byte)Math.Max(state.FragmentShaderBindingCounts.NumSamplers, slot + 1);
        state.FragmentShaderBindingCounts = state.FragmentShaderBindingCounts with { NumSamplers = numSamplers };

        unsafe
        {
            SDL_GPUTextureSamplerBinding sdlGpuBufferBinding = new SDL_GPUTextureSamplerBinding
            {
                texture = textureArray.SdlGpuTexture,
                sampler = sampler.Pointer
            };

            SDL3.SDL_BindGPUFragmentSamplers(state.NativePointer, slot, &sdlGpuBufferBinding, 1);
        }
    }

    public void BindVertexStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ref RenderPassState state = ref OpenState();

        byte numStorageBuffers = (byte)Math.Max(state.VertexShaderBindingCounts.NumStorageBuffers, slot + buffers.Length);
        state.VertexShaderBindingCounts = state.VertexShaderBindingCounts with { NumStorageBuffers = numStorageBuffers };

        state.Validator.OnBindVertexStorageBuffers(slot, buffers);

        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];

            for (int i = 0; i < buffers.Length; i++)
            {
                sdlBuffers[i] = buffers[i].SdlBuffer;
            }

            SDL3.SDL_BindGPUVertexStorageBuffers(state.NativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
    }

    public void BindVertexStorageBuffer(GpuStorageBuffer buffer, uint slot = 0)
    {
        ReadOnlySpan<GpuStorageBuffer> buffers = [buffer];
        BindVertexStorageBuffers(buffers, slot);
    }

    public void BindFragmentStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ref RenderPassState state = ref OpenState();

        byte numStorageBuffers = (byte)Math.Max(state.FragmentShaderBindingCounts.NumStorageBuffers, slot + buffers.Length);
        state.FragmentShaderBindingCounts = state.FragmentShaderBindingCounts with { NumStorageBuffers = numStorageBuffers };

        state.Validator.OnBindFragmentStorageBuffers(slot, buffers);

        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];

            for (int i = 0; i < buffers.Length; i++)
            {
                sdlBuffers[i] = buffers[i].SdlBuffer;
            }

            SDL3.SDL_BindGPUFragmentStorageBuffers(state.NativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
    }

    public void BindFragmentStorageBuffer(GpuStorageBuffer buffer, uint slot = 0)
    {
        ReadOnlySpan<GpuStorageBuffer> buffers = [buffer];
        BindFragmentStorageBuffers(buffers, slot);
    }

    public void SetStencilReference(byte reference)
    {
        ref RenderPassState state = ref OpenState();
        unsafe { SDL3.SDL_SetGPUStencilReference(state.NativePointer, reference); }
    }

    public void SetScissor(Rectangle scissor)
    {
        ref RenderPassState state = ref OpenState();

        RenderPassValidator.ValidateScissorSize(scissor);

        // A scissor restricts drawing to an area, so anything outside the pass is simply not part
        // of it. Intersecting rather than rejecting also keeps the rectangle within what the
        // backends accept, which an arbitrary caller rectangle is not.
        Rectangle clipped = scissor.Intersect(new Rectangle(0, 0, state.TargetSize.Width, state.TargetSize.Height));

        unsafe
        {
            SDL_Rect sdlScissor = new SDL_Rect
            {
                x = clipped.X,
                y = clipped.Y,
                w = clipped.Width,
                h = clipped.Height
            };

            SDL3.SDL_SetGPUScissor(state.NativePointer, &sdlScissor);
        }
    }

    public void ClearScissor()
    {
        ShortSize targetSize = TargetSize;
        SetScissor(new Rectangle(0, 0, targetSize.Width, targetSize.Height));
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
        ref RenderPassState state = ref OpenState();

        state.Validator.OnDrawPrimitive(_commandBuffer, firstInstance);

        unsafe
        {
            SDL3.SDL_DrawGPUPrimitives(state.NativePointer, state.VerticesCount, instanceCount, 0, firstInstance);
        }
    }

    public void DrawIndexedPrimitive()
    {
        uint indexCount = (uint)(OpenState().IndexBuffer?.Size ?? 0);
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
        uint indexCount = (uint)(OpenState().IndexBuffer?.Size ?? 0);
        DrawIndexedPrimitiveInstanced(indexCount, instanceCount, 0, 0, firstInstance);
    }

    public void DrawIndexedPrimitiveInstanced(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance)
    {
        ref RenderPassState state = ref OpenState();

        state.Validator.OnDrawIndexedPrimitive(_commandBuffer, indexCount, firstIndex, vertexOffset, firstInstance);

        unsafe
        {
            SDL3.SDL_DrawGPUIndexedPrimitives(
                state.NativePointer,
                indexCount,
                instanceCount,
                firstIndex,
                vertexOffset,
                firstInstance);
        }
    }

    public bool IsDefault()
    {
        return Unsafe.IsNullRef(ref _commandBuffer);
    }

    public void Dispose()
    {
        if (Unsafe.IsNullRef(ref _commandBuffer) || !_commandBuffer.IsPassOpen(OpenPassKind.Render, _passNumber))
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_EndGPURenderPass(_commandBuffer.RenderPass.NativePointer);
        }

        _commandBuffer.ClosePass();
    }

    private ref RenderPassState OpenState()
    {
        if (Unsafe.IsNullRef(ref _commandBuffer) || !_commandBuffer.IsPassOpen(OpenPassKind.Render, _passNumber))
        {
            throw new ObjectDisposedException(nameof(RenderPass));
        }

        return ref _commandBuffer.RenderPass;
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
