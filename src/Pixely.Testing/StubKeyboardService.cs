using Pixely.Input;
using SDL;

namespace Pixely.Testing;

/// <summary>
/// A keyboard for a game's tests, which presses keys through Pixely's own keyboard service. Handlers run in their order, a
/// consumed event stops there, and <see cref="KeyEventArgs.Keyboard"/> and <see cref="KeyEventArgs.Repeat"/> follow the keys
/// held, as in the game. A key's <see cref="VirtualKey"/> is SDL's for its scancode, which before SDL starts is the one of a US
/// layout on every machine.
/// </summary>
public sealed class StubKeyboardService : IKeyboardService
{
    private const SDL_KeyboardID KeyboardId = (SDL_KeyboardID)1;

    private readonly KeyboardService _keyboardService = new(new AppControl());
    private bool _raising;

    public event InputEventHandler<KeyEventArgs> KeyDown
    {
        add => _keyboardService.KeyDown += value;
        remove => _keyboardService.KeyDown -= value;
    }

    public event InputEventHandler<KeyEventArgs> KeyUp
    {
        add => _keyboardService.KeyUp += value;
        remove => _keyboardService.KeyUp -= value;
    }

    public void SubscribeKeyDown(int order, InputEventHandler<KeyEventArgs> handler)
    {
        _keyboardService.SubscribeKeyDown(order, handler);
    }

    public void SubscribeKeyUp(int order, InputEventHandler<KeyEventArgs> handler)
    {
        _keyboardService.SubscribeKeyUp(order, handler);
    }

    public void SubscribeKeyDown(ViewScope viewScope, int order, InputEventHandler<KeyEventArgs> handler)
    {
        _keyboardService.SubscribeKeyDown(viewScope, order, handler);
    }

    public void SubscribeKeyUp(ViewScope viewScope, int order, InputEventHandler<KeyEventArgs> handler)
    {
        _keyboardService.SubscribeKeyUp(viewScope, order, handler);
    }

    /// <summary>
    /// Presses the key and holds it. Another call while it is held is a repeat, as when the player keeps holding it.
    /// </summary>
    public void Down(Scancode scancode, ViewScope viewScope = default)
    {
        Raise(scancode, viewScope, true);
    }

    public void Up(Scancode scancode, ViewScope viewScope = default)
    {
        Raise(scancode, viewScope, false);
    }

    /// <summary>
    /// Presses the key and releases it.
    /// </summary>
    public void Press(Scancode scancode, ViewScope viewScope = default)
    {
        Down(scancode, viewScope);
        Up(scancode, viewScope);
    }

    // The service reuses one event args object and SDL never calls it from a handler, so a call from a handler would change the
    // event the handlers after it get.
    private void Raise(Scancode scancode, ViewScope viewScope, bool isPressed)
    {
        ValidateScancode(scancode);
        if (_raising)
        {
            throw new InvalidOperationException($"{nameof(StubKeyboardService)} cannot be called from one of its own event handlers.");
        }

        _raising = true;
        try
        {
            _keyboardService.OnKeyEvent(viewScope, KeyboardId, scancode, GetVirtualKey(scancode), isPressed, 0);
        }
        finally
        {
            _raising = false;
        }
    }

    // Before SDL_Init, SDL maps scancodes with its default US layout.
    private static VirtualKey GetVirtualKey(Scancode scancode)
    {
        return (VirtualKey)SdlBoolInterop.SDL_GetKeyFromScancode((SDL_Scancode)scancode, SDL_Keymod.SDL_KMOD_NONE, true);
    }

    // The enum has gaps where SDL defines no key, such as 1 to 3.
    internal static void ValidateScancode(Scancode scancode)
    {
        if (scancode == Scancode.Count || !Enum.IsDefined(scancode))
        {
            throw new ArgumentOutOfRangeException(nameof(scancode), scancode, "Not a scancode.");
        }
    }
}
