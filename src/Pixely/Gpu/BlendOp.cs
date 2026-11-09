using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

public enum BlendOp: byte
{
    Invalid = SDL_GPUBlendOp.SDL_GPU_BLENDOP_INVALID,
    Add = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
    Subtract = SDL_GPUBlendOp.SDL_GPU_BLENDOP_SUBTRACT,
    ReverseSubtract = SDL_GPUBlendOp.SDL_GPU_BLENDOP_REVERSE_SUBTRACT,
    Min = SDL_GPUBlendOp.SDL_GPU_BLENDOP_MIN,
    Max = SDL_GPUBlendOp.SDL_GPU_BLENDOP_MAX
}