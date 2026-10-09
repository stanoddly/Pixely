using SDL;

namespace Pixely.Gpu;

/// <summary>
/// The number of samples per pixel of a texture or a pipeline's rasterization. Above <see cref="Count1"/> it is multisample anti-aliasing (MSAA).
/// Support depends on the backend and the texture format, so check a count with <see cref="GpuDevice.IsSampleCountSupported"/>.
/// WebGPU supports only <see cref="Count1"/> and <see cref="Count4"/>.
/// </summary>
public enum SampleCount
{
    Count1 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
    Count2 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2,
    Count4 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4,
    Count8 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_8
}
