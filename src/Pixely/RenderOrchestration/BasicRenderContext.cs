using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// A window's swapchain texture and the command buffer that draws into it for one frame. <see cref="BasicRenderContextProvider{TRenderContext}"/>
/// hands the same context out again every frame; between <see cref="Dispose"/> and the next frame it holds neither, and
/// reading them throws <see cref="ObjectDisposedException"/>.
/// </summary>
public class BasicRenderContext : IRenderContext
{
    private SwapchainTexture? _swapchainTexture;
    private CommandBuffer? _commandBuffer;

    public BasicRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        Begin(swapchainTexture, commandBuffer);
    }

    /// <summary>For a context that <see cref="BasicRenderContextProvider{TRenderContext}"/> creates once and reuses.</summary>
    protected internal BasicRenderContext()
    {
    }

    public SwapchainTexture SwapchainTexture => _swapchainTexture ?? throw new ObjectDisposedException(GetType().Name);
    public CommandBuffer CommandBuffer => _commandBuffer ?? throw new ObjectDisposedException(GetType().Name);
    public Texture ColorTarget => SwapchainTexture;

    // Holding a command buffer is what makes the context this frame's; Dispose gives it up.
    internal bool IsInUse => _commandBuffer != null;

    internal void Begin(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        _swapchainTexture = swapchainTexture;
        _commandBuffer = commandBuffer;
    }

    /// <summary>
    /// Submits the frame's command buffer. A pass a renderer left open is ended first instead of throwing, since the
    /// coordinator disposes the context while an exception from that renderer may be on its way out.
    /// </summary>
    public virtual void Dispose()
    {
        CommandBuffer? commandBuffer = _commandBuffer;
        _commandBuffer = null;
        _swapchainTexture = null;
        commandBuffer?.SubmitEndingOpenPass();
    }
}
