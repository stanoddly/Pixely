using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public interface IRenderCoordinator
{
    // Whether renderers drew a frame to the window.
    bool Execute();
}

public sealed class RenderCoordinator<TRenderContext> : IRenderCoordinator
    where TRenderContext : IRenderContext
{
    private readonly Window _window;
    private readonly GpuDevice _gpuDevice;
    private readonly GpuMemorySystem _gpuMemorySystem;
    private readonly RenderContextProvider<TRenderContext> _renderContextProvider;
    private readonly ServiceRegistry<IRenderer<TRenderContext>> _renderers;

    public RenderCoordinator(
        Window window,
        GpuDevice gpuDevice,
        GpuMemorySystem gpuMemorySystem,
        RenderContextProvider<TRenderContext> renderContextProvider,
        ServiceRegistry<IRenderer<TRenderContext>> renderers)
    {
        _window = window;
        _gpuDevice = gpuDevice;
        _gpuMemorySystem = gpuMemorySystem;
        _renderContextProvider = renderContextProvider;
        _renderers = renderers;
    }

    public bool Execute()
    {
        // The coordinator, not the provider, acquires the command buffer and swapchain texture, so no provider can skip the
        // request or cancel the command buffer. They are acquired every frame, even for a window that is not renderable, so the
        // command buffer requests a swapchain texture and is submitted. SDL's Vulkan backend frees finished GPU work only on a submit whose command buffer
        // requested a swapchain texture, or on a fence wait. Without that request, every upload would keep its buffer in use,
        // and every later update of the buffer would cycle it into a new full-size copy. Metal and D3D12 free finished work on
        // every submit.
        //
        // Apple documents that CAMetalLayer.nextDrawable, which SDL's Metal acquire calls without checking the window's state,
        // waits up to one second when no drawable is free. On 2026-09-30 the acquire was measured on GitHub's macOS 14.8 and
        // 26.6 runners, on the Apple Paravirtual device, with SDL 3.4.14 and 3.4.16, for 300 frames each while the window was
        // minimized and while it was hidden. It never blocked, it returned a texture every time, and memory stayed flat.
        if (!_window.TryAcquireFrame(_gpuDevice, out FrameContext frameContext))
        {
            // In the browser, pending uploads wait for the next drawn frame instead: a submission there takes one of the
            // frame-limit slots that kept the acquire from returning a texture.
#if !BROWSER
            _gpuMemorySystem.Submit();
#endif
            return false;
        }

        TRenderContext renderContext;
        try
        {
            renderContext = _renderContextProvider.CreateRenderContext(frameContext);
        }
        catch
        {
            // The context never took ownership, and a command buffer with an acquired swapchain texture cannot be cancelled.
            frameContext.CommandBuffer.Submit();
            throw;
        }

        using (renderContext)
        {
            bool isRenderable = _window.IsRenderable;
            if (isRenderable)
            {
                foreach (IRenderer<TRenderContext> renderer in _renderers)
                {
                    if (renderer.ViewScope == _window.ViewScope)
                    {
                        renderer.Render(renderContext);
                    }
                }
            }

            _gpuMemorySystem.Submit();
            return isRenderable;
        }
    }
}
