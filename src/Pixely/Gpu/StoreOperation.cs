using XdlSharp;

namespace Pixely.Gpu;

/// <summary>
/// Specifies how the contents of a GPU resource should be handled at the end of a render pass.
/// </summary>
public enum StoreOperation
{
    /// <summary>
    /// Store the rendered results to memory.
    /// </summary>
    Store = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
    
    /// <summary>
    /// The contents of the resource will not be preserved after the render pass.
    /// </summary>
    DontCare = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
    
    /// <summary>
    /// Resolve multisampled contents to a non-multisampled resource.
    /// </summary>
    Resolve = SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE,
    
    /// <summary>
    /// Resolve multisampled contents and also store the multisampled contents.
    /// </summary>
    ResolveAndStore = SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE_AND_STORE
}