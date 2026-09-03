using Pixely.Input;

namespace Pixely.Pencuil;

internal sealed class PencilSystem : IUpdatable
{
    private readonly Pencil _pencil;
    private readonly PencuilViewRegistry _viewRegistry;
    private readonly List<IPencuilView> _views = new();
    private readonly ViewScope _viewScope;
    private readonly Window _window;
    private readonly ITextInputService _textInputService;
    private bool _textInputActive;

    internal PencilSystem(
        Pencuil pencuil,
        int inputOrder,
        PencuilViewRegistry viewRegistry,
        Window window,
        IMouseService mouseService,
        IKeyboardService keyboardService,
        ITextInputService textInputService)
    {
        Pencil pencil = pencuil.Pencil;
        _pencil = pencil;
        _viewRegistry = viewRegistry;
        _viewScope = pencuil.ViewScope;
        _window = window;
        _textInputService = textInputService;

        mouseService.SubscribeMotion(_viewScope, inputOrder, args =>
        {
            pencil.UpdateCursor((Vector2Int)args.Position);
        });

        mouseService.SubscribeWindowLeave(_viewScope, inputOrder, _ =>
        {
            pencil.UpdateCursor(null);
        });

        mouseService.SubscribeButtonPress(_viewScope, inputOrder, args =>
        {
            if (args.Button == MouseButton.Left)
            {
                pencil.UpdateCursor((Vector2Int)args.Position);
                pencil.SetCursorPressed(true);
                pencil.CursorJustPressed = true;
                pencil.Invalidate();

                if (pencil.IsOverInteractiveArea((Vector2Int)args.Position))
                {
                    args.Consume();
                }
            }
        });

        mouseService.SubscribeButtonRelease(_viewScope, inputOrder, args =>
        {
            if (args.Button == MouseButton.Left)
            {
                bool hadCapture = pencil.HasCapture;
                pencil.UpdateCursor((Vector2Int)args.Position);
                pencil.SetCursorPressed(false);
                pencil.CursorJustReleased = true;
                pencil.Invalidate();

                if (hadCapture || pencil.IsOverInteractiveArea((Vector2Int)args.Position))
                {
                    args.Consume();
                }
            }
        });

        mouseService.SubscribeWheel(_viewScope, inputOrder, args =>
        {
            Vector2Int position = (Vector2Int)args.Position;
            pencil.UpdateCursor(position);

            if (pencil.IsOverScrollArea(position))
            {
                pencil.AddWheelDelta(args.Delta);
                args.Consume();
            }
        });

        keyboardService.SubscribeKeyDown(_viewScope, inputOrder, args =>
        {
            if (pencil.HasFocus && pencil.HandleEditingKeyDown(args.Scancode, args.Keyboard.Shift, args.Keyboard.Ctrl))
            {
                args.Consume();
            }
        });

        textInputService.SubscribeTextInput(_viewScope, inputOrder, args =>
        {
            if (pencil.HasFocus)
            {
                pencil.InsertText(args.Text);
                args.Consume();
            }
        });
    }

    public void Update()
    {
        ShortSize renderSize = _window.RenderSizeInPixels;
        _pencil.UpdateViewport(renderSize.Width, renderSize.Height);

        bool viewsChanged = _viewRegistry.ConsumeChanged(_viewScope);
        if (viewsChanged)
        {
            _viewRegistry.CopyViews(_viewScope, _views);
        }

        bool needsBuild = _pencil.NeedsUpdate | viewsChanged;

        foreach (IPencuilView view in _views)
        {
            needsBuild |= view.ConsumeDirty();
        }

        if (needsBuild)
        {
            _pencil.FocusedControlSeenThisFrame = false;
            _pencil.CapturedControlSeenThisFrame = false;
            _pencil.NeedsUpdate = false;
            _pencil.ResetInteractionData();

            foreach (IPencuilView view in _views)
            {
                view.Build(_pencil);
            }

            _pencil.FinishBuild();
            _pencil.RenderDirty |= _pencil.HaveInstructionsChanged();
            _pencil.MarkInstructionsCompleted();
            _pencil.CycleInstructions();
        }

        bool hasFocus = _pencil.HasFocus;
        if (hasFocus != _textInputActive)
        {
            if (hasFocus)
            {
                _textInputService.Start(_viewScope);
            }
            else
            {
                _textInputService.Stop(_viewScope);
            }
            _textInputActive = hasFocus;
        }

        _pencil.CursorJustReleased = false;
        _pencil.CursorJustPressed = false;
        _pencil.ClearWheelDelta();
    }
}
