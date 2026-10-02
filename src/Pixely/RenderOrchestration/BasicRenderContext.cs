using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public class BasicRenderContext : IRenderContext
{
    public SwapchainTexture SwapchainTexture { get; }
    public CommandBuffer CommandBuffer { get; }
    public Texture ColorTarget => SwapchainTexture;

    public BasicRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        SwapchainTexture = swapchainTexture;
        CommandBuffer = commandBuffer;
    }

    // The render coordinator submits the command buffer after disposing the context, so a derived context overrides this only
    // for its own per-frame cleanup.
    public virtual void Dispose()
    {
    }
}
