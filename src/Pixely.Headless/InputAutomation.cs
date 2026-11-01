using System.Numerics;
using Pixely.Input;
using SDL;

namespace Pixely.Headless;

internal sealed class InputAutomation
{
    // One virtual mouse, keyboard and gamepad. SDL hands out small incrementing device ids and reserves the top ones
    // (SDL_TOUCH_MOUSEID, SDL_PEN_MOUSEID), so the synthetic ids live in the middle of the range.
    private const uint VirtualDeviceId = 0x4000_0000;
    private const SDL_MouseID VirtualMouseId = (SDL_MouseID)VirtualDeviceId;
    private const SDL_KeyboardID VirtualKeyboardId = (SDL_KeyboardID)VirtualDeviceId;
    private const SDL_JoystickID VirtualGamepadId = (SDL_JoystickID)VirtualDeviceId;

    private readonly MouseService _mouseService;
    private readonly KeyboardService _keyboardService;
    private readonly TextInputService _textInputService;
    private readonly GamepadService _gamepadService;
    private readonly FrameClock _frameClock;
    private Gamepad? _gamepad;

    internal InputAutomation(MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService, GamepadService gamepadService,
        FrameClock frameClock)
    {
        _mouseService = mouseService;
        _keyboardService = keyboardService;
        _textInputService = textInputService;
        _gamepadService = gamepadService;
        _frameClock = frameClock;
    }

    public bool IsGamepadConnected => _gamepad != null;

    public void MouseMoveTo(Vector2 windowPosition)
    {
        EnterWindow();
        _mouseService.OnMouseMoveTo(VirtualMouseId, windowPosition, GetTimestamp());
    }

    public void MouseMoveBy(Vector2 delta)
    {
        EnterWindow();
        _mouseService.OnMouseMoveBy(VirtualMouseId, delta, GetTimestamp());
    }

    public void MouseDown(MouseButton button, Vector2 windowPosition)
    {
        EnterWindow();
        _mouseService.OnMouseButtonEvent(VirtualMouseId, button, windowPosition, true, GetTimestamp());
    }

    public void MouseUp(MouseButton button, Vector2 windowPosition)
    {
        EnterWindow();
        _mouseService.OnMouseButtonEvent(VirtualMouseId, button, windowPosition, false, GetTimestamp());
    }

    public void MouseClick(MouseButton button, Vector2 windowPosition)
    {
        EnterWindow();
        _mouseService.OnMouseMoveTo(VirtualMouseId, windowPosition, GetTimestamp());
        _mouseService.OnMouseButtonEvent(VirtualMouseId, button, windowPosition, true, GetTimestamp());
        _mouseService.OnMouseButtonEvent(VirtualMouseId, button, windowPosition, false, GetTimestamp());
    }

    public void MouseWheel(Vector2 delta, Vector2 windowPosition)
    {
        EnterWindow();
        _mouseService.OnMouseWheelEvent(VirtualMouseId, delta, windowPosition, GetTimestamp());
    }

    public void MouseLeave()
    {
        _mouseService.OnMouseWindowPresenceEvent(false, GetTimestamp());
    }

    public void KeyDown(Scancode scancode)
    {
        _keyboardService.OnKeyEvent(VirtualKeyboardId, scancode, GetVirtualKey(scancode), true, GetTimestamp());
    }

    public void KeyUp(Scancode scancode)
    {
        _keyboardService.OnKeyEvent(VirtualKeyboardId, scancode, GetVirtualKey(scancode), false, GetTimestamp());
    }

    public void KeyPress(Scancode scancode)
    {
        VirtualKey virtualKey = GetVirtualKey(scancode);
        _keyboardService.OnKeyEvent(VirtualKeyboardId, scancode, virtualKey, true, GetTimestamp());
        _keyboardService.OnKeyEvent(VirtualKeyboardId, scancode, virtualKey, false, GetTimestamp());
    }

    public void TextInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _textInputService.OnTextInputEvent(text, GetTimestamp());
    }

    public void GamepadConnect()
    {
        if (_gamepad != null)
        {
            throw new InvalidOperationException("The synthetic gamepad is already connected.");
        }

        _gamepad = _gamepadService.AddGamepad(VirtualGamepadId);
    }

    public void GamepadDisconnect()
    {
        GetGamepad();
        _gamepadService.RemoveGamepad(VirtualGamepadId, GetTimestamp());
        _gamepad = null;
    }

    public void GamepadButtonDown(GamepadButton button)
    {
        GetGamepad();
        _gamepadService.OnGamepadButtonEvent(VirtualGamepadId, button, true, GetTimestamp());
    }

    public void GamepadButtonUp(GamepadButton button)
    {
        GetGamepad();
        _gamepadService.OnGamepadButtonEvent(VirtualGamepadId, button, false, GetTimestamp());
    }

    public void GamepadButtonPress(GamepadButton button)
    {
        GetGamepad();
        _gamepadService.OnGamepadButtonEvent(VirtualGamepadId, button, true, GetTimestamp());
        _gamepadService.OnGamepadButtonEvent(VirtualGamepadId, button, false, GetTimestamp());
    }

    // Sticks range from -1 to 1 and triggers from 0 to 1. Each axis is its own event, as from SDL.
    public void GamepadAxisMotion(SDL_GamepadAxis axis, float value)
    {
        GetGamepad();
        _gamepadService.OnGamepadAxisMotion(VirtualGamepadId, axis, value, GetTimestamp());
    }

    private Gamepad GetGamepad()
    {
        return _gamepad ?? throw new InvalidOperationException("The synthetic gamepad is not connected.");
    }

    // SDL raises the window enter before the first motion of a pointer that arrives; the synthetic mouse does the same.
    private void EnterWindow()
    {
        _mouseService.OnMouseWindowPresenceEvent(true, GetTimestamp());
    }

    private static VirtualKey GetVirtualKey(Scancode scancode)
    {
        SDL_Keycode keycode = SdlBoolInterop.SDL_GetKeyFromScancode((SDL_Scancode)scancode, SDL_Keymod.SDL_KMOD_NONE, true);
        return (VirtualKey)keycode;
    }

    // Synthetic input happens at the frame that runs it, so its timestamps follow game time, which a headless run fixes per frame.
    private ulong GetTimestamp()
    {
        return _frameClock.ElapsedNanoseconds;
    }
}
