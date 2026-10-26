using SDL;

namespace Pixely.Gpu;

[Flags]
public enum StorageBufferUsage : uint
{
    GraphicsStorageRead = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ,
    ComputeStorageRead = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ,
    ComputeStorageWrite = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_WRITE
}
