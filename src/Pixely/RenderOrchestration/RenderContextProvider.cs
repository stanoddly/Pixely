using System.Diagnostics.CodeAnalysis;

namespace Pixely.RenderOrchestration;

public abstract class RenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    public abstract bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out TRenderContext? renderContext);

    /// <summary>
    /// Whether the window has a colour target to draw into this frame. Update systems that only work for the
    /// renderer skip their frame when this is false. The default requires a visible window, since a hidden
    /// window has no swapchain image; override it when the context draws into something other than the window.
    /// </summary>
    public virtual bool CanRender(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.IsVisible;
    }

    /// <summary>
    /// The size of the colour target <see cref="TryCreateRenderContext"/> will produce, answered
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
