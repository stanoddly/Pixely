using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public interface IRenderCoordinator
{
    /// <summary>
    /// Runs one frame for the window. Returns true when the window could show the frame: it was renderable and a swapchain
    /// texture came back, even if no renderer drew. When every coordinator returns false, the frame loop waits up to 16 ms
    /// for an event, except in the browser.
    /// </summary>
    bool Execute();
}

public sealed class RenderCoordinator<TRenderContext> : IRenderCoordinator
    where TRenderContext : IRenderContext
{
    private readonly Window _window;
    private readonly IRenderCoordinatorGpu _gpu;
    private readonly RenderContextProvider<TRenderContext> _renderContextProvider;
    private readonly ServiceRegistry<IRenderer<TRenderContext>> _renderers;
    private readonly IFrameTimingRecorder _frameTimingRecorder;

    public RenderCoordinator(
        Window window,
        GpuDevice gpuDevice,
        GpuMemorySystem gpuMemorySystem,
        RenderContextProvider<TRenderContext> renderContextProvider,
        ServiceRegistry<IRenderer<TRenderContext>> renderers)
        : this(window, new RenderCoordinatorGpu(gpuDevice, gpuMemorySystem), renderContextProvider, renderers, NullFrameTimingRecorder.Instance)
    {
    }

    internal RenderCoordinator(
        Window window,
        IRenderCoordinatorGpu gpu,
        RenderContextProvider<TRenderContext> renderContextProvider,
        ServiceRegistry<IRenderer<TRenderContext>> renderers,
        IFrameTimingRecorder frameTimingRecorder)
    {
        _window = window;
        _gpu = gpu;
        _renderContextProvider = renderContextProvider;
        _renderers = renderers;
        _frameTimingRecorder = frameTimingRecorder;
    }

    public bool Execute()
    {
        // A window that is not renderable gets no frame, so nothing presents an undrawn texture for a hidden or minimized
        // window. Pending uploads are still submitted, so the buffers they update are not cycled into new copies on every
        // update while it is hidden. This relies on every backend freeing finished work on any submit, which SDL's Vulkan
        // backend does only since XDL v3.4.20-7. On Vulkan, defrag and the freeing of empty allocations still wait for a
        // submit that requested a swapchain texture.
        if (!_window.IsRenderable)
        {
            _gpu.SubmitUploads();
            return false;
        }

        // The coordinator, not the provider or the context, acquires and submits the command buffer and swapchain texture, so
        // no provider or context has to request the texture or submit; providers, contexts and renderers must not submit or
        // cancel the command buffer.
        CommandBuffer commandBuffer = _gpu.AcquireCommandBuffer();
        bool hasTexture;
        SwapchainTexture swapchainTexture;
        _frameTimingRecorder.BeginSwapchainWait();
        try
        {
            hasTexture = _window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out swapchainTexture);
        }
        catch
        {
            _gpu.Cancel(commandBuffer);
            throw;
        }
        _frameTimingRecorder.EndSwapchainWait();

        if (!hasTexture)
        {
#if BROWSER
            // The WebGPU fork returns no texture while its submissions in flight reach the frame limit, counting empty ones,
            // so a submission here would hold a slot until the frame ahead of it finishes. Pending uploads wait for the next
            // drawn frame instead.
            _gpu.Cancel(commandBuffer);
#else
            // Submitted rather than cancelled, because on Vulkan only a submit that requested a swapchain texture can defragment
            // memory and free empty allocations.
            SubmitUploadsAndFrame(commandBuffer);
#endif
            return false;
        }

        _frameTimingRecorder.OnSwapchainAcquired(_window, swapchainTexture);

        try
        {
            Render(commandBuffer, swapchainTexture);
        }
        finally
        {
            // Even when the provider or a renderer throws: a draw recorded before the throw may read a buffer whose update is
            // still pending, and a command buffer with an acquired swapchain texture cannot be cancelled.
            SubmitUploadsAndFrame(commandBuffer);
        }

        return true;
    }

    // Builds the frame's context, runs the window's renderers and disposes the context.
    internal void Render(CommandBuffer commandBuffer, SwapchainTexture swapchainTexture)
    {
        FrameContext frameContext = new() { Window = _window, CommandBuffer = commandBuffer, SwapchainTexture = swapchainTexture };
        using (TRenderContext renderContext = _renderContextProvider.CreateRenderContext(frameContext))
        {
            foreach (IRenderer<TRenderContext> renderer in _renderers)
            {
                if (renderer.ViewScope == _window.ViewScope)
                {
                    renderer.Render(renderContext);
                }
            }
        }
    }

    // Uploads go first, because the frame's work may read them. The frame is submitted even when submitting the uploads
    // throws, so an acquired swapchain texture is not abandoned.
    private void SubmitUploadsAndFrame(CommandBuffer commandBuffer)
    {
        try
        {
            _gpu.SubmitUploads();
        }
        finally
        {
            _gpu.Submit(commandBuffer);
        }
    }
}
