using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// Feeds mouse, keyboard and text events into a <see cref="UiRoot"/>. Subscribing in the constructor
/// is enough: the container builds every singleton, and there is no per-frame work to do because a
/// retained tree only reacts to events.
/// </summary>
internal sealed class UiInputSystem
{
    private readonly UiRoot _root;
    private readonly Window _window;
    private readonly ITextInputService _textInputService;
    private readonly ViewScope _viewScope;
    private bool _textInputStarted;

    internal UiInputSystem(
        UiRoot root,
        Window window,
        ViewScope viewScope,
        int inputOrder,
        IMouseService mouseService,
        IKeyboardService keyboardService,
        ITextInputService textInputService)
    {
        _root = root;
        _window = window;
        _textInputService = textInputService;
        _viewScope = viewScope;

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

        SubscribeKeyboard(keyboardService, textInputService, viewScope, inputOrder);
    }

    private void SubscribeKeyboard(IKeyboardService keyboardService, ITextInputService textInputService, ViewScope viewScope, int inputOrder)
    {
        keyboardService.SubscribeKeyDown(viewScope, inputOrder, eventArgs =>
        {
            if (_root.KeyPressed(eventArgs.Scancode, eventArgs.Keyboard, eventArgs.Repeat))
            {
                eventArgs.Consume();
            }
        });

        // Committed text only. What the platform is still composing goes to TextEditing, which nothing
        // here draws yet, so an IME's half-finished word is not shown mid-composition.
        textInputService.SubscribeTextInput(viewScope, inputOrder, eventArgs =>
        {
            if (_root.TextEntered(eventArgs.Text))
            {
                eventArgs.Consume();
            }
        });

        // Driven from where focus ended up rather than from any transition on the way there, so a
        // field that commits and hands focus to the next one does not stop and restart the platform's
        // text input in between.
        _root.FocusChanged += focused => SetTextInputActive(focused != null);
    }

    private void SetTextInputActive(bool active)
    {
        if (_textInputStarted == active)
        {
            return;
        }

        _textInputStarted = active;

        if (active)
        {
            _textInputService.Start(_viewScope);
        }
        else
        {
            _textInputService.Stop(_viewScope);
        }
    }

    /// <summary>
    /// Converts a window position into the space the tree was laid out in. The root lays out in its
    /// viewport, which is the render target's size rather than the window's — those differ on a
    /// high-DPI display, and they differ again when the UI draws into a target of its own — while
    /// the mouse reports window coordinates. Without this the two disagree by the display scale and
    /// every hit test lands somewhere else.
    /// </summary>
    private Vector2Int ToUiPosition(System.Numerics.Vector2 windowPosition) =>
        ToUiPosition(windowPosition, _window.Size, _root.ViewportSize);

    /// <inheritdoc cref="ToUiPosition(System.Numerics.Vector2)"/>
    internal static Vector2Int ToUiPosition(System.Numerics.Vector2 windowPosition, Size<uint> windowSize, Vector2Int viewport) =>
        new(Scale(windowPosition.X, viewport.X, windowSize.Width), Scale(windowPosition.Y, viewport.Y, windowSize.Height));

    /// <summary>
    /// Scales one axis, flooring rather than truncating. A pixel covers the half-open span from its
    /// own coordinate to the next, so the position left of the origin belongs to pixel -1; truncation
    /// rounds it towards zero into pixel 0 instead, which turns a release just outside an element's
    /// left edge into a click on it.
    /// </summary>
    /// <remarks>
    /// Each axis falls back on its own. Before the first build there is no viewport to scale into,
    /// and a window can report zero while minimised; scaling the other axis is still right, and
    /// abandoning both because one is unusable would put the pointer somewhere it never was.
    /// </remarks>
    private static int Scale(float windowPosition, int viewportExtent, uint windowExtent) =>
        viewportExtent == 0 || windowExtent == 0
            ? (int)MathF.Floor(windowPosition)
            : (int)MathF.Floor(windowPosition * viewportExtent / windowExtent);
}
