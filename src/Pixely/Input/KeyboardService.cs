using System.Runtime.InteropServices;
using SDL;

namespace Pixely.Input;

public class KeyEventArgs : ConsumableInputEventArgs
{
    public Keyboard Keyboard { get; internal set; } = null!;
    public Scancode Scancode { get; internal set; }
    public VirtualKey Key { get; internal set; }
    public ulong Timestamp { get; internal set; }

    /// <summary>
    /// True when the platform generated this key down because the key is being held,
    /// rather than because it was just pressed. Handlers that act on a press
    /// (toggling a mode, firing a shot) should ignore repeats; handlers that act on a
    /// held key (moving a text caret, deleting characters) should honour them.
    /// </summary>
    public bool Repeat { get; internal set; }
}

public class KeyboardService : IKeyboardService
{
    private readonly AppControl _appControl;

    // TODO: Dictionary isn't necessary, the amount of keyboards is usually truly small
    private readonly Dictionary<SDL_KeyboardID, Keyboard> _keyboards = new();

    // Cached to avoid per-event allocations. Do not hold references to event args beyond the callback.
    private readonly KeyEventArgs _keyEventArgs = new();
    private readonly ViewScopedPriorityEventHandlers<KeyEventArgs> _keyDownHandlers = new();
    private readonly ViewScopedPriorityEventHandlers<KeyEventArgs> _keyUpHandlers = new();

    public event InputEventHandler<KeyEventArgs> KeyDown
    {
        add => _keyDownHandlers.Add(default, 0, value);
        remove => _keyDownHandlers.Remove(default, value);
    }

    public event InputEventHandler<KeyEventArgs> KeyUp
    {
        add => _keyUpHandlers.Add(default, 0, value);
        remove => _keyUpHandlers.Remove(default, value);
    }

    public void SubscribeKeyDown(int priority, InputEventHandler<KeyEventArgs> handler)
    {
        _keyDownHandlers.Add(default, priority, handler);
    }

    public void SubscribeKeyUp(int priority, InputEventHandler<KeyEventArgs> handler)
    {
        _keyUpHandlers.Add(default, priority, handler);
    }

    public void SubscribeKeyDown(ViewScope viewScope, int priority, InputEventHandler<KeyEventArgs> handler)
    {
        _keyDownHandlers.Add(viewScope, priority, handler);
    }

    public void SubscribeKeyUp(ViewScope viewScope, int priority, InputEventHandler<KeyEventArgs> handler)
    {
        _keyUpHandlers.Add(viewScope, priority, handler);
    }

    internal KeyboardService(AppControl appControl)
    {
        _appControl = appControl;
    }

    internal void OnKeyEvent(ViewScope viewScope, in SDL_KeyboardEvent keyboardEvent)
    {
        Scancode scancode = (Scancode)keyboardEvent.scancode;
        ulong timestamp = keyboardEvent.timestamp;
        SDL_KeyboardID keyboardId = keyboardEvent.which;
        VirtualKey virtualKey = (VirtualKey)keyboardEvent.key;

        ref Keyboard? keyboard = ref CollectionsMarshal.GetValueRefOrAddDefault(_keyboards, keyboardId, out bool exists);

        if (!exists || keyboard == null)
        {
            keyboard = new Keyboard();
        }

        _keyEventArgs.Keyboard = keyboard;
        _keyEventArgs.Scancode = scancode;
        _keyEventArgs.Key = virtualKey;
        _keyEventArgs.Timestamp = timestamp;
        if (keyboardEvent.down)
        {
            // A held key arrives as repeated downs against an already-set scancode. Both the
            // initial press and the repeats are delivered; Repeat tells them apart. It is
            // derived from tracked key state rather than SDL_KeyboardEvent.repeat so that it
            // always agrees with Keyboard.IsPressed, even if a key up is missed.
            bool pressed = keyboard.Set(scancode);
            bool repeat = !pressed;

            if (pressed && keyboard.Ctrl && scancode == Scancode.Q)
            {
                _appControl.Quit();
            }

            _keyEventArgs.Repeat = repeat;
            _keyDownHandlers.Invoke(viewScope, _keyEventArgs);
        }
        else
        {
            keyboard.Unset(scancode);

            // The event args are cached and reused, so Repeat has to be reset here or a
            // key up would carry the flag left behind by the previous key down.
            _keyEventArgs.Repeat = false;
            _keyUpHandlers.Invoke(viewScope, _keyEventArgs);
        }
    }
}
