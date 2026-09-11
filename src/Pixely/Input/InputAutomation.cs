using System.Numerics;
using SDL;

namespace Pixely.Input;

internal sealed class InputAutomation : IInputAutomation
{
    private const SDL_MouseID VirtualMouseId = (SDL_MouseID)0;
    private const SDL_KeyboardID VirtualKeyboardId = (SDL_KeyboardID)0;

    private readonly WindowRegistry _windowRegistry;
    private readonly MouseService _mouseService;
    private readonly KeyboardService _keyboardService;
    private readonly TextInputService _textInputService;

    internal InputAutomation(WindowRegistry windowRegistry, MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService)
    {
        _windowRegistry = windowRegistry;
        _mouseService = mouseService;
        _keyboardService = keyboardService;
        _textInputService = textInputService;
    }

    public void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseMoveTo(viewScope, VirtualMouseId, windowPosition, GetTimestamp());
    }

    public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseMoveBy(viewScope, VirtualMouseId, delta, GetTimestamp());
    }

    public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId, button, windowPosition, true, GetTimestamp());
    }

    public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId, button, windowPosition, false, GetTimestamp());
    }

    public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseMoveTo(viewScope, VirtualMouseId, windowPosition, GetTimestamp());
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId, button, windowPosition, true, GetTimestamp());
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId, button, windowPosition, false, GetTimestamp());
    }

    public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseWheelEvent(viewScope, VirtualMouseId, delta, windowPosition, GetTimestamp());
    }

    public void KeyDown(Scancode scancode, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId, scancode, GetVirtualKey(scancode), true, GetTimestamp());
    }

    public void KeyUp(Scancode scancode, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId, scancode, GetVirtualKey(scancode), false, GetTimestamp());
    }

    public void KeyPress(Scancode scancode, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        VirtualKey virtualKey = GetVirtualKey(scancode);
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId, scancode, virtualKey, true, GetTimestamp());
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId, scancode, virtualKey, false, GetTimestamp());
    }

    public void TextInput(string text, ViewScope viewScope = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateView(viewScope);
        _textInputService.OnTextInputEvent(viewScope, text, GetTimestamp());
    }

    private void ValidateView(ViewScope viewScope)
    {
        _windowRegistry.GetWindow(viewScope);
    }

    private static VirtualKey GetVirtualKey(Scancode scancode)
    {
        SDL_Keycode keycode = SDL3.SDL_GetKeyFromScancode((SDL_Scancode)scancode, SDL_Keymod.SDL_KMOD_NONE, true);
        return (VirtualKey)keycode;
    }

    private static ulong GetTimestamp()
    {
        return SDL3.SDL_GetTicksNS();
    }
}
