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

    public virtual void Dispose()
    {
        CommandBuffer.Submit();
    }
}
