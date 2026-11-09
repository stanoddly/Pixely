using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

/// <summary>
/// Defines blend factors for GPU blend operations
/// </summary>
public enum BlendFactor: byte
{
    Invalid = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_INVALID,
    Zero = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
    One = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
    SrcColor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_COLOR,
    OneMinusSrcColor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_COLOR,
    DstColor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_DST_COLOR,
    OneMinusDstColor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_DST_COLOR,
    SrcAlpha = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
    OneMinusSrcAlpha = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
    DstAlpha = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_DST_ALPHA,
    OneMinusDstAlpha = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_DST_ALPHA,
    ConstantColor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_CONSTANT_COLOR,
    OneMinusConstantColor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_CONSTANT_COLOR,
    SrcAlphaSaturate = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA_SATURATE
}
