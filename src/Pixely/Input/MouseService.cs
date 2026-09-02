using System.Numerics;
using System.Runtime.InteropServices;
using Pixely.Utilities;
using SDL;

namespace Pixely.Input;

public enum MouseButton : byte
{
    Left = 1,
    Middle = 2,
    Right = 3,
    X1 = 4,
    X2 = 5
}

public class Mouse
{
    internal Mouse(SDL_MouseID mouseId)
    {
        MouseId = mouseId;
    }

    public SDL_MouseID MouseId { get; }
    public MouseState State { get; private set; }

    public Vector2 Position
    {
        get
        {
            return State.Position;
        }
        internal set
        {
            State = State with { Position = value };
        }
    }

    public int ButtonFlags
    {
        get
        {
            return State.ButtonFlags;
        }
        internal set
        {
            State = State with { ButtonFlags = value };
        }
    }

    public bool IsPressed(MouseButton button)
    {
        return State.IsPressed(button);
    }

    internal bool Set(MouseButton button)
    {
        int mask = 1 << ((int)button - 1);
        bool wasUnset = (ButtonFlags & mask) == 0;
        ButtonFlags |= mask;
        return wasUnset;
    }

    internal void Unset(MouseButton button)
    {
        int mask = 1 << ((int)button - 1);
        ButtonFlags &= ~mask;
    }
}

