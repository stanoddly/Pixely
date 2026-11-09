using XdlSharp;

namespace Pixely.Gpu;

/// <summary>
/// Specifies how the contents of a GPU resource should be treated at the start of a render pass.
/// </summary>
public enum LoadOperation
{
    /// <summary>
    /// Load the existing contents of the resource.
    /// </summary>
    Load = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
    
    /// <summary>
    /// Clear the resource at the start of the render pass.
    /// </summary>
    Clear = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
    
    /// <summary>
    /// The contents of the resource will be undefined at the start of the render pass.
    /// </summary>
    DontCare = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE
}