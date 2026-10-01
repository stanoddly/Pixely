using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public class BasicRenderContextProvider : RenderContextProvider<BasicRenderContext>
{
    internal BasicRenderContextProvider()
    {
    }

    public override BasicRenderContext CreateRenderContext(Window window, CommandBuffer commandBuffer, SwapchainTexture swapchainTexture)
    {
        return new BasicRenderContext(swapchainTexture, commandBuffer);
    }
}
