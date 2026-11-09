using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

[Flags]
public enum ColorComponentFlags : byte
{
    Red = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R,
    Green = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G,
    Blue = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_B,
    Alpha = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_A,
    
    RGBA = Red | Green | Blue | Alpha
}
