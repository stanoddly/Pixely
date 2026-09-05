using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// Feeds mouse events into a <see cref="UiRoot"/>. Subscribing in the constructor is enough: the
/// container builds every singleton, and there is no per-frame work to do because a retained tree
/// only reacts to events.
/// </summary>
internal sealed class UiInputSystem
{
    internal UiInputSystem(UiRoot root, ViewScope viewScope, int inputOrder, IMouseService mouseService)
    {
        // Motion is never consumed. The UI needs to see it to track hover, but a camera that follows
        // the mouse has to keep seeing it too, even while the pointer is over a button.
        mouseService.SubscribeMotion(viewScope, inputOrder, eventArgs => root.PointerMoved((Vector2Int)eventArgs.Position));

        mouseService.SubscribeWindowLeave(viewScope, inputOrder, _ => root.PointerLeft());

        mouseService.SubscribeButtonPress(viewScope, inputOrder, eventArgs =>
        {
            if (eventArgs.Button == MouseButton.Left && root.PointerPressed((Vector2Int)eventArgs.Position))
            {
                eventArgs.Consume();
            }
        });

        mouseService.SubscribeButtonRelease(viewScope, inputOrder, eventArgs =>
        {
            if (eventArgs.Button == MouseButton.Left && root.PointerReleased((Vector2Int)eventArgs.Position))
            {
                eventArgs.Consume();
            }
        });
    }
}
