using System.Diagnostics.CodeAnalysis;

namespace Pixely.RenderOrchestration;

public abstract class RenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    /// <summary>
    /// Creates the context for one frame of <paramref name="window"/>. The render coordinator calls this every frame, even
    /// while the window is not renderable, and then disposes the context without rendering. When no swapchain texture comes
    /// back, submit the command buffer that requested it instead of cancelling it: SDL's Vulkan backend frees finished GPU
    /// work only on such a submit.
    /// </summary>
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
