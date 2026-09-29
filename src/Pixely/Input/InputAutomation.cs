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
    // Gamepads are not per window, so there is one synthetic gamepad.
    private const SDL_JoystickID VirtualGamepadId = (SDL_JoystickID)VirtualDeviceIdBase;

    private static readonly SDL_GamepadAxis[] GamepadAxes =
    [
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER
    ];

    private readonly WindowRegistry _windowRegistry;
    private readonly MouseService _mouseService;
    private readonly KeyboardService _keyboardService;
    private readonly TextInputService _textInputService;
    private readonly GamepadService _gamepadService;
    private readonly FrameContext _frameContext;
    private Gamepad? _gamepad;

    internal InputAutomation(WindowRegistry windowRegistry, MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService,
        GamepadService gamepadService, FrameContext frameContext)
    {
        _windowRegistry = windowRegistry;
        _mouseService = mouseService;
        _keyboardService = keyboardService;
        _textInputService = textInputService;
        _gamepadService = gamepadService;
        _frameContext = frameContext;
    }

    public bool IsGamepadConnected => _gamepad != null;

    public void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        EnterWindow(viewScope);
        _mouseService.OnMouseMoveTo(viewScope, VirtualMouseId(viewScope), windowPosition, GetTimestamp());
    }

    public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        EnterWindow(viewScope);
        _mouseService.OnMouseMoveBy(viewScope, VirtualMouseId(viewScope), delta, GetTimestamp());
    }

    public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        EnterWindow(viewScope);
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, true, GetTimestamp());
    }

    public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        EnterWindow(viewScope);
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, false, GetTimestamp());
    }

    public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        EnterWindow(viewScope);
        _mouseService.OnMouseMoveTo(viewScope, VirtualMouseId(viewScope), windowPosition, GetTimestamp());
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, true, GetTimestamp());
        _mouseService.OnMouseButtonEvent(viewScope, VirtualMouseId(viewScope), button, windowPosition, false, GetTimestamp());
    }

    public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        EnterWindow(viewScope);
        _mouseService.OnMouseWheelEvent(viewScope, VirtualMouseId(viewScope), delta, windowPosition, GetTimestamp());
    }

    public void MouseLeave(ViewScope viewScope = default)
    {
        ValidateView(viewScope);
        _mouseService.OnMouseWindowPresenceEvent(viewScope, false, GetTimestamp());
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

    public void GamepadConnect()
    {
        if (_gamepad != null)
        {
            throw new InvalidOperationException("The synthetic gamepad is already connected.");
        }

        _gamepad = _gamepadService.AddGamepad(VirtualGamepadId);
    }

    // Like SDL on an unplug: every held button is released and every axis returns to 0, in that order, before the gamepad goes.
    public void GamepadDisconnect()
    {
        Gamepad gamepad = GetGamepad();
        foreach (GamepadButton button in Enum.GetValues<GamepadButton>())
        {
            if (button is not (GamepadButton.Invalid or GamepadButton.Count) && (gamepad.ButtonFlags & (1 << (int)button)) != 0)
            {
                _gamepadService.OnGamepadButtonEvent(VirtualGamepadId, button, false, GetTimestamp());
            }
        }

        foreach (SDL_GamepadAxis axis in GamepadAxes)
        {
            _gamepadService.OnGamepadAxisMotion(VirtualGamepadId, axis, 0, GetTimestamp());
        }

        _gamepadService.OnGamepadRemoved(VirtualGamepadId);
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

    private void ValidateView(ViewScope viewScope)
    {
        _windowRegistry.GetWindow(viewScope);
    }

    // SDL raises the window enter before the first motion of a pointer that arrives; the synthetic mouse does the same.
    private void EnterWindow(ViewScope viewScope)
    {
        _mouseService.OnMouseWindowPresenceEvent(viewScope, true, GetTimestamp());
    }

    private static VirtualKey GetVirtualKey(Scancode scancode)
    {
        SDL_Keycode keycode = SdlBoolInterop.SDL_GetKeyFromScancode((SDL_Scancode)scancode, SDL_Keymod.SDL_KMOD_NONE, true);
        return (VirtualKey)keycode;
    }

    // Synthetic input happens at the frame that runs it, so its timestamps follow game time, which a headless run fixes per frame.
    private ulong GetTimestamp()
    {
        return _frameContext.ElapsedNanoseconds;
    }
}
