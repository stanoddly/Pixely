using Pixely.ShaderCommon;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

internal sealed class SdlComputePass : ComputePass
{
    private Pointer<SDL_GPUComputePass> _nativePointer;

    internal SdlComputePass(Pointer<SDL_GPUComputePass> nativePointer, uint readWriteStorageTextureCount, uint readWriteStorageBufferCount, StorageBufferElementSizes readWriteStorageBufferElementSizes)
        : base(readWriteStorageTextureCount, readWriteStorageBufferCount, readWriteStorageBufferElementSizes)
    {
        _nativePointer = nativePointer;
    }

    internal override void BindComputePipelineCore(ComputePipeline pipeline)
    {
        unsafe
        {
            SDL_BindGPUComputePipeline(_nativePointer, pipeline.Pointer);
        }
    }

    internal override void BindSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler)
    {
        unsafe
        {
            SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[textures.Length];
            SdlBindings.FillSamplerBindings(bindings, textures, sampler);
            SDL_BindGPUComputeSamplers(_nativePointer, slot, bindings, (uint)textures.Length);
        }
    }

    internal override void BindReadOnlyStorageTexturesCore(uint slot, ReadOnlySpan<Texture> textures)
    {
        unsafe
        {
            SDL_GPUTexture** sdlTextures = stackalloc SDL_GPUTexture*[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                sdlTextures[i] = textures[i].SdlGpuTexture;
            }
            SDL_BindGPUComputeStorageTextures(_nativePointer, slot, (Pointer<SDL_GPUTexture>*)sdlTextures, (uint)textures.Length);
        }
    }

    internal override void BindReadOnlyStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];
            SdlBindings.FillBuffers(sdlBuffers, buffers);
            SDL_BindGPUComputeStorageBuffers(_nativePointer, slot, (Pointer<SDL_GPUBuffer>*)sdlBuffers, (uint)buffers.Length);
        }
    }

    internal override void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        unsafe
        {
            SDL_DispatchGPUCompute(_nativePointer, groupCountX, groupCountY, groupCountZ);
        }
    }

    internal override void DispatchIndirectCore(GpuStorageBuffer buffer, uint offset)
    {
        unsafe
        {
            SDL_DispatchGPUComputeIndirect(_nativePointer, buffer.SdlBuffer, offset);
        }
    }

    internal override void EndCore()
    {
        unsafe
        {
            SDL_EndGPUComputePass(_nativePointer);
        }

        _nativePointer = Pointer<SDL_GPUComputePass>.Null;
    }
}
