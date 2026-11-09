using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

public enum TextureType
{
    TwoD = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
    TwoDArray = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D_ARRAY,
    ThreeD = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_3D,
    Cube = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_CUBE,
    CubeArray = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_CUBE_ARRAY
}
