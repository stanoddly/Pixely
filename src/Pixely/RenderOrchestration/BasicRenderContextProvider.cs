namespace Pixely.RenderOrchestration;

public class BasicRenderContextProvider : RenderContextProvider<BasicRenderContext>
{
    internal BasicRenderContextProvider()
    {
    }

    public override BasicRenderContext CreateRenderContext(FrameContext frameContext)
    {
        return new BasicRenderContext(frameContext.SwapchainTexture, frameContext.CommandBuffer);
    }
}
