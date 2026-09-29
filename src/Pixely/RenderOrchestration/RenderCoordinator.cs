using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public interface IRenderCoordinator
{
    void Execute();
}

public sealed class RenderCoordinator<TRenderContext> : IRenderCoordinator
    where TRenderContext : IRenderContext
{
    private readonly Window _window;
    private readonly GpuMemorySystem _gpuMemorySystem;
    private readonly RenderContextProvider<TRenderContext> _renderContextProvider;
    private readonly ServiceRegistry<IRenderer<TRenderContext>> _renderers;

    public RenderCoordinator(
        Window window,
        GpuMemorySystem gpuMemorySystem,
        RenderContextProvider<TRenderContext> renderContextProvider,
        ServiceRegistry<IRenderer<TRenderContext>> renderers)
    {
        _window = window;
        _gpuMemorySystem = gpuMemorySystem;
        _renderContextProvider = renderContextProvider;
        _renderers = renderers;
    }

    public void Execute()
    {
        if (!_window.IsRenderable || !_renderContextProvider.TryCreateRenderContext(_window, out TRenderContext? renderContext))
        {
            // Uploads left unsubmitted keep their buffers in use, so each later update of such a buffer would make SDL cycle it
            // into a new copy for as long as the window cannot render.
            _gpuMemorySystem.Submit();
            return;
        }

        using (renderContext)
        {
            foreach (IRenderer<TRenderContext> renderer in _renderers)
            {
                if (renderer.ViewScope == _window.ViewScope)
                {
                    renderer.Render(renderContext);
                }
            }

            _gpuMemorySystem.Submit();
        }
    }
}
