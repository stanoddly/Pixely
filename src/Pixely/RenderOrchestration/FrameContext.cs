using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

// Not positional: a member added later would change a positional constructor and break every caller that creates one.
public readonly struct FrameContext
{
    public required Window Window { get; init; }
    public required CommandBuffer CommandBuffer { get; init; }
    public required SwapchainTexture SwapchainTexture { get; init; }
}
