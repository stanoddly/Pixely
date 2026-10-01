namespace Pixely.RenderOrchestration;

public abstract class RenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    /// <summary>
    /// Creates the context for the frame the render coordinator acquired. The coordinator calls this every frame it gets a
    /// swapchain texture, even while the window is not renderable, and then disposes the context without rendering. The
    /// context owns the frame's command buffer and submits it when disposed; a texture acquired on it forbids cancelling it.
    /// One provider can serve several windows, so <see cref="FrameContext.Window"/> tells them apart.
    /// </summary>
    public abstract TRenderContext CreateRenderContext(FrameContext frameContext);

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
