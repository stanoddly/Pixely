using System.Numerics;
using SDL;

namespace Pixely.Input;

internal sealed class InputAutomation
{
    // Every scope gets its own virtual mouse and keyboard, so position, relative motion, buttons and keys held in one
    // window never leak into another. SDL hands out small incrementing device ids and reserves the top ones
    // (SDL_TOUCH_MOUSEID, SDL_PEN_MOUSEID), so the synthetic ids live in the middle of the range.
    private const uint VirtualDeviceIdBase = 0x4000_0000;

    private static SDL_MouseID VirtualMouseId(ViewScope viewScope) => (SDL_MouseID)unchecked(VirtualDeviceIdBase + (uint)viewScope.Value);
    private static SDL_KeyboardID VirtualKeyboardId(ViewScope viewScope) => (SDL_KeyboardID)unchecked(VirtualDeviceIdBase + (uint)viewScope.Value);

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
        _mouseService.OnMouseMoveTo(viewScope, VirtualMouseId(viewScope), windowPosition, GetTimestamp());
    }

    public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseMoveBy(viewScope, VirtualMouseId(viewScope), delta, GetTimestamp());
    }

    public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, true, GetTimestamp());
    }

    public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, false, GetTimestamp());
    }

    public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseMoveTo(viewScope, VirtualMouseId(viewScope), windowPosition, GetTimestamp());
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, true, GetTimestamp());
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, false, GetTimestamp());
    }

    public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseWheelEvent(viewScope, VirtualMouseId(viewScope), delta, windowPosition, GetTimestamp());
    }

    public void KeyDown(Scancode scancode, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId(viewScope), scancode, GetVirtualKey(scancode), true, GetTimestamp());
    }

    public void KeyUp(Scancode scancode, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId(viewScope), scancode, GetVirtualKey(scancode), false, GetTimestamp());
    }

    public void KeyPress(Scancode scancode, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        VirtualKey virtualKey = GetVirtualKey(scancode);
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId(viewScope), scancode, virtualKey, true, GetTimestamp());
        _keyboardService.OnKeyEvent(viewScope, VirtualKeyboardId(viewScope), scancode, virtualKey, false, GetTimestamp());
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
