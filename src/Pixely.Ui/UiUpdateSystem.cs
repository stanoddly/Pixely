namespace Pixely.Ui;

/// <summary>
/// Builds the tree in the update phase. A build raises application callbacks — pointer enter and
/// leave as layout moves under a stationary pointer, focus lost, and every custom element, layout
/// and drawable in the tree — and a renderer may not run those: the renderers sharing a frame are
/// entitled to domain data that does not change underneath them.
/// </summary>
internal sealed class UiUpdateSystem : IUpdatable, IOrderable
{
    private readonly UiRoot _root;
    private readonly Func<Vector2Int> _viewport;
    private readonly Func<bool> _isVisible;

    /// <param name="viewport">
    /// The size to lay out against, read per frame rather than held: it changes as the window is
    /// resized. A delegate rather than the window itself, because the window's size and visibility
    /// are non-virtual SDL calls and this has to be constructible in a test.
    /// </param>
    internal UiUpdateSystem(UiRoot root, Func<Vector2Int> viewport, Func<bool> isVisible, int updateOrder)
    {
        _root = root;
        _viewport = viewport;
        _isVisible = isVisible;
        Order = updateOrder;
    }

    public int Order { get; }

    public void Update()
    {
        // Visibility first, so a hidden window does not pay for a size call it will not use. Nothing
        // skipped the build for it before this class existed either: the only caller of Update was
        // the renderer, which RenderCoordinator had already skipped.
        if (!_isVisible())
        {
            return;
        }

        Vector2Int viewport = _viewport();

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
