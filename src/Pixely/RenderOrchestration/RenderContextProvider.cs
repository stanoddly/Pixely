using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public abstract class RenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    /// <summary>
    /// Creates the context for one frame of <paramref name="window"/> from the command buffer and swapchain texture the render
    /// coordinator acquired for it. The coordinator calls this every frame it gets a swapchain texture, even while the window
    /// is not renderable, and then disposes the context without rendering. The context owns
    /// <paramref name="commandBuffer"/> and submits it when disposed; a texture acquired on it forbids cancelling it.
    /// </summary>
    public abstract TRenderContext CreateRenderContext(Window window, CommandBuffer commandBuffer, SwapchainTexture swapchainTexture);

    /// <summary>
    /// The size of the colour target <see cref="CreateRenderContext"/> will produce, answered
    /// without acquiring one. Systems that lay out against the target read this in the update phase,
    /// where no render context exists yet. Override it when the context draws into something other
    /// than the window; the default is what a swapchain target is.
    /// </summary>
    public virtual ShortSize GetColorTargetSize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.RenderSizeInPixels;
    }
}
