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

    public RenderCoordinator(
        Window window,
        GpuDevice gpuDevice,
        GpuMemorySystem gpuMemorySystem,
        RenderContextProvider<TRenderContext> renderContextProvider,
        ServiceRegistry<IRenderer<TRenderContext>> renderers)
        : this(window, new RenderCoordinatorGpu(gpuDevice, gpuMemorySystem), renderContextProvider, renderers)
    {
    }

    internal RenderCoordinator(
        Window window,
        IRenderCoordinatorGpu gpu,
        RenderContextProvider<TRenderContext> renderContextProvider,
        ServiceRegistry<IRenderer<TRenderContext>> renderers)
    {
        _window = window;
        _gpu = gpu;
        _renderContextProvider = renderContextProvider;
        _renderers = renderers;
    }

    public bool Execute()
    {
#if BROWSER
        // SDL's browser driver does not hide the canvas, so a frame acquired for a hidden window would present an undrawn
        // texture and blank it. The browser needs no acquire to free finished work: the WebGPU fork frees it on every acquire
        // and submit. Pending uploads are still submitted, so their buffers are not cycled on every update while hidden.
        if (!_window.IsRenderable)
        {
            _gpu.SubmitUploads();
            return false;
        }
#endif

        // The coordinator, not the provider or the context, acquires and submits the command buffer and swapchain texture, so
        // no provider or context has to request the texture or submit; providers, contexts and renderers must not submit or
        // cancel the command buffer. On the desktop they are acquired every frame, even for a window that is not renderable,
        // and the command buffer is submitted even when no texture comes back. SDL's Vulkan backend frees finished GPU work
        // only on a submit whose command buffer requested a swapchain texture, or on a fence wait. Without that request, every
        // upload would keep its buffer in use, and every later update of the buffer would cycle it into a new full-size copy.
        // Metal and D3D12 free finished work on every submit.
        //
        // Apple documents that CAMetalLayer.nextDrawable, which SDL's Metal acquire calls without checking the window's state,
        // waits up to one second when no drawable is free. On 2026-09-30 the acquire was measured on GitHub's macOS 14.8 and
        // 26.6 runners, on the Apple Paravirtual device, with SDL 3.4.14 and 3.4.16, for 300 frames each while the window was
        // minimized and while it was hidden. It never blocked, it returned a texture every time, and memory stayed flat.
        CommandBuffer commandBuffer = _gpu.AcquireCommandBuffer();
        bool hasTexture;
        SwapchainTexture swapchainTexture;
        try
        {
            hasTexture = _window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out swapchainTexture);
        }
        catch
        {
            _gpu.Cancel(commandBuffer);
            throw;
        }

        if (!hasTexture)
        {
#if BROWSER
            // The WebGPU fork returns no texture while its submissions in flight reach the frame limit, counting empty ones,
            // so a submission here would hold a slot until the frame ahead of it finishes. Pending uploads wait for the next
            // drawn frame instead.
            _gpu.Cancel(commandBuffer);
#else
            SubmitUploadsAndFrame(commandBuffer);
#endif
            return false;
        }

        // A frame nobody sees, such as one of a hidden or minimized window on Metal or D3D12, is submitted without a context.
        bool isRenderable = false;
        try
        {
            isRenderable = _window.IsRenderable;
            if (isRenderable)
            {
                Render(commandBuffer, swapchainTexture);
            }
        }
        finally
        {
            // Even when the window, the provider or a renderer throws: a draw recorded before the throw may read a buffer whose
            // update is still pending, and a command buffer with an acquired swapchain texture cannot be cancelled.
            SubmitUploadsAndFrame(commandBuffer);
        }

        return isRenderable;
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
