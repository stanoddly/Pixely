using System.Diagnostics.CodeAnalysis;

namespace Pixely.RenderOrchestration;

public abstract class RenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    public abstract bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out TRenderContext? renderContext);

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
