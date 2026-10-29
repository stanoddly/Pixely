using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

internal sealed class SdlRenderPass : RenderPass
{
    private Pointer<SDL_GPURenderPass> _nativePointer;

    internal SdlRenderPass(
        CommandBuffer commandBuffer,
        Pointer<SDL_GPURenderPass> nativePointer,
        DepthBufferFormat depthBufferFormat,
        SampleCount sampleCount,
        ShortSize targetSize)
        : base(commandBuffer, depthBufferFormat, sampleCount, targetSize)
    {
        _nativePointer = nativePointer;
    }

    internal override void BindGraphicsPipelineCore(GraphicsPipeline graphicsPipeline)
    {
        unsafe
        {
            SDL3.SDL_BindGPUGraphicsPipeline(_nativePointer, graphicsPipeline.Pointer);
        }
    }

    internal override void BindVertexBufferCore(uint slot, GpuVertexBuffer buffer)
    {
        unsafe
        {
            SDL_GPUBufferBinding sdlGpuBufferBinding = new SDL_GPUBufferBinding { buffer = buffer.SdlVertexBuffer, offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(_nativePointer, slot, &sdlGpuBufferBinding, 1);
        }
    }

    internal override void BindIndexBufferCore(GpuIndexBuffer buffer)
    {
        unsafe
        {
            SDL_GPUBufferBinding sdlGpuBufferBinding = new SDL_GPUBufferBinding { buffer = buffer.SdlBuffer, offset = 0 };
            SDL3.SDL_BindGPUIndexBuffer(_nativePointer, &sdlGpuBufferBinding, GetSdlIndexElementSize(buffer.ElementSize));
        }
    }

    internal override void BindVertexSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler)
    {
        unsafe
        {
            SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[textures.Length];
            FillSamplerBindings(bindings, textures, sampler);
            SDL3.SDL_BindGPUVertexSamplers(_nativePointer, slot, bindings, (uint)textures.Length);
        }
    }

    internal override void BindFragmentSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler)
    {
        unsafe
        {
            SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[textures.Length];
            FillSamplerBindings(bindings, textures, sampler);
            SDL3.SDL_BindGPUFragmentSamplers(_nativePointer, slot, bindings, (uint)textures.Length);
        }
    }

    internal override void BindVertexStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];
            FillBuffers(sdlBuffers, buffers);
            SDL3.SDL_BindGPUVertexStorageBuffers(_nativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
    }

    internal override void BindFragmentStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];
            FillBuffers(sdlBuffers, buffers);
            SDL3.SDL_BindGPUFragmentStorageBuffers(_nativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
    }

    internal override void SetStencilReferenceCore(byte reference)
    {
        unsafe
        {
            SDL3.SDL_SetGPUStencilReference(_nativePointer, reference);
        }
    }

    internal override void SetScissorCore(Rectangle scissor)
    {
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

    internal override void DrawPrimitivesCore(uint vertexCount, uint instanceCount)
    {
        unsafe
        {
            SDL3.SDL_DrawGPUPrimitives(_nativePointer, vertexCount, instanceCount, 0, 0);
        }
    }

    internal override void DrawIndexedPrimitivesCore(uint indexCount, uint instanceCount, uint firstIndex)
    {
        unsafe
        {
            SDL3.SDL_DrawGPUIndexedPrimitives(_nativePointer, indexCount, instanceCount, firstIndex, 0, 0);
        }
    }

    internal override void EndCore()
    {
        unsafe
        {
            SDL3.SDL_EndGPURenderPass(_nativePointer);
        }

        _nativePointer = Pointer<SDL_GPURenderPass>.Null;
    }

    private static unsafe void FillSamplerBindings(SDL_GPUTextureSamplerBinding* bindings, ReadOnlySpan<Texture> textures, Sampler sampler)
    {
        for (int i = 0; i < textures.Length; i++)
        {
            bindings[i] = new SDL_GPUTextureSamplerBinding { texture = textures[i].SdlGpuTexture, sampler = sampler.Pointer };
        }
    }

    private static unsafe void FillBuffers(SDL_GPUBuffer** sdlBuffers, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            sdlBuffers[i] = buffers[i].SdlBuffer;
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
