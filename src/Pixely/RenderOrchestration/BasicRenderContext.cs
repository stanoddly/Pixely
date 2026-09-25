using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public class BasicRenderContext : IRenderContext
{
    public SwapchainTexture SwapchainTexture { get; private set; }
    public CommandBuffer CommandBuffer { get; private set; }
    public Texture ColorTarget => SwapchainTexture;

    // Between creation and Dispose. BasicRenderContextProvider reuses a context only once it is disposed.
    internal bool IsInUse { get; private set; }

    public BasicRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        SwapchainTexture = swapchainTexture;
        CommandBuffer = commandBuffer;
        IsInUse = true;
    }

    internal void Reuse(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        SwapchainTexture = swapchainTexture;
        CommandBuffer = commandBuffer;
        IsInUse = true;
    }

    public virtual void Dispose()
    {
        IsInUse = false;
        CommandBuffer.Submit();
    }
}
