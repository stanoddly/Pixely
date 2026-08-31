using System.Runtime.InteropServices;
using SDL;

namespace Pixely.Input;

public class KeyEventArgs : ConsumableInputEventArgs
{
    public Keyboard Keyboard { get; internal set; } = null!;
    public Scancode Scancode { get; internal set; }
    public VirtualKey Key { get; internal set; }
    public ulong Timestamp { get; internal set; }
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

    internal void OnKeyEvent(ViewScope viewScope, SDL_KeyboardID keyboardId, Scancode scancode, VirtualKey virtualKey, bool isPressed, ulong timestamp)
    {
        ref Keyboard? keyboard = ref CollectionsMarshal.GetValueRefOrAddDefault(_keyboards, keyboardId, out bool exists);

        if (!exists || keyboard == null)
        {
            keyboard = new Keyboard();
        }

        _keyEventArgs.Keyboard = keyboard;
        _keyEventArgs.Scancode = scancode;
        _keyEventArgs.Key = virtualKey;
        _keyEventArgs.Timestamp = timestamp;
        if (isPressed)
        {
            if (keyboard.Set(scancode))
            {
                if (keyboard.Ctrl && scancode == Scancode.Q)
                {
                    _appControl.Quit();
                }

                _keyDownHandlers.Invoke(viewScope, _keyEventArgs);
            }
        }
        else
        {
            keyboard.Unset(scancode);

            _keyUpHandlers.Invoke(viewScope, _keyEventArgs);
        }
    }
}
