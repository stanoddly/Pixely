namespace Pixely.RenderOrchestration;

public abstract class RenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    /// <summary>
    /// Creates the context for a frame the render coordinator acquired. The coordinator calls this only for a frame it draws:
    /// the window is renderable and a swapchain texture came back. It disposes the context after the renderers, then submits
    /// the command buffer itself, so neither the context nor a renderer submits or cancels it. One provider can serve several
    /// windows, so <see cref="FrameContext.Window"/> tells them apart.
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
