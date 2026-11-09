using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

[Flags]
public enum TextureUsage : uint
{
    Sampler = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
    ColorTarget = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
    DepthStencilTarget = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET,
    GraphicsStorageRead = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_GRAPHICS_STORAGE_READ,
    ComputeStorageRead = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COMPUTE_STORAGE_READ,
    ComputeStorageWrite = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COMPUTE_STORAGE_WRITE,
    ComputeStorageSimultaneousReadWrite = 1u << 7
}
