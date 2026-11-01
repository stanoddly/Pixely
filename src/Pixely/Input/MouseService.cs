using System.Numerics;
using System.Runtime.InteropServices;
using Pixely.Collections;
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

    internal bool Unset(MouseButton button)
    {
        int mask = 1 << ((int)button - 1);
        bool wasSet = (ButtonFlags & mask) != 0;
        ButtonFlags &= ~mask;
        return wasSet;
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
    private readonly Dictionary<SDL_MouseID, Mouse> _mice = new();
    private bool _isInWindow;

    // Cached to avoid per-event allocations. Do not hold references to event args beyond the callback.
    private readonly MouseButtonEventArgs _buttonEventArgs = new();
    private readonly MouseMotionEventArgs _motionEventArgs = new();
    private readonly MouseWheelEventArgs _wheelEventArgs = new();
    private readonly MouseWindowPresenceEventArgs _windowPresenceEventArgs = new();

    private readonly OrderedEventHandlers<MouseButtonEventArgs> _buttonPressHandlers = new();
    private readonly OrderedEventHandlers<MouseButtonEventArgs> _buttonReleaseHandlers = new();
    private readonly OrderedEventHandlers<MouseMotionEventArgs> _motionHandlers = new();
    private readonly OrderedEventHandlers<MouseWheelEventArgs> _wheelHandlers = new();
    private readonly OrderedEventHandlers<MouseWindowPresenceEventArgs> _windowEnterHandlers = new();
    private readonly OrderedEventHandlers<MouseWindowPresenceEventArgs> _windowLeaveHandlers = new();

    internal MouseService()
    {
    }

    public bool IsInWindow() => _isInWindow;

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
        add => _buttonPressHandlers.Add(0, value);
        remove => _buttonPressHandlers.Remove(value);
    }

    public event InputEventHandler<MouseButtonEventArgs> ButtonRelease
    {
        add => _buttonReleaseHandlers.Add(0, value);
        remove => _buttonReleaseHandlers.Remove(value);
    }

    public event InputEventHandler<MouseMotionEventArgs> Motion
    {
        add => _motionHandlers.Add(0, value);
        remove => _motionHandlers.Remove(value);
    }

    public event InputEventHandler<MouseWheelEventArgs> Wheel
    {
        add => _wheelHandlers.Add(0, value);
        remove => _wheelHandlers.Remove(value);
    }

    public event InputEventHandler<MouseWindowPresenceEventArgs> WindowEnter
    {
        add => _windowEnterHandlers.Add(0, value);
        remove => _windowEnterHandlers.Remove(value);
    }

    public event InputEventHandler<MouseWindowPresenceEventArgs> WindowLeave
    {
        add => _windowLeaveHandlers.Add(0, value);
        remove => _windowLeaveHandlers.Remove(value);
    }

    public void SubscribeButtonPress(int order, InputEventHandler<MouseButtonEventArgs> handler)
    {
        _buttonPressHandlers.Add(order, handler);
    }

    public void SubscribeButtonRelease(int order, InputEventHandler<MouseButtonEventArgs> handler)
    {
        _buttonReleaseHandlers.Add(order, handler);
    }

    public void SubscribeMotion(int order, InputEventHandler<MouseMotionEventArgs> handler)
    {
        _motionHandlers.Add(order, handler);
    }

    public void SubscribeWheel(int order, InputEventHandler<MouseWheelEventArgs> handler)
    {
        _wheelHandlers.Add(order, handler);
    }

    public void SubscribeWindowEnter(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler)
    {
        _windowEnterHandlers.Add(order, handler);
    }

    public void SubscribeWindowLeave(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler)
    {
        _windowLeaveHandlers.Add(order, handler);
    }

    // Presence is recorded before the handlers run so a handler observes the new IsInWindow.
    internal void OnMouseWindowPresenceEvent(bool isInWindow, ulong timestamp)
    {
        if (isInWindow == _isInWindow)
        {
            return;
        }

        _isInWindow = isInWindow;

        _windowPresenceEventArgs.IsInWindow = isInWindow;
        _windowPresenceEventArgs.Timestamp = timestamp;

        OrderedEventHandlers<MouseWindowPresenceEventArgs> handlers = isInWindow
            ? _windowEnterHandlers
            : _windowLeaveHandlers;

        handlers.Invoke(_windowPresenceEventArgs);
    }

    internal void OnMouseButtonEvent(SDL_MouseID mouseId, MouseButton button, Vector2 position, bool isPressed, ulong timestamp)
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
                _buttonPressHandlers.Invoke(_buttonEventArgs);
            }
        }
        else
        {
            // A release without a recorded press reaches handlers as a gesture that never started, so it is
            // dropped. SDL can suppress a press on its own, for example the click that activates a window.
            if (mouse.Unset(button))
            {
                _buttonReleaseHandlers.Invoke(_buttonEventArgs);
            }
        }
    }

    internal void OnMouseMotionEvent(SDL_MouseID mouseId, Vector2 position, Vector2 relativeMotion, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);

        mouse.Position = position;

        _motionEventArgs.Mouse = mouse;
        _motionEventArgs.Position = position;
        _motionEventArgs.RelativeMotion = relativeMotion;
        _motionEventArgs.Timestamp = timestamp;
        _motionHandlers.Invoke(_motionEventArgs);
    }

    internal void OnMouseMoveTo(SDL_MouseID mouseId, Vector2 position, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);
        OnMouseMotionEvent(mouseId, position, position - mouse.Position, timestamp);
    }

    internal void OnMouseMoveBy(SDL_MouseID mouseId, Vector2 relativeMotion, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);
        OnMouseMotionEvent(mouseId, mouse.Position + relativeMotion, relativeMotion, timestamp);
    }

    internal void OnMouseWheelEvent(SDL_MouseID mouseId, Vector2 delta, Vector2 position, ulong timestamp)
    {
        Mouse mouse = GetOrCreateMouse(mouseId);

        mouse.Position = position;

        _wheelEventArgs.Mouse = mouse;
        _wheelEventArgs.Delta = delta;
        _wheelEventArgs.Position = position;
        _wheelEventArgs.Timestamp = timestamp;
        _wheelHandlers.Invoke(_wheelEventArgs);
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
