using Pixely.RenderOrchestration;

namespace Pixely.Ui;

/// <summary>
/// Builds the tree in the update phase. A build raises application callbacks, such as pointer enter
/// and leave as layout moves under a stationary pointer, focus lost, and every custom element,
/// layout and drawable in the tree. A renderer may not run those, because the renderers sharing a
/// frame are entitled to domain data that does not change underneath them.
/// </summary>
internal sealed class UiUpdateSystem<TRenderContext> : IUpdatable
    where TRenderContext : IRenderContext
{
    private readonly UiRoot _root;
    private readonly Window _window;
    private readonly RenderContextProvider<TRenderContext> _contextProvider;

    internal UiUpdateSystem(UiRoot root, Window window, RenderContextProvider<TRenderContext> contextProvider, int updateOrder)
    {
        _root = root;
        _window = window;
        _contextProvider = contextProvider;
        UpdateOrder = updateOrder;
    }

    public int UpdateOrder { get; }

    public void Update()
    {
        // Visibility first, so a hidden window does not pay for a size call it will not use. Nothing
        // skipped the build for it before this class existed either: the only caller of Update was
        // the renderer, which RenderCoordinator had already skipped.
        if (!_window.IsVisible)
        {
            return;
        }

        // Asked of the provider rather than the window, because the provider is what decides the
        // colour target. Read per frame, since it changes as the window is resized.
        ShortSize targetSize = _contextProvider.GetColorTargetSize(_window);
        Vector2Int viewport = new(targetSize.Width, targetSize.Height);

        // A window with no area has nothing to lay out against. Building against it would invalidate
        // every layer now and again on restore, for a tree nothing is going to draw.
        if (viewport.X <= 0 || viewport.Y <= 0)
        {
            return;
        }

        _root.SetViewportSize(viewport);
        _root.Update();
    }
}
