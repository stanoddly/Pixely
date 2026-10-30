using SDL;

namespace Pixely.Gpu;

// Fills the binding arrays that SDL's render and compute pass bind calls take.
internal static class SdlBindings
{
    internal static unsafe void FillSamplerBindings(SDL_GPUTextureSamplerBinding* bindings, ReadOnlySpan<Texture> textures, Sampler sampler)
    {
        for (int i = 0; i < textures.Length; i++)
        {
            bindings[i] = new SDL_GPUTextureSamplerBinding { texture = textures[i].SdlGpuTexture, sampler = sampler.Pointer };
        }
    }

    internal static unsafe void FillBuffers(SDL_GPUBuffer** sdlBuffers, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            sdlBuffers[i] = buffers[i].SdlBuffer;
        }
    }
}