public class MouseButtonEventArgs : ConsumableInputEventArgs
{
    public Mouse Mouse { get; internal set; } = null!;
    public MouseButton Button { get; internal set; }
    public Vector2 Position { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class MouseMotionEventArgs : ConsumableInputEventArgs
{
    public Mouse Mouse { get; internal set; } = null!;
    public Vector2 Position { get; internal set; }
    public Vector2 RelativeMotion { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class MouseWheelEventArgs : ConsumableInputEventArgs
{
    public Mouse Mouse { get; internal set; } = null!;
    public Vector2 Delta { get; internal set; }
    public Vector2 Position { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class MouseWindowPresenceEventArgs : ConsumableInputEventArgs
{
    public bool IsInWindow { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class MouseService : IMouseService
{
    private readonly WindowRegistry _windowRegistry;
    private readonly Dictionary<SDL_MouseID, Mouse> _mice = new();

    // Cached to avoid per-event allocations. Do not hold references to event args beyond the callback.
    private readonly MouseButtonEventArgs _buttonEventArgs = new();
    private readonly MouseMotionEventArgs _motionEventArgs = new();
    private readonly MouseWheelEventArgs _wheelEventArgs = new();
    private readonly MouseWindowPresenceEventArgs _windowPresenceEventArgs = new();

    private readonly ViewScopedPriorityEventHandlers<MouseButtonEventArgs> _buttonPressHandlers = new();
    private readonly ViewScopedPriorityEventHandlers<MouseButtonEventArgs> _buttonReleaseHandlers = new();
    private readonly ViewScopedPriorityEventHandlers<MouseMotionEventArgs> _motionHandlers = new();
    private readonly ViewScopedPriorityEventHandlers<MouseWheelEventArgs> _wheelHandlers = new();
    private readonly ViewScopedPriorityEventHandlers<MouseWindowPresenceEventArgs> _windowEnterHandlers = new();
    private readonly ViewScopedPriorityEventHandlers<MouseWindowPresenceEventArgs> _windowLeaveHandlers = new();

    internal MouseService(WindowRegistry windowRegistry)
    {
        _windowRegistry = windowRegistry;
    }

    public bool IsInWindow(ViewScope viewScope = default)
    {
        Window window = _windowRegistry.GetWindow(viewScope);
        unsafe
        {
            Pointer<SDL_Window> mouseFocusWindow = SDL3.SDL_GetMouseFocus();
            return !mouseFocusWindow.IsNull &&
                (uint)SDL3.SDL_GetWindowID(mouseFocusWindow) == window.SdlId;
        }
    }

    public MouseState GetGlobalState()
    {
        float x;
        float y;
        SDL_MouseButtonFlags buttonFlags;

        unsafe
        {
            buttonFlags = SDL3.SDL_GetGlobalMouseState(&x, &y);
        }

        return new MouseState(new Vector2(x, y), (int)buttonFlags);
    }

    public event InputEventHandler<MouseButtonEventArgs> ButtonPress
    {
        add => _buttonPressHandlers.Add(default, 0, value);
        remove => _buttonPressHandlers.Remove(default, value);
    }

    public event InputEventHandler<MouseButtonEventArgs> ButtonRelease
    {
        add => _buttonReleaseHandlers.Add(default, 0, value);
        remove => _buttonReleaseHandlers.Remove(default, value);
    }

    public event InputEventHandler<MouseMotionEventArgs> Motion
    {
        add => _motionHandlers.Add(default, 0, value);
        remove => _motionHandlers.Remove(default, value);
    }

    public event InputEventHandler<MouseWheelEventArgs> Wheel
    {
        add => _wheelHandlers.Add(default, 0, value);
        remove => _wheelHandlers.Remove(default, value);
    }

    public event InputEventHandler<MouseWindowPresenceEventArgs> WindowEnter
    {
        add => _windowEnterHandlers.Add(default, 0, value);
        remove => _windowEnterHandlers.Remove(default, value);
    }

    public event InputEventHandler<MouseWindowPresenceEventArgs> WindowLeave
    {
        add => _windowLeaveHandlers.Add(default, 0, value);
        remove => _windowLeaveHandlers.Remove(default, value);
    }

    public void SubscribeButtonPress(int priority, InputEventHandler<MouseButtonEventArgs> handler)
    {
        _buttonPressHandlers.Add(default, priority, handler);
    }

    public void SubscribeButtonRelease(int priority, InputEventHandler<MouseButtonEventArgs> handler)
    {
        _buttonReleaseHandlers.Add(default, priority, handler);
    }

    public void SubscribeMotion(int priority, InputEventHandler<MouseMotionEventArgs> handler)
    {
        _motionHandlers.Add(default, priority, handler);
    }

    public void SubscribeWheel(int priority, InputEventHandler<MouseWheelEventArgs> handler)
    {
        _wheelHandlers.Add(default, priority, handler);
    }

    public void SubscribeWindowEnter(int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler)
    {
        _windowEnterHandlers.Add(default, priority, handler);
    }

    public void SubscribeWindowLeave(int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler)
    {
        _windowLeaveHandlers.Add(default, priority, handler);
    }

    public void SubscribeButtonPress(ViewScope viewScope, int priority, InputEventHandler<MouseButtonEventArgs> handler)
    {
        _buttonPressHandlers.Add(viewScope, priority, handler);
    }

    public void SubscribeButtonRelease(ViewScope viewScope, int priority, InputEventHandler<MouseButtonEventArgs> handler)
    {
        _buttonReleaseHandlers.Add(viewScope, priority, handler);
    }

    public void SubscribeMotion(ViewScope viewScope, int priority, InputEventHandler<MouseMotionEventArgs> handler)
    {
        _motionHandlers.Add(viewScope, priority, handler);
    }

    public void SubscribeWheel(ViewScope viewScope, int priority, InputEventHandler<MouseWheelEventArgs> handler)
    {
        _wheelHandlers.Add(viewScope, priority, handler);
    }

    public void SubscribeWindowEnter(ViewScope viewScope, int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler)
    {
        _windowEnterHandlers.Add(viewScope, priority, handler);
    }

    public void SubscribeWindowLeave(ViewScope viewScope, int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler)
    {
        _windowLeaveHandlers.Add(viewScope, priority, handler);
    }

    internal void OnMouseWindowPresenceEvent(ViewScope viewScope, bool isInWindow, ulong timestamp)
    {
        _windowPresenceEventArgs.IsInWindow = isInWindow;
        _windowPresenceEventArgs.Timestamp = timestamp;

        ViewScopedPriorityEventHandlers<MouseWindowPresenceEventArgs> handlers = isInWindow
            ? _windowEnterHandlers
            : _windowLeaveHandlers;

        handlers.Invoke(viewScope, _windowPresenceEventArgs);
    }

    internal void OnMouseButtonEvent(ViewScope viewScope, SDL_MouseID mouseId, MouseButton button, Vector2 position, bool isPressed, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);

        mouse.Position = position;

        _buttonEventArgs.Mouse = mouse;
        _buttonEventArgs.Button = button;
        _buttonEventArgs.Position = position;
        _buttonEventArgs.Timestamp = timestamp;
        if (isPressed)
        {
            if (mouse.Set(button))
            {
                _buttonPressHandlers.Invoke(viewScope, _buttonEventArgs);
            }
        }
        else
        {
            mouse.Unset(button);

            _buttonReleaseHandlers.Invoke(viewScope, _buttonEventArgs);
        }
    }

    internal void OnMouseMotionEvent(ViewScope viewScope, SDL_MouseID mouseId, Vector2 position, Vector2 relativeMotion, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);

        mouse.Position = position;

        _motionEventArgs.Mouse = mouse;
        _motionEventArgs.Position = position;
        _motionEventArgs.RelativeMotion = relativeMotion;
        _motionEventArgs.Timestamp = timestamp;
        _motionHandlers.Invoke(viewScope, _motionEventArgs);
    }

    internal void OnMouseMoveTo(ViewScope viewScope, SDL_MouseID mouseId, Vector2 position, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);
        OnMouseMotionEvent(viewScope, mouseId, position, position - mouse.Position, timestamp);
    }

    internal void OnMouseMoveBy(ViewScope viewScope, SDL_MouseID mouseId, Vector2 relativeMotion, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);
        OnMouseMotionEvent(viewScope, mouseId, mouse.Position + relativeMotion, relativeMotion, timestamp);
    }

    internal void OnMouseWheelEvent(ViewScope viewScope, SDL_MouseID mouseId, Vector2 delta, Vector2 position, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);

        mouse.Position = position;

        _wheelEventArgs.Mouse = mouse;
        _wheelEventArgs.Delta = delta;
        _wheelEventArgs.Position = position;
        _wheelEventArgs.Timestamp = timestamp;
        _wheelHandlers.Invoke(viewScope, _wheelEventArgs);
    }

    private Mouse GetOrCreateMouse(SDL_MouseID mouseId)
    {
        ref Mouse? mouse = ref CollectionsMarshal.GetValueRefOrAddDefault(_mice, mouseId, out bool exists);

        if (!exists || mouse == null)
        {
            mouse = new Mouse(mouseId);
        }

        return mouse;
    }
}
