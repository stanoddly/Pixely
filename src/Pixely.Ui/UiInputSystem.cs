using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// Feeds mouse events into a <see cref="UiRoot"/>. Subscribing in the constructor is enough: the
/// container builds every singleton, and there is no per-frame work to do because a retained tree
/// only reacts to events.
/// </summary>
internal sealed class UiInputSystem
{
    private readonly UiRoot _root;
    private readonly Window _window;

    internal UiInputSystem(UiRoot root, Window window, ViewScope viewScope, int inputOrder, IMouseService mouseService)
    {
        _root = root;
        _window = window;

        // Motion is never consumed. The UI needs to see it to track hover, but a camera that follows
        // the mouse has to keep seeing it too, even while the pointer is over a button.
        mouseService.SubscribeMotion(viewScope, inputOrder, eventArgs => _root.PointerMoved(ToUiPosition(eventArgs.Position)));

        mouseService.SubscribeWindowLeave(viewScope, inputOrder, _ => _root.PointerLeft());

        mouseService.SubscribeButtonPress(viewScope, inputOrder, eventArgs =>
        {
            if (_root.PointerPressed(ToUiPosition(eventArgs.Position), eventArgs.Button))
            {
                eventArgs.Consume();
            }
        });

        mouseService.SubscribeButtonRelease(viewScope, inputOrder, eventArgs =>
        {
            if (_root.PointerReleased(ToUiPosition(eventArgs.Position), eventArgs.Button))
            {
                eventArgs.Consume();
            }
        });
    }

    /// <summary>
    /// Converts a window position into the space the tree was laid out in. The root lays out in its
    /// viewport, which is the render target's size rather than the window's — those differ on a
    /// high-DPI display, and they differ again when the UI draws into a target of its own — while
    /// the mouse reports window coordinates. Without this the two disagree by the display scale and
    /// every hit test lands somewhere else.
    /// </summary>
    private Vector2Int ToUiPosition(System.Numerics.Vector2 windowPosition)
    {
        Size<uint> windowSize = _window.Size;
        Vector2Int viewport = _root.ViewportSize;

        // Before the first build there is no viewport to scale into, and a window can report zero
        // while minimised. Either way the raw position is the best answer available and is what the
        // unscaled case would have used anyway.
        if (windowSize.Width == 0 || windowSize.Height == 0 || viewport.X == 0 || viewport.Y == 0)
        {
            return (Vector2Int)windowPosition;
        }

        return new Vector2Int(
            (int)(windowPosition.X * viewport.X / windowSize.Width),
            (int)(windowPosition.Y * viewport.Y / windowSize.Height));
    }
}
