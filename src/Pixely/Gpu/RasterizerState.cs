using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

/// <summary>
/// Defines how polygons are rendered.
/// </summary>
public class RasterizerState
{
    /// <summary>
    /// Gets or sets the culling mode for polygons.
    /// </summary>
    public CullMode CullMode { get; set; }

    /// <summary>
    /// Gets or sets which face is considered the front face for culling.
    /// </summary>
    public FrontFace FrontFace { get; set; }

    /// <summary>
    /// Gets or sets the polygon fill mode.
    /// </summary>
    public FillMode FillMode { get; set; } = FillMode.Fill;

    /// <summary>
    /// A scalar factor controlling the depth value added to each fragment.
    /// </summary>
    public float DepthBiasConstantFactor { get; set; }

    /// <summary>
    /// The maximum depth bias of a fragment.
    /// </summary>
    public float DepthBiasClamp { get; set; }

    /// <summary>
    /// A scalar factor applied to a fragment's slope in depth calculations.
    /// </summary>
    public float DepthBiasSlopeFactor { get; set; }

    /// <summary>
    /// Gets or sets whether to bias fragment depth values.
    /// </summary>
    public bool EnableDepthBias { get; set; }

    /// <summary>
    /// Gets or sets whether to enable depth clip (true) or depth clamp (false).
    /// </summary>
    public bool EnableDepthClip { get; set; }
}

/// <summary>
/// Defines the polygon fill mode.
/// </summary>
public enum FillMode
{
    /// <summary>
    /// Polygons are filled solid.
    /// </summary>
    Fill = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,

    /// <summary>
    /// Polygons are rendered as wireframe lines.
    /// </summary>
    Line = SDL_GPUFillMode.SDL_GPU_FILLMODE_LINE
}

/// <summary>
/// Defines the polygon culling mode.
/// </summary>
public enum CullMode
{
    /// <summary>
    /// Do not cull any polygons.
    /// </summary>
    None = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
    
    /// <summary>
    /// Cull front-facing polygons.
    /// </summary>
    Front = SDL_GPUCullMode.SDL_GPU_CULLMODE_FRONT,
    
    /// <summary>
    /// Cull back-facing polygons.
    /// </summary>
    Back = SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK
}

/// <summary>
/// Defines which winding order is considered the front face.
/// </summary>
public enum FrontFace
{
    /// <summary>
    /// Counter-clockwise winding order defines front-facing polygons.
    /// </summary>
    CounterClockwise = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
    
    /// <summary>
    /// Clockwise winding order defines front-facing polygons.
    /// </summary>
    Clockwise = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_CLOCKWISE
}
